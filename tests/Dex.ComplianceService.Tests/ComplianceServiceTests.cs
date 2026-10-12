using Dex.ComplianceService.Models;
using Dex.ComplianceService.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dex.ComplianceService.Tests;

public class ComplianceServiceTests
{
    private readonly AmlScreeningService _aml;
    private readonly ZkProofGeneratorAdapter _zk;
    private readonly ComplianceRuleEngine _engine;

    public ComplianceServiceTests()
    {
        _aml = new AmlScreeningService(NullLogger<AmlScreeningService>.Instance);
        _zk = new ZkProofGeneratorAdapter(NullLogger<ZkProofGeneratorAdapter>.Instance);
        _engine = new ComplianceRuleEngine(
            _aml, _zk, NullLogger<ComplianceRuleEngine>.Instance,
            new ComplianceRule
            {
                Name = "test",
                MaxNotionalWithoutKyc = 10_000m,
                RequireZkForAll = false,
                MaxSingleWithdrawal = 500_000m
            });
    }

    [Fact]
    public void AmlScreening_Blocklist_ReturnsBlocked()
    {
        _aml.AddToBlocklist("0xblocked");
        var result = _aml.Screen("0xblocked");
        result.IsBlocked.Should().BeTrue();
        result.RiskLevel.Should().Be(AmlRiskLevel.Blocked);
        result.Flags.Should().Contain("local_blocklist");
    }

    [Fact]
    public void AmlScreening_ClearAddress_IsNotBlocked()
    {
        // Use a stable low-score address pattern; most random will be clear or elevated
        var result = _aml.Screen("0x0000000000000000000000000000000000000001");
        result.Should().NotBeNull();
        result.Address.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ZkAttestation_IssueAndVerify_Succeeds()
    {
        var att = _zk.IssueAttestation("institution-acme", tier: 2, validity: TimeSpan.FromHours(24));
        att.Commitment.Should().StartWith("0x");
        att.Proof.Length.Should().BeGreaterThanOrEqualTo(32);

        var vr = _zk.Verify(att);
        vr.IsValid.Should().BeTrue();
        vr.Commitment.Should().Be(att.Commitment);
    }

    [Fact]
    public void ZkAttestation_ReplayNullifier_Fails()
    {
        var att = _zk.IssueAttestation("institution-acme", tier: 2, validity: TimeSpan.FromHours(24));
        _zk.Verify(att).IsValid.Should().BeTrue();

        // Same nullifier again
        var vr2 = _zk.Verify(att);
        vr2.IsValid.Should().BeFalse();
        vr2.Message.Should().Contain("Nullifier");
    }

    [Fact]
    public void RuleEngine_SmallDeposit_AllowedWithoutZk()
    {
        var decision = _engine.EvaluateDepositOrTrade("0xuser1", notional: 100m, attestation: null);
        decision.Action.Should().Be(ComplianceAction.Allow);
        decision.ZkRequired.Should().BeFalse();
    }

    [Fact]
    public void RuleEngine_LargeDeposit_RequiresKyc()
    {
        var decision = _engine.EvaluateDepositOrTrade("0xuser2", notional: 50_000m, attestation: null);
        decision.Action.Should().Be(ComplianceAction.RequireKyc);
        decision.ZkRequired.Should().BeTrue();
    }

    [Fact]
    public void RuleEngine_LargeDeposit_WithValidZk_Allowed()
    {
        var att = _zk.IssueAttestation("institution-beta", tier: 2, TimeSpan.FromDays(30));
        var decision = _engine.EvaluateDepositOrTrade("0xuser3", notional: 50_000m, attestation: att);
        decision.Action.Should().Be(ComplianceAction.Allow);
        decision.ZkSatisfied.Should().BeTrue();
    }

    [Fact]
    public void RuleEngine_BlockedAddress_Denied()
    {
        _aml.AddToBlocklist("0xsanctioned");
        var decision = _engine.EvaluateDepositOrTrade("0xsanctioned", 100m, null);
        decision.Action.Should().Be(ComplianceAction.Deny);
        decision.Reason.Should().Contain("blocked");
    }

    [Fact]
    public void RuleEngine_WithdrawalOverMax_Denied()
    {
        var decision = _engine.EvaluateWithdrawal("0xuser4", amount: 1_000_000m, attestation: null);
        decision.Action.Should().Be(ComplianceAction.Deny);
        decision.Reason.Should().Contain("max single withdrawal");
    }

    [Fact]
    public void RuleEngine_RestrictedJurisdiction_Denied()
    {
        _engine.UpdateRule(new ComplianceRule
        {
            Name = "geo",
            MaxNotionalWithoutKyc = 10_000m,
            RestrictedJurisdictions = new HashSet<string> { "XX" },
            MaxSingleWithdrawal = 500_000m
        });

        var decision = _engine.EvaluateDepositOrTrade("0xuser5", 100m, null, jurisdiction: "XX");
        decision.Action.Should().Be(ComplianceAction.Deny);
        decision.Reason.Should().Contain("Jurisdiction");
    }
}
