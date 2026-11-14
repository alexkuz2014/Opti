using Opti.Analysis.Detectors;
using Opti.Core.Fixes;
using Opti.Core.Profile;
using Opti.Core.Tweaks;
using Opti.Fixes.Backends;
using Opti.Fixes.Journal;
using Opti.Fixes.Services;
using Opti.ViewModels;

namespace Opti.Tests;

public sealed class FeaturePackTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("opti-fp-").FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    // ---------- твики ----------

    [Fact]
    public void NoTwoTweaksWriteTheSameValue()
    {
        var dup = TweakCatalog.All.SelectMany(t => t.Values.Select(v => (Key: (v.Key + "|" + v.Name).ToLowerInvariant(), t.Id)))
            .GroupBy(x => x.Key).Where(g => g.Select(x => x.Id).Distinct().Count() > 1).Select(g => g.Key + ": " + string.Join(", ", g.Select(x => x.Id)));
        Assert.Empty(dup);
    }

    [Fact]
    public void EveryTweak_TogglesOnAndOff_RestoringOriginalState()
    {
        foreach (var t in TweakCatalog.All)
        {
            var reg = InMemoryBackend.CreateRegistry(out var map);
            var before = map[ChangeType.Registry].Snapshot().OrderBy(k => k.Key).ToList();
            var svc = new TweakService(reg, new InMemoryChangeJournal());
            Assert.False(svc.IsOn(t), t.Id);
            svc.Enable(t);
            Assert.True(svc.IsOn(t), t.Id);
            svc.Disable(t);
            Assert.False(svc.IsOn(t), t.Id);
            Assert.Equal(before, map[ChangeType.Registry].Snapshot().OrderBy(k => k.Key).ToList());
        }
    }

    [Fact]
    public void ExtraTweaks_AreHonest_NotInMaximum_AndHaveTexts()
    {
        Assert.True(ExtraTweaks.All.Length >= 39);
        foreach (var t in ExtraTweaks.All)
        {
            Assert.DoesNotContain(t.Id, TweakCatalog.MaxOptimizeIds);
            Assert.True(t.Title.Length > 5 && t.Description.Length > 20, t.Id);
            Assert.DoesNotContain("FPS +", t.Description);
        }
        Assert.Contains(TweakCatalog.All, t => t.Category == TweakCategory.Privacy);
        Assert.Equal("Приватность и реклама", TweakCatalog.CategoryTitle(TweakCategory.Privacy));
    }

    // ---------- страница «Твики» ----------

    private (MainViewModel Main, TweaksViewModel Page) Page()
    {
        var main = new MainViewModel(new ViewModelTests.FakeServices(_dir, "desktop-rtx", _ => 8.0));
        var page = main.Tweaks;
        page.ConfigPath = Path.Combine(_dir, "opti-tweaks.json");
        page.Load();
        return (main, page);
    }

    [Fact]
    public void Search_FiltersGroups_AndShowsEmptyState()
    {
        var (_, page) = Page();
        int all = page.Groups.Sum(g => g.Items.Count);
        page.Search = "реклам";
        int found = page.Groups.Sum(g => g.Items.Count);
        Assert.InRange(found, 1, all - 1);
        page.Search = "такого-твика-нет";
        Assert.True(page.NothingFound);
        page.Search = "";
        Assert.Equal(all, page.Groups.Sum(g => g.Items.Count));
    }

    [Fact]
    public async Task ExportImport_AndDisableAll_RoundTrip()
    {
        var (_, page) = Page();
        var items = page.Groups.SelectMany(g => g.Items).ToList();
        items.First(i => i.Tweak.Id == "NO_AD_ID").IsOn = true;
        items.First(i => i.Tweak.Id == "SHOW_HIDDEN_FILES").IsOn = true;
        Assert.Contains("включено 1 из", page.Groups.First(g => g.Items.Any(i => i.Tweak.Id == "NO_AD_ID")).Counter);
        page.ExportConfigCommand.Execute(null);
        Assert.Contains("NO_AD_ID", File.ReadAllText(page.ConfigPath));

        await page.DisableAllCommand.ExecuteAsync(null);
        Assert.DoesNotContain(page.Groups.SelectMany(g => g.Items), i => i.IsOn);

        page.ImportConfigCommand.Execute(null);
        var on = page.Groups.SelectMany(g => g.Items).Where(i => i.IsOn).Select(i => i.Tweak.Id).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "NO_AD_ID", "SHOW_HIDDEN_FILES" }, on);
    }

    [Fact]
    public void Import_WithoutFile_OrBrokenFile_DoesNotThrow()
    {
        var (_, page) = Page();
        page.ImportConfigCommand.Execute(null);
        File.WriteAllText(page.ConfigPath, "{ это не список");
        page.ImportConfigCommand.Execute(null);
        Assert.DoesNotContain(page.Groups.SelectMany(g => g.Items), i => i.IsOn);
    }

    // ---------- детекторы ----------

    private static List<string> Ids(SystemProfile p) => TestProfiles.Findings(p).Select(f => f.Id).ToList();

    [Fact]
    public void BiosOld_OnlyWhenOlderThanFourYears()
    {
        var p = TestProfiles.Load("desktop-rtx");
        p.Device.BiosDate = TestProfiles.Now.AddYears(-6).DateTime;
        Assert.Contains("BIOS_OLD", Ids(p));
        p.Device.BiosDate = TestProfiles.Now.AddYears(-1).DateTime;
        Assert.DoesNotContain("BIOS_OLD", Ids(p));
        p.Device.BiosDate = null;
        Assert.DoesNotContain("BIOS_OLD", Ids(p));
    }

    [Fact]
    public void VirtualMachine_And_SystemRestoreOff()
    {
        var p = TestProfiles.Load("desktop-rtx");
        p.Device.IsVirtualMachine = true;
        p.Security.SystemRestoreEnabled = false;
        var ids = Ids(p);
        Assert.Contains("VIRTUAL_MACHINE", ids);
        Assert.Contains("SYSTEM_RESTORE_OFF", ids);
        p.Device.IsVirtualMachine = false;
        p.Security.SystemRestoreEnabled = null;   // не знаем — молчим
        ids = Ids(p);
        Assert.DoesNotContain("VIRTUAL_MACHINE", ids);
        Assert.DoesNotContain("SYSTEM_RESTORE_OFF", ids);
    }

    [Fact]
    public void RamMixedModules_DifferentSizesOrModels()
    {
        var p = TestProfiles.Load("desktop-rtx");
        p.Memory.Modules = new()
        {
            new MemoryModule { CapacityMb = 8192, RatedSpeed = 3200, PartNumber = "A" },
            new MemoryModule { CapacityMb = 16384, RatedSpeed = 3200, PartNumber = "A" },
        };
        Assert.Contains("RAM_MIXED_MODULES", Ids(p));
        p.Memory.Modules = new()
        {
            new MemoryModule { CapacityMb = 8192, RatedSpeed = 3200, PartNumber = "A" },
            new MemoryModule { CapacityMb = 8192, RatedSpeed = 3200, PartNumber = "a " },
        };
        Assert.DoesNotContain("RAM_MIXED_MODULES", Ids(p));
    }

    [Fact]
    public void Displays_PrimarySlow_SixtyHzTip_HeavyResolution()
    {
        var p = TestProfiles.Load("desktop-rtx");
        p.Displays = new()
        {
            new DisplayInfo { DeviceName = "1", Width = 1920, Height = 1080, CurrentHz = 60, MaxHzAtCurrentResolution = 60, IsPrimary = true },
            new DisplayInfo { DeviceName = "2", Width = 2560, Height = 1440, CurrentHz = 165, MaxHzAtCurrentResolution = 165 },
        };
        var ids = Ids(p);
        Assert.Contains("DISP_PRIMARY_SLOW", ids);
        Assert.DoesNotContain("DISP_60HZ_CAP_TIP", ids);

        p.Displays.RemoveAt(1);
        ids = Ids(p);
        Assert.DoesNotContain("DISP_PRIMARY_SLOW", ids);
        Assert.Contains("DISP_60HZ_CAP_TIP", ids);

        var igpu = TestProfiles.Load("laptop-igpu");
        igpu.Displays = new() { new DisplayInfo { DeviceName = "1", Width = 2560, Height = 1600, CurrentHz = 60, MaxHzAtCurrentResolution = 60, IsPrimary = true } };
        Assert.Contains("DISP_RES_HEAVY", Ids(igpu));
        igpu.Displays[0].Width = 1920; igpu.Displays[0].Height = 1080;
        Assert.DoesNotContain("DISP_RES_HEAVY", Ids(igpu));
    }

    [Fact]
    public void NewDetectors_AreRegistered()
    {
        var ids = DetectorCatalog.All().Select(d => d.Id).ToHashSet();
        foreach (var id in new[] { "BIOS_OLD", "VIRTUAL_MACHINE", "SYSTEM_RESTORE_OFF", "RAM_MIXED_MODULES", "DISP_PRIMARY_SLOW", "DISP_60HZ_CAP_TIP", "DISP_RES_HEAVY" })
            Assert.Contains(id, ids);
    }
}
