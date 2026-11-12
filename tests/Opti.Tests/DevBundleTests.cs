using System.IO.Compression;
using Opti.Analysis;
using Opti.Fixes.Journal;
using Opti.Workflow.Dev;

namespace Opti.Tests;

public class DevBundleTests
{
    [Fact]
    public void Redact_HidesUserMachineAndMac()
    {
        var text = @"C:\Users\Ivan\Games, ПК IVAN-PC, MAC 00-1A-2B-3C-4D-5E и aa:bb:cc:dd:ee:ff, ivan";
        var r = DevBundle.Redact(text, "Ivan", "IVAN-PC");
        Assert.DoesNotContain("Ivan", r, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("1A-2B", r);
        Assert.DoesNotContain("aa:bb", r);
        Assert.Contains(@"C:\Users\<user>\Games", r);
        Assert.Contains("<pc>", r);
    }

    [Fact]
    public void Redact_ShortNamesAreLeftAlone()
    {
        // Имя из одной-двух букв заменило бы половину текста
        Assert.Equal("a b ab", DevBundle.Redact("a b ab", "ab", "pc"));
    }

    [Fact]
    public void Bundle_ContainsAnalysisPlansAndNoPersonalData()
    {
        var profile = TestProfiles.Load("old-gaming-pc");
        profile.Label = Environment.MachineName;
        var path = Path.Combine(Path.GetTempPath(), $"opti-dev-{Guid.NewGuid():N}.zip");
        try
        {
            DevBundle.Write(path, new DevBundle.Input
            {
                AppVersion = "test",
                Reason = "тест",
                Analysis = SystemAnalyzer.FromProfile(profile),
                Journal = new InMemoryChangeJournal(),
                Probes = new Dictionary<string, string> { ["ipconfig"] = "Физический адрес: 00-1A-2B-3C-4D-5E" },
            });
            using var zip = ZipFile.OpenRead(path);
            var names = zip.Entries.Select(e => e.FullName).ToHashSet();
            foreach (var n in new[] { "meta.txt", "analysis/profile.json", "analysis/findings.json", "analysis/findings.txt", "analysis/plans.json", "state/journal.json", "system/ipconfig.txt" })
                Assert.Contains(n, names);
            Assert.DoesNotContain("bundle-errors.txt", names);

            string Read(string n) { using var r = new StreamReader(zip.GetEntry(n)!.Open()); return r.ReadToEnd(); }
            Assert.Contains("NET_ISP_UNSTABLE", Read("analysis/findings.txt"));
            Assert.Contains("LowLatency", Read("analysis/plans.json"));
            Assert.DoesNotContain("00-1A-2B", Read("system/ipconfig.txt"));
            if (Environment.MachineName.Length >= 3)
                Assert.All(zip.Entries.Where(e => e.FullName.EndsWith(".json") || e.FullName.EndsWith(".txt")),
                    e => Assert.DoesNotContain(Environment.MachineName, Read(e.FullName), StringComparison.OrdinalIgnoreCase));
        }
        finally { File.Delete(path); File.Delete(path + ".tmp"); }
    }

    [Fact]
    public void Bundle_WithoutAnalysisExplainsWhy()
    {
        var path = Path.Combine(Path.GetTempPath(), $"opti-dev-{Guid.NewGuid():N}.zip");
        try
        {
            DevBundle.Write(path, new DevBundle.Input { AppVersion = "test", Reason = "запуск" });
            using var zip = ZipFile.OpenRead(path);
            Assert.NotNull(zip.GetEntry("analysis/README.txt"));
            Assert.NotNull(zip.GetEntry("system/README.txt"));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally { File.Delete(path); }
    }
}
