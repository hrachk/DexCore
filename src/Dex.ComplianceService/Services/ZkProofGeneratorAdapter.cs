using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Dex.ComplianceService.Models;
using Microsoft.Extensions.Logging;

namespace Dex.ComplianceService.Services;

/// <summary>
/// Adapter for generating and verifying ZK attestation proofs.
/// Production: integrate with a real prover (rapidsnark / gnark / Circom witness).
/// This implementation produces deterministic commitments and verifies structure + expiry.
/// </summary>
public sealed class ZkProofGeneratorAdapter
{
    private readonly ConcurrentDictionary<string, ZkAttestation> _issued = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _usedNullifiers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<ZkProofGeneratorAdapter> _logger;

    public ZkProofGeneratorAdapter(ILogger<ZkProofGeneratorAdapter> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Issues an institutional attestation for a subject identifier (off-chain KYC reference).
    /// </summary>
    public ZkAttestation IssueAttestation(string subjectId, int tier, TimeSpan validity)
    {
        if (string.IsNullOrWhiteSpace(subjectId)) throw new ArgumentException("subjectId required.");
        if (tier < 1) throw new ArgumentOutOfRangeException(nameof(tier));

        var commitment = ComputeCommitment(subjectId, tier);
        var nullifier = ComputeNullifier(subjectId, DateTime.UtcNow.Ticks);
        var proof = BuildProofBlob(commitment, nullifier, tier);

        var attestation = new ZkAttestation
        {
            Commitment = commitment,
            Nullifier = nullifier,
            Proof = proof,
            PublicInputs = new[] { commitment },
            ExpiresAtUtc = DateTime.UtcNow.Add(validity),
            Tier = tier
        };

        _issued[commitment] = attestation;
        _logger.LogInformation("Issued ZK attestation commitment={Commitment} tier={Tier}", commitment, tier);
        return attestation;
    }

    public ZkVerificationResult Verify(ZkAttestation attestation)
    {
        if (attestation is null)
            return new ZkVerificationResult { IsValid = false, Message = "Null attestation" };

        if (!_issued.TryGetValue(attestation.Commitment, out var issued))
            return new ZkVerificationResult { IsValid = false, Message = "Unknown commitment", Commitment = attestation.Commitment };

        if (DateTime.UtcNow > issued.ExpiresAtUtc)
            return new ZkVerificationResult { IsValid = false, Message = "Attestation expired", Commitment = attestation.Commitment };

        if (_usedNullifiers.ContainsKey(attestation.Nullifier))
            return new ZkVerificationResult { IsValid = false, Message = "Nullifier already used", Commitment = attestation.Commitment };

        if (attestation.Proof is null || attestation.Proof.Length < 32)
            return new ZkVerificationResult { IsValid = false, Message = "Invalid proof length", Commitment = attestation.Commitment };

        // Structural check: proof must commit to the same commitment
        var expectedPrefix = SHA256.HashData(Encoding.UTF8.GetBytes(attestation.Commitment));
        if (!attestation.Proof.AsSpan(0, 16).SequenceEqual(expectedPrefix.AsSpan(0, 16)))
            return new ZkVerificationResult { IsValid = false, Message = "Proof commitment mismatch", Commitment = attestation.Commitment };

        _usedNullifiers[attestation.Nullifier] = 1;
        _logger.LogDebug("ZK proof verified for {Commitment}", attestation.Commitment);

        return new ZkVerificationResult
        {
            IsValid = true,
            Message = "OK",
            Commitment = attestation.Commitment
        };
    }

    public bool IsAttestationActive(string commitment)
        => _issued.TryGetValue(commitment, out var a) && DateTime.UtcNow <= a.ExpiresAtUtc;

    private static string ComputeCommitment(string subjectId, int tier)
    {
        var data = Encoding.UTF8.GetBytes($"commit|{subjectId}|{tier}");
        return "0x" + Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    }

    private static string ComputeNullifier(string subjectId, long ticks)
    {
        var data = Encoding.UTF8.GetBytes($"null|{subjectId}|{ticks}");
        return "0x" + Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    }

    private static byte[] BuildProofBlob(string commitment, string nullifier, int tier)
    {
        var prefix = SHA256.HashData(Encoding.UTF8.GetBytes(commitment));
        var rest = SHA256.HashData(Encoding.UTF8.GetBytes($"{nullifier}|{tier}"));
        var proof = new byte[64];
        Buffer.BlockCopy(prefix, 0, proof, 0, 32);
        Buffer.BlockCopy(rest, 0, proof, 32, 32);
        return proof;
    }
}
