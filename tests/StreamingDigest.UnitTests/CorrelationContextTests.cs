using System.Diagnostics;
using StreamingDigest.Application.Observability;

namespace StreamingDigest.UnitTests;

public class CorrelationContextTests
{
    [Fact]
    public void BeginOperation_RecordsTraceAndSpanIds()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };

        ActivitySource.AddActivityListener(listener);

        using var operation = CorrelationContext.BeginOperation("unit-test", ActivityKind.Internal, new Dictionary<string, object?>
        {
            ["test.key"] = "value"
        });

        Assert.NotNull(Activity.Current);
        Assert.Equal(Activity.Current!.TraceId.ToString(), CorrelationContext.CurrentTraceId);
        Assert.Equal(Activity.Current.SpanId.ToString(), CorrelationContext.CurrentSpanId);
        Assert.Contains(Activity.Current.Tags, tag => tag.Key == "test.key" && tag.Value == "value");
    }

    [Fact]
    public async Task RunWithActivityAsync_ExposesCurrentActivityAndReturnsResult()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };

        ActivitySource.AddActivityListener(listener);

        var result = await CorrelationContext.RunWithActivityAsync(
            "unit-test-async",
            async activity =>
            {
                Assert.NotNull(activity);
                Assert.Equal(Activity.Current?.TraceId, activity!.TraceId);
                activity.SetTag("async.key", "value");
                await Task.Yield();
                return Activity.Current!.SpanId.ToString();
            },
            new Dictionary<string, object?>
            {
                ["async.key"] = "value"
            });

        Assert.NotNull(result);
        Assert.Null(Activity.Current);
    }
}
