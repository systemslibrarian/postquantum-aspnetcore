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
    MLDsa? Resolve(string? keyId);
}
