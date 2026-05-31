using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PostQuantum.AspNetCore;
using PostQuantum.AspNetCore.SignalR.Demo;
using PostQuantum.Jwt;

// ---------------------------------------------------------------------------
// PostQuantum.AspNetCore — SignalR demo
//
// SignalR's WebSocket transport can't send custom headers from a browser, so
// the canonical pattern is `?access_token=…` on the connection URL.
// PostQuantum.AspNetCore.OnMessageReceived is the hook that pulls the token
// out of the query string when the request targets the hub.
//
// Try it:
//   dotnet run --project samples/PostQuantum.AspNetCore.SignalR.Demo
//   # browse to http://localhost:5050/ for the in-page client.
// ---------------------------------------------------------------------------

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5050");

var signingKey = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65);
var verificationKey = MLDsa.ImportMLDsaPublicKey(
    MLDsaAlgorithm.MLDsa65, signingKey.ExportMLDsaPublicKey());
builder.Services.AddSingleton(signingKey);
builder.Services.AddSingleton(verificationKey);

const string Issuer = "https://demo.postquantum.local";
const string Audience = "https://hub.demo.postquantum.local";

builder.Services
    .AddAuthentication(PostQuantumJwtBearerDefaults.AuthenticationScheme)
    .AddPostQuantumJwtBearer(options =>
    {
        options.ValidationParameters = new PqJwtValidationParameters
        {
            SignatureVerificationKey = verificationKey,
            ValidIssuer = Issuer,
            ValidAudience = Audience,
        };

        // The SignalR-specific bit: pull the token out of ?access_token=
        // when the request is destined for the hub. Standard
        // Authorization-header tokens still work everywhere else because
        // we only override the token source for hub paths.
        options.Events.OnMessageReceived = ctx =>
        {
            var accessToken = ctx.HttpContext.Request.Query["access_token"].ToString();
            var path = ctx.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) &&
                path.StartsWithSegments("/hubs/chat"))
            {
                ctx.Token = accessToken;
            }

            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddSignalR();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Content(Page.Index, "text/html"));

// Dev-only issuer. In a real app the token comes from your identity
// provider; DO NOT ship this endpoint as-is.
app.MapPost("/dev/token", (MLDsa signer, string? user) =>
{
    var token = new PqJwtBuilder()
        .WithIssuer(Issuer)
        .WithAudience(Audience)
        .WithSubject(user ?? "anon")
        .WithJwtId(Guid.NewGuid().ToString("N"))
        .WithLifetime(TimeSpan.FromMinutes(30))
        .WithClaim("role", "chat-user")
        .SignWith(signer)
        .Build();
    return Results.Ok(new { token });
});

app.MapHub<ChatHub>("/hubs/chat").RequireAuthorization();

app.Run();

namespace PostQuantum.AspNetCore.SignalR.Demo
{
    [Authorize]
    public sealed class ChatHub : Hub
    {
        public override async Task OnConnectedAsync()
        {
            var user = Context.User?.FindFirstValue("sub") ?? "unknown";
            await Clients.All.SendAsync("system", $"{user} joined.");
            await base.OnConnectedAsync();
        }

        public Task Send(string message)
        {
            var user = Context.User?.FindFirstValue("sub") ?? "unknown";
            return Clients.All.SendAsync("message", user, message);
        }
    }

    internal static class Page
    {
        public const string Index = """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <title>PostQuantum.AspNetCore SignalR demo</title>
  <style>
    body { font-family: system-ui, sans-serif; max-width: 640px; margin: 2rem auto; }
    fieldset { margin-bottom: 1rem; }
    #log { background:#f7f7f7; padding:0.75rem; border-radius:4px; height:240px; overflow-y:auto; white-space:pre-wrap; }
    input, button { font: inherit; }
    input[type=text] { width: 70%; }
  </style>
</head>
<body>
  <h1>PostQuantum.AspNetCore SignalR demo</h1>
  <p>This page mints a post-quantum JWT, opens a SignalR connection with
     <code>?access_token=…</code>, and exercises the
     <code>OnMessageReceived</code> handler in
     <code>PostQuantum.AspNetCore</code>.</p>

  <fieldset>
    <legend>1. Mint a token</legend>
    <label>Username: <input id="user" type="text" value="alice"></label>
    <button id="mint">Mint</button>
    <pre id="token-preview"></pre>
  </fieldset>

  <fieldset>
    <legend>2. Connect to the hub</legend>
    <button id="connect" disabled>Connect</button>
    <button id="disconnect" disabled>Disconnect</button>
  </fieldset>

  <fieldset>
    <legend>3. Send a message</legend>
    <input id="msg" type="text" placeholder="say hi…">
    <button id="send" disabled>Send</button>
  </fieldset>

  <h2>Log</h2>
  <div id="log"></div>

  <script src="https://cdnjs.cloudflare.com/ajax/libs/microsoft-signalr/8.0.7/signalr.min.js"></script>
  <script>
    const log = (line) => {
        const el = document.getElementById('log');
        el.textContent += line + '\n';
        el.scrollTop = el.scrollHeight;
    };

    let token = null;
    let connection = null;

    document.getElementById('mint').addEventListener('click', async () => {
        const user = document.getElementById('user').value;
        const resp = await fetch(`/dev/token?user=${encodeURIComponent(user)}`, { method: 'POST' });
        const json = await resp.json();
        token = json.token;
        document.getElementById('token-preview').textContent =
            token.slice(0, 60) + '...  (' + token.length + ' chars)';
        document.getElementById('connect').disabled = false;
        log('token minted (' + token.length + ' chars)');
    });

    document.getElementById('connect').addEventListener('click', async () => {
        connection = new signalR.HubConnectionBuilder()
            .withUrl('/hubs/chat', { accessTokenFactory: () => token })
            .build();

        connection.on('system', (msg) => log('[system] ' + msg));
        connection.on('message', (user, msg) => log(user + ': ' + msg));

        try {
            await connection.start();
            log('connected');
            document.getElementById('disconnect').disabled = false;
            document.getElementById('send').disabled = false;
        } catch (err) {
            log('connect failed: ' + err);
        }
    });

    document.getElementById('disconnect').addEventListener('click', async () => {
        if (connection) {
            await connection.stop();
            log('disconnected');
        }
        document.getElementById('disconnect').disabled = true;
        document.getElementById('send').disabled = true;
    });

    document.getElementById('send').addEventListener('click', async () => {
        const input = document.getElementById('msg');
        if (connection && input.value) {
            await connection.invoke('Send', input.value);
            input.value = '';
        }
    });
  </script>
</body>
</html>
""";
    }
}
