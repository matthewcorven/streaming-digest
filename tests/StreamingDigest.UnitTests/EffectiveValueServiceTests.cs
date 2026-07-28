using StreamingDigest.Application;

namespace StreamingDigest.UnitTests;

public sealed class EffectiveValueServiceTests
{
    private readonly IEffectiveValueService _service = new EffectiveValueService();

    [Fact]
    public void Resolve_uses_override_when_present()
    {
        var result = _service.Resolve("Original Title", "Updated Title");

        Assert.Equal("Original Title", result.Original);
        Assert.Equal("Updated Title", result.Override);
        Assert.Equal("Updated Title", result.Effective);
    }

    [Fact]
    public void Resolve_returns_null_values_when_both_original_and_override_are_null()
    {
        var result = _service.Resolve(null, null);

        Assert.Null(result.Original);
        Assert.Null(result.Override);
        Assert.Null(result.Effective);
    }

    [Fact]
    public void Resolve_trims_override_and_uses_trimmed_value_when_present()
    {
        var result = _service.Resolve("Original Title", "  Updated Title  ");

        Assert.Equal("Original Title", result.Original);
        Assert.Equal("Updated Title", result.Override);
        Assert.Equal("Updated Title", result.Effective);
    }

    [Fact]
    public void Resolve_falls_back_to_original_for_whitespace_override()
    {
        var result = _service.Resolve("Original Title", "   ");

        Assert.Equal("Original Title", result.Original);
        Assert.Null(result.Override);
        Assert.Equal("Original Title", result.Effective);
    }

    [Fact]
    public void Resolve_falls_back_to_original_when_override_is_null()
    {
        var result = _service.Resolve("Original Title", null);

        Assert.Equal("Original Title", result.Original);
        Assert.Null(result.Override);
        Assert.Equal("Original Title", result.Effective);
    }
}
