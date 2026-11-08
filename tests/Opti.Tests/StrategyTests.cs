using Opti.Core.Fixes;
using Opti.Core.Strategy;
using Opti.Fixes.Catalog;

namespace Opti.Tests;

public class StrategyTests
{
    private static StrategyPlan Plan(string profile, UserGoal goal)
    {
        var p = TestProfiles.Load(profile);
        return new StrategyEngine(FixCatalog.All()).Build(p, TestProfiles.Findings(p), goal);
    }

    [Fact]
    public void DeviceClassification_MatchesProfiles()
    {
        Assert.Equal(DeviceClass.HybridLaptop, DeviceClassification.From(TestProfiles.Load("laptop-optimus")).Class);
        Assert.Equal(DeviceClass.DesktopDgpu, DeviceClassification.From(TestProfiles.Load("desktop-rtx")).Class);
        Assert.Equal(DeviceClass.IgpuLaptop, DeviceClassification.From(TestProfiles.Load("laptop-igpu")).Class);
        Assert.True(DeviceClassification.From(TestProfiles.Load("weak-pc")).IsWeak);
        Assert.True(DeviceClassification.From(TestProfiles.Load("revios")).IsModifiedOs);
        Assert.False(DeviceClassification.From(TestProfiles.Load("desktop-rtx")).IsWeak);
    }

    [Fact]
    public void FixesComeOnlyFromTriggeredDetectors()
    {
        foreach (var name in TestProfiles.All)
        {
            var p = TestProfiles.Load(name);
            var triggered = TestProfiles.Findings(p).SelectMany(f => f.FixIds).ToHashSet();
            var plan = new StrategyEngine(FixCatalog.All()).Build(p, TestProfiles.Findings(p), UserGoal.MaxFps);
            Assert.All(plan.Fixes, f => Assert.Contains(f.Fix.Id, triggered));
        }
    }

    [Fact]
    public void QuietGoal_ExcludesPerformancePower()
    {
        var plan = Plan("desktop-rtx", UserGoal.QuietCool);
        Assert.DoesNotContain(plan.Fixes, f => f.Fix.Id is FixIds.PowerSchemePerf or FixIds.PowerAcTuning);
        Assert.Contains(plan.Excluded, e => e.FixId == FixIds.PowerSchemePerf && e.Reason.Contains("Тихо"));

        var fps = Plan("desktop-rtx", UserGoal.MaxFps);
        Assert.Contains(fps.Fixes, f => f.Fix.Id == FixIds.PowerSchemePerf && f.Selected);
    }

    [Fact]
    public void Laptop_DoesNotSwitchWholeSchemeButTunesAc()
    {
        var plan = Plan("laptop-optimus", UserGoal.MaxFps);
        Assert.DoesNotContain(plan.Fixes, f => f.Fix.Id == FixIds.PowerSchemePerf);
        Assert.Contains(plan.Fixes, f => f.Fix.Id == FixIds.PowerAcTuning);
    }

    [Fact]
    public void RiskyAndAbFixes_GoLastAndSeparately()
    {
        var plan = Plan("desktop-rtx", UserGoal.LowLatency);
        var hags = plan.Fixes.Single(f => f.Fix.Id == FixIds.HagsAb);
        Assert.True(hags.ApplySeparately);
        var lastSafe = plan.Fixes.Where(f => !f.ApplySeparately).Max(f => f.Order);
        Assert.All(plan.Fixes.Where(f => f.ApplySeparately), f => Assert.True(f.Order > lastSafe));
        // Сначала «бесплатные»: экран/GPU/OBS раньше питания
        var order = plan.Fixes.Select(f => f.Fix.Id).ToList();
        Assert.True(order.IndexOf(FixIds.MpoReset) < order.IndexOf(FixIds.PowerSchemePerf));
        Assert.True(order.IndexOf(FixIds.PowerSchemePerf) < order.IndexOf(FixIds.PowerAcTuning));
    }

    [Fact]
    public void BitLocker_UnchecksBcdChanges()
    {
        var p = TestProfiles.Load("desktop-rtx");
        p.Security.BitLockerOnSystemDrive = true;
        var plan = new StrategyEngine(FixCatalog.All()).Build(p, TestProfiles.Findings(p), UserGoal.LowLatency);
        var bcd = plan.Fixes.Single(f => f.Fix.Id == FixIds.BcdTimersReset);
        Assert.False(bcd.Selected);
        Assert.Contains(bcd.Notes, n => n.Contains("BitLocker"));
    }

    [Fact]
    public void ModifiedOs_DoesNotDuplicateOrForceItsSettings()
    {
        var plan = Plan("revios", UserGoal.LowLatency);
        Assert.DoesNotContain(plan.Fixes, f => f.Fix.Id == FixIds.TimerResGlobal); // уже включено сборкой
        var mpo = plan.Fixes.Single(f => f.Fix.Id == FixIds.MpoReset);
        Assert.False(mpo.Selected);
        Assert.Contains(mpo.Notes, n => n.Contains("ReviOS"));
        var bcd = plan.Fixes.Single(f => f.Fix.Id == FixIds.BcdTimersReset);
        Assert.False(bcd.Selected);
    }

    [Fact]
    public void StartupFix_NeedsUserChoice()
    {
        var plan = Plan("laptop-igpu", UserGoal.Smoothness);
        var s = plan.Fixes.Single(f => f.Fix.Id == FixIds.StartupDisable);
        Assert.False(s.Selected);
    }

    [Fact]
    public void ObsFixes_InPlanForStreaming()
    {
        var plan = Plan("laptop-optimus", UserGoal.Streaming);
        Assert.Contains(plan.Fixes, f => f.Fix.Id == FixIds.ObsGameCapture && f.Selected);
        Assert.Contains(plan.Fixes, f => f.Fix.Id == FixIds.ObsNoAdmin && f.Selected);
        Assert.Contains(plan.Fixes.Single(f => f.Fix.Id == FixIds.GameDvrOff).Notes, n => n.Contains("Game Bar"));
    }

    [Fact]
    public void AdviceOnlyFindings_AreSeparated()
    {
        var plan = Plan("weak-pc", UserGoal.MaxFps);
        Assert.Contains(plan.AdviceOnly, f => f.Id == "THERMAL_THROTTLE");
        Assert.DoesNotContain(plan.Fixes, f => f.TriggeredBy.Contains("THERMAL_THROTTLE"));
    }
}
