using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using SquareDeck.Services;

namespace SquareDeck.Screens;

/// <summary>
/// The launch animation for games started from Recomp Zone: the original console's machine code streams in on the
/// left, native PC code streams out on the right, and the cover is rebuilt block by block in between as the
/// "functions" are translated, then a flash: native. About 2.6 seconds; B skips it.
/// </summary>
public sealed class RecompileScreen : Screen
{
    private const double Duration = 2.6;
    private const int Blocks = 8;   // the cover is rebuilt from an 8 x 8 grid of blocks

    private readonly Game _game;
    private readonly TaskCompletionSource<bool> _done = new();
    private readonly Grid _root = new() { ClipToBounds = true };
    private readonly StackPanel _source = new(), _native = new();
    private readonly TranslateTransform _sourceScroll = new(), _nativeScroll = new();
    private readonly Border[] _blocks = new Border[Blocks * Blocks];
    private readonly int[] _order;
    private readonly Border _barFill = new() { HorizontalAlignment = HorizontalAlignment.Left, Width = 0, Height = 6, CornerRadius = new CornerRadius(3) };
    private readonly TextBlock _status = Ui.Text("Translating functions…", 22, "FgBrush", bold: true);
    private readonly TextBlock _count = Ui.Text("", 15, "SubtleBrush");
    private readonly Border _flash = new() { Background = Brushes.White, Opacity = 0, IsHitTestVisible = false };
    private readonly Border _cover;
    private readonly int _functions;
    private readonly (string Console, string Cpu, Func<Random, string> Line) _arch;
    private readonly Random _random = new();
    private DateTime _start;
    private bool _finishing;
    private int _lastTick = -1, _lastLine = -1;

    public override bool IsDialog => false;
    public Task<bool> Result => _done.Task;

    public RecompileScreen(Game game)
    {
        _game = game;
        _arch = Arch(game);
        _functions = 1200 + Math.Abs(game.Name.GetHashCode() % 3800);
        _order = Enumerable.Range(0, Blocks * Blocks).OrderBy(_ => _random.Next()).ToArray();

        _root.SetResourceReference(Panel.BackgroundProperty, "BgBrush");
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _root.Children.Add(columns);

        // The two code streams, fading out toward the edges.
        columns.Children.Add(Stream(_source, _sourceScroll, _arch.Cpu, 0, HorizontalAlignment.Right));
        var nativeStream = Stream(_native, _nativeScroll, "x86-64", 2, HorizontalAlignment.Left);
        columns.Children.Add(nativeStream);

        // The middle: the cover, rebuilt from blocks.
        var middle = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        Grid.SetColumn(middle, 1);
        var title = Ui.Text("RECOMPILING", 15, "AccentBrush", bold: true);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        middle.Children.Add(title);
        var route = Ui.Text($"{_arch.Console}  ·  {_arch.Cpu}  →  x86-64", 14, "SubtleBrush");
        route.HorizontalAlignment = HorizontalAlignment.Center;
        route.Margin = new Thickness(0, 4, 0, 16);
        middle.Children.Add(route);

        const double size = 240;
        var grid = new UniformGrid { Rows = Blocks, Columns = Blocks, Width = size, Height = size };
        for (int i = 0; i < _blocks.Length; i++)
        {
            var block = new Border { Opacity = 1, Margin = new Thickness(0.5) };
            block.SetResourceReference(Border.BackgroundProperty, "CardBrush");
            _blocks[i] = block;
            grid.Children.Add(block);
        }
        _cover = new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(Math.Max(Theme.TileCorner + 4, 6)), ClipToBounds = true,
            Effect = Perf.Active ? null : new DropShadowEffect { BlurRadius = 30, ShadowDepth = 0, Opacity = 0.5 }
        };
        var stack = new Grid();
        if (game.Cover != null)
        {
            // Each block is a window onto the real cover: blocks light up accent-colored, then show their piece of the art.
            var art = new Image { Source = game.Cover, Stretch = Stretch.UniformToFill };
            stack.Children.Add(art);
            _art = art;
        }
        stack.Children.Add(grid);
        _cover.Child = stack;
        middle.Children.Add(_cover);

        var bar = new Border { Height = 6, Width = size, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 18, 0, 0) };
        bar.SetResourceReference(Border.BackgroundProperty, "CardBrush");
        _barFill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        bar.Child = _barFill;
        middle.Children.Add(bar);
        _status.HorizontalAlignment = HorizontalAlignment.Center;
        _status.Margin = new Thickness(0, 12, 0, 0);
        middle.Children.Add(_status);
        _count.HorizontalAlignment = HorizontalAlignment.Center;
        middle.Children.Add(_count);
        var name = Ui.Text(game.Name, 16, "SubtleBrush");
        name.HorizontalAlignment = HorizontalAlignment.Center;
        name.Margin = new Thickness(0, 6, 0, 0);
        middle.Children.Add(name);
        columns.Children.Add(middle);

        _root.Children.Add(_flash);
        Content = _root;
    }

    private Image? _art;

    private static UIElement Stream(StackPanel lines, TranslateTransform scroll, string label, int column, HorizontalAlignment align)
    {
        var host = new Grid { ClipToBounds = true, Margin = new Thickness(30, 60, 30, 60) };
        Grid.SetColumn(host, column);
        var mask = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        mask.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
        mask.GradientStops.Add(new GradientStop(Colors.Black, 0.25));
        mask.GradientStops.Add(new GradientStop(Colors.Black, 0.75));
        mask.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        host.OpacityMask = mask;
        lines.RenderTransform = scroll;
        lines.HorizontalAlignment = align;
        lines.VerticalAlignment = VerticalAlignment.Bottom;
        host.Children.Add(lines);
        var tag = Ui.Text(label, 13, "AccentBrush", bold: true);
        tag.HorizontalAlignment = align;
        tag.VerticalAlignment = VerticalAlignment.Top;
        tag.Margin = new Thickness(0, 30, 0, 0);
        var outer = new Grid();
        Grid.SetColumn(outer, column);
        Grid.SetColumn(host, 0);
        outer.Children.Add(host);
        outer.Children.Add(tag);
        return outer;
    }

    public override void FocusDefault() { }

    public override void OnShown()
    {
        if (_start != default) return;
        _start = DateTime.UtcNow;
        CompositionTarget.Rendering += OnFrame;
    }

    public override void OnClosed()
    {
        CompositionTarget.Rendering -= OnFrame;
        _done.TrySetResult(true);
    }

    public override bool OnAction(NavAction action)
    {
        if (action is NavAction.Back or NavAction.Accept && !_finishing) Finish();
        return true;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        double t = (DateTime.UtcNow - _start).TotalSeconds;
        double p = Math.Clamp(t / (Duration - 0.35), 0, 1);
        double eased = 1 - Math.Pow(1 - p, 2);

        // New code lines, faster as it goes.
        int line = (int)(t * (14 + 40 * p));
        while (_lastLine < line)
        {
            _lastLine++;
            AddLine(_source, _arch.Line(_random), 0.85);
            AddLine(_native, X86(_random), 0.85);
        }

        // Blocks light up in a scattered order (accent), then fade to show their piece of the cover.
        int lit = (int)(eased * _blocks.Length);
        for (int i = 0; i < _blocks.Length; i++)
        {
            var block = _blocks[_order[i]];
            if (i >= lit + 6 || block.Tag is "done") continue;
            if (block.Tag == null) { block.SetResourceReference(Border.BackgroundProperty, "AccentBrush"); block.Tag = "lit"; }
            if (i < lit)
            {
                block.Opacity = _art != null ? Math.Max(0, block.Opacity - 0.09) : 0.9;
                if (block.Opacity <= 0 || _art == null) block.Tag = "done";
            }
        }

        _barFill.Width = 240 * eased;
        _count.Text = $"{(int)(_functions * eased):N0} / {_functions:N0} functions";

        int tick = lit / 8;
        if (tick != _lastTick)
        {
            _lastTick = tick;
            if (Host?.Settings.RumbleStrength > 0) Host.RumbleScaled(18, 0.35);
        }
        if (!_finishing && t >= Duration) Finish();
    }

    private static void AddLine(StackPanel panel, string text, double opacity)
    {
        var block = Ui.Text(text, 14, "SubtleBrush");
        block.FontFamily = new FontFamily("Consolas, Cascadia Mono, Courier New");
        block.Opacity = opacity;
        panel.Children.Add(block);
        if (panel.Children.Count > 40) panel.Children.RemoveAt(0);
    }

    private async void Finish()
    {
        if (_finishing) return;
        _finishing = true;
        CompositionTarget.Rendering -= OnFrame;
        foreach (var b in _blocks)
        {
            b.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            b.Opacity = _art != null ? 0 : 0.9;
        }
        _barFill.Width = 240;
        _count.Text = $"{_functions:N0} / {_functions:N0} functions";
        _status.Text = "Native ✓  Ready to play";
        Host?.RumbleScaled(60, 0.8);
        _flash.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0.35, 0, TimeSpan.FromMilliseconds(380)));
        await Task.Delay(600);
        if (Host?.IsTop(this) == true) Host.Pop(this);
        _done.TrySetResult(true);
    }

    // ------------------------------------------------------------------ the machine code

    private static (string, string, Func<Random, string>) Arch(Game game)
    {
        string origin = "";
        try
        {
            var id = game.LaunchId.StartsWith("recomp-", StringComparison.Ordinal) ? game.LaunchId[7..] : "";
            var entry = RecompZone.Games.FirstOrDefault(g => g.Id == id);
            if (entry != null) origin = entry.ConsoleName;
        }
        catch (Exception) { }
        string o = origin.ToUpperInvariant();
        if (o.Contains("64") && o.Contains("NINTENDO") || o == "N64") return (origin, "MIPS R4300i", Mips);
        if (o.Contains("PLAYSTATION 2") || o == "PS2") return (origin, "MIPS R5900", Mips);
        if (o.Contains("PLAYSTATION 3") || o == "PS3") return (origin, "Cell PPU", Ppc);
        if (o.Contains("PLAYSTATION") || o == "PS1") return (origin, "MIPS R3000A", Mips);
        if (o.Contains("GAMECUBE")) return (origin, "PowerPC Gekko", Ppc);
        if (o.Contains("WII")) return (origin, "PowerPC Broadway", Ppc);
        if (o.Contains("360")) return (origin, "PowerPC Xenon", Ppc);
        if (o.Contains("DREAMCAST")) return (origin, "SuperH SH-4", Sh4);
        if (o.Contains("ADVANCE") || o.Contains("GBA") || o.Contains("DS")) return (origin, "ARM", Arm);
        return (origin.Length > 0 ? origin : "Console", "MIPS", Mips);
    }

    private static readonly string[] MipsRegs = { "$a0", "$a1", "$v0", "$t0", "$t1", "$t2", "$s0", "$s1", "$sp", "$ra", "$at", "$gp" };
    private static readonly string[] PpcRegs = { "r3", "r4", "r5", "r9", "r10", "r11", "r29", "r30", "r31", "r1" };
    private static readonly string[] X86Regs = { "rax", "rbx", "rcx", "rdx", "rsi", "rdi", "r8", "r9", "r12", "r13", "eax", "ecx" };

    private static string Pick(Random r, string[] a) => a[r.Next(a.Length)];
    private static string Addr(Random r) => "8" + r.Next(0x0010000, 0x0FFFFFF).ToString("X7");

    private static string Mips(Random r) => r.Next(7) switch
    {
        0 => $"{Addr(r)}  lui   {Pick(r, MipsRegs)}, 0x{r.Next(0x8000, 0x80FF):X4}",
        1 => $"{Addr(r)}  addiu {Pick(r, MipsRegs)}, {Pick(r, MipsRegs)}, 0x{r.Next(0, 0xFFF):X}",
        2 => $"{Addr(r)}  lw    {Pick(r, MipsRegs)}, 0x{r.Next(0, 0xFF):X}({Pick(r, MipsRegs)})",
        3 => $"{Addr(r)}  sw    {Pick(r, MipsRegs)}, 0x{r.Next(0, 0xFF):X}($sp)",
        4 => $"{Addr(r)}  jal   func_{Addr(r)}",
        5 => $"{Addr(r)}  beq   {Pick(r, MipsRegs)}, $zero, .L{r.Next(100, 999)}",
        _ => $"{Addr(r)}  mtc1  {Pick(r, MipsRegs)}, $f{r.Next(0, 31)}"
    };

    private static string Ppc(Random r) => r.Next(6) switch
    {
        0 => $"{Addr(r)}  lis   {Pick(r, PpcRegs)}, 0x{r.Next(0x8000, 0x80FF):X4}",
        1 => $"{Addr(r)}  lwz   {Pick(r, PpcRegs)}, 0x{r.Next(0, 0xFF):X}({Pick(r, PpcRegs)})",
        2 => $"{Addr(r)}  stw   {Pick(r, PpcRegs)}, 0x{r.Next(0, 0xFF):X}(r1)",
        3 => $"{Addr(r)}  bl    fn_{Addr(r)}",
        4 => $"{Addr(r)}  mflr  r0",
        _ => $"{Addr(r)}  fmadds f{r.Next(0, 31)}, f{r.Next(0, 31)}, f{r.Next(0, 31)}, f{r.Next(0, 31)}"
    };

    private static string Sh4(Random r) => r.Next(4) switch
    {
        0 => $"{Addr(r)}  mov.l @r{r.Next(0, 15)}+, r{r.Next(0, 15)}",
        1 => $"{Addr(r)}  add   #{r.Next(1, 127)}, r{r.Next(0, 15)}",
        2 => $"{Addr(r)}  jsr   @r{r.Next(0, 15)}",
        _ => $"{Addr(r)}  fmul  fr{r.Next(0, 15)}, fr{r.Next(0, 15)}"
    };

    private static string Arm(Random r) => r.Next(4) switch
    {
        0 => $"{Addr(r)}  ldr   r{r.Next(0, 12)}, [r{r.Next(0, 12)}, #0x{r.Next(0, 0xFF):X}]",
        1 => $"{Addr(r)}  str   r{r.Next(0, 12)}, [sp, #0x{r.Next(0, 0x7F):X}]",
        2 => $"{Addr(r)}  bl    sub_{Addr(r)}",
        _ => $"{Addr(r)}  add   r{r.Next(0, 12)}, r{r.Next(0, 12)}, #{r.Next(1, 255)}"
    };

    private static string X86(Random r) => r.Next(7) switch
    {
        0 => $"mov   {Pick(r, X86Regs)}, qword ptr [rdi+0x{r.Next(0, 0x3FF):X}]",
        1 => $"mov   dword ptr [rsp+0x{r.Next(0, 0xFF):X}], {Pick(r, X86Regs)}",
        2 => $"lea   {Pick(r, X86Regs)}, [{Pick(r, X86Regs)}+0x{r.Next(0, 0xFFF):X}]",
        3 => $"call  recomp_func_{r.Next(0x100000, 0xFFFFFF):x}",
        4 => $"test  {Pick(r, X86Regs)}, {Pick(r, X86Regs)}",
        5 => $"vfmadd231ps xmm{r.Next(0, 15)}, xmm{r.Next(0, 15)}, xmm{r.Next(0, 15)}",
        _ => $"jne   .L{r.Next(100, 999)}"
    };
}
