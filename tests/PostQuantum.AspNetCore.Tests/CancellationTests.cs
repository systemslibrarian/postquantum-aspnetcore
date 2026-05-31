using System.Net;
using System.Text;

namespace PostQuantum.AspNetCore.Tests;

/// <summary>
/// Caller-supplied cancellation must propagate through ResolveAsync.
/// The earlier version of HttpPostQuantumJwtKeyRing log-and-swallowed
/// TaskCanceledException unconditionally, swallowing genuine
/// cancellation as well — this locks the fix.
/// </summary>
public sealed class CancellationTests
{
    [Fact]
    public async Task ResolveAsync_PropagatesCallerCancellation()
    {
        // Stub that never completes — only the cancellation token can end
        // the call.
        using var handler = new HangingHandler();
        using var http = new HttpClient(handler);
        using var ring = new HttpPostQuantumJwtKeyRing(
            http, new Uri("https://keys.test/keys"));

        using var cts = new CancellationTokenSource();
        var resolveTask = ring.ResolveAsync("k1", cts.Token).AsTask();

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolveTask);
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Wait until the caller cancels. Throws OperationCanceledException
            // when the token fires — the production code path under test.
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{ \"keys\": [] }", Encoding.UTF8, "application/json"),
            };
        }
    }
}
