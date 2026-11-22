using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// Recomp Zone (experimental): native PC versions of console games, rebuilt from their code by fans. Installed
/// ones first, then every recomp grouped by console. Y searches, X refreshes the list.
/// </summary>
public sealed class RecompZoneScreen : GridScreen
{
    private string _search = "";
    private bool _loaded;
    private string? _error;

    public RecompZoneScreen() : base("Recomp Zone", "{Select}  Open        {Options}  Search        {Details}  Refresh        {Back}  Back") { }

    public override async void OnShown()
    {
        Build();
        await LoadAsync(false);
    }

    public override void OnResumed() => Build();

    private async Task LoadAsync(bool force)
    {
        _error = null;
        try { await RecompZone.LoadAsync(force); }
        catch (Exception ex) { _error = ex.Message; }
        _loaded = true;
        Build();
    }

    public override bool OnAction(NavAction action)
    {
        switch (action)
        {
            case NavAction.Y: _ = SearchAsync(); return true;
            case NavAction.X: _loaded = false; Build(); _ = LoadAsync(true); return true;
        }
        return false;
    }

    private async Task SearchAsync()
    {
        var text = await Host.PromptAsync("Search Recomp Zone", "A game, a recomp's name, a console or a developer.", _search, "Search");
        if (text == null) return;
        _search = text.Trim();
        Build();
    }

    private void Note(string text, double size = 16)
    {
        var t = Ui.Text(text, size, "SubtleBrush");
        t.TextWrapping = TextWrapping.Wrap;
        t.Margin = new Thickness(4, 0, 0, 14);
        Body.Children.Add(t);
    }

    private void Heading(string text)
    {
        var h = Ui.Text(text, 20, "FgBrush", bold: true);
        h.Margin = new Thickness(4, 10, 0, 10);
        Body.Children.Add(h);
    }

    private void Build()
    {
        Body.Children.Clear();
        if (!_loaded) { Note("Loading the list of recomps…", 18); return; }
        if (_error != null && RecompZone.Games.Count == 0) { Note(_error, 18); return; }
        Note("Native PC versions of console games, rebuilt from the original code by their developers. Each one needs your own copy of the game: " +
             "SquareDeck never downloads game files." + (RecompZone.Offline ? "  (Offline: showing the list from last time.)" : ""));
        var installed = RecompZone.Installed().Where(i => File.Exists(i.Program)).Select(i => i.Id).ToHashSet();
        var games = RecompZone.Games.Where(g => !g.Soon).ToList();
        if (_search.Length > 0)
        {
            games = games.Where(g => $"{g.Name} {g.Game} {g.ConsoleName} {g.Developer} {string.Join(' ', g.Tags)}".Contains(_search, StringComparison.OrdinalIgnoreCase)).ToList();
            Note($"Matching \"{_search}\": {games.Count}. Press {Hints.KeyLabel("Options")} to search again.");
        }
        Button? first = null;
        void Section(string title, IEnumerable<RecompGame> list)
        {
            var items = list.ToList();
            if (items.Count == 0) return;
            Heading(title);
            var panel = new WrapPanel();
            Body.Children.Add(panel);
            foreach (var game in items)
            {
                var g = game;
                var box = ImageBox(150, 225, out var image);
                if (installed.Contains(g.Id))
                {
                    var badge = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Child = Ui.Text("Installed", 12, "BgBrush", bold: true) };
                    badge.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                    ((Grid)box.Child).Children.Add(badge);
                }
                if (g.Cover.Length == 0)
                {
                    var name = Ui.Text(g.Game.Length > 0 ? g.Game : g.Name, 16, "FgBrush", bold: true);
                    name.TextWrapping = TextWrapping.Wrap;
                    name.TextAlignment = TextAlignment.Center;
                    name.VerticalAlignment = VerticalAlignment.Center;
                    name.Margin = new Thickness(10);
                    ((Grid)box.Child).Children.Add(name);
                }
                else _ = LoadInto(image, g.Cover, 300);
                var tile = AddTile(box, g.Name, 150, () => Host.Push(new RecompGameScreen(g)), g.Id, panel);
                first ??= tile;
            }
        }
        Section("Installed", games.Where(g => installed.Contains(g.Id)));
        foreach (var group in games.GroupBy(g => g.ConsoleName).OrderByDescending(x => x.Count()).ThenBy(x => x.Key))
            Section(group.Key, group.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase));
        if (first == null) Note(_search.Length > 0 ? "Nothing matches." : "The list is empty right now.");
        else FocusLater(first);
    }

    internal static async Task LoadInto(Image image, string url, int width)
    {
        try { image.Source = await ImageLoader.FromUrlAsync(url, width); } catch (Exception) { }
    }
}

/// <summary>One recomp: what it is, which version is available, and Install / Play / Update / Add to Home / Uninstall.</summary>
public sealed class RecompGameScreen : ListScreen
{
    private readonly RecompGame _game;
    private RecompRelease? _release;
    private bool _releaseChecked;
    private string? _releaseError;
    private CancellationTokenSource? _install;
    private readonly TextBlock _progressText = Ui.Text("", 15, "SubtleBrush");
    private readonly Border _progressFill = new() { Height = 8, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(4), Width = 0 };

    public RecompGameScreen(RecompGame game) : base(game.Name)
    {
        _game = game;
        _progressFill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
    }

    public override async void OnShown()
    {
        base.OnShown();
        if (!_game.CanInstall || !_game.ForWindows) { _releaseChecked = true; Rebuild(); return; }
        try { _release = await RecompZone.ReleaseAsync(_game); }
        catch (Exception ex) { _releaseError = ex.Message.Contains("403") ? "GitHub is limiting requests right now. Try again in a few minutes." : "Couldn't check GitHub for its download: " + ex.Message; }
        _releaseChecked = true;
        Rebuild();
    }

    public override void OnClosed() => _install?.Cancel();

    protected override void Build()
    {
        // The banner: the game's backdrop (or cover), with its logo over it when there is one.
        string art = _game.Hero.Length > 0 ? _game.Hero : _game.Cover;
        if (art.Length > 0)
        {
            var banner = new Grid { Height = 250, Margin = new Thickness(0, 0, 0, 12) };
            var image = new Image { Stretch = Stretch.UniformToFill };
            banner.Children.Add(image);
            banner.Clip = new RectangleGeometry(new Rect(0, 0, 940, 250), 16, 16);
            _ = RecompZoneScreen.LoadInto(image, art, 960);
            if (_game.Logo.Length > 0)
            {
                var logo = new Image { MaxHeight = 120, MaxWidth = 380, Margin = new Thickness(28), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom };
                _ = RecompZoneScreen.LoadInto(logo, _game.Logo, 480);
                banner.Children.Add(logo);
            }
            Rows.Children.Add(banner);
        }
        string line = string.Join("   ·   ", new[] { _game.Game.Length > 0 && _game.Game != _game.Name ? "Based on " + _game.Game : "", _game.ConsoleName, _game.Developer.Length > 0 ? "By " + _game.Developer : "" }.Where(s => s.Length > 0));
        var facts = Ui.Text(line, 16, "SubtleBrush");
        facts.TextWrapping = TextWrapping.Wrap;
        facts.Margin = new Thickness(4, 0, 0, 8);
        Rows.Children.Add(facts);
        string about = _game.Blurb.Length > 0 ? _game.Blurb : _game.Summary;
        if (about.Length > 0) AddNote(about);

        var install = RecompZone.InstalledFor(_game.Id);
        AddHeader(install != null ? "Installed" : "Get it");
        if (_install != null)
        {
            var box = new StackPanel { Margin = new Thickness(6, 4, 6, 10) };
            box.Children.Add(_progressText);
            var track = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            var back = new Border { Height = 8, CornerRadius = new CornerRadius(4) };
            back.SetResourceReference(Border.BackgroundProperty, "CircleBrush");
            track.Children.Add(back);
            track.Children.Add(_progressFill);
            box.Children.Add(track);
            Rows.Children.Add(box);
            AddRow("Cancel", null, "", click: () => _install?.Cancel());
        }
        else if (install != null)
        {
            AddRow("Play", install.Tag, "", click: () => Play(install));
            if (_release != null && _release.Tag != install.Tag)
                AddRow("Update to " + _release.Tag, Size(_release.Size), "", click: () => _ = InstallAsync(), subtitle: "Unpacked over this version; your saves and settings stay");
            else if (_releaseChecked && _release != null) AddRow("Up to date", _release.Tag, "", click: null);
            AddRow("Add to Home", null, "", click: () => _ = AddToHomeAsync(install), subtitle: "A tile for it on the home row, like your other games");
            AddRow("Open its folder", null, "", click: () => Host.Push(new ExplorerScreen(install.Folder)));
            AddRow("Uninstall", null, "", click: () => _ = UninstallAsync(install));
        }
        else if (!_game.ForWindows) AddNote("There's no Windows version of this recomp yet.");
        else if (!_game.CanInstall) AddRow("Get it from its page", null, "", click: OpenPage, subtitle: "This one isn't on GitHub, so it's installed from its own page");
        else if (!_releaseChecked) AddNote("Checking GitHub for the newest version…");
        else if (_release == null) { AddNote(_releaseError ?? "There's no Windows download for it on GitHub right now."); AddRow("Open its page", null, "", click: OpenPage); }
        else AddRow("Install", $"{_release.Tag}  ·  {Size(_release.Size)}", "", click: () => _ = InstallAsync(),
            subtitle: "Downloaded from the developer's GitHub release and checked against GitHub's checksum");

        AddHeader("More");
        if (_game.Screenshots.Count > 0) AddRow("Screenshots", _game.Screenshots.Count.ToString(), "", click: () => Host.Push(new ViewerScreen(_game.Screenshots, 0)));
        if (_release != null && _release.Notes.Length > 0)
            AddRow("What's new in " + _release.Tag, _release.Published > DateTime.MinValue ? _release.Published.ToString("MMM d, yyyy") : null, "",
                click: () => _ = Host.AskAsync(_release.Name, Clean(_release.Notes), "OK"));
        AddRow("The developer's page", _game.Repo, "", click: OpenPage);
        AddNote($"You need your own copy of {(_game.Game.Length > 0 ? _game.Game : "the original game")}. The recomp asks for it (or for files from it) the first time it starts, " +
                "and its page says which version it needs. SquareDeck never downloads game files. Recomp Zone's list comes from recomplauncher.com.");
    }

    private static string Size(long bytes) => bytes <= 0 ? "" : SystemStats.Bytes(bytes);

    /// <summary>Release notes without the Markdown marks, kept to a readable length.</summary>
    private static string Clean(string notes)
    {
        var text = System.Text.RegularExpressions.Regex.Replace(notes, @"\*\*|__|`|^#+\s*|!\[[^\]]*\]\([^)]*\)", "", System.Text.RegularExpressions.RegexOptions.Multiline);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\[([^\]]+)\]\([^)]*\)", "$1");
        return text.Length > 2400 ? text[..2400] + "…" : text.Trim();
    }

    private void OpenPage() => Host.Push(BrowserScreen.Open(Host, _release?.Page is { Length: > 0 } p ? p : _game.Page));

    private void Play(RecompInstall install)
    {
        try { RecompZone.Play(install); }
        catch (Exception ex) { _ = Host.AskAsync("Couldn't start it", ex.Message, "OK"); }
    }

    private async Task InstallAsync()
    {
        if (_release == null || _install != null) return;
        _install = new CancellationTokenSource();
        var ct = _install.Token;
        var release = _release;
        _progressText.Text = "Starting the download…";
        _progressFill.Width = 0;
        Rebuild();
        var progress = new Progress<(long Done, long Total)>(p =>
        {
            double f = p.Total > 0 ? Math.Clamp(p.Done / (double)p.Total, 0, 1) : 0;
            _progressFill.Width = 928 * f;
            _progressText.Text = p.Total > 0 ? $"Downloading {release.Tag}: {(int)Math.Floor(f * 100)}%  ({SystemStats.Bytes(p.Done)} of {SystemStats.Bytes(p.Total)})" : $"Downloading {release.Tag}: {SystemStats.Bytes(p.Done)}";
            if (f >= 1) _progressText.Text = "Unpacking…";
        });
        try
        {
            var done = await RecompZone.InstallAsync(_game, release, progress, ct);
            _install = null;
            Rebuild();
            Host.ShowToast("Recomp Zone", _game.Name + " is installed", "Press Play to start it. It asks for your copy of the game the first time.", "", "info");
        }
        catch (OperationCanceledException) { _install = null; Rebuild(); }
        catch (Exception ex)
        {
            _install = null;
            Rebuild();
            await Host.AskAsync("Couldn't install it", ex.Message, "OK");
        }
    }

    private async Task AddToHomeAsync(RecompInstall install)
    {
        string id = "recomp-" + install.Id;
        if (Host.Library.CustomGames.Any(c => c.Id == id || string.Equals(c.Path, install.Program, StringComparison.OrdinalIgnoreCase)))
        {
            await Host.AskAsync("It's already on Home", null, "OK");
            return;
        }
        string? cover = null;
        if (_game.Cover.Length > 0)
        {
            try
            {
                using var http = new System.Net.Http.HttpClient();
                var bytes = await http.GetByteArrayAsync(_game.Cover);
                string ext = Path.GetExtension(new Uri(_game.Cover).AbsolutePath);
                cover = Path.Combine(RecompZone.Folder, "Covers", install.Id + (ext.Length is > 1 and <= 5 ? ext : ".jpg"));
                Directory.CreateDirectory(Path.GetDirectoryName(cover)!);
                await File.WriteAllBytesAsync(cover, bytes);
            }
            catch (Exception) { cover = null; }
        }
        Host.AddCustomGame(id, _game.Name, install.Program, cover);
        Host.ShowToast("Added to Home", _game.Name, "It's on the home row and in the Library.", "", "info");
    }

    private async Task UninstallAsync(RecompInstall install)
    {
        if (await Host.AskAsync("Uninstall " + _game.Name + "?", "Its folder goes to the Recycle Bin. Saves it keeps elsewhere (in your user folder) aren't touched.", "Cancel", "Uninstall") != 1) return;
        try { await Host.RunBusyAsync("Uninstalling…", () => Task.Run(() => RecompZone.Uninstall(install.Id))); }
        catch (Exception ex) { await Host.AskAsync("Couldn't uninstall it", ex.Message, "OK"); }
        Rebuild();
    }
}
