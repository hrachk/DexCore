using Dex.ComplianceService.Models;
using Microsoft.Extensions.Logging;

namespace Dex.ComplianceService.Services;

/// <summary>
/// Configurable rule engine that combines AML screening, ZK policy and notional limits
/// into a single ComplianceDecision for deposits, trades and withdrawals.
/// </summary>
public sealed class ComplianceRuleEngine
{
    private readonly AmlScreeningService _aml;
    private readonly ZkProofGeneratorAdapter _zk;
    private readonly ILogger<ComplianceRuleEngine> _logger;
    private ComplianceRule _rule;

    public ComplianceRuleEngine(
        AmlScreeningService aml,
        ZkProofGeneratorAdapter zk,
        ILogger<ComplianceRuleEngine> logger,
        ComplianceRule? initialRule = null)
    {
        _aml = aml ?? throw new ArgumentNullException(nameof(aml));
        _zk = zk ?? throw new ArgumentNullException(nameof(zk));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _rule = initialRule ?? new ComplianceRule
        {
            Name = "default",
            MaxNotionalWithoutKyc = 10_000m,
            RequireZkForAll = false,
            MaxSingleWithdrawal = 1_000_000m,
            RestrictedJurisdictions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        };
    }

    public ComplianceRule CurrentRule => _rule;

    public void UpdateRule(ComplianceRule rule)
    {
        _rule = rule ?? throw new ArgumentNullException(nameof(rule));
        _logger.LogInformation("Compliance rule updated: {Name}", rule.Name);
    }

    public ComplianceDecision EvaluateDepositOrTrade(
        string address,
        decimal notional,
        ZkAttestation? attestation,
        string? jurisdiction = null)
    {
        var aml = _aml.Screen(address);
        if (aml.IsBlocked)
        {
            return Deny("Address blocked by AML/sanctions", aml.RiskLevel, zkRequired: false, zkSatisfied: false);
        }

        if (!string.IsNullOrEmpty(jurisdiction) &&
            _rule.RestrictedJurisdictions.Contains(jurisdiction))
        {
            return Deny($"Jurisdiction {jurisdiction} restricted", aml.RiskLevel, false, false);
        }

        var zkRequired = _rule.RequireZkForAll || notional > _rule.MaxNotionalWithoutKyc;
        var zkSatisfied = false;

        if (zkRequired)
        {
            if (attestation is null)
                return new ComplianceDecision
                {
                    Action = ComplianceAction.RequireKyc,
                    Reason = "ZK/KYC attestation required for this notional",
                    AmlLevel = aml.RiskLevel,
                    ZkRequired = true,
                    ZkSatisfied = false
                };

            var vr = _zk.Verify(attestation);
            zkSatisfied = vr.IsValid;
            if (!zkSatisfied)
                return Deny($"ZK verification failed: {vr.Message}", aml.RiskLevel, true, false);
        }

        if (aml.RiskLevel == AmlRiskLevel.High && notional > _rule.MaxNotionalWithoutKyc / 2)
        {
            return Deny("High AML risk with elevated notional", aml.RiskLevel, zkRequired, zkSatisfied);
        }

        return new ComplianceDecision
        {
            Action = ComplianceAction.Allow,
            Reason = "OK",
            AmlLevel = aml.RiskLevel,
            ZkRequired = zkRequired,
            ZkSatisfied = zkSatisfied
        };
    }

    public ComplianceDecision EvaluateWithdrawal(
        string address,
        decimal amount,
        ZkAttestation? attestation)
    {
        if (amount > _rule.MaxSingleWithdrawal)
            return Deny("Exceeds max single withdrawal", AmlRiskLevel.Clear, false, false);

        return EvaluateDepositOrTrade(address, amount, attestation);
    }

    private static ComplianceDecision Deny(string reason, AmlRiskLevel aml, bool zkRequired, bool zkSatisfied)
        => new()
        {
            Action = ComplianceAction.Deny,
            Reason = reason,
            AmlLevel = aml,
            ZkRequired = zkRequired,
            ZkSatisfied = zkSatisfied
        };
}
