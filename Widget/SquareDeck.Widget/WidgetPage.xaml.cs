using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Gaming.XboxGameBar;
using SquareDeck.Widget.Controls;
using SquareDeck.Widget.Link;
using Windows.Data.Json;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Xaml.Shapes;

namespace SquareDeck.Widget
{
    /// <summary>
    /// The widget: SquareDeck's command center inside Game Bar.
    ///   - a rail on the left with brightness and volume (vertical sliders: A to grab, up / down to change, B to let go),
    ///   - Power: the dial for the TDP, minus and plus, four steps across the chip's range, back to the game's profile,
    ///   - Live: four tiles you choose (processor, graphics, APU power, TDP, frame rate, temperatures, fan, memory,
    ///     battery…), each with its last half minute,
    ///   - ROG Ally (on an Ally): the operating mode, fan curve, lights and controller / mouse mode,
    ///   - Quick: overlay, refresh rate, night light, HDR, do not disturb, Wi-Fi, Bluetooth and more.
    /// SquareDeck does the work (it has the permissions); the widget shows what's going on and asks.
    /// </summary>
    public sealed partial class WidgetPage : Page
    {
        private static readonly Color Teal = Color.FromArgb(255, 0x0A, 0xB9, 0xC9);
        private static readonly Color Blue = Color.FromArgb(255, 0x3F, 0x7C, 0xFF);
        private static readonly Color Violet = Color.FromArgb(255, 0xC4, 0x6B, 0xE6);
        private static readonly Color Pink = Color.FromArgb(255, 0xFF, 0x7F, 0xB5);
        private static readonly Color Subtle = Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF);

        private readonly DeckLink _link = new DeckLink();
        // The highlight color: SquareDeck's teal, the UI style's color or the game's (Settings > Controller > Widget colors in SquareDeck).
        private Color _accent = Teal;
        private readonly SolidColorBrush _accentBrush = new SolidColorBrush(Teal);
        private GradientStop _glowTop, _glowBottom;
        private XboxGameBarWidget _widget;
        private bool _syncing;

        // Header
        private TextBlock _subtitle, _battery, _status;
        private Ellipse _dot;
        // Rail
        private Slider _bright, _volume;
        private TextBlock _muteGlyph;
        private Throttle _brightSend, _volumeSend;
        private DateTime _userBright, _userVolume, _userTdp;
        // Power
        private PowerDial _dial;
        private Button _minus, _plus, _reset;
        private readonly Button[] _presets = new Button[4];
        private readonly TextBlock[] _presetWatts = new TextBlock[4];
        private TextBlock _powerInfo, _powerHelp;
        private Grid _powerBody;
        private int[] _presetValues = { 0, 0, 0, 0 };
        private int _watts, _min = 5, _max = 30;
        private bool _tdpReady;
        // Live
        private DateTime _lastSample;
        // ROG Ally
        private Border _allyCard;
        private readonly Dictionary<string, Button> _modes = new Dictionary<string, Button>(), _fans = new Dictionary<string, Button>();
        private Button _lights, _mouse;
        // Quick
        private readonly Dictionary<string, (Button Button, TextBlock Glyph, TextBlock Label, string Name)> _quick = new Dictionary<string, (Button, TextBlock, TextBlock, string)>();
        private int[] _rates = new int[0];
        private int _hz;
        // Game card
        private Border _gameCard, _liveCard, _quickCard;
        private Image _gameHero, _gameIcon;
        private Image _backArt;            // the running game's art behind everything ("The game's art" background)
        private string _background = "";
        private TextBlock _gameName, _gameDetail;
        private ToggleSwitch _gameProfile;
        private Button _closeGame;
        private TextBlock _closeLabel;
        private string _gameKey = "", _artAsked = "";
        private bool _syncingProfile;
        private DateTime _closeArmed = DateTime.MinValue;
        private HashSet<string> _sections = new HashSet<string> { "game", "live", "ally", "quick" };
        // Overlays
        private Grid _offline;
        private TextBlock _startHelp;
        private Border _notePill;
        private TextBlock _note;
        private readonly DispatcherTimer _noteTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };
        private Storyboard _pulse;
        private bool _focusedOnce;

        public WidgetPage()
        {
            InitializeComponent();
            Build();
            _link.Message += m => Ui(() => OnMessage(m));
            _link.ConnectionChanged += on => Ui(() => ShowConnected(on));
            _noteTimer.Tick += (s, e) => { _noteTimer.Stop(); _notePill.Visibility = Visibility.Collapsed; };
            Loaded += (s, e) => { ShowConnected(false); _link.Start(); };
            Unloaded += (s, e) => _link.Dispose();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            _widget = e.Parameter as XboxGameBarWidget;
        }

        private void Ui(Action work)
        {
            var _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => { try { work(); } catch (Exception) { } });
        }

        // ============================================================ building

        private void Build()
        {
            Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _backArt = new Image { Stretch = Stretch.UniformToFill, Opacity = 0, IsHitTestVisible = false };
            Grid.SetRowSpan(_backArt, 2);
            Root.Children.Add(_backArt);

            // A soft teal glow in the top corner, like light off the logo.
            var glow = new Ellipse { Width = 380, Height = 380, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(-190, -220, 0, 0), IsHitTestVisible = false };
            var glowFill = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0.5, 0), EndPoint = new Windows.Foundation.Point(0.5, 1) };
            _glowTop = new GradientStop { Color = Color.FromArgb(0x00, _accent.R, _accent.G, _accent.B), Offset = 0 };
            _glowBottom = new GradientStop { Color = Color.FromArgb(0x38, _accent.R, _accent.G, _accent.B), Offset = 1 };
            glowFill.GradientStops.Add(_glowTop);
            glowFill.GradientStops.Add(_glowBottom);
            glow.Fill = glowFill;
            Grid.SetRowSpan(glow, 2);
            Root.Children.Add(glow);

            Root.Children.Add(BuildHeader());

            var body = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(body, 1);
            body.Children.Add(BuildRail());
            // The sliders shrink to fit, so the power and SquareDeck buttons under them always show.
            body.SizeChanged += (o, e) => FitRail(e.NewSize.Height);

            var cards = new StackPanel { Margin = new Thickness(0, 0, 14, 18) };
            cards.Children.Add(BuildGame());
            cards.Children.Add(BuildMedia());
            _powerCard = (Border)BuildPower();
            cards.Children.Add(_powerCard);
            _liveCard = (Border)BuildLive();
            cards.Children.Add(_liveCard);
            cards.Children.Add(BuildAlly());
            _quickCard = (Border)BuildQuick();
            cards.Children.Add(_quickCard);
            _cardsPanel = cards;
            BuildEightCards(cards); // the experimental cards (WidgetPage.Eight.cs)
            var scroll = new ScrollViewer { Content = cards, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            Grid.SetColumn(scroll, 1);
            body.Children.Add(scroll);
            Root.Children.Add(body);
            Root.Children.Add(BuildSheet()); // the Wi-Fi and Bluetooth panels, over the cards

            // The line that says what just happened ("120 Hz", "Fans: Cool"), at the bottom.
            _note = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Colors.White) };
            _notePill = new Border
            {
                Child = _note, Padding = new Thickness(18, 9, 18, 10), CornerRadius = new CornerRadius(20), Margin = new Thickness(0, 0, 0, 18),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Visibility = Visibility.Collapsed,
                Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x0E, 0x6E, 0x7A)), BorderBrush = _accentBrush, BorderThickness = new Thickness(1)
            };
            Grid.SetRow(_notePill, 1);
            Root.Children.Add(_notePill);

            Root.Children.Add(BuildOffline());
        }

        private FrameworkElement BuildHeader()
        {
            var header = new Grid { Padding = new Thickness(18, 16, 18, 10) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Your profile picture (SquareDeck's logo until it arrives) and your name.
            var logo = new Grid { Width = 40, Height = 40, Margin = new Thickness(0, 0, 12, 0) };
            _headerLogo = new Image { Source = new BitmapImage(new Uri("ms-appx:///Assets/Logo.png")), Width = 36, Height = 36 };
            logo.Children.Add(_headerLogo);
            _headerAvatar = new Ellipse { Width = 40, Height = 40, Visibility = Visibility.Collapsed };
            logo.Children.Add(_headerAvatar);
            header.Children.Add(logo);

            var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            _headerName = new TextBlock { Text = "SquareDeck", FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Colors.White), TextTrimming = TextTrimming.CharacterEllipsis };
            titles.Children.Add(_headerName);
            _subtitle = new TextBlock { Text = "Command center", FontSize = 12, Foreground = new SolidColorBrush(Subtle), TextTrimming = TextTrimming.CharacterEllipsis };
            titles.Children.Add(_subtitle);
            Grid.SetColumn(titles, 1);
            header.Children.Add(titles);

            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _battery = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Colors.White), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            right.Children.Add(_battery);
            var chip = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 4, 11, 5), Background = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)) };
            var chipRow = new StackPanel { Orientation = Orientation.Horizontal };
            _dot = new Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
            chipRow.Children.Add(_dot);
            _status = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Colors.White) };
            chipRow.Children.Add(_status);
            chip.Child = chipRow;
            right.Children.Add(chip);
            Grid.SetColumn(right, 2);
            header.Children.Add(right);
            return header;
        }

        /// <summary>The rail: brightness, volume (with mute), and a way into SquareDeck.</summary>
        private FrameworkElement BuildRail()
        {
            var rail = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(6, 4, 6, 18) };
            _rail = rail;
            var back = _railBack = new Border
            {
                CornerRadius = new CornerRadius(22), Padding = new Thickness(0, 14, 0, 14), Width = 56,
                Background = new SolidColorBrush(Color.FromArgb(0x80, 0x1B, 0x21, 0x29)), Child = rail, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(10, 0, 6, 18)
            };

            _bright = RailSlider();
            _brightSend = new Throttle(v => _link.Send("brightness", "value", v));
            _bright.ValueChanged += (s, e) => { if (_syncing) return; _userBright = DateTime.Now; _brightSend.Push(e.NewValue); };
            rail.Children.Add(_bright);
            rail.Children.Add(RailGlyph(""));

            rail.Children.Add(new Border { Height = 18 });

            _volume = RailSlider();
            _volumeSend = new Throttle(v => _link.Send("volume", "value", v));
            _volume.ValueChanged += (s, e) => { if (_syncing) return; _userVolume = DateTime.Now; _volumeSend.Push(e.NewValue); };
            rail.Children.Add(_volume);
            _muteGlyph = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center };
            var mute = new Button { Style = DeckButton, Content = _muteGlyph, Width = 44, Height = 40, Padding = new Thickness(0), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, Background = new SolidColorBrush(Colors.Transparent) };
            ToolTipService.SetToolTip(mute, "Mute");
            mute.Click += (s, e) => _link.Send("mute");
            rail.Children.Add(mute);
            rail.Children.Add(BuildPowerButton());

            rail.Children.Add(new Rectangle { Height = 1, Width = 30, Fill = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), Margin = new Thickness(0, 14, 0, 14) });

            var open = new Button
            {
                Style = DeckButton, Width = 44, Height = 44, Padding = new Thickness(6), HorizontalAlignment = HorizontalAlignment.Center,
                Content = new Image { Source = new BitmapImage(new Uri("ms-appx:///Assets/Logo.png")) }, Background = new SolidColorBrush(Colors.Transparent)
            };
            ToolTipService.SetToolTip(open, "Open SquareDeck");
            open.Click += (s, e) => _link.Send("open");
            rail.Children.Add(open);
            return back;
        }

        /// <summary>
        /// The power button under the volume: Lock, Sleep, Hibernate, Restart and Shut down, each with its icon.
        /// Restart and Shut down ask once more ("now?"), so a stray press doesn't end the game.
        /// </summary>
        private Button BuildPowerButton()
        {
            var power = new Button
            {
                Style = DeckButton, Content = Glyph("\uE7E8", 18), Width = 44, Height = 40, Padding = new Thickness(0), Margin = new Thickness(0, 4, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center, Background = new SolidColorBrush(Colors.Transparent)
            };
            ToolTipService.SetToolTip(power, "Power");
            var menu = new MenuFlyout { Placement = FlyoutPlacementMode.Right };
            Action<string, string> send = (id, label) => _link.Send("power", "id", id);
            Func<string, string, MenuFlyoutItem> item = (glyph, text) => new MenuFlyoutItem
            {
                Text = text, Icon = new FontIcon { Glyph = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets") }
            };
            var lockItem = item("\uE72E", "Lock");
            lockItem.Click += (s, e) => send("lock", "Lock");
            var sleepItem = item("\uE708", "Sleep");
            sleepItem.Click += (s, e) => send("sleep", "Sleep");
            var hibernateItem = item("\uE823", "Hibernate");
            hibernateItem.Click += (s, e) => send("hibernate", "Hibernate");
            var restartItem = item("\uE777", "Restart");
            restartItem.Click += (s, e) => Confirm(power, "\uE777", "Restart now", () => send("restart", "Restart"));
            var shutdownItem = item("\uE7E8", "Shut down");
            shutdownItem.Click += (s, e) => Confirm(power, "\uE7E8", "Shut down now", () => send("shutdown", "Shut down"));
            menu.Items.Add(lockItem);
            menu.Items.Add(sleepItem);
            menu.Items.Add(hibernateItem);
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(restartItem);
            menu.Items.Add(shutdownItem);
            power.Click += (s, e) => menu.ShowAt(power);
            return power;
        }

        /// <summary>A second, small menu to make sure: the action, or Cancel.</summary>
        private static void Confirm(FrameworkElement at, string glyph, string text, Action yes)
        {
            var sure = new MenuFlyout { Placement = FlyoutPlacementMode.Right };
            var go = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets") } };
            go.Click += (s, e) => yes();
            var cancel = new MenuFlyoutItem { Text = "Cancel", Icon = new FontIcon { Glyph = "\uE711", FontFamily = new FontFamily("Segoe MDL2 Assets") } };
            sure.Items.Add(go);
            sure.Items.Add(cancel);
            sure.ShowAt(at);
        }

        private StackPanel _rail;
        private Border _railBack;

        /// <summary>
        /// Sizes the brightness and volume sliders to the room there is: up to 150 each, down to 56, so everything
        /// on the rail (mute, power, Open SquareDeck) fits in the widget's height.
        /// </summary>
        private void FitRail(double height)
        {
            if (_rail == null || _bright == null || height <= 0) return;
            double now = _bright.Height;
            _rail.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            double fixedPart = _rail.DesiredSize.Height - 2 * now;
            double room = height - _railBack.Margin.Top - _railBack.Margin.Bottom - _railBack.Padding.Top - _railBack.Padding.Bottom; // the rail's own margin is in its measured size
            double each = Math.Max(56, Math.Min(150, Math.Floor((room - fixedPart) / 2)));
            if (Math.Abs(each - now) < 1) return;
            _bright.Height = each;
            _volume.Height = each;
        }

        private Slider RailSlider() => new Slider
        {
            Orientation = Orientation.Vertical, Height = 150, Minimum = 0, Maximum = 100, StepFrequency = 1, SmallChange = 5, LargeChange = 10,
            HorizontalAlignment = HorizontalAlignment.Center, Foreground = _accentBrush,
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            // With a controller: A grabs the slider, up / down changes it, B lets go (so left / right can leave the rail).
            IsFocusEngagementEnabled = true, IsThumbToolTipEnabled = true,
            FocusVisualPrimaryBrush = _accentBrush,
        };

        private static TextBlock RailGlyph(string glyph) => new TextBlock
        {
            Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0), Foreground = new SolidColorBrush(Colors.White)
        };

        private Style DeckButton => (Style)Application.Current.Resources["DeckButton"];

        /// <summary>A card: a small icon and spaced capitals for its name, then what's in it.</summary>
        private Border Card(string glyph, string title, UIElement content, FrameworkElement extra = null)
        {
            var stack = new StackPanel();
            var head = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            var name = new StackPanel { Orientation = Orientation.Horizontal };
            name.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14, Foreground = _accentBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 9, 0) });
            name.Children.Add(new TextBlock { Text = title.ToUpperInvariant(), FontSize = 12, FontWeight = FontWeights.Bold, CharacterSpacing = 140, Foreground = new SolidColorBrush(Subtle), VerticalAlignment = VerticalAlignment.Center });
            head.Children.Add(name);
            if (extra != null) { extra.HorizontalAlignment = HorizontalAlignment.Right; extra.VerticalAlignment = VerticalAlignment.Center; head.Children.Add(extra); }
            stack.Children.Add(head);
            stack.Children.Add(content);
            return new Border
            {
                Child = stack, CornerRadius = new CornerRadius(20), Padding = new Thickness(16, 14, 16, 16), Margin = new Thickness(0, 0, 0, 12),
                Background = (Brush)Application.Current.Resources["DeckCardBrush"],
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1)
            };
        }

        private static TextBlock Glyph(string glyph, double size) => new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = size, HorizontalAlignment = HorizontalAlignment.Center };

        private static Grid Columns(int count)
        {
            var g = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
            for (int i = 0; i < count; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            return g;
        }

        // ---------------------------------------------------------------- Game

        /// <summary>
        /// The game that's running: its picture across the card, its icon, how long you've played, a switch for its own
        /// power profile (the dial then edits that profile) and Close game (press twice).
        /// </summary>
        private FrameworkElement BuildGame()
        {
            var grid = new Grid { Height = 118 };
            _gameHero = new Image { Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.9 };
            grid.Children.Add(_gameHero);
            // Dark from the left so the words read over any picture.
            var shade = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(1, 0) };
            shade.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0xE8, 0x10, 0x13, 0x18), Offset = 0 });
            shade.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0x99, 0x10, 0x13, 0x18), Offset = 0.55 });
            shade.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0x40, 0x10, 0x13, 0x18), Offset = 1 });
            grid.Children.Add(new Border { Background = shade });

            var row = new Grid { Padding = new Thickness(14, 12, 12, 12) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var iconFrame = new Border { Width = 60, Height = 60, CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            _gameIcon = new Image { Stretch = Stretch.UniformToFill };
            iconFrame.Child = _gameIcon;
            row.Children.Add(iconFrame);
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            _gameName = new TextBlock { FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Colors.White), TextTrimming = TextTrimming.CharacterEllipsis };
            words.Children.Add(_gameName);
            _gameDetail = new TextBlock { FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)), Margin = new Thickness(0, 3, 0, 8), TextTrimming = TextTrimming.CharacterEllipsis };
            words.Children.Add(_gameDetail);
            _closeLabel = new TextBlock { Text = "Close game", FontSize = 12, FontWeight = FontWeights.SemiBold };
            var closeRow = new StackPanel { Orientation = Orientation.Horizontal };
            closeRow.Children.Add(new TextBlock { Text = "\uE711", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 7, 0) });
            closeRow.Children.Add(_closeLabel);
            _closeGame = new Button { Style = DeckButton, Content = closeRow, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(11, 5, 12, 6), CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)) };
            _closeGame.Click += (s, e) => CloseGamePressed();
            words.Children.Add(_closeGame);
            Grid.SetColumn(words, 1);
            row.Children.Add(words);

            var profile = new StackPanel { VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Right };
            profile.Children.Add(new TextBlock { Text = "Per-game profile", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Colors.White), HorizontalAlignment = HorizontalAlignment.Right });
            _gameProfile = new ToggleSwitch { OnContent = "", OffContent = "", MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, -2, -12, 0), FocusVisualPrimaryBrush = _accentBrush };
            _gameProfile.Toggled += (s, e) =>
            {
                if (_syncingProfile) return;
                var o = new JsonObject { ["op"] = JsonValue.CreateStringValue("gameprofile"), ["on"] = JsonValue.CreateBooleanValue(_gameProfile.IsOn) };
                _link.Send(o);
            };
            ToolTipService.SetToolTip(_gameProfile, "On: this game keeps its own TDP, and the dial edits it. Off: your usual power");
            profile.Children.Add(_gameProfile);
            Grid.SetColumn(profile, 2);
            row.Children.Add(profile);
            grid.Children.Add(row);

            _gameCard = new Border
            {
                Child = grid, CornerRadius = new CornerRadius(20), Margin = new Thickness(0, 0, 0, 12), Visibility = Visibility.Collapsed,
                Background = (Brush)Application.Current.Resources["DeckCardBrush"], BorderBrush = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1)
            };
            grid.SizeChanged += (s, e) => grid.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
            return _gameCard;
        }

        private void CloseGamePressed()
        {
            // Twice within three seconds, so a stray press doesn't end your game.
            if ((DateTime.Now - _closeArmed).TotalSeconds < 3)
            {
                _closeArmed = DateTime.MinValue;
                _closeLabel.Text = "Closing…";
                _link.Send("closegame");
                return;
            }
            _closeArmed = DateTime.Now;
            _closeLabel.Text = "Press again to close";
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (s, e) => { timer.Stop(); if (_closeArmed != DateTime.MinValue) _closeLabel.Text = "Close game"; _closeArmed = DateTime.MinValue; };
            timer.Start();
        }

        private void ApplyGame(JsonObject game)
        {
            bool show = game != null && _sections.Contains("game");
            _gameCard.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (game == null) { _gameKey = ""; return; }
            string key = Str(game, "key") ?? "";
            _gameName.Text = Str(game, "name") ?? "";
            var parts = new List<string>();
            double? hours = Num(game, "hours"), minutes = Num(game, "minutes");
            if (hours != null && hours.Value > 0) parts.Add(hours.Value.ToString("0.#") + " h played");
            if (minutes != null) parts.Add(minutes.Value < 60 ? minutes.Value.ToString("0") + " min now" : Math.Floor(minutes.Value / 60) + " h " + (minutes.Value % 60).ToString("0") + " min now");
            if (Bool(game, "other") == true) parts.Add("Not started from SquareDeck");
            _gameDetail.Text = string.Join("  \u00B7  ", parts);
            _closeGame.Visibility = Bool(game, "close") == true ? Visibility.Visible : Visibility.Collapsed;
            if (_closeArmed == DateTime.MinValue && _closeLabel.Text == "Closing…" && key != _gameKey) _closeLabel.Text = "Close game";

            // Its own profile only for games SquareDeck knows, with TDP control set up.
            _gameProfile.Visibility = key.Length > 0 && _tdpReady ? Visibility.Visible : Visibility.Collapsed;
            _syncingProfile = true;
            _gameProfile.IsOn = Bool(game, "profile") == true;
            _syncingProfile = false;

            if (key != _gameKey)
            {
                _gameKey = key;
                _gameHero.Source = null;
                _gameIcon.Source = null;
                _backArt.Source = null;
                _closeLabel.Text = "Close game";
            }
            if (key.Length > 0 && _artAsked != key)
            {
                _artAsked = key;
                _link.Send("art", "key", key);
            }
        }

        // ---------------------------------------------------------------- Media (what SquareDeck is playing)

        private Border _mediaCard;
        private Image _mediaArt;
        private TextBlock _mediaTitle, _mediaArtist, _mediaPlayGlyph, _mediaNoArt;
        private string _mediaKey = "", _mediaArtAsked = "";

        /// <summary>
        /// A row that appears while SquareDeck plays music or video (Settings > Experimental > Media controls in the
        /// widget): the cover art, the song and artist, and previous / play-pause / next.
        /// </summary>
        private FrameworkElement BuildMedia()
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var cover = new Grid { Width = 64, Height = 64, Margin = new Thickness(0, 0, 12, 0) };
            cover.Children.Add(new Border { CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)) });
            _mediaNoArt = Glyph("\uE8D6", 24);
            _mediaNoArt.VerticalAlignment = VerticalAlignment.Center;
            cover.Children.Add(_mediaNoArt);
            _mediaArt = new Image { Stretch = Stretch.UniformToFill };
            cover.Children.Add(new Border { CornerRadius = new CornerRadius(12), Child = _mediaArt });
            row.Children.Add(cover);

            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            _mediaTitle = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = new SolidColorBrush(Colors.White) };
            _mediaArtist = new TextBlock { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = new SolidColorBrush(Subtle), Margin = new Thickness(0, 3, 0, 0) };
            words.Children.Add(_mediaTitle);
            words.Children.Add(_mediaArtist);
            Grid.SetColumn(words, 1);
            row.Children.Add(words);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            Func<string, string, double, Button> make = (glyph, cmd, size) =>
            {
                var g = Glyph(glyph, 16);
                var b = new Button { Style = DeckButton, Content = g, Width = size, Height = size, Padding = new Thickness(0), Margin = new Thickness(3, 0, 0, 0), CornerRadius = new CornerRadius(size / 2) };
                b.Click += (s, e) => _link.Send("media", "cmd", cmd);
                return b;
            };
            buttons.Children.Add(make("\uE892", "previous", 38));
            var play = make("\uE768", "toggle", 46);
            _mediaPlayGlyph = (TextBlock)play.Content;
            play.Background = _accentBrush;
            _mediaPlayGlyph.Foreground = new SolidColorBrush(Color.FromArgb(255, 0x0B, 0x16, 0x1A));
            buttons.Children.Add(play);
            buttons.Children.Add(make("\uE893", "next", 38));
            Grid.SetColumn(buttons, 2);
            row.Children.Add(buttons);

            _mediaCard = Card("\uE8D6", "Now playing", row);
            _mediaCard.Visibility = Visibility.Collapsed;
            return _mediaCard;
        }

        private void ApplyMedia(JsonObject media)
        {
            if (media == null) { _mediaCard.Visibility = Visibility.Collapsed; return; }
            _mediaCard.Visibility = Visibility.Visible;
            _mediaTitle.Text = Str(media, "title") ?? "";
            string artist = Str(media, "artist") ?? "";
            _mediaArtist.Text = artist.Length > 0 ? artist : "SquareDeck";
            bool playing = Bool(media, "playing") == true;
            _mediaPlayGlyph.Text = playing ? "\uE769" : "\uE768";
            _mediaArt.Opacity = playing ? 1 : 0.55;
            string key = Str(media, "key") ?? "";
            if (key != _mediaKey)
            {
                _mediaKey = key;
                _mediaArt.Source = null;
                _mediaNoArt.Visibility = Visibility.Visible;
            }
            if (key.Length > 0 && _mediaArtAsked != key)
            {
                _mediaArtAsked = key;
                _link.Send("mediaart", "key", key);
            }
        }

        private async void ApplyMediaArt(JsonObject m)
        {
            string key = Str(m, "key");
            if (key != _mediaKey) return;
            var art = await Decode(Str(m, "art"));
            if (key != _mediaKey || art == null) return;
            _mediaArt.Source = art;
            _mediaNoArt.Visibility = Visibility.Collapsed;
        }

        /// <summary>The game's pictures, sent once by SquareDeck as base64 text.</summary>
        private async void ApplyArt(JsonObject art)
        {
            string key = Str(art, "key");
            if (key != _gameKey) return;
            var icon = await Decode(Str(art, "icon"));
            var hero = await Decode(Str(art, "hero"));
            if (key != _gameKey) return;
            if (icon != null) _gameIcon.Source = icon;
            if (hero != null) _gameHero.Source = hero;
            if (hero != null && _background == "game") _backArt.Source = hero;
        }

        // ---------------------------------------------------------------- the background (Settings > Widget background)

        private void ApplyBackground(string style)
        {
            _background = style;
            Brush brush;
            if (style == "glass")
                brush = new AcrylicBrush
                {
                    BackgroundSource = AcrylicBackgroundSource.HostBackdrop, TintColor = Color.FromArgb(255, 0x12, 0x15, 0x19), TintOpacity = 0.5,
                    FallbackColor = Color.FromArgb(0xB4, 0x12, 0x15, 0x19)
                };
            else if (style == "oled") brush = new SolidColorBrush(Color.FromArgb(0xFA, 0, 0, 0));
            else if (style == "accent")
                brush = Gradient(Color.FromArgb(0xF2, (byte)(_accent.R * 0.34), (byte)(_accent.G * 0.34), (byte)(_accent.B * 0.34)),
                                 Color.FromArgb(0xF2, (byte)(_accent.R * 0.16), (byte)(_accent.G * 0.16), (byte)(_accent.B * 0.16)), Color.FromArgb(0xF2, 0x0E, 0x10, 0x14));
            else if (style == "aurora") brush = Gradient(Color.FromArgb(0xF2, 0x0A, 0x3A, 0x3C), Color.FromArgb(0xF2, 0x14, 0x1A, 0x34), Color.FromArgb(0xF2, 0x2C, 0x10, 0x48));
            else if (style == "sunset") brush = Gradient(Color.FromArgb(0xF2, 0x3C, 0x0E, 0x2E), Color.FromArgb(0xF2, 0x2A, 0x10, 0x22), Color.FromArgb(0xF2, 0x42, 0x24, 0x08));
            else brush = Gradient(Color.FromArgb(0xF0, 0x12, 0x15, 0x19), Color.FromArgb(0xF0, 0x17, 0x1C, 0x23), Color.FromArgb(0xF0, 0x1A, 0x1A, 0x26));
            Root.Background = brush;
            bool art = style == "game";
            _backArt.Opacity = art ? 0.3 : 0;
            if (art && _backArt.Source == null) _backArt.Source = _gameHero.Source;
            if (!art) _backArt.Source = null;
        }

        private static LinearGradientBrush Gradient(Color a, Color b, Color c)
        {
            var g = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(1, 1) };
            g.GradientStops.Add(new GradientStop { Color = a, Offset = 0 });
            g.GradientStops.Add(new GradientStop { Color = b, Offset = 0.6 });
            g.GradientStops.Add(new GradientStop { Color = c, Offset = 1 });
            return g;
        }

        private static async System.Threading.Tasks.Task<BitmapImage> Decode(string base64)
        {
            if (string.IsNullOrEmpty(base64)) return null;
            try
            {
                byte[] bytes = Convert.FromBase64String(base64);
                using (var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream())
                {
                    using (var writer = new Windows.Storage.Streams.DataWriter(stream.GetOutputStreamAt(0)))
                    {
                        writer.WriteBytes(bytes);
                        await writer.StoreAsync();
                        await writer.FlushAsync();
                        writer.DetachStream();
                    }
                    stream.Seek(0);
                    var image = new BitmapImage();
                    await image.SetSourceAsync(stream);
                    return image;
                }
            }
            catch (Exception) { return null; }
        }

        // ---------------------------------------------------------------- the highlight color

        private void SetAccent(string hex)
        {
            Color c;
            if (!TryParseColor(hex, out c) || c == _accent) return;
            _accent = c;
            _accentBrush.Color = c;
            if (Application.Current.Resources["DeckTealBrush"] is SolidColorBrush shared) shared.Color = c;
            _glowTop.Color = Color.FromArgb(0x00, c.R, c.G, c.B);
            _glowBottom.Color = Color.FromArgb(0x38, c.R, c.G, c.B);
            _notePill.Background = new SolidColorBrush(Color.FromArgb(0xF0, (byte)(c.R * 0.45), (byte)(c.G * 0.45), (byte)(c.B * 0.45)));
            _dial.SetAccent(c);
            if (_background == "accent") ApplyBackground("accent");
            AccentLive(c);
            MarkPresets(_watts);
        }

        private static bool TryParseColor(string hex, out Color color)
        {
            color = Teal;
            if (string.IsNullOrEmpty(hex) || hex.Length != 7 || hex[0] != '#') return false;
            try
            {
                color = Color.FromArgb(255, Convert.ToByte(hex.Substring(1, 2), 16), Convert.ToByte(hex.Substring(3, 2), 16), Convert.ToByte(hex.Substring(5, 2), 16));
                return true;
            }
            catch (Exception) { return false; }
        }

        // ---------------------------------------------------------------- Power

        private static readonly (string Name, string Glyph)[] PresetLooks =
        {
            ("Quiet", ""), ("Balanced", ""), ("Performance", ""), ("Max", ""),
        };

        private FrameworkElement BuildPower()
        {
            var body = new StackPanel();
            _powerBody = new Grid();
            _powerBody.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _powerBody.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _powerBody.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _minus = RoundButton("", "Less power");
            _minus.Click += (s, e) => SetTdp(_watts - 1);
            _powerBody.Children.Add(_minus);
            _dial = new PowerDial { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) };
            Grid.SetColumn(_dial, 1);
            _powerBody.Children.Add(_dial);
            _plus = RoundButton("", "More power");
            _plus.Click += (s, e) => SetTdp(_watts + 1);
            Grid.SetColumn(_plus, 2);
            _powerBody.Children.Add(_plus);
            body.Children.Add(_powerBody);

            _powerHelp = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = new SolidColorBrush(Subtle), Margin = new Thickness(0, 4, 0, 10), Visibility = Visibility.Collapsed, TextAlignment = TextAlignment.Center };
            body.Children.Add(_powerHelp);

            // Four steps across the chip's range, like Command Center's operating modes.
            var presets = Columns(4);
            presets.Margin = new Thickness(0, 6, 0, 0);
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                var face = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                face.Children.Add(Glyph(PresetLooks[i].Glyph, 20));
                _presetWatts[i] = new TextBlock { FontSize = 12, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0), Foreground = _accentBrush };
                face.Children.Add(_presetWatts[i]);
                face.Children.Add(new TextBlock { Text = PresetLooks[i].Name, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Foreground = new SolidColorBrush(Subtle), TextTrimming = TextTrimming.CharacterEllipsis });
                var b = new Button { Style = DeckButton, Content = face, Height = 78, Padding = new Thickness(4, 8, 4, 8) };
                b.Click += (s, e) => { if (_presetValues[index] > 0) SetTdp(_presetValues[index]); };
                Grid.SetColumn(b, i);
                presets.Children.Add(b);
                _presets[i] = b;
            }
            body.Children.Add(presets);

            var foot = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            _powerInfo = new TextBlock { FontSize = 11, Foreground = new SolidColorBrush(Subtle), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 0, 120, 0) };
            foot.Children.Add(_powerInfo);
            _reset = new Button { Style = DeckButton, HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(12, 6, 12, 7), CornerRadius = new CornerRadius(14), Visibility = Visibility.Collapsed };
            var resetRow = new StackPanel { Orientation = Orientation.Horizontal };
            resetRow.Children.Add(new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 7, 0) });
            resetRow.Children.Add(new TextBlock { Text = "Back to profile", FontSize = 12, FontWeight = FontWeights.SemiBold });
            _reset.Content = resetRow;
            ToolTipService.SetToolTip(_reset, "Drop this limit: back to the game's profile, or your usual one");
            _reset.Click += (s, e) => _link.Send("tdpreset");
            foot.Children.Add(_reset);
            body.Children.Add(foot);
            return Card("", "Power", body);
        }

        private Button RoundButton(string glyph, string tip)
        {
            var b = new Button { Style = DeckButton, Width = 48, Height = 48, CornerRadius = new CornerRadius(24), Padding = new Thickness(0), Content = Glyph(glyph, 16), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            ToolTipService.SetToolTip(b, tip);
            return b;
        }

        private void SetTdp(int watts)
        {
            if (!_tdpReady) return;
            watts = Math.Max(_min, Math.Min(_max, watts));
            _watts = watts;
            _userTdp = DateTime.Now;
            _dial.SetWatts(watts);
            MarkPresets(watts);
            _link.Send("tdp", "watts", watts);
        }

        private void MarkPresets(int watts)
        {
            for (int i = 0; i < 4; i++)
            {
                bool on = _presetValues[i] == watts;
                _presets[i].Background = new SolidColorBrush(on ? Color.FromArgb(0x55, _accent.R, _accent.G, _accent.B) : Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
                _presets[i].BorderBrush = new SolidColorBrush(on ? _accent : Colors.Transparent);
            }
        }

        // ---------------------------------------------------------------- Live

        // BuildLive and the Live tiles are in WidgetPage.Live.cs.

        // ---------------------------------------------------------------- ROG Ally

        private FrameworkElement BuildAlly()
        {
            var body = new StackPanel();
            body.Children.Add(Label("Operating mode"));
            var modes = Columns(3);
            int i = 0;
            foreach (var (id, name, glyph) in new[] { ("silent", "Silent", ""), ("performance", "Performance", ""), ("turbo", "Turbo", "") })
            {
                var b = Segment(name, glyph);
                string mode = id;
                b.Click += (s, e) => _link.Send("asusmode", "id", mode);
                Grid.SetColumn(b, i++);
                modes.Children.Add(b);
                _modes[id] = b;
            }
            body.Children.Add(modes);

            body.Children.Add(Label("Fans"));
            var fans = Columns(4);
            i = 0;
            foreach (var (id, name) in new[] { ("quiet", "Quiet"), ("balanced", "Balanced"), ("cool", "Cool"), ("max", "Max") })
            {
                var b = Segment(name, null);
                string fan = id;
                b.Click += (s, e) => _link.Send("fan", "id", fan);
                Grid.SetColumn(b, i++);
                fans.Children.Add(b);
                _fans[id] = b;
            }
            body.Children.Add(fans);

            var toggles = Columns(2);
            toggles.Margin = new Thickness(0, 12, 0, 0);
            _lights = Segment("Lights", "");
            _lights.Click += (s, e) => _link.Send("action", "id", "lights");
            toggles.Children.Add(_lights);
            _mouse = Segment("Controller", "");
            _mouse.Click += (s, e) => _link.Send("action", "id", "padmouse");
            Grid.SetColumn(_mouse, 1);
            toggles.Children.Add(_mouse);
            body.Children.Add(toggles);

            _allyCard = Card("", "ROG Ally", body); // the fan speed is in the Live card
            _allyCard.Visibility = Visibility.Collapsed;
            return _allyCard;
        }

        private static TextBlock Label(string text) => new TextBlock { Text = text, FontSize = 12, Foreground = new SolidColorBrush(Subtle), Margin = new Thickness(2, 8, 0, 6) };

        private Button Segment(string name, string glyph)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            if (glyph != null) row.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 8, 0) });
            row.Children.Add(new TextBlock { Text = name, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            return new Button { Style = DeckButton, Content = row, Height = 42, CornerRadius = new CornerRadius(12), Padding = new Thickness(6, 4, 6, 4) };
        }

        private void Mark(Button b, bool on)
        {
            b.Background = new SolidColorBrush(on ? Color.FromArgb(0x55, _accent.R, _accent.G, _accent.B) : Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
            b.BorderBrush = new SolidColorBrush(on ? _accent : Colors.Transparent);
        }

        // ---------------------------------------------------------------- Quick

        private static readonly (string Id, string Glyph, string Name)[] QuickTiles =
        {
            ("perf", "", "Overlay"), ("refresh", "", "Refresh rate"), ("nightlight", "", "Night light"),
            ("hdr", "", "HDR"), ("dnd", "", "Do not disturb"), ("playpause", "", "Play / pause"),
            ("wifi", "", "Wi-Fi"), ("bluetooth", "", "Bluetooth"), ("keyboard", "", "Keyboard"),
            ("taskmanager", "", "Task Manager"), ("amdoverlay", "", "AMD overlay"), ("screenoff", "", "Screen off"),
            ("mic", "\uE720", "Microphone"), ("desktop", "\uE7F4", "Show desktop"), ("amdmetrics", "\uE9D9", "AMD metrics"),
            ("cpuboost", "\uE945", "CPU boost"), ("ris", "\uE91B", "Image sharpening"), ("chill", "\uE9CA", "Radeon Chill"),
            ("ledlevel", "\uEA80", "LED brightness"), ("airplane", "\uE709", "Airplane mode"),
        };

        private List<string> _quickShown;      // the tiles chosen, in order (null: all of them)
        private bool _quickEditing;
        private Grid _quickGrid;
        private Button _quickEdit;
        private TextBlock _quickHint;
        private readonly Dictionary<string, TextBlock> _quickBadges = new Dictionary<string, TextBlock>();

        /// <summary>The quick settings: the chosen tiles, and Edit to add or take off any of them (saved in SquareDeck).</summary>
        private FrameworkElement BuildQuick()
        {
            _quickGrid = Columns(3);
            foreach (var t in QuickTiles)
            {
                var face = new Grid();
                var stackFace = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var g = Glyph(t.Glyph, 20);
                stackFace.Children.Add(g);
                var label = new TextBlock { Text = t.Name, FontSize = 12, Margin = new Thickness(0, 7, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxLines = 2 };
                stackFace.Children.Add(label);
                face.Children.Add(stackFace);
                var badge = new TextBlock { FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -4, -2, 0), Foreground = _accentBrush, Visibility = Visibility.Collapsed };
                face.Children.Add(badge);
                var b = new Button { Style = DeckButton, Content = face, Height = 84, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
                string tile = t.Id;
                b.Click += (s2, e) => { if (_quickEditing) ToggleQuick(tile); else QuickPressed(tile); };
                _quick[t.Id] = (b, g, label, t.Name);
                _quickBadges[t.Id] = badge;
            }
            _quickEdit = new Button { Style = DeckButton, Content = new TextBlock { Text = "Edit", FontSize = 12, FontWeight = FontWeights.SemiBold }, Padding = new Thickness(12, 3, 12, 4), CornerRadius = new CornerRadius(10) };
            _quickEdit.Click += (s2, e) => { _quickEditing = !_quickEditing; LayoutQuick(); };
            var body = new StackPanel();
            _quickHint = new TextBlock { FontSize = 12, Foreground = new SolidColorBrush(Subtle), Margin = new Thickness(2, 0, 0, 10), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
            body.Children.Add(_quickHint);
            body.Children.Add(_quickGrid);
            LayoutQuick();
            return Card("", "Quick", body, _quickEdit);
        }

        /// <summary>Lays out the chosen tiles; while editing, the ones left out follow, dimmed, ready to add.</summary>
        private void LayoutQuick()
        {
            _quickGrid.Children.Clear();
            _quickGrid.RowDefinitions.Clear();
            var shown = _quickShown ?? QuickTiles.Select(t => t.Id).ToList();
            var order = shown.Where(id => _quick.ContainsKey(id)).ToList();
            if (_quickEditing) order.AddRange(QuickTiles.Select(t => t.Id).Where(id => !order.Contains(id)));
            for (int i = 0; i < order.Count; i++)
            {
                if (i % 3 == 0) _quickGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var b = _quick[order[i]].Button;
                bool on = shown.Contains(order[i]);
                b.Opacity = _quickEditing && !on ? 0.45 : 1;
                if (_quickEditing) b.IsEnabled = true;
                var badge = _quickBadges[order[i]];
                badge.Visibility = _quickEditing ? Visibility.Visible : Visibility.Collapsed;
                badge.Text = on ? "\uE738" : "\uE710"; // take off / add
                Grid.SetColumn(b, i % 3);
                Grid.SetRow(b, i / 3);
                _quickGrid.Children.Add(b);
            }
            ((TextBlock)_quickEdit.Content).Text = _quickEditing ? "Done" : "Edit";
            _quickHint.Text = _quickEditing ? "Select a tile to take it off, or a dimmed one to add it. Done when you're finished."
                : "No quick settings. Press Edit to add some.";
            _quickHint.Visibility = _quickEditing || order.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ToggleQuick(string id)
        {
            var shown = new List<string>(_quickShown ?? QuickTiles.Select(t => t.Id).ToList());
            if (!shown.Remove(id)) shown.Add(id);
            _quickShown = shown;
            LayoutQuick();
            _quick[id].Button.Focus(FocusState.Keyboard);
            var ids = new JsonArray();
            foreach (var x in shown) ids.Add(JsonValue.CreateStringValue(x));
            var message = new JsonObject();
            message["op"] = JsonValue.CreateStringValue("quicktiles");
            message["ids"] = ids;
            _link.Send(message);
        }

        private void QuickPressed(string id)
        {
            // Wi-Fi and Bluetooth open their panel: the switch, then networks to join or devices to connect and pair.
            if (id == "wifi" || id == "bluetooth") { OpenSheet(id); return; }
            if (id == "refresh" && _rates.Length > 1)
            {
                int next = _rates.FirstOrDefault(r => r > _hz);
                if (next == 0) next = _rates[0];
                _link.Send("refresh", "hz", next);
                return;
            }
            _link.Send("action", "id", id);
        }

        private void MarkQuick(string id, bool? on, string label = null)
        {
            (Button Button, TextBlock Glyph, TextBlock Label, string Name) t;
            if (!_quick.TryGetValue(id, out t)) return;
            t.Button.IsEnabled = _quickEditing || on != null || (id != "nightlight" && id != "hdr" && id != "cpuboost" && id != "ris" && id != "chill" && id != "ledlevel");
            Mark(t.Button, on == true);
            t.Glyph.Foreground = new SolidColorBrush(on == true ? _accent : Colors.White);
            t.Label.Text = label ?? t.Name;
        }

        // ---------------------------------------------------------------- not connected

        private FrameworkElement BuildOffline()
        {
            _offline = new Grid { Background = new SolidColorBrush(Color.FromArgb(0xF4, 0x10, 0x13, 0x18)), Visibility = Visibility.Collapsed };
            Grid.SetRowSpan(_offline, 2);
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 340, Margin = new Thickness(24) };
            var logo = new Image { Source = new BitmapImage(new Uri("ms-appx:///Assets/Logo.png")), Width = 84, Height = 84, RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5), RenderTransform = new ScaleTransform() };
            stack.Children.Add(logo);
            stack.Children.Add(new TextBlock { Text = "SquareDeck isn't running", FontSize = 20, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 22, 0, 8), Foreground = new SolidColorBrush(Colors.White) });
            stack.Children.Add(new TextBlock
            {
                Text = "The widget asks SquareDeck to change the TDP and the rest, so it needs SquareDeck open (it can sit in the background).",
                FontSize = 13, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Foreground = new SolidColorBrush(Subtle)
            });
            var start = new Button { Style = DeckButton, Margin = new Thickness(0, 22, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(26, 11, 26, 12), CornerRadius = new CornerRadius(22), Background = _accentBrush, Foreground = new SolidColorBrush(Color.FromArgb(255, 0x0B, 0x16, 0x1A)) };
            var startRow = new StackPanel { Orientation = Orientation.Horizontal };
            startRow.Children.Add(new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 10, 0) });
            startRow.Children.Add(new TextBlock { Text = "Start SquareDeck", FontSize = 15, FontWeight = FontWeights.SemiBold });
            start.Content = startRow;
            _startHelp = new TextBlock
            {
                FontSize = 12, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 14, 0, 0),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 0xFF, 0xC8, 0x6B)), Visibility = Visibility.Collapsed,
                Text = "Windows doesn't know SquareDeck's link yet. Open SquareDeck yourself once (version 7.3.19 or newer): it registers the link, and this button works from then on."
            };
            start.Click += async (s, e) =>
            {
                var link = new Uri("squaredeck:background");
                // Ask first, so Windows never offers to search the Microsoft Store for an app.
                bool known = false;
                try { known = await Windows.System.Launcher.QueryUriSupportAsync(link, Windows.System.LaunchQuerySupportType.Uri) == Windows.System.LaunchQuerySupportStatus.Available; }
                catch (Exception) { }
                _startHelp.Visibility = known ? Visibility.Collapsed : Visibility.Visible;
                if (!known) return;
                try { await Windows.System.Launcher.LaunchUriAsync(link); } catch (Exception) { }
                _link.Retry();
            };
            stack.Children.Add(start);
            stack.Children.Add(_startHelp);
            var waiting = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 22, 0, 0) };
            waiting.Children.Add(new ProgressRing { IsActive = true, Width = 16, Height = 16, Foreground = _accentBrush, Margin = new Thickness(0, 0, 10, 0) });
            waiting.Children.Add(new TextBlock { Text = "Looking for SquareDeck…", FontSize = 12, Foreground = new SolidColorBrush(Subtle), VerticalAlignment = VerticalAlignment.Center });
            stack.Children.Add(waiting);
            stack.Children.Add(new TextBlock
            {
                Text = "Already open? Check Settings > Controller > Xbox Game Bar widget in SquareDeck.",
                FontSize = 11, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Foreground = new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)), Margin = new Thickness(0, 14, 0, 0)
            });
            _offline.Children.Add(stack);

            // The logo breathes while it waits.
            _pulse = new Storyboard { RepeatBehavior = RepeatBehavior.Forever, AutoReverse = true };
            foreach (var axis in new[] { "ScaleX", "ScaleY" })
            {
                var a = new DoubleAnimation { From = 1, To = 1.08, Duration = new Duration(TimeSpan.FromSeconds(1.2)), EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
                Storyboard.SetTarget(a, logo);
                Storyboard.SetTargetProperty(a, "(UIElement.RenderTransform).(ScaleTransform." + axis + ")");
                _pulse.Children.Add(a);
            }
            return _offline;
        }

        private void ShowConnected(bool on)
        {
            _offline.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            _dot.Fill = new SolidColorBrush(on ? Color.FromArgb(255, 0x3D, 0xDC, 0x84) : Color.FromArgb(255, 0x8A, 0x8F, 0x98));
            _status.Text = on ? "Live" : "Offline";
            if (on) { _pulse.Stop(); }
            else
            {
                _pulse.Begin();
                foreach (var s in _stats.Values) s.Line.Clear();
                _dial.SetEmpty("Not connected");
            }
        }

        // ============================================================ what SquareDeck sends

        private void OnMessage(JsonObject m)
        {
            string type = Str(m, "type");
            if (type == "note") { ShowNote(Str(m, "text")); return; }
            if (type == "state") { Apply(m); ApplyEight(m); }
            else if (type == "art") ApplyArt(m);
            else if (type == "achievements" || type == "genie" || type == "apps" || type == "theme" || type == "companion") ApplyEightMessage(type, m);
            else if (type == "avatar") ApplyAvatar(m);
            else if (type == "mediaart") ApplyMediaArt(m);
            else if (type == "wifi") ApplyWifi(m);
            else if (type == "bluetooth") ApplyBluetooth(m);
        }

        private Image _headerLogo;
        private Ellipse _headerAvatar;
        private TextBlock _headerName;
        private string _avatarAsked;

        /// <summary>Your profile picture from SquareDeck, in a circle in the header.</summary>
        private async void ApplyAvatar(JsonObject m)
        {
            var image = await Decode(Str(m, "image"));
            if (image == null) return;
            _headerAvatar.Fill = new ImageBrush { ImageSource = image, Stretch = Stretch.UniformToFill };
            _headerAvatar.Visibility = Visibility.Visible;
            _headerLogo.Visibility = Visibility.Collapsed;
        }

        private void ShowNote(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            _note.Text = text;
            _notePill.Visibility = Visibility.Visible;
            _noteTimer.Stop();
            _noteTimer.Start();
        }

        private void Apply(JsonObject s)
        {
            if (_offline.Visibility == Visibility.Visible) ShowConnected(true);

            // The highlight color and which cards show.
            SetAccent(Str(s, "accent"));
            var sections = Arr(s, "sections");
            if (sections != null)
                _sections = new HashSet<string>(sections.Where(v => v.ValueType == JsonValueType.String).Select(v => v.GetString()));
            _liveCard.Visibility = _sections.Contains("live") ? Visibility.Visible : Visibility.Collapsed;
            _quickCard.Visibility = _sections.Contains("quick") ? Visibility.Visible : Visibility.Collapsed;

            // Header: the device, the battery and how long it lasts at this rate.
            var game = Obj(s, "game");
            string device = Str(s, "device");
            _subtitle.Text = device ?? "SquareDeck";
            string user = Str(s, "user");
            _headerName.Text = string.IsNullOrWhiteSpace(user) ? "SquareDeck" : user;
            string avatar = Str(s, "avatar");
            if (!string.IsNullOrEmpty(avatar) && avatar != _avatarAsked)
            {
                _avatarAsked = avatar;
                _link.Send("avatar");
            }
            double? battery = Num(s, "battery"), left = Num(s, "left");
            bool plugged = Bool(s, "plugged") == true;
            string time = left == null || left.Value <= 0 ? "" : "  " + (left.Value >= 60 ? Math.Floor(left.Value / 60) + "h " + (left.Value % 60).ToString("00") + "m" : left.Value.ToString("0") + "m");
            _battery.Text = battery == null ? "" : (plugged ? "\u26A1 " : "") + battery.Value.ToString("0") + "%" + time;

            ApplyPower(Obj(s, "tdp"));
            ApplyGame(game);

            // The rail (left alone while you're moving it, so it doesn't jump back).
            _syncing = true;
            double? volume = Num(s, "volume");
            if (volume != null && (DateTime.Now - _userVolume).TotalSeconds > 1.5) _volume.Value = volume.Value;
            bool muted = Bool(s, "muted") == true;
            _muteGlyph.Text = muted ? "" : "";
            double? bright = Num(s, "brightness");
            _bright.IsEnabled = bright != null;
            if (bright != null && (DateTime.Now - _userBright).TotalSeconds > 1.5) _bright.Value = bright.Value;
            _syncing = false;

            // Live numbers: one point on each line per second or so.
            bool sample = (DateTime.Now - _lastSample).TotalSeconds > 0.8;
            if (sample) _lastSample = DateTime.Now;
            UpdateLive(s, sample);

            // The ROG Ally.
            var ally = Obj(s, "ally");
            _allyCard.Visibility = ally != null && _sections.Contains("ally") ? Visibility.Visible : Visibility.Collapsed;
            if (ally != null)
            {
                bool asus = Bool(ally, "asus") == true;
                string mode = Str(ally, "mode") ?? "", fan = Str(ally, "fan") ?? "";
                foreach (var kv in _modes) { kv.Value.IsEnabled = asus; Mark(kv.Value, kv.Key == mode); }
                foreach (var kv in _fans) { kv.Value.IsEnabled = asus; Mark(kv.Value, kv.Key == fan); }
                bool lights = Bool(ally, "lights") == true, mouse = Bool(ally, "mouse") == true;
                Mark(_lights, lights);
                SetSegmentText(_lights, lights ? "Lights on" : "Lights off");
                Mark(_mouse, mouse);
                SetSegmentText(_mouse, mouse ? "Mouse mode" : "Controller");
            }

            // Quick toggles.
            var toggles = Obj(s, "toggles");
            if (toggles != null)
            {
                MarkQuick("perf", Bool(toggles, "perf"));
                MarkQuick("nightlight", Bool(toggles, "nightlight"));
                bool? hdr = Bool(toggles, "hdr");
                MarkQuick("hdr", hdr, hdr == null ? "No HDR" : null);
                MarkQuick("dnd", Bool(toggles, "dnd"));
                MarkQuick("cpuboost", Bool(toggles, "cpuboost"));
                bool? ris = Bool(toggles, "ris"), chill = Bool(toggles, "chill");
                MarkQuick("ris", ris, ris == null ? "No sharpening" : null);
                MarkQuick("chill", chill, chill == null ? "No Chill" : null);
                MarkQuick("wifi", Bool(toggles, "wifi"));
                MarkQuick("bluetooth", Bool(toggles, "bluetooth"));
                MarkQuick("airplane", Bool(toggles, "airplane"));
                double? led = Num(toggles, "ledlevel");
                MarkQuick("ledlevel", led == null ? (bool?)null : led.Value > 0, led == null ? "No LEDs" : led.Value > 0 ? "LEDs " + led.Value.ToString("0") + "%" : "LEDs off");
            }
            var quickIds = Arr(s, "quick");
            if (quickIds != null && !_quickEditing)
            {
                var list = quickIds.Where(v => v.ValueType == JsonValueType.String).Select(v => v.GetString()).ToList();
                if (_quickShown == null || !list.SequenceEqual(_quickShown)) { _quickShown = list; LayoutQuick(); }
            }
            ApplyMedia(Obj(s, "media"));
            string background = Str(s, "background") ?? "squaredeck";
            if (background != _background) ApplyBackground(background);
            double? hz = Num(s, "refresh");
            if (hz != null) _hz = (int)hz.Value;
            var rates = Arr(s, "rates");
            if (rates != null) _rates = rates.Select(v => (int)v.GetNumber()).ToArray();
            MarkQuick("refresh", false, _hz > 0 ? _hz + " Hz" : null);

            if (!_focusedOnce) { _focusedOnce = true; _presets[1].Focus(FocusState.Keyboard); }
        }

        private void ApplyPower(JsonObject tdp)
        {
            bool ready = tdp != null && Bool(tdp, "ready") == true && Num(tdp, "max") != null;
            _tdpReady = ready;
            _minus.IsEnabled = _plus.IsEnabled = ready;
            foreach (var p in _presets) p.IsEnabled = ready;
            if (!ready)
            {
                _dial.SetEmpty("Not set up");
                bool canApply = tdp != null && Bool(tdp, "canApply") == true;
                _powerHelp.Text = canApply
                    ? "TDP control is off. Turn it on in SquareDeck's settings, on your handheld's page."
                    : "TDP control isn't set up yet. SquareDeck sets it up in its settings, on your handheld's page (one Windows permission prompt).";
                _powerHelp.Visibility = Visibility.Visible;
                _powerInfo.Text = "";
                _reset.Visibility = Visibility.Collapsed;
                return;
            }
            _powerHelp.Visibility = Visibility.Collapsed;
            _min = (int)Num(tdp, "min").GetValueOrDefault(5);
            _max = (int)Num(tdp, "max").GetValueOrDefault(30);
            _dial.SetRange(_min, _max);
            var presets = Arr(tdp, "presets");
            if (presets != null && presets.Count == 4)
                for (int i = 0; i < 4; i++) { _presetValues[i] = (int)presets[i].GetNumber(); _presetWatts[i].Text = _presetValues[i] + " W"; }
            // While you're pressing minus / plus, what you chose stays on the dial until SquareDeck catches up.
            if ((DateTime.Now - _userTdp).TotalSeconds > 1.5)
            {
                _watts = (int)Num(tdp, "watts").GetValueOrDefault(_watts);
                _dial.SetWatts(_watts);
                MarkPresets(_watts);
            }
            bool session = Bool(tdp, "session") == true, profile = Bool(tdp, "profile") == true;
            _reset.Visibility = session ? Visibility.Visible : Visibility.Collapsed;
            string method = Str(tdp, "method"), chip = Str(tdp, "chip");
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(chip)) parts.Add(chip);
            if (!string.IsNullOrEmpty(method)) parts.Add("via " + method);
            if (!session && profile) parts.Add("game profile");
            _powerInfo.Text = string.Join("  ·  ", parts);
        }

        private static void SetSegmentText(Button b, string text)
        {
            var row = b.Content as StackPanel;
            if (row != null && row.Children.Count > 0 && row.Children[row.Children.Count - 1] is TextBlock t) t.Text = text;
        }

        // ---------------------------------------------------------------- reading the JSON safely

        private static IJsonValue Get(JsonObject o, string key)
        {
            IJsonValue v;
            return o != null && o.TryGetValue(key, out v) && v.ValueType != JsonValueType.Null ? v : null;
        }

        private static string Str(JsonObject o, string key) { var v = Get(o, key); return v != null && v.ValueType == JsonValueType.String ? v.GetString() : null; }
        private static double? Num(JsonObject o, string key) { var v = Get(o, key); return v != null && v.ValueType == JsonValueType.Number ? v.GetNumber() : (double?)null; }
        private static bool? Bool(JsonObject o, string key) { var v = Get(o, key); return v != null && v.ValueType == JsonValueType.Boolean ? v.GetBoolean() : (bool?)null; }
        private static JsonObject Obj(JsonObject o, string key) { var v = Get(o, key); return v != null && v.ValueType == JsonValueType.Object ? v.GetObject() : null; }
        private static JsonArray Arr(JsonObject o, string key) { var v = Get(o, key); return v != null && v.ValueType == JsonValueType.Array ? v.GetArray() : null; }

        /// <summary>Sends a slider's value at most every 90 ms while it moves, and always the last one.</summary>
        private sealed class Throttle
        {
            private readonly Action<double> _send;
            private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
            private double _pending;
            private bool _has;

            public Throttle(Action<double> send)
            {
                _send = send;
                _timer.Tick += (s, e) =>
                {
                    if (_has) { _has = false; _send(Math.Round(_pending)); }
                    else _timer.Stop();
                };
            }

            public void Push(double value)
            {
                if (!_timer.IsEnabled) { _send(Math.Round(value)); _timer.Start(); return; }
                _pending = value;
                _has = true;
            }
        }
    }
}
