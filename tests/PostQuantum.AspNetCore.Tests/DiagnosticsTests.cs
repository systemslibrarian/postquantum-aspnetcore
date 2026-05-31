using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;

namespace PostQuantum.AspNetCore.Tests;

/// <summary>
/// Locks the Metrics + ActivitySource signal contract. Subscribers (OpenTelemetry,
/// Prometheus, Application Insights, …) need stable signal names and tag
/// shapes; these tests fail noisily if the production code drops or renames
/// a signal.
/// </summary>
/// <remarks>
/// MeterListener and ActivityListener observe process-global signals; to
/// avoid racing other parallel auth-flow tests, each test uses a unique
/// scheme name and filters listener callbacks by that <c>scheme</c> tag.
/// </remarks>
public sealed class DiagnosticsTests
{
    [PqcFact]
    public async Task ValidToken_RecordsAuthSuccessCounter()
    {
        var scheme = NewScheme();
        var measurements = new ConcurrentBag<MeasurementRecord<long>>();
        using var listener = StartCounterListener<long>(
            "postquantum.jwt.auth.success",
            scheme,
            measurements);

        using var factory = new TestServerFactory(scheme);
        using var client = factory.CreateClient();
        var token = factory.MintToken();

        using var req = new HttpRequestMessage(HttpMethod.Get, "/me");
        req.Headers.Authorization = new("Bearer", token);
        using var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Single(measurements);
        Assert.Equal(1, measurements.Single().Value);
    }

    [PqcFact]
    public async Task TamperedToken_RecordsAuthFailureCounter_WithReasonTag()
    {
        var scheme = NewScheme();
        var measurements = new ConcurrentBag<MeasurementRecord<long>>();
        using var listener = StartCounterListener<long>(
            "postquantum.jwt.auth.failure",
            scheme,
            measurements);

        using var factory = new TestServerFactory(scheme);
        using var client = factory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, "/me");
        req.Headers.Authorization = new("Bearer", "junk.token.here");
        using var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.Single(measurements);
        Assert.Contains(measurements.Single().Tags, t => t.Key == "reason");
    }

    [PqcFact]
    public async Task ValidToken_RecordsLatencyHistogram()
    {
        var scheme = NewScheme();
        var measurements = new ConcurrentBag<MeasurementRecord<double>>();
        using var listener = StartCounterListener<double>(
            "postquantum.jwt.auth.latency",
            scheme,
            measurements);

        using var factory = new TestServerFactory(scheme);
        using var client = factory.CreateClient();
        var token = factory.MintToken();

        using var req = new HttpRequestMessage(HttpMethod.Get, "/me");
        req.Headers.Authorization = new("Bearer", token);
        using var resp = await client.SendAsync(req);

        Assert.Single(measurements);
        Assert.True(measurements.Single().Value >= 0);
        Assert.Contains(measurements.Single().Tags, t => t.Key == "result" && (string?)t.Value == "success");
    }

    [PqcFact]
    public async Task ValidToken_EmitsActivity()
    {
        var scheme = NewScheme();
        var activities = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == PostQuantumJwtBearerDiagnostics.InstrumentationName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = a =>
            {
                if ((string?)a.GetTagItem("scheme") == scheme)
                {
                    activities.Add(a);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);

        using var factory = new TestServerFactory(scheme);
        using var client = factory.CreateClient();
        var token = factory.MintToken();

        using var req = new HttpRequestMessage(HttpMethod.Get, "/me");
        req.Headers.Authorization = new("Bearer", token);
        using var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Single(activities);
        var activity = activities.Single();
        Assert.Equal("PostQuantumJwtBearer.Validate", activity.OperationName);
        Assert.Equal(ActivityStatusCode.Ok, activity.Status);
        Assert.Contains(activity.Tags, t => t.Key == "result" && t.Value == "success");
    }

    [PqcFact]
    public async Task TamperedToken_EmitsActivity_WithErrorStatus()
    {
        var scheme = NewScheme();
        var activities = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == PostQuantumJwtBearerDiagnostics.InstrumentationName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = a =>
            {
                if ((string?)a.GetTagItem("scheme") == scheme)
                {
                    activities.Add(a);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);

        using var factory = new TestServerFactory(scheme);
        using var client = factory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, "/me");
        req.Headers.Authorization = new("Bearer", "junk.token.here");
        using var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.Single(activities);
        Assert.Equal(ActivityStatusCode.Error, activities.Single().Status);
    }

    private static string NewScheme() => $"DiagTest-{Guid.NewGuid():N}";

    private static MeterListener StartCounterListener<T>(
        string instrumentName,
        string schemeFilter,
        ConcurrentBag<MeasurementRecord<T>> sink)
        where T : struct
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == PostQuantumJwtBearerDiagnostics.InstrumentationName &&
                    instrument.Name == instrumentName)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<T>((_, value, tags, _) =>
        {
            // Filter to only the test's scheme — other tests running in
            // parallel use different scheme names and are ignored.
            foreach (var tag in tags)
            {
                if (tag.Key == "scheme" && (string?)tag.Value == schemeFilter)
                {
                    sink.Add(new MeasurementRecord<T>(value, tags.ToArray()));
                    return;
                }
            }
        });
        listener.Start();
        return listener;
    }

    private readonly record struct MeasurementRecord<T>(T Value, KeyValuePair<string, object?>[] Tags);
}
