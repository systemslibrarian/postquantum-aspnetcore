using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace PostQuantum.AspNetCore;

/// <summary>
/// DI helpers for wiring an <see cref="IPostQuantumJwtKeyRing"/> into the
/// authentication pipeline without the <c>BuildServiceProvider()</c>
/// anti-pattern. These helpers register the key ring as a singleton, then
/// register an <see cref="IPostConfigureOptions{PostQuantumJwtBearerOptions}"/>
/// that resolves the ring from the real service provider and sets it on
/// <see cref="PostQuantumJwtBearerOptions.KeyRing"/>. The handler then
/// weaves the ring's <see cref="IPostQuantumJwtKeyRing.Resolve"/> method
/// onto <c>SignatureKeyResolver</c> on every options reload.
/// </summary>
public static class PostQuantumJwtKeyRingExtensions
{
    /// <summary>
    /// Registers an <see cref="HttpPostQuantumJwtKeyRing"/> backed by the
    /// supplied HTTPS endpoint and wires it onto
    /// <see cref="PostQuantumJwtBearerDefaults.AuthenticationScheme"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="endpoint">The fully-qualified key-directory URL. Must be HTTPS in production.</param>
    /// <param name="refreshInterval">How often the directory may be re-fetched. Defaults to 5 minutes.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddPostQuantumJwtKeyRing(
        this IServiceCollection services,
        Uri endpoint,
        TimeSpan? refreshInterval = null)
        => services.AddPostQuantumJwtKeyRing(
            PostQuantumJwtBearerDefaults.AuthenticationScheme, endpoint, refreshInterval);

    /// <summary>
    /// Registers an <see cref="HttpPostQuantumJwtKeyRing"/> backed by the
    /// supplied HTTPS endpoint and wires it onto a named authentication
    /// scheme.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="authenticationScheme">The scheme name that
    /// <see cref="PostQuantumJwtBearerExtensions.AddPostQuantumJwtBearer(Microsoft.AspNetCore.Authentication.AuthenticationBuilder, string, Action{PostQuantumJwtBearerOptions})"/>
    /// was called with.</param>
    /// <param name="endpoint">The fully-qualified key-directory URL. Must be HTTPS in production.</param>
    /// <param name="refreshInterval">How often the directory may be re-fetched. Defaults to 5 minutes.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddPostQuantumJwtKeyRing(
        this IServiceCollection services,
        string authenticationScheme,
        Uri endpoint,
        TimeSpan? refreshInterval = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(authenticationScheme);
        ArgumentNullException.ThrowIfNull(endpoint);

        services.AddHttpClient<HttpPostQuantumJwtKeyRing>();
        services.AddSingleton<IPostQuantumJwtKeyRing>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>()
                         .CreateClient(nameof(HttpPostQuantumJwtKeyRing));
            var timeProvider = sp.GetService<TimeProvider>() ?? TimeProvider.System;
            return new HttpPostQuantumJwtKeyRing(
                http,
                endpoint,
                refreshInterval,
                timeProvider,
                sp.GetService<Microsoft.Extensions.Logging.ILogger<HttpPostQuantumJwtKeyRing>>());
        });

        WireKeyRingPostConfigure(services, authenticationScheme);
        return services;
    }

    /// <summary>
    /// Registers a user-supplied <see cref="IPostQuantumJwtKeyRing"/>
    /// implementation (singleton) and wires it onto
    /// <see cref="PostQuantumJwtBearerDefaults.AuthenticationScheme"/>.
    /// Use this when you have a non-HTTP key source (database, KMS, file).
    /// </summary>
    /// <typeparam name="TKeyRing">The key-ring implementation.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddPostQuantumJwtKeyRing<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TKeyRing>(
        this IServiceCollection services)
        where TKeyRing : class, IPostQuantumJwtKeyRing
        => services.AddPostQuantumJwtKeyRing<TKeyRing>(PostQuantumJwtBearerDefaults.AuthenticationScheme);

    /// <summary>
    /// Registers a user-supplied <see cref="IPostQuantumJwtKeyRing"/>
    /// implementation (singleton) and wires it onto a named authentication
    /// scheme.
    /// </summary>
    /// <typeparam name="TKeyRing">The key-ring implementation.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="authenticationScheme">The scheme name.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddPostQuantumJwtKeyRing<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TKeyRing>(
        this IServiceCollection services,
        string authenticationScheme)
        where TKeyRing : class, IPostQuantumJwtKeyRing
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(authenticationScheme);

        services.AddSingleton<IPostQuantumJwtKeyRing, TKeyRing>();
        WireKeyRingPostConfigure(services, authenticationScheme);
        return services;
    }

    private static void WireKeyRingPostConfigure(IServiceCollection services, string authenticationScheme)
    {
        services.AddOptions<PostQuantumJwtBearerOptions>(authenticationScheme)
            .PostConfigure<IPostQuantumJwtKeyRing>((options, keyRing) =>
            {
                // Only set if the user hasn't already supplied one.
                // PostConfigure runs after Configure, so this respects an
                // explicit options.KeyRing = ...; assignment in the user's
                // AddPostQuantumJwtBearer callback.
                options.KeyRing ??= keyRing;
            });
    }
}
