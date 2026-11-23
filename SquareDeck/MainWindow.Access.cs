using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SquareDeck.Services;

namespace SquareDeck;

// Accessibility: "Flash the screen for alerts" (Settings > Accessibility > Hearing). Notifications, errors and the
// low-battery alert also glow around the edge of the screen, for people who can't hear the sounds.
public partial class MainWindow
{
    private Border? _alertFlash;

    private void FlashForAlert(string kind)
    {
        if (!Settings.VisualSounds) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (_alertFlash == null)
            {
                _alertFlash = new Border { BorderThickness = new Thickness(14), IsHitTestVisible = false, Opacity = 0 };
                Panel.SetZIndex(_alertFlash, 900);
                AddOverlay(_alertFlash);
            }
            var color = kind is "error" or "battery" ? Color.FromRgb(0xFF, 0x3B, 0x30)
                      : (Application.Current.Resources["AccentBrush"] as SolidColorBrush)?.Color ?? Colors.White;
            _alertFlash.BorderBrush = new SolidColorBrush(color);
            // Two quick glows, gentle with Reduce motion (one, slower).
            var flash = new DoubleAnimationUsingKeyFrames();
            if (Theme.ReduceMotion)
            {
                flash.KeyFrames.Add(new LinearDoubleKeyFrame(0.9, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(250))));
                flash.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1100))));
            }
            else
            {
                flash.KeyFrames.Add(new LinearDoubleKeyFrame(0.95, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120))));
                flash.KeyFrames.Add(new LinearDoubleKeyFrame(0.2, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(320))));
                flash.KeyFrames.Add(new LinearDoubleKeyFrame(0.95, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(460))));
                flash.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(900))));
            }
            _alertFlash.BeginAnimation(OpacityProperty, flash);
        });
    }
}
