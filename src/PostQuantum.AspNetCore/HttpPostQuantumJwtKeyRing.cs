using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PostQuantum.Jwt;

namespace PostQuantum.AspNetCore;

/// <summary>
/// An <see cref="IPostQuantumJwtKeyRing"/> that lazily fetches a JSON key
/// directory from a trusted HTTPS endpoint and caches the resolved
/// <see cref="MLDsa"/> instances in memory. The cache refreshes on a
/// configurable interval and on any unknown <c>kid</c> (giving a single
/// chance to re-fetch before failing closed).
/// </summary>
/// <remarks>
/// The expected wire format is a JSON object of the form
/// <c>{ "keys": [ { "kid": "...", "alg": "ML-DSA-65", "key": "&lt;base64 bytes&gt;" }, ... ] }</c>.
/// Entries with an <c>alg</c> other than <c>ML-DSA-65</c> are skipped — this
/// is the single-suite policy the library is built on.
/// </remarks>
public sealed class HttpPostQuantumJwtKeyRing : IPostQuantumJwtKeyRing, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly Uri _endpoint;
    private readonly TimeSpan _refreshInterval;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<HttpPostQuantumJwtKeyRing>? _logger;
    // Volatile read/write for atomic swap on refresh — readers see either the
    // old snapshot or the new one, never a torn intermediate. Declared as the
    // interface so the analyzer's CA1859 "use the concrete type" suggestion
    // wouldn't apply; CA1859 fires anyway because it can't see the volatile
    // intent — suppress with a directed comment.
#pragma warning disable CA1859 // immutable snapshot semantics, not perf
    private volatile IReadOnlyDictionary<string, MLDsa> _cache = new Dictionary<string, MLDsa>(StringComparer.Ordinal);
#pragma warning restore CA1859
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private DateTimeOffset _lastFetched = DateTimeOffset.MinValue;
    private bool _disposed;

    /// <summary>Creates an HTTP-backed key ring.</summary>
    /// <param name="httpClient">An <see cref="HttpClient"/> (typed-client friendly).</param>
    /// <param name="endpoint">The fully-qualified key-directory URL. Must be HTTPS in production.</param>
    /// <param name="refreshInterval">How often the directory may be re-fetched. Defaults to 5 minutes.</param>
    /// <param name="timeProvider">Clock used for refresh timing.</param>
    /// <param name="logger">Optional logger.</param>
    public HttpPostQuantumJwtKeyRing(
        HttpClient httpClient,
        Uri endpoint,
        TimeSpan? refreshInterval = null,
        TimeProvider? timeProvider = null,
        ILogger<HttpPostQuantumJwtKeyRing>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(endpoint);
        _httpClient = httpClient;
        _endpoint = endpoint;
        _refreshInterval = refreshInterval ?? TimeSpan.FromMinutes(5);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Synchronous lookups against the in-memory cache are non-blocking; a
    /// miss falls back to <see cref="ResolveAsync"/> via
    /// <c>GetAwaiter().GetResult()</c>. Prefer warming the cache at startup
    /// with <see cref="PreloadAsync"/> so cold misses on the hot
    /// authentication path are rare.
    /// </remarks>
    public MLDsa? Resolve(string? keyId)
    {
        if (string.IsNullOrEmpty(keyId))
        {
            return null;
        }

        if (_cache.TryGetValue(keyId, out var cached))
        {
            return cached;
        }

        // Sync-over-async on the cold path only. The engine's
        // SignatureKeyResolver is currently synchronous, so this is the
        // narrowest place to bridge.
        return ResolveAsync(keyId).AsTask().GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public async ValueTask<MLDsa?> ResolveAsync(string? keyId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(keyId))
        {
            return null;
        }

        if (_cache.TryGetValue(keyId, out var cached))
        {
            return cached;
        }

        // Unknown kid → give the directory one chance to refresh.
        await RefreshAsync(force: true, cancellationToken).ConfigureAwait(false);
        return _cache.TryGetValue(keyId, out var resolved) ? resolved : null;
    }

    /// <summary>
    /// Preloads the directory. Optional — used by tests and by hosts that
    /// want to fail at startup if the key endpoint is unreachable.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the directory has been fetched (or has failed).</returns>
    public Task PreloadAsync(CancellationToken cancellationToken = default)
        => RefreshAsync(force: true, cancellationToken);

    private async Task RefreshAsync(bool force, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var now = _timeProvider.GetUtcNow();
        if (!force && now - _lastFetched < _refreshInterval)
        {
            return;
        }

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var nowInsideLock = _timeProvider.GetUtcNow();
            if (force && nowInsideLock - _lastFetched < TimeSpan.FromSeconds(10))
            {
                // Throttling: prevent amplification attacks from unknown kid flooding
                return;
            }
            else if (!force && nowInsideLock - _lastFetched < _refreshInterval)
            {
                return;
            }

            var directory = await _httpClient
                .GetFromJsonAsync(
                    _endpoint,
                    PostQuantumJwtKeyRingJsonContext.Default.PostQuantumJwtKeyDirectory,
                    cancellationToken)
                .ConfigureAwait(false);

            if (directory?.Keys is null)
            {
                _logger?.KeyRingEmpty(_endpoint);
                _lastFetched = _timeProvider.GetUtcNow();
                return;
            }

            var newCache = new Dictionary<string, MLDsa>(StringComparer.Ordinal);
            foreach (var entry in directory.Keys)
            {
                if (string.IsNullOrEmpty(entry.Kid) || string.IsNullOrEmpty(entry.Key))
                {
                    continue;
                }

                if (!string.Equals(entry.Alg, PqJwtAlgorithms.MLDsa65, StringComparison.Ordinal))
                {
                    // Single-suite policy: anything other than ML-DSA-65 is ignored.
                    continue;
                }

                try
                {
                    var bytes = Convert.FromBase64String(entry.Key);
                    var key = MLDsa.ImportMLDsaPublicKey(MLDsaAlgorithm.MLDsa65, bytes);
                    // Deliberately NOT disposing previous entries:
                    // an in-flight validation may still hold a reference to it
                    // via SignatureKeyResolver, and disposing under it would
                    // throw ObjectDisposedException mid-request. The native
                    // handle gets released by MLDsa's finalizer once the GC
                    // sees no live references.
                    newCache[entry.Kid] = key;
                }
                catch (Exception ex) when (ex is FormatException or CryptographicException)
                {
                    _logger?.KeyRingEntryMalformed(ex, entry.Kid);
                }
            }

            _cache = newCache;
            _lastFetched = _timeProvider.GetUtcNow();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Caller-initiated cancellation must propagate — never log-and-swallow.
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
        {
            // Network / parse failures (including server-side timeouts surfacing
            // as TaskCanceledException with no caller cancellation) are logged
            // and swallowed — Resolve() will return null for an unknown kid,
            // which surfaces upstream as a fail-closed signature-resolver miss.
            _logger?.KeyRingFetchFailed(ex, _endpoint);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // Note: cached MLDsa instances are NOT disposed here — once handed out
        // via Resolve(), they may still be in flight inside a validator. They
        // will be released by the finalizer when the GC sees no live
        // references. Dispose only owns the refresh lock.
        _refreshLock.Dispose();
        _disposed = true;
    }
}

/// <summary>One entry in the HTTP-fetched key directory.</summary>
public sealed class PostQuantumJwtKeyEntry
{
    /// <summary>The key identifier referenced by a token's <c>kid</c> header.</summary>
    [JsonPropertyName("kid")]
    public string Kid { get; init; } = "";

    /// <summary>The algorithm identifier; must be <c>"ML-DSA-65"</c>.</summary>
    [JsonPropertyName("alg")]
    public string Alg { get; init; } = "";

    /// <summary>Base64-encoded raw ML-DSA-65 public-key bytes.</summary>
    [JsonPropertyName("key")]
    public string Key { get; init; } = "";
}

/// <summary>Top-level shape of the HTTP key directory document.</summary>
public sealed class PostQuantumJwtKeyDirectory
{
    /// <summary>The published keys.</summary>
    [JsonPropertyName("keys")]
    public IList<PostQuantumJwtKeyEntry> Keys { get; init; } = [];
}

/// <summary>Source-generated JSON metadata for the key-ring types — keeps the package AOT-safe.</summary>
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(PostQuantumJwtKeyDirectory))]
[JsonSerializable(typeof(PostQuantumJwtKeyEntry))]
internal sealed partial class PostQuantumJwtKeyRingJsonContext : JsonSerializerContext;
