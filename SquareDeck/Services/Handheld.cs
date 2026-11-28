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

/// <summary>How SquareDeck talks to the device's firmware: ASUS's, or none (any other PC).</summary>
public enum HandheldVendor { Generic, Asus }

/// <summary>
/// A ROG Ally SquareDeck recognizes (8.5.81: SquareDeck is made for ASUS's handhelds only), by the motherboard's maker
/// and ASUS's board code, with its power range in watts.
/// </summary>
public sealed record HandheldModel(
    string Brand, string Name, bool Intel, int TdpMin, int TdpDefault, int TdpMax,
    HandheldVendor Vendor, HandheldControl Controls, string[] Makers, string[] Products,
    string[]? SystemModels = null, string? ProcessorHas = null, string? VersionIs = null);

public static class Handheld
{
    private static readonly string[] Asus = { "ASUSTEK COMPUTER INC." };

    private const HandheldControl AllyControls = HandheldControl.FirmwareTdp | HandheldControl.PowerModes | HandheldControl.FanCurves
        | HandheldControl.ChargeLimit | HandheldControl.Lights | HandheldControl.Buttons | HandheldControl.Sensors;

    /// <summary>The ROG Ally family, newest first: everything through ASUS's firmware and the Ally's own HID device (see AllyHardware.cs).</summary>
    public static readonly HandheldModel[] Models =
    {
        new("ASUS", "ROG Xbox Ally X20", false, 15, 20, 35, HandheldVendor.Asus, AllyControls, Asus, new[] { "RC74XA" }),
        new("ASUS", "ROG Xbox Ally X", false, 15, 20, 35, HandheldVendor.Asus, AllyControls, Asus, new[] { "RC73XA" }),
        new("ASUS", "ROG Xbox Ally", false, 6, 15, 20, HandheldVendor.Asus, AllyControls, Asus, new[] { "RC73YA" }),
        new("ASUS", "ROG Ally X", false, 5, 15, 30, HandheldVendor.Asus, AllyControls, Asus, new[] { "RC72LA" }),
        new("ASUS", "ROG Ally", false, 5, 15, 30, HandheldVendor.Asus, AllyControls, Asus, new[] { "RC71L" }),
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

    /// <summary>The ROG Ally this is, or null on any other PC. Read once.</summary>
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

    /// <summary>A ROG Ally was recognized (or chosen as an Ally in Settings).</summary>
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

    /// <summary>
    /// 9.0.19: the page on ASUS's site with this model's user manual, opened inside SquareDeck; null for anything else.
    /// The ROG Xbox Ally and Xbox Ally X share one page. Follows the model SquareDeck settled on, like the picture.
    /// </summary>
    public static string? ManualUrl => DisplayName switch
    {
        "ROG Xbox Ally X20" => "https://rog.asus.com/us/gaming-handhelds/rog-ally/rog-xbox-ally-x20-2026/helpdesk_manual/",
        "ROG Xbox Ally X" or "ROG Xbox Ally" => "https://rog.asus.com/gaming-handhelds/rog-ally/rog-xbox-ally-2025/helpdesk_manual/",
        "ROG Ally X" => "https://rog.asus.com/gaming-handhelds/rog-ally/rog-ally-x-2024/helpdesk_manual/",
        "ROG Ally" => "https://rog.asus.com/gaming-handhelds/rog-ally/rog-ally-2023/helpdesk_manual/",
        _ => null
    };

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
