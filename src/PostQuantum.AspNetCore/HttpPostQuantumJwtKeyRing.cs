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
    private readonly ConcurrentDictionary<string, MLDsa> _cache = new(StringComparer.Ordinal);
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
            if (!force && _timeProvider.GetUtcNow() - _lastFetched < _refreshInterval)
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
                    _cache.AddOrUpdate(entry.Kid, key, (_, old) =>
                    {
                        old.Dispose();
                        return key;
                    });
                }
                catch (Exception ex) when (ex is FormatException or CryptographicException)
                {
                    _logger?.KeyRingEntryMalformed(ex, entry.Kid);
                }
            }

            _lastFetched = _timeProvider.GetUtcNow();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            // Network / parse failures are logged and swallowed — Resolve() will
            // return null for an unknown kid, which surfaces upstream as a
            // fail-closed signature-resolver miss.
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

        foreach (var key in _cache.Values)
        {
            key.Dispose();
        }

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
