using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PostQuantum.AspNetCore;

/// <summary>
/// Diagnostic primitives — <see cref="ActivitySource"/> and
/// <see cref="Meter"/> — emitted by the post-quantum JWT bearer handler
/// and the HTTP key ring. Consumers wire these up to OpenTelemetry,
/// Application Insights, Prometheus exporters, or whatever observability
/// stack they prefer.
/// </summary>
public static class PostQuantumJwtBearerDiagnostics
{
    /// <summary>
    /// The instrumentation name used by both the
    /// <see cref="ActivitySource"/> and the <see cref="Meter"/>. Subscribe to
    /// this name in your OpenTelemetry / metrics configuration to receive
    /// signals from the handler.
    /// </summary>
    public const string InstrumentationName = "PostQuantum.AspNetCore";

    /// <summary>
    /// Schema version reported alongside emitted signals. Bumped whenever
    /// signal names or attribute shapes change in a way exporters need to
    /// notice; otherwise stable.
    /// </summary>
    public const string InstrumentationVersion = "0.5.0";

    internal static readonly ActivitySource ActivitySource = new(InstrumentationName, InstrumentationVersion);

    internal static readonly Meter Meter = new(InstrumentationName, InstrumentationVersion);

    /// <summary>
    /// Counter: tokens that successfully validated.
    /// Tags: <c>scheme</c> (string).
    /// </summary>
    internal static readonly Counter<long> AuthSuccess = Meter.CreateCounter<long>(
        "postquantum.jwt.auth.success",
        unit: "{token}",
        description: "Tokens that successfully validated and produced a ClaimsPrincipal.");

    /// <summary>
    /// Counter: tokens that failed validation.
    /// Tags: <c>scheme</c> (string), <c>reason</c> (string — exception type short name).
    /// </summary>
    internal static readonly Counter<long> AuthFailure = Meter.CreateCounter<long>(
        "postquantum.jwt.auth.failure",
        unit: "{token}",
        description: "Tokens that failed validation. Tagged with the exception type short name so dashboards can break down by reason.");

    /// <summary>
    /// Histogram: end-to-end token-validation latency, in milliseconds.
    /// Tags: <c>scheme</c> (string), <c>result</c> ("success" or "failure").
    /// </summary>
    internal static readonly Histogram<double> AuthLatency = Meter.CreateHistogram<double>(
        "postquantum.jwt.auth.latency",
        unit: "ms",
        description: "End-to-end token validation latency from PqJwtValidator.Validate.");

    /// <summary>
    /// Counter: key-ring resolve operations.
    /// Tags: <c>result</c> ("hit", "miss", "refresh-hit", "refresh-miss").
    /// </summary>
    internal static readonly Counter<long> KeyRingResolve = Meter.CreateCounter<long>(
        "postquantum.jwt.keyring.resolve",
        unit: "{lookup}",
        description: "Key-ring lookups. 'hit' means a cached key; 'miss' means a null return; the 'refresh-*' variants indicate a fetch was triggered.");

    /// <summary>
    /// Histogram: HTTP key-directory fetch latency, in milliseconds.
    /// Tags: <c>result</c> ("success" or "failure").
    /// </summary>
    internal static readonly Histogram<double> KeyRingFetchLatency = Meter.CreateHistogram<double>(
        "postquantum.jwt.keyring.fetch.latency",
        unit: "ms",
        description: "HTTP key-directory fetch latency, including JSON parse and key import.");
}
