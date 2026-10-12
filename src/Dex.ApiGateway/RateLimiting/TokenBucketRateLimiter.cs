using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Dex.ApiGateway.RateLimiting;

/// <summary>
/// High-performance Token Bucket rate limiter.
/// Separate buckets per key (API key or IP). Thread-safe, lock-free on hot path where possible.
/// </summary>
public sealed class TokenBucketRateLimiter
{
    private readonly ConcurrentDictionary<string, Bucket> _buckets = new();
    private readonly double _capacity;
    private readonly double _refillPerSecond;
    private readonly TimeProvider _time;

    public TokenBucketRateLimiter(double capacity, double refillPerSecond, TimeProvider? time = null)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (refillPerSecond <= 0) throw new ArgumentOutOfRangeException(nameof(refillPerSecond));
        _capacity = capacity;
        _refillPerSecond = refillPerSecond;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Attempts to consume one token. Returns true if allowed.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryAcquire(string key)
    {
        var bucket = _buckets.GetOrAdd(key, _ => new Bucket(_capacity, _time.GetTimestamp()));
        return bucket.TryConsume(_capacity, _refillPerSecond, _time);
    }

    public void Reset(string key) => _buckets.TryRemove(key, out _);

    private sealed class Bucket
    {
        private double _tokens;
        private long _lastRefillTimestamp;
        private readonly object _gate = new();

        public Bucket(double initialTokens, long nowTimestamp)
        {
            _tokens = initialTokens;
            _lastRefillTimestamp = nowTimestamp;
        }

        public bool TryConsume(double capacity, double refillPerSecond, TimeProvider time)
        {
            lock (_gate)
            {
                var now = time.GetTimestamp();
                var elapsedSeconds = (now - _lastRefillTimestamp) / (double)Stopwatch.Frequency;
                if (elapsedSeconds > 0)
                {
                    _tokens = Math.Min(capacity, _tokens + elapsedSeconds * refillPerSecond);
                    _lastRefillTimestamp = now;
                }

                if (_tokens < 1.0)
                    return false;

                _tokens -= 1.0;
                return true;
            }
        }
    }
}

/// <summary>
/// Composite limiter: separate limits for IP and API key.
/// </summary>
public sealed class CompositeRateLimiter
{
    private readonly TokenBucketRateLimiter _ipLimiter;
    private readonly TokenBucketRateLimiter _apiKeyLimiter;

    public CompositeRateLimiter(
        double ipCapacity,
        double ipRefillPerSecond,
        double apiKeyCapacity,
        double apiKeyRefillPerSecond)
    {
        _ipLimiter = new TokenBucketRateLimiter(ipCapacity, ipRefillPerSecond);
        _apiKeyLimiter = new TokenBucketRateLimiter(apiKeyCapacity, apiKeyRefillPerSecond);
    }

    public bool TryAcquire(string? ip, string? apiKey)
    {
        if (!string.IsNullOrEmpty(ip) && !_ipLimiter.TryAcquire(ip))
            return false;

        if (!string.IsNullOrEmpty(apiKey) && !_apiKeyLimiter.TryAcquire(apiKey))
            return false;

        return true;
    }
}
