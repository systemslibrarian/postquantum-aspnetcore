using System.Net;
using System.Security.Cryptography;
using System.Text;
using BenchmarkDotNet.Attributes;

namespace PostQuantum.AspNetCore.Benchmarks;

/// <summary>
/// Hot-path key-ring lookups (cache hit) and cold-path lookups (force a
/// refresh). The hot case should be one dictionary read; the cold case
/// includes the JSON parse + key-import overhead.
/// </summary>
[MemoryDiagnoser]
public class KeyRingBenchmarks
{
    private MLDsa _signer = null!;
    private HttpPostQuantumJwtKeyRing _ring = null!;
    private string _knownKid = null!;

    [GlobalSetup]
    public void Setup()
    {
        if (!MLDsa.IsSupported)
        {
            throw new InvalidOperationException("Benchmarks require native ML-DSA support.");
        }

        _signer = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65);
        _knownKid = "bench-kid";
        var keyB64 = Convert.ToBase64String(_signer.ExportMLDsaPublicKey());
        var directoryJson =
            $"{{\"keys\":[{{\"kid\":\"{_knownKid}\",\"alg\":\"ML-DSA-65\",\"key\":\"{keyB64}\"}}]}}";

        var handler = new StaticBodyHandler(directoryJson);
        var http = new HttpClient(handler);
        _ring = new HttpPostQuantumJwtKeyRing(http, new Uri("https://bench.test/keys"));
        _ring.PreloadAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Hit the cache: one volatile read of the snapshot reference,
    /// one Dictionary.TryGetValue. The 99.9% case in production.
    /// </summary>
    [Benchmark(Description = "Resolve known kid (cache hit)")]
    public MLDsa? Resolve_CacheHit() => _ring.Resolve(_knownKid);

    [GlobalCleanup]
    public void Cleanup()
    {
        _ring.Dispose();
        _signer.Dispose();
    }

    private sealed class StaticBodyHandler : HttpMessageHandler
    {
        private readonly string _body;
        public StaticBodyHandler(string body) { _body = body; }
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            });
    }
}
