using System.Diagnostics;
using Hangfire.Server;
using StreamingDigest.Application.Observability;

namespace StreamingDigest.Api.Observability;

public sealed class HangfireObservabilityFilter : IServerFilter
{
    public void OnPerforming(PerformingContext context)
    {
        var activity = CorrelationContext.BeginOperation(
            "hangfire.job",
            ActivityKind.Internal,
            new Dictionary<string, object?>
            {
                ["hangfire.job"] = context.BackgroundJob?.Job?.Type?.Name ?? "unknown",
                ["hangfire.method"] = context.BackgroundJob?.Job?.Method?.Name ?? "unknown",
                ["hangfire.queue"] = context.GetJobParameter<string>("Queue") ?? "default"
            });

        context.Items["observability.activity"] = activity;
    }

    public void OnPerformed(PerformedContext context)
    {
        if (context.Items.TryGetValue("observability.activity", out var value) && value is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
