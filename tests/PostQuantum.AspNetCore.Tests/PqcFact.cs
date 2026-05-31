using System.Security.Cryptography;

namespace PostQuantum.AspNetCore.Tests;

/// <summary>
/// A <see cref="FactAttribute"/> that skips the test (with a stated reason)
/// when the host runtime lacks the native ML-DSA primitive. Mirrors the
/// engine repository's discipline: "a test that can't run its crypto skips
/// with a reason, never silently passes."
/// </summary>
public sealed class PqcFactAttribute : FactAttribute
{
    public PqcFactAttribute()
    {
        if (!MLDsa.IsSupported)
        {
            Skip = "ML-DSA not supported on this host (need .NET 10 BCL on OpenSSL 3.5+ or recent Windows).";
        }
    }
}
