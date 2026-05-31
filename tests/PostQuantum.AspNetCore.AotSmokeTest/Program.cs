using System.Security.Cryptography;
using PostQuantum.AspNetCore;
using PostQuantum.Jwt;

// AOT smoke test: a minimal consuming app that exercises the public
// surface this library promises is AOT-safe. The point isn't to run —
// `dotnet publish -p:PublishAot=true` building cleanly with
// TreatWarningsAsErrors is the assertion. If a future change in the
// library introduces an unannotated reflection path, the AOT publish
// here will surface it as an IL/Trim warning → error.

var builder = WebApplication.CreateBuilder(args);

using var verifier = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65);

builder.Services
    .AddAuthentication(PostQuantumJwtBearerDefaults.AuthenticationScheme)
    .AddPostQuantumJwtBearer(options =>
    {
        options.ValidationParameters = new PqJwtValidationParameters
        {
            SignatureVerificationKey = verifier,
            ValidIssuer = "https://issuer.example",
            ValidAudience = "https://api.example",
        };

        options.Events.OnMessageReceived = _ => Task.CompletedTask;
        options.Events.OnTokenValidated = _ => Task.CompletedTask;
        options.Events.OnAuthenticationFailed = _ => Task.CompletedTask;
        options.Events.OnChallenge = _ => Task.CompletedTask;
    });

builder.Services.AddPostQuantumJwtKeyRing(new Uri("https://keys.example/keys"));
builder.Services.AddPostQuantumJwtKeyRingWarmup(options =>
{
    options.FailFastOnStartup = false;
});
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/me", () => "ok").RequireAuthorization();

// We intentionally don't Run() — the app doesn't need to serve traffic
// for the AOT smoke test. Construction + the AOT publish covers the
// linker analysis we care about.
Console.WriteLine("AOT smoke test built successfully.");
