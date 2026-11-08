using GameTune.Analysis.Collectors;
using GameTune.Core.Profile;
using GameTune.Fixes.Catalog;
using GameTune.Native;

namespace GameTune.Tests;

public class ParserTests
{
    [Fact]
    public void BcdEnum_English()
    {
        const string output = """
            Windows Boot Loader
            -------------------
            identifier              {current}
            device                  partition=C:
            path                    \Windows\system32\winload.efi
            description             Windows 11
            useplatformclock        Yes
            disabledynamictick      Yes
            hypervisorlaunchtype    Auto
            """;
        var m = ToolParsers.ParseBcdEnum(output);
        Assert.Equal("Yes", m["useplatformclock"]);
        Assert.Equal("Yes", m["disabledynamictick"]);
        Assert.Equal("Auto", m["hypervisorlaunchtype"]);
        Assert.False(m.ContainsKey("useplatformtick"));
    }

    [Fact]
    public void BcdEnum_Russian()
    {
        const string output = "Загрузка Windows\r\n-------------------\r\nидентификатор           {current}\r\ndevice                  partition=C:\r\ndescription             ReviOS 24.10\r\nuseplatformtick         Да\r\n";
        var m = ToolParsers.ParseBcdEnum(output);
        Assert.Equal("Yes", m["useplatformtick"]);
        Assert.Equal("ReviOS 24.10", m["description"]);
    }

    [Fact]
    public void Powercfg_ReadsAcAndDcIndexes()
    {
        const string output = """
            Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced)
              Subgroup GUID: 54533251-82be-4824-96c1-47b60b740d00  (Processor power management)
                Power Setting GUID: bc5038f7-23e0-4960-96da-33abaf5935ec  (Maximum processor state)
                  Minimum Possible Setting: 0x00000000
                  Maximum Possible Setting: 0x00000064
                  Possible Settings increment: 0x00000001
                  Possible Settings units: %
                Current AC Power Setting Index: 0x00000046
                Current DC Power Setting Index: 0x00000032
            """;
        var (ac, dc) = ToolParsers.ParsePowercfgIndexes(output);
        Assert.Equal(70u, ac);
        Assert.Equal(50u, dc);
    }

    [Fact]
    public void WindowedOptimizations_MergeKeepsUnknownKeys()
    {
        Assert.Equal("AutoHDREnable=1;SwapEffectUpgradeEnable=1;VRROptimizeEnable=1;",
            WindowedOptimizationsFix.Merge("AutoHDREnable=1;SwapEffectUpgradeEnable=0;", ("SwapEffectUpgradeEnable", "1"), ("VRROptimizeEnable", "1")));
        Assert.Equal("SwapEffectUpgradeEnable=1;", WindowedOptimizationsFix.Merge(null, ("SwapEffectUpgradeEnable", "1")));
    }

    [Theory]
    [InlineData("~ RUNASADMIN", null)]
    [InlineData("RUNASADMIN", null)]
    [InlineData("~ RUNASADMIN HIGHDPIAWARE", "~ HIGHDPIAWARE")]
    [InlineData("~ HIGHDPIAWARE", "~ HIGHDPIAWARE")]
    public void ObsNoAdmin_RemovesOnlyRunAsAdmin(string input, string? expected) =>
        Assert.Equal(expected, ObsNoAdminFix.RemoveRunAsAdmin(input));

    [Fact]
    public void ObsScene_DisplayCaptureDetectedAndReplaced()
    {
        const string json = """{"sources":[{"id":"monitor_capture","name":"Экран","filters":[{"id":"color_filter"}]},{"id":"game_capture","name":"Игра"}]}""";
        Assert.True(ObsConfig.SceneHasDisplayCapture(json));
        var (updated, n) = ObsGameCaptureFix.ReplaceDisplayCapture(json);
        Assert.Equal(1, n);
        Assert.False(ObsConfig.SceneHasDisplayCapture(updated));
        Assert.Contains("color_filter", updated); // фильтры сохранены
        Assert.Contains("Экран", updated);        // имя сохранено
        Assert.False(ObsConfig.SceneHasDisplayCapture("не json"));
    }

    [Fact]
    public void ObsIni_ReplayBuffer()
    {
        const string ini = "[Output]\nMode=Advanced\n[AdvOut]\nRecRB=true\nRecRBTime=600\nRecRBSize=4096\n[SimpleOutput]\nRecRB=false\n";
        var obs = new ObsInfo();
        ObsConfig.ReadReplayBuffer(ini, obs);
        Assert.True(obs.ReplayBufferEnabled);
        Assert.Equal(600, obs.ReplayBufferSeconds);
        Assert.Equal(4096, obs.ReplayBufferMaxMb);
    }

    [Fact]
    public void Steam_LibrariesAndManifest()
    {
        const string vdf = """
            "libraryfolders"
            {
              "0" { "path" "C:\\Program Files (x86)\\Steam" "apps" { "228980" "0" } }
              "1" { "path" "D:\\SteamLibrary" }
            }
            """;
        Assert.Equal(new[] { @"C:\Program Files (x86)\Steam", @"D:\SteamLibrary" }, GameScan.ParseSteamLibraries(vdf));
        const string acf = "\"AppState\"\n{\n\t\"appid\"\t\t\"730\"\n\t\"name\"\t\t\"Counter-Strike 2\"\n\t\"installdir\"\t\t\"Counter-Strike Global Offensive\"\n}";
        var kv = GameScan.ParseAcf(acf);
        Assert.Equal("Counter-Strike 2", kv["name"]);
        Assert.Equal("Counter-Strike Global Offensive", kv["installdir"]);
        Assert.True(GameScan.IsSteamTool("Steamworks Common Redistributables"));
        Assert.False(GameScan.IsSteamTool("Counter-Strike 2"));
    }

    [Fact]
    public void FindExecutables_SkipsInstallersAndSmallFiles()
    {
        var dir = Directory.CreateTempSubdirectory("gametune-game-").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "Game.exe"), new byte[2_000_000]);
            File.WriteAllBytes(Path.Combine(dir, "unins000.exe"), new byte[2_000_000]);
            File.WriteAllBytes(Path.Combine(dir, "tiny.exe"), new byte[1000]);
            Directory.CreateDirectory(Path.Combine(dir, "_CommonRedist"));
            File.WriteAllBytes(Path.Combine(dir, "_CommonRedist", "big.exe"), new byte[3_000_000]);
            Directory.CreateDirectory(Path.Combine(dir, "EasyAntiCheat"));
            var exes = GameScan.FindExecutables(dir);
            Assert.Equal(new[] { Path.Combine(dir, "Game.exe") }, exes);
            Assert.True(GameScan.LooksOnline("Some Game", dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData(0x8086u, "Intel(R) Iris(R) Xe Graphics", 128, true)]
    [InlineData(0x8086u, "Intel(R) Arc(TM) Graphics", 128, true)]
    [InlineData(0x8086u, "Intel(R) Arc(TM) A770 Graphics", 16000, false)]
    [InlineData(0x1002u, "AMD Radeon(TM) Graphics", 512, true)]
    [InlineData(0x1002u, "AMD Radeon 780M Graphics", 512, true)]
    [InlineData(0x1002u, "AMD Radeon(TM) RX Vega 10 Graphics", 1024, true)]
    [InlineData(0x1002u, "AMD Radeon RX 7800 XT", 16000, false)]
    [InlineData(0x1002u, "AMD Radeon RX 6600M", 8000, false)]
    [InlineData(0x10DEu, "NVIDIA GeForce RTX 4060 Laptop GPU", 8000, false)]
    public void GpuClassifier_IntegratedVsDiscrete(uint vendorId, string name, long vram, bool integrated)
    {
        var vendor = GpuClassifier.Vendor(vendorId, name);
        Assert.Equal(integrated, GpuClassifier.IsIntegrated(vendor, name, vram));
    }

    [Theory]
    [InlineData("ChannelA-DIMM0", "A")]
    [InlineData("P0 CHANNEL B", "B")]
    [InlineData("DIMM_A2", "A")]
    [InlineData("Controller0-ChannelB-DIMM0", "B")]
    [InlineData("DIMM 0", null)]
    [InlineData("BANK 0", null)]
    public void MemoryChannel_Parsed(string label, string? expected)
    {
#pragma warning disable CA1416 // разбор строки не зависит от ОС
        Assert.Equal(expected, MemoryCollector.ChannelOf(label));
#pragma warning restore CA1416
    }

    [Theory]
    [InlineData("    Band                   : 5 GHz", 5.0)]
    [InlineData("    Диапазон               : 2,4 ГГц", 2.4)]
    [InlineData("    Channel                : 6", 2.4)]
    [InlineData("    Channel                : 149", 5.0)]
    public void WifiBand_Parsed(string netsh, double ghz)
    {
#pragma warning disable CA1416
        Assert.Equal(ghz, NetworkCollector.ParseWifiBand(netsh));
#pragma warning restore CA1416
    }

    [Fact]
    public void StartupApproved_Flag()
    {
#pragma warning disable CA1416
        Assert.True(BackgroundCollector.IsEnabled(null));
        Assert.True(BackgroundCollector.IsEnabled(new byte[] { 2, 0, 0, 0 }));
        Assert.False(BackgroundCollector.IsEnabled(new byte[] { 3, 0, 0, 0 }));
        Assert.True(BackgroundCollector.IsEnabled(new byte[] { 6, 0, 0, 0 }));
        Assert.False(BackgroundCollector.IsEnabled(new byte[] { 7, 0, 0, 0 }));
#pragma warning restore CA1416
    }
}
