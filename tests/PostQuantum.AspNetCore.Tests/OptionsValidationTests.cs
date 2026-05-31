using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PostQuantum.Jwt;

namespace PostQuantum.AspNetCore.Tests;

/// <summary>
/// Locks the startup-time validation contract: missing key sources surface
/// as a configuration exception when options are first resolved, not on
/// the first request.
/// </summary>
public sealed class OptionsValidationTests
{
    [Fact]
    public void Validate_Throws_WhenNoKeyOrResolverOrKeyRing()
    {
        var services = new ServiceCollection();
        services.AddAuthentication(PostQuantumJwtBearerDefaults.AuthenticationScheme)
            .AddPostQuantumJwtBearer(options =>
            {
                options.ValidationParameters = new PqJwtValidationParameters
                {
                    ValidIssuer = "https://issuer.example",
                    // No verification key, no resolver, no key ring.
                };
            });
        using var provider = services.BuildServiceProvider();

        var monitor = provider.GetRequiredService<IOptionsMonitor<PostQuantumJwtBearerOptions>>();

        // OptionsBuilder.Validate's lambda throws InvalidOperationException
        // straight through (it doesn't repackage as OptionsValidationException
        // when the validator itself throws rather than returns Fail).
        var ex = Assert.Throws<InvalidOperationException>(() =>
            monitor.Get(PostQuantumJwtBearerDefaults.AuthenticationScheme));

        Assert.Contains("source of verification keys", ex.Message, StringComparison.Ordinal);
    }

    [PqcFact]
    public void Validate_Passes_WithSignatureVerificationKey()
    {
        using var verifier = System.Security.Cryptography.MLDsa.GenerateKey(
            System.Security.Cryptography.MLDsaAlgorithm.MLDsa65);

        var services = new ServiceCollection();
        services.AddAuthentication(PostQuantumJwtBearerDefaults.AuthenticationScheme)
            .AddPostQuantumJwtBearer(options =>
            {
                options.ValidationParameters = new PqJwtValidationParameters
                {
                    SignatureVerificationKey = verifier,
                    ValidIssuer = "https://issuer.example",
                };
            });
        using var provider = services.BuildServiceProvider();

        var monitor = provider.GetRequiredService<IOptionsMonitor<PostQuantumJwtBearerOptions>>();
        var options = monitor.Get(PostQuantumJwtBearerDefaults.AuthenticationScheme);

        Assert.NotNull(options.ValidationParameters.SignatureVerificationKey);
    }

    [Fact]
    public void Validate_Passes_WithSignatureKeyResolver()
    {
        var services = new ServiceCollection();
        services.AddAuthentication(PostQuantumJwtBearerDefaults.AuthenticationScheme)
            .AddPostQuantumJwtBearer(options =>
            {
                options.ValidationParameters = new PqJwtValidationParameters
                {
                    SignatureKeyResolver = _ => null,
                    ValidIssuer = "https://issuer.example",
                };
            });
        using var provider = services.BuildServiceProvider();

        var monitor = provider.GetRequiredService<IOptionsMonitor<PostQuantumJwtBearerOptions>>();
        var options = monitor.Get(PostQuantumJwtBearerDefaults.AuthenticationScheme);

        Assert.NotNull(options.ValidationParameters.SignatureKeyResolver);
    }
}
