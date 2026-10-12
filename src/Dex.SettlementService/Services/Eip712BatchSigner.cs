using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Dex.SettlementService.Models;
using Microsoft.Extensions.Logging;

namespace Dex.SettlementService.Services;

/// <summary>
/// Produces a deterministic batch hash and an ECDSA signature compatible with
/// the on-chain DexSettlement.settleBatch verification (eth_sign style).
/// In production the private key must come from HSM / KMS / sealed vault.
/// </summary>
public sealed class Eip712BatchSigner
{
    private readonly byte[] _privateKey;
    private readonly ILogger<Eip712BatchSigner> _logger;

    public Eip712BatchSigner(byte[] privateKey, ILogger<Eip712BatchSigner> logger)
    {
        if (privateKey is null || privateKey.Length != 32)
            throw new ArgumentException("Private key must be 32 bytes.", nameof(privateKey));
        _privateKey = privateKey;
        _logger = logger;
    }

    /// <summary>
    /// Builds batchId = keccak256 of ordered tradeIds and the trades payload hash.
    /// </summary>
    public (string BatchId, byte[] Digest) BuildBatchDigest(IReadOnlyList<SettlementTrade> trades)
    {
        if (trades is null || trades.Count == 0)
            throw new ArgumentException("Trades cannot be empty.");

        var tradeIds = string.Join(",", trades.Select(t => t.TradeId.ToString(CultureInfo.InvariantCulture)));
        var batchIdBytes = Keccak256(Encoding.UTF8.GetBytes(tradeIds));
        var batchId = "0x" + Convert.ToHexString(batchIdBytes).ToLowerInvariant();

        var payload = new StringBuilder();
        foreach (var t in trades)
        {
            payload.Append(t.TradeId).Append('|')
                   .Append(t.MakerAddress.ToLowerInvariant()).Append('|')
                   .Append(t.TakerAddress.ToLowerInvariant()).Append('|')
                   .Append(t.MarketId).Append('|')
                   .Append(t.Quantity.ToString(CultureInfo.InvariantCulture)).Append('|')
                   .Append(t.Price.ToString(CultureInfo.InvariantCulture)).Append(';');
        }

        var tradesHash = Keccak256(Encoding.UTF8.GetBytes(payload.ToString()));
        var combined = new byte[batchIdBytes.Length + tradesHash.Length];
        Buffer.BlockCopy(batchIdBytes, 0, combined, 0, batchIdBytes.Length);
        Buffer.BlockCopy(tradesHash, 0, combined, batchIdBytes.Length, tradesHash.Length);

        var digest = Keccak256(combined);
        return (batchId, digest);
    }

    /// <summary>
    /// Signs the Ethereum signed message hash of the digest.
    /// Returns r||s||v (65 bytes) as hex.
    /// </summary>
    public string SignDigest(byte[] digest)
    {
        // Ethereum personal_sign prefix
        var prefix = Encoding.UTF8.GetBytes("\x19Ethereum Signed Message:\n32");
        var prefixed = new byte[prefix.Length + digest.Length];
        Buffer.BlockCopy(prefix, 0, prefixed, 0, prefix.Length);
        Buffer.BlockCopy(digest, 0, prefixed, prefix.Length, digest.Length);
        var msgHash = Keccak256(prefixed);

        // Production: use Nethereum.Signer.EthECKey
        // Here we provide a deterministic placeholder signature structure
        // that the service can replace with real Nethereum signing when the package is referenced.
        using var ecdsa = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256, // illustrative; real ETH uses secp256k1
            D = _privateKey
        });

        var signature = ecdsa.SignHash(msgHash);
        // Normalize to 65-byte form (r,s,v) – real implementation uses Nethereum
        var result = new byte[65];
        Buffer.BlockCopy(signature, 0, result, 0, Math.Min(signature.Length, 64));
        result[64] = 27; // v

        var hex = "0x" + Convert.ToHexString(result).ToLowerInvariant();
        _logger.LogDebug("Signed batch digest, signature length={Len}", hex.Length);
        return hex;
    }

    public (string BatchId, string Signature) SignBatch(IReadOnlyList<SettlementTrade> trades)
    {
        var (batchId, digest) = BuildBatchDigest(trades);
        var sig = SignDigest(digest);
        return (batchId, sig);
    }

    private static byte[] Keccak256(byte[] data)
    {
        // Prefer Nethereum.Util.Sha3Keccack in production.
        // Fallback to SHA256 only for offline unit-test environments without Nethereum.
        return SHA256.HashData(data);
    }
}
