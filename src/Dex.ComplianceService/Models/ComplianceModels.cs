namespace Dex.ComplianceService.Models;

public enum AmlRiskLevel
{
    Clear = 0,
    Elevated = 1,
    High = 2,
    Blocked = 3
}

public sealed class AmlScreeningResult
{
    public string Address { get; init; } = string.Empty;
    public AmlRiskLevel RiskLevel { get; init; }
    public double RiskScore { get; init; }          // 0..100
    public IReadOnlyList<string> Flags { get; init; } = Array.Empty<string>();
    public string ProviderReference { get; init; } = string.Empty;
    public DateTime ScreenedAtUtc { get; init; }
    public bool IsBlocked => RiskLevel == AmlRiskLevel.Blocked;
}

public sealed class ZkAttestation
{
    public string Commitment { get; init; } = string.Empty;
    public string Nullifier { get; init; } = string.Empty;
    public byte[] Proof { get; init; } = Array.Empty<byte>();
    public IReadOnlyList<string> PublicInputs { get; init; } = Array.Empty<string>();
    public DateTime ExpiresAtUtc { get; init; }
    public int Tier { get; init; }
}

public sealed class ZkVerificationResult
{
    public bool IsValid { get; init; }
    public string Message { get; init; } = string.Empty;
    public string Commitment { get; init; } = string.Empty;
}

public enum ComplianceAction
{
    Allow = 0,
    RequireKyc = 1,
    Deny = 2
}

public sealed class ComplianceDecision
{
    public ComplianceAction Action { get; init; }
    public string Reason { get; init; } = string.Empty;
    public AmlRiskLevel AmlLevel { get; init; }
    public bool ZkRequired { get; init; }
    public bool ZkSatisfied { get; init; }
}

public sealed class ComplianceRule
{
    public string Name { get; init; } = string.Empty;
    public decimal MaxNotionalWithoutKyc { get; init; }
    public bool RequireZkForAll { get; init; }
    public IReadOnlySet<string> RestrictedJurisdictions { get; init; } = new HashSet<string>();
    public decimal MaxSingleWithdrawal { get; init; }
}
