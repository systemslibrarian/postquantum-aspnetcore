using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PostQuantum.AspNetCore;
using PostQuantum.Jwt;
using StackExchange.Redis;

namespace PostQuantum.AspNetCore.RedisReplayCache;

/// <summary>
/// DI helpers that register <see cref="RedisPqJwtReplayCache"/> and wire
/// it onto <see cref="PqJwtValidationParameters.ReplayCache"/> via a
/// <see cref="IPostConfigureOptions{PostQuantumJwtBearerOptions}"/>.
/// </summary>
public static class RedisPqJwtReplayCacheExtensions
{
    /// <summary>
    /// Registers <see cref="RedisPqJwtReplayCache"/> and wires it onto
    /// <see cref="PostQuantumJwtBearerDefaults.AuthenticationScheme"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">A StackExchange.Redis connection string.</param>
    /// <param name="keyPrefix">Prefix for every <c>jti</c> key. Defaults to <c>pqjwt:jti:</c>.</param>
    /// <param name="fallbackTtl">TTL for entries with unknown expiration. Defaults to 15 minutes.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddPostQuantumJwtRedisReplayCache(
        this IServiceCollection services,
        string connectionString,
        string keyPrefix = "pqjwt:jti:",
        TimeSpan? fallbackTtl = null)
        => services.AddPostQuantumJwtRedisReplayCache(
            PostQuantumJwtBearerDefaults.AuthenticationScheme,
            connectionString,
            keyPrefix,
            fallbackTtl);

    /// <summary>
    /// Registers <see cref="RedisPqJwtReplayCache"/> and wires it onto a
    /// named authentication scheme.
    /// </summary>
    public static IServiceCollection AddPostQuantumJwtRedisReplayCache(
        this IServiceCollection services,
        string authenticationScheme,
        string connectionString,
        string keyPrefix = "pqjwt:jti:",
        TimeSpan? fallbackTtl = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(authenticationScheme);
        ArgumentException.ThrowIfNullOrEmpty(connectionString);
        ArgumentException.ThrowIfNullOrEmpty(keyPrefix);

        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(connectionString));
        services.AddSingleton<IPqJwtReplayCache>(sp =>
        {
            var mux = sp.GetRequiredService<IConnectionMultiplexer>();
            return new RedisPqJwtReplayCache(
                mux.GetDatabase(),
                keyPrefix,
                fallbackTtl,
                sp.GetService<TimeProvider>() ?? TimeProvider.System);
        });

        // PostConfigure: weave the cache onto the options after the
        // user's AddPostQuantumJwtBearer callback has run. We re-create
        // ValidationParameters because init-only properties — same
        // pattern as PostQuantum.AspNetCore's key-ring weaving.
        services.AddOptions<PostQuantumJwtBearerOptions>(authenticationScheme)
            .PostConfigure<IPqJwtReplayCache>((options, replayCache) =>
            {
                var src = options.ValidationParameters;
                if (src.ReplayCache is not null)
                {
                    // User explicitly set one; respect it.
                    return;
                }

                options.ValidationParameters = new PqJwtValidationParameters
                {
                    SignatureVerificationKey = src.SignatureVerificationKey,
                    SignatureKeyResolver = src.SignatureKeyResolver,
                    ReplayCache = replayCache,
                    DecryptionKey = src.DecryptionKey,
                    ValidIssuer = src.ValidIssuer,
                    ValidAudience = src.ValidAudience,
                    ValidateLifetime = src.ValidateLifetime,
                    RequireExpiration = src.RequireExpiration,
                    ClockSkew = src.ClockSkew,
                };
            });

        return services;
    }
}
