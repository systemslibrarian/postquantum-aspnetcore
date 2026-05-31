using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PostQuantum.Jwt;

namespace PostQuantum.AspNetCore.Tests;

/// <summary>
/// End-to-end integration test for the two DI helpers that together
/// supply a production deployment with its keys:
///
///   services.AddPostQuantumJwtKeyRing(uri);
///   services.AddPostQuantumJwtKeyRingWarmup();
///
/// Unit tests cover each helper in isolation. This test wires both
/// against a stubbed HTTP key-directory endpoint, starts a full Host,
/// and confirms (1) the warmup actually preloaded the cache before the
/// host considers itself started, and (2) the very first authentication
/// request hits the warm cache (no inline fetch).
/// </summary>
public sealed class WarmupIntegrationTests
{
    [PqcFact]
    public async Task AddKeyRing_AddKeyRingWarmup_TogetherWarmCacheBeforeFirstRequest()
    {
        using var signer = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65);
        using var verifier = MLDsa.ImportMLDsaPublicKey(
            MLDsaAlgorithm.MLDsa65, signer.ExportMLDsaPublicKey());
        var keyB64 = Convert.ToBase64String(signer.ExportMLDsaPublicKey());

        var directory = "{ \"keys\": [" +
            $"{{ \"kid\": \"k-prod\", \"alg\": \"ML-DSA-65\", \"key\": \"{keyB64}\" }}" +
            "] }";

        var counterHandler = new CountingStubHandler(directory);
        var keysUri = new Uri("https://keys.test/keys");

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
                                ValidIssuer = "https://issuer.test",
                                ValidAudience = "https://api.test",
                            };
                            // KeyRing wired via the AddPostQuantumJwtKeyRing
                            // helper's PostConfigure — no BuildServiceProvider
                            // anti-pattern. One singleton ring, shared by the
                            // warmup hosted service and the handler.
                        });
                    services.AddAuthorization();
                    services.AddPostQuantumJwtKeyRing(keysUri);
                    // Swap the typed client's primary handler to the stub
                    // so the ring talks to our counter instead of the network.
                    services.AddHttpClient<HttpPostQuantumJwtKeyRing>()
                        .ConfigurePrimaryHttpMessageHandler(() => counterHandler);
                    services.AddPostQuantumJwtKeyRingWarmup(o =>
                    {
                        o.FailFastOnStartup = true;
                    });
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

        // The warmup hosted service must have called PreloadAsync before
        // Host.Start() returned (StartAsync is awaited synchronously).
        Assert.Equal(1, counterHandler.CallCount);

        // The first authenticated request hits the warm cache; no
        // additional fetch happens.
        var token = new PqJwtBuilder()
            .WithIssuer("https://issuer.test")
            .WithAudience("https://api.test")
            .WithSubject("integration-user")
            .WithJwtId(Guid.NewGuid().ToString("N"))
            .WithLifetime(TimeSpan.FromMinutes(5))
            .WithKeyId("k-prod")
            .SignWith(signer)
            .Build();

        using var client = host.GetTestClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, "/me");
        req.Headers.Authorization = new("Bearer", token);
        using var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        // Still only the warmup fetch — the request didn't trigger a
        // refresh because the kid was already cached.
        Assert.Equal(1, counterHandler.CallCount);

        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WarmupFailFast_AbortsHostStartup_OnUnreachableEndpoint()
    {
        // No PqcFact requirement — this test exercises only the warmup
        // hosted service's startup contract, not the engine's crypto.
        var failingHandler = new FailingHandler();
        using var brokenHost = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddHttpClient<HttpPostQuantumJwtKeyRing>()
                    .ConfigurePrimaryHttpMessageHandler(() => failingHandler);
                services.AddSingleton<IPostQuantumJwtKeyRing>(sp =>
                {
                    var http = sp.GetRequiredService<IHttpClientFactory>()
                                 .CreateClient(nameof(HttpPostQuantumJwtKeyRing));
                    return new HttpPostQuantumJwtKeyRing(http, new Uri("https://keys.test/keys"));
                });
                services.AddPostQuantumJwtKeyRingWarmup(o => o.FailFastOnStartup = true);
            })
            .Build();

        await Assert.ThrowsAsync<HttpRequestException>(
            () => brokenHost.StartAsync(TestContext.Current.CancellationToken));
    }

    private sealed class CountingStubHandler(string responseBody) : HttpMessageHandler
    {
        private int _count;
        public int CallCount => Volatile.Read(ref _count);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _count);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("simulated unreachable endpoint");
    }
}
