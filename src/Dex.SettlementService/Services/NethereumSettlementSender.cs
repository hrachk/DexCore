using Dex.SettlementService.Models;
using Microsoft.Extensions.Logging;

namespace Dex.SettlementService.Services;

/// <summary>
/// Production skeleton for sending settleBatch transactions via Nethereum.
/// In a real deployment this class holds Web3, Account, Contract handlers,
/// EIP-1559 fee estimation and nonce management against the node.
/// </summary>
public sealed class NethereumSettlementSender : ISettlementTransactionSender
{
    private readonly string _settlementContractAddress;
    private readonly string _rpcUrl;
    private readonly ILogger<NethereumSettlementSender> _logger;

    public NethereumSettlementSender(
        string settlementContractAddress,
        string rpcUrl,
        ILogger<NethereumSettlementSender> logger)
    {
        _settlementContractAddress = settlementContractAddress
            ?? throw new ArgumentNullException(nameof(settlementContractAddress));
        _rpcUrl = rpcUrl ?? throw new ArgumentNullException(nameof(rpcUrl));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string> SendSettleBatchAsync(
        string batchId,
        IReadOnlyList<SettlementTrade> trades,
        string signature,
        long nonce,
        CancellationToken ct)
    {
        // Production path (requires Nethereum packages):
        //
        // var web3 = new Web3(account, _rpcUrl);
        // var contract = web3.Eth.GetContract(abi, _settlementContractAddress);
        // var fn = contract.GetFunction("settleBatch");
        // var gas = await fn.EstimateGasAsync(...);
        // var fee = await web3.Eth.GasPrice.SendRequestAsync(); // or EIP-1559
        // var receipt = await fn.SendTransactionAndWaitForReceiptAsync(
        //     account.Address, gas, value: null, nonce, batchId, trades, signature);
        // return receipt.TransactionHash;

        // Offline / test path: deterministic pseudo tx hash
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
}
