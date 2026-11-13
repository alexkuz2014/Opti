using Opti.Core;
using Opti.Core.Fixes;
using Opti.Fixes.Backends;
using Opti.Fixes.Journal;
using Opti.Fixes.Services;
using Opti.Workflow;

namespace Opti.Tests;

public class RollbackConflictTests
{
    [Fact]
    public void UpdateSource_PointsToOfficialRepository() =>
        Assert.Equal("https://github.com/alexkuz2014/Opti/releases", AppInfo.ReleasesUrl);

    [Fact]
    public void Rollback_DoesNotOverwriteValueChangedLater()
    {
        var reg = InMemoryBackend.CreateRegistry(out var map);
        var target = Targets.Reg(@"HKEY_CURRENT_USER\Software\Test", "V");
        map[ChangeType.Registry].Seed(target, StateValue.DWord(1));
        var journal = new InMemoryChangeJournal();
        var ctx = new ChangeContext(reg, journal, "s1", "FIX", new Opti.Core.Profile.SystemProfile());
        ctx.Set(ChangeType.Registry, target, StateValue.DWord(2));
        map[ChangeType.Registry].Write(target, StateValue.DWord(7));   // пользователь выбрал своё

        var rep = new RollbackService(reg, journal).RollbackAll();

        Assert.Empty(rep.Restored);
        Assert.Single(rep.SkippedChangedExternally);
        Assert.Equal(StateValue.DWord(7), map[ChangeType.Registry].Read(target));
    }

    [Fact]
    public void Rollback_RestoresWhenValueIsStillOurs()
    {
        var reg = InMemoryBackend.CreateRegistry(out var map);
        var target = Targets.Reg(@"HKEY_CURRENT_USER\Software\Test", "V");
        map[ChangeType.Registry].Seed(target, StateValue.DWord(1));
        var journal = new InMemoryChangeJournal();
        new ChangeContext(reg, journal, "s1", "FIX", new Opti.Core.Profile.SystemProfile()).Set(ChangeType.Registry, target, StateValue.DWord(2));

        var rep = new RollbackService(reg, journal).RollbackAll();

        Assert.Single(rep.Restored);
        Assert.Equal(StateValue.DWord(1), map[ChangeType.Registry].Read(target));
    }

    [Fact]
    public void Set_ReportsUnverified_WhenSystemIgnoresWrite()
    {
        var reg = InMemoryBackend.CreateRegistry(out var map);
        var target = Targets.Reg(@"HKEY_CURRENT_USER\Software\Test", "V");
        map[ChangeType.Registry].Seed(target, StateValue.DWord(1));
        var ctx = new ChangeContext(new IgnoringRegistry(reg), new InMemoryChangeJournal(), "s", "FIX", new Opti.Core.Profile.SystemProfile());
        ctx.Set(ChangeType.Registry, target, StateValue.DWord(2));
        Assert.Single(ctx.Unverified);
    }

    private sealed class IgnoringRegistry(IBackendRegistry inner) : IBackendRegistry
    {
        public IStateBackend Get(ChangeType type) => new Ignoring(inner.Get(type));
        private sealed class Ignoring(IStateBackend b) : IStateBackend
        {
            public ChangeType Type => b.Type;
            public StateValue Read(string t) => b.Read(t);
            public void Write(string t, StateValue v) { }
        }
    }
}
