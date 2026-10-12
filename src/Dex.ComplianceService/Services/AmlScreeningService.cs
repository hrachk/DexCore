using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Dex.ComplianceService.Models;
using Microsoft.Extensions.Logging;

namespace Dex.ComplianceService.Services;

/// <summary>
/// Synchronous AML / sanctions screening.
/// Production: replace the local database with Chainalysis / Elliptic / TRM API clients.
/// Keeps an in-memory cache + explicit blocklist for hot-path decisions.
/// </summary>
public sealed class AmlScreeningService
{
    private readonly ConcurrentDictionary<string, AmlScreeningResult> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, AmlRiskLevel> _blocklist = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<AmlScreeningService> _logger;
    private readonly TimeSpan _cacheTtl;

    public AmlScreeningService(ILogger<AmlScreeningService> logger, TimeSpan? cacheTtl = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cacheTtl = cacheTtl ?? TimeSpan.FromMinutes(15);
    }

    public void AddToBlocklist(string address, AmlRiskLevel level = AmlRiskLevel.Blocked)
    {
        if (string.IsNullOrWhiteSpace(address)) throw new ArgumentException("Address required.");
        _blocklist[Normalize(address)] = level;
        _cache.TryRemove(Normalize(address), out _);
        _logger.LogWarning("Address {Address} added to blocklist at level {Level}", address, level);
    }

    public void RemoveFromBlocklist(string address)
    {
        _blocklist.TryRemove(Normalize(address), out _);
        _cache.TryRemove(Normalize(address), out _);
    }

    public AmlScreeningResult Screen(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new ArgumentException("Address required.");

        var key = Normalize(address);

        if (_blocklist.TryGetValue(key, out var blockedLevel))
        {
            return new AmlScreeningResult
            {
                Address = key,
                RiskLevel = blockedLevel,
                RiskScore = blockedLevel == AmlRiskLevel.Blocked ? 100 : 80,
                Flags = new[] { "local_blocklist" },
                ProviderReference = "local",
                ScreenedAtUtc = DateTime.UtcNow
            };
        }

        if (_cache.TryGetValue(key, out var cached) &&
            DateTime.UtcNow - cached.ScreenedAtUtc < _cacheTtl)
        {
            return cached;
        }

        // Simulated provider response (deterministic from address hash for tests)
        var result = SimulateProviderScreen(key);
        _cache[key] = result;
        _logger.LogDebug("Screened {Address} → {Level} score={Score}", key, result.RiskLevel, result.RiskScore);
        return result;
    }

    public bool IsBlocked(string address) => Screen(address).IsBlocked;

    private static AmlScreeningResult SimulateProviderScreen(string address)
    {
        // Deterministic pseudo-risk from address bytes for offline tests
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(address.ToLowerInvariant()));
        var score = hash[0] % 100;

        AmlRiskLevel level;
        var flags = new List<string>();

        if (score >= 95)
        {
            level = AmlRiskLevel.Blocked;
            flags.Add("sanctions_match");
        }
        else if (score >= 80)
        {
            level = AmlRiskLevel.High;
            flags.Add("mixer_exposure");
        }
        else if (score >= 50)
        {
            level = AmlRiskLevel.Elevated;
            flags.Add("elevated_activity");
        }
        else
        {
            level = AmlRiskLevel.Clear;
        }

        return new AmlScreeningResult
        {
            Address = address,
            RiskLevel = level,
            RiskScore = score,
            Flags = flags,
            ProviderReference = "simulated-" + Convert.ToHexString(hash.AsSpan(0, 4)).ToLowerInvariant(),
            ScreenedAtUtc = DateTime.UtcNow
        };
    }

    private static string Normalize(string address) => address.Trim().ToLowerInvariant();
}
