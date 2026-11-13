using Opti.Fixes.Catalog;

namespace Opti.Tests;

/// <summary>Проверка текстов находок на всех эталонных ПК: пустые поля, «NaN», «null», сырые скобки, битые ссылки, неизвестные исправления.</summary>
public class FindingContentLintTests
{
    [Fact]
    public void FindingsOnAllProfiles_AreWellFormed()
    {
        var known = FixCatalog.All().Select(f => f.Id).ToHashSet();
        var problems = new List<string>();
        foreach (var name in TestProfiles.All)
        {
            var list = TestProfiles.Findings(TestProfiles.Load(name));
            foreach (var dup in list.GroupBy(f => f.Id).Where(g => g.Count() > 1)) problems.Add($"{name}: дубликат {dup.Key}");
            foreach (var f in list)
            {
                var all = new[] { f.Title, f.Explanation }.Concat(f.Evidence).Concat(f.ManualSteps);
                if (string.IsNullOrWhiteSpace(f.Title) || string.IsNullOrWhiteSpace(f.Explanation)) problems.Add($"{name}/{f.Id}: пустой заголовок или объяснение");
                foreach (var t in all)
                {
                    if (string.IsNullOrWhiteSpace(t)) problems.Add($"{name}/{f.Id}: пустая строка");
                    else if (t.Contains("NaN") || t.Contains("∞") || t.Contains("{0}") || t.Contains("null", StringComparison.Ordinal) || t.Contains("System.")) problems.Add($"{name}/{f.Id}: «{t}»");
                }
                foreach (var l in f.Links) if (!l.StartsWith("https://") && !l.StartsWith("ms-settings:")) problems.Add($"{name}/{f.Id}: ссылка {l}");
                foreach (var id in f.FixIds) if (!known.Contains(id)) problems.Add($"{name}/{f.Id}: неизвестное исправление {id}");
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems.Distinct()));
    }
}
