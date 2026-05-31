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

    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Information,
        Message = "Post-quantum JWT key ring warmup completed.")]
    public static partial void WarmupSucceeded(this ILogger logger);

    [LoggerMessage(
        EventId = 6,
        Level = LogLevel.Error,
        Message = "Post-quantum JWT key ring warmup failed; host startup will fail (FailFastOnStartup=true).")]
    public static partial void WarmupFailedFailFast(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 7,
        Level = LogLevel.Warning,
        Message = "Post-quantum JWT key ring warmup failed; continuing best-effort (FailFastOnStartup=false). Cache will populate on first miss.")]
    public static partial void WarmupFailedBestEffort(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 8,
        Level = LogLevel.Warning,
        Message = "Post-quantum JWT key ring periodic refresh tick failed.")]
    public static partial void PeriodicRefreshFailed(this ILogger logger, Exception exception);
}
