using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using Windows.Devices.WiFi;
using Windows.Networking.Connectivity;
using Windows.Security.Credentials;

namespace SquareDeck.Services;

public sealed record WifiNetwork(string Ssid, int Bars, bool Secured, bool Connected, WiFiAvailableNetwork Network);

public sealed record BluetoothItem(string Name, string Id, bool Paired, bool Connected);

/// <summary>What the home screen's status icons show. WifiBars is null when not connected over Wi-Fi.</summary>
public sealed record ConnectionStatus(bool WifiOn, int? WifiBars, bool BluetoothOn);

/// <summary>Wi-Fi and Bluetooth control through the Windows runtime APIs.</summary>
public static class NetworkService
{
    private const string IsConnectedProperty = "System.Devices.Aep.IsConnected";

    // ------------------------------------------------------------ radios (on / off)

    private static async Task<Radio?> FindRadioAsync(RadioKind kind)
    {
        var radios = await Radio.GetRadiosAsync();
        return radios.FirstOrDefault(r => r.Kind == kind);
    }

    /// <summary>True/false for on/off, or null when the PC has no such radio.</summary>
    public static async Task<bool?> GetRadioStateAsync(RadioKind kind)
    {
        try
        {
            var radio = await FindRadioAsync(kind);
            return radio == null ? null : radio.State == RadioState.On;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static async Task<bool> SetRadioAsync(RadioKind kind, bool on)
    {
        try
        {
            if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed) return false;
            var radio = await FindRadioAsync(kind);
            if (radio == null) return false;
            return await radio.SetStateAsync(on ? RadioState.On : RadioState.Off) == RadioAccessStatus.Allowed;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Cheap status check for the top bar: no scanning, no permission prompts.</summary>
    public static async Task<ConnectionStatus> GetStatusAsync()
    {
        bool wifi = false, bluetooth = false;
        int? bars = null;
        try
        {
            var radios = await Radio.GetRadiosAsync();
            wifi = radios.Any(r => r.Kind == RadioKind.WiFi && r.State == RadioState.On);
            bluetooth = radios.Any(r => r.Kind == RadioKind.Bluetooth && r.State == RadioState.On);
        }
        catch (Exception) { }

        try
        {
            var profile = NetworkInformation.GetInternetConnectionProfile();
            if (profile != null && profile.IsWlanConnectionProfile) bars = profile.GetSignalBars();
        }
        catch (Exception) { }

        return new ConnectionStatus(wifi, bars, bluetooth);
    }

    // ------------------------------------------------------------ Wi-Fi

    private static async Task<WiFiAdapter?> GetWifiAdapterAsync()
    {
        if (await WiFiAdapter.RequestAccessAsync() != WiFiAccessStatus.Allowed) return null;
        var adapters = await WiFiAdapter.FindAllAdaptersAsync();
        return adapters.FirstOrDefault();
    }

    /// <summary>Scans for networks. Strongest first, one entry per network name.</summary>
    public static async Task<(List<WifiNetwork> Networks, string? Error)> ScanWifiAsync()
    {
        try
        {
            var adapter = await GetWifiAdapterAsync();
            if (adapter == null)
                return (new(), "Windows did not allow Wi-Fi access. Check that Location is on in Windows privacy settings.");

            await adapter.ScanAsync();

            string? connectedName = null;
            try
            {
                var profile = await adapter.NetworkAdapter.GetConnectedProfileAsync();
                connectedName = profile?.ProfileName;
            }
            catch (Exception) { }

            var list = adapter.NetworkReport.AvailableNetworks
                .Where(n => !string.IsNullOrWhiteSpace(n.Ssid))
                .GroupBy(n => n.Ssid)
                .Select(g => g.OrderByDescending(n => n.SignalBars).First())
                .Select(n => new WifiNetwork(
                    n.Ssid,
                    n.SignalBars,
                    n.SecuritySettings.NetworkAuthenticationType is not
                        (NetworkAuthenticationType.Open80211 or NetworkAuthenticationType.None),
                    n.Ssid == connectedName,
                    n))
                .OrderByDescending(n => n.Connected)
                .ThenByDescending(n => n.Bars)
                .ToList();
            return (list, null);
        }
        catch (Exception ex)
        {
            return (new(), ex.Message);
        }
    }

    /// <summary>Returns null on success, otherwise a message to show.</summary>
    public static async Task<string?> ConnectWifiAsync(WifiNetwork network, string? password)
    {
        try
        {
            var adapter = await GetWifiAdapterAsync();
            if (adapter == null) return "Wi-Fi is not available.";

            WiFiConnectionResult result;
            if (network.Secured && !string.IsNullOrEmpty(password))
            {
                var credential = new PasswordCredential { Password = password };
                result = await adapter.ConnectAsync(network.Network, WiFiReconnectionKind.Automatic, credential);
            }
            else
            {
                result = await adapter.ConnectAsync(network.Network, WiFiReconnectionKind.Automatic);
            }

            return result.ConnectionStatus switch
            {
                WiFiConnectionStatus.Success => null,
                WiFiConnectionStatus.InvalidCredential => "That password was not accepted.",
                WiFiConnectionStatus.Timeout => "The connection timed out.",
                var other => "Could not connect (" + other + ")."
            };
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public static async Task DisconnectWifiAsync()
    {
        try
        {
            var adapter = await GetWifiAdapterAsync();
            adapter?.Disconnect();
        }
        catch (Exception) { }
    }

    // ------------------------------------------------------------ Bluetooth

    // Windows' battery level for a Bluetooth device (DEVPKEY_Bluetooth_Battery), reported by headsets over hands-free.
    private const string BatteryProperty = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    private const string BluetoothClass = "{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}";

    private const string ContainerProperty = "System.Devices.ContainerId";
    private const string AepContainerProperty = "System.Devices.Aep.ContainerId";

    /// <summary>
    /// The battery of connected Bluetooth headphones or a headset, or null when none is connected or none reports one.
    /// With several, the lowest. Looked for in three places, as headsets differ:
    ///  1. Windows' own number (Settings > Bluetooth shows it) on any of the headphones' device entries: classic headsets
    ///     put it on their hands-free or audio entry, which isn't in the Bluetooth class, so the entries are found by the
    ///     headphones' container (one physical device, all its entries);
    ///  2. the same, by name across the Bluetooth class, for a device with no container;
    ///  3. Bluetooth LE headphones' own battery service (GATT 0x180F), read directly.
    /// </summary>
    public static async Task<(string Name, int Percent)?> GetHeadphoneBatteryAsync()
    {
        try
        {
            var connected = new List<(string Name, string Id, Guid? Container, bool Le)>();
            foreach (var (selector, le) in new[] { (BluetoothDevice.GetDeviceSelectorFromPairingState(true), false), (BluetoothLEDevice.GetDeviceSelectorFromPairingState(true), true) })
            {
                try
                {
                    var list = await DeviceInformation.FindAllAsync(selector, new[] { IsConnectedProperty, AepContainerProperty });
                    foreach (var d in list)
                    {
                        if (string.IsNullOrWhiteSpace(d.Name)) continue;
                        if (!(d.Properties.TryGetValue(IsConnectedProperty, out var c) && c is bool on && on)) continue;
                        Guid? container = d.Properties.TryGetValue(AepContainerProperty, out var g) && g is Guid id && id != Guid.Empty ? id : null;
                        connected.Add((d.Name, d.Id, container, le));
                    }
                }
                catch (Exception) { }
            }
            if (connected.Count == 0) return null;

            var found = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            void Offer(string name, object? value)
            {
                int percent;
                try { percent = Convert.ToInt32(value); } catch (Exception) { return; }
                if (percent is < 0 or > 100) return;
                if (!found.TryGetValue(name, out var had) || percent < had) found[name] = percent;
            }

            // 1. Windows' number, on any entry of the same physical device.
            foreach (var device in connected.Where(d => d.Container != null).GroupBy(d => d.Container))
            {
                try
                {
                    var nodes = await DeviceInformation.FindAllAsync($"{ContainerProperty}:=\"{{{device.Key}}}\"",
                        new[] { BatteryProperty }, DeviceInformationKind.Device);
                    foreach (var node in nodes)
                        if (node.Properties.TryGetValue(BatteryProperty, out var value) && value != null) Offer(device.First().Name, value);
                }
                catch (Exception) { }
            }

            // 2. By name across the Bluetooth class, for those still without a number.
            var missing = connected.Select(d => d.Name).Where(n => !found.ContainsKey(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (missing.Count > 0)
            {
                try
                {
                    var nodes = await DeviceInformation.FindAllAsync($"System.Devices.ClassGuid:=\"{BluetoothClass}\"",
                        new[] { BatteryProperty }, DeviceInformationKind.Device);
                    foreach (var node in nodes)
                    {
                        if (!node.Properties.TryGetValue(BatteryProperty, out var value) || value == null) continue;
                        // The device entry is named after the headphones ("WH-1000XM4 Hands-Free AG" and the like).
                        string? name = missing.FirstOrDefault(n => node.Name.StartsWith(n, StringComparison.OrdinalIgnoreCase) || n.StartsWith(node.Name, StringComparison.OrdinalIgnoreCase));
                        if (name != null) Offer(name, value);
                    }
                }
                catch (Exception) { }
            }

            // 3. Bluetooth LE: the headphones' own battery service.
            foreach (var device in connected.Where(d => d.Le && !found.ContainsKey(d.Name)))
            {
                var level = await ReadLeBatteryAsync(device.Id);
                if (level != null) Offer(device.Name, level.Value);
            }

            if (found.Count == 0) return null;
            var lowest = found.OrderBy(kv => kv.Value).First();
            return (lowest.Key, lowest.Value);
        }
        catch (Exception) { return null; }
    }

    /// <summary>A Bluetooth LE device's battery level from its battery service, or null (none, or no answer within 4 seconds).</summary>
    private static async Task<int?> ReadLeBatteryAsync(string deviceId)
    {
        BluetoothLEDevice? device = null;
        try
        {
            var work = Task.Run(async () =>
            {
                device = await BluetoothLEDevice.FromIdAsync(deviceId);
                if (device == null) return (int?)null;
                var services = await device.GetGattServicesForUuidAsync(Windows.Devices.Bluetooth.GenericAttributeProfile.GattServiceUuids.Battery, BluetoothCacheMode.Uncached);
                if (services.Status != Windows.Devices.Bluetooth.GenericAttributeProfile.GattCommunicationStatus.Success) return null;
                foreach (var service in services.Services)
                {
                    using (service)
                    {
                        var chars = await service.GetCharacteristicsForUuidAsync(Windows.Devices.Bluetooth.GenericAttributeProfile.GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Uncached);
                        if (chars.Status != Windows.Devices.Bluetooth.GenericAttributeProfile.GattCommunicationStatus.Success) continue;
                        foreach (var ch in chars.Characteristics)
                        {
                            var read = await ch.ReadValueAsync(BluetoothCacheMode.Uncached);
                            if (read.Status != Windows.Devices.Bluetooth.GenericAttributeProfile.GattCommunicationStatus.Success || read.Value.Length < 1) continue;
                            using var reader = Windows.Storage.Streams.DataReader.FromBuffer(read.Value);
                            return (int?)reader.ReadByte();
                        }
                    }
                }
                return null;
            });
            var done = await Task.WhenAny(work, Task.Delay(4000));
            return done == work ? await work : null;
        }
        catch (Exception) { return null; }
        finally { try { device?.Dispose(); } catch (Exception) { } }
    }

    /// <summary>Paired devices, or (paired: false) nearby devices ready to pair. The latter takes several seconds.</summary>
    public static async Task<List<BluetoothItem>> ListBluetoothAsync(bool paired)
    {
        var found = new Dictionary<string, BluetoothItem>();
        var selectors = new[]
        {
            BluetoothDevice.GetDeviceSelectorFromPairingState(paired),
            BluetoothLEDevice.GetDeviceSelectorFromPairingState(paired)
        };

        foreach (var selector in selectors)
        {
            try
            {
                var devices = await DeviceInformation.FindAllAsync(selector, new[] { IsConnectedProperty });
                foreach (var d in devices)
                {
                    if (string.IsNullOrWhiteSpace(d.Name)) continue;
                    bool connected = d.Properties.TryGetValue(IsConnectedProperty, out var c) && c is bool b && b;
                    // The same physical device can show up as both classic and LE: keep one row per name.
                    if (!found.TryGetValue(d.Name, out var existing) || (connected && !existing.Connected))
                        found[d.Name] = new BluetoothItem(d.Name, d.Id, d.Pairing.IsPaired, connected);
                }
            }
            catch (Exception) { }
        }
        return found.Values.OrderByDescending(d => d.Connected).ThenBy(d => d.Name).ToList();
    }

    /// <summary>Returns null on success, otherwise a message to show.</summary>
    public static async Task<string?> PairAsync(string deviceId)
    {
        try
        {
            var device = await DeviceInformation.CreateFromIdAsync(deviceId);
            var custom = device.Pairing.Custom;
            custom.PairingRequested += OnPairingRequested;
            try
            {
                var result = await custom.PairAsync(
                    DevicePairingKinds.ConfirmOnly | DevicePairingKinds.ConfirmPinMatch | DevicePairingKinds.DisplayPin);
                return result.Status is DevicePairingResultStatus.Paired or DevicePairingResultStatus.AlreadyPaired
                    ? null
                    : "Pairing failed (" + result.Status + ").";
            }
            finally
            {
                custom.PairingRequested -= OnPairingRequested;
            }
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static void OnPairingRequested(DeviceInformationCustomPairing sender, DevicePairingRequestedEventArgs args)
    {
        // Controllers, headsets and the like only need a plain confirmation.
        args.Accept();
    }

    public static async Task<string?> UnpairAsync(string deviceId)
    {
        try
        {
            var device = await DeviceInformation.CreateFromIdAsync(deviceId);
            var result = await device.Pairing.UnpairAsync();
            return result.Status is DeviceUnpairingResultStatus.Unpaired or DeviceUnpairingResultStatus.AlreadyUnpaired
                ? null
                : "Could not remove the device (" + result.Status + ").";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
