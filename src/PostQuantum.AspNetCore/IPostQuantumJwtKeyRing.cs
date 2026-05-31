using System.Security.Cryptography;

namespace PostQuantum.AspNetCore;

/// <summary>
/// A directory of ML-DSA-65 verification keys keyed by <c>kid</c>. Used to
/// supply <c>PqJwtValidationParameters.SignatureKeyResolver</c> a thread-safe
/// lookup; implementations can be backed by in-memory state, a configuration
/// system, or an HTTP fetch from a trusted endpoint
/// (see <see cref="HttpPostQuantumJwtKeyRing"/>).
/// </summary>
/// <remarks>
/// This is the post-quantum analogue of a JWKS endpoint. The standard JWKS
/// format isn't applicable here — there is no IANA-registered representation
/// for an ML-DSA-65 key yet — so the over-the-wire format is intentionally
/// trivial: a JSON object whose <c>keys</c> array carries
/// <c>{ kid, alg, key }</c> triples, with <c>key</c> the base64-encoded raw
/// ML-DSA-65 public-key bytes.
/// </remarks>
public interface IPostQuantumJwtKeyRing
{
    /// <summary>
    /// Resolves a verification key for a given <c>kid</c>, or
    /// <see langword="null"/> if the kid is not known.
    /// </summary>
    /// <param name="keyId">The token's <c>kid</c> header value (may be <see langword="null"/>).</param>
    /// <returns>The verification key, or <see langword="null"/>.</returns>
    /// <remarks>
    /// This synchronous shape matches
    /// <c>PqJwtValidationParameters.SignatureKeyResolver</c>, which is itself
    /// synchronous in the current engine. For HTTP-backed implementations,
    /// prefer warming the cache via <see cref="ResolveAsync"/> /
    /// <c>PreloadAsync</c> at startup so this hot path never has to block.
    /// </remarks>
    MLDsa? Resolve(string? keyId);

    /// <summary>
    /// Asynchronously resolves a verification key for a given <c>kid</c>,
    /// or <see langword="null"/> if the kid is not known.
    /// </summary>
    /// <param name="keyId">The token's <c>kid</c> header value (may be <see langword="null"/>).</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task whose result is the verification key or <see langword="null"/>.</returns>
    /// <remarks>
    /// The default implementation delegates to <see cref="Resolve"/> and
    /// wraps the result in a completed task — fine for in-memory key rings.
    /// Implementations that perform I/O (HTTP, database, KMS) should
    /// override this with a natively-async path so warming the cache from
    /// background services doesn't block a thread.
    /// </remarks>
    ValueTask<MLDsa?> ResolveAsync(string? keyId, CancellationToken cancellationToken = default)
        => new(Resolve(keyId));

    /// <summary>
    /// Optionally pre-loads the ring's key cache so the first authentication
    /// request doesn't pay a cold-start fetch. Implementations that do not
    /// need warmup (e.g. an in-memory ring populated at construction) can
    /// inherit the default no-op behaviour.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the cache has been warmed.</returns>
    /// <remarks>
    /// Typically called from a hosted service registered via
    /// <c>services.AddPostQuantumJwtKeyRingWarmup(...)</c>. May throw if
    /// the underlying source is unreachable and the caller wants
    /// fail-fast startup semantics; the warmup helper exposes that
    /// behaviour as an option.
    /// </remarks>
    Task PreloadAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
