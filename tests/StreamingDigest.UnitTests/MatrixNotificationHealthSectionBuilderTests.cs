using Microsoft.Extensions.Logging.Abstractions;
using StreamingDigest.Api.Admin;
using StreamingDigest.Domain;
using StreamingDigest.MatrixNotifier;
using StreamingDigest.Web.Models;

namespace StreamingDigest.UnitTests;

public sealed class MatrixNotificationHealthSectionBuilderTests
{
    private readonly MatrixNotificationHealthSectionBuilder _builder = new(NullLogger.Instance);

    [Fact]
    public void Build_returns_degraded_section_for_incomplete_configuration()
    {
        var section = _builder.Build(
            new MatrixHealthInfo(true, "needs-token", "Add a Matrix access token to start sending.", "https://matrix.example.com", "!room:example.com", "@digest:example.com"),
            []);

        Assert.Equal(HealthState.Degraded, section.State);
        Assert.Equal("needs-token", section.Status);
        Assert.Equal("Add a Matrix access token to start sending.", section.Summary);
        Assert.Contains("Add a Matrix access token to start sending.", section.Details);
    }

    [Fact]
    public void Build_returns_ready_idle_section_when_configuration_is_valid_and_no_recent_notifications_exist()
    {
        var section = _builder.Build(
            new MatrixHealthInfo(true, "ready", "Matrix notifications are configured.", "https://matrix.example.com", "!room:example.com", "@digest:example.com"),
            []);

        Assert.Equal(HealthState.Ready, section.State);
        Assert.Equal("idle", section.Status);
        Assert.Equal("Matrix notifications are configured; no recent deliveries to verify.", section.Summary);
        Assert.Equal(0, section.RecentSuccessCount);
        Assert.Equal(0, section.RecentFailureCount);
    }

    [Fact]
    public void Build_returns_error_section_when_recent_delivery_attempts_all_fail()
    {
        var notifications = new List<Notification>
        {
            new Notification
            {
                Provider = "matrix",
                Status = "pending",
                ErrorSummary = "401 unauthorized",
                UpdatedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow
            }
        };

        var section = _builder.Build(
            new MatrixHealthInfo(true, "ready", "Matrix notifications are configured.", "https://matrix.example.com", "!room:example.com", "@digest:example.com"),
            notifications);

        Assert.Equal(HealthState.Error, section.State);
        Assert.Equal("delivery-failed", section.Status);
        Assert.Equal(0, section.RecentSuccessCount);
        Assert.Equal(1, section.RecentFailureCount);
        Assert.Contains("401 unauthorized", string.Join(' ', section.Details));
    }
}