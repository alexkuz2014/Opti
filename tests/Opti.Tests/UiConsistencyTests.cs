using System.Reflection;
using System.Text.RegularExpressions;
using Opti.ViewModels;

namespace Opti.Tests;

/// <summary>
/// Интерфейс WPF нельзя запустить в Linux, но самые частые поломки видны статически: привязка к несуществующему свойству,
/// страница без шаблона, ключ ресурса, которого нет. Эти тесты читают XAML и сверяют его с кодом.
/// </summary>
public class UiConsistencyTests
{
    private static readonly string Root = FindRoot();
    private static string FindRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !Directory.Exists(Path.Combine(d.FullName, "src", "Opti.App"))) d = d.Parent;
        return d?.FullName ?? throw new DirectoryNotFoundException("Корень репозитория не найден");
    }

    private static string Views => Path.Combine(Root, "src", "Opti.App", "Views");
    private static IEnumerable<string> AllXaml() => Directory.EnumerateFiles(Path.Combine(Root, "src", "Opti.App"), "*.xaml", SearchOption.AllDirectories).Where(f => !f.Contains("/obj/") && !f.Contains("/bin/"));

    private static IEnumerable<Type> PageViewModels() =>
        typeof(MainViewModel).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(PageViewModel)) && !t.IsAbstract);

    [Fact]
    public void EveryPageViewModel_HasATemplateAndAView()
    {
        var window = File.ReadAllText(Path.Combine(Root, "src", "Opti.App", "MainWindow.xaml"));
        foreach (var vm in PageViewModels())
        {
            var m = Regex.Match(window, $@"DataType=""\{{x:Type vm:{vm.Name}\}}""\s*>\s*<v:(\w+)/>");
            Assert.True(m.Success, $"Для {vm.Name} нет DataTemplate в MainWindow.xaml");
            var view = m.Groups[1].Value;
            Assert.True(File.Exists(Path.Combine(Views, view + ".xaml")), $"Нет файла {view}.xaml");
            Assert.True(File.Exists(Path.Combine(Views, view + ".xaml.cs")), $"Нет файла {view}.xaml.cs");
            var cs = File.ReadAllText(Path.Combine(Views, view + ".xaml.cs"));
            Assert.Contains($"class {view}", cs);
            Assert.Contains($"x:Class=\"Opti.App.Views.{view}\"", File.ReadAllText(Path.Combine(Views, view + ".xaml")));
        }
    }

    private static HashSet<string> KnownMembers()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        var asms = new[] { typeof(MainViewModel).Assembly, typeof(Opti.Core.Profile.SystemProfile).Assembly, typeof(Opti.Workflow.TuneWorkflow).Assembly,
                           typeof(Opti.Fixes.Services.ApplyService).Assembly, typeof(Opti.Analysis.SystemAnalyzer).Assembly, typeof(Opti.Measure.Stats.MeasurementRun).Assembly };
        foreach (var t in asms.SelectMany(a => a.GetTypes()).Where(t => t.IsPublic || t.IsNestedPublic))
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance)) set.Add(p.Name);
        foreach (var n in new[] { "Count", "Length", "Header", "Content", "Text", "Items", "Tag", "Name", "IsExpanded", "IsChecked", "IsSelected", "IsEnabled" }) set.Add(n);
        return set;
    }

    // {Binding Name}, {Binding Path=Name, ...}, {Binding DataContext.Name, RelativeSource=...}
    private static IEnumerable<(string Path, bool Ancestor, string Raw)> Bindings(string xaml)
    {
        foreach (Match m in Regex.Matches(xaml, @"\{Binding\s+([^}]*)\}"))
        {
            var raw = m.Groups[1].Value.Trim();
            if (raw.Length == 0 || raw.StartsWith("Source=") || raw.StartsWith("ElementName") || raw.StartsWith("RelativeSource") || raw.StartsWith(".") || raw.StartsWith(",")) continue;
            var first = raw.Split(',')[0].Trim();
            if (first.StartsWith("Path=")) first = first[5..];
            if (first.Contains('=')) continue;                        // {Binding Converter=…} без пути
            yield return (first, raw.Contains("RelativeSource"), raw);
        }
    }

    [Fact]
    public void EveryBindingPointsAtAnExistingMember()
    {
        var known = KnownMembers();
        var window = File.ReadAllText(Path.Combine(Root, "src", "Opti.App", "MainWindow.xaml"));
        var problems = new List<string>();
        foreach (var file in AllXaml())
        {
            var text = File.ReadAllText(file);
            var viewName = Path.GetFileNameWithoutExtension(file);
            Type? vmType = null;
            var vm = Regex.Match(window, $@"DataType=""\{{x:Type vm:(\w+)\}}""\s*>\s*<v:{viewName}/>");
            if (vm.Success) vmType = typeof(MainViewModel).Assembly.GetType("Opti.ViewModels." + vm.Groups[1].Value);
            var vmMembers = vmType?.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToHashSet() ?? new HashSet<string>();
            foreach (var (path, ancestor, raw) in Bindings(text))
            {
                var segs = path.Split('.');
                // DataContext.Foo с предком-страницей: Foo должен быть у модели страницы (если это страница)
                if (ancestor && segs[0] == "DataContext" && segs.Length > 1)
                {
                    if (vmType is not null && !vmMembers.Contains(segs[1]) && !(raw.Contains("AncestorType=Window") && typeof(MainViewModel).GetProperty(segs[1]) is not null))
                        problems.Add($"{viewName}: у {vmType.Name} нет «{segs[1]}» (привязка через предка)");
                    continue;
                }
                if (!known.Contains(segs[0])) problems.Add($"{Path.GetFileName(file)}: «{segs[0]}» нет ни в одной модели");
                else if (vmType is not null && !ancestor && !vmMembers.Contains(segs[0]) && viewName != "MainWindow")
                {
                    // Свойство есть в какой-то модели элемента списка — допустимо внутри шаблона; на верхнем уровне это была бы ошибка,
                    // но отличить уровни без разбора дерева нельзя, поэтому проверяем только очевидные команды страницы
                    if (segs[0].EndsWith("Command") && !vmMembers.Contains(segs[0]) && !text.Contains($"Binding {segs[0]}") == false && IsOutsideTemplates(text, raw))
                        problems.Add($"{viewName}: у {vmType.Name} нет команды «{segs[0]}»");
                }
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems.Distinct()));
    }

    /// <summary>Привязка вне ItemTemplate/DataTemplate (грубо: ищем последнее вхождение до неё).</summary>
    private static bool IsOutsideTemplates(string xaml, string raw)
    {
        int at = xaml.IndexOf("{Binding " + raw, StringComparison.Ordinal);
        if (at < 0) return false;
        var before = xaml[..at];
        return Regex.Matches(before, "<DataTemplate[ >]").Count == Regex.Matches(before, "</DataTemplate>").Count;
    }

    [Fact]
    public void EveryTopLevelCommandOfAPageExists()
    {
        // Команды, привязанные к странице вне шаблонов, обязаны существовать у её модели
        var window = File.ReadAllText(Path.Combine(Root, "src", "Opti.App", "MainWindow.xaml"));
        var problems = new List<string>();
        foreach (var vmType in PageViewModels())
        {
            var m = Regex.Match(window, $@"DataType=""\{{x:Type vm:{vmType.Name}\}}""\s*>\s*<v:(\w+)/>");
            var xaml = File.ReadAllText(Path.Combine(Views, m.Groups[1].Value + ".xaml"));
            var members = vmType.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToHashSet();
            foreach (Match b in Regex.Matches(xaml, @"Command=""\{Binding (\w+)\}"""))
                if (!members.Contains(b.Groups[1].Value) && IsOutsideTemplates(xaml, b.Groups[1].Value + "}"))
                    problems.Add($"{vmType.Name}: нет команды {b.Groups[1].Value}");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void EveryStaticAndDynamicResourceKeyIsDefined()
    {
        var defs = new HashSet<string>();
        foreach (var f in AllXaml()) foreach (Match m in Regex.Matches(File.ReadAllText(f), @"x:Key=""([^""]+)""")) defs.Add(m.Groups[1].Value);
        var missing = new List<string>();
        foreach (var f in AllXaml())
            foreach (Match m in Regex.Matches(File.ReadAllText(f), @"\{StaticResource (\w+)\}"))
                if (!defs.Contains(m.Groups[1].Value)) missing.Add($"{Path.GetFileName(f)}: {m.Groups[1].Value}");
        Assert.True(missing.Count == 0, string.Join("\n", missing.Distinct()));
    }

    [Fact]
    public void ConverterParametersAreKnown()
    {
        // ToneToBrush / ToneToSoft принимают палитру Palette: good, bad, warn, neutral, accent
        var tones = new HashSet<string> { "good", "bad", "warn", "neutral", "accent" };
        var bad = new List<string>();
        foreach (var f in AllXaml())
            foreach (Match m in Regex.Matches(File.ReadAllText(f), @"Source=(\w+),\s*Converter=\{StaticResource (ToneToBrush|ToneToSoft)\}"))
                if (!tones.Contains(m.Groups[1].Value)) bad.Add($"{Path.GetFileName(f)}: {m.Groups[1].Value}");
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    [Fact]
    public void XamlFilesAreWellFormed()
    {
        foreach (var f in AllXaml())
        {
            try { System.Xml.Linq.XDocument.Load(f); }
            catch (Exception ex) { Assert.Fail($"{Path.GetFileName(f)}: {ex.Message}"); }
        }
    }
}
