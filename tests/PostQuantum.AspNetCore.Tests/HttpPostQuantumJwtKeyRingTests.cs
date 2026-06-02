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
    public async Task PreloadAsync_EvictsKidsRemovedFromUpstream()
    {
        // ChatGPT review item 1: a kid that disappears from the upstream
        // directory must not survive in the cache after a refresh. Locks
        // the atomic-snapshot-swap behaviour against a regression to
        // additive-only updates.
        //
        // Note: ResolveAsync only triggers a refresh on a cache *miss*; a
        // known kid is returned from cache without a freshness check, by
        // design (the hot path stays off the network). Eviction therefore
        // happens on the next refresh — driven by either a miss on an
        // unknown kid or an explicit PreloadAsync from a background
        // service / hosted task. This test exercises the explicit path;
        // the next test covers eviction via a miss.
        using var signer1 = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65);
        using var signer2 = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65);
        var k1 = Convert.ToBase64String(signer1.ExportMLDsaPublicKey());
        var k2 = Convert.ToBase64String(signer2.ExportMLDsaPublicKey());

        var initial = "{ \"keys\": [" +
            $"{{ \"kid\": \"k1\", \"alg\": \"ML-DSA-65\", \"key\": \"{k1}\" }}," +
            $"{{ \"kid\": \"k2\", \"alg\": \"ML-DSA-65\", \"key\": \"{k2}\" }}" +
            "] }";
        var after = "{ \"keys\": [" +
            $"{{ \"kid\": \"k2\", \"alg\": \"ML-DSA-65\", \"key\": \"{k2}\" }}" +
            "] }";

        var stub = new ScriptedHandler(initial, after);
        using var http = new HttpClient(stub);
        // Refresh interval long enough that the second PreloadAsync would
        // normally be a no-op; the test relies on the 10-second forced
        // throttle being passed (Thread.Sleep beats it).
        using var ring = new HttpPostQuantumJwtKeyRing(
            http, new Uri("https://keys.test/keys"),
            refreshInterval: TimeSpan.FromMinutes(5));

        await ring.PreloadAsync();
        Assert.NotNull(await ring.ResolveAsync("k1"));
        Assert.NotNull(await ring.ResolveAsync("k2"));

        // Upstream rotates: k1 is removed.
        stub.Advance();

        // Wait past the 10-second forced-refresh throttle so PreloadAsync
        // re-fetches. (Time provider stub would be cleaner — left for v0.4.)
        await Task.Delay(TimeSpan.FromSeconds(11));
        await ring.PreloadAsync();

        Assert.NotNull(await ring.ResolveAsync("k2"));
        Assert.Null(await ring.ResolveAsync("k1"));
    }

    [PqcFact]
    public async Task UnknownKidMiss_TriggersRefresh_WhichEvictsRemovedKid()
    {
        // Eviction via the more common path: a miss for an unknown kid
        // forces a refresh, which atomically swaps in the new snapshot
        // that no longer contains the removed kid.
        using var signer1 = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65);
        using var signer2 = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65);
        var k1 = Convert.ToBase64String(signer1.ExportMLDsaPublicKey());
        var k2 = Convert.ToBase64String(signer2.ExportMLDsaPublicKey());

        var initial = "{ \"keys\": [" +
            $"{{ \"kid\": \"k1\", \"alg\": \"ML-DSA-65\", \"key\": \"{k1}\" }}," +
            $"{{ \"kid\": \"k2\", \"alg\": \"ML-DSA-65\", \"key\": \"{k2}\" }}" +
            "] }";
        var after = "{ \"keys\": [" +
            $"{{ \"kid\": \"k2\", \"alg\": \"ML-DSA-65\", \"key\": \"{k2}\" }}" +
            "] }";

        var stub = new ScriptedHandler(initial, after);
        using var http = new HttpClient(stub);
        using var ring = new HttpPostQuantumJwtKeyRing(
            http, new Uri("https://keys.test/keys"));

        // Prime the cache.
        Assert.NotNull(await ring.ResolveAsync("k1"));
        Assert.NotNull(await ring.ResolveAsync("k2"));

        // Upstream removes k1.
        stub.Advance();

        // Pass the 10-second forced-refresh throttle so the unknown-kid
        // miss can actually fetch.
        await Task.Delay(TimeSpan.FromSeconds(11));

        // An unknown kid miss forces a refresh — and the swap evicts k1.
        Assert.Null(await ring.ResolveAsync("never-existed"));

        // The next resolve sees the post-swap snapshot.
        Assert.NotNull(await ring.ResolveAsync("k2"));
        Assert.Null(await ring.ResolveAsync("k1"));
    }

    [PqcFact]
    public async Task ResolveAsync_ThrottlesRepeatedUnknownKidRefreshes()
    {
        // ChatGPT review item 3: an attacker sending a flood of random
        // kids must not turn into N back-to-back fetches against the key
        // endpoint. Within the 10s "force refresh" throttle window, a
        // burst of unknown kids should fan out to a single fetch.
        var stub = new StubHttpMessageHandler("{ \"keys\": [] }");
        using var http = new HttpClient(stub);
        using var ring = new HttpPostQuantumJwtKeyRing(
            http, new Uri("https://keys.test/keys"));

        for (var i = 0; i < 25; i++)
        {
            Assert.Null(await ring.ResolveAsync($"random-{i}"));
        }

        // First miss triggers a fetch; the rest fall inside the throttle
        // window. Exact upper bound is 1, but allow a small slack in case
        // the throttle window edge fires once more.
        Assert.InRange(stub.CallCount, 1, 2);
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

    [PqcFact]
    public void Resolve_UnknownKidFlood_SameKid_NegativeCacheShortCircuits()
    {
        // Finding 3: under sustained random-kid flooding the sync Resolve
        // path must not enter the sync-over-async bridge or _refreshLock
        // on every miss. After the first refresh-miss confirms the kid is
        // absent, subsequent Resolve calls for the same kid should
        // short-circuit via the negative cache — same call, same answer,
        // no additional fetch.
        var stub = new StubHttpMessageHandler("{ \"keys\": [] }");
        using var http = new HttpClient(stub);
        using var ring = new HttpPostQuantumJwtKeyRing(
            http, new Uri("https://keys.test/keys"));

        // First miss does a fetch (refresh-miss) and stamps the negative cache.
        Assert.Null(ring.Resolve("ghost-kid"));
        Assert.Equal(1, stub.CallCount);

        // 200 subsequent misses for the same kid: short-circuit via negative
        // cache. No additional HTTP calls; no _refreshLock contention.
        for (var i = 0; i < 200; i++)
        {
            Assert.Null(ring.Resolve("ghost-kid"));
        }
        Assert.Equal(1, stub.CallCount);
    }

    [PqcFact]
    public async Task Resolve_NegativeCache_BoundedSize_DoesNotGrowUnbounded()
    {
        // Finding 3: bound the memory footprint of the negative cache.
        // Even when a flood of unique random kids each populates the cache,
        // its size must stay at or below the documented cap (1024 entries).
        var stub = new StubHttpMessageHandler("{ \"keys\": [] }");
        using var http = new HttpClient(stub);
        using var ring = new HttpPostQuantumJwtKeyRing(
            http, new Uri("https://keys.test/keys"));

        // Flood with N unique kids, N > cap. ResolveAsync drives
        // RememberMissing on each refresh-miss; the throttle inside
        // RefreshAsync short-circuits the actual fetch after the first
        // one, but the negative-cache stamp still happens for every kid.
        const int Flood = 4000;
        for (var i = 0; i < Flood; i++)
        {
            Assert.Null(await ring.ResolveAsync($"random-{i:D6}"));
        }

        // After 4× the cap of unique kids, the bounded eviction must keep
        // the cache at or below the documented maximum.
        Assert.InRange(ring.DebugNegativeCacheCount, 0, 1024);
    }

    [PqcFact]
    public async Task Resolve_KidBecomesValid_NotWronglyRejectedPastTtlWindow()
    {
        // Finding 3 — bound verification: a kid which becomes valid in
        // the upstream directory between two misses must be resolvable
        // once the 10s negative-cache TTL (= forced-refresh throttle
        // window) elapses, and must NOT be wrongly rejected past that
        // window. On the refresh-hit, the handler must also clear the
        // stale negative entry so subsequent calls hit the cache cleanly.
        using var signer = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65);
        var k1Bytes = Convert.ToBase64String(signer.ExportMLDsaPublicKey());

        var emptyDirectory = "{ \"keys\": [] }";
        var withK1 = "{ \"keys\": [" +
            $"{{ \"kid\": \"late-kid\", \"alg\": \"ML-DSA-65\", \"key\": \"{k1Bytes}\" }}" +
            "] }";

        var stub = new ScriptedHandler(emptyDirectory, withK1);
        using var http = new HttpClient(stub);
        using var ring = new HttpPostQuantumJwtKeyRing(
            http, new Uri("https://keys.test/keys"));

        // Phase 1: kid is absent. Miss populates the negative cache.
        Assert.Null(await ring.ResolveAsync("late-kid"));
        Assert.Equal(1, ring.DebugNegativeCacheCount);

        // Phase 2: a Resolve inside the 10s window short-circuits to null.
        // (We assert on the sync path so the short-circuit, not just the
        // throttle, is what produced the null.)
        Assert.Null(ring.Resolve("late-kid"));
        Assert.Equal(1, stub.CallCount); // still no additional fetch

        // Phase 3: upstream now serves the kid. Advance the script and
        // wait past the 10s window (matches both NegativeCacheTtl and
        // the forced-refresh throttle).
        stub.Advance();
        await Task.Delay(TimeSpan.FromSeconds(11));

        // Phase 4: kid is now resolvable. The negative-cache TTL has
        // expired, the forced-refresh throttle has passed, the refresh
        // fetches the new directory, the refresh-hit branch returns the
        // key AND clears the stale negative entry. The bound on wrong
        // rejection — 10s, equal to the pre-existing forced-refresh
        // throttle window — holds.
        var key = await ring.ResolveAsync("late-kid");
        Assert.NotNull(key);
        Assert.Equal(0, ring.DebugNegativeCacheCount); // refresh-hit cleared it
    }

    [PqcFact]
    public async Task Fetch_OversizeBody_RejectedByMaxResponseBufferSize()
    {
        // Finding 2: a hostile or compromised key endpoint must not be
        // able to drive memory pressure by returning a very large JSON
        // body. The DI helper applies HttpClient.MaxResponseContentBufferSize
        // = 1 MB by default; the manual constructor accepts an explicit
        // maxResponseBytes parameter. Either way, an oversize body
        // surfaces as HttpRequestException and fails closed.
        // Generate a body larger than the cap. Content is junk; the size
        // is what matters for this test.
        var body = "{ \"keys\": [" + new string(' ', 4096) + "] }";
        var stub = new StubHttpMessageHandler(body);
        using var http = new HttpClient(stub);
        using var ring = new HttpPostQuantumJwtKeyRing(
            http,
            new Uri("https://keys.test/keys"),
            maxResponseBytes: 1024); // cap < body size → reject

        // PreloadAsync uses throwOnFailure=true, so an oversize body
        // surfaces directly to the caller rather than being swallowed.
        await Assert.ThrowsAsync<HttpRequestException>(() => ring.PreloadAsync());
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

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly string[] _responses;
        private int _index;
        public int CallCount { get; private set; }

        public ScriptedHandler(params string[] responses)
        {
            _responses = responses;
        }

        public void Advance()
        {
            _index = Math.Min(_index + 1, _responses.Length - 1);
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responses[_index], Encoding.UTF8, "application/json"),
            });
        }
    }
}
