using NSubstitute;
using PostQuantum.AspNetCore.RedisReplayCache;
using StackExchange.Redis;

namespace PostQuantum.AspNetCore.Tests;

/// <summary>
/// Locks the <see cref="RedisPqJwtReplayCache"/> contract using NSubstitute
/// to stub <see cref="IDatabase"/>'s SETNX behaviour. The single Redis
/// primitive the cache depends on (StringSetAsync with When.NotExists) is
/// configured to match the production semantics; any other Redis call
/// would surface immediately because the substitute returns default.
/// </summary>
public sealed class RedisReplayCacheTests
{
    [Fact]
    public void TryRegister_FirstUse_ReturnsTrue()
    {
        var (db, _) = NewStub();
        var cache = new RedisPqJwtReplayCache(db, "test:");
        Assert.True(cache.TryRegister("jti-001", DateTimeOffset.UtcNow.AddMinutes(10)));
    }

    [Fact]
    public void TryRegister_SecondUseSameJti_ReturnsFalse()
    {
        var (db, _) = NewStub();
        var cache = new RedisPqJwtReplayCache(db, "test:");
        Assert.True(cache.TryRegister("jti-001", DateTimeOffset.UtcNow.AddMinutes(10)));
        Assert.False(cache.TryRegister("jti-001", DateTimeOffset.UtcNow.AddMinutes(10)));
    }

    [Fact]
    public void TryRegister_DifferentJtis_BothReturnTrue()
    {
        var (db, _) = NewStub();
        var cache = new RedisPqJwtReplayCache(db, "test:");
        Assert.True(cache.TryRegister("jti-A", DateTimeOffset.UtcNow.AddMinutes(10)));
        Assert.True(cache.TryRegister("jti-B", DateTimeOffset.UtcNow.AddMinutes(10)));
    }

    [Fact]
    public void TryRegister_KeyPrefixIsApplied()
    {
        var (db, store) = NewStub();
        var cache = new RedisPqJwtReplayCache(db, "my-prefix:");
        cache.TryRegister("abc", DateTimeOffset.UtcNow.AddMinutes(10));
        Assert.True(store.ContainsKey("my-prefix:abc"));
    }

    [Fact]
    public void TryRegister_TtlMatchesRemainingLifetime_WithinSecondTolerance()
    {
        var now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var time = new FixedTimeProvider(now);
        var (db, store) = NewStub();
        var cache = new RedisPqJwtReplayCache(db, "test:", timeProvider: time);

        cache.TryRegister("jti-001", now + TimeSpan.FromMinutes(30));

        var entry = store["test:jti-001"];
        var diff = (entry.Expiry - TimeSpan.FromMinutes(30)).Duration();
        Assert.True(diff < TimeSpan.FromSeconds(1),
            $"Expected ~30min TTL, got {entry.Expiry.TotalMinutes:F2}min.");
    }

    [Fact]
    public void TryRegister_ExpInPast_UsesFallbackTtl()
    {
        var now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var time = new FixedTimeProvider(now);
        var (db, store) = NewStub();
        var cache = new RedisPqJwtReplayCache(
            db, "test:",
            fallbackTtl: TimeSpan.FromMinutes(5),
            timeProvider: time);

        cache.TryRegister("jti-001", now - TimeSpan.FromHours(1));

        Assert.Equal(TimeSpan.FromMinutes(5), store["test:jti-001"].Expiry);
    }

    [Fact]
    public void TryRegister_DateTimeOffsetMax_UsesFallbackTtl()
    {
        var (db, store) = NewStub();
        var cache = new RedisPqJwtReplayCache(db, "test:",
            fallbackTtl: TimeSpan.FromMinutes(5));

        cache.TryRegister("jti-001", DateTimeOffset.MaxValue);

        Assert.Equal(TimeSpan.FromMinutes(5), store["test:jti-001"].Expiry);
    }

    [Fact]
    public void TryRegister_RemainingTtlOverThirtyDays_CapsToFallback()
    {
        var now = DateTimeOffset.UtcNow;
        var (db, store) = NewStub();
        var cache = new RedisPqJwtReplayCache(
            db, "test:",
            fallbackTtl: TimeSpan.FromMinutes(10),
            timeProvider: new FixedTimeProvider(now));

        cache.TryRegister("jti-greedy", now + TimeSpan.FromDays(365));

        Assert.Equal(TimeSpan.FromMinutes(10), store["test:jti-greedy"].Expiry);
    }

    [Fact]
    public void Constructor_RejectsNullDatabase_AndEmptyPrefix()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new RedisPqJwtReplayCache(null!, "x:"));
        var (db, _) = NewStub();
        Assert.Throws<ArgumentException>(() =>
            new RedisPqJwtReplayCache(db, ""));
    }

    [Fact]
    public void TryRegister_RejectsNullOrEmptyJti()
    {
        var (db, _) = NewStub();
        var cache = new RedisPqJwtReplayCache(db, "test:");
        Assert.Throws<ArgumentNullException>(() =>
            cache.TryRegister(null!, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() =>
            cache.TryRegister(string.Empty, DateTimeOffset.UtcNow));
    }

    // Builds an NSubstitute IDatabase that mimics SETNX semantics
    // against a backing dictionary so tests can observe what landed.
    private static (IDatabase database, Dictionary<string, StoredEntry> store) NewStub()
    {
        var store = new Dictionary<string, StoredEntry>(StringComparer.Ordinal);
        var db = Substitute.For<IDatabase>();

        db.StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<bool>(),
            Arg.Any<When>(),
            Arg.Any<CommandFlags>())
            .Returns(call =>
            {
                var key = (string)call.ArgAt<RedisKey>(0)!;
                var value = call.ArgAt<RedisValue>(1);
                var expiry = call.ArgAt<TimeSpan?>(2);
                var when = call.ArgAt<When>(4);

                if (when == When.NotExists && store.ContainsKey(key))
                {
                    return Task.FromResult(false);
                }

                store[key] = new StoredEntry(value, expiry ?? TimeSpan.MaxValue);
                return Task.FromResult(true);
            });

        return (db, store);
    }

    private sealed record StoredEntry(RedisValue Value, TimeSpan Expiry);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
