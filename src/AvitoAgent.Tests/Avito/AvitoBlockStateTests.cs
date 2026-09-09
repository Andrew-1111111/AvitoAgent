using AvitoAgent.Avito;

namespace AvitoAgent.Tests.Avito;

public sealed class AvitoBlockStateTests
{
    public AvitoBlockStateTests() => AvitoBlockState.ResetForTests();

    [Fact]
    public void RegisterBlock_increments_within_hour()
    {
        AvitoBlockState.ResetForTests();
        Assert.Equal(1, AvitoBlockState.RegisterBlock());
        Assert.Equal(2, AvitoBlockState.RegisterBlock());
        Assert.Equal(3, AvitoBlockState.RegisterBlock());
    }

    [Fact]
    public void ResolveCooldown_zero_base_or_no_repeats()
    {
        AvitoBlockState.ResetForTests();
        Assert.Equal(TimeSpan.Zero, AvitoBlockState.ResolveCooldown(0));
        Assert.Equal(TimeSpan.Zero, AvitoBlockState.ResolveCooldown(-10));
        Assert.Equal(TimeSpan.FromMilliseconds(1000), AvitoBlockState.ResolveCooldown(1000));
    }

    [Fact]
    public void ResolveCooldown_doubles_and_caps_at_30_minutes()
    {
        AvitoBlockState.ResetForTests();
        AvitoBlockState.RegisterBlock();
        Assert.Equal(TimeSpan.FromMilliseconds(1000), AvitoBlockState.ResolveCooldown(1000));

        AvitoBlockState.RegisterBlock();
        Assert.Equal(TimeSpan.FromMilliseconds(2000), AvitoBlockState.ResolveCooldown(1000));

        AvitoBlockState.RegisterBlock();
        Assert.Equal(TimeSpan.FromMilliseconds(4000), AvitoBlockState.ResolveCooldown(1000));

        for (var i = 0; i < 20; i++)
        {
            AvitoBlockState.RegisterBlock();
        }

        Assert.Equal(TimeSpan.FromMinutes(30), AvitoBlockState.ResolveCooldown(60_000));
    }
}
