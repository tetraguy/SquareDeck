using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using SquareDeck.Services;
using Windows.Devices.Sensors;

namespace SquareDeck;

/// <summary>
/// Experimental "Continue where you left off parallax": on Home, while the "Continue where you left off" game is
/// there, moving the handheld moves its artwork in depth. The art slides and leans one way, the logo and name drift
/// the other at a shallower depth, and in the big-artwork Home layout the artwork behind the page moves too.
///
/// It reads the handheld's motion sensors: the inclinometer (the gyro and accelerometer together) when Windows has
/// one, else the gyro on its own, else the accelerometer. Whatever angle you hold it at becomes the middle after a
/// moment, so it reacts to moving the handheld, not to how you hold it.
/// </summary>
public partial class MainWindow
{
    private readonly DispatcherTimer _plxTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private Inclinometer? _plxIncl;
    private Gyrometer? _plxGyro;
    private Accelerometer? _plxAccel;
    private bool _plxHooked;
    // The latest reading, as degrees of turn left / right (X) and forward / back (Y).
    private double _plxRawX, _plxRawY;
    private double _plxBaseX, _plxBaseY, _plxX, _plxY;
    private bool _plxHasBase;
    private DateTimeOffset _plxGyroAt;
    private FrameworkElement? _plxArt, _plxFace, _plxClipped;
    private bool _plxHeroMoved;

    public static bool ContinueParallaxOn => Labs.On("plxcontinue");

    private void SetContinueParallax(bool on)
    {
        if (on)
        {
            if (!_plxHooked)
            {
                _plxTimer.Tick -= ParallaxTick;
                _plxTimer.Tick += ParallaxTick;
                try
                {
                    _plxIncl = Inclinometer.GetDefault();
                    if (_plxIncl != null)
                    {
                        _plxIncl.ReportInterval = Math.Max(_plxIncl.MinimumReportInterval, 16u);
                        _plxIncl.ReadingChanged += OnPlxIncline;
                    }
                    else if ((_plxGyro = Gyrometer.GetDefault()) != null)
                    {
                        _plxGyro.ReportInterval = Math.Max(_plxGyro.MinimumReportInterval, 16u);
                        _plxGyro.ReadingChanged += OnPlxGyro;
                    }
                    else if ((_plxAccel = Accelerometer.GetDefault()) != null)
                    {
                        _plxAccel.ReportInterval = Math.Max(_plxAccel.MinimumReportInterval, 16u);
                        _plxAccel.ReadingChanged += OnPlxAccel;
                    }
                }
                catch (Exception) { }
                _plxHooked = true;
            }
            _plxHasBase = false;
            _plxTimer.Start();
        }
        else
        {
            _plxTimer.Stop();
            try
            {
                if (_plxIncl != null) _plxIncl.ReadingChanged -= OnPlxIncline;
                if (_plxGyro != null) _plxGyro.ReadingChanged -= OnPlxGyro;
                if (_plxAccel != null) _plxAccel.ReadingChanged -= OnPlxAccel;
            }
            catch (Exception) { }
            _plxIncl = null;
            _plxGyro = null;
            _plxAccel = null;
            _plxHooked = false;
            ResetParallax();
        }
    }

    /// <summary>Whether a motion sensor was found (for the experiment's note in Settings).</summary>
    public bool HasParallaxSensor => _plxIncl != null || _plxGyro != null || _plxAccel != null;

    private void OnPlxIncline(Inclinometer sender, InclinometerReadingChangedEventArgs e)
    {
        _plxRawX = e.Reading.RollDegrees;
        _plxRawY = e.Reading.PitchDegrees;
    }

    private void OnPlxGyro(Gyrometer sender, GyrometerReadingChangedEventArgs e)
    {
        // Turning speed, added up into how far it's turned (and eased back to the middle in the tick).
        var at = e.Reading.Timestamp;
        double dt = _plxGyroAt == default ? 0 : Math.Clamp((at - _plxGyroAt).TotalSeconds, 0, 0.1);
        _plxGyroAt = at;
        _plxRawX += e.Reading.AngularVelocityY * dt;
        _plxRawY += e.Reading.AngularVelocityX * dt;
    }

    private void OnPlxAccel(Accelerometer sender, AccelerometerReadingChangedEventArgs e)
    {
        // Which way gravity pulls, as an angle.
        _plxRawX = Math.Asin(Math.Clamp(e.Reading.AccelerationX, -1, 1)) * 180 / Math.PI;
        _plxRawY = Math.Asin(Math.Clamp(e.Reading.AccelerationY, -1, 1)) * 180 / Math.PI;
    }

    private void ParallaxTick(object? sender, EventArgs e)
    {
        if (_resting) return;
        bool home = _screens.Count == 0 && IsActive && !Theme.ReduceMotion && HasParallaxSensor;

        // How far from the middle: the angle you hold it at settles as the new middle over a second or two.
        double tx = 0, ty = 0;
        if (home)
        {
            if (_plxGyro != null)
            {
                _plxRawX *= 0.985;
                _plxRawY *= 0.985;
                tx = _plxRawX;
                ty = _plxRawY;
            }
            else
            {
                if (!_plxHasBase) { _plxBaseX = _plxRawX; _plxBaseY = _plxRawY; _plxHasBase = true; }
                // Roll wraps at ±180°: take the short way round.
                double dx = Wrap(_plxRawX - _plxBaseX), dy = Wrap(_plxRawY - _plxBaseY);
                _plxBaseX = Wrap(_plxBaseX + dx * 0.012);
                _plxBaseY = Wrap(_plxBaseY + dy * 0.012);
                tx = dx;
                ty = dy;
            }
            double reach = double.TryParse(Labs.Get("plx.reach", "12"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var r) ? r : 12;
            tx = Math.Clamp(tx / reach, -1, 1);
            ty = Math.Clamp(ty / reach, -1, 1);
            if (Labs.Get("plx.invert") == "1") { tx = -tx; ty = -ty; }
        }
        _plxX += (tx - _plxX) * 0.16;
        _plxY += (ty - _plxY) * 0.16;

        // What moves: the "Continue" tile's art and words, and in the big-artwork layout the art behind the page.
        FrameworkElement? art = null, face = null;
        bool hero = false;
        if (home)
        {
            if (_kicker is { Visibility: Visibility.Visible }) hero = true;
            var tile = GameList.Items.OfType<Game>().FirstOrDefault(g => g.IsContinueTile);
            if (tile != null && GameList.ItemContainerGenerator.ContainerFromItem(tile) is FrameworkElement container)
            {
                art = Named(container, "CoverImage");
                face = Named(container, "ContinueFace");
            }
        }
        if (!ReferenceEquals(art, _plxArt) || !ReferenceEquals(face, _plxFace))
        {
            ResetParallaxElements();
            _plxArt = art;
            _plxFace = face;
            // The tile's rounded mask is sized from what's drawn inside it: with the art moved and enlarged, the mask
            // grew with it and the art spilled past the tile. Clipping the tile's inside to its own box keeps the mask
            // (and so the art) exactly the tile's size.
            _plxClipped = art != null ? VisualTreeHelper.GetParent(art) as FrameworkElement : null;
            if (_plxClipped != null) _plxClipped.ClipToBounds = true;
        }

        double x = _plxX, y = _plxY;
        double depth = double.TryParse(Labs.Get("plx.depth", "1"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 1;
        if (art != null)
        {
            // The art: a little bigger than its frame so it can slide, leaning slightly as it goes.
            var group = new TransformGroup();
            // Enough extra size that its edges never show inside the tile, however deep the setting.
            double zoom = 1 + 0.14 * Math.Max(1, depth);
            group.Children.Add(new ScaleTransform(zoom, zoom));
            group.Children.Add(new SkewTransform(x * 1.6 * depth, -y * 1.2 * depth));
            group.Children.Add(new TranslateTransform(-x * 22 * depth, -y * 16 * depth));
            art.RenderTransformOrigin = new Point(0.5, 0.5);
            art.RenderTransform = group;
        }
        if (face != null) face.RenderTransform = new TranslateTransform(x * 8 * depth, y * 6 * depth);
        if (hero && HeroImage != null)
        {
            // The art behind the page is the deepest layer: it moves the most (it has room past the window's edges).
            var group = new TransformGroup();
            group.Children.Add(new ScaleTransform(1.03, 1.03));
            group.Children.Add(new TranslateTransform(-x * 30 * depth, -y * 22 * depth));
            HeroImage.RenderTransformOrigin = new Point(0.5, 0.5);
            HeroImage.RenderTransform = group;
            _plxHeroMoved = true;
        }
        else if (_plxHeroMoved)
        {
            HeroImage.RenderTransform = Transform.Identity;
            _plxHeroMoved = false;
        }
    }

    private static double Wrap(double degrees)
    {
        while (degrees > 180) degrees -= 360;
        while (degrees < -180) degrees += 360;
        return degrees;
    }

    private void ResetParallaxElements()
    {
        if (_plxArt != null) _plxArt.RenderTransform = Transform.Identity;
        if (_plxClipped != null) _plxClipped.ClipToBounds = false;
        _plxClipped = null;
        if (_plxFace != null) _plxFace.RenderTransform = Transform.Identity;
        _plxArt = _plxFace = null;
    }

    private void ResetParallax()
    {
        ResetParallaxElements();
        if (_plxHeroMoved) { HeroImage.RenderTransform = Transform.Identity; _plxHeroMoved = false; }
        _plxX = _plxY = 0;
    }

    /// <summary>The "Continue" tile is moved by the parallax: the tilting covers leave it alone.</summary>
    private bool ParallaxOwns(Game? item) => ContinueParallaxOn && _plxTimer.IsEnabled && item is { IsContinueTile: true };
}
