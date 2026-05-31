using System.Net;
using Microsoft.AspNetCore.Authentication;

namespace PostQuantum.AspNetCore.Tests;

/// <summary>
/// Locks the OnMessageReceived contract: a hook that sets ctx.Token
/// substitutes the standard Authorization-header lookup, enabling
/// alternate transports (SignalR ?access_token=, signed cookies, …).
/// </summary>
public sealed class OnMessageReceivedTests
{
    [PqcFact]
    public async Task SetsToken_ShortCircuitsAuthorizationHeader()
    {
        using var factory = new TestServerFactory
        {
            ConfigureOptions = options =>
            {
                options.Events.OnMessageReceived = ctx =>
                {
                    var query = ctx.HttpContext.Request.Query["access_token"].ToString();
                    if (!string.IsNullOrEmpty(query))
                    {
                        ctx.Token = query;
                    }

                    return Task.CompletedTask;
                };
            },
        };
        using var client = factory.CreateClient();
        var token = factory.MintToken();

        // Token is in the query string, NOT the Authorization header.
        using var resp = await client.GetAsync($"/me?access_token={Uri.EscapeDataString(token)}");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [PqcFact]
    public async Task EmptyToken_FallsThroughToAuthorizationHeader()
    {
        using var factory = new TestServerFactory
        {
            ConfigureOptions = options =>
            {
                options.Events.OnMessageReceived = _ => Task.CompletedTask;
            },
        };
        using var client = factory.CreateClient();
        var token = factory.MintToken();

        // No query string token; standard header path must still work.
        using var req = new HttpRequestMessage(HttpMethod.Get, "/me");
        req.Headers.Authorization = new("Bearer", token);
        using var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [PqcFact]
    public async Task ResultNoResult_ShortCircuitsAuthorizationHeader()
    {
        using var factory = new TestServerFactory
        {
            ConfigureOptions = options =>
            {
                options.Events.OnMessageReceived = ctx =>
                {
                    ctx.Result = AuthenticateResult.NoResult();
                    return Task.CompletedTask;
                };
            },
        };
        using var client = factory.CreateClient();
        var token = factory.MintToken();

        // Even with a valid header, the event result must win and stop
        // the handler from continuing into the standard bearer lookup.
        using var req = new HttpRequestMessage(HttpMethod.Get, "/me");
        req.Headers.Authorization = new("Bearer", token);
        using var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
