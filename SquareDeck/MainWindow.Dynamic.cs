using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using SquareDeck.Services;

namespace SquareDeck;

/// <summary>
/// Moving backgrounds (Settings > Appearance > Moving background): a silent looping video behind the home screen
/// and every page, optionally blurred. Pauses while a game is running or SquareDeck isn't in front.
/// </summary>
public partial class MainWindow
{
    /// <summary>The videos that come with SquareDeck, in Backgrounds\, by group.</summary>
    public static readonly (string Group, string[] Names)[] DynamicBackgrounds =
    {
        ("Waves", new[] { "Black Wave", "Blue Wave", "Gold Wave", "Silver Wave" }),
        ("Blades", new[] { "Black Blades", "Green Blades", "Mint Blades", "Blurry Blades", "Fuzzy Blades" }),
        ("Pixel art", new[] { "Lofi City", "City Dawn", "Cyberpunk Rain", "Night River", "Mushroom Forest", "Yellowstone", "Chill Mario",
                              "Ocarina", "Pokee", "Stars and Me", "Witcher" }),
        ("Scenery", new[] { "Aero", "Heaven", "Pasture", "Plague" }),
        ("Vaporwave", new[] { "La Fuente", "Analog" })
    };

    public static readonly (int Radius, string Name)[] DynamicBlurLevels =
        { (0, "None"), (8, "Light"), (20, "Medium"), (40, "Strong"), (70, "Very strong") };

    private MediaElement? _dynamicVideo;
    private string? _dynamicPath;
    private bool _dynamicPaused;

    public static string BackgroundsFolder => Path.Combine(AppContext.BaseDirectory, "Backgrounds");

    /// <summary>Full path of a background: a name from the list, or a video file of your own.</summary>
    public static string? DynamicPathFor(string? setting)
    {
        if (string.IsNullOrWhiteSpace(setting)) return null;
        string path = Path.IsPathRooted(setting) ? setting : Path.Combine(BackgroundsFolder, setting + ".mp4");
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Chooses a moving background ("" for none) and saves it. A moving background replaces the game artwork
    /// behind the home row, so that is switched off when one is chosen (it can be switched back on in Settings).
    /// </summary>
    public void UseDynamicBackground(string name)
    {
        Settings.DynamicBackground = name;
        bool heroWasOn = Settings.HeroBackgrounds;
        if (name.Length > 0) Settings.HeroBackgrounds = false;
        Settings.Save();
        ApplyDynamicBackground();
        if (heroWasOn && !Settings.HeroBackgrounds) ApplySettings(); // clears the artwork now
    }

    /// <summary>Starts, changes or stops the moving background to match the settings, and sets its blur.</summary>
    public void ApplyDynamicBackground()
    {
        // Not while a game runs: it comes back when the game closes (see UpdateGameMemory).
        if (AllGames.Any(g => g.IsRunning)) { StopDynamicBackground(); return; }
        var path = DynamicPathFor(Settings.DynamicBackground);
        if (path == null)
        {
            bool wasOn = _dynamicVideo != null;
            StopDynamicBackground();
            if (wasOn) ApplyWallpaper(); // the still wallpaper (if any) comes back
            return;
        }

        if (_dynamicVideo == null)
        {
            _dynamicVideo = new MediaElement
            {
                LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual, IsMuted = true, Volume = 0,
                Stretch = Stretch.UniformToFill, IsHitTestVisible = false
            };
            RenderOptions.SetBitmapScalingMode(_dynamicVideo, BitmapScalingMode.LowQuality);
            // Loop: back to the start when it ends.
            _dynamicVideo.MediaEnded += (_, _) =>
            {
                if (_dynamicVideo == null) return;
                _dynamicVideo.Position = TimeSpan.Zero;
                if (!_dynamicPaused) _dynamicVideo.Play();
            };
            _dynamicVideo.MediaFailed += (_, _) =>
            {
                StopDynamicBackground();
                // Right after a game the video decoder can still be busy: try again a couple of times first.
                if (_dynamicRetries < 3)
                {
                    _dynamicRetries++;
                    RetryDynamicSoon(TimeSpan.FromSeconds(2 * _dynamicRetries));
                    return;
                }
                _dynamicRetries = 0;
                StatusText.Text = "That moving background couldn't be played";
                ApplyWallpaper();
            };
            _dynamicVideo.MediaOpened += (_, _) => _dynamicRetries = 0;
            DynamicLayer.Children.Add(_dynamicVideo);
        }
        if (!string.Equals(_dynamicPath, path, StringComparison.OrdinalIgnoreCase))
        {
            _dynamicPath = path;
            _dynamicVideo.Source = new Uri(path);
            _dynamicPaused = false;
            _dynamicVideo.Play();
            CheckDynamicStartedSoon();
        }

        // Blur, with the edges pushed off screen so they don't go soft. The video is blurred at half size and then
        // stretched (it's blurry anyway), which is about a quarter of the work. None in Performance mode.
        int blur = Perf.Active ? 0 : Math.Clamp(Settings.DynamicBlur, 0, 100);
        _dynamicVideo.Effect = blur > 0 ? new BlurEffect { Radius = blur / 2.0, RenderingBias = RenderingBias.Performance } : null;
        _dynamicVideo.Margin = new Thickness(0);
        if (blur > 0)
        {
            _dynamicVideo.Width = 640 + blur * 1.5;
            _dynamicVideo.Height = 360 + blur * 1.5;
            _dynamicVideo.LayoutTransform = Transform.Identity;
            _dynamicVideo.RenderTransformOrigin = new Point(0.5, 0.5);
            _dynamicVideo.RenderTransform = new ScaleTransform(2, 2);
            _dynamicVideo.HorizontalAlignment = HorizontalAlignment.Center;
            _dynamicVideo.VerticalAlignment = VerticalAlignment.Center;
        }
        else
        {
            _dynamicVideo.Width = _dynamicVideo.Height = double.NaN;
            _dynamicVideo.RenderTransform = Transform.Identity;
            _dynamicVideo.HorizontalAlignment = HorizontalAlignment.Stretch;
            _dynamicVideo.VerticalAlignment = VerticalAlignment.Stretch;
        }
        DynamicLayer.Visibility = Visibility.Visible;

        // Pages see the same moving picture through a brush that copies this layer as it plays.
        var brush = new VisualBrush(DynamicLayer) { Stretch = Stretch.UniformToFill };
        Theme.CurrentWallpaper = brush;
        WallpaperLayer.Visibility = Visibility.Collapsed;
        WallpaperScrim.Visibility = Visibility.Visible;
        WallpaperScrim.Opacity = UiStyles.Current.Glass ? 0.1 : 0.3;
        UpdateDynamicPlayback();
    }

    private int _dynamicRetries;
    private bool _dynamicRestorePending;
    private DispatcherTimer? _dynamicRetryTimer;

    /// <summary>
    /// After a game: the moving background comes back once SquareDeck is in front and not minimized. Loading the video
    /// while SquareDeck was still behind the game (or minimized) sometimes left it blank for good.
    /// </summary>
    private void RestoreDynamicBackground()
    {
        if (DynamicPathFor(Settings.DynamicBackground) == null) { ApplyDynamicBackground(); return; }
        if (IsActive && WindowState != WindowState.Minimized && IsVisible)
        {
            _dynamicRestorePending = false;
            _dynamicRetries = 0;
            ApplyDynamicBackground();
            return;
        }
        _dynamicRestorePending = true;
        ApplyWallpaper(); // the still wallpaper meanwhile
    }

    private void RetryDynamicSoon(TimeSpan after)
    {
        _dynamicRetryTimer?.Stop();
        _dynamicRetryTimer = new DispatcherTimer { Interval = after };
        _dynamicRetryTimer.Tick += (_, _) =>
        {
            _dynamicRetryTimer?.Stop();
            if (AllGames.Any(g => g.IsRunning)) return; // it comes back after this game instead
            RestoreDynamicBackground();
        };
        _dynamicRetryTimer.Start();
    }

    /// <summary>A few seconds after starting the video: if it never opened (no picture), start it again.</summary>
    private void CheckDynamicStartedSoon()
    {
        var video = _dynamicVideo;
        var check = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        check.Tick += (_, _) =>
        {
            check.Stop();
            if (video == null || !ReferenceEquals(video, _dynamicVideo) || _dynamicPaused) return;
            if (video.NaturalVideoWidth > 0) return; // playing
            if (_dynamicRetries >= 3) return;
            _dynamicRetries++;
            StopDynamicBackground();
            RestoreDynamicBackground();
        };
        check.Start();
    }

    private void StopDynamicBackground()
    {
        if (_dynamicVideo != null)
        {
            _dynamicVideo.Stop();
            _dynamicVideo.Close();
            DynamicLayer.Children.Remove(_dynamicVideo);
            _dynamicVideo = null;
        }
        _dynamicPath = null;
        DynamicLayer.Visibility = Visibility.Collapsed;
    }

    /// <summary>A cloud game is open (even with something drawn over it): music and moving backgrounds stay paused.</summary>
    private bool CloudOpen => _screens.Any(x => x is Screens.CloudGameScreen);

    /// <summary>Checks again whether the menu music and the moving background should be playing.</summary>
    public void RefreshBackgroundPlayback()
    {
        UpdateMenuMusic();
        UpdateDynamicPlayback();
    }

    /// <summary>Plays only while SquareDeck is in front, not minimized, and no game is running.</summary>
    private void UpdateDynamicPlayback()
    {
        if (_dynamicVideo == null) return;
        bool pause = !IsActive || WindowState == WindowState.Minimized || AllGames.Any(g => g.IsRunning) || CloudOpen;
        if (pause == _dynamicPaused) return;
        _dynamicPaused = pause;
        if (pause) _dynamicVideo.Pause();
        else _dynamicVideo.Play();
    }

    private void InitDynamicBackground()
    {
        Activated += (_, _) => { if (_dynamicRestorePending) RestoreDynamicBackground(); UpdateDynamicPlayback(); };
        Deactivated += (_, _) => UpdateDynamicPlayback();
        StateChanged += (_, _) => { if (_dynamicRestorePending) RestoreDynamicBackground(); UpdateDynamicPlayback(); };
        ApplyDynamicBackground();
    }
}
