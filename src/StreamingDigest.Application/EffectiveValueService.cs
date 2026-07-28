namespace StreamingDigest.Application;

public interface IEffectiveValueService
{
    EffectiveValue Resolve(string? original, string? overrideValue);
}

public sealed class EffectiveValueService : IEffectiveValueService
{
    public EffectiveValue Resolve(string? original, string? overrideValue)
    {
        var normalizedOriginal = original;
        var normalizedOverride = string.IsNullOrWhiteSpace(overrideValue) ? null : overrideValue.Trim();
        var effectiveValue = normalizedOverride ?? normalizedOriginal;

        return new EffectiveValue(normalizedOriginal, normalizedOverride, effectiveValue);
    }
}

public sealed record EffectiveValue(string? Original, string? Override, string? Effective);
