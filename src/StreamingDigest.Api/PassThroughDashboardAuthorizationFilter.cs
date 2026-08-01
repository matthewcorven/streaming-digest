using Hangfire.Dashboard;

namespace StreamingDigest.Api;

internal sealed class PassThroughDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) => true;
}