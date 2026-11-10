using Opti.Analysis.Collectors;
using Opti.Analysis.Detectors;
using Opti.Core.Drivers;
using Opti.Core.Findings;
using Opti.Core.Fixes;
using Opti.Core.Hardware;
using Opti.Core.Network;
using Opti.Core.Os;
using Opti.Core.Profile;
using Opti.Core.Strategy;
using Opti.Core.Tweaks;
using Opti.Fixes.Backends;
using Opti.Fixes.Catalog;
using Opti.Fixes.Journal;
using Opti.Fixes.Services;

namespace Opti.Tests;

public class GamingAndNetworkTests
{
    private static List<Finding> Run(SystemProfile p) => TestProfiles.Findings(p);
    private static HashSet<string> Ids(SystemProfile p) => Run(p).Select(f => f.Id).ToHashSet();

    // ---------- Старый игровой ПК целиком ----------

    [Fact]
    public void OldGamingPc_FindsNetworkStorageAndSupportIssues()
    {
        var ids = TestProfiles.FindingIds("old-gaming-pc");
        Assert.Contains("OS_END_OF_SUPPORT", ids);
        Assert.Contains("GPU_LEGACY", ids);
        Assert.Contains("GAME_ON_HDD", ids);
        Assert.Contains("DISK_TRIM_OFF", ids);
        Assert.Contains("NET_ISP_UNSTABLE", ids);       // до роутера чисто, дальше потери
        Assert.Contains("NET_LINK_SLOW", ids);
        Assert.Contains("NET_EEE_ON", ids);
        Assert.Contains("NET_AUTOTUNING_OFF", ids);
        Assert.Contains("NET_VPN_PROXY", ids);
        Assert.Contains("NET_BACKGROUND_TRAFFIC", ids);
        Assert.Contains("NET_NAGLE_CANDIDATE", ids);
        Assert.Contains("BG_CPU_HOG", ids);
        // Драйвер GTX 1080 — последний выпущенный: «устаревшим» его называть нельзя
        Assert.DoesNotContain("DRIVER_OLD", ids);
        // Windows 10 снята с обслуживания — «давно не обновлялась» было бы упрёком без решения
        Assert.DoesNotContain("OS_UPDATES_STALE", ids);
        Assert.DoesNotContain("NET_LOCAL_UNSTABLE", ids);
    }

    [Fact]
    public void OldGamingPc_CpuHogSumsProcessesAndIgnoresOwnTools()
    {
        var f = Run(TestProfiles.Load("old-gaming-pc")).Single(x => x.Id == "BG_CPU_HOG");
        Assert.Equal(Severity.Important, f.Severity);                 // два процесса chrome в сумме 27,5 %
        Assert.Contains(f.Evidence, e => e.StartsWith("chrome"));
        Assert.DoesNotContain(f.Evidence, e => e.Contains("WmiPrvSE")); // это нагрузка от самого анализа
    }

    // ---------- Сеть ----------

    private static SystemProfile WithNet(NetworkQuality q, bool wifi = false) => new()
    {
        NetQuality = q,
        Network = { new NetAdapterInfo { Name = "n", Kind = wifi ? NetAdapterKind.WiFi : NetAdapterKind.Ethernet, IsUp = true } },
    };

    [Fact]
    public void CleanNetwork_NoFindings()
    {
        var ids = Ids(WithNet(new NetworkQuality { Target = "1.1.1.1", IdlePingMs = 14, IdleJitterMs = 1.5, PacketLossPercent = 0, Gateway = "192.168.0.1", GatewayPingMs = 0.5, GatewayJitterMs = 0.3, GatewayLossPercent = 0 }));
        Assert.DoesNotContain(ids, i => i.StartsWith("NET_"));
    }

    [Fact]
    public void SingleLostPacket_IsNotAProblem()
    {
        // 1 из 20 = 5 % — случайность, а не диагноз
        var ids = Ids(WithNet(new NetworkQuality { Target = "1.1.1.1", IdlePingMs = 14, IdleJitterMs = 2, PacketLossPercent = 5 }));
        Assert.DoesNotContain("NET_ISP_UNSTABLE", ids);
        Assert.DoesNotContain("NET_LOCAL_UNSTABLE", ids);
    }

    [Fact]
    public void LossBeforeRouter_BlamesHomeNetworkNotProvider()
    {
        var p = WithNet(new NetworkQuality { Target = "1.1.1.1", IdlePingMs = 40, IdleJitterMs = 25, PacketLossPercent = 15, Gateway = "192.168.0.1", GatewayPingMs = 18, GatewayJitterMs = 14, GatewayLossPercent = 10 }, wifi: true);
        var f = Run(p).Single(x => x.Id.EndsWith("_UNSTABLE"));
        Assert.Equal("NET_LOCAL_UNSTABLE", f.Id);
        Assert.Contains("Wi-Fi", f.Title);
        Assert.Contains(f.ManualSteps, s => s.Contains("кабел"));
    }

    [Fact]
    public void LossWithoutGatewayData_SaysLocationUnknown()
    {
        var f = Run(WithNet(new NetworkQuality { Target = "1.1.1.1", IdlePingMs = 30, IdleJitterMs = 3, PacketLossPercent = 20 })).Single(x => x.Id == "NET_ISP_UNSTABLE");
        Assert.Contains("Роутер на пинг не отвечает", f.Explanation);
    }

    [Fact]
    public void HighPing_PointsAtVpnWhenActive_AndSkipsCustomTargets()
    {
        var p = WithNet(new NetworkQuality { Target = "1.1.1.1", IdlePingMs = 140, IdleJitterMs = 3 });
        p.NetSettings.ActiveVpnAdapters.Add("WireGuard Tunnel");
        var f = Run(p).Single(x => x.Id == "NET_HIGH_PING");
        Assert.Contains("VPN", f.Explanation);
        Assert.False(f.IsHardwareLimitation);

        // До своего сервера (например, игрового в другой стране) 140 мс может быть нормой
        Assert.DoesNotContain("NET_HIGH_PING", Ids(WithNet(new NetworkQuality { Target = "203.0.113.7", IdlePingMs = 140 })));
    }

    [Fact]
    public void QualityCollector_NoRepliesMeansNoVerdict()
    {
        Assert.Null(NetworkQualityCollector.Build("1.1.1.1", new PingProbe.Series(new(), 20), "192.168.0.1", new PingProbe.Series(new() { 1, 1 }, 0)));
        var q = NetworkQualityCollector.Build("1.1.1.1", new PingProbe.Series(new() { 10, 14, 12 }, 1), "192.168.0.1", new PingProbe.Series(new(), 4))!;
        Assert.Equal(12, q.IdlePingMs, 3);
        Assert.Equal(25, q.PacketLossPercent, 3);
        Assert.Equal(3, q.IdleJitterMs, 3);
        Assert.Null(q.GatewayPingMs); // роутер не отвечает на пинг — это не «100 % потерь»
    }

    [Theory]
    [InlineData(2.4, 80, "802.11ax", true)]
    [InlineData(5.0, 45, "802.11ac", true)]
    [InlineData(5.0, 85, "802.11n", true)]
    [InlineData(5.0, 85, "802.11ax", false)]
    public void WifiQuality(double band, int signal, string radio, bool expected)
    {
        var p = new SystemProfile { Network = { new NetAdapterInfo { Name = "Wi-Fi", Kind = NetAdapterKind.WiFi, IsUp = true, WifiBandGhz = band, WifiSignalPercent = signal, WifiRadioType = radio } } };
        Assert.Equal(expected, Ids(p).Contains("NET_WIFI_WEAK"));
    }

    [Fact]
    public void WifiQuality_IgnoredWhenCableIsConnected()
    {
        var p = new SystemProfile
        {
            Network =
            {
                new NetAdapterInfo { Name = "Wi-Fi", Kind = NetAdapterKind.WiFi, IsUp = true, WifiBandGhz = 2.4, WifiSignalPercent = 30 },
                new NetAdapterInfo { Name = "Ethernet", Kind = NetAdapterKind.Ethernet, IsUp = true, LinkSpeedMbps = 1000 },
            },
        };
        Assert.DoesNotContain("NET_WIFI_WEAK", Ids(p));
    }

    [Theory]
    [InlineData("Realtek PCIe GbE Family Controller", 100, true)]
    [InlineData("Realtek PCIe GbE Family Controller", 1000, false)]
    [InlineData("Realtek PCIe FE Family Controller", 100, false)] // Fast Ethernet: 100 Мбит/с — её потолок
    public void SlowLink_OnlyForGigabitCards(string description, int speed, bool expected)
    {
        var p = new SystemProfile { Network = { new NetAdapterInfo { Name = "Ethernet", Description = description, Kind = NetAdapterKind.Ethernet, IsUp = true, LinkSpeedMbps = speed } } };
        Assert.Equal(expected, Ids(p).Contains("NET_LINK_SLOW"));
    }

    [Theory]
    [InlineData("Normal", false)]
    [InlineData("Disabled", true)]
    [InlineData("HighlyRestricted", true)]
    [InlineData(null, false)]
    public void AutoTuning(string? level, bool expected)
    {
        var p = new SystemProfile();
        p.NetSettings.TcpAutoTuning = level;
        Assert.Equal(expected, Ids(p).Contains("NET_AUTOTUNING_OFF"));
    }

    [Fact]
    public void NagleCandidate_OnlyWithOnlineGames()
    {
        var p = new SystemProfile { Network = { new NetAdapterInfo { Name = "Ethernet", Kind = NetAdapterKind.Ethernet, IsUp = true, InterfaceGuid = "{A}", NagleDisabled = false } } };
        Assert.DoesNotContain("NET_NAGLE_CANDIDATE", Ids(p));
        p.Games.Add(new GameInfo { Name = "WoW", IsOnline = true });
        Assert.Contains("NET_NAGLE_CANDIDATE", Ids(p));
        p.Network[0].NagleDisabled = true;
        Assert.DoesNotContain("NET_NAGLE_CANDIDATE", Ids(p));
    }

    [Theory]
    [InlineData("WireGuard Tunnel", true)]
    [InlineData("TAP-Windows Adapter V9", true)]
    [InlineData("Cloudflare WARP Interface Tunnel", true)]
    [InlineData("Intel(R) Wi-Fi 6 AX200 160MHz", false)]
    [InlineData("Realtek Gaming 2.5GbE Family Controller", false)]
    [InlineData("Hyper-V Virtual Ethernet Adapter", false)]
    public void VpnAdapterNames(string description, bool expected) => Assert.Equal(expected, NetworkApps.LooksLikeVpn(description));

    [Fact]
    public void NetworkApps_MatchByProcessName()
    {
        var found = NetworkApps.Match(new[] { "explorer", "QBITTORRENT", "OneDrive", "chrome" }).ToList();
        Assert.Equal(new[] { "qBittorrent", "OneDrive" }, found);
        Assert.All(found, t => Assert.False(string.IsNullOrEmpty(NetworkApps.HintFor(t))));
    }

    [Theory]
    [InlineData("    Signal                 : 78% ", 78)]
    [InlineData("    Сигнал                 : 41%", 41)]
    [InlineData("    State                  : connected", null)]
    public void ParseWifiSignal(string line, int? expected) => Assert.Equal(expected, NetworkCollector.ParseWifiSignal(line));

    [Theory]
    [InlineData("    Radio type             : 802.11ac", "802.11ac")]
    [InlineData("    Тип радио              : 802.11AX", "802.11ax")]
    [InlineData("    Authentication         : WPA2-Personal", null)]
    public void ParseWifiRadio(string line, string? expected) => Assert.Equal(expected, NetworkCollector.ParseWifiRadio(line));

    // ---------- Windows: обновления и срок поддержки ----------

    private static SystemProfile Os(int build, string edition, DateTime? lastUpdate) =>
        new() { Os = new OsInfo { Build = build, Edition = edition, ProductName = build >= 22000 ? "Windows 11" : "Windows 10", LastUpdateInstalled = lastUpdate } };

    [Fact]
    public void StaleUpdates_SeverityGrowsWithAge()
    {
        // TestProfiles.Now = 08.11.2026; сборка 26200 (25H2) ещё обслуживается
        Assert.DoesNotContain("OS_UPDATES_STALE", Ids(Os(26200, "Professional", new DateTime(2026, 10, 14))));
        Assert.Equal(Severity.Advice, Run(Os(26200, "Professional", new DateTime(2026, 7, 1))).Single(f => f.Id == "OS_UPDATES_STALE").Severity);
        var year = Run(Os(26200, "Professional", new DateTime(2025, 11, 1))).Single(f => f.Id == "OS_UPDATES_STALE");
        Assert.Equal(Severity.Important, year.Severity);
        Assert.Contains(year.Evidence, e => e.Contains("01.11.2025"));
        // Нет данных — нет вывода
        Assert.DoesNotContain("OS_UPDATES_STALE", Ids(Os(26200, "Professional", null)));
    }

    [Theory]
    [InlineData(19045, "Core", true)]            // Windows 10 Home
    [InlineData(19044, "EnterpriseS", false)]    // LTSC обслуживается дольше
    [InlineData(22631, "Professional", true)]    // 23H2 — до 11.11.2025
    [InlineData(22631, "Enterprise", false)]
    [InlineData(26100, "Professional", true)]    // 24H2 — до 13.10.2026
    [InlineData(26200, "Core", false)]           // 25H2 — до 12.10.2027
    [InlineData(28000, "Core", false)]           // неизвестная новая сборка — не пугаем
    [InlineData(0, "Core", false)]
    public void EndOfSupport(int build, string edition, bool expected) =>
        Assert.Equal(expected, Ids(Os(build, edition, null)).Contains("OS_END_OF_SUPPORT"));

    [Fact]
    public void Lifecycle_PreviewBuildsMapToPreviousRelease()
    {
        Assert.Equal("Windows 11 24H2", WindowsLifecycle.Find(26120)!.Name);
        Assert.Equal("Windows 10", WindowsLifecycle.Find(19045)!.Name);
        Assert.Null(WindowsLifecycle.Find(27900));
    }

    // ---------- Видеокарты, снятые с поддержки ----------

    [Theory]
    [InlineData(GpuVendor.Nvidia, "NVIDIA GeForce GTX 1080", true)]
    [InlineData(GpuVendor.Nvidia, "NVIDIA GeForce GTX 1050 Ti", true)]
    [InlineData(GpuVendor.Nvidia, "NVIDIA GeForce GTX 970", true)]
    [InlineData(GpuVendor.Nvidia, "NVIDIA GeForce GTX 750 Ti", true)]
    [InlineData(GpuVendor.Nvidia, "NVIDIA GeForce GTX 660", true)]
    [InlineData(GpuVendor.Nvidia, "NVIDIA GeForce GTX 1650", false)]
    [InlineData(GpuVendor.Nvidia, "NVIDIA GeForce GTX 1660 SUPER", false)]
    [InlineData(GpuVendor.Nvidia, "NVIDIA GeForce RTX 2060", false)]
    [InlineData(GpuVendor.Nvidia, "NVIDIA GeForce RTX 4070", false)]
    [InlineData(GpuVendor.Nvidia, "NVIDIA TITAN RTX", false)]
    [InlineData(GpuVendor.Amd, "Radeon RX 580 Series", true)]
    [InlineData(GpuVendor.Amd, "AMD Radeon R9 380 Series", true)]
    [InlineData(GpuVendor.Amd, "AMD Radeon RX 5700 XT", false)]
    [InlineData(GpuVendor.Amd, "AMD Radeon RX 5500 XT", false)]
    [InlineData(GpuVendor.Amd, "AMD Radeon RX 6600", false)]
    [InlineData(GpuVendor.Intel, "Intel(R) HD Graphics 530", true)]
    [InlineData(GpuVendor.Intel, "Intel(R) UHD Graphics 630", true)]
    [InlineData(GpuVendor.Intel, "Intel(R) UHD Graphics 770", false)]
    [InlineData(GpuVendor.Intel, "Intel(R) Iris(R) Xe Graphics", false)]
    [InlineData(GpuVendor.Intel, "Intel(R) Arc(TM) A750 Graphics", false)]
    public void LegacyGpu(GpuVendor vendor, string name, bool legacy) => Assert.Equal(legacy, GpuSupport.Legacy(vendor, name) is not null);

    [Fact]
    public void LegacyGpu_DriverJudgedAgainstFinalRelease()
    {
        var now = TestProfiles.Now.AddYears(2); // 2028: финальному драйверу GTX 1080 уже три года
        var age = DriverOldDetector.MaxAge;
        Assert.False(GpuSupport.IsDriverOutdated(GpuVendor.Nvidia, "NVIDIA GeForce GTX 1080", new DateTime(2025, 10, 10), now, age));
        Assert.True(GpuSupport.IsDriverOutdated(GpuVendor.Nvidia, "NVIDIA GeForce GTX 1080", new DateTime(2022, 3, 1), now, age));
        Assert.True(GpuSupport.IsDriverOutdated(GpuVendor.Nvidia, "NVIDIA GeForce RTX 4070", new DateTime(2025, 10, 10), now, age));

        var d = new DriverInfo { DeviceName = "NVIDIA GeForce GTX 1080", Category = DriverCategory.Display, Manufacturer = "NVIDIA", Provider = "NVIDIA", Date = new DateTime(2025, 10, 10) };
        Assert.Equal(DriverStatus.Ok, DriverAdvisor.Evaluate(d, now));
        d.Date = new DateTime(2022, 3, 1);
        Assert.Equal(DriverStatus.Old, DriverAdvisor.Evaluate(d, now));
    }

    [Fact]
    public void LegacyGpu_OldDriverStillReportedOnWeakPc()
    {
        // GTX 1050 Ti с драйвером 2021 года: финальный драйвер новее — обновить один раз стоит
        var findings = Run(TestProfiles.Load("weak-pc"));
        Assert.Contains(findings, f => f.Id == "DRIVER_OLD");
        var legacy = findings.Single(f => f.Id == "GPU_LEGACY");
        Assert.NotEmpty(legacy.Links);
    }

    [Fact]
    public void LegacyGpu_IntegratedIgnoredWhenDiscreteIsModern()
    {
        var p = new SystemProfile
        {
            Gpus =
            {
                new GpuInfo { Name = "Intel(R) UHD Graphics 630", Vendor = GpuVendor.Intel, IsIntegrated = true },
                new GpuInfo { Name = "NVIDIA GeForce RTX 3060", Vendor = GpuVendor.Nvidia },
            },
        };
        Assert.DoesNotContain("GPU_LEGACY", Ids(p));
    }

    // ---------- Питание, диски ----------

    [Fact]
    public void LaptopOnBattery()
    {
        var p = new SystemProfile { Device = { IsLaptop = true, HasBattery = true }, Power = { OnAcPower = false, BatteryPercent = 64 } };
        Assert.Contains("POWER_ON_BATTERY", Ids(p));
        p.Power.OnAcPower = true;
        Assert.DoesNotContain("POWER_ON_BATTERY", Ids(p));
    }

    [Fact]
    public void GamesOnHdd_HardwareLimitOnlyWithoutSsd()
    {
        var p = new SystemProfile
        {
            Disks = { new DiskInfo { Model = "HDD", MediaType = DiskMediaType.Hdd, IsSystem = true, DriveLetters = { "C:" } } },
            Games = { new GameInfo { Name = "Game", InstallDir = @"c:\Games\Game" } },
        };
        Assert.True(Run(p).Single(f => f.Id == "GAME_ON_HDD").IsHardwareLimitation);
        p.Disks.Add(new DiskInfo { Model = "SSD", MediaType = DiskMediaType.Nvme, DriveLetters = { "E:" } });
        var f2 = Run(p).Single(f => f.Id == "GAME_ON_HDD");
        Assert.False(f2.IsHardwareLimitation);
        Assert.NotEmpty(f2.ManualSteps);
        p.Games[0].InstallDir = @"E:\Games\Game";
        Assert.DoesNotContain("GAME_ON_HDD", Ids(p));
    }

    // ---------- Исправления ----------

    private static (ApplyReport Report, Dictionary<ChangeType, InMemoryBackend> Map, InMemoryChangeJournal Journal, BackendRegistry Reg) Apply(SystemProfile p, params string[] fixIds)
    {
        var reg = ProfileSeeder.CreateEmulation(p, out var map);
        var journal = new InMemoryChangeJournal();
        var fixes = fixIds.Select(id => FixCatalog.Find(id)!).ToList();
        return (new ApplyService(reg, journal, null).Apply(p, fixes, UserGoal.LowLatency), map, journal, reg);
    }

    [Fact]
    public void NetworkFixes_ApplyAndRollBack()
    {
        var p = TestProfiles.Load("old-gaming-pc");
        var (report, map, journal, reg) = Apply(p, FixIds.NetNagleOff, FixIds.NetEeeOff, FixIds.TrimOn);
        Assert.All(report.Results, r => Assert.Equal(FixOutcome.Applied, r.Outcome));

        var guid = p.Network[0].InterfaceGuid!;
        var r = map[ChangeType.Registry];
        Assert.Equal(StateValue.DWord(1), r.Read(Targets.Reg(NagleOffFix.InterfacesKey + guid, "TcpAckFrequency")));
        Assert.Equal(StateValue.DWord(1), r.Read(Targets.Reg(NagleOffFix.InterfacesKey + guid, "TCPNoDelay")));
        Assert.Equal(StateValue.DWord(0), r.Read(Targets.Reg(TrimOnFix.Key, TrimOnFix.ValueName)));
        Assert.Equal(StateValue.Str("0"), map[ChangeType.NetAdapter].Read(Targets.NetAdapterAdvanced("Ethernet", "*EEE")));
        Assert.Equal(StateValue.Str("0"), map[ChangeType.NetAdapter].Read(Targets.NetAdapterAdvanced("Ethernet", "EEELinkAdvertisement")));

        new RollbackService(reg, journal).RollbackAll();
        Assert.False(r.Read(Targets.Reg(NagleOffFix.InterfacesKey + guid, "TcpAckFrequency")).Exists);
        Assert.Equal(StateValue.DWord(1), r.Read(Targets.Reg(TrimOnFix.Key, TrimOnFix.ValueName)));
        Assert.Equal(StateValue.Str("1"), map[ChangeType.NetAdapter].Read(Targets.NetAdapterAdvanced("Ethernet", "*EEE")));
    }

    [Fact]
    public void Nagle_SelectedOnlyForLowLatencyGoal()
    {
        var p = TestProfiles.Load("old-gaming-pc");
        var findings = Run(p);
        var engine = new StrategyEngine(FixCatalog.All());
        Assert.True(engine.Build(p, findings, UserGoal.LowLatency).Fixes.Single(f => f.Fix.Id == FixIds.NetNagleOff).Selected);
        var fps = engine.Build(p, findings, UserGoal.MaxFps).Fixes.Single(f => f.Fix.Id == FixIds.NetNagleOff);
        Assert.False(fps.Selected);
        Assert.NotEmpty(fps.Notes);
        // Безопасные исправления отмечены при любой цели
        Assert.True(engine.Build(p, findings, UserGoal.MaxFps).Fixes.Single(f => f.Fix.Id == FixIds.NetEeeOff).Selected);
        Assert.True(engine.Build(p, findings, UserGoal.MaxFps).Fixes.Single(f => f.Fix.Id == FixIds.TrimOn).Selected);
    }

    [Fact]
    public void EveryFixIdUsedByDetectors_ExistsInCatalog()
    {
        var catalog = FixCatalog.All().Select(f => f.Id).ToHashSet();
        foreach (var name in TestProfiles.All)
            foreach (var f in Run(TestProfiles.Load(name)))
                Assert.All(f.FixIds, id => Assert.Contains(id, catalog));
    }

    // ---------- Твики ----------

    [Fact]
    public void GamingTweaks_AreListedFirstAndToggleCleanly()
    {
        Assert.Equal(TweakCategory.Gaming, TweakCatalog.All[0].Category);
        var gaming = TweakCatalog.All.Where(t => t.Category == TweakCategory.Gaming).ToList();
        Assert.True(gaming.Count >= 5);
        Assert.False(string.IsNullOrEmpty(TweakCatalog.CategoryTitle(TweakCategory.Gaming)));

        var reg = InMemoryBackend.CreateRegistry(out var map);
        var svc = new TweakService(reg, new InMemoryChangeJournal());
        var before = map[ChangeType.Registry].Snapshot();
        foreach (var t in gaming)
        {
            Assert.False(svc.IsOn(t));
            svc.Enable(t);
            Assert.True(svc.IsOn(t));
        }
        foreach (var t in gaming) svc.Disable(t);
        Assert.Equal(before.Count, map[ChangeType.Registry].Snapshot().Count(kv => kv.Value.Exists));
    }
}
