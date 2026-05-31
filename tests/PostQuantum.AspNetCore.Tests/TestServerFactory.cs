using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PostQuantum.Jwt;

namespace PostQuantum.AspNetCore.Tests;

/// <summary>
/// Spins up a minimal in-process ASP.NET Core host that:
///   • signs tokens with a freshly-generated ML-DSA-65 key,
///   • validates them with <see cref="PostQuantumJwtBearerHandler"/> against
///     the matching public key,
///   • exposes <c>/me</c> as the protected endpoint under test.
/// The test fixture owns the disposable key pair; tests pull the
/// <see cref="System.Net.Http.HttpClient"/> and call into the server.
/// </summary>
internal sealed class TestServerFactory : IDisposable
{
    public const string Issuer = "https://test.postquantum.local";
    public const string Audience = "https://api.test.postquantum.local";

    private readonly IHost _host;
    private bool _disposed;

    public MLDsa Signer { get; }

    public MLDsa Verifier { get; }

    public Action<PostQuantumJwtBearerOptions> ConfigureOptions { get; set; }
        = static _ => { };

    public TestServerFactory()
    {
        Signer = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65);
        Verifier = MLDsa.ImportMLDsaPublicKey(
            MLDsaAlgorithm.MLDsa65, Signer.ExportMLDsaPublicKey());

        var builder = Host.CreateDefaultBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services
                        .AddAuthentication(PostQuantumJwtBearerDefaults.AuthenticationScheme)
                        .AddPostQuantumJwtBearer(options =>
                        {
                            options.ValidationParameters = new PqJwtValidationParameters
                            {
                                SignatureVerificationKey = Verifier,
                                ValidIssuer = Issuer,
                                ValidAudience = Audience,
                            };
                            ConfigureOptions(options);
                        });
                    services.AddAuthorization();
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/me", [Authorize] (HttpContext ctx) => Results.Ok(new
                        {
                            sub = ctx.User.FindFirst("sub")?.Value,
                            role = ctx.User.FindFirst("role")?.Value,
                            extra = ctx.User.FindFirst("extra")?.Value,
                        }));
                        endpoints.MapGet("/open", () => Results.Ok(new { ok = true }));
                    });
                });
            });

        _host = builder.Start();
    }

    public HttpClient CreateClient()
        => _host.GetTestClient();

    public string MintToken(
        string subject = "test-user",
        string? role = "tester",
        TimeSpan? lifetime = null,
        string issuer = Issuer,
        string audience = Audience,
        string? extra = null,
        DateTimeOffset? expiration = null)
    {
        var builder = new PqJwtBuilder()
            .WithIssuer(issuer)
            .WithAudience(audience)
            .WithSubject(subject)
            .WithJwtId(Guid.NewGuid().ToString("N"))
            .SignWith(Signer);

        // WithExpiration takes precedence when set (e.g. to simulate an
        // already-expired token); otherwise use the simpler WithLifetime.
        builder = expiration is { } exp
            ? builder.WithExpiration(exp)
            : builder.WithLifetime(lifetime ?? TimeSpan.FromMinutes(10));

        if (role is not null)
        {
            builder = builder.WithClaim("role", role);
        }

        if (extra is not null)
        {
            builder = builder.WithClaim("extra", extra);
        }

        return builder.Build();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _host.Dispose();
        Verifier.Dispose();
        Signer.Dispose();
        _disposed = true;
    }
}
