using Opti.Analysis.Detectors;
using Opti.Core.Findings;
using Opti.Core.Json;
using Opti.Core.Profile;

namespace Opti.Tests;

internal static class TestProfiles
{
    public static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.FromHours(3));
    public static readonly string[] All = { "laptop-optimus", "desktop-rtx", "laptop-igpu", "weak-pc", "revios" };

    public static SystemProfile Load(string name) =>
        OptiJson.Deserialize<SystemProfile>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Profiles", name + ".json")));

    public static List<Finding> Findings(SystemProfile p) => DetectorCatalog.RunAll(p, now: Now);

    public static HashSet<string> FindingIds(string name) => Findings(Load(name)).Select(f => f.Id).ToHashSet();
}
