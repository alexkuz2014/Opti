namespace Opti.Core.Hardware;

/// <summary>Фоновое ПО периферии (мышь, клавиатура, подсветка): тяжёлые фреймворки, которые постоянно работают в фоне.</summary>
public static class PeripheralApps
{
    /// <summary>Имя процесса → название программы.</summary>
    public static readonly IReadOnlyDictionary<string, string> Known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["lghub"] = "Logitech G HUB", ["lghub_agent"] = "Logitech G HUB", ["lghub_updater"] = "Logitech G HUB", ["lghub_system_tray"] = "Logitech G HUB",
        ["Razer Synapse 3"] = "Razer Synapse", ["RazerCentralService"] = "Razer Synapse", ["Razer Synapse Service"] = "Razer Synapse", ["RazerAppEngine"] = "Razer Synapse", ["Razer Central"] = "Razer Synapse",
        ["ArmouryCrate.Service"] = "ASUS Armoury Crate", ["ArmouryCrate.UserSessionHelper"] = "ASUS Armoury Crate", ["ArmourySocketServer"] = "ASUS Armoury Crate", ["ArmouryCrateControlInterface"] = "ASUS Armoury Crate",
        ["SteelSeriesGGClient"] = "SteelSeries GG", ["SteelSeriesEngine"] = "SteelSeries GG", ["SteelSeriesGG"] = "SteelSeries GG", ["SteelSeriesSonar"] = "SteelSeries GG",
        ["iCUE"] = "Corsair iCUE", ["Corsair.Service"] = "Corsair iCUE", ["CorsairLLAccessService"] = "Corsair iCUE",
        ["SignalRgb"] = "SignalRGB", ["OpenRGB"] = "OpenRGB",
        ["HyperX NGENUITY"] = "HyperX NGENUITY", ["NGENUITY"] = "HyperX NGENUITY",
        ["Mystic_Light"] = "MSI Mystic Light", ["LightKeeperService"] = "MSI Mystic Light",
    };

    public static IEnumerable<string> Match(IEnumerable<string> processNames) =>
        processNames.Where(Known.ContainsKey).Select(n => Known[n]).Distinct(StringComparer.OrdinalIgnoreCase);
}
