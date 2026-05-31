using Microsoft.Extensions.Logging;

namespace PostQuantum.AspNetCore;

/// <summary>
/// Source-generated logger messages — zero-allocation, AOT-friendly, satisfies CA1848.
/// </summary>
internal static partial class Logging
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        Message = "Post-quantum JWT validation failed.")]
    public static partial void ValidationFailed(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "Post-quantum JWT key ring fetch from {Endpoint} returned an empty document.")]
    public static partial void KeyRingEmpty(this ILogger logger, Uri endpoint);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "Post-quantum JWT key ring entry {Kid} skipped due to malformed key material.")]
    public static partial void KeyRingEntryMalformed(this ILogger logger, Exception exception, string kid);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Warning,
        Message = "Post-quantum JWT key ring fetch from {Endpoint} failed.")]
    public static partial void KeyRingFetchFailed(this ILogger logger, Exception exception, Uri endpoint);
}
