using Microsoft.AspNetCore.Authentication;

namespace PostQuantum.AspNetCore;

/// <summary>
/// <c>AddPostQuantumJwtBearer(…)</c> extension methods for
/// <see cref="AuthenticationBuilder"/>. Mirrors the shape of
/// <c>AddJwtBearer</c> from
/// <c>Microsoft.AspNetCore.Authentication.JwtBearer</c> so post-quantum
/// tokens slot into existing auth-pipeline configuration the same way.
/// </summary>
public static class PostQuantumJwtBearerExtensions
{
    /// <summary>
    /// Adds post-quantum JWT bearer authentication under
    /// <see cref="PostQuantumJwtBearerDefaults.AuthenticationScheme"/> with default options.
    /// </summary>
    /// <param name="builder">The <see cref="AuthenticationBuilder"/>.</param>
    /// <param name="configure">Callback to configure <see cref="PostQuantumJwtBearerOptions"/>.</param>
    /// <returns>The same <see cref="AuthenticationBuilder"/>.</returns>
    public static AuthenticationBuilder AddPostQuantumJwtBearer(
        this AuthenticationBuilder builder,
        Action<PostQuantumJwtBearerOptions> configure)
        => builder.AddPostQuantumJwtBearer(PostQuantumJwtBearerDefaults.AuthenticationScheme, configure);

    /// <summary>
    /// Adds post-quantum JWT bearer authentication under a custom scheme name.
    /// </summary>
    /// <param name="builder">The <see cref="AuthenticationBuilder"/>.</param>
    /// <param name="authenticationScheme">The scheme name (e.g. <c>"PostQuantumJwtBearer"</c>).</param>
    /// <param name="configure">Callback to configure <see cref="PostQuantumJwtBearerOptions"/>.</param>
    /// <returns>The same <see cref="AuthenticationBuilder"/>.</returns>
    public static AuthenticationBuilder AddPostQuantumJwtBearer(
        this AuthenticationBuilder builder,
        string authenticationScheme,
        Action<PostQuantumJwtBearerOptions> configure)
        => builder.AddPostQuantumJwtBearer(authenticationScheme, displayName: null, configure);

    /// <summary>
    /// Adds post-quantum JWT bearer authentication under a custom scheme name
    /// and display name.
    /// </summary>
    /// <param name="builder">The <see cref="AuthenticationBuilder"/>.</param>
    /// <param name="authenticationScheme">The scheme name.</param>
    /// <param name="displayName">An optional human-readable display name (e.g. for UIs that enumerate registered schemes).</param>
    /// <param name="configure">Callback to configure <see cref="PostQuantumJwtBearerOptions"/>.</param>
    /// <returns>The same <see cref="AuthenticationBuilder"/>.</returns>
    public static AuthenticationBuilder AddPostQuantumJwtBearer(
        this AuthenticationBuilder builder,
        string authenticationScheme,
        string? displayName,
        Action<PostQuantumJwtBearerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(authenticationScheme);
        ArgumentNullException.ThrowIfNull(configure);

        return builder.AddScheme<PostQuantumJwtBearerOptions, PostQuantumJwtBearerHandler>(
            authenticationScheme, displayName, configure);
    }
}
