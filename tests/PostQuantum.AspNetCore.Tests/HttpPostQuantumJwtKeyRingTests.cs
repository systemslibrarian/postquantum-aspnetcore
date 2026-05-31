using System.Net;
using System.Security.Cryptography;
using System.Text;
using PostQuantum.Jwt;

namespace PostQuantum.AspNetCore.Tests;

/// <summary>
/// Unit tests for the HTTP-fetched key ring. Uses a tiny stub
/// <see cref="HttpMessageHandler"/> so the test never makes a real network
/// call.
/// </summary>
public sealed class HttpPostQuantumJwtKeyRingTests
{
    [PqcFact]
    public async Task ResolveAsync_FetchesAndCachesByKid()
    {
        using var signer = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65);
        var rawPublic = signer.ExportMLDsaPublicKey();
        var directoryJson =
            $$"""
            { "keys": [ { "kid": "k1", "alg": "ML-DSA-65", "key": "{{Convert.ToBase64String(rawPublic)}}" } ] }
            """;

        var stub = new StubHttpMessageHandler(directoryJson);
        using var http = new HttpClient(stub);
        using var ring = new HttpPostQuantumJwtKeyRing(
            http,
            new Uri("https://keys.test/keys"));

        var first = await ring.ResolveAsync("k1");
        var second = await ring.ResolveAsync("k1");

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(1, stub.CallCount); // second resolve hit cache
    }

    [PqcFact]
    public async Task ResolveAsync_ReturnsNullForUnknownKid_AfterRefresh()
    {
        var stub = new StubHttpMessageHandler("{ \"keys\": [] }");
        using var http = new HttpClient(stub);
        using var ring = new HttpPostQuantumJwtKeyRing(
            http,
            new Uri("https://keys.test/keys"));

        var key = await ring.ResolveAsync("does-not-exist");

        Assert.Null(key);
        Assert.Equal(1, stub.CallCount);
    }

    [PqcFact]
    public async Task ResolveAsync_IgnoresEntriesWithUnsupportedAlg()
    {
        // Single-suite policy: anything other than ML-DSA-65 is dropped on
        // the way in. Verifies the policy is enforced at the key-ring boundary.
        var directoryJson =
            """
            { "keys": [ { "kid": "k1", "alg": "RS256", "key": "AAAA" } ] }
            """;
        var stub = new StubHttpMessageHandler(directoryJson);
        using var http = new HttpClient(stub);
        using var ring = new HttpPostQuantumJwtKeyRing(
            http,
            new Uri("https://keys.test/keys"));

        var key = await ring.ResolveAsync("k1");

        Assert.Null(key);
    }

    [PqcFact]
    public void Resolve_ReturnsNull_ForNullOrEmptyKid()
    {
        var stub = new StubHttpMessageHandler("{ \"keys\": [] }");
        using var http = new HttpClient(stub);
        using var ring = new HttpPostQuantumJwtKeyRing(
            http,
            new Uri("https://keys.test/keys"));

        Assert.Null(ring.Resolve(null));
        Assert.Null(ring.Resolve(string.Empty));
        Assert.Equal(0, stub.CallCount); // never hits the network for a null kid
    }

    [PqcFact]
    public void Resolve_StaticAlg_MatchesEngineConstant()
    {
        // Locks in that we and the engine agree on the algorithm identifier
        // string. If this assert ever breaks, the wire-format contract has
        // drifted and HttpPostQuantumJwtKeyRing's single-suite policy is
        // silently filtering everything.
        Assert.Equal("ML-DSA-65", PqJwtAlgorithms.MLDsa65);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseBody;
        public int CallCount { get; private set; }

        public StubHttpMessageHandler(string responseBody)
        {
            _responseBody = responseBody;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json"),
            });
        }
    }
}
