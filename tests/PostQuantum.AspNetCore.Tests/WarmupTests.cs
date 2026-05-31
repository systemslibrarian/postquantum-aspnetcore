using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Security.Cryptography;
using System.Threading;

namespace PostQuantum.AspNetCore.Tests;

/// <summary>
/// Locks the warmup hosted-service contract: PreloadAsync runs at host
/// start; FailFastOnStartup controls whether a failing fetch aborts the
/// host or is logged-and-swallowed; periodic refresh fires on the
/// configured interval; cancellation propagates on host stop.
/// </summary>
public sealed class WarmupTests
{
    [Fact]
    public async Task StartAsync_CallsPreloadOnce_ByDefault()
    {
        var ring = new CountingKeyRing();
        var options = new PostQuantumJwtKeyRingWarmupOptions();
        using var service = new PostQuantumJwtKeyRingWarmupService(ring, options);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(1, ring.PreloadCount);
    }

    [Fact]
    public async Task StartAsync_FailFast_PropagatesPreloadException()
    {
        var ring = new ThrowingKeyRing(new InvalidOperationException("unreachable"));
        var options = new PostQuantumJwtKeyRingWarmupOptions { FailFastOnStartup = true };
        using var service = new PostQuantumJwtKeyRingWarmupService(ring, options);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.StartAsync(CancellationToken.None));
        Assert.Equal("unreachable", ex.Message);
    }

    [Fact]
    public async Task StartAsync_BestEffort_SwallowsPreloadException()
    {
        var ring = new ThrowingKeyRing(new InvalidOperationException("unreachable"));
        var options = new PostQuantumJwtKeyRingWarmupOptions { FailFastOnStartup = false };
        using var service = new PostQuantumJwtKeyRingWarmupService(ring, options);

        // Must not throw.
        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task PeriodicRefresh_FiresOnInterval()
    {
        var ring = new CountingKeyRing();
        var options = new PostQuantumJwtKeyRingWarmupOptions
        {
            RefreshInterval = TimeSpan.FromMilliseconds(50),
        };
        var fakeTime = new FakeTimeProvider();
        using var service = new PostQuantumJwtKeyRingWarmupService(ring, options, fakeTime);

        await service.StartAsync(CancellationToken.None);
        Assert.Equal(1, ring.PreloadCount); // initial warmup

        // Advance time past 3 intervals; each tick fires Preload once.
        for (var i = 0; i < 3; i++)
        {
            fakeTime.Advance(TimeSpan.FromMilliseconds(50));
            // Periodic tick is fire-and-forget on the threadpool; allow a
            // short real-world settle window so the awaited PreloadAsync
            // completes before the next assertion.
            await ring.WaitForPreloadCountAtLeast(2 + i, TimeSpan.FromSeconds(1));
        }

        await service.StopAsync(CancellationToken.None);
        Assert.InRange(ring.PreloadCount, 4, 6); // initial + 3 ticks, with some slack
    }

    [Fact]
    public async Task DiHelper_RegistersHostedService()
    {
        var ring = new CountingKeyRing();
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IPostQuantumJwtKeyRing>(ring);
                services.AddPostQuantumJwtKeyRingWarmup();
            })
            .Build();

        await host.StartAsync(TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, ring.PreloadCount);
    }

    private sealed class CountingKeyRing : IPostQuantumJwtKeyRing
    {
        private int _preloadCount;
        public int PreloadCount => Volatile.Read(ref _preloadCount);

        public MLDsa? Resolve(string? keyId) => null;

        public Task PreloadAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _preloadCount);
            return Task.CompletedTask;
        }

        public async Task WaitForPreloadCountAtLeast(int target, TimeSpan timeout)
        {
            var deadline = DateTimeOffset.UtcNow + timeout;
            while (PreloadCount < target && DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(5);
            }
        }
    }

    private sealed class ThrowingKeyRing : IPostQuantumJwtKeyRing
    {
        private readonly Exception _toThrow;

        public ThrowingKeyRing(Exception toThrow)
        {
            _toThrow = toThrow;
        }

        public MLDsa? Resolve(string? keyId) => null;

        public Task PreloadAsync(CancellationToken cancellationToken = default)
            => Task.FromException(_toThrow);
    }

    // Minimal fake TimeProvider with synchronous timer callbacks driven
    // by Advance(). Sufficient for the periodic-tick test.
    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly List<FakeTimer> _timers = new();
        private DateTimeOffset _utcNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new FakeTimer(callback, state, dueTime, period, _utcNow);
            _timers.Add(timer);
            return timer;
        }

        public void Advance(TimeSpan by)
        {
            _utcNow += by;
            foreach (var timer in _timers.ToArray())
            {
                timer.Tick(_utcNow);
            }
        }

        private sealed class FakeTimer : ITimer
        {
            private readonly TimerCallback _callback;
            private readonly object? _state;
            private readonly TimeSpan _period;
            private DateTimeOffset _nextFire;
            private bool _disposed;

            public FakeTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period, DateTimeOffset now)
            {
                _callback = callback;
                _state = state;
                _period = period;
                _nextFire = now + dueTime;
            }

            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Tick(DateTimeOffset now)
            {
                if (_disposed)
                {
                    return;
                }

                while (now >= _nextFire)
                {
                    _callback(_state);
                    _nextFire = _period > TimeSpan.Zero ? _nextFire + _period : DateTimeOffset.MaxValue;
                }
            }

            public void Dispose() => _disposed = true;

            public ValueTask DisposeAsync()
            {
                _disposed = true;
                return ValueTask.CompletedTask;
            }
        }
    }
}
