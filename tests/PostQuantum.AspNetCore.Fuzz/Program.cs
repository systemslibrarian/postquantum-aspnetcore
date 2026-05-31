using System.Security.Cryptography;
using System.Text;
using PostQuantum.Jwt;
using SharpFuzz;

// ---------------------------------------------------------------------------
// PostQuantum.AspNetCore fuzz target — coverage-guided fuzzing via SharpFuzz
// + libfuzzer-dotnet. See README.md in this project for the workflow.
//
// Target: PqJwtValidator.Validate(string). The most interesting code path in
// the stack — owns header parse, payload parse, signature verify, claim
// binding. Other surfaces (HeaderEncoding helpers, the handler) are exercised
// by the in-process fuzz in PostQuantum.AspNetCore.Tests/FuzzTests.cs.
//
// Contract: the only acceptable exception classes are PqJwtException + its
// subclasses, plus the known engine leaks (FormatException for bad Base64,
// CryptographicException for malformed key material). Anything else escaping
// is a fail-open bug that libfuzzer-dotnet will catch and shrink to a minimal
// reproducer.
// ---------------------------------------------------------------------------

if (!MLDsa.IsSupported)
{
    Console.Error.WriteLine("Fuzz target requires ML-DSA support (net10 + OpenSSL 3.5+).");
    return 1;
}

using var key = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65);
var validator = new PqJwtValidator(new PqJwtValidationParameters
{
    SignatureVerificationKey = key,
    ValidIssuer = "https://fuzz.issuer",
    ValidAudience = "https://fuzz.audience",
});

Fuzzer.Run(stream =>
{
    // SharpFuzz hands us a Stream per iteration. Read it as bytes,
    // decode as UTF-8 (invalid sequences = skip — those aren't tokens
    // either), and feed to the validator.
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    var bytes = ms.ToArray();

    string asString;
    try { asString = Encoding.UTF8.GetString(bytes); }
    catch { return; }

    try
    {
        validator.Validate(asString);
    }
    catch (PqJwtException)
    {
        // Expected fail-closed: PqJwtValidationException + family.
    }
    catch (FormatException)
    {
        // Known engine leak (bad Base64). Tracked; v0.7 engine-side wrap
        // will fold this into PqJwtException.
    }
    catch (CryptographicException)
    {
        // Known engine leak for malformed key material in encrypted-token paths.
    }
    catch (OperationCanceledException)
    {
        // Acceptable; no cancellation should fire here but tolerate it.
    }
    // Anything else escapes → libfuzzer-dotnet catches the crash.
});

return 0;
