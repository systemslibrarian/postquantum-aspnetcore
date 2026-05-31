using System.Security.Cryptography;
using PostQuantum.AspNetCore;
using PostQuantum.AspNetCore.Mvc.Demo;
using PostQuantum.Jwt;

// ---------------------------------------------------------------------------
// PostQuantum.AspNetCore — MVC controller-based demo
//
// Shows the classic ASP.NET Core MVC pattern (controllers, [Authorize] attribute,
// role + policy-based authorization) backed by post-quantum JWT authentication.
//
// Try it:
//   dotnet run --project samples/PostQuantum.AspNetCore.Mvc.Demo
//   # browse http://localhost:5100/
// ---------------------------------------------------------------------------

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5100");

var signingKey = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65);
var verificationKey = MLDsa.ImportMLDsaPublicKey(
    MLDsaAlgorithm.MLDsa65, signingKey.ExportMLDsaPublicKey());
builder.Services.AddSingleton(signingKey);

builder.Services
    .AddAuthentication(PostQuantumJwtBearerDefaults.AuthenticationScheme)
    .AddPostQuantumJwtBearer(options =>
    {
        options.ValidationParameters = new PqJwtValidationParameters
        {
            SignatureVerificationKey = verificationKey,
            ValidIssuer = "https://mvc-demo.local",
            ValidAudience = "https://mvc-demo.local/api",
        };
    });

// Authorization combinations the controllers exercise:
//   - default: just RequireAuthenticatedUser (the [Authorize] attribute)
//   - "Admin" role check via [Authorize(Roles = "admin")]
//   - "AcmeTenant" policy via [Authorize(Policy = "AcmeTenant")]
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AcmeTenant", policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("tenant", "acme"));
});

builder.Services.AddControllers();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Tiny landing page so the sample is one-process and easy to drive.
app.MapGet("/", () => Results.Content(LandingPage.Html, "text/html"));

app.Run();
