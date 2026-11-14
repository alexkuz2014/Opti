using Opti.Core.Tweaks;

namespace Opti.Tests;

public class MaxUniversalityTests
{
    [Fact]
    public void StrongDesktop_GetsOnlyUniversalTweaks()
    {
        var p = TestProfiles.Load("desktop-rtx");
        p.Memory.TotalMb = 32 * 1024; p.Cpu.Cores = 8; p.Device.IsLaptop = false;
        var ids = TweakCatalog.MaxIdsFor(p);
        Assert.DoesNotContain("NO_TRANSPARENCY", ids);
        Assert.DoesNotContain("NO_WIDGETS", ids);
        Assert.Contains("NO_UPDATE_SHARING", ids);
    }

    [Theory]
    [InlineData("weak-pc")]
    [InlineData("laptop-igpu")]
    [InlineData("laptop-optimus")]
    public void ConstrainedPcs_GetResourceSavingTweaks(string name)
    {
        var ids = TweakCatalog.MaxIdsFor(TestProfiles.Load(name));
        Assert.Contains("NO_BACKGROUND_APPS", ids);
        Assert.Contains("NO_TRANSPARENCY", ids);
    }

    [Fact]
    public void NoProfile_KeepsFullSet() =>
        Assert.Equal(TweakCatalog.MaxOptimizeIds, TweakCatalog.MaxIdsFor(null));

    [Fact]
    public void EveryMaxTweakExists()
    {
        foreach (var id in TweakCatalog.MaxOptimizeIds) Assert.NotNull(TweakCatalog.Find(id));
    }
}
