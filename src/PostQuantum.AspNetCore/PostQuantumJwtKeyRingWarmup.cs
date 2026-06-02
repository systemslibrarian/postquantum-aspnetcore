using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PostQuantum.AspNetCore;

/// <summary>
/// Configuration for <see cref="PostQuantumJwtKeyRingWarmupExtensions.AddPostQuantumJwtKeyRingWarmup"/>.
/// </summary>
public sealed class PostQuantumJwtKeyRingWarmupOptions
{
    /// <summary>
    /// If <see langword="true"/> (the default), warmup throws on startup
    /// when the key endpoint is unreachable — the host fails to start
    /// rather than coming up with an empty cache. Set to
    /// <see langword="false"/> for best-effort warmup that logs the
    /// failure and lets the host start; the first authentication request
    /// will then trigger a refresh on cache miss.
    /// </summary>
    public bool FailFastOnStartup { get; set; } = true;

    /// <summary>
    /// If set, the hosted service re-runs <see cref="IPostQuantumJwtKeyRing.PreloadAsync"/>
    /// on this interval after the initial warmup. <see langword="null"/>
    /// (the default) disables periodic refresh — the ring still refreshes
    /// on unknown-<c>kid</c> misses inside the validation hot path.
    /// </summary>
    /// <remarks>
    /// Background refresh is the operational answer to "I want a removed
    /// kid to drop from the cache within N minutes without waiting for a
    /// miss." Pick an interval matched to your key-rotation cadence.
    /// </remarks>
    public TimeSpan? RefreshInterval { get; set; }
}

/// <summary>
/// Hosted service that warms <see cref="IPostQuantumJwtKeyRing"/> at
/// host startup and (optionally) refreshes it on a timer. Registered by
/// <see cref="PostQuantumJwtKeyRingWarmupExtensions.AddPostQuantumJwtKeyRingWarmup"/>;
/// not intended for direct construction.
/// </summary>
public sealed class PostQuantumJwtKeyRingWarmupService : IHostedService, IDisposable
{
    private readonly IPostQuantumJwtKeyRing _keyRing;
    private readonly PostQuantumJwtKeyRingWarmupOptions _options;
    private readonly ILogger<PostQuantumJwtKeyRingWarmupService>? _logger;
    private readonly TimeProvider _timeProvider;
    private ITimer? _timer;
    private CancellationTokenSource? _stoppingCts;

    /// <summary>Creates the warmup service.</summary>
    /// <param name="keyRing">The key ring to warm.</param>
    /// <param name="options">Warmup configuration.</param>
    /// <param name="timeProvider">Clock used for the optional periodic timer.</param>
    /// <param name="logger">Optional logger.</param>
    public PostQuantumJwtKeyRingWarmupService(
        IPostQuantumJwtKeyRing keyRing,
        PostQuantumJwtKeyRingWarmupOptions options,
        TimeProvider? timeProvider = null,
        ILogger<PostQuantumJwtKeyRingWarmupService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(keyRing);
        ArgumentNullException.ThrowIfNull(options);
        _keyRing = keyRing;
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            await _keyRing.PreloadAsync(_stoppingCts.Token).ConfigureAwait(false);
            _logger?.WarmupSucceeded();
        }
        catch (OperationCanceledException) when (_stoppingCts.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (_options.FailFastOnStartup)
            {
                _logger?.WarmupFailedFailFast(ex);
                throw;
            }

            _logger?.WarmupFailedBestEffort(ex);
        }

        if (_options.RefreshInterval is { } interval && interval > TimeSpan.Zero)
        {
            _timer = _timeProvider.CreateTimer(
                RefreshTimerCallback,
                state: null,
                dueTime: interval,
                period: interval);
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Dispose();
        _timer = null;
        _stoppingCts?.Cancel();
        return Task.CompletedTask;
    }

    private void RefreshTimerCallback(object? state)
    {
        // Fire-and-forget: a tick that fails just logs and waits for the
        // next tick — periodic refresh is "best effort" by definition
        // (the hot path already covers unknown-kid misses).
        var token = _stoppingCts?.Token ?? CancellationToken.None;
        if (token.IsCancellationRequested)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await _keyRing.PreloadAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Service is stopping — swallow.
            }
            catch (Exception ex)
            {
                _logger?.PeriodicRefreshFailed(ex);
            }
        }, token);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _timer?.Dispose();
        _stoppingCts?.Dispose();
    }
}

/// <summary>
/// DI helper for registering the key-ring warmup hosted service.
/// </summary>
public static class PostQuantumJwtKeyRingWarmupExtensions
{
    /// <summary>
    /// Registers a hosted service that calls
    /// <see cref="IPostQuantumJwtKeyRing.PreloadAsync"/> on host startup,
    /// and optionally on a periodic timer thereafter. By default warmup
    /// is fail-fast — the host won't start if the key endpoint is
    /// unreachable. Set
    /// <see cref="PostQuantumJwtKeyRingWarmupOptions.FailFastOnStartup"/>
    /// to <see langword="false"/> to make warmup best-effort instead.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration callback.</param>
    /// <returns>The same service collection.</returns>
    /// <remarks>
    /// Call this after <see cref="PostQuantumJwtKeyRingExtensions.AddPostQuantumJwtKeyRing(IServiceCollection, Uri, TimeSpan?, Action{IHttpClientBuilder}?)"/>
    /// (or any other registration that puts an
    /// <see cref="IPostQuantumJwtKeyRing"/> in DI). Without a ring in DI,
    /// the hosted service throws at startup — and that's the right time
    /// for that error to surface.
    /// </remarks>
    public static IServiceCollection AddPostQuantumJwtKeyRingWarmup(
        this IServiceCollection services,
        Action<PostQuantumJwtKeyRingWarmupOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new PostQuantumJwtKeyRingWarmupOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddHostedService<PostQuantumJwtKeyRingWarmupService>();
        return services;
    }
}
