using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// Settings > Appearance > Windows lock screen: sets the picture Windows shows on its lock screen. The wallpapers that
/// come with SquareDeck are offered as tiles (and your own SquareDeck wallpaper, if you picked one), or "Pick my own"
/// for any picture. The chosen picture is copied into SquareDeck's data folder first, so it keeps working if the
/// original moves.
/// </summary>
public sealed class LockScreenScreen : GridScreen
{
    private const double W = 272, H = 153;
    private readonly TextBlock _status = Ui.Text("", 16, "SubtleBrush");

    public LockScreenScreen() : base("Windows lock screen", "{Select}  Use this      {Back}  Back") { }

    public override void OnShown() => Build();

    private void Build()
    {
        Body.Children.Clear();
        Tiles.Children.Clear();
        var note = Ui.Text("The picture Windows shows when the device is locked. Choose one of SquareDeck's wallpapers or one of your own. " +
                           "Windows Spotlight (if it's on) is replaced.", 16, "SubtleBrush");
        note.TextWrapping = TextWrapping.Wrap;
        note.Margin = new Thickness(4, 0, 0, 8);
        Body.Children.Add(note);
        _status.Margin = new Thickness(4, 0, 0, 12);
        _status.TextWrapping = TextWrapping.Wrap;
        Body.Children.Add(_status);

        var row = new WrapPanel();
        AddTile(GlyphFace("", "Pick my own"), "Pick my own…", W, () => _ = PickAsync(), into: row);
        // Your SquareDeck wallpaper, when it's a picture of your own.
        var mine = Host.Settings.Wallpaper;
        if (mine.Length > 0 && !mine.StartsWith("image:") && File.Exists(mine))
            AddTile(Face(mine), "Your SquareDeck wallpaper", W, () => _ = ApplyAsync(mine), into: row);
        Body.Children.Add(row);

        Body.Children.Add(Ui.Header("SquareDeck wallpapers"));
        var wallpapers = new WrapPanel();
        Button? first = null;
        foreach (var (name, file) in Theme.PictureWallpapers)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Wallpapers", file);
            if (!File.Exists(path)) continue;
            var tile = AddTile(Face(path), name, W, () => _ = ApplyAsync(path), into: wallpapers);
            first ??= tile;
        }
        Body.Children.Add(wallpapers);
        FocusLater(first ?? row.Children.OfType<Button>().FirstOrDefault());
    }

    private static Grid Face(string path)
    {
        var grid = new Grid { Width = W, Height = H, ClipToBounds = true, Clip = new RectangleGeometry(new Rect(0, 0, W, H), 12, 12) };
        var back = new Border();
        back.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        grid.Children.Add(back);
        var image = new Image { Stretch = Stretch.UniformToFill };
        grid.Children.Add(image);
        _ = Task.Run(() => ImageLoader.FromFile(path, 480)).ContinueWith(t => image.Source = t.Result, TaskScheduler.FromCurrentSynchronizationContext());
        return grid;
    }

    private static Grid GlyphFace(string glyph, string label)
    {
        var grid = new Grid { Width = W, Height = H, Clip = new RectangleGeometry(new Rect(0, 0, W, H), 12, 12) };
        var back = new Border();
        back.SetResourceReference(Border.BackgroundProperty, "TileBrush");
        grid.Children.Add(back);
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var icon = Ui.Glyph(glyph, 34, "AccentBrush");
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(icon);
        var text = Ui.Text(label, 15, "SubtleBrush");
        text.Margin = new Thickness(0, 8, 0, 0);
        text.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(text);
        grid.Children.Add(stack);
        return grid;
    }

    private async Task PickAsync()
    {
        var picked = await Host.PickFileAsync("Choose a picture for the lock screen", new[] { ".jpg", ".jpeg", ".png", ".bmp" });
        if (picked != null) await ApplyAsync(picked);
    }

    /// <summary>
    /// Sets the lock screen: first the normal way (as Settings > Personalization does); if Windows refuses, through the
    /// lock screen policy, which asks for administrator permission once.
    /// </summary>
    private async Task ApplyAsync(string source)
    {
        _status.Text = "Setting the lock screen…";
        string? copy = null;
        try
        {
            var folder = Path.Combine(Settings.DataDir, "lockscreen");
            Directory.CreateDirectory(folder);
            foreach (var old in Directory.GetFiles(folder)) { try { File.Delete(old); } catch (Exception) { } }
            copy = Path.Combine(folder, "lockscreen-" + DateTime.Now.ToString("yyyyMMddHHmmss") + Path.GetExtension(source));
            File.Copy(source, copy, true);
        }
        catch (Exception ex)
        {
            _status.Text = "Couldn't copy the picture: " + ex.Message;
            return;
        }

        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(copy);
            await Windows.System.UserProfile.LockScreen.SetImageFileAsync(file);
            _status.Text = "Done. Lock the device (Windows + L) to see it.";
            UiSound.Select();
            return;
        }
        catch (Exception) { }

        // Some editions and managed PCs only allow it through the policy setting.
        if (await Host.AskAsync("Windows needs permission", "Windows didn't let SquareDeck change the lock screen directly. It can be set through the lock screen policy instead, which needs administrator permission once.",
                "Allow", "Cancel") != 0)
        {
            _status.Text = "The lock screen wasn't changed.";
            return;
        }
        const string key = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\PersonalizationCSP";
        string commands = $"reg add \"{key}\" /v LockScreenImagePath /t REG_SZ /d \"{copy}\" /f & " +
                          $"reg add \"{key}\" /v LockScreenImageUrl /t REG_SZ /d \"{copy}\" /f & " +
                          $"reg add \"{key}\" /v LockScreenImageStatus /t REG_DWORD /d 1 /f";
        try
        {
            using var p = Process.Start(new ProcessStartInfo("cmd.exe", "/c " + commands) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
            if (p != null) await p.WaitForExitAsync();
            _status.Text = p?.ExitCode == 0 ? "Done. Lock the device (Windows + L) to see it." : "Windows didn't accept the change.";
        }
        catch (Exception)
        {
            _status.Text = "The lock screen wasn't changed (permission was declined).";
        }
    }
}
