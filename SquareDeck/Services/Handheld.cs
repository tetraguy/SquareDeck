using System.IO;
using Microsoft.Win32;

namespace SquareDeck.Services;

/// <summary>What SquareDeck can change on a handheld, beyond what works on any Windows PC.</summary>
[Flags]
public enum HandheldControl
{
    None = 0,
    /// <summary>TDP straight through the firmware (no RyzenAdj or other helper needed).</summary>
    FirmwareTdp = 1,
    /// <summary>The maker's performance modes (Silent / Performance / Turbo, Quiet / Balanced / Performance, Eco / Sport…).</summary>
    PowerModes = 2,
    FanCurves = 4,
    /// <summary>Fans at full speed on demand.</summary>
    FanBoost = 8,
    /// <summary>Stop charging at a set level.</summary>
    ChargeLimit = 16,
    /// <summary>RGB around the sticks.</summary>
    Lights = 32,
    /// <summary>The maker's own buttons can be given actions.</summary>
    Buttons = 64,
    /// <summary>Temperatures and fan speed from the firmware.</summary>
    Sensors = 128,
}

/// <summary>How SquareDeck talks to the device's firmware.</summary>
public enum HandheldVendor { Generic, Asus, Lenovo, Msi }

/// <summary>
/// A handheld SquareDeck recognizes, with its sensible power range. Recognition and the ranges follow
/// HandheldCompanion by Valkirie (github.com/Valkirie/HandheldCompanion, Devices/IDevice.cs and each device's class):
/// the motherboard's maker and product name (or Lenovo's system model), and for some, the processor.
/// HandheldCompanion is licensed CC BY-NC-SA 4.0; this list, adapted from it, is shared under the same licence.
/// </summary>
public sealed record HandheldModel(
    string Brand, string Name, bool Intel, int TdpMin, int TdpDefault, int TdpMax,
    HandheldVendor Vendor, HandheldControl Controls, string[] Makers, string[] Products,
    string[]? SystemModels = null, string? ProcessorHas = null, string? VersionIs = null);

public static class Handheld
{
    private static readonly string[] Asus = { "ASUSTEK COMPUTER INC." };
    private static readonly string[] Lenovo = { "LENOVO" };
    private static readonly string[] Msi = { "MICRO-STAR INTERNATIONAL CO., LTD." };
    private static readonly string[] Aya = { "AYADEVICE", "AYANEO", "AYA" };
    private static readonly string[] Gpd = { "GPD" };
    private static readonly string[] OneX = { "ONE-NETBOOK TECHNOLOGY CO., LTD.", "ONE-NETBOOK" };
    private static readonly string[] Aok = { "AOKZOE" };
    private static readonly string[] Ayn = { "AYN" };
    private static readonly string[] Valve = { "VALVE" };
    private static readonly string[] Zotac = { "PC PARTNER LIMITED", "ZOTAC" };

    private const HandheldControl AllyControls = HandheldControl.FirmwareTdp | HandheldControl.PowerModes | HandheldControl.FanCurves
        | HandheldControl.ChargeLimit | HandheldControl.Lights | HandheldControl.Buttons | HandheldControl.Sensors;
    private const HandheldControl LegionControls = HandheldControl.FirmwareTdp | HandheldControl.PowerModes | HandheldControl.FanBoost
        | HandheldControl.ChargeLimit | HandheldControl.Sensors;
    private const HandheldControl ClawControls = HandheldControl.FirmwareTdp | HandheldControl.PowerModes | HandheldControl.FanBoost
        | HandheldControl.ChargeLimit;

    private static HandheldModel M(string brand, string name, int min, int def, int max, string[] makers, params string[] products) =>
        new(brand, name, false, min, def, max, HandheldVendor.Generic, HandheldControl.None, makers, products);

    /// <summary>Every handheld SquareDeck recognizes (the most specific first where two share a product name).</summary>
    public static readonly HandheldModel[] Models =
    {
        // ASUS: everything through ASUS's firmware and the Ally's own HID device (see AllyHardware.cs).
        new("ASUS", "ROG Xbox Ally X20", false, 15, 20, 35, HandheldVendor.Asus, AllyControls, Asus, new[] { "RC74XA" }),
        new("ASUS", "ROG Xbox Ally X", false, 15, 20, 35, HandheldVendor.Asus, AllyControls, Asus, new[] { "RC73XA" }),
        new("ASUS", "ROG Xbox Ally", false, 6, 15, 20, HandheldVendor.Asus, AllyControls, Asus, new[] { "RC73YA" }),
        new("ASUS", "ROG Ally X", false, 5, 15, 30, HandheldVendor.Asus, AllyControls, Asus, new[] { "RC72LA" }),
        new("ASUS", "ROG Ally", false, 5, 15, 30, HandheldVendor.Asus, AllyControls, Asus, new[] { "RC71L" }),

        // Lenovo: Legion modes, power limits, fan boost and the 80% charge limit through Lenovo's WMI methods.
        new("Lenovo", "Legion Go", false, 5, 15, 30, HandheldVendor.Lenovo, LegionControls, Lenovo, Array.Empty<string>(), new[] { "83E1" }),
        new("Lenovo", "Legion Go 2", false, 15, 15, 35, HandheldVendor.Lenovo, LegionControls, Lenovo, Array.Empty<string>(), new[] { "83N0", "83N1" }),
        new("Lenovo", "Legion Go S (Z2 Go)", false, 5, 15, 30, HandheldVendor.Lenovo, LegionControls, Lenovo, Array.Empty<string>(), new[] { "83L3" }),
        new("Lenovo", "Legion Go S (Z1 Extreme)", false, 5, 15, 30, HandheldVendor.Lenovo, LegionControls, Lenovo, Array.Empty<string>(), new[] { "83N6", "83Q2", "83Q3" }),

        // MSI: user scenarios, power limits, fan boost and the charge limit through MSI's WMI interface.
        new("MSI", "Claw A1M", true, 20, 30, 45, HandheldVendor.Msi, ClawControls, Msi, new[] { "MS-1T41" }),
        new("MSI", "Claw 7 AI+ / Claw 8 AI+", true, 8, 17, 37, HandheldVendor.Msi, ClawControls, Msi, new[] { "MS-1T42", "MS-1T52" }),
        new("MSI", "Claw A8", false, 15, 20, 35, HandheldVendor.Msi, ClawControls, Msi, new[] { "MS-1T8K" }),
        new("MSI", "Claw 8 EX AI+", true, 20, 15, 37, HandheldVendor.Msi, ClawControls, Msi, new[] { "MS-1T91" }),

        // Valve.
        new("Valve", "Steam Deck LCD", false, 4, 10, 15, HandheldVendor.Generic, HandheldControl.None, Valve, new[] { "Jupiter" }),
        new("Valve", "Steam Deck OLED", false, 4, 10, 15, HandheldVendor.Generic, HandheldControl.None, Valve, new[] { "Galileo" }),

        // AYANEO.
        M("AYANEO", "AYANEO 3", 5, 15, 54, Aya, "AYANEO 3", "AB07"),
        M("AYANEO", "AYANEO 2S", 3, 15, 30, Aya, "AYANEO 2S", "GEEK 1S"),
        M("AYANEO", "AYANEO 2 / GEEK", 3, 15, 33, Aya, "AYANEO 2", "GEEK"),
        M("AYANEO", "AYANEO 2021", 3, 15, 25, Aya, "AYA NEO FOUNDER", "AYANEO 2021"),
        M("AYANEO", "AYANEO 2021 Pro", 3, 15, 25, Aya, "AYANEO 2021 Pro", "AYANEO 2021 Pro Retro Power"),
        M("AYANEO", "AIR", 3, 12, 15, Aya, "AIR"),
        M("AYANEO", "AIR Pro", 3, 12, 18, Aya, "AIR Pro"),
        M("AYANEO", "AIR 1S", 4, 15, 28, Aya, "AIR 1S", "AIR 1S Limited"),
        M("AYANEO", "AIR Pro 1S", 4, 15, 28, Aya, "AIR Pro 1S", "AYANEO AIR Pro 1S"),
        M("AYANEO", "AIR Lite", 3, 8, 12, Aya, "AIR Lite"),
        M("AYANEO", "AIR 2", 5, 15, 30, Aya, "AIR 2", "AIR2", "AYANEO AIR 2", "AB10"),
        M("AYANEO", "AIR Plus (AMD)", 3, 15, 33, Aya, "AB05-AMD"),
        M("AYANEO", "AIR Plus (Mendocino)", 5, 12, 15, Aya, "AB05-Mendocino"),
        M("AYANEO", "AIR Plus (Intel)", 5, 15, 55, Aya, "AB05-Intel") with { Intel = true },
        M("AYANEO", "AIR Plus (Intel N)", 5, 6, 15, Aya, "AB05N", "AB05NXX", "AIR Plus Nxx") with { Intel = true },
        M("AYANEO", "KUN", 3, 15, 54, Aya, "KUN"),
        M("AYANEO", "SLIDE", 3, 15, 28, Aya, "AS01"),
        M("AYANEO", "NEXT", 10, 15, 25, Aya, "NEXT Pro", "NEXT Advance", "NEXT"),
        M("AYANEO", "NEXT Lite", 3, 15, 25, Aya, "NEXT Lite"),
        M("AYANEO", "NEXT 2", 5, 15, 85, Aya, "NEXT II", "NEXT 2", "AYANEO NEXT II", "AYANEO NEXT 2", "AB09"),
        M("AYANEO", "FLIP KB", 3, 15, 28, Aya, "FLIP KB"),
        M("AYANEO", "FLIP DS", 3, 15, 28, Aya, "FLIP DS"),
        M("AYANEO", "FLIP 1S KB", 3, 15, 28, Aya, "FLIP 1S KB"),
        M("AYANEO", "FLIP 1S DS", 3, 15, 28, Aya, "FLIP 1S DS"),
        M("AYANEO", "FLIP 11", 5, 15, 54, Aya, "FLIP 11", "AYANEO FLIP 11"),

        // GPD.
        M("GPD", "WIN 2", 5, 10, 15, Gpd, "WIN2") with { Intel = true },
        M("GPD", "WIN 3", 7, 20, 25, Gpd, "G1618-03") with { Intel = true },
        M("GPD", "WIN 4 (6800U)", 5, 15, 28, Gpd, "G1618-04") with { ProcessorHas = "6800U" },
        M("GPD", "WIN 4 (HX 370)", 5, 15, 30, Gpd, "G1618-04") with { ProcessorHas = "HX 370" },
        M("GPD", "WIN 4", 5, 15, 30, Gpd, "G1618-04"),
        M("GPD", "WIN 5", 8, 55, 85, Gpd, "G1618-05"),
        M("GPD", "WIN Mini (HX 370)", 3, 15, 28, Gpd, "G1617-02"),
        M("GPD", "WIN Mini", 5, 15, 18, Gpd, "G1617-01"),
        M("GPD", "WIN Max 2 (Intel)", 15, 15, 28, Gpd, "G1619-03") with { Intel = true },
        M("GPD", "WIN Max 2", 3, 15, 28, Gpd, "G1619-04", "G1619-05"),

        // OneXPlayer.
        M("OneXPlayer", "OneXPlayer X1 (Intel)", 6, 15, 35, OneX, "ONEXPLAYER X1 i") with { Intel = true },
        M("OneXPlayer", "OneXPlayer X1 (AMD)", 15, 15, 30, OneX, "ONEXPLAYER X1 A", "ONEXPLAYER X1z"),
        M("OneXPlayer", "OneXPlayer X1 mini", 15, 15, 30, OneX, "ONEXPLAYER X1 mini"),
        M("OneXPlayer", "OneXPlayer X1 Pro", 15, 15, 30, OneX, "ONEXPLAYER X1Pro"),
        M("OneXPlayer", "OneXPlayer APEX", 25, 25, 65, OneX, "ONEXPLAYER APEX", "ONEXPLAYERAPEX"),
        M("OneXPlayer", "OneXPlayer X2", 3, 25, 35, OneX, "ONEXPLAYER X2"),
        M("OneXPlayer", "OneXPlayer X2 Mini Pro", 6, 35, 80, OneX, "ONEXPLAYER X2Mini PRO"),
        M("OneXPlayer", "OneXPlayer G1 (Intel)", 25, 25, 80, OneX, "ONEXPLAYER G1 i") with { Intel = true },
        M("OneXPlayer", "OneXPlayer G1 (AMD)", 25, 25, 65, OneX, "ONEXPLAYER G1 A"),
        M("OneXPlayer", "OneXFly F1", 5, 15, 30, OneX, "ONEXPLAYER F1"),
        M("OneXPlayer", "OneXFly F1 Pro", 5, 15, 30, OneX, "ONEXPLAYER F1Pro"),
        M("OneXPlayer", "OneXPlayer Mini (Intel)", 20, 28, 64, OneX, "ONE XPLAYER", "ONEXPLAYER Mini Pro") with { Intel = true, VersionIs = "1002-C" },
        M("OneXPlayer", "OneXPlayer Mini Pro", 4, 15, 28, OneX, "ONE XPLAYER", "ONEXPLAYER Mini Pro") with { VersionIs = "V03" },
        M("OneXPlayer", "OneXPlayer Mini (AMD)", 10, 15, 25, OneX, "ONE XPLAYER", "ONEXPLAYER Mini Pro", "ONEXPLAYER mini A07"),
        M("OneXPlayer", "OneXPlayer 2", 4, 15, 28, OneX, "ONEXPLAYER 2 ARP23"),
        M("OneXPlayer", "OneXPlayer 2 Pro", 4, 15, 30, OneX, "ONEXPLAYER 2 PRO ARP23P", "ONEXPLAYER 2 PRO ARP23P EVA-01"),
        M("OneXPlayer", "OneXPlayer 3", 3, 25, 35, OneX, "ONEXPLAYER 3", "ONEXPLAYER-3"),

        // AOKZOE.
        M("AOKZOE", "AOKZOE A1", 4, 15, 28, Aok, "AOKZOE A1 AR07"),
        M("AOKZOE", "AOKZOE A1 Pro", 4, 15, 28, Aok, "AOKZOE A1 Pro"),
        M("AOKZOE", "AOKZOE A1X", 4, 15, 28, Aok, "AOKZOE A1X"),
        M("AOKZOE", "AOKZOE A2 Pro", 4, 15, 28, Aok, "AOKZOE A2 Pro"),

        // AYN.
        M("AYN", "Loki Max", 5, 15, 28, Ayn, "Loki Max"),
        M("AYN", "Loki MiniPro", 5, 12, 15, Ayn, "Loki MiniPro"),
        M("AYN", "Loki Zero", 5, 10, 15, Ayn, "Loki Zero"),

        // Others.
        M("ZOTAC", "ZOTAC Gaming Zone", 8, 15, 30, Zotac, "G0A1W"),
        M("Minisforum", "Minisforum V3", 3, 15, 28, new[] { "SHENZHEN MEIGAO ELECTRONIC EQUIPMENT CO.,LTD" }, "HPPAC"),
        M("Mysten Labs", "SuiPlay0X1", 3, 15, 54, new[] { "MYSTEN LABS, INC." }, "SuiPlay0X1"),
    };

    private const string BiosKey = @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS";

    private static string Bios(string name) => (Registry.GetValue(BiosKey, name, "") as string ?? "").Trim();

    /// <summary>The motherboard's maker, its product and version, the system model and the processor, as Windows reports them.</summary>
    public static (string Maker, string Product, string Version, string SystemModel, string SystemMaker, string Processor) Identity
    {
        get
        {
            try
            {
                return (Bios("BaseBoardManufacturer"), Bios("BaseBoardProduct"), Bios("BaseBoardVersion"),
                        Bios("SystemProductName"), Bios("SystemManufacturer"), TdpControl.CpuName);
            }
            catch (Exception) { return ("", "", "", "", "", ""); }
        }
    }

    private static bool _checked;
    private static HandheldModel? _model;

    /// <summary>The handheld this is, or null on any other PC. Read once.</summary>
    public static HandheldModel? Current
    {
        get
        {
            if (_checked) return _model;
            _checked = true;
            try { _model = Detect(Identity); } catch (Exception) { _model = null; }
            return _model;
        }
    }

    internal static HandheldModel? Detect((string Maker, string Product, string Version, string SystemModel, string SystemMaker, string Processor) id)
    {
        string maker = id.Maker.ToUpperInvariant();
        string systemMaker = id.SystemMaker.ToUpperInvariant();
        foreach (var m in Models)
        {
            bool makerOk = m.Makers.Any(x => x == maker || x == systemMaker);
            if (!makerOk) continue;
            bool productOk = m.Products.Length > 0 && m.Products.Any(p => string.Equals(p, id.Product, StringComparison.OrdinalIgnoreCase));
            bool modelOk = m.SystemModels != null && m.SystemModels.Any(p => string.Equals(p, id.SystemModel, StringComparison.OrdinalIgnoreCase));
            if (!productOk && !modelOk) continue;
            if (m.ProcessorHas != null && !id.Processor.Contains(m.ProcessorHas, StringComparison.OrdinalIgnoreCase)) continue;
            if (m.VersionIs != null && !string.Equals(m.VersionIs, id.Version, StringComparison.OrdinalIgnoreCase)) continue;
            return m;
        }
        return null;
    }

    /// <summary>An Ally that the table didn't match by board name, but its own detection (or Settings) did.</summary>
    private static bool AllyKnown => AllyButtons.ModelOverride.Length > 0 || AllyButtons.Model.Length > 0;

    /// <summary>A handheld was recognized (or chosen as an Ally in Settings).</summary>
    public static bool IsHandheld => Current != null || AllyKnown;

    /// <summary>The model's name for headings ("ROG Xbox Ally X20"), or "Handheld".</summary>
    public static string DisplayName
    {
        get
        {
            if (Current is { Vendor: HandheldVendor.Asus } && AllyButtons.Model.Length > 0) return AllyButtons.Model;
            if (Current != null) return Current.Name;
            if (AllyKnown) return AllyButtons.Model;
            return "Handheld";
        }
    }

    public static string Brand => Current?.Brand ?? (AllyKnown ? "ASUS" : "");

    /// <summary>
    /// A picture of this handheld for the My Device page (Devices\&lt;board&gt;.png next to SquareDeck), or null when there
    /// isn't one for the model. Follows the model SquareDeck settled on, so choosing one in Settings changes it too.
    /// </summary>
    public static string? ImagePath
    {
        get
        {
            string? board = DisplayName switch
            {
                "ROG Xbox Ally X20" => "RC74XA",
                "ROG Xbox Ally X" => "RC73XA",
                "ROG Xbox Ally" => "RC73YA",
                "ROG Ally X" => "RC72LA",
                "ROG Ally" => "RC71L",
                _ => null
            };
            if (board == null) return null;
            string path = Path.Combine(AppContext.BaseDirectory, "Devices", board + ".png");
            return File.Exists(path) ? path : null;
        }
    }

    public static HandheldVendor Vendor => Current?.Vendor ?? (AllyKnown ? HandheldVendor.Asus : HandheldVendor.Generic);

    public static bool Has(HandheldControl c) => ((Current?.Controls ?? (Vendor == HandheldVendor.Asus ? AllyControls : HandheldControl.None)) & c) != 0;

    /// <summary>What SquareDeck can change on a model, in words, for the supported devices list.</summary>
    public static string ControlsText(HandheldModel m)
    {
        var parts = new List<string> { m.Intel ? "TDP (Intel)" : "TDP" };
        if ((m.Controls & HandheldControl.PowerModes) != 0) parts.Add("power modes");
        if ((m.Controls & HandheldControl.FanCurves) != 0) parts.Add("fan curves");
        if ((m.Controls & HandheldControl.FanBoost) != 0) parts.Add("fan boost");
        if ((m.Controls & HandheldControl.ChargeLimit) != 0) parts.Add("charge limit");
        if ((m.Controls & HandheldControl.Lights) != 0) parts.Add("lights");
        if ((m.Controls & HandheldControl.Buttons) != 0) parts.Add("buttons");
        if ((m.Controls & HandheldControl.Sensors) != 0) parts.Add("temperatures");
        return string.Join(", ", parts);
    }
}
