using GameTune.Core.Profile;

namespace GameTune.Analysis.Collectors;

public interface ICollector
{
    string Name { get; }
    void Collect(SystemProfile profile);
}

public sealed record CollectorTiming(string Name, TimeSpan Elapsed, string? Error);
