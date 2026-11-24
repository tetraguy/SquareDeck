using System;
using Windows.Data.Json;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;

namespace SquareDeck.Widget
{
    /// <summary>
    /// The Wi-Fi and Bluetooth panels. Selecting the Wi-Fi or Bluetooth tile opens one over the cards: the radio's
    /// switch at the top, then networks to join (a password box opens under a secured one Windows doesn't know), or
    /// paired devices to connect or forget and "Add a device" to look for new ones and pair. SquareDeck does the work
    /// and answers with a "wifi" or "bluetooth" message.
    /// </summary>
    public sealed partial class WidgetPage
    {
        private Grid _sheet;
        private TextBlock _sheetTitle, _sheetStatus;
        private ToggleSwitch _sheetSwitch;
        private ProgressRing _sheetRing;
        private StackPanel _sheetList;
        private string _sheetKind;          // "wifi", "bluetooth" or null when closed
        private string _askSsid;            // the network whose password box is open
        private bool _sheetSyncing;

        private FrameworkElement BuildSheet()
        {
            _sheet = new Grid { Background = new SolidColorBrush(Color.FromArgb(0xF6, 0x12, 0x16, 0x1C)), Visibility = Visibility.Collapsed, Padding = new Thickness(16, 6, 16, 16) };
            _sheet.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _sheet.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _sheet.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var back = new Button { Style = DeckButton, Content = Glyph("", 16), Width = 44, Height = 40, Padding = new Thickness(0), Background = new SolidColorBrush(Colors.Transparent) };
            ToolTipService.SetToolTip(back, "Back");
            back.Click += (s, e) => CloseSheet();
            head.Children.Add(back);
            _sheetTitle = new TextBlock { FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Foreground = new SolidColorBrush(Colors.White) };
            Grid.SetColumn(_sheetTitle, 1);
            head.Children.Add(_sheetTitle);
            var refresh = new Button { Style = DeckButton, Content = Glyph("", 15), Width = 44, Height = 40, Padding = new Thickness(0), Margin = new Thickness(0, 0, 6, 0), Background = new SolidColorBrush(Colors.Transparent) };
            ToolTipService.SetToolTip(refresh, "Look again");
            refresh.Click += (s, e) => AskSheet(scan: true);
            Grid.SetColumn(refresh, 2);
            head.Children.Add(refresh);
            _sheetSwitch = new ToggleSwitch { OnContent = "", OffContent = "", MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
            _sheetSwitch.Toggled += (s, e) =>
            {
                if (_sheetSyncing || _sheetKind == null) return;
                var message = new JsonObject();
                message["op"] = JsonValue.CreateStringValue("radio");
                message["kind"] = JsonValue.CreateStringValue(_sheetKind);
                message["on"] = JsonValue.CreateBooleanValue(_sheetSwitch.IsOn);
                _link.Send(message);
                SheetBusy(_sheetSwitch.IsOn ? "Turning on…" : "Turning off…");
            };
            Grid.SetColumn(_sheetSwitch, 3);
            head.Children.Add(_sheetSwitch);
            _sheet.Children.Add(head);

            var status = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 10, 0, 8) };
            _sheetRing = new ProgressRing { Width = 14, Height = 14, Margin = new Thickness(0, 0, 8, 0), Foreground = _accentBrush, IsActive = false, Visibility = Visibility.Collapsed };
            status.Children.Add(_sheetRing);
            _sheetStatus = new TextBlock { FontSize = 12, Foreground = new SolidColorBrush(Subtle), TextWrapping = TextWrapping.Wrap, MaxWidth = 420 };
            status.Children.Add(_sheetStatus);
            Grid.SetRow(status, 1);
            _sheet.Children.Add(status);

            _sheetList = new StackPanel();
            var scroll = new ScrollViewer { Content = _sheetList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            Grid.SetRow(scroll, 2);
            _sheet.Children.Add(scroll);
            Grid.SetRow(_sheet, 1);
            return _sheet;
        }

        private void OpenSheet(string kind)
        {
            _sheetKind = kind;
            _askSsid = null;
            _sheetTitle.Text = kind == "wifi" ? "Wi-Fi" : "Bluetooth";
            _sheetList.Children.Clear();
            _sheet.Visibility = Visibility.Visible;
            SheetBusy(kind == "wifi" ? "Looking for networks…" : "Finding your devices…");
            AskSheet(scan: kind == "wifi");
        }

        private void CloseSheet()
        {
            _sheetKind = null;
            _askSsid = null;
            _sheet.Visibility = Visibility.Collapsed;
            if (_quick.ContainsKey("wifi")) _quick["wifi"].Button.Focus(FocusState.Programmatic);
        }

        /// <summary>Asks SquareDeck for the list again (for Bluetooth, <paramref name="scan"/> also looks for new devices).</summary>
        private void AskSheet(bool scan)
        {
            if (_sheetKind == "wifi") _link.Send("wifiscan");
            else if (_sheetKind == "bluetooth")
            {
                var message = new JsonObject();
                message["op"] = JsonValue.CreateStringValue("btlist");
                message["scan"] = JsonValue.CreateBooleanValue(scan);
                _link.Send(message);
            }
        }

        private void SheetBusy(string text)
        {
            _sheetStatus.Text = text ?? "";
            _sheetRing.IsActive = text != null;
            _sheetRing.Visibility = text != null ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SheetSwitch(bool? on)
        {
            _sheetSyncing = true;
            _sheetSwitch.IsEnabled = on != null;
            _sheetSwitch.IsOn = on == true;
            _sheetSyncing = false;
        }

        private static void SendWith(Link.DeckLink link, string op, string name, string value, string name2 = null, string value2 = null)
        {
            var message = new JsonObject();
            message["op"] = JsonValue.CreateStringValue(op);
            message[name] = JsonValue.CreateStringValue(value);
            if (name2 != null) message[name2] = JsonValue.CreateStringValue(value2 ?? "");
            link.Send(message);
        }

        /// <summary>A row in a panel: an icon, a name, a line under it, and what selecting it does.</summary>
        private Button SheetRow(string glyph, string name, string detail, bool highlight, Action click)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var icon = Glyph(glyph, 18);
            icon.VerticalAlignment = VerticalAlignment.Center;
            icon.Foreground = new SolidColorBrush(highlight ? _accent : Colors.White);
            row.Children.Add(icon);
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            words.Children.Add(new TextBlock { Text = name, FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            if (!string.IsNullOrEmpty(detail))
                words.Children.Add(new TextBlock { Text = detail, FontSize = 12, Foreground = new SolidColorBrush(highlight ? _accent : Subtle), Margin = new Thickness(0, 2, 0, 0) });
            Grid.SetColumn(words, 1);
            row.Children.Add(words);
            var b = new Button
            {
                Style = DeckButton, Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 0, 6)
            };
            if (click != null) b.Click += (s, e) => click();
            return b;
        }

        private TextBlock SheetHeading(string text) => new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Subtle), Margin = new Thickness(4, 10, 0, 6) };

        private TextBlock SheetNote(string text) => new TextBlock { Text = text, FontSize = 13, Foreground = new SolidColorBrush(Subtle), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 8, 4, 8) };

        // ---------------------------------------------------------------- Wi-Fi

        private static string Bars(int bars)
        {
            switch (bars)
            {
                case 0: case 1: return "";
                case 2: return "";
                case 3: return "";
                default: return "";
            }
        }

        private void ApplyWifi(JsonObject m)
        {
            if (_sheetKind != "wifi") return;
            bool? on = Bool(m, "on");
            SheetSwitch(on);
            string error = Str(m, "error");
            SheetBusy(Str(m, "busy"));
            if (Str(m, "busy") == null) _sheetStatus.Text = error ?? "";
            if (Str(m, "ask") is string ask) _askSsid = ask;

            var networks = Arr(m, "networks");
            if (networks == null && Str(m, "busy") != null && on == true) return; // still looking: keep the old list
            _sheetList.Children.Clear();
            if (on != true)
            {
                _sheetList.Children.Add(SheetNote(Bool(m, "airplane") == true ? "Airplane mode is on. Turn Wi-Fi on to connect anyway." :
                    on == null ? "This PC has no Wi-Fi." : "Wi-Fi is off. Turn it on to see networks."));
                return;
            }
            if (networks == null || networks.Count == 0) { _sheetList.Children.Add(SheetNote("No networks found. Look again in a moment.")); return; }

            foreach (var value in networks)
            {
                var n = value.GetObject();
                string ssid = Str(n, "ssid") ?? "";
                bool connected = Bool(n, "connected") == true, secured = Bool(n, "secured") == true, saved = Bool(n, "saved") == true;
                string detail = connected ? "Connected" : secured ? (saved ? "Secured, saved" : "Secured") : "Open";
                Button row = null;
                row = SheetRow(Bars((int)(Num(n, "bars") ?? 0)), ssid, detail, connected, () =>
                {
                    if (connected) { ConfirmAt(row, "", "Disconnect", () => _link.Send("wifidisconnect")); return; }
                    if (secured && !saved) { _askSsid = ssid; ApplyWifiAsk(); return; }
                    _askSsid = null;
                    SendWith(_link, "wificonnect", "ssid", ssid);
                    SheetBusy("Connecting to " + ssid + "…");
                });
                row.Tag = ssid;
                _sheetList.Children.Add(row);
            }
            ApplyWifiAsk();
        }

        /// <summary>The password box under the network it's for (only one open at a time).</summary>
        private void ApplyWifiAsk()
        {
            for (int i = _sheetList.Children.Count - 1; i >= 0; i--)
                if (_sheetList.Children[i] is StackPanel p && "ask".Equals(p.Tag)) _sheetList.Children.RemoveAt(i);
            if (_askSsid == null) return;
            int index = -1;
            for (int i = 0; i < _sheetList.Children.Count; i++)
                if (_sheetList.Children[i] is Button b && _askSsid.Equals(b.Tag)) { index = i; break; }
            if (index < 0) return;

            string ssid = _askSsid;
            var panel = new StackPanel { Tag = "ask", Margin = new Thickness(36, 0, 0, 12) };
            var box = new PasswordBox { PlaceholderText = "Network password", Margin = new Thickness(0, 0, 0, 8) };
            panel.Children.Add(box);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            var connect = new Button { Style = DeckButton, Content = new TextBlock { Text = "Connect", FontSize = 13, FontWeight = FontWeights.SemiBold }, Padding = new Thickness(16, 6, 16, 7), Background = _accentBrush, Foreground = new SolidColorBrush(Color.FromArgb(255, 0x0B, 0x16, 0x1A)) };
            Action go = () =>
            {
                if (string.IsNullOrEmpty(box.Password)) return;
                SendWith(_link, "wificonnect", "ssid", ssid, "password", box.Password);
                _askSsid = null;
                SheetBusy("Connecting to " + ssid + "…");
                ApplyWifiAsk();
            };
            connect.Click += (s, e) => go();
            box.KeyDown += (s, e) => { if (e.Key == Windows.System.VirtualKey.Enter) go(); };
            var cancel = new Button { Style = DeckButton, Content = new TextBlock { Text = "Cancel", FontSize = 13 }, Padding = new Thickness(16, 6, 16, 7), Margin = new Thickness(8, 0, 0, 0) };
            cancel.Click += (s, e) => { _askSsid = null; ApplyWifiAsk(); };
            buttons.Children.Add(connect);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);
            _sheetList.Children.Insert(index + 1, panel);
            var _ = Dispatcher.RunIdleAsync(a => box.Focus(FocusState.Programmatic));
        }

        // ---------------------------------------------------------------- Bluetooth

        private void ApplyBluetooth(JsonObject m)
        {
            if (_sheetKind != "bluetooth") return;
            bool? on = Bool(m, "on");
            SheetSwitch(on);
            string busy = Str(m, "busy"), error = Str(m, "error");
            SheetBusy(busy);
            if (busy == null) _sheetStatus.Text = error ?? "";
            _sheetList.Children.Clear();
            if (on != true)
            {
                _sheetList.Children.Add(SheetNote(Bool(m, "airplane") == true ? "Airplane mode is on. Turn Bluetooth on to use your devices anyway." :
                    on == null ? "This PC has no Bluetooth." : "Bluetooth is off. Turn it on to connect headphones, controllers and more."));
                return;
            }

            _sheetList.Children.Add(SheetHeading("PAIRED DEVICES"));
            var paired = Arr(m, "paired");
            if (paired == null || paired.Count == 0) _sheetList.Children.Add(SheetNote("Nothing paired yet."));
            else
                foreach (var value in paired)
                {
                    var d = value.GetObject();
                    string id = Str(d, "id"), name = Str(d, "name") ?? "Device";
                    bool connected = Bool(d, "connected") == true;
                    Button row = null;
                    row = SheetRow("", name, connected ? "Connected" : "Paired", connected, () =>
                    {
                        var menu = new MenuFlyout { Placement = FlyoutPlacementMode.Bottom };
                        if (!connected)
                        {
                            var connect = new MenuFlyoutItem { Text = "Connect", Icon = new FontIcon { Glyph = "", FontFamily = new FontFamily("Segoe MDL2 Assets") } };
                            connect.Click += (s, e) => { SendWith(_link, "btconnect", "id", id); SheetBusy("Connecting to " + name + "…"); };
                            menu.Items.Add(connect);
                        }
                        var forget = new MenuFlyoutItem { Text = "Remove device", Icon = new FontIcon { Glyph = "", FontFamily = new FontFamily("Segoe MDL2 Assets") } };
                        forget.Click += (s, e) => ConfirmAt(row, "", "Remove " + name, () => SendWith(_link, "btforget", "id", id));
                        menu.Items.Add(forget);
                        menu.ShowAt(row);
                    });
                    _sheetList.Children.Add(row);
                }

            bool scanning = Bool(m, "scanning") == true;
            var nearby = Arr(m, "nearby");
            if (nearby != null)
            {
                _sheetList.Children.Add(SheetHeading("NEARBY, READY TO PAIR"));
                if (nearby.Count == 0) _sheetList.Children.Add(SheetNote("Nothing found. Put the device in pairing mode, then look again."));
                foreach (var value in nearby)
                {
                    var d = value.GetObject();
                    string id = Str(d, "id"), name = Str(d, "name") ?? "Device";
                    _sheetList.Children.Add(SheetRow("", name, "Select to pair", false, () => { SendWith(_link, "btpair", "id", id); SheetBusy("Pairing with " + name + "…"); }));
                }
            }
            var add = SheetRow(scanning ? "" : "", scanning ? "Looking for devices…" : "Add a device", scanning ? "Put yours in pairing mode" : "Look for devices in pairing mode", false,
                scanning ? (Action)null : () => AskSheet(scan: true));
            add.Margin = new Thickness(0, 12, 0, 6);
            _sheetList.Children.Add(add);
        }

        /// <summary>A small menu to make sure, by the row: the action, or Cancel.</summary>
        private static void ConfirmAt(FrameworkElement at, string glyph, string text, Action yes)
        {
            var sure = new MenuFlyout { Placement = FlyoutPlacementMode.Bottom };
            var go = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets") } };
            go.Click += (s, e) => yes();
            sure.Items.Add(go);
            sure.Items.Add(new MenuFlyoutItem { Text = "Cancel", Icon = new FontIcon { Glyph = "", FontFamily = new FontFamily("Segoe MDL2 Assets") } });
            sure.ShowAt(at);
        }
    }
}
