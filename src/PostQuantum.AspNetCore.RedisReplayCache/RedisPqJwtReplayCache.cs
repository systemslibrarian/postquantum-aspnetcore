using PostQuantum.Jwt;
using StackExchange.Redis;

namespace PostQuantum.AspNetCore.RedisReplayCache;

/// <summary>
/// A Redis-backed <see cref="IPqJwtReplayCache"/> for horizontally-scaled
/// deployments. <see cref="TryRegister"/> issues a Redis <c>SET key value
/// NX PX ttl</c>: if the key is new, the write succeeds and the method
/// returns <see langword="true"/>; if the <c>jti</c> has been seen, the
/// write is rejected and the method returns <see langword="false"/>.
/// Single-use <c>jti</c> enforcement coordinates across every instance
/// sharing the Redis endpoint.
/// </summary>
/// <remarks>
/// <para>
/// The TTL is set to the remaining token lifetime so entries clean
/// themselves up. Tokens whose expiration is in the past — or whose
/// <c>exp</c> claim is missing — are still registered, but with a
/// fallback TTL (<see cref="FallbackTtl"/>) so the cache doesn't grow
/// unbounded.
/// </para>
/// <para>
/// The interface (<c>IPqJwtReplayCache.TryRegister</c>) is synchronous;
/// StackExchange.Redis is fundamentally async. This implementation
/// bridges with <c>.GetAwaiter().GetResult()</c> on the per-request hot
/// path. For most deployments the Redis latency dominates over the
/// sync-bridge cost; for latency-sensitive deployments, push for the
/// engine to expose an async <c>TryRegisterAsync</c> overload.
/// </para>
/// </remarks>
public sealed class RedisPqJwtReplayCache : IPqJwtReplayCache
{
    /// <summary>
    /// Fallback TTL applied when the token's expiration is in the past
    /// or unknown. Defaults to <see cref="DefaultFallbackTtl"/>. Tokens
    /// past their <c>exp</c> are already rejected by the validator
    /// before reaching the replay cache; the fallback exists for the
    /// rare case where a malformed or test-fixture token gets through
    /// with an <c>exp</c> we can't trust.
    /// </summary>
    public static readonly TimeSpan DefaultFallbackTtl = TimeSpan.FromMinutes(15);

    private readonly IDatabase _database;
    private readonly TimeProvider _timeProvider;
    private readonly string _keyPrefix;

    /// <summary>The configured fallback TTL.</summary>
    public TimeSpan FallbackTtl { get; }

    /// <summary>
    /// Creates a Redis-backed replay cache.
    /// </summary>
    /// <param name="database">The StackExchange.Redis database instance.</param>
    /// <param name="keyPrefix">Prefix prepended to every <c>jti</c> key. Pick something unique to this service so multiple apps sharing one Redis can't collide.</param>
    /// <param name="fallbackTtl">TTL for entries whose token expiration is unknown or in the past.</param>
    /// <param name="timeProvider">Clock used to compute remaining TTL.</param>
    public RedisPqJwtReplayCache(
        IDatabase database,
        string keyPrefix = "pqjwt:jti:",
        TimeSpan? fallbackTtl = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrEmpty(keyPrefix);
        _database = database;
        _keyPrefix = keyPrefix;
        FallbackTtl = fallbackTtl ?? DefaultFallbackTtl;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Issues <c>SET prefix:{jti} 1 NX PX {ttl}</c>. Returns
    /// <see langword="true"/> on first use, <see langword="false"/> on
    /// any replay. Network failures bubble up — a Redis outage should
    /// surface, not silently disable replay protection.
    /// </remarks>
    public bool TryRegister(string jwtId, DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrEmpty(jwtId);

        var ttl = ComputeTtl(expiresAt);
        var key = _keyPrefix + jwtId;

        // StringSetAsync with When.NotExists is the equivalent of SET NX
        // and returns true iff the key didn't exist (= first use). We
        // pass every argument by name + position to avoid any future
        // overload-resolution surprise (StackExchange.Redis ships both
        // 4-arg and 6-arg overloads).
        return _database
            .StringSetAsync(
                key: key,
                value: "1",
                expiry: ttl,
                keepTtl: false,
                when: When.NotExists,
                flags: CommandFlags.None)
            .GetAwaiter().GetResult();
    }

    private TimeSpan ComputeTtl(DateTimeOffset expiresAt)
    {
        var now = _timeProvider.GetUtcNow();
        if (expiresAt <= now || expiresAt == DateTimeOffset.MaxValue)
        {
            return FallbackTtl;
        }

        var remaining = expiresAt - now;
        // Don't carry entries longer than the fallback caps at — even if a
        // token claims an extreme exp, we cap to keep the cache bounded
        // in adversarial-clock cases.
        return remaining > TimeSpan.FromDays(30) ? FallbackTtl : remaining;
    }
}
