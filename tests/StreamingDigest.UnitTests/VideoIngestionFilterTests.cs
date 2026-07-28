using StreamingDigest.Application;

namespace StreamingDigest.UnitTests;

public sealed class VideoIngestionFilterTests
{
    // ── ClassifyIsLongForm ────────────────────────────────────────────────────

    [Fact]
    public void ClassifyIsLongForm_returns_true_when_duration_is_null()
    {
        // Unknown duration — assume long-form until proven otherwise
        var result = VideoIngestionFilter.ClassifyIsLongForm(null, minDurationSeconds: 61);
        Assert.True(result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(60)]
    public void ClassifyIsLongForm_returns_false_when_duration_is_below_threshold(int durationSeconds)
    {
        var result = VideoIngestionFilter.ClassifyIsLongForm(durationSeconds, minDurationSeconds: 61);
        Assert.False(result);
    }

    [Fact]
    public void ClassifyIsLongForm_returns_true_when_duration_equals_threshold()
    {
        var result = VideoIngestionFilter.ClassifyIsLongForm(61, minDurationSeconds: 61);
        Assert.True(result);
    }

    [Theory]
    [InlineData(62)]
    [InlineData(630)]   // PT10M30S — a typical long video
    [InlineData(3600)]  // 1 hour
    [InlineData(7200)]  // 2 hours
    public void ClassifyIsLongForm_returns_true_when_duration_exceeds_threshold(int durationSeconds)
    {
        var result = VideoIngestionFilter.ClassifyIsLongForm(durationSeconds, minDurationSeconds: 61);
        Assert.True(result);
    }

    [Fact]
    public void ClassifyIsLongForm_respects_custom_threshold()
    {
        // Threshold of 300s (5 min): a 299s video should be short-form
        Assert.False(VideoIngestionFilter.ClassifyIsLongForm(299, minDurationSeconds: 300));

        // A 300s video should be long-form
        Assert.True(VideoIngestionFilter.ClassifyIsLongForm(300, minDurationSeconds: 300));
    }

    [Fact]
    public void ClassifyIsLongForm_returns_true_when_threshold_is_zero()
    {
        // All non-null durations pass when threshold is 0
        Assert.True(VideoIngestionFilter.ClassifyIsLongForm(0, minDurationSeconds: 0));
        Assert.True(VideoIngestionFilter.ClassifyIsLongForm(1, minDurationSeconds: 0));
    }

    // ── ComputePublishedAfterCutoff ───────────────────────────────────────────

    [Fact]
    public void ComputePublishedAfterCutoff_uses_channel_override_when_present()
    {
        var now = new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);
        var cutoff = VideoIngestionFilter.ComputePublishedAfterCutoff(now, channelMaxAgeDays: 7, globalDefaultMaxAgeDays: 30);

        var expected = now.AddDays(-7);
        Assert.Equal(expected, cutoff);
    }

    [Fact]
    public void ComputePublishedAfterCutoff_uses_global_default_when_channel_override_is_null()
    {
        var now = new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);
        var cutoff = VideoIngestionFilter.ComputePublishedAfterCutoff(now, channelMaxAgeDays: null, globalDefaultMaxAgeDays: 30);

        var expected = now.AddDays(-30);
        Assert.Equal(expected, cutoff);
    }

    [Fact]
    public void ComputePublishedAfterCutoff_channel_override_of_zero_pins_cutoff_to_now()
    {
        var now = new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);
        var cutoff = VideoIngestionFilter.ComputePublishedAfterCutoff(now, channelMaxAgeDays: 0, globalDefaultMaxAgeDays: 30);

        Assert.Equal(now, cutoff);
    }

    [Fact]
    public void ComputePublishedAfterCutoff_channel_override_takes_precedence_over_global_default()
    {
        var now = new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

        // Channel override of 90 days vs. global default of 30 days
        var cutoffWithOverride = VideoIngestionFilter.ComputePublishedAfterCutoff(now, channelMaxAgeDays: 90, globalDefaultMaxAgeDays: 30);
        var cutoffWithGlobal = VideoIngestionFilter.ComputePublishedAfterCutoff(now, channelMaxAgeDays: null, globalDefaultMaxAgeDays: 30);

        Assert.NotEqual(cutoffWithOverride, cutoffWithGlobal);
        Assert.Equal(now.AddDays(-90), cutoffWithOverride);
        Assert.Equal(now.AddDays(-30), cutoffWithGlobal);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(14)]
    [InlineData(30)]
    [InlineData(90)]
    [InlineData(365)]
    public void ComputePublishedAfterCutoff_produces_correct_offset_for_various_day_counts(int days)
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var cutoff = VideoIngestionFilter.ComputePublishedAfterCutoff(now, channelMaxAgeDays: null, globalDefaultMaxAgeDays: days);

        Assert.Equal(now.AddDays(-days), cutoff);
    }
}
