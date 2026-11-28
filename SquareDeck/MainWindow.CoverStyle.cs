using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SquareDeck.Services;

namespace SquareDeck;

/// <summary>Experimental "Cover styles" on the home row: each tile's cut, pose and frame (see CoverStyles).</summary>
public partial class MainWindow
{
    private bool _coverStyleHooked;
    private readonly HashSet<FrameworkElement> _coverStyleSized = new();

    /// <summary>Applies (or takes away) the cover styles on every tile of the home row.</summary>
    public void ApplyCoverStyle()
    {
        if (!_coverStyleHooked)
        {
            _coverStyleHooked = true;
            GameList.ItemContainerGenerator.StatusChanged += (_, _) =>
            {
                if (GameList.ItemContainerGenerator.Status == System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated)
                    Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(ApplyCoverStyle));
            };
        }
        int selected = GameList.SelectedIndex;
        for (int i = 0; i < GameList.Items.Count; i++)
            if (GameList.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem container) StyleTile(container, i, selected);
    }

    /// <summary>Just the poses (they follow the highlighted cover when fanned, leaning in or straightened).</summary>
    private void UpdateCoverPoses()
    {
        if (CoverStyles.Home is not { } look || look.Pose == "flat") return;
        int selected = GameList.SelectedIndex;
        for (int i = 0; i < GameList.Items.Count; i++)
            if (GameList.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem container && container.Template?.FindName("TileBody", container) is FrameworkElement body)
                SetPose(body, CoverStyles.PoseFor(look, i, selected));
    }

    private void StyleTile(ListBoxItem container, int index, int selected)
    {
        container.ApplyTemplate();
        if (container.Template?.FindName("TileBody", container) is not FrameworkElement body
            || container.Template.FindName("TileFace", container) is not Border face
            || container.Template.FindName("CoverFrameHost", container) is not Grid host) return;
        var ring = container.Template.FindName("Ring", container) as Border;
        var look = CoverStyles.Home;
        // The continue tile (wide artwork with words over it) and the action tiles keep their own look.
        bool skip = container.DataContext is Game g && (g.ShowContinueFace || g.IsActionTile);
        if (look == null || look.Plain || skip)
        {
            body.RenderTransform = Transform.Identity;
            face.ClearValue(UIElement.ClipProperty);
            host.Children.Clear();
            host.Effect = null;
            if (ring != null) ring.Visibility = Visibility.Visible;
            StyleAppIcon(container, null);
            return;
        }
        void Shape()
        {
            double w = face.ActualWidth, h = face.ActualHeight;
            if (w <= 0 || h <= 0) return;
            var outline = CoverStyles.Outline(look.Shape, w, h, Theme.TileCorner);
            if (outline != null) face.Clip = outline; else face.ClearValue(UIElement.ClipProperty);
            CoverStyles.Decorate(host, look, w, h, Theme.TileCorner, ring);
            if (ring != null) ring.Visibility = look.Shape == "style" ? Visibility.Visible : Visibility.Hidden;
        }
        Shape();
        if (_coverStyleSized.Add(face)) face.SizeChanged += (_, _) => { if (CoverStyles.On) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => StyleTile(container, GameList.ItemContainerGenerator.IndexFromContainer(container), GameList.SelectedIndex))); };
        SetPose(body, CoverStyles.PoseFor(look, index, selected));
        StyleAppIcon(container, look);
    }

    private static void SetPose(FrameworkElement body, Transform? pose)
    {
        body.RenderTransform = pose ?? Transform.Identity;
    }

    /// <summary>The apps' icons on the home row take the chosen icon shape.</summary>
    private static void StyleAppIcon(ListBoxItem container, CoverStyles.Look? look)
    {
        if (container.Template?.FindName("IconImage", container) is not Image icon) return;
        icon.ClearValue(UIElement.ClipProperty);
        icon.RenderTransform = Transform.Identity;
        icon.ClearValue(UIElement.EffectProperty);
        if (look == null || look.Icon == "style" || container.DataContext is not Game { UsesIcon: true, IconFills: false }) return;
        double size = icon.ActualWidth > 0 ? icon.ActualWidth : icon.Width;
        if (double.IsNaN(size) || size <= 0) return;
        CoverStyles.StyleIcon(icon, size, size, look.Icon);
    }

    /// <summary>The running game's little icon beside the clock takes the icon shape too.</summary>
    private void StyleRunningIcon()
    {
        RunningIcon.ClearValue(UIElement.ClipProperty);
        RunningIcon.RenderTransform = Transform.Identity;
        RunningIcon.ClearValue(UIElement.EffectProperty);
        RunningIcon.ClearValue(Border.BorderThicknessProperty);
        RunningIcon.CornerRadius = new CornerRadius(6);
        if (CoverStyles.Home is { Icon: not "style" } look) CoverStyles.StyleIcon(RunningIcon, RunningIcon.Width, RunningIcon.Height, look.Icon);
    }
}

public partial class MainWindow
{
    // Experimental "SquareDeck EQ": checks the output device (and the volume, for loudness) every few seconds while on.
    private readonly DispatcherTimer _eqTimer = new() { Interval = TimeSpan.FromSeconds(3) };

    private void UpdateEq()
    {
        if (SquareEq.On) { _eqTimer.Start(); SquareEq.Tick(); }
        else _eqTimer.Stop();
        SquareEq.Apply(); // on: the preset; off: an empty file, so Equalizer APO plays the sound as it is
    }
}
