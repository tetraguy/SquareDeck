using System.Text.Json.Nodes;
using SquareDeck.Services;
using Windows.Devices.Radios;

namespace SquareDeck;

/// <summary>
/// The widget's Wi-Fi and Bluetooth pickers and its airplane mode tile. Selecting the Wi-Fi or Bluetooth tile opens a
/// panel in the widget; it asks for networks or devices here and SquareDeck answers with a "wifi" or "bluetooth"
/// message (the radio's switch, the list, and what's happening).
/// </summary>
public partial class MainWindow
{
    private static readonly HashSet<string> WidgetRadioOps = new()
    {
        "radio", "wifiscan", "wificonnect", "wifidisconnect", "btlist", "btconnect", "btpair", "btforget",
    };

    private readonly Dictionary<string, WifiNetwork> _widgetNetworks = new(StringComparer.Ordinal);
    private bool? _widgetWifi, _widgetBluetooth, _widgetAirplane;
    private bool _widgetRadiosBusy, _widgetScanning, _widgetBtScanning;
    private List<BluetoothItem>? _widgetNearby;

    /// <summary>The radios' states for the tiles, read every few seconds (off the window's thread).</summary>
    private async Task RefreshWidgetRadiosAsync()
    {
        if (_widgetRadiosBusy) return;
        _widgetRadiosBusy = true;
        try
        {
            _widgetWifi = await NetworkService.GetRadioStateAsync(RadioKind.WiFi);
            _widgetBluetooth = await NetworkService.GetRadioStateAsync(RadioKind.Bluetooth);
            _widgetAirplane = await Task.Run(() => Wireless.AirplaneMode);
        }
        catch (Exception) { }
        finally { _widgetRadiosBusy = false; }
    }

    private void AddWidgetRadioToggles(JsonObject toggles)
    {
        toggles["wifi"] = _widgetWifi;
        toggles["bluetooth"] = _widgetBluetooth;
        toggles["airplane"] = _widgetAirplane;
    }

    /// <summary>Airplane mode on or off (the quick menu, the widget and the Ally's buttons).</summary>
    private async Task ToggleAirplaneAsync()
    {
        bool? now = await Task.Run(() => Wireless.AirplaneMode);
        if (now == null) { ProcessUtil.Open("ms-settings:network-airplanemode"); Note("Opened airplane mode settings"); return; }
        bool ok = await Task.Run(() => Wireless.SetAirplaneMode(!now.Value));
        Note(ok ? (now.Value ? "Airplane mode off" : "Airplane mode on") : "Couldn't change airplane mode");
        await Task.Delay(800);
        await RefreshWidgetRadiosAsync();
        WidgetBridge.Nudge();
    }

    private async void WidgetRadioRequest(string op, JsonObject request)
    {
        string? Str(string name) { try { return request[name]?.GetValue<string>(); } catch (Exception) { return null; } }
        bool? Bool(string name) { try { return request[name]?.GetValue<bool>(); } catch (Exception) { return null; } }
        try
        {
            switch (op)
            {
                case "radio" when Str("kind") is { } kind && Bool("on") is bool on:
                {
                    var radio = kind == "bluetooth" ? RadioKind.Bluetooth : RadioKind.WiFi;
                    string name = kind == "bluetooth" ? "Bluetooth" : "Wi-Fi";
                    bool ok = await NetworkService.SetRadioAsync(radio, on);
                    WidgetBridge.Note(ok ? $"{name} {(on ? "on" : "off")}" : $"Couldn't change {name}" + (_widgetAirplane == true ? " (airplane mode is on)" : ""));
                    await Task.Delay(on ? 1500 : 300); // a radio takes a moment to find anything
                    await RefreshWidgetRadiosAsync();
                    WidgetBridge.Nudge();
                    if (kind == "bluetooth") await SendWidgetBluetoothAsync(scan: false);
                    else await SendWidgetWifiAsync();
                    break;
                }
                case "wifiscan":
                    await SendWidgetWifiAsync();
                    break;
                case "wificonnect" when Str("ssid") is { Length: > 0 } ssid:
                {
                    if (!_widgetNetworks.TryGetValue(ssid, out var network)) { await SendWidgetWifiAsync(); break; }
                    string? password = Str("password");
                    bool saved = Wireless.SavedNetworks().Contains(ssid);
                    if (network.Secured && string.IsNullOrEmpty(password) && !saved)
                    {
                        await SendWidgetWifiAsync(ask: ssid);
                        break;
                    }
                    await SendWidgetWifiAsync(busy: "Connecting to " + ssid + "…", scan: false);
                    var error = await NetworkService.ConnectWifiAsync(network, password);
                    if (error == null) WidgetBridge.Note("Connected to " + ssid);
                    // A wrong (or no longer right) password: ask for it again.
                    await SendWidgetWifiAsync(ask: error != null && network.Secured ? ssid : null, error: error);
                    break;
                }
                case "wifidisconnect":
                    await NetworkService.DisconnectWifiAsync();
                    WidgetBridge.Note("Disconnected");
                    await Task.Delay(600);
                    await SendWidgetWifiAsync();
                    break;
                case "btlist":
                    await SendWidgetBluetoothAsync(scan: Bool("scan") == true);
                    break;
                case "btconnect" when Str("id") is { Length: > 0 } id:
                {
                    await SendWidgetBluetoothAsync(scan: false, busy: "Connecting…");
                    var error = await Wireless.ConnectAsync(id);
                    if (error == null) { WidgetBridge.Note("Connecting…"); await Task.Delay(2500); }
                    await SendWidgetBluetoothAsync(scan: false, error: error);
                    break;
                }
                case "btpair" when Str("id") is { Length: > 0 } id:
                {
                    await SendWidgetBluetoothAsync(scan: false, busy: "Pairing…");
                    var error = await NetworkService.PairAsync(id);
                    if (error == null) { WidgetBridge.Note("Paired"); _widgetNearby?.RemoveAll(d => d.Id == id); }
                    await SendWidgetBluetoothAsync(scan: false, error: error);
                    break;
                }
                case "btforget" when Str("id") is { Length: > 0 } id:
                {
                    var error = await NetworkService.UnpairAsync(id);
                    if (error == null) WidgetBridge.Note("Removed");
                    await SendWidgetBluetoothAsync(scan: false, error: error);
                    break;
                }
            }
        }
        catch (Exception) { }
    }

    /// <summary>
    /// The Wi-Fi panel's contents: the switch, the networks (scanned again unless <paramref name="scan"/> is false),
    /// and a network to ask a password for, a line of progress or an error.
    /// </summary>
    private async Task SendWidgetWifiAsync(string? ask = null, string? busy = null, string? error = null, bool scan = true)
    {
        var on = await NetworkService.GetRadioStateAsync(RadioKind.WiFi);
        var message = new JsonObject { ["type"] = "wifi", ["on"] = on, ["airplane"] = _widgetAirplane };
        if (busy != null) message["busy"] = busy;
        if (ask != null) message["ask"] = ask;
        if (on == true)
        {
            if (scan && !_widgetScanning)
            {
                _widgetScanning = true;
                WidgetBridge.Send(new JsonObject { ["type"] = "wifi", ["on"] = true, ["busy"] = "Looking for networks…", ["networks"] = WifiList(Wireless.SavedNetworks()) });
                try
                {
                    var (networks, scanError) = await NetworkService.ScanWifiAsync();
                    _widgetNetworks.Clear();
                    foreach (var n in networks) _widgetNetworks[n.Ssid] = n;
                    error ??= scanError;
                }
                finally { _widgetScanning = false; }
            }
            message["networks"] = WifiList(Wireless.SavedNetworks());
        }
        if (error != null) message["error"] = error;
        WidgetBridge.Send(message);
    }

    private JsonArray WifiList(HashSet<string> saved) => new(_widgetNetworks.Values
        .OrderByDescending(n => n.Connected).ThenByDescending(n => n.Bars)
        .Select(n => (JsonNode)new JsonObject
        {
            ["ssid"] = n.Ssid, ["bars"] = n.Bars, ["secured"] = n.Secured, ["connected"] = n.Connected, ["saved"] = saved.Contains(n.Ssid),
        }).ToArray());

    /// <summary>The Bluetooth panel's contents: the switch, paired devices, and (after a scan) devices nearby to pair.</summary>
    private async Task SendWidgetBluetoothAsync(bool scan, string? busy = null, string? error = null)
    {
        var on = await NetworkService.GetRadioStateAsync(RadioKind.Bluetooth);
        JsonObject Message(List<BluetoothItem> paired, string? line)
        {
            var m = new JsonObject { ["type"] = "bluetooth", ["on"] = on, ["airplane"] = _widgetAirplane };
            if (line != null) m["busy"] = line;
            m["paired"] = new JsonArray(paired.Select(d => (JsonNode)new JsonObject { ["name"] = d.Name, ["id"] = d.Id, ["connected"] = d.Connected }).ToArray());
            if (_widgetNearby != null)
                m["nearby"] = new JsonArray(_widgetNearby.Select(d => (JsonNode)new JsonObject { ["name"] = d.Name, ["id"] = d.Id }).ToArray());
            m["scanning"] = _widgetBtScanning;
            return m;
        }
        if (on != true)
        {
            _widgetNearby = null;
            var off = Message(new(), null);
            if (error != null) off["error"] = error;
            WidgetBridge.Send(off);
            return;
        }
        var paired = await NetworkService.ListBluetoothAsync(paired: true);
        if (scan && !_widgetBtScanning)
        {
            _widgetBtScanning = true;
            WidgetBridge.Send(Message(paired, "Looking for devices… put yours in pairing mode"));
            try
            {
                var nearby = await NetworkService.ListBluetoothAsync(paired: false);
                var pairedNames = paired.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                _widgetNearby = nearby.Where(d => !pairedNames.Contains(d.Name)).ToList();
            }
            finally { _widgetBtScanning = false; }
        }
        var message = Message(paired, busy);
        if (error != null) message["error"] = error;
        WidgetBridge.Send(message);
    }
}
