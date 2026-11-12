using System.Runtime.Versioning;
using Opti.Core.Fixes;
using Opti.Native;

namespace Opti.Fixes.Backends;

/// <summary>Частота экрана. Target: «\\.\DISPLAY1|refresh», значение — Гц.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDisplayBackend : IStateBackend
{
    public ChangeType Type => ChangeType.Display;

    public StateValue Read(string target)
    {
        var (device, prop) = Targets.Split2(target);
        if (prop != "refresh") throw new NotSupportedException(prop);
        var mode = DisplayNative.GetCurrentMode(device) ?? throw new IOException($"Экран {device} не найден");
        return StateValue.Str(mode.Hz.ToString());
    }

    public void Write(string target, StateValue value)
    {
        var (device, _) = Targets.Split2(target);
        if (!value.Exists) return;
        DisplayNative.SetRefreshRate(device, int.Parse(value.Value!));
    }
}

/// <summary>Управление питанием сетевого адаптера через NetAdapter (PowerShell). Target: «Имя|AllowComputerToTurnOffDevice».</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsNetAdapterBackend : IStateBackend
{
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
        { "AllowComputerToTurnOffDevice", "SelectiveSuspend", "WakeOnMagicPacket", "WakeOnPattern", "Rss" };

    private readonly ProcessRunner _runner;
    public WindowsNetAdapterBackend(ProcessRunner? runner = null) => _runner = runner ?? ProcessRunner.Instance;
    public ChangeType Type => ChangeType.NetAdapter;

    public StateValue Read(string target)
    {
        var (name, prop) = Split(target);
        if (prop.StartsWith(Targets.AdvancedPrefix, StringComparison.Ordinal))
        {
            var kw = prop[Targets.AdvancedPrefix.Length..];
            var a = _runner.RunPowerShell($"$p = Get-NetAdapterAdvancedProperty -Name {ProcessRunner.PsQuote(name)} -RegistryKeyword {ProcessRunner.PsQuote(kw)} -ErrorAction SilentlyContinue; " +
                                          "if ($p) { [string]($p.RegistryValue | Select-Object -First 1) }");
            if (!a.Ok) throw new IOException(a.StdErr.Trim());
            var av = a.StdOut.Trim();
            return av.Length == 0 ? StateValue.Missing : StateValue.Str(av);
        }
        if (prop == "Rss")
        {
            var q = _runner.RunPowerShell($"[string](Get-NetAdapterRss -Name {ProcessRunner.PsQuote(name)}).Enabled");
            if (!q.Ok) throw new IOException(q.StdErr.Trim());
            return q.StdOut.Trim() switch { "True" => StateValue.Str("Enabled"), "False" => StateValue.Str("Disabled"), _ => StateValue.Missing };
        }
        var r = _runner.RunPowerShell($"(Get-NetAdapterPowerManagement -Name {ProcessRunner.PsQuote(name)}).{prop}");
        if (!r.Ok) throw new IOException(r.StdErr.Trim());
        var v = r.StdOut.Trim();
        return v.Length == 0 ? StateValue.Missing : StateValue.Str(v);
    }

    public void Write(string target, StateValue value)
    {
        var (name, prop) = Split(target);
        if (prop.StartsWith(Targets.AdvancedPrefix, StringComparison.Ordinal))
        {
            // Свойства не было — возвращать нечего; значение проверяем, чтобы в команду не попало ничего лишнего
            if (!value.Exists || value.Value is null || !value.Value.All(char.IsLetterOrDigit)) return;
            var kw = prop[Targets.AdvancedPrefix.Length..];
            // -NoRestart: без него адаптер перезапускается и сеть пропадает на несколько секунд. Вступает в силу после перезагрузки.
            var a = _runner.RunPowerShell($"Set-NetAdapterAdvancedProperty -Name {ProcessRunner.PsQuote(name)} -RegistryKeyword {ProcessRunner.PsQuote(kw)} -RegistryValue {ProcessRunner.PsQuote(value.Value)} -NoRestart");
            if (!a.Ok) throw new IOException(a.StdErr.Trim());
            return;
        }
        if (!value.Exists || value.Value is not ("Enabled" or "Disabled")) return;
        if (prop == "Rss")
        {
            var cmd = value.Value == "Enabled" ? "Enable-NetAdapterRss" : "Disable-NetAdapterRss";
            var rr = _runner.RunPowerShell($"{cmd} -Name {ProcessRunner.PsQuote(name)}");
            if (!rr.Ok) throw new IOException(rr.StdErr.Trim());
            return;
        }
        var r = _runner.RunPowerShell($"Set-NetAdapterPowerManagement -Name {ProcessRunner.PsQuote(name)} -{prop} {value.Value} -NoRestart");
        if (!r.Ok) throw new IOException(r.StdErr.Trim());
    }

    private static (string, string) Split(string target)
    {
        var (name, prop) = Targets.Split2(target);
        if (prop.StartsWith(Targets.AdvancedPrefix, StringComparison.Ordinal))
        {
            var kw = prop[Targets.AdvancedPrefix.Length..];
            if (kw.Length == 0 || !kw.All(c => char.IsLetterOrDigit(c) || c == '*')) throw new NotSupportedException($"Свойство адаптера {prop} не поддерживается");
            return (name, prop);
        }
        if (!Allowed.Contains(prop)) throw new NotSupportedException($"Свойство адаптера {prop} не поддерживается");
        return (name, prop);
    }
}
