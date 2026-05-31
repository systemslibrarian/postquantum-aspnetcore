using Microsoft.AspNetCore.Authentication;
using PostQuantum.Jwt;

namespace PostQuantum.AspNetCore;

/// <summary>
/// Options for the <see cref="PostQuantumJwtBearerHandler"/>. The handler
/// delegates token validation to <see cref="PqJwtValidator"/>; this class
/// supplies the validation parameters and a few ASP.NET-Core-specific knobs.
/// </summary>
public sealed class PostQuantumJwtBearerOptions : AuthenticationSchemeOptions
{
    /// <summary>
    /// The validation parameters PostQuantum.Jwt uses to verify incoming tokens.
    /// At minimum, supply either
    /// <see cref="PqJwtValidationParameters.SignatureVerificationKey"/> or
    /// <see cref="PqJwtValidationParameters.SignatureKeyResolver"/> — the
    /// validator constructor fails closed if neither is set.
    /// </summary>
    public PqJwtValidationParameters ValidationParameters { get; set; } = new();

    /// <summary>
    /// The claim type that <see cref="System.Security.Claims.ClaimsIdentity.Name"/>
    /// is sourced from. Defaults to <c>"sub"</c> (the JWT subject claim) — what
    /// most JWT consumers expect. <c>Microsoft.AspNetCore.Authentication.JwtBearer</c>
    /// defaults to <c>"unique_name"</c>, which is less portable.
    /// </summary>
    public string NameClaimType { get; set; } = "sub";

    /// <summary>
    /// The claim type used for role checks (<c>[Authorize(Roles = …)]</c>).
    /// Defaults to <c>"role"</c>, matching common ML-DSA-issued tokens.
    /// </summary>
    public string RoleClaimType { get; set; } = "role";

    /// <summary>
    /// The authentication type used for the constructed
    /// <see cref="System.Security.Claims.ClaimsIdentity"/>. Defaults to
    /// <see cref="PostQuantumJwtBearerDefaults.AuthenticationScheme"/> so
    /// <c>User.Identity.IsAuthenticated</c> behaves correctly without
    /// further configuration.
    /// </summary>
    public string AuthenticationType { get; set; } = PostQuantumJwtBearerDefaults.AuthenticationScheme;

    /// <summary>
    /// If set, the value placed in the <c>WWW-Authenticate</c> response header
    /// when the handler issues a 401 challenge. Defaults to the scheme name.
    /// </summary>
    public string? Realm { get; set; }

    /// <summary>
    /// If <see langword="true"/> (the default), the 401 challenge response
    /// includes an <c>error="invalid_token"</c> parameter when a token was
    /// supplied but failed validation. Set to <see langword="false"/> if you
    /// would rather not leak any signal about why the request was rejected.
    /// </summary>
    public bool IncludeErrorDetailsInChallenge { get; set; } = true;

    /// <summary>
    /// Event hooks raised by the handler. Replace individual delegates on
    /// the supplied instance to react to token validation, failure, or
    /// challenge moments. The default instance has no-op delegates, so an
    /// unconfigured options object behaves exactly the same as one without
    /// events.
    /// </summary>
    public new PostQuantumJwtBearerEvents Events { get; set; } = new();

    /// <summary>
    /// An optional <see cref="IPostQuantumJwtKeyRing"/> wired into validation.
    /// When set <em>and</em> <see cref="PqJwtValidationParameters.SignatureKeyResolver"/>
    /// is <see langword="null"/>, the handler resolves verification keys
    /// by <c>kid</c> through this ring. An explicit
    /// <see cref="PqJwtValidationParameters.SignatureKeyResolver"/> always
    /// wins — this is the fallback wiring, not an override.
    /// </summary>
    /// <remarks>
    /// Prefer <c>services.AddPostQuantumJwtKeyRing(uri)</c> from
    /// <see cref="PostQuantumJwtKeyRingExtensions"/> over setting this
    /// property directly: the helper handles DI registration of the
    /// underlying <see cref="HttpClient"/> and post-configures the
    /// options without forcing you to build a parallel service provider.
    /// </remarks>
    public IPostQuantumJwtKeyRing? KeyRing { get; set; }

    /// <inheritdoc />
    /// <remarks>
    /// Called by the framework before the handler initializes. We refuse a
    /// configuration that supplies neither a verification key nor a key
    /// resolver — a validator with no way to obtain a public key is
    /// misconfigured by definition, and surfacing that at startup is far
    /// kinder than surfacing it on the first request.
    /// </remarks>
    public override void Validate()
    {
        base.Validate();

        if (ValidationParameters is null)
        {
            throw new InvalidOperationException(
                "PostQuantumJwtBearerOptions.ValidationParameters is required.");
        }

        if (ValidationParameters.SignatureVerificationKey is null &&
            ValidationParameters.SignatureKeyResolver is null &&
            KeyRing is null)
        {
            throw new InvalidOperationException(
                "PostQuantumJwtBearerOptions must supply at least one source of verification keys: " +
                "ValidationParameters.SignatureVerificationKey, ValidationParameters.SignatureKeyResolver, " +
                "or KeyRing. A validator with no source of verification keys cannot authenticate any token.");
        }
    }

    // Clock comes from the inherited AuthenticationSchemeOptions.TimeProvider —
    // set it on Options if you need a deterministic clock for tests or
    // simulated time in production.
}
