using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Dex.ApiGateway.Auth;

/// <summary>
/// HMAC-SHA256 authentication for private API / trading sessions.
/// Signature = HMAC-SHA256(secret, timestamp + method + path + body)
/// </summary>
public sealed class HmacAuthService
{
    private readonly ConcurrentDictionarySlim _apiKeys; // apiKey → secret
    private readonly ILogger<HmacAuthService> _logger;
    private readonly long _maxSkewTicks;

    public HmacAuthService(ILogger<HmacAuthService> logger, TimeSpan maxClockSkew)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _maxSkewTicks = maxClockSkew.Ticks;
        _apiKeys = new ConcurrentDictionarySlim();
    }

    public void RegisterApiKey(string apiKey, string secret)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(secret))
            throw new ArgumentException("API key and secret are required.");
        _apiKeys.Set(apiKey, secret);
    }

    public bool TryAuthenticate(
        string apiKey,
        string timestamp,
        string method,
        string path,
        string body,
        string providedSignature)
    {
        if (!_apiKeys.TryGet(apiKey, out var secret))
        {
            _logger.LogWarning("Unknown API key {ApiKey}", apiKey);
            return false;
        }

        if (!long.TryParse(timestamp, out var ts))
            return false;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (Math.Abs(now - ts) * TimeSpan.TicksPerMillisecond > _maxSkewTicks)
        {
            _logger.LogWarning("Timestamp skew too large for key {ApiKey}", apiKey);
            return false;
        }

        var payload = timestamp + method.ToUpperInvariant() + path + (body ?? string.Empty);
        var expected = ComputeHmac(secret, payload);

        var ok = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(providedSignature));

        if (!ok)
            _logger.LogWarning("Invalid HMAC signature for key {ApiKey}", apiKey);

        return ok;
    }

    private static string ComputeHmac(string secret, string payload)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var hash = HMACSHA256.HashData(keyBytes, payloadBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Minimal concurrent dictionary without external deps.
    /// </summary>
    private sealed class ConcurrentDictionarySlim
    {
        private readonly Dictionary<string, string> _map = new();
        private readonly object _gate = new();

        public void Set(string key, string value)
        {
            lock (_gate) _map[key] = value;
        }

        public bool TryGet(string key, out string value)
        {
            lock (_gate) return _map.TryGetValue(key, out value!);
        }
    }
}
