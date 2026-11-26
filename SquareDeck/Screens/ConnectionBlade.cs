using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SquareDeck.Services;
using Windows.Devices.Radios;

namespace SquareDeck.Screens;

/// <summary>
/// The blade that slides in from the right when the Wi-Fi or Bluetooth icon at the top of the home screen is
/// pressed: the radio's switch, then the networks to join or the devices to connect, pair or remove, without leaving
/// the home screen. LB / RB switch between Wi-Fi and Bluetooth; Y opens the full page; B closes it.
/// </summary>
public sealed class ConnectionBladeScreen : Screen
{
    private const double BladeWidth = 520;
    private readonly Border _sheet;
    private readonly StackPanel _list = new();
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _title, _glyph, _status;
    private readonly Button _wifiTab, _bluetoothTab, _wifiSwitch, _bluetoothSwitch;
    private readonly TextBlock _wifiState, _bluetoothState;
    private Button _switch => _bluetooth ? _bluetoothSwitch : _wifiSwitch;
    private TextBlock _switchState => _bluetooth ? _bluetoothState : _wifiState;
    private bool _bluetooth;
    private bool? _on;
    private bool _busy, _searching, _closing;
    private string? _error;
    private List<WifiNetwork> _networks = new();
    private List<BluetoothItem> _paired = new();
    private List<BluetoothItem>? _nearby;
    private int _generation;

    public override bool IsDialog => true;

    /// <summary>Experimental: Switch 2 style (with the menus and Search). The UI style's colors, flat rows, a pill switch.</summary>
    private readonly bool _s2 = Switch2Pages.SystemOn;

    public ConnectionBladeScreen(bool bluetooth)
    {
        _bluetooth = bluetooth;
        RumbleOnMove();

        var panel = new DockPanel { LastChildFill = true };

        // ---- the top: Wi-Fi | Bluetooth, then the radio's name, state and switch
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 22) };
        _wifiTab = Tab("", "Wi-Fi", () => SwitchTo(false));
        _bluetoothTab = Tab("", "Bluetooth", () => SwitchTo(true));
        tabs.Children.Add(_wifiTab);
        tabs.Children.Add(_bluetoothTab);
        var hint = Ui.Text(Hints.Format("LB / RB"), 12, "SubtleBrush");
        Action relabel = () => Dispatcher.BeginInvoke(new Action(() => hint.Text = Hints.Format("LB / RB")));
        Loaded += (_, _) => { Hints.Changed -= relabel; Hints.Changed += relabel; };
        Unloaded += (_, _) => Hints.Changed -= relabel;
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.Margin = new Thickness(8, 0, 0, 0);
        tabs.Children.Add(hint);
        DockPanel.SetDock(tabs, Dock.Top);
        panel.Children.Add(tabs);

        var head = new DockPanel { Margin = new Thickness(2, 0, 0, 14) };
        _glyph = Ui.Glyph("", 34, "AccentBrush");
        _glyph.Margin = new Thickness(0, 0, 16, 0);
        _glyph.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(_glyph, Dock.Left);
        head.Children.Add(_glyph);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _title = Ui.Text("Wi-Fi", 28, "FgBrush", bold: true);
        _status = Ui.Text("", 14, "SubtleBrush");
        _status.TextTrimming = TextTrimming.CharacterEllipsis;
        words.Children.Add(_title);
        words.Children.Add(_status);
        head.Children.Add(words);
        DockPanel.SetDock(head, Dock.Top);
        panel.Children.Add(head);

        // The radio's switch (one each, so its name matches).
        var switches = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        _wifiSwitch = Ui.ValueRow("Wi-Fi", "\uE7E8", out _wifiState);
        _bluetoothSwitch = Ui.ValueRow("Bluetooth", "\uE7E8", out _bluetoothState);
        foreach (var sw in new[] { _wifiSwitch, _bluetoothSwitch })
        {
            sw.Click += (_, _) => _ = ToggleAsync();
            switches.Children.Add(sw);
        }
        DockPanel.SetDock(switches, Dock.Top);
        panel.Children.Add(switches);

        // ---- the bottom: the full page, and Windows' own settings
        var foot = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        var more = Ui.Row("All settings", null, "", subtitle: "The full page, and Windows' own settings");
        more.Click += (_, _) => OpenFullPage();
        foot.Children.Add(more);
        DockPanel.SetDock(foot, Dock.Bottom);
        panel.Children.Add(foot);

        // ---- the middle: networks or devices
        _scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.VerticalOnly, Focusable = false, Content = _list
        };
        panel.Children.Add(_scroll);

        _sheet = new Border
        {
            Width = BladeWidth, Padding = new Thickness(30, 34, 30, 26), HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Stretch, CornerRadius = new CornerRadius(QuickSettingsScreen.Round(26), 0, 0, QuickSettingsScreen.Round(26)),
            Child = panel, RenderTransform = new TranslateTransform(),
            Effect = Perf.Active ? null : new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 40, ShadowDepth = 0, Opacity = 0.45 }
        };
        _sheet.SetResourceReference(Border.BackgroundProperty, "DialogBrush");
        _sheet.SetResourceReference(Border.BorderBrushProperty, "PanelStroke");
        _sheet.SetResourceReference(Border.BorderThicknessProperty, "PanelStrokeThickness");
        LookChrome.StyleSheet(_sheet);
        QuickSettingsScreen.SkinSheet(_sheet);

        if (_s2)
        {
            // Switch 2 style: the blade in the page color with no outline, Wi-Fi | Bluetooth as plain names that take the
            // rimmed highlight, the radio's switch as a pill, and the networks and devices as flat rows under
            // bar-and-name headings.
            Switch2Settings.ApplyColors(this);
            _sheet.Background = new SolidColorBrush(Switch2Settings.Background);
            _sheet.BorderThickness = new Thickness(0);
            foreach (var tab in new[] { _wifiTab, _bluetoothTab })
            {
                tab.Style = Switch2Pages.TileStyle;
                tab.Margin = new Thickness(0, 0, 4, 0);
            }
            _title.FontWeight = FontWeights.Normal;
            Switch2Pages.RestyleTree(switches, 64);
            Switch2Pages.RestyleTree(foot, 64);
            Switch2Pages.AutoRestyle(_list, 62);
        }
        var backdrop = new Grid { Background = new SolidColorBrush(Color.FromArgb(0x70, 0, 0, 0)) };
        // A press outside the blade closes it.
        backdrop.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource == backdrop) Close(); };
        backdrop.TouchDown += (_, e) => { if (e.OriginalSource == backdrop) Close(); };
        backdrop.Children.Add(_sheet);
        Content = backdrop;
    }

    /// <summary>Switch 2 style: the radio's switch as a pill (blue and knob right when on), in place of the On / Off text.</summary>
    private void ShowPill()
    {
        if (_switch.Content is not Grid grid) return;
        foreach (var old in grid.Children.OfType<FrameworkElement>().Where(e => e.Tag as string == "s2pill").ToList()) grid.Children.Remove(old);
        if (_on == null) { _switchState.Visibility = Visibility.Visible; return; } // still finding out: "…"
        _switchState.Visibility = Visibility.Collapsed;
        var pill = Switch2Settings.Toggle(_on == true);
        pill.Tag = "s2pill";
        pill.Margin = new Thickness(16, 0, 0, 0);
        Grid.SetColumn(pill, Grid.GetColumn(_switchState));
        Grid.SetRow(pill, Grid.GetRow(_switchState));
        grid.Children.Add(pill);
    }

    private static Button Tab(string glyph, string label, Action click)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 7, 16, 7) };
        var g = Ui.Glyph(glyph, 15, "FgBrush");
        g.Margin = new Thickness(0, 0, 8, 0);
        g.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(g);
        var t = Ui.Text(label, 15, "FgBrush", bold: true);
        t.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(t);
        var b = new Button { Style = Ui.StyleOf("PanelButton"), Content = row, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(0) };
        b.Click += (_, _) => click();
        return b;
    }

    public override void OnShown()
    {
        if (!Theme.ReduceMotion && _sheet.RenderTransform is TranslateTransform slide)
        {
            slide.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(BladeWidth + 30, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            _sheet.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        }
        SwitchTo(_bluetooth, force: true);
    }

    public override void OnResumed() => _ = RefreshAsync(); // back from a full page or a password prompt

    public override void FocusDefault() => _switch.Focus();

    public override bool OnAction(NavAction action)
    {
        switch (action)
        {
            case NavAction.LB: SwitchTo(false); return true;
            case NavAction.RB: SwitchTo(true); return true;
            case NavAction.Y: OpenFullPage(); return true;
            case NavAction.X: _ = RefreshAsync(); return true;
            case NavAction.Back: Close(); return true;
        }
        return false;
    }

    /// <summary>Slides back out, then closes.</summary>
    private async void Close()
    {
        if (_closing) return;
        _closing = true;
        if (!Theme.ReduceMotion && _sheet.RenderTransform is TranslateTransform slide)
        {
            slide.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(0, BladeWidth + 30, TimeSpan.FromMilliseconds(190)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
            await Task.Delay(180);
        }
        Host.Pop(this);
    }

    private void OpenFullPage()
    {
        Host.Pop(this);
        Host.Push(_bluetooth ? new BluetoothScreen() : new WifiScreen());
    }

    private void SwitchTo(bool bluetooth, bool force = false)
    {
        if (!force && bluetooth == _bluetooth) return;
        _bluetooth = bluetooth;
        _nearby = null;
        _searching = false;
        _networks = new();
        _paired = new();
        _title.Text = bluetooth ? "Bluetooth" : "Wi-Fi";
        _glyph.Text = bluetooth ? "" : "";
        _wifiSwitch.Visibility = bluetooth ? Visibility.Collapsed : Visibility.Visible;
        _bluetoothSwitch.Visibility = bluetooth ? Visibility.Visible : Visibility.Collapsed;
        _wifiTab.Opacity = bluetooth ? 0.55 : 1;
        _bluetoothTab.Opacity = bluetooth ? 1 : 0.55;
        if (_s2)
        {
            // The one showing in blue, the other in gray, as the Switch 2 marks the section you're in.
            _wifiTab.Opacity = _bluetoothTab.Opacity = 1;
            _wifiTab.SetResourceReference(ForegroundProperty, bluetooth ? "SubtleBrush" : "AccentBrush");
            _bluetoothTab.SetResourceReference(ForegroundProperty, bluetooth ? "AccentBrush" : "SubtleBrush");
            foreach (var (tab, on) in new[] { (_wifiTab, !bluetooth), (_bluetoothTab, bluetooth) })
                if (tab.Content is Panel row)
                    foreach (var t in row.Children.OfType<TextBlock>()) t.SetResourceReference(TextBlock.ForegroundProperty, on ? "AccentBrush" : "SubtleBrush");
        }
        _ = RefreshAsync();
        FocusLater(_switch);
    }

    // ---------------------------------------------------------------- reading and acting

    private async Task RefreshAsync()
    {
        int gen = ++_generation;
        bool bt = _bluetooth;
        _busy = true;
        Fill();
        var on = await NetworkService.GetRadioStateAsync(bt ? RadioKind.Bluetooth : RadioKind.WiFi);
        List<WifiNetwork> networks = new();
        List<BluetoothItem> paired = new();
        string? error = null;
        if (on == true)
        {
            if (bt) paired = await NetworkService.ListBluetoothAsync(paired: true);
            else (networks, error) = await NetworkService.ScanWifiAsync();
        }
        if (gen != _generation) return; // switched to the other radio meanwhile
        _on = on;
        _networks = networks;
        _paired = paired;
        _error = error;
        _busy = false;
        Fill();
    }

    private async Task ToggleAsync()
    {
        if (_on == null) return;
        var kind = _bluetooth ? RadioKind.Bluetooth : RadioKind.WiFi;
        if (!await NetworkService.SetRadioAsync(kind, _on != true))
            await Host.AskAsync("Couldn't change " + (_bluetooth ? "Bluetooth" : "Wi-Fi"), "Windows did not allow this app to switch the radio.", "OK");
        _nearby = null;
        await RefreshAsync();
    }

    private void Fill()
    {
        bool hadFocus = _list.IsKeyboardFocusWithin;
        int focusIndex = hadFocus ? _list.Children.OfType<Button>().ToList().FindIndex(b => b.IsKeyboardFocused) : -1;
        _list.Children.Clear();
        _switchState.Text = _on == true ? "On" : _on == false ? "Off" : "…";
        _switch.IsEnabled = _on != null || _busy;
        if (_s2) ShowPill();

        void Note(string text)
        {
            var t = Ui.Text(text, 15, "SubtleBrush");
            t.TextWrapping = TextWrapping.Wrap;
            t.Margin = new Thickness(6, 8, 6, 8);
            _list.Children.Add(t);
        }
        void Header(string text)
        {
            var h = Ui.Header(text);
            if (_s2) h.Tag = "s2title"; // a bar-and-name heading
            _list.Children.Add(h);
        }
        Button Row(string label, string? value, string glyph, Action click, string? subtitle = null)
        {
            var r = Ui.Row(label, value, glyph, subtitle: subtitle);
            r.Click += (_, _) => click();
            _list.Children.Add(r);
            return r;
        }

        if (_bluetooth)
        {
            var connected = _paired.Where(d => d.Connected).ToList();
            _status.Text = _busy ? "Checking…" : _on == null ? "No Bluetooth adapter" : _on == false ? "Off" :
                connected.Count > 0 ? "Connected to " + string.Join(", ", connected.Select(d => d.Name)) : "Not connected";
            if (_on == null && !_busy) Note("No Bluetooth adapter was found on this PC.");
            else if (_busy && _paired.Count == 0) Note("Checking Bluetooth…");
            else if (_on == true)
            {
                Header("Your devices");
                if (_paired.Count == 0) Note("Nothing paired yet.");
                foreach (var d in _paired.OrderByDescending(d => d.Connected))
                {
                    var device = d;
                    Row(d.Name, d.Connected ? "Connected" : "Paired", d.Connected ? "" : "", () => _ = PairedAsync(device));
                }
                Header("Add a device");
                Row(_searching ? "Searching…" : "Search for nearby devices", null, "", () => _ = SearchAsync(),
                    subtitle: _nearby == null ? "Put the device in pairing mode first" : null);
                if (_nearby != null)
                {
                    if (_nearby.Count == 0 && !_searching) Note("No devices found. Put the device in pairing mode and search again.");
                    foreach (var d in _nearby)
                    {
                        var device = d;
                        Row(d.Name, "Pair", "", () => _ = PairAsync(device));
                    }
                }
            }
            else if (_on == false) Note("Bluetooth is off. Turn it on to connect headphones, controllers and other devices.");
        }
        else
        {
            var current = _networks.FirstOrDefault(n => n.Connected);
            _status.Text = _busy ? "Looking for networks…" : _on == null ? "No Wi-Fi adapter" : _on == false ? "Off" :
                current != null ? "Connected to " + current.Ssid : "Not connected";
            if (_on == null && !_busy) Note("No Wi-Fi adapter was found on this PC.");
            else if (_busy && _networks.Count == 0) Note("Looking for networks…");
            else if (_error != null) Note(_error);
            else if (_on == true)
            {
                Header("Networks");
                if (_networks.Count == 0) Note("No networks found. Press X to look again.");
                foreach (var n in _networks.OrderByDescending(n => n.Connected).ThenByDescending(n => n.Bars))
                {
                    var net = n;
                    int bars = Math.Clamp(n.Bars, 1, 4);
                    string glyph = bars switch { 1 => "", 2 => "", 3 => "", _ => "" };
                    Row(n.Ssid, n.Connected ? "Connected" : n.Secured ? "Secured" : "Open", glyph, () => _ = SelectAsync(net),
                        subtitle: n.Connected ? "Press to disconnect" : null);
                }
            }
            else if (_on == false) Note("Wi-Fi is off. Turn it on to see networks nearby.");
        }
        Ui.GroupRows(_list);

        if (hadFocus)
        {
            var buttons = _list.Children.OfType<Button>().ToList();
            if (buttons.Count > 0) FocusLater(buttons[Math.Clamp(focusIndex, 0, buttons.Count - 1)]);
            else FocusLater(_switch);
        }
    }

    private async Task SelectAsync(WifiNetwork network)
    {
        if (network.Connected)
        {
            if (await Host.AskAsync(network.Ssid, "You're connected to this network.", "Disconnect", "Cancel") == 0)
            {
                await NetworkService.DisconnectWifiAsync();
                await RefreshAsync();
            }
            return;
        }
        string? password = null;
        if (network.Secured)
        {
            password = await Host.PromptAsync("Connect to " + network.Ssid, "Enter the network password. Leave it empty to use a password Windows already saved.", "", "Connect");
            if (password == null) return;
        }
        _busy = true;
        _status.Text = "Connecting to " + network.Ssid + "…";
        var error = await NetworkService.ConnectWifiAsync(network, password);
        if (error != null) await Host.AskAsync("Couldn't connect", error, "OK");
        else Host.ShowToast("Wi-Fi", "Connected to " + network.Ssid, "", "", "info");
        await RefreshAsync();
    }

    private async Task PairedAsync(BluetoothItem device)
    {
        int pick = await Host.AskAsync(device.Name, device.Connected ? "Connected" : "Paired. It connects by itself when it's turned on and near.",
            "Remove device", "Bluetooth settings", "Cancel");
        if (pick == 1) { ProcessUtil.Open("ms-settings:bluetooth"); return; }
        if (pick != 0) return;
        var error = await NetworkService.UnpairAsync(device.Id);
        if (error != null) await Host.AskAsync("Couldn't remove " + device.Name, error, "OK");
        await RefreshAsync();
    }

    private async Task SearchAsync()
    {
        if (_searching) return;
        _searching = true;
        _nearby ??= new();
        Fill();
        var found = await NetworkService.ListBluetoothAsync(paired: false);
        if (!_bluetooth) return;
        _nearby = found;
        _searching = false;
        Fill();
    }

    private async Task PairAsync(BluetoothItem device)
    {
        _status.Text = "Pairing " + device.Name + "…";
        var error = await NetworkService.PairAsync(device.Id);
        if (error != null) await Host.AskAsync("Couldn't pair " + device.Name, error, "OK");
        else Host.ShowToast("Bluetooth", device.Name + " is paired", "", "", "info");
        _nearby = null;
        await RefreshAsync();
    }
}
