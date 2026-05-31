using Microsoft.AspNetCore.Authentication;

namespace PostQuantum.AspNetCore;

/// <summary>
/// Events raised by the <see cref="PostQuantumJwtBearerHandler"/> at well-known
/// moments in the authentication pipeline. The shape mirrors
/// <c>Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents</c> so it
/// feels familiar, but the contract is deliberately small — only the three
/// hooks consumers actually reach for are surfaced.
/// </summary>
/// <remarks>
/// All callbacks return <see cref="Task"/> and run inline with request
/// processing. Heavy work belongs elsewhere; an event handler should be
/// short, allocation-light, and never block.
/// </remarks>
public class PostQuantumJwtBearerEvents
{
    /// <summary>
    /// Invoked after a token has been successfully validated. Use this to
    /// enrich the <see cref="System.Security.Claims.ClaimsPrincipal"/> (for
    /// example, load roles from a database) or replace the constructed
    /// <see cref="AuthenticationTicket"/>.
    /// </summary>
    public Func<PostQuantumJwtBearerTokenValidatedContext, Task> OnTokenValidated { get; set; }
        = static _ => Task.CompletedTask;

    /// <summary>
    /// Invoked when token validation throws. The caught exception is exposed
    /// on the context; the default behaviour after this callback returns is
    /// still <see cref="AuthenticateResult.Fail(Exception)"/>. Set
    /// <see cref="PostQuantumJwtBearerAuthenticationFailedContext.Result"/>
    /// to short-circuit with a different outcome (rare; usually
    /// observational).
    /// </summary>
    public Func<PostQuantumJwtBearerAuthenticationFailedContext, Task> OnAuthenticationFailed { get; set; }
        = static _ => Task.CompletedTask;

    /// <summary>
    /// Invoked when the handler is about to write a 401 challenge. Set
    /// <see cref="PostQuantumJwtBearerChallengeContext.Handled"/> to
    /// <see langword="true"/> to suppress the default
    /// <c>WWW-Authenticate</c> header (e.g. to substitute your own).
    /// </summary>
    public Func<PostQuantumJwtBearerChallengeContext, Task> OnChallenge { get; set; }
        = static _ => Task.CompletedTask;
}
