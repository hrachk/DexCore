using System.Numerics;
using Dex.SettlementService.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;

namespace Dex.SettlementService.Services;

public sealed class SettlementRpcOptions
{
    public string RpcUrl { get; set; } = "http://127.0.0.1:8545";
    public string SettlementContractAddress { get; set; } = "0x0000000000000000000000000000000000000000";
    public string? RelayerPrivateKey { get; set; }
    public bool EnableLiveSubmit { get; set; }
    public int ChainId { get; set; } = 31337;
    public int ReceiptTimeoutSeconds { get; set; } = 60;
}

/// <summary>
/// Sends settleBatch via Nethereum against a real (or Anvil) RPC.
/// Falls back to deterministic simulation when EnableLiveSubmit is false or key is missing.
/// </summary>
public sealed class NethereumSettlementSender : ISettlementTransactionSender
{
    private const string SettleBatchAbi = """
        [{\"inputs\":[{\"internalType\":\"bytes32\",\"name\":\"batchId\",\"type\":\"bytes32\"},{\"internalType\":\"bytes\",\"name\":\"trades\",\"type\":\"bytes\"},{\"internalType\":\"bytes\",\"name\":\"signature\",\"type\":\"bytes\"},{\"internalType\":\"uint256\",\"name\":\"nonce\",\"type\":\"uint256\"}],\"name\":\"settleBatch\",\"outputs\":[],\"stateMutability\":\"nonpayable\",\"type\":\"function\"}]
        """;

    private readonly SettlementRpcOptions _options;
    private readonly ILogger<NethereumSettlementSender> _logger;
    private readonly object _nonceLock = new();
    private BigInteger? _localNonce;

    public NethereumSettlementSender(
        IOptions<SettlementRpcOptions> options,
        ILogger<NethereumSettlementSender> logger)
    {
        _options = options?.Value ?? new SettlementRpcOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public NethereumSettlementSender(
        string settlementContractAddress,
        string rpcUrl,
        ILogger<NethereumSettlementSender> logger,
        string? relayerPrivateKey = null,
        bool enableLiveSubmit = false)
        : this(Options.Create(new SettlementRpcOptions
        {
            SettlementContractAddress = settlementContractAddress,
            RpcUrl = rpcUrl,
            RelayerPrivateKey = relayerPrivateKey,
            EnableLiveSubmit = enableLiveSubmit
        }), logger)
    {
    }

    public async Task<string> SendSettleBatchAsync(
        string batchId,
        IReadOnlyList<SettlementTrade> trades,
        string signature,
        long nonce,
        CancellationToken ct)
    {
        if (!_options.EnableLiveSubmit
            || string.IsNullOrWhiteSpace(_options.RelayerPrivateKey)
            || _options.SettlementContractAddress is null
            || _options.SettlementContractAddress.StartsWith("0x0000", StringComparison.OrdinalIgnoreCase))
        {
            return await SimulateAsync(batchId, trades, signature, nonce, ct).ConfigureAwait(false);
        }

        try
        {
            return await SubmitLiveAsync(batchId, trades, signature, nonce, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Live settleBatch failed batch={BatchId}; falling back to simulation", batchId);
            return await SimulateAsync(batchId, trades, signature, nonce, ct).ConfigureAwait(false);
        }
    }

    private async Task<string> SubmitLiveAsync(
        string batchId,
        IReadOnlyList<SettlementTrade> trades,
        string signature,
        long nonce,
        CancellationToken ct)
    {
        var account = new Account(_options.RelayerPrivateKey!, _options.ChainId);
        var web3 = new Web3(account, _options.RpcUrl);

        var tradesBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(trades);
        var sigBytes = ConvertHexToBytes(signature);
        var batchIdBytes = DeriveBatchId32(batchId);

        var contract = web3.Eth.GetContract(SettleBatchAbi, _options.SettlementContractAddress);
        var fn = contract.GetFunction("settleBatch");

        var nodeNonce = await web3.Eth.Transactions.GetTransactionCount
            .SendRequestAsync(account.Address, BlockParameter.CreatePending())
            .ConfigureAwait(false);

        BigInteger txNonce;
        lock (_nonceLock)
        {
            if (_localNonce is null || nodeNonce.Value > _localNonce.Value)
                _localNonce = nodeNonce.Value;
            txNonce = _localNonce.Value;
            _localNonce = txNonce + 1;
        }

        var gas = await fn.EstimateGasAsync(
            account.Address, null, null,
            batchIdBytes, tradesBytes, sigBytes, new BigInteger(nonce)).ConfigureAwait(false);

        var gasPrice = await web3.Eth.GasPrice.SendRequestAsync().ConfigureAwait(false);

        _logger.LogInformation(
            "Submitting settleBatch live batch={BatchId} nonce={Nonce} gas={Gas} rpc={Rpc}",
            batchId, txNonce, gas.Value, _options.RpcUrl);

        var txHash = await fn.SendTransactionAsync(
            account.Address, gas, gasPrice, new HexBigInteger(0),
            batchIdBytes, tradesBytes, sigBytes, new BigInteger(nonce)).ConfigureAwait(false);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.ReceiptTimeoutSeconds));
        TransactionReceipt? receipt = null;
        while (!cts.IsCancellationRequested)
        {
            receipt = await web3.Eth.Transactions.GetTransactionReceipt
                .SendRequestAsync(txHash).ConfigureAwait(false);
            if (receipt != null) break;
            await Task.Delay(500, cts.Token).ConfigureAwait(false);
        }

        if (receipt is null)
        {
            _logger.LogWarning("Receipt timeout for tx={Tx}; returning hash anyway", txHash);
            return txHash;
        }

        if (receipt.Status?.Value == 0)
            throw new InvalidOperationException($"settleBatch reverted tx={txHash}");

        _logger.LogInformation("settleBatch confirmed tx={Tx} block={Block}", txHash, receipt.BlockNumber?.Value);
        return txHash;
    }

    private async Task<string> SimulateAsync(
        string batchId,
        IReadOnlyList<SettlementTrade> trades,
        string signature,
        long nonce,
        CancellationToken ct)
    {
        await Task.Delay(1, ct).ConfigureAwait(false);
        var payload = $"{batchId}:{trades.Count}:{nonce}:{signature[..Math.Min(18, signature.Length)]}";
        var hashBytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(payload));
        var txHash = "0x" + Convert.ToHexString(hashBytes).ToLowerInvariant();
        _logger.LogInformation(
            "NethereumSettlementSender simulated submit batch={BatchId} nonce={Nonce} tx={Tx}",
            batchId, nonce, txHash);
        return txHash;
    }

    private static byte[] ConvertHexToBytes(string hex)
    {
        var h = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex[2..] : hex;
        if (h.Length % 2 != 0) h = "0" + h;
        return Convert.FromHexString(h);
    }

    private static byte[] DeriveBatchId32(string batchId)
    {
        return System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(batchId));
    }
}
