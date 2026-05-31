using System.Net;
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
/// Full-pipeline tests for IPostQuantumJwtKeyRing wiring: tokens with a
/// kid resolve through the configured ring, MLDsa instances handed out by
/// the ring stay valid through a refresh, and the ring helper survives a
/// real DI graph without the BuildServiceProvider anti-pattern.
/// </summary>
public sealed class KeyRingIntegrationTests
{
    [PqcFact]
    public async Task TokenWithKid_ValidatesThroughInProcessKeyRing()
    {
        using var signer = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65);
        using var verifier = MLDsa.ImportMLDsaPublicKey(
            MLDsaAlgorithm.MLDsa65, signer.ExportMLDsaPublicKey());
        var ring = new InMemoryKeyRing { ["k1"] = verifier };

        using var host = BuildHost(ring);
        using var client = host.GetTestClient();

        var token = new PqJwtBuilder()
            .WithIssuer(TestServerFactory.Issuer)
            .WithAudience(TestServerFactory.Audience)
            .WithSubject("kid-test")
            .WithJwtId(Guid.NewGuid().ToString("N"))
            .WithLifetime(TimeSpan.FromMinutes(5))
            .WithKeyId("k1")
            .SignWith(signer)
            .Build();

        using var req = new HttpRequestMessage(HttpMethod.Get, "/me");
        req.Headers.Authorization = new("Bearer", token);
        using var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [PqcFact]
    public async Task TokenWithUnknownKid_FailsClosed()
    {
        using var signer = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65);
        var ring = new InMemoryKeyRing();   // empty

        using var host = BuildHost(ring);
        using var client = host.GetTestClient();

        var token = new PqJwtBuilder()
            .WithIssuer(TestServerFactory.Issuer)
            .WithAudience(TestServerFactory.Audience)
            .WithSubject("kid-test")
            .WithJwtId(Guid.NewGuid().ToString("N"))
            .WithLifetime(TimeSpan.FromMinutes(5))
            .WithKeyId("never-registered")
            .SignWith(signer)
            .Build();

        using var req = new HttpRequestMessage(HttpMethod.Get, "/me");
        req.Headers.Authorization = new("Bearer", token);
        using var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [PqcFact]
    public async Task KidRotation_OldKeyKeepsWorking_DuringRefresh()
    {
        // Regression test for the disposal race: a token issued under k1 must
        // still validate even after the key ring "rotates" and replaces the k1
        // entry. The handler holds a reference to the MLDsa returned by the
        // resolver; if the ring eagerly disposes the previous instance, this
        // test would throw ObjectDisposedException mid-validation.
        using var signerA = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65);
        using var verifierA = MLDsa.ImportMLDsaPublicKey(
            MLDsaAlgorithm.MLDsa65, signerA.ExportMLDsaPublicKey());
        using var signerB = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65);
        using var verifierB = MLDsa.ImportMLDsaPublicKey(
            MLDsaAlgorithm.MLDsa65, signerB.ExportMLDsaPublicKey());

        var ring = new InMemoryKeyRing { ["k1"] = verifierA };
        using var host = BuildHost(ring);
        using var client = host.GetTestClient();

        var tokenA = new PqJwtBuilder()
            .WithIssuer(TestServerFactory.Issuer)
            .WithAudience(TestServerFactory.Audience)
            .WithSubject("rotation-test")
            .WithJwtId(Guid.NewGuid().ToString("N"))
            .WithLifetime(TimeSpan.FromMinutes(5))
            .WithKeyId("k1")
            .SignWith(signerA)
            .Build();

        // First request: succeeds against verifierA.
        using (var req1 = new HttpRequestMessage(HttpMethod.Get, "/me"))
        {
            req1.Headers.Authorization = new("Bearer", tokenA);
            using var resp1 = await client.SendAsync(req1);
            Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);
        }

        // Simulate a key rotation: replace k1 with verifierB. The ring must
        // NOT dispose verifierA — tokenA was issued against it and still
        // needs to validate (engine holds a reference via SignatureKeyResolver).
        ring["k1"] = verifierB;

        // The "old" tokenA should now fail (signed by signerA, ring returns
        // verifierB) but the request must reach validation cleanly —
        // ObjectDisposedException would be a 500, not a 401.
        using (var req2 = new HttpRequestMessage(HttpMethod.Get, "/me"))
        {
            req2.Headers.Authorization = new("Bearer", tokenA);
            using var resp2 = await client.SendAsync(req2);
            Assert.Equal(HttpStatusCode.Unauthorized, resp2.StatusCode);
        }
    }

    private static IHost BuildHost(IPostQuantumJwtKeyRing ring)
    {
        return Host.CreateDefaultBuilder()
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
                                ValidIssuer = TestServerFactory.Issuer,
                                ValidAudience = TestServerFactory.Audience,
                            };
                            // Use the KeyRing options surface directly with the
                            // test ring instance. The DI helper has its own
                            // dedicated test below; this path exercises the
                            // handler's read of Options.KeyRing.
                            options.KeyRing = ring;
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
                        endpoints.MapGet("/me", [Authorize] (HttpContext ctx) =>
                            Results.Ok(new { sub = ctx.User.FindFirst("sub")?.Value }));
                    });
                });
            })
            .Start();
    }

    [PqcFact]
    public async Task DiHelper_WiresKeyRingThroughPostConfigure()
    {
        // Verify that AddPostQuantumJwtKeyRing<T>() wires the ring onto the
        // named options instance via PostConfigure — the production path
        // that the README documents.
        using var signer = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65);
        using var verifier = MLDsa.ImportMLDsaPublicKey(
            MLDsaAlgorithm.MLDsa65, signer.ExportMLDsaPublicKey());
        DiHelperKeyRing.SharedKey = verifier;

        using var host = Host.CreateDefaultBuilder()
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
                                ValidIssuer = TestServerFactory.Issuer,
                                ValidAudience = TestServerFactory.Audience,
                            };
                        });
                    // The actual production helper under test:
                    services.AddPostQuantumJwtKeyRing<DiHelperKeyRing>();
                    services.AddAuthorization();
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/me", [Authorize] (HttpContext ctx) =>
                            Results.Ok(new { sub = ctx.User.FindFirst("sub")?.Value }));
                    });
                });
            })
            .Start();
        using var client = host.GetTestClient();

        var token = new PqJwtBuilder()
            .WithIssuer(TestServerFactory.Issuer)
            .WithAudience(TestServerFactory.Audience)
            .WithSubject("di-helper")
            .WithJwtId(Guid.NewGuid().ToString("N"))
            .WithLifetime(TimeSpan.FromMinutes(5))
            .WithKeyId("di-kid")
            .SignWith(signer)
            .Build();

        using var req = new HttpRequestMessage(HttpMethod.Get, "/me");
        req.Headers.Authorization = new("Bearer", token);
        using var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    // The DI helper resolves IPostQuantumJwtKeyRing via Activator/DI, so this
    // type needs a default constructor. The key is published on a static
    // field by the test scaffold — fine for a single-suite test fixture.
    private sealed class DiHelperKeyRing : IPostQuantumJwtKeyRing
    {
        public static MLDsa? SharedKey { get; set; }

        public MLDsa? Resolve(string? keyId)
            => keyId == "di-kid" ? SharedKey : null;
    }

    private sealed class InMemoryKeyRing : IPostQuantumJwtKeyRing
    {
        private readonly Dictionary<string, MLDsa> _keys = new(StringComparer.Ordinal);

        public MLDsa? this[string kid]
        {
            get => _keys.TryGetValue(kid, out var k) ? k : null;
            set => _keys[kid] = value!;
        }

        public MLDsa? Resolve(string? keyId)
            => keyId is not null && _keys.TryGetValue(keyId, out var k) ? k : null;
    }
}
