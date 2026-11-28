using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// 9.0.18: a handheld's user manual, read inside SquareDeck. Since 9.0.19 the PDF comes from ASUS's manual page for the
/// model (Device Info > User manual), downloaded once into SquareDeck's data folder; nothing is shipped. It's drawn page by page with Windows' own PDF reader, a few pages at a time, so a 60-page manual opens at
/// once and uses little memory.
/// - Y turns the whole page sideways (right, then left, then back), for reading with the handheld held upright like a
///   book. The buttons turn with it: whichever way the handheld is held, up still scrolls up.
/// - Up and down scroll, left and right (or LB and RB) go a page back or forward, A goes forward too, and the right
///   stick scrolls smoothly.
/// - LT and RT zoom out and in, and X sets the zoom back to fit.
/// - Touch and the mouse wheel scroll too. The way it was turned is kept for next time.
/// </summary>
public sealed class ManualScreen : Screen
{
    private sealed class PageSlot
    {
        public required Border Holder;
        public required Image Image;
        public double Aspect;
        public double RenderedWidth;
        public bool Rendering;
    }

    private const double Gap = 18, TopPad = 14;
    private static readonly double[] Zooms = { 0.5, 0.65, 0.8, 1, 1.25, 1.5, 1.75, 2 };

    private readonly string _path;
    private readonly Grid _frame = new();
    private readonly RotateTransform _rotate = new();
    private readonly ScrollViewer _scroll;
    private readonly StackPanel _pages = new() { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, TopPad, 0, 40) };
    private readonly TextBlock _pageLabel = Ui.Text("", 18, "SubtleBrush");
    private readonly TextBlock _turnLabel = Ui.Text("", 18, "SubtleBrush");
    private readonly TextBlock _status = Ui.Text("Opening the manual…", 20, "SubtleBrush");
    private readonly HintBar _hints = new("");
    private readonly List<PageSlot> _slots = new();
    private readonly SemaphoreSlim _renderGate = new(1, 1);
    private Windows.Data.Pdf.PdfDocument? _doc;
    private int _zoomStep = 3;
    private int _rotation;
    private bool _closed, _scheduled;

    public ManualScreen(string title, string path)
    {
        _path = path;
        Switch2Settings.Refresh();
        Switch2Settings.ApplyColors(this);
        _rotation = Host_Rotation();

        var root = new Grid { Background = new SolidColorBrush(Switch2Settings.Background), ClipToBounds = true };
        _frame.LayoutTransform = _rotate;
        _frame.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _frame.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _frame.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // The title bar: the book and its name on the left, the page and which way it's turned on the right.
        var header = new DockPanel { Margin = new Thickness(40, 22, 40, 12), LastChildFill = true };
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _turnLabel.Margin = new Thickness(0, 0, 26, 0);
        right.Children.Add(_turnLabel);
        right.Children.Add(_pageLabel);
        DockPanel.SetDock(right, Dock.Right);
        header.Children.Add(right);
        var name = new StackPanel { Orientation = Orientation.Horizontal };
        var book = Ui.Glyph("\uE736", 30, "FgBrush");
        book.VerticalAlignment = VerticalAlignment.Center;
        book.Margin = new Thickness(0, 2, 14, 0);
        name.Children.Add(book);
        var text = Ui.Text(title, 28, "FgBrush");
        text.VerticalAlignment = VerticalAlignment.Center;
        text.TextTrimming = TextTrimming.CharacterEllipsis;
        name.Children.Add(text);
        header.Children.Add(name);
        Grid.SetRow(header, 0);
        _frame.Children.Add(header);
        var line = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(40, 0, 40, 0), Background = new SolidColorBrush(Switch2Settings.Line) };
        Grid.SetRow(line, 0);
        _frame.Children.Add(line);

        _scroll = new ScrollViewer
        {
            Content = _pages, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            PanningMode = PanningMode.Both, Focusable = false, IsDeferredScrollingEnabled = false,
        };
        _scroll.ScrollChanged += (_, _) => Schedule();
        _scroll.SizeChanged += (_, e) => { if (Math.Abs(e.PreviousSize.Width - e.NewSize.Width) > 1) Relayout(); };
        Grid.SetRow(_scroll, 1);
        _frame.Children.Add(_scroll);
        _status.HorizontalAlignment = HorizontalAlignment.Center;
        _status.VerticalAlignment = VerticalAlignment.Center;
        _status.TextWrapping = TextWrapping.Wrap;
        _status.TextAlignment = TextAlignment.Center;
        _status.Margin = new Thickness(60);
        Grid.SetRow(_status, 1);
        _frame.Children.Add(_status);

        var foot = new Border { BorderThickness = new Thickness(0, 1, 0, 0), BorderBrush = new SolidColorBrush(Switch2Settings.Line), Margin = new Thickness(40, 0, 40, 0), Padding = new Thickness(0, 12, 0, 14) };
        foot.Child = _hints;
        Grid.SetRow(foot, 2);
        _frame.Children.Add(foot);

        root.Children.Add(_frame);
        Content = root;
        ApplyRotation(keepPlace: false);
    }

    private static int Host_Rotation() => Application.Current?.MainWindow is MainWindow w ? Normalize(w.Settings.ManualRotation) : 0;

    private static int Normalize(int r) => r is 90 or 270 ? r : 0;

    public override void OnShown()
    {
        base.OnShown();
        if (_doc == null) _ = OpenAsync();
    }

    public override void OnClosed()
    {
        _closed = true;
        foreach (var slot in _slots) slot.Image.Source = null;
        base.OnClosed();
    }

    private async Task OpenAsync()
    {
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(_path);
            _doc = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);
            if (_closed) return;
            for (uint i = 0; i < _doc.PageCount; i++)
            {
                double aspect = 1.414;
                using (var page = _doc.GetPage(i))
                    if (page.Size.Width > 0) aspect = page.Size.Height / page.Size.Width;
                var image = new Image { Stretch = Stretch.Uniform };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                var holder = new Border
                {
                    Background = Brushes.White, Margin = new Thickness(0, 0, 0, Gap), Child = image, SnapsToDevicePixels = true,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.35 },
                };
                _slots.Add(new PageSlot { Holder = holder, Image = image, Aspect = aspect });
                _pages.Children.Add(holder);
            }
            _status.Visibility = _slots.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            if (_slots.Count == 0) _status.Text = "This manual has no pages.";
            Relayout();
        }
        catch (Exception ex)
        {
            _status.Text = "The manual couldn't be opened.\n" + ex.Message;
            _status.Visibility = Visibility.Visible;
        }
    }

    // ---------------------------------------------------------------- layout

    /// <summary>Fits the pages to the width there is (after turning), at the chosen zoom, staying on the same spot.</summary>
    private void Relayout(double? keepPage = null)
    {
        if (_slots.Count == 0) return;
        double place = keepPage ?? Place();
        double view = _scroll.ViewportWidth > 0 ? _scroll.ViewportWidth : Math.Max(200, _scroll.ActualWidth);
        double fit = Math.Max(240, view - 56);
        // Sideways the page fills the width; held normally it stays a comfortable reading width.
        if (_rotation == 0) fit = Math.Min(fit, 1000);
        double width = Math.Round(fit * Zooms[_zoomStep]);
        foreach (var slot in _slots)
        {
            slot.Holder.Width = width;
            slot.Holder.Height = Math.Round(width * slot.Aspect);
        }
        _pages.UpdateLayout();
        _scroll.UpdateLayout();
        GoTo(place);
        Schedule();
    }

    private double PageTop(int index)
    {
        double top = TopPad;
        for (int i = 0; i < index && i < _slots.Count; i++) top += _slots[i].Holder.Height + Gap;
        return top;
    }

    /// <summary>Where the reader is, as a page number with a fraction (2.5 is halfway down the third page).</summary>
    private double Place()
    {
        if (_slots.Count == 0) return 0;
        double y = _scroll.VerticalOffset, top = TopPad;
        for (int i = 0; i < _slots.Count; i++)
        {
            double h = _slots[i].Holder.Height + Gap;
            if (y < top + h) return i + Math.Clamp((y - top) / Math.Max(1, h), 0, 1);
            top += h;
        }
        return _slots.Count - 1;
    }

    private void GoTo(double place)
    {
        int index = Math.Clamp((int)Math.Floor(place), 0, Math.Max(0, _slots.Count - 1));
        double frac = place - index;
        double h = _slots.Count > 0 ? _slots[index].Holder.Height + Gap : 0;
        _scroll.ScrollToVerticalOffset(PageTop(index) + frac * h);
    }

    private int CurrentPage()
    {
        if (_slots.Count == 0) return 0;
        double middle = _scroll.VerticalOffset + _scroll.ViewportHeight * 0.4, top = TopPad;
        for (int i = 0; i < _slots.Count; i++)
        {
            top += _slots[i].Holder.Height + Gap;
            if (middle < top) return i;
        }
        return _slots.Count - 1;
    }

    // ---------------------------------------------------------------- drawing the pages

    /// <summary>Soon after scrolling: draws the pages in view (and one either side), and lets go of ones far away.</summary>
    private void Schedule()
    {
        if (_slots.Count > 0) _pageLabel.Text = $"Page {CurrentPage() + 1} of {_slots.Count}";
        if (_scheduled || _closed) return;
        _scheduled = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _scheduled = false;
            if (_closed || _slots.Count == 0) return;
            double top = _scroll.VerticalOffset, bottom = top + _scroll.ViewportHeight;
            int first = -1, last = -1;
            double y = TopPad;
            for (int i = 0; i < _slots.Count; i++)
            {
                double h = _slots[i].Holder.Height;
                if (y + h >= top && y <= bottom) { if (first < 0) first = i; last = i; }
                y += h + Gap;
            }
            if (first < 0) first = last = CurrentPage();
            int current = CurrentPage();
            var order = Enumerable.Range(Math.Max(0, first - 1), Math.Min(_slots.Count, last + 2) - Math.Max(0, first - 1))
                                  .OrderBy(i => Math.Abs(i - current));
            foreach (int i in order) _ = RenderAsync(i);
            for (int i = 0; i < _slots.Count; i++)
                if ((i < first - 4 || i > last + 4) && _slots[i].Image.Source != null) { _slots[i].Image.Source = null; _slots[i].RenderedWidth = 0; }
        });
    }

    private async Task RenderAsync(int index)
    {
        var slot = _slots[index];
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double want = Math.Min(2200, Math.Round(slot.Holder.Width * dpi));
        if (slot.Rendering || (slot.Image.Source != null && Math.Abs(slot.RenderedWidth - want) < want * 0.15)) return;
        slot.Rendering = true;
        await _renderGate.WaitAsync();
        try
        {
            if (_closed || _doc == null) return;
            byte[] png;
            using (var page = _doc.GetPage((uint)index))
            using (var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream())
            {
                await page.RenderToStreamAsync(ms, new Windows.Data.Pdf.PdfPageRenderOptions { DestinationWidth = (uint)want });
                ms.Seek(0);
                using var src = System.IO.WindowsRuntimeStreamExtensions.AsStream(ms);
                using var copy = new System.IO.MemoryStream();
                await src.CopyToAsync(copy);
                png = copy.ToArray();
            }
            var bitmap = await Task.Run(() =>
            {
                var b = new BitmapImage();
                b.BeginInit();
                b.CacheOption = BitmapCacheOption.OnLoad;
                b.StreamSource = new System.IO.MemoryStream(png);
                b.EndInit();
                b.Freeze();
                return b;
            });
            if (_closed) return;
            slot.Image.Source = bitmap;
            slot.RenderedWidth = want;
        }
        catch (Exception) { }
        finally
        {
            slot.Rendering = false;
            _renderGate.Release();
        }
    }

    // ---------------------------------------------------------------- turning and the controls

    private void ApplyRotation(bool keepPlace = true)
    {
        double place = Place();
        _rotate.Angle = _rotation;
        _turnLabel.Text = _rotation switch { 90 => "Turned right", 270 => "Turned left", _ => "" };
        _turnLabel.Visibility = _rotation == 0 ? Visibility.Collapsed : Visibility.Visible;
        _hints.Template = _rotation == 0
            ? "▲▼  Scroll      ◀▶ {LB}{RB}  Page      {LT}{RT}  Zoom      {Details}  Fit      {Options}  Turn sideways      {Back}  Back"
            : "Scroll and turn pages the way you're holding it      {LT}{RT}  Zoom      {Details}  Fit      {Options}  Turn      {Back}  Back";
        if (keepPlace) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => Relayout(place));
    }

    private void Turn()
    {
        _rotation = _rotation switch { 0 => 90, 90 => 270, _ => 0 };
        if (Application.Current?.MainWindow is MainWindow w) { w.Settings.ManualRotation = _rotation; w.Settings.Save(); }
        ApplyRotation();
    }

    private void Zoom(int step)
    {
        int next = Math.Clamp(_zoomStep + step, 0, Zooms.Length - 1);
        if (next == _zoomStep) return;
        double place = Place();
        _zoomStep = next;
        Relayout(place);
        Host.ShowToast("ZOOM", Math.Round(Zooms[_zoomStep] * 100) + "%", "", "", "info");
    }

    private void ScrollBy(double dy) => _scroll.ScrollToVerticalOffset(Math.Clamp(_scroll.VerticalOffset + dy, 0, _scroll.ScrollableHeight));

    private void PageBy(int delta)
    {
        int page = Math.Clamp(CurrentPage() + delta, 0, Math.Max(0, _slots.Count - 1));
        _scroll.ScrollToVerticalOffset(PageTop(page) - 6);
    }

    private enum Move { None, Up, Down, Back, Forward }

    /// <summary>The d-pad as the reader sees it: turned with the page, so up is always up.</summary>
    private Move Map(NavAction a) => _rotation switch
    {
        90 => a switch { NavAction.Right => Move.Up, NavAction.Left => Move.Down, NavAction.Up => Move.Back, NavAction.Down => Move.Forward, _ => Move.None },
        270 => a switch { NavAction.Left => Move.Up, NavAction.Right => Move.Down, NavAction.Down => Move.Back, NavAction.Up => Move.Forward, _ => Move.None },
        _ => a switch { NavAction.Up => Move.Up, NavAction.Down => Move.Down, NavAction.Left => Move.Back, NavAction.Right => Move.Forward, _ => Move.None },
    };

    public override bool OnAction(NavAction action)
    {
        switch (Map(action))
        {
            case Move.Up: ScrollBy(-140); return true;
            case Move.Down: ScrollBy(140); return true;
            case Move.Back: PageBy(-1); return true;
            case Move.Forward: PageBy(1); return true;
        }
        switch (action)
        {
            case NavAction.LB: PageBy(-1); return true;
            case NavAction.RB: case NavAction.Accept: PageBy(1); return true;
            case NavAction.LT: Zoom(-1); return true;
            case NavAction.RT: Zoom(1); return true;
            case NavAction.X:
                if (_zoomStep != 3) { double place = Place(); _zoomStep = 3; Relayout(place); }
                return true;
            case NavAction.Y: UiSound.Move(); Turn(); return true;
            case NavAction.Back: UiSound.Back(); Host.Pop(this); return true;
        }
        return true;
    }

    public override bool OnRightStick(double x, double y, bool pressed)
    {
        // Up on the stick, as the reader holds it.
        double up = _rotation switch { 90 => x, 270 => -x, _ => y };
        if (Math.Abs(up) < 0.15) return false;
        ScrollBy(-up * 46);
        return true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Keyboard: Page Up / Page Down, Home and End, and R to turn.
        switch (e.Key)
        {
            case Key.PageUp: PageBy(-1); e.Handled = true; return;
            case Key.PageDown: PageBy(1); e.Handled = true; return;
            case Key.Home: _scroll.ScrollToTop(); e.Handled = true; return;
            case Key.End: _scroll.ScrollToBottom(); e.Handled = true; return;
            case Key.R: Turn(); e.Handled = true; return;
        }
        base.OnKeyDown(e);
    }
}
