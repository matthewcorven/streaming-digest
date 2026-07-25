using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace StreamingDigest.Application.Observability;

public static class CorrelationContext
{
    public const string ActivitySourceName = "StreamingDigest.Application";
    public const string TraceIdTagName = "streamingdigest.trace_id";
    public const string SpanIdTagName = "streamingdigest.span_id";

    private static readonly ActivitySource ActivitySourceInstance = new(ActivitySourceName);

    public static ActivitySource ActivitySource => ActivitySourceInstance;

    public static string? CurrentTraceId => Activity.Current?.TraceId.ToString();

    public static string? CurrentSpanId => Activity.Current?.SpanId.ToString();

    public static IDisposable BeginOperation(string operationName, ActivityKind kind = ActivityKind.Internal, IEnumerable<KeyValuePair<string, object?>>? tags = null)
    {
        var activity = ActivitySourceInstance.StartActivity(operationName, kind);
        if (activity is null)
        {
            return new NoopScope();
        }

        activity.SetTag(TraceIdTagName, activity.TraceId.ToString());
        activity.SetTag(SpanIdTagName, activity.SpanId.ToString());

        if (tags is not null)
        {
            foreach (var tag in tags)
            {
                activity.SetTag(tag.Key, tag.Value?.ToString());
            }
        }

        return new ActivityScope(activity);
    }

    public static IDisposable BeginLoggingScope(ILogger logger, IEnumerable<KeyValuePair<string, object?>>? tags = null)
    {
        var state = new Dictionary<string, object?>
        {
            ["trace_id"] = CurrentTraceId,
            ["span_id"] = CurrentSpanId
        };

        if (tags is not null)
        {
            foreach (var tag in tags)
            {
                state[tag.Key] = tag.Value;
            }
        }

        return logger.BeginScope(state);
    }

    private sealed class ActivityScope : IDisposable
    {
        private readonly Activity? _activity;
        private bool _disposed;

        public ActivityScope(Activity? activity)
        {
            _activity = activity;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _activity?.Stop();
        }
    }

    private sealed class NoopScope : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
