using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace SquareDeck.Widget
{
    /// <summary>
    /// The widget's experimental cards (SquareDeck 8.1), each shown only while it's turned on in SquareDeck's
    /// Settings > Experimental: frame cap and Auto TDP in the Power card, the thermal warning, game notes,
    /// achievements, Ask Genie, the controller, friends, notifications, downloads, Invite on the game card, apps and
    /// SquareDeck shortcuts, the card editor (with layouts and presets per game), the style match and the companion.
    /// </summary>
    public sealed partial class WidgetPage
    {
        private StackPanel _cardsPanel;
        private Border _powerCard;
        private readonly HashSet<string> _labs = new HashSet<string>();
        private readonly Dictionary<string, Border> _eightCards = new Dictionary<string, Border>();
        private string _runningName = "";

        private static readonly Color AmberColor = Color.FromArgb(255, 0xFF, 0xB0, 0x3B);

        private static readonly string[] CardIds = { "game", "media", "power", "live", "ally", "notes", "achievements", "genie", "controller", "friends", "inbox", "downloads", "launch", "quick" };
        private static readonly string[] CardNames = { "Game", "Now playing", "Power", "Live", "ROG Ally", "Game notes", "Achievements", "Ask Genie", "Controller", "Friends", "Notifications", "Downloads", "Launch", "Quick" };

        private Border CardOf(string id)
        {
            switch (id)
            {
                case "game": return _gameCard;
                case "media": return _mediaCard;
                case "power": return _powerCard;
                case "live": return _liveCard;
                case "ally": return _allyCard;
                case "quick": return _quickCard;
                default:
                    Border b;
                    return _eightCards.TryGetValue(id, out b) ? b : null;
            }
        }

        private static Button SmallButton(string glyph, string text)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            if (glyph != null) row.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 7, 0) });
            row.Children.Add(new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            return new Button
            {
                Style = (Style)Application.Current.Resources["DeckButton"], Content = row, Padding = new Thickness(11, 5, 12, 6), CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)), Margin = new Thickness(0, 0, 6, 6)
            };
        }

        private static TextBlock Line(string text, double size, Color color, bool bold = false) => new TextBlock
        {
            Text = text ?? "", FontSize = size, Foreground = new SolidColorBrush(color), TextWrapping = TextWrapping.Wrap,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal
        };

        private Border AddCard(StackPanel cards, string id, string glyph, string title, UIElement body, FrameworkElement extra = null)
        {
            var card = Card(glyph, title, body, extra);
            card.Visibility = Visibility.Collapsed;
            _eightCards[id] = card;
            cards.Children.Add(card);
            return card;
        }

        // ============================================================ building

        private void BuildEightCards(StackPanel cards)
        {
            BuildPowerExtras();
            BuildThermal();
            BuildNotes(cards);
            BuildAchievements(cards);
            BuildGenie(cards);
            BuildController(cards);
            BuildFriends(cards);
            BuildInbox(cards);
            BuildDownloads(cards);
            BuildLaunch(cards);
            BuildHeaderExtras();
            BuildParty();
            BuildEditor();
            // The Quick card stays last, as before.
            cards.Children.Remove(_quickCard);
            cards.Children.Add(_quickCard);
            Root.GotFocus += OnWidgetFocus; // rumble while moving around (Experimental)
            _achTimer.Tick += (s, e) => { if (_labs.Contains("wach") && _runningName.Length > 0) _link.Send("achievements"); };
            _achTimer.Start();
        }

        // ---- rumble while moving around

        private DateTime _lastRumble = DateTime.MinValue;
        private object _lastFocused;

        /// <summary>
        /// The highlight moved to something new with the controller (or keyboard): asks SquareDeck for a short buzz, as
        /// moving around SquareDeck itself does. Not for taps or mouse clicks, and at most every 40 ms.
        /// </summary>
        private void OnWidgetFocus(object sender, RoutedEventArgs e)
        {
            if (!_labs.Contains("wrumble")) return;
            var control = e.OriginalSource as Control;
            if (control == null || control.FocusState != FocusState.Keyboard) return;
            if (ReferenceEquals(control, _lastFocused)) return;
            _lastFocused = control;
            var now = DateTime.Now;
            if ((now - _lastRumble).TotalMilliseconds < 40) return;
            _lastRumble = now;
            _link.Send("navrumble");
        }

        // ---- Power: frame cap and Auto TDP

        private readonly Dictionary<int, Button> _capButtons = new Dictionary<int, Button>();
        private StackPanel _capRow, _autoRow;
        private ToggleSwitch _autoSwitch;
        private readonly Dictionary<int, Button> _autoTargets = new Dictionary<int, Button>();
        private TextBlock _autoStatus;
        private bool _autoSyncing;
        private int _autoTarget = 40;

        // ---- Experimental "Widget: frame cap slider": Off, and a slider from 30 to 120, no Save button

        private StackPanel _capSlideRow;
        private Slider _capSlider;
        private Button _capOff;
        private TextBlock _capValue;
        private DispatcherTimer _capTimer;
        private int _capPending;
        private bool _capSyncing;
        private DateTime _capTouched;

        private void BuildCapSlider(StackPanel stack)
        {
            _capSlideRow = new StackPanel { Visibility = Visibility.Collapsed };
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Children.Add(Label("Frame cap"));
            _capValue = Line("Off", 14, Colors.White, true);
            _capValue.VerticalAlignment = VerticalAlignment.Center;
            _capValue.Margin = new Thickness(0, 4, 2, 0);
            Grid.SetColumn(_capValue, 1);
            head.Children.Add(_capValue);
            _capSlideRow.Children.Add(head);

            var line = new Grid();
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _capOff = Segment("Off", null);
            _capOff.MinWidth = 64;
            _capOff.Margin = new Thickness(0, 0, 12, 0);
            _capOff.Click += (s, e) =>
            {
                _capTimer.Stop();
                _capTouched = DateTime.Now;
                ShowCap(0);
                _link.Send("fpscap", "value", 0);
            };
            line.Children.Add(_capOff);
            _capSlider = new Slider
            {
                Minimum = 30, Maximum = 120, StepFrequency = 5, SmallChange = 5, LargeChange = 15, TickFrequency = 15,
                TickPlacement = TickPlacement.Outside, Foreground = _accentBrush, IsThumbToolTipEnabled = true, VerticalAlignment = VerticalAlignment.Center, Value = 60
            };
            _capSlider.ValueChanged += (s, e) =>
            {
                if (_capSyncing) return;
                _capTouched = DateTime.Now;
                _capPending = (int)Math.Round(e.NewValue);
                ShowCap(_capPending);
                // Applied once the slider settles, not at every step on the way.
                _capTimer.Stop();
                _capTimer.Start();
            };
            Grid.SetColumn(_capSlider, 1);
            line.Children.Add(_capSlider);
            _capSlideRow.Children.Add(line);
            var ends = new Grid { Margin = new Thickness(76, -2, 0, 0) };
            ends.Children.Add(Line("30", 11, Subtle));
            var top = Line("120", 11, Subtle);
            top.HorizontalAlignment = HorizontalAlignment.Right;
            ends.Children.Add(top);
            _capSlideRow.Children.Add(ends);
            stack.Children.Add(_capSlideRow);

            _capTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            _capTimer.Tick += (s, e) =>
            {
                _capTimer.Stop();
                _capTouched = DateTime.Now;
                _link.Send("fpscap", "value", _capPending);
            };
        }

        private void ShowCap(int fps)
        {
            if (_capValue == null) return;
            _capValue.Text = fps > 0 ? fps + " FPS" : "Off";
            Mark(_capOff, fps <= 0);
            _capSlider.Opacity = fps > 0 ? 1 : 0.55;
        }

        private void SyncCapSlider(int cap)
        {
            if (_capSlideRow == null) return;
            // Not while you're moving it (the state lags a moment behind).
            if ((DateTime.Now - _capTouched).TotalMilliseconds < 1500 || _capTimer.IsEnabled) return;
            _capSyncing = true;
            if (cap > 0) _capSlider.Value = Math.Max(30, Math.Min(120, cap));
            _capSyncing = false;
            ShowCap(cap);
        }

        private void BuildPowerExtras()
        {
            var stack = _powerCard.Child as StackPanel;
            if (stack == null) return;

            _capRow = new StackPanel { Visibility = Visibility.Collapsed };
            _capRow.Children.Add(Label("Frame cap"));
            var caps = Columns(5);
            int i = 0;
            foreach (int fps in new[] { 0, 30, 40, 60 })
            {
                var b = Segment(fps == 0 ? "Off" : fps.ToString(), null);
                int value = fps;
                b.Click += (s, e) => _link.Send("fpscap", "value", value);
                Grid.SetColumn(b, i++);
                caps.Children.Add(b);
                _capButtons[fps] = b;
            }
            var save = Segment("Save", "");
            ToolTipService.SetToolTip(save, "Keep this cap for this game");
            save.Click += (s, e) => _link.Send("fpscapsave");
            Grid.SetColumn(save, 4);
            caps.Children.Add(save);
            _capRow.Children.Add(caps);
            stack.Children.Add(_capRow);
            BuildCapSlider(stack);

            _autoRow = new StackPanel { Visibility = Visibility.Collapsed };
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Children.Add(Label("Auto TDP: the lowest power that holds"));
            _autoSwitch = new ToggleSwitch { OnContent = "", OffContent = "", MinWidth = 0, Margin = new Thickness(0, 0, -12, 0), FocusVisualPrimaryBrush = _accentBrush };
            _autoSwitch.Toggled += (s, e) =>
            {
                if (_autoSyncing) return;
                var o = new JsonObject();
                o["op"] = JsonValue.CreateStringValue("autotdp");
                o["on"] = JsonValue.CreateBooleanValue(_autoSwitch.IsOn);
                o["target"] = JsonValue.CreateNumberValue(_autoTarget);
                _link.Send(o);
            };
            Grid.SetColumn(_autoSwitch, 1);
            head.Children.Add(_autoSwitch);
            _autoRow.Children.Add(head);
            var targets = Columns(3);
            i = 0;
            foreach (int fps in new[] { 30, 40, 60 })
            {
                var b = Segment(fps + " FPS", null);
                int value = fps;
                b.Click += (s, e) =>
                {
                    _autoTarget = value;
                    var o = new JsonObject();
                    o["op"] = JsonValue.CreateStringValue("autotdp");
                    o["target"] = JsonValue.CreateNumberValue(value);
                    _link.Send(o);
                };
                Grid.SetColumn(b, i++);
                targets.Children.Add(b);
                _autoTargets[fps] = b;
            }
            _autoRow.Children.Add(targets);
            _autoStatus = Line("", 12, Subtle);
            _autoStatus.Margin = new Thickness(2, 6, 0, 0);
            _autoRow.Children.Add(_autoStatus);
            stack.Children.Add(_autoRow);
        }

        // ---- Live: thermal warning

        private TextBlock _heatNote;

        private void BuildThermal()
        {
            var stack = _liveCard.Child as StackPanel;
            if (stack == null) return;
            _heatNote = Line("", 12, AmberColor, bold: true);
            _heatNote.Margin = new Thickness(2, 10, 0, 0);
            _heatNote.Visibility = Visibility.Collapsed;
            stack.Children.Add(_heatNote);
        }

        // ---- game notes

        private TextBlock _notesText;
        private TextBox _noteBox;
        private StackPanel _noteEditor;

        private void BuildNotes(StackPanel cards)
        {
            var body = new StackPanel();
            _notesText = Line("", 13, Colors.White);
            body.Children.Add(new ScrollViewer { Content = _notesText, MaxHeight = 170, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            var add = SmallButton("", "Add note");
            add.Margin = new Thickness(0, 10, 0, 0);
            body.Children.Add(add);
            _noteEditor = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0) };
            _noteBox = new TextBox { PlaceholderText = "A code, a hint, where you left off…", AcceptsReturn = false, TextWrapping = TextWrapping.Wrap };
            _noteEditor.Children.Add(_noteBox);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            var saveNote = SmallButton("", "Save");
            var cancel = SmallButton("", "Cancel");
            row.Children.Add(saveNote);
            row.Children.Add(cancel);
            _noteEditor.Children.Add(row);
            body.Children.Add(_noteEditor);
            add.Click += (s, e) => { _noteEditor.Visibility = Visibility.Visible; add.Visibility = Visibility.Collapsed; _noteBox.Focus(FocusState.Programmatic); };
            cancel.Click += (s, e) => { _noteEditor.Visibility = Visibility.Collapsed; add.Visibility = Visibility.Visible; _noteBox.Text = ""; };
            saveNote.Click += (s, e) =>
            {
                if (_noteBox.Text.Trim().Length > 0) _link.Send("noteadd", "text", _noteBox.Text.Trim());
                _noteBox.Text = "";
                _noteEditor.Visibility = Visibility.Collapsed;
                add.Visibility = Visibility.Visible;
            };
            AddCard(cards, "notes", "", "Game notes", body);
        }

        // ---- achievements

        private readonly DispatcherTimer _achTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        private TextBlock _achCount, _achNone;
        private ProgressBar _achBar;
        private StackPanel _achList;

        private void BuildAchievements(StackPanel cards)
        {
            var body = new StackPanel();
            _achBar = new ProgressBar { Minimum = 0, Maximum = 1, Height = 6, Foreground = _accentBrush, Margin = new Thickness(0, 0, 0, 10) };
            body.Children.Add(_achBar);
            _achNone = Line("Start a Steam game to see its achievements.", 13, Subtle);
            body.Children.Add(_achNone);
            _achList = new StackPanel();
            body.Children.Add(_achList);
            _achCount = new TextBlock { FontSize = 12, Foreground = new SolidColorBrush(Subtle) };
            AddCard(cards, "achievements", "", "Achievements", body, _achCount);
        }

        private void ApplyAchievements(JsonObject m)
        {
            _achList.Children.Clear();
            string none = Str(m, "none");
            _achNone.Visibility = none != null ? Visibility.Visible : Visibility.Collapsed;
            _achNone.Text = none ?? "";
            double unlocked = Num(m, "unlocked") ?? 0, total = Num(m, "total") ?? 0;
            _achBar.Visibility = total > 0 ? Visibility.Visible : Visibility.Collapsed;
            _achBar.Value = total > 0 ? unlocked / total : 0;
            _achCount.Text = total > 0 ? unlocked.ToString("0") + " / " + total.ToString("0") : "";
            string game = Str(m, "game") ?? "";
            var next = Arr(m, "next");
            if (next == null) return;
            if (next.Count > 0) _achList.Children.Add(Label("Next to get"));
            foreach (var v in next)
            {
                if (v.ValueType != JsonValueType.Object) continue;
                var a = v.GetObject();
                string name = Str(a, "name") ?? "", desc = Str(a, "desc") ?? "";
                var row = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
                row.Children.Add(Line(name, 14, Colors.White, bold: true));
                if (desc.Length > 0) row.Children.Add(Line(desc, 12, Subtle));
                var how = SmallButton("", "How to get this");
                how.Margin = new Thickness(0, 6, 0, 0);
                how.Click += async (s, e) =>
                {
                    string q = Uri.EscapeDataString(game + " \"" + name + "\" achievement guide");
                    try { await Windows.System.Launcher.LaunchUriAsync(new Uri("https://www.google.com/search?q=" + q)); } catch (Exception) { }
                };
                row.Children.Add(how);
                _achList.Children.Add(row);
            }
        }

        // ---- Ask Genie

        private TextBox _genieBox;
        private TextBlock _genieAnswer, _genieQuestion;
        private ProgressRing _genieRing;
        private Button _genieSpoiler;

        private void BuildGenie(StackPanel cards)
        {
            var body = new StackPanel();
            var askRow = new Grid();
            askRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            askRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _genieBox = new TextBox { PlaceholderText = "Ask about the game you're playing", TextWrapping = TextWrapping.Wrap };
            _genieBox.KeyDown += (s, e) => { if (e.Key == Windows.System.VirtualKey.Enter) AskGenie(_genieBox.Text); };
            askRow.Children.Add(_genieBox);
            var ask = SmallButton("", "Ask");
            ask.Margin = new Thickness(8, 0, 0, 0);
            ask.Click += (s, e) => AskGenie(_genieBox.Text);
            Grid.SetColumn(ask, 1);
            askRow.Children.Add(ask);
            body.Children.Add(askRow);
            var chips = new WrapPanelLite();
            foreach (var q in new[] { "How do I get started?", "Any tips for where I am?", "Any secrets worth finding?", "What's the best way to level up?" })
            {
                var chip = SmallButton(null, q);
                string question = q;
                chip.Click += (s, e) => AskGenie(question);
                chips.Children.Add(chip);
            }
            chips.Margin = new Thickness(0, 10, 0, 0);
            body.Children.Add(chips);
            var status = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            _genieRing = new ProgressRing { Width = 16, Height = 16, IsActive = false, Visibility = Visibility.Collapsed, Foreground = _accentBrush, Margin = new Thickness(0, 0, 8, 0) };
            status.Children.Add(_genieRing);
            _genieQuestion = Line("", 12, Subtle);
            status.Children.Add(_genieQuestion);
            body.Children.Add(status);
            _genieAnswer = Line("", 14, Colors.White);
            _genieAnswer.Margin = new Thickness(0, 6, 0, 0);
            _genieSpoiler = SmallButton("", "Spoiler: select to show the answer");
            _genieSpoiler.Visibility = Visibility.Collapsed;
            _genieSpoiler.Click += (s, e) => { _genieSpoiler.Visibility = Visibility.Collapsed; _genieAnswer.Visibility = Visibility.Visible; };
            body.Children.Add(_genieSpoiler);
            body.Children.Add(new ScrollViewer { Content = _genieAnswer, MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            AddCard(cards, "genie", "", "Ask Genie", body);
        }

        private void AskGenie(string question)
        {
            if (string.IsNullOrWhiteSpace(question)) return;
            _link.Send("genie", "q", question.Trim());
            _genieQuestion.Text = "Asking: " + question.Trim();
            _genieRing.IsActive = true;
            _genieRing.Visibility = Visibility.Visible;
            _genieAnswer.Text = "";
            _genieSpoiler.Visibility = Visibility.Collapsed;
            _genieBox.Text = "";
        }

        private void ApplyGenie(JsonObject m)
        {
            _genieRing.IsActive = false;
            _genieRing.Visibility = Visibility.Collapsed;
            _genieQuestion.Text = Str(m, "q") ?? "";
            string error = Str(m, "error");
            _genieAnswer.Text = error ?? Str(m, "a") ?? "";
            bool spoiler = error == null && Bool(m, "spoiler") == true;
            _genieSpoiler.Visibility = spoiler ? Visibility.Visible : Visibility.Collapsed;
            _genieAnswer.Visibility = spoiler ? Visibility.Collapsed : Visibility.Visible;
        }

        // ---- controller

        private TextBlock _padBattery;
        private Slider _padRumble;
        private Throttle _rumbleSend;
        private bool _padSyncing;
        private DateTime _userRumble;

        private void BuildController(StackPanel cards)
        {
            var body = new StackPanel();
            _padBattery = Line("", 14, Colors.White, bold: true);
            body.Children.Add(_padBattery);
            body.Children.Add(Label("Rumble strength"));
            _padRumble = new Slider { Minimum = 0, Maximum = 100, StepFrequency = 5, Foreground = _accentBrush, IsThumbToolTipEnabled = true };
            _rumbleSend = new Throttle(v => _link.Send("rumble", "value", v));
            _padRumble.ValueChanged += (s, e) => { if (_padSyncing) return; _userRumble = DateTime.Now; _rumbleSend.Push(e.NewValue); };
            body.Children.Add(_padRumble);
            var drift = SmallButton("", "Check for stick drift");
            drift.Margin = new Thickness(0, 8, 0, 0);
            drift.Click += (s, e) => _link.Send("drift");
            body.Children.Add(drift);
            AddCard(cards, "controller", "", "Controller", body);
        }

        // ---- friends

        private StackPanel _friendsList;

        private void BuildFriends(StackPanel cards)
        {
            _friendsList = new StackPanel();
            AddCard(cards, "friends", "", "Friends online", _friendsList);
        }

        private string _friendsSig = "";

        private void ApplyFriends(JsonObject s)
        {
            var list = Arr(s, "friends");
            string sig = list == null ? "" : list.Stringify();
            if (sig == _friendsSig) return;
            _friendsSig = sig;
            _friendsList.Children.Clear();
            if (Bool(s, "friendsready") != true) { _friendsList.Children.Add(Line("Add your Steam Web API key in SquareDeck (Settings > Accounts) to see friends here.", 13, Subtle)); return; }
            if (list == null || list.Count == 0) { _friendsList.Children.Add(Line("Nobody's online right now.", 13, Subtle)); return; }
            foreach (var v in list)
            {
                if (v.ValueType != JsonValueType.Object) continue;
                var f = v.GetObject();
                string name = Str(f, "name") ?? "", id = Str(f, "id") ?? "";
                bool playing = Bool(f, "playing") == true;
                var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var initial = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Margin = new Thickness(0, 0, 10, 0), Background = new SolidColorBrush(Color.FromArgb(0x55, _accent.R, _accent.G, _accent.B)) };
                initial.Child = new TextBlock { Text = name.Length > 0 ? name.Substring(0, 1).ToUpperInvariant() : "?", FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Colors.White), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                row.Children.Add(initial);
                var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                words.Children.Add(new TextBlock { Text = name, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Colors.White), TextTrimming = TextTrimming.CharacterEllipsis });
                words.Children.Add(new TextBlock { Text = Str(f, "status") ?? "", FontSize = 12, Foreground = playing ? _accentBrush : new SolidColorBrush(Subtle), TextTrimming = TextTrimming.CharacterEllipsis });
                Grid.SetColumn(words, 1);
                row.Children.Add(words);
                var chat = SmallButton("", "Message");
                chat.Margin = new Thickness(6, 0, 0, 0);
                chat.Click += (o, e) => _link.Send("chat", "id", id);
                Grid.SetColumn(chat, 2);
                row.Children.Add(chat);
                _friendsList.Children.Add(row);
            }
        }

        // ---- notifications

        private StackPanel _inboxList;
        private string _inboxSig = "";

        private void BuildInbox(StackPanel cards)
        {
            _inboxList = new StackPanel();
            AddCard(cards, "inbox", "", "Notifications", _inboxList);
        }

        private void ApplyInbox(JsonObject s)
        {
            var list = Arr(s, "inbox");
            string sig = list == null ? "" : list.Stringify();
            if (sig == _inboxSig) return;
            _inboxSig = sig;
            _inboxList.Children.Clear();
            if (list == null || list.Count == 0) { _inboxList.Children.Add(Line("No notifications.", 13, Subtle)); return; }
            foreach (var v in list)
            {
                if (v.ValueType != JsonValueType.Object) continue;
                var n = v.GetObject();
                var row = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
                var top = new Grid();
                top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var title = new TextBlock { Text = Str(n, "title") ?? "", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Colors.White), TextTrimming = TextTrimming.CharacterEllipsis };
                top.Children.Add(title);
                var when = new TextBlock { Text = Str(n, "when") ?? "", FontSize = 11, Foreground = new SolidColorBrush(Subtle), Margin = new Thickness(8, 0, 0, 0) };
                Grid.SetColumn(when, 1);
                top.Children.Add(when);
                row.Children.Add(top);
                string text = Str(n, "text") ?? "";
                if (text.Length > 0) row.Children.Add(Line(text, 12, Subtle));
                _inboxList.Children.Add(row);
            }
        }

        // ---- downloads

        private StackPanel _downloadsList;
        private Button _stopQueue;
        private string _downloadsSig = "";

        private void BuildDownloads(StackPanel cards)
        {
            var body = new StackPanel();
            _downloadsList = new StackPanel();
            body.Children.Add(_downloadsList);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            var steam = SmallButton("", "Steam downloads");
            steam.Click += (s, e) => _link.Send("steamdownloads");
            buttons.Children.Add(steam);
            _stopQueue = SmallButton("", "Stop SquareDeck's queue");
            _stopQueue.Click += (s, e) => _link.Send("clearqueue");
            buttons.Children.Add(_stopQueue);
            body.Children.Add(buttons);
            body.Children.Add(Line("Steam's own downloads pause from its downloads page.", 11, Subtle));
            AddCard(cards, "downloads", "", "Downloads", body);
        }

        private void ApplyDownloads(JsonObject s)
        {
            _stopQueue.Visibility = (Num(s, "queue") ?? 0) > 0 ? Visibility.Visible : Visibility.Collapsed;
            var list = Arr(s, "downloads");
            string sig = list == null ? "" : list.Stringify();
            if (sig == _downloadsSig) return;
            _downloadsSig = sig;
            _downloadsList.Children.Clear();
            if (list == null || list.Count == 0) { _downloadsList.Children.Add(Line("Nothing downloading.", 13, Subtle)); return; }
            foreach (var v in list)
            {
                if (v.ValueType != JsonValueType.Object) continue;
                var d = v.GetObject();
                var row = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
                row.Children.Add(new TextBlock { Text = Str(d, "name") ?? "", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Colors.White), TextTrimming = TextTrimming.CharacterEllipsis });
                row.Children.Add(Line(Str(d, "text") ?? "", 12, Subtle));
                _downloadsList.Children.Add(row);
            }
        }

        // ---- launch: apps and SquareDeck shortcuts

        private WrapPanelLite _appsRow;
        private StackPanel _appsBlock, _pagesBlock;
        private string _appsAsked = "";

        private void BuildLaunch(StackPanel cards)
        {
            var body = new StackPanel();
            _appsBlock = new StackPanel();
            _appsBlock.Children.Add(Label("Your apps"));
            _appsRow = new WrapPanelLite();
            _appsBlock.Children.Add(_appsRow);
            body.Children.Add(_appsBlock);
            _pagesBlock = new StackPanel();
            _pagesBlock.Children.Add(Label("SquareDeck"));
            var pages = new WrapPanelLite();
            foreach (var p in new[] { new[] { "home", "", "Home" }, new[] { "library", "", "Library" }, new[] { "retro", "", "Retro Lounge" }, new[] { "captures", "", "Captures" }, new[] { "settings", "", "Settings" } })
            {
                var b = SmallButton(p[1], p[2]);
                string page = p[0];
                b.Click += (s, e) => _link.Send("go", "page", page);
                pages.Children.Add(b);
            }
            _pagesBlock.Children.Add(pages);
            body.Children.Add(_pagesBlock);
            AddCard(cards, "launch", "", "Launch", body);
        }

        private async void ApplyApps(JsonObject m)
        {
            _appsRow.Children.Clear();
            var list = Arr(m, "apps");
            if (list == null) return;
            foreach (var v in list)
            {
                if (v.ValueType != JsonValueType.Object) continue;
                var a = v.GetObject();
                string id = Str(a, "id") ?? "", name = Str(a, "name") ?? "";
                var face = new Grid { Width = 40, Height = 40 };
                face.Children.Add(new TextBlock { Text = name.Length > 0 ? name.Substring(0, 1) : "?", FontSize = 18, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                var b = new Button
                {
                    Style = DeckButton, Content = face, Width = 56, Height = 56, Padding = new Thickness(6), Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(14),
                    Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF))
                };
                ToolTipService.SetToolTip(b, name);
                b.Click += (s, e) => _link.Send("launch", "id", id);
                _appsRow.Children.Add(b);
                var icon = await Decode(Str(a, "icon"));
                if (icon != null) { face.Children.Clear(); face.Children.Add(new Image { Source = icon, Stretch = Stretch.Uniform }); }
            }
        }

        // ---- header: companion and the card editor button

        private Image _companionImage;
        private TextBlock _companionSays;
        private StackPanel _companionBox;
        private string _companionAsked = "";
        private Button _editButton;

        private void BuildHeaderExtras()
        {
            var right = _battery.Parent as StackPanel;
            if (right == null) return;
            _companionBox = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), Visibility = Visibility.Collapsed };
            _companionSays = new TextBlock { FontSize = 11, Foreground = new SolidColorBrush(Subtle), MaxWidth = 120, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, Margin = new Thickness(0, 0, 6, 0) };
            _companionBox.Children.Add(_companionSays);
            _companionImage = new Image { Width = 34, Height = 34 };
            _companionBox.Children.Add(_companionImage);
            right.Children.Insert(0, _companionBox);
            _editButton = new Button { Style = DeckButton, Content = Glyph("", 15), Width = 40, Height = 36, Padding = new Thickness(0), Margin = new Thickness(0, 0, 8, 0), Background = new SolidColorBrush(Colors.Transparent), Visibility = Visibility.Collapsed };
            ToolTipService.SetToolTip(_editButton, "Arrange the cards");
            _editButton.Click += (s, e) => OpenEditor();
            right.Children.Insert(0, _editButton);
        }

        private async void ApplyCompanionImage(JsonObject m)
        {
            var image = await Decode(Str(m, "image"));
            if (image != null) _companionImage.Source = image;
        }

        // ---- game card: Invite

        private Button _invite;

        private void BuildParty()
        {
            var words = _closeGame.Parent as StackPanel;
            if (words == null) return;
            int at = words.Children.IndexOf(_closeGame);
            words.Children.Remove(_closeGame);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(_closeGame);
            _invite = SmallButton("", "Invite");
            _invite.Margin = new Thickness(8, 0, 0, 0);
            _invite.Visibility = Visibility.Collapsed;
            _invite.Click += (s, e) => _link.Send("party");
            row.Children.Add(_invite);
            words.Children.Insert(at, row);
        }

        // ---- the card editor

        private Grid _editor;
        private StackPanel _editorList;
        private TextBlock _editorPresetNote;
        private Button _presetSave, _presetClear;
        private List<string> _order = new List<string>(CardIds);
        private HashSet<string> _hidden = new HashSet<string>();
        private string _orderSig = "";

        private void BuildEditor()
        {
            _editor = new Grid { Background = new SolidColorBrush(Color.FromArgb(0xF6, 0x12, 0x16, 0x1C)), Visibility = Visibility.Collapsed, Padding = new Thickness(16, 6, 16, 16) };
            _editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _editor.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            var done = new Button { Style = DeckButton, Content = Glyph("", 16), Width = 44, Height = 40, Padding = new Thickness(0), Background = new SolidColorBrush(Colors.Transparent) };
            ToolTipService.SetToolTip(done, "Done");
            done.Click += (s, e) => CloseEditor();
            head.Children.Add(done);
            head.Children.Add(new TextBlock { Text = "Arrange the cards", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Colors.White), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
            _editor.Children.Add(head);
            _editorList = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            var scroll = new ScrollViewer { Content = _editorList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Grid.SetRow(scroll, 1);
            _editor.Children.Add(scroll);
            var foot = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            _editorPresetNote = Line("", 12, Subtle);
            foot.Children.Add(_editorPresetNote);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            _presetSave = SmallButton("", "Save this setup to the game");
            _presetSave.Click += (s, e) =>
            {
                var o = new JsonObject();
                o["op"] = JsonValue.CreateStringValue("presetsave");
                o["order"] = JsonValue.CreateStringValue(string.Join(",", _order));
                o["hidden"] = JsonValue.CreateStringValue(string.Join(",", _hidden));
                o["live"] = JsonValue.CreateStringValue(string.Join(",", _live));
                _link.Send(o);
            };
            buttons.Children.Add(_presetSave);
            _presetClear = SmallButton("", "Remove the game's preset");
            _presetClear.Click += (s, e) => _link.Send("presetclear");
            buttons.Children.Add(_presetClear);
            foot.Children.Add(buttons);
            Grid.SetRow(foot, 2);
            _editor.Children.Add(foot);
            Grid.SetRow(_editor, 1);
            Root.Children.Add(_editor);
        }

        private void OpenEditor()
        {
            FillEditor();
            _editor.Visibility = Visibility.Visible;
        }

        private void CloseEditor()
        {
            _editor.Visibility = Visibility.Collapsed;
            _editButton.Focus(FocusState.Programmatic);
        }

        private void FillEditor()
        {
            _editorList.Children.Clear();
            for (int i = 0; i < _order.Count; i++)
            {
                string id = _order[i];
                int nameAt = Array.IndexOf(CardIds, id);
                if (nameAt < 0) continue;
                var row = new Grid { Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(10, 4, 4, 4), Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)), CornerRadius = new CornerRadius(12) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                bool hidden = _hidden.Contains(id);
                row.Children.Add(new TextBlock { Text = CardNames[nameAt], FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(hidden ? Subtle : Colors.White), VerticalAlignment = VerticalAlignment.Center });
                int at = i;
                var up = new Button { Style = DeckButton, Content = Glyph("", 13), Width = 40, Height = 36, Padding = new Thickness(0), Background = new SolidColorBrush(Colors.Transparent), IsEnabled = i > 0 };
                ToolTipService.SetToolTip(up, "Move up");
                up.Click += (s, e) => { Swap(at, at - 1); };
                Grid.SetColumn(up, 1);
                row.Children.Add(up);
                var down = new Button { Style = DeckButton, Content = Glyph("", 13), Width = 40, Height = 36, Padding = new Thickness(0), Background = new SolidColorBrush(Colors.Transparent), IsEnabled = i < _order.Count - 1 };
                ToolTipService.SetToolTip(down, "Move down");
                down.Click += (s, e) => { Swap(at, at + 1); };
                Grid.SetColumn(down, 2);
                row.Children.Add(down);
                var eye = new Button { Style = DeckButton, Content = Glyph(hidden ? "" : "", 14), Width = 40, Height = 36, Padding = new Thickness(0), Background = new SolidColorBrush(Colors.Transparent) };
                ToolTipService.SetToolTip(eye, hidden ? "Show" : "Hide");
                eye.Click += (s, e) =>
                {
                    if (!_hidden.Remove(id)) _hidden.Add(id);
                    SendLayout();
                    FillEditor();
                };
                Grid.SetColumn(eye, 3);
                row.Children.Add(eye);
                _editorList.Children.Add(row);
            }
        }

        private void Swap(int a, int b)
        {
            if (a < 0 || b < 0 || a >= _order.Count || b >= _order.Count) return;
            string t = _order[a];
            _order[a] = _order[b];
            _order[b] = t;
            SendLayout();
            FillEditor();
            if (_editorList.Children.Count > b && _editorList.Children[b] is Grid row)
                foreach (var c in row.Children) if (c is Button btn && Grid.GetColumn(btn) == (b < a ? 1 : 2) && btn.IsEnabled) { btn.Focus(FocusState.Programmatic); break; }
        }

        private void SendLayout()
        {
            var o = new JsonObject();
            o["op"] = JsonValue.CreateStringValue("layout");
            o["order"] = JsonValue.CreateStringValue(string.Join(",", _order));
            o["hidden"] = JsonValue.CreateStringValue(string.Join(",", _hidden));
            _link.Send(o);
            ApplyOrder(true);
        }

        /// <summary>Puts the cards in the chosen order (only when it changed, so the focus isn't disturbed).</summary>
        private void ApplyOrder(bool force)
        {
            string sig = string.Join(",", _order);
            if (!force && sig == _orderSig) return;
            _orderSig = sig;
            int index = 0;
            foreach (var id in _order)
            {
                var card = CardOf(id);
                if (card == null || !_cardsPanel.Children.Contains(card)) continue;
                int now = _cardsPanel.Children.IndexOf(card);
                if (now != index) { _cardsPanel.Children.RemoveAt(now); _cardsPanel.Children.Insert(index, card); }
                index++;
            }
        }

        // ============================================================ what SquareDeck sends

        private void ApplyEightMessage(string type, JsonObject m)
        {
            if (type == "achievements") ApplyAchievements(m);
            else if (type == "genie") ApplyGenie(m);
            else if (type == "apps") ApplyApps(m);
            else if (type == "theme") ApplyTheme(m);
            else if (type == "companion") ApplyCompanionImage(m);
        }

        private void ApplyEight(JsonObject s)
        {
            _labs.Clear();
            var labs = Arr(s, "labs");
            if (labs != null) foreach (var v in labs) if (v.ValueType == JsonValueType.String) _labs.Add(v.GetString());
            var game = Obj(s, "game");
            string running = game != null && Bool(game, "other") != true ? (Str(game, "name") ?? "") : "";
            if (running != _runningName)
            {
                _runningName = running;
                if (_labs.Contains("wach")) _link.Send("achievements");
            }

            // Power: the wattage look (Experimental).
            Guard("wattage look", () =>
            {
                ApplyPowerLook(_labs.Contains("wpowerlook") ? (Str(s, "powerlook") ?? "dial") : "dial");
            });

            // Power: frame cap and Auto TDP.
            Guard("frame cap and Auto TDP", () =>
            {
                if (_capRow == null) return;
                bool slider = _labs.Contains("wfpsslider");
                _capRow.Visibility = _labs.Contains("wfpscap") && !slider ? Visibility.Visible : Visibility.Collapsed;
                if (_capSlideRow != null) _capSlideRow.Visibility = slider ? Visibility.Visible : Visibility.Collapsed;
                int cap = (int)(Num(s, "fpscap") ?? 0);
                foreach (var kv in _capButtons) Mark(kv.Value, kv.Key == cap);
                if (slider) SyncCapSlider(cap);
                var auto = Obj(s, "autotdp");
                _autoRow.Visibility = _labs.Contains("wautotdp") ? Visibility.Visible : Visibility.Collapsed;
                if (auto != null)
                {
                    _autoSyncing = true;
                    _autoSwitch.IsOn = Bool(auto, "on") == true;
                    _autoSyncing = false;
                    _autoTarget = (int)(Num(auto, "target") ?? 40);
                    foreach (var kv in _autoTargets) Mark(kv.Value, kv.Key == _autoTarget);
                    _autoStatus.Text = Str(auto, "status") ?? "";
                }
            });

            // Live: running hot.
            Guard("thermal warning", () =>
            {
                double? temp = Num(s, "cputemp");
                double limit = Num(s, "templimit") ?? 95;
                bool hot = _labs.Contains("wthermal") && temp != null && temp.Value >= limit - 5;
                if (_heatNote != null) _heatNote.Visibility = hot ? Visibility.Visible : Visibility.Collapsed;
                if (hot && _heatNote != null) _heatNote.Text = "Running hot: " + temp.Value.ToString("0") + "°, close to the " + limit.ToString("0") + "° limit. A lower TDP or frame cap cools it down.";
                _liveCard.BorderBrush = new SolidColorBrush(hot ? AmberColor : Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
                _liveCard.BorderThickness = new Thickness(hot ? 2 : 1);
            });

            // Live tiles from a layout or preset.
            Guard("Live tiles", () =>
            {
                string live = Str(s, "live");
                if (live != null) ApplyLiveIds(live);
            });

            // Cards.
            Guard("game notes", () =>
            {
                if (_labs.Contains("wnotes"))
                {
                    string notes = Str(s, "notes");
                    _notesText.Text = string.IsNullOrWhiteSpace(notes) ? (running.Length > 0 ? "No notes yet for " + running + "." : "Start a game to see its notes.") : notes;
                }
            });
            Guard("controller card", () =>
            {
                var pad = Obj(s, "pad");
                if (pad != null)
                {
                    string battery = Str(pad, "battery");
                    _padBattery.Text = Bool(pad, "connected") == true ? "Battery: " + (battery ?? "unknown") : "No controller found";
                    if ((DateTime.Now - _userRumble).TotalSeconds > 1.5) { _padSyncing = true; _padRumble.Value = Num(pad, "rumble") ?? 0; _padSyncing = false; }
                }
            });
            Guard("friends", () => { if (_labs.Contains("wfriends")) ApplyFriends(s); });
            Guard("notifications", () => { if (_labs.Contains("winbox")) ApplyInbox(s); });
            Guard("downloads", () => { if (_labs.Contains("wdownloads")) ApplyDownloads(s); });
            Guard("invite", () => { if (_invite != null) _invite.Visibility = _labs.Contains("wparty") && Str(s, "party") != null ? Visibility.Visible : Visibility.Collapsed; });
            Guard("launch apps and shortcuts", () =>
            {
                _appsBlock.Visibility = _labs.Contains("wapps") ? Visibility.Visible : Visibility.Collapsed;
                _pagesBlock.Visibility = _labs.Contains("wshortcuts") ? Visibility.Visible : Visibility.Collapsed;
                string appsKey = Str(s, "appskey");
                if (_labs.Contains("wapps") && appsKey != null && appsKey != _appsAsked) { _appsAsked = appsKey; _link.Send("apps"); }
            });

            // Theme and companion.
            Guard("style match and companion", () =>
            {
                string themeKey = _labs.Contains("wtheme") ? Str(s, "themekey") : null;
                if (themeKey != null && themeKey != _themeAsked) { _themeAsked = themeKey; _link.Send("theme"); }
                if (themeKey == null && _themed) ResetTheme();
                var companion = _labs.Contains("wcompanion") ? Obj(s, "companion") : null;
                if (_companionBox != null) _companionBox.Visibility = companion != null ? Visibility.Visible : Visibility.Collapsed;
                if (companion != null && _companionBox != null)
                {
                    _companionSays.Text = Str(companion, "says") ?? "";
                    ToolTipService.SetToolTip(_companionImage, Str(companion, "name") ?? "");
                    string key = Str(companion, "key") ?? "";
                    if (key != _companionAsked) { _companionAsked = key; _link.Send("companion", "mood", Str(companion, "mood") ?? "happy"); }
                }
            });

            // Experimental: the Switch 2 style, in the UI style's colors (WidgetPage.Switch2.cs).
            Guard("switch 2 style", () => ApplySwitch2(s));

            // Which experimental cards show.
            Guard("showing the cards", () =>
            {
                ShowCard("notes", _labs.Contains("wnotes") && running.Length > 0);
                ShowCard("achievements", _labs.Contains("wach"));
                ShowCard("genie", _labs.Contains("wgenie"));
                ShowCard("controller", _labs.Contains("wpad"));
                ShowCard("friends", _labs.Contains("wfriends"));
                ShowCard("inbox", _labs.Contains("winbox"));
                ShowCard("downloads", _labs.Contains("wdownloads"));
                ShowCard("launch", _labs.Contains("wapps") || _labs.Contains("wshortcuts"));
            });

            // Layout: order and hidden cards.
            Guard("card order", () =>
            {
                var layout = Obj(s, "layout");
                bool editing = _labs.Contains("wcards") || _labs.Contains("wpresets");
                if (_editButton != null) _editButton.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
                if (layout != null)
                {
                    var order = Arr(layout, "order");
                    if (order != null && _editor.Visibility != Visibility.Visible)
                    {
                        _order = order.Where(v => v.ValueType == JsonValueType.String).Select(v => v.GetString()).Where(id => Array.IndexOf(CardIds, id) >= 0).ToList();
                        foreach (var id in CardIds) if (!_order.Contains(id)) _order.Add(id);
                        var hidden = Arr(layout, "hidden");
                        _hidden = hidden == null ? new HashSet<string>() : new HashSet<string>(hidden.Where(v => v.ValueType == JsonValueType.String).Select(v => v.GetString()));
                    }
                    ApplyOrder(false);
                    _powerCard.Visibility = Visibility.Visible; // shown unless hidden here (nothing else hides it)
                    foreach (var id in _hidden) { var card = CardOf(id); if (card != null) card.Visibility = Visibility.Collapsed; }
                    string gameName = Str(layout, "game");
                    bool hasPreset = Bool(layout, "preset") == true;
                    bool presets = _labs.Contains("wpresets") && !string.IsNullOrEmpty(gameName);
                    _presetSave.Visibility = presets ? Visibility.Visible : Visibility.Collapsed;
                    _presetClear.Visibility = presets && hasPreset ? Visibility.Visible : Visibility.Collapsed;
                    _editorPresetNote.Text = presets ? (hasPreset ? gameName + " uses its own preset." : "Save this setup (cards, Live tiles and Quick tiles) to " + gameName + ".")
                        : _labs.Contains("wgameaware") && !string.IsNullOrEmpty(gameName) ? "Changes are remembered for " + gameName + "." : "";
                }
            });
        }

        /// <summary>Runs one part of an update; if it fails, the rest still runs and the problem is shown once, named.</summary>
        private void Guard(string part, Action action)
        {
            try { action(); }
            catch (Exception ex) { ReportOnce(part, ex); }
        }

        private void ShowCard(string id, bool on)
        {
            Border card;
            if (!_eightCards.TryGetValue(id, out card)) return;
            card.Visibility = on && !_hidden.Contains(id) ? Visibility.Visible : Visibility.Collapsed;
        }

        // ---- the style match

        private string _themeAsked;
        private bool _themed;
        private Image _themeArt;

        private static Color? ParseColor(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex[0] != '#') return null;
            try
            {
                string h = hex.Substring(1);
                if (h.Length == 6) h = "FF" + h;
                if (h.Length != 8) return null;
                return Color.FromArgb(Convert.ToByte(h.Substring(0, 2), 16), Convert.ToByte(h.Substring(2, 2), 16), Convert.ToByte(h.Substring(4, 2), 16), Convert.ToByte(h.Substring(6, 2), 16));
            }
            catch (Exception) { return null; }
        }

        private async void ApplyTheme(JsonObject m)
        {
            if (!_labs.Contains("wtheme")) return;
            _themed = true;
            var cardColor = ParseColor(Str(m, "card"));
            var shared = Application.Current.Resources["DeckCardBrush"] as SolidColorBrush;
            if (shared != null && cardColor != null) shared.Color = Color.FromArgb(0xD8, cardColor.Value.R, cardColor.Value.G, cardColor.Value.B);
            var bg = ParseColor(Str(m, "bg"));
            if (bg != null) Root.Background = new SolidColorBrush(Color.FromArgb(0xF2, bg.Value.R, bg.Value.G, bg.Value.B));

            if (_themeArt == null)
            {
                _themeArt = new Image { Stretch = Stretch.UniformToFill, Opacity = 0, IsHitTestVisible = false };
                Grid.SetRowSpan(_themeArt, 2);
                Root.Children.Insert(Math.Max(0, Root.Children.IndexOf(_backArt) + 1), _themeArt);
            }
            var wallpaper = await Decode(Str(m, "wallpaper"));
            _themeArt.Source = wallpaper;
            _themeArt.Opacity = wallpaper != null ? 0.32 : 0;

            // The style's font, saved in the widget's own folder (it can't read SquareDeck's).
            string file = Str(m, "fontfile"), name = Str(m, "fontname"), data = Str(m, "font");
            try
            {
                if (!string.IsNullOrEmpty(file) && !string.IsNullOrEmpty(data) && !string.IsNullOrEmpty(name))
                {
                    var folder = await ApplicationData.Current.LocalFolder.CreateFolderAsync("WidgetFonts", CreationCollisionOption.OpenIfExists);
                    var stored = await folder.CreateFileAsync(file, CreationCollisionOption.ReplaceExisting);
                    await FileIO.WriteBytesAsync(stored, Convert.FromBase64String(data));
                    FontFamily = new FontFamily("ms-appdata:///local/WidgetFonts/" + file + "#" + name);
                }
                else if (!string.IsNullOrEmpty(name)) FontFamily = new FontFamily(name + ", Segoe UI");
            }
            catch (Exception) { }
        }

        private void ResetTheme()
        {
            _themed = false;
            _themeAsked = null;
            var shared = Application.Current.Resources["DeckCardBrush"] as SolidColorBrush;
            if (shared != null) shared.Color = Color.FromArgb(0xC8, 0x1B, 0x21, 0x29);
            if (_themeArt != null) { _themeArt.Source = null; _themeArt.Opacity = 0; }
            FontFamily = new FontFamily("Segoe UI");
            ApplyBackground(_background);
        }
    }

    /// <summary>A simple wrapping row (UWP has no WrapPanel): children flow left to right onto new lines.</summary>
    public sealed class WrapPanelLite : Panel
    {
        protected override Windows.Foundation.Size MeasureOverride(Windows.Foundation.Size available)
        {
            double x = 0, y = 0, line = 0, width = 0;
            foreach (var child in Children)
            {
                child.Measure(new Windows.Foundation.Size(available.Width, double.PositiveInfinity));
                var d = child.DesiredSize;
                if (x > 0 && x + d.Width > available.Width) { y += line; x = 0; line = 0; }
                x += d.Width;
                line = Math.Max(line, d.Height);
                width = Math.Max(width, x);
            }
            return new Windows.Foundation.Size(double.IsInfinity(available.Width) ? width : available.Width, y + line);
        }

        protected override Windows.Foundation.Size ArrangeOverride(Windows.Foundation.Size final)
        {
            double x = 0, y = 0, line = 0;
            foreach (var child in Children)
            {
                var d = child.DesiredSize;
                if (x > 0 && x + d.Width > final.Width) { y += line; x = 0; line = 0; }
                child.Arrange(new Windows.Foundation.Rect(x, y, d.Width, d.Height));
                x += d.Width;
                line = Math.Max(line, d.Height);
            }
            return final;
        }
    }
}
