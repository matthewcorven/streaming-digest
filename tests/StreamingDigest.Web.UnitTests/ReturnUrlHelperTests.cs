using StreamingDigest.Web.Services;
using Xunit;

namespace StreamingDigest.Web.UnitTests;

public sealed class ReturnUrlHelperTests
{
    [Theory]
    [InlineData("settings", "/settings")]
    [InlineData("/settings", "/settings")]
    [InlineData("settings?tab=models", "/settings?tab=models")]
    public void ResolveSafeReturnUrl_NormalizesLocalRoutes(string input, string expected)
    {
        Assert.Equal(expected, ReturnUrlHelper.ResolveSafeReturnUrl(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("//evil.example")]
    [InlineData("/login")]
    [InlineData("/login?returnUrl=%2Fsettings")]
    public void ResolveSafeReturnUrl_RejectsUnsafeOrRecursiveRoutes(string? input)
    {
        Assert.Null(ReturnUrlHelper.ResolveSafeReturnUrl(input));
    }

    [Fact]
    public void BuildLoginRedirectTarget_EncodesSafeReturnUrl()
    {
        Assert.Equal("/login?returnUrl=%2Fsettings%3Ftab%3Dmodels", ReturnUrlHelper.BuildLoginRedirectTarget("settings?tab=models"));
    }

    [Fact]
    public void BuildLoginRedirectTarget_FallsBackToLoginWithoutReturnUrl()
    {
        Assert.Equal("/login", ReturnUrlHelper.BuildLoginRedirectTarget("/login"));
    }
}