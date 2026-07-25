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
}
