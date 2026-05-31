using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;
using PostQuantum.Jwt;

namespace PostQuantum.AspNetCore.Benchmarks;

/// <summary>
/// Throughput + allocation profile of <see cref="PqJwtValidator.Validate(string)"/>
/// — the hot path inside <c>PostQuantumJwtBearerHandler</c>.
/// </summary>
[MemoryDiagnoser]
public class TokenValidationBenchmarks
{
    private MLDsa _signer = null!;
    private MLDsa _verifier = null!;
    private PqJwtValidator _validator = null!;
    private string _signedToken = null!;

    [GlobalSetup]
    public void Setup()
    {
        if (!MLDsa.IsSupported)
        {
            throw new InvalidOperationException(
                "Benchmarks require a runtime with native ML-DSA support (.NET 10 on OpenSSL 3.5+ or recent Windows).");
        }

        _signer = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65);
        _verifier = MLDsa.ImportMLDsaPublicKey(
            MLDsaAlgorithm.MLDsa65, _signer.ExportMLDsaPublicKey());

        _validator = new PqJwtValidator(new PqJwtValidationParameters
        {
            SignatureVerificationKey = _verifier,
            ValidIssuer = "https://benchmark.issuer",
            ValidAudience = "https://benchmark.audience",
        });

        _signedToken = new PqJwtBuilder()
            .WithIssuer("https://benchmark.issuer")
            .WithAudience("https://benchmark.audience")
            .WithSubject("bench-user")
            .WithJwtId(Guid.NewGuid().ToString("N"))
            .WithLifetime(TimeSpan.FromHours(1))
            .WithClaim("role", "bench")
            .SignWith(_signer)
            .Build();
    }

    /// <summary>
    /// End-to-end token validation: signature verify + claim parse +
    /// issuer/audience/expiration checks. This is the cost of one
    /// authenticated request inside <c>PostQuantumJwtBearerHandler</c>.
    /// </summary>
    [Benchmark(Description = "PqJwtValidator.Validate, signed-only token")]
    public PqJwtValidationResult Validate_SignedOnly() => _validator.Validate(_signedToken);

    [GlobalCleanup]
    public void Cleanup()
    {
        _signer.Dispose();
        _verifier.Dispose();
    }
}
