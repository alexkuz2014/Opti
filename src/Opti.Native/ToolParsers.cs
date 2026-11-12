using System.Globalization;
using System.Text.RegularExpressions;

namespace Opti.Native;

/// <summary>Разбор вывода bcdedit и powercfg (не зависит от Windows — покрыт тестами).</summary>
public static partial class ToolParsers
{
    /// <summary>bcdedit /enum: имена элементов выводятся по-английски и в локализованной Windows; логические значения — Yes/No.</summary>
    public static Dictionary<string, string> ParseBcdEnum(string output)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in output.Split('\n'))
        {
            var m = BcdLineRegex().Match(line.TrimEnd('\r'));
            if (!m.Success) continue;
            map.TryAdd(m.Groups[1].Value, NormalizeBool(m.Groups[2].Value.Trim()));
        }
        return map;
    }

    private static string NormalizeBool(string v) => v.ToLowerInvariant() switch
    {
        "yes" or "да" or "on" or "true" => "Yes",
        "no" or "нет" or "off" or "false" => "No",
        _ => v,
    };

    /// <summary>powercfg /query для одного параметра: последние два шестнадцатеричных значения — текущие индексы AC и DC.</summary>
    public static (uint? Ac, uint? Dc) ParsePowercfgIndexes(string output)
    {
        var hex = PowerHexRegex().Matches(output).Select(m => uint.Parse(m.Groups[1].Value, NumberStyles.HexNumber)).ToList();
        if (hex.Count < 2) return (null, null);
        return (hex[^2], hex[^1]);
    }

    /// <summary>
    /// Вывод nvidia-smi --query-gpu=clocks_throttle_reasons.* --format=csv,noheader: «Not Active, Active».
    /// Троттлинг — только если какое-то поле равно ровно «Active» (подстрока есть и в «Not Active»).
    /// </summary>
    public static bool ParseNvidiaThrottleActive(string output) =>
        output.Split('\n').Any(line => line.Split(',').Any(f => f.Trim().Equals("Active", StringComparison.OrdinalIgnoreCase)));

    /// <summary>Размер BAR1 из «nvidia-smi -q -d MEMORY»; null — строки нет.</summary>
    public static int? ParseNvidiaBar1TotalMb(string output)
    {
        var m = Bar1Regex().Match(output);
        return m.Success && int.TryParse(m.Groups[1].Value, out var v) ? v : null;
    }

    [GeneratedRegex(@"BAR1 Memory Usage\s*\r?\n\s*Total\s*:\s*(\d+)\s*MiB", RegexOptions.IgnoreCase)] private static partial Regex Bar1Regex();
    [GeneratedRegex(@"^([a-z][a-z0-9]+)\s{2,}(.+)$")] private static partial Regex BcdLineRegex();
    [GeneratedRegex(@":\s*0x([0-9a-fA-F]{1,8})\s*$", RegexOptions.Multiline)] private static partial Regex PowerHexRegex();
}
