using System.Diagnostics;
using System.IO;

namespace SquareDeck.Services;

/// <summary>
/// Installs a downloaded SquareDeck .msi without the Windows Installer dialogs. A small PowerShell script shows a
/// full-screen "Updating SquareDeck" window in the launcher's colors (a spinning ring, the logo, one of five moving
/// backgrounds, a progress bar that follows the installer's log, a bounce at each quarter and confetti at the end),
/// with one of five vibration styles. The script waits for the launcher to close, runs msiexec silently
/// (Windows asks once for permission), writes the result for the next start, and opens the new version.
/// </summary>
public static class SilentUpdate
{
    public static string ResultFile => Path.Combine(Settings.DataDir, "update-result.txt");

    /// <summary>Starts the script and returns the file it creates once its window is on screen.</summary>
    /// <param name="rumble">How strong the controller's vibration is (0 = none, 1 = full), from the vibration setting.</param>
    /// <summary>The update screen's moving backgrounds (Settings > Updates).</summary>
    public static readonly (string Id, string Name)[] Looks =
    {
        ("random", "A different one each time"), ("orbit", "Rising sparks"), ("warp", "Warp speed"), ("pulse", "Pulse rings"),
        ("blocks", "Building blocks"), ("aurora", "Aurora"),
    };

    /// <summary>The update screen's vibration styles.</summary>
    public static readonly (string Id, string Name)[] Haptics =
    {
        ("random", "A different one each time"), ("heartbeat", "Heartbeat"), ("engine", "Engine"), ("ticks", "Clock ticks"),
        ("waves", "Waves (side to side)"), ("fanfare", "Fanfare"),
    };

    private static string Resolve((string Id, string Name)[] list, string? id)
    {
        if (id != null && id != "random" && list.Any(x => x.Id == id)) return id;
        var real = list.Where(x => x.Id != "random").ToArray();
        return real[Random.Shared.Next(real.Length)].Id;
    }

    /// <summary>Shows the update screen with nothing installed: the steps are played back, and a press closes it.</summary>
    public static void Preview(string background, string foreground, string accent, double rumble, string? look, string? haptic)
    {
        string folder = Path.Combine(Path.GetTempPath(), "SquareDeckUpdatePreview");
        Directory.CreateDirectory(folder);
        Start(Path.Combine(folder, "preview.msi"), Updater.Current.ToString(), background, foreground, accent, rumble, look, haptic, preview: true);
    }

    public static string Start(string msi, string version, string background, string foreground, string accent, double rumble = 0.6,
        string? look = null, string? haptic = null, bool preview = false)
    {
        string folder = Path.GetDirectoryName(msi)!;
        string exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "SquareDeck.exe");
        string logo = Path.Combine(AppContext.BaseDirectory, "HomeApp", "Assets", "Square150x150Logo.png");
        string ready = Path.Combine(folder, "update-ready.txt");
        try { File.Delete(ready); } catch (Exception) { }
        if (!preview) { try { File.Delete(ResultFile); } catch (Exception) { } }

        static string Q(string s) => s.Replace("'", "''"); // inside PowerShell single quotes
        string text = Script
            .Replace("__MSI__", Q(msi)).Replace("__EXE__", Q(exe)).Replace("__PID__", Environment.ProcessId.ToString())
            .Replace("__LOG__", Q(Path.Combine(folder, "install.log"))).Replace("__RESULT__", Q(ResultFile))
            .Replace("__READY__", Q(ready)).Replace("__VERSION__", Q(version)).Replace("__LOGO__", Q(logo))
            .Replace("__RESTART__", App.RestartArgument)
            .Replace("__RUMBLE__", Math.Clamp(rumble, 0, 1).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))
            .Replace("__STYLE__", Resolve(Looks, look)).Replace("__HAPTIC__", Resolve(Haptics, haptic))
            .Replace("__PREVIEW__", preview ? "True" : "False")
            .Replace("__BG__", background).Replace("__FG__", foreground).Replace("__ACCENT__", accent);
        string path = Path.Combine(folder, "update.ps1");
        File.WriteAllText(path, text, new System.Text.UTF8Encoding(true)); // a BOM, so Windows PowerShell reads it as UTF-8

        Process.Start(new ProcessStartInfo("powershell.exe",
            $"-NoProfile -ExecutionPolicy Bypass -Sta -WindowStyle Hidden -File \"{path}\"")
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = folder
        });
        return ready;
    }

    /// <summary>What the last silent update did, read once at start: (Windows Installer exit code, version), or null.</summary>
    public static (int Code, string Version)? TakeResult()
    {
        try
        {
            if (!File.Exists(ResultFile)) return null;
            var parts = File.ReadAllText(ResultFile).Trim().TrimStart('\uFEFF').Split('|');
            File.Delete(ResultFile);
            return int.TryParse(parts[0], out int code) ? (code, parts.Length > 1 ? parts[1] : "") : null;
        }
        catch (Exception) { return null; }
    }

    private const string Script = """
# SquareDeck update: waits for SquareDeck to close, installs the .msi silently, shows progress, then reopens SquareDeck.
# In preview mode (Settings > Updates > Try the update screen) nothing is installed: the steps are played back.
$ErrorActionPreference = 'SilentlyContinue'
$msi = '__MSI__'
$exe = '__EXE__'
$waitPid = __PID__
$log = '__LOG__'
$result = '__RESULT__'
$version = '__VERSION__'
$style = '__STYLE__'
$haptic = '__HAPTIC__'
$preview = [bool]::Parse('__PREVIEW__')
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase

# ---------------------------------------------------------------- the controller: vibration, and buttons (to close the preview)
$rumbleStrength = [double]'__RUMBLE__'
$script:padOk = $true
try {
  Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class SdRumble {
  [StructLayout(LayoutKind.Sequential)] public struct Vibration { public ushort Left; public ushort Right; }
  [StructLayout(LayoutKind.Sequential)] public struct State { public uint Packet; public ushort Buttons; public byte LT; public byte RT; public short LX; public short LY; public short RX; public short RY; }
  [DllImport("xinput1_4.dll")] private static extern int XInputSetState(int user, ref Vibration v);
  [DllImport("xinput1_4.dll")] private static extern int XInputGetState(int user, out State s);
  public static void Set(double left, double right) {
    var v = new Vibration { Left = (ushort)(Math.Max(0, Math.Min(1, left)) * 65535), Right = (ushort)(Math.Max(0, Math.Min(1, right)) * 65535) };
    for (int i = 0; i < 4; i++) { try { XInputSetState(i, ref v); } catch { } }
  }
  public static bool AnyButton() {
    for (int i = 0; i < 4; i++) { try { State s; if (XInputGetState(i, out s) == 0 && s.Buttons != 0) return true; } catch { } }
    return false;
  }
}
"@
} catch { $rumbleStrength = 0; $script:padOk = $false }

$script:buzzUntil = $null
$script:humming = $false
function Buzz([double]$left, [double]$right, [int]$ms) {
  if ($rumbleStrength -le 0) { return }
  try { [SdRumble]::Set($left * $rumbleStrength, $right * $rumbleStrength) } catch { }
  $script:buzzUntil = (Get-Date).AddMilliseconds($ms)
}
# A steady vibration that changes every frame (engine, waves).
function Hum([double]$left, [double]$right) {
  if ($rumbleStrength -le 0) { return }
  try { [SdRumble]::Set($left * $rumbleStrength, $right * $rumbleStrength) } catch { }
  $script:humming = $true
}
function Quiet { if ($script:humming -or $script:buzzUntil) { try { [SdRumble]::Set(0, 0) } catch { } }; $script:humming = $false; $script:buzzUntil = $null }
# A pattern is a flat list of steps, four numbers each: left, right, milliseconds, pause after.
$script:pattern = @(); $script:patternIndex = 0; $script:patternAt = Get-Date
function Pattern([double[]]$steps) { $script:pattern = $steps; $script:patternIndex = 0; $script:patternAt = Get-Date }
function PatternPlaying { return ($script:patternIndex + 3 -lt $script:pattern.Count) }

# Each vibration style: a tap for each install step, a pulse at 25 / 50 / 75%, its own finish.
function StepTap {
  switch ($haptic) {
    'engine'  { Pattern @(0.55, 0.2, 60, 40, 0.25, 0.1, 70, 0) }   # a gear change
    'ticks'   { Buzz 0.1 0.6 45 }
    'waves'   { Buzz 0.35 0.35 160 }
    'fanfare' { Buzz 0.2 0.3 80 }
    default   { Buzz 0.25 0.35 110 }
  }
}
function MilestoneTap {
  switch ($haptic) {
    'ticks'   { Pattern @(0, 0.6, 35, 55, 0, 0.6, 35, 55, 0, 0.6, 35, 0) }
    'fanfare' { Pattern @(0.4, 0.6, 90, 70, 0.6, 0.8, 130, 0) }
    'waves'   { Pattern @(0.5, 0, 140, 0, 0, 0.5, 140, 0) }
    default   { Buzz 0.3 0.45 120 }
  }
}
function SuccessPattern {
  switch ($haptic) {
    'engine'  { Pattern @(0.3, 0.1, 120, 0, 0.55, 0.25, 120, 0, 0.9, 0.5, 240, 0) }          # revs up, then cuts out
    'ticks'   { Pattern @(0, 0.7, 40, 60, 0, 0.7, 40, 60, 0, 0.7, 40, 60, 0.6, 0.9, 220, 0) } # tick, tick, tick, ding
    'waves'   { Pattern @(0.7, 0, 180, 0, 0, 0.7, 180, 0, 0.7, 0.7, 280, 0) }                # left, right, both
    'fanfare' { Pattern @(0.5, 0.5, 90, 60, 0.5, 0.5, 90, 60, 0.7, 0.7, 90, 140, 1, 1, 450, 0) } # ta-ta-ta-daaa
    default   { Pattern @(0.5, 0.7, 140, 120, 0.8, 1.0, 260, 0) }                           # a happy double beat
  }
}
# What plays between taps while installing.
function IdleHaptic([datetime]$now, [double]$t) {
  switch ($haptic) {
    'heartbeat' {
      if (($now - $script:lastBeat).TotalMilliseconds -gt 2400) { $script:lastBeat = $now; Pattern @(0.14, 0, 70, 110, 0.22, 0, 90, 0) }  # lub-dub
    }
    'engine' { $l = 0.04 + 0.2 * [Math]::Min([double]1, [double]$script:shown); Hum $l ($l * 0.35) }   # a hum that rises with the bar
    'ticks' {
      if (($now - $script:lastBeat).TotalMilliseconds -gt 1000) { $script:lastBeat = $now; Buzz 0 0.22 30 }  # a clock
    }
    'waves' { $w = [Math]::Sin($t * 2.1); Hum (0.13 * [Math]::Max([double]0, [double]$w)) (0.13 * [Math]::Max([double]0, [double](-$w))) }  # swells side to side
    default { }  # fanfare: quiet until the milestones
  }
}

[xml]$xaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        WindowStyle="None" ResizeMode="NoResize" WindowState="Maximized" Topmost="True"
        Background="__BG__" Title="Updating SquareDeck" FontFamily="Segoe UI">
  <Grid>
    <Canvas x:Name="Sky" IsHitTestVisible="False"/>
    <Ellipse x:Name="Glow" Width="900" Height="900" Opacity="0.22" IsHitTestVisible="False">
      <Ellipse.Fill>
        <RadialGradientBrush>
          <GradientStop Color="__ACCENT__" Offset="0"/>
          <GradientStop Color="#00000000" Offset="1"/>
        </RadialGradientBrush>
      </Ellipse.Fill>
    </Ellipse>
    <StackPanel x:Name="Content" VerticalAlignment="Center" HorizontalAlignment="Center" Opacity="0">
      <StackPanel.RenderTransform><TranslateTransform Y="40"/></StackPanel.RenderTransform>
      <Grid x:Name="Badge" Width="240" Height="240" HorizontalAlignment="Center" RenderTransformOrigin="0.5,0.5">
        <Grid.RenderTransform>
          <TransformGroup><ScaleTransform/><TranslateTransform/></TransformGroup>
        </Grid.RenderTransform>
        <Ellipse x:Name="Ring" Width="220" Height="220" StrokeThickness="6" Stroke="__ACCENT__"
                 StrokeDashArray="14 8" StrokeDashCap="Round" RenderTransformOrigin="0.5,0.5" Opacity="0.9">
          <Ellipse.RenderTransform><RotateTransform/></Ellipse.RenderTransform>
        </Ellipse>
        <Ellipse x:Name="Ring2" Width="176" Height="176" StrokeThickness="2" Stroke="__ACCENT__"
                 StrokeDashArray="2 6" StrokeDashCap="Round" RenderTransformOrigin="0.5,0.5" Opacity="0.45">
          <Ellipse.RenderTransform><RotateTransform/></Ellipse.RenderTransform>
        </Ellipse>
        <Ellipse x:Name="Flash" Width="220" Height="220" Fill="__ACCENT__" Opacity="0" RenderTransformOrigin="0.5,0.5">
          <Ellipse.RenderTransform><ScaleTransform/></Ellipse.RenderTransform>
        </Ellipse>
        <Border x:Name="Logo" Width="104" Height="104" CornerRadius="26" ClipToBounds="True" RenderTransformOrigin="0.5,0.5" Background="__ACCENT__">
          <Border.RenderTransform><ScaleTransform/></Border.RenderTransform>
          <Grid>
            <Image x:Name="LogoImage" Stretch="UniformToFill"/>
            <TextBlock x:Name="Check" Text="&#xE73E;" FontFamily="Segoe MDL2 Assets" FontSize="54" Foreground="White"
                       HorizontalAlignment="Center" VerticalAlignment="Center" Opacity="0"/>
          </Grid>
        </Border>
      </Grid>
      <TextBlock x:Name="Title" Text="Updating SquareDeck" Foreground="__FG__" FontSize="36" FontWeight="SemiBold" HorizontalAlignment="Center" Margin="0,30,0,0"/>
      <TextBlock x:Name="Version" Foreground="__FG__" Opacity="0.7" FontSize="17" HorizontalAlignment="Center" Margin="0,6,0,0"/>
      <Grid Width="480" Height="10" Margin="0,32,0,0">
        <Border CornerRadius="5" Background="__FG__" Opacity="0.15"/>
        <!-- The fill, with the shine sweeping inside it only: it used to run along the whole track, past the end of the
             fill, so the bar looked out of step with the percentage. -->
        <Border x:Name="Fill" CornerRadius="5" Background="__ACCENT__" HorizontalAlignment="Left" Width="0" ClipToBounds="True">
          <Canvas>
            <Border x:Name="Shine" Width="90" Height="10" Opacity="0.55">
              <Border.Background>
                <LinearGradientBrush StartPoint="0,0" EndPoint="1,0">
                  <GradientStop Color="#00FFFFFF" Offset="0"/>
                  <GradientStop Color="#CCFFFFFF" Offset="0.5"/>
                  <GradientStop Color="#00FFFFFF" Offset="1"/>
                </LinearGradientBrush>
              </Border.Background>
              <Border.RenderTransform><TranslateTransform/></Border.RenderTransform>
            </Border>
          </Canvas>
        </Border>
        <Ellipse x:Name="Head" Width="18" Height="18" Fill="__ACCENT__" HorizontalAlignment="Left" Margin="0,-4,0,-4" Opacity="0.9" Visibility="Hidden">
          <Ellipse.Effect><DropShadowEffect Color="__ACCENT__" BlurRadius="10" ShadowDepth="0" Opacity="0.8"/></Ellipse.Effect>
        </Ellipse>
      </Grid>
      <Grid Width="480" Margin="0,12,0,0">
        <TextBlock x:Name="Stage" Foreground="__FG__" Opacity="0.8" FontSize="15" Text="Waiting for SquareDeck to close..."/>
        <TextBlock x:Name="Percent" Foreground="__FG__" Opacity="0.8" FontSize="15" HorizontalAlignment="Right" Text="0%"/>
      </Grid>
      <TextBlock x:Name="Note" Foreground="__FG__" Opacity="0.45" FontSize="13" HorizontalAlignment="Center" Margin="0,30,0,0"
                 Text="Keep your device on. SquareDeck opens again by itself when it's done."/>
    </StackPanel>
    <Canvas x:Name="Fx" IsHitTestVisible="False"/>
  </Grid>
</Window>
'@

$win = [Windows.Markup.XamlReader]::Load((New-Object System.Xml.XmlNodeReader $xaml))
$find = { param($n) $win.FindName($n) }
$sky = & $find 'Sky'; $fx = & $find 'Fx'; $ring = & $find 'Ring'; $ring2 = & $find 'Ring2'; $logo = & $find 'Logo'; $fill = & $find 'Fill'
$shine = & $find 'Shine'; $stage = & $find 'Stage'; $percent = & $find 'Percent'; $title = & $find 'Title'; $head = & $find 'Head'
$check = & $find 'Check'; $logoImage = & $find 'LogoImage'; $note = & $find 'Note'; $content = & $find 'Content'
$badge = & $find 'Badge'; $flash = & $find 'Flash'; $glow = & $find 'Glow'
$badgeScale = $badge.RenderTransform.Children[0]; $badgeShift = $badge.RenderTransform.Children[1]
(& $find 'Version').Text = "Version $version"
if ($preview) {
  $title.Text = 'Updating SquareDeck'
  $note.Text = 'This is a preview: nothing is installed. Press any button or key to close it.'
}

$accent = [System.Windows.Media.ColorConverter]::ConvertFromString('__ACCENT__')
function Brush($c) { $b = New-Object System.Windows.Media.SolidColorBrush $c; $b.Freeze(); return $b }
$accentBrush = Brush $accent
# Two more colors from the accent, for the aurora and confetti (its channels turned around).
$accent2 = [System.Windows.Media.Color]::FromRgb($accent.G, $accent.B, $accent.R)
$accent3 = [System.Windows.Media.Color]::FromRgb($accent.B, $accent.R, $accent.G)

$logoPath = '__LOGO__'
if (Test-Path $logoPath) {
  $bmp = New-Object System.Windows.Media.Imaging.BitmapImage
  $bmp.BeginInit(); $bmp.CacheOption = 'OnLoad'; $bmp.UriSource = New-Object Uri $logoPath; $bmp.EndInit()
  $logoImage.Source = $bmp
}

function Animate($target, $prop, $from, $to, $seconds, [bool]$forever = $false, [bool]$reverse = $false, $ease = $null) {
  $a = New-Object System.Windows.Media.Animation.DoubleAnimation
  if ($from -ne $null) { $a.From = [double]$from }
  $a.To = [double]$to
  $a.Duration = New-Object System.Windows.Duration ([TimeSpan]::FromSeconds($seconds))
  if ($forever) { $a.RepeatBehavior = [System.Windows.Media.Animation.RepeatBehavior]::Forever }
  $a.AutoReverse = $reverse
  if ($ease) { $a.EasingFunction = $ease }
  elseif ($reverse) { $e = New-Object System.Windows.Media.Animation.SineEase; $e.EasingMode = 'EaseInOut'; $a.EasingFunction = $e }
  $target.BeginAnimation($prop, $a)
}
function Forever($target, $prop, $from, $to, $seconds, [bool]$reverse = $false) { Animate $target $prop $from $to $seconds $true $reverse }
function FadeTo($element, [double]$to, [double]$seconds) { Animate $element ([System.Windows.UIElement]::OpacityProperty) $null $to $seconds }

$scaleX = [System.Windows.Media.ScaleTransform]::ScaleXProperty; $scaleY = [System.Windows.Media.ScaleTransform]::ScaleYProperty
Forever $ring.RenderTransform ([System.Windows.Media.RotateTransform]::AngleProperty) 0 360 3.2
Forever $ring2.RenderTransform ([System.Windows.Media.RotateTransform]::AngleProperty) 360 0 5.5
Forever $logo.RenderTransform $scaleX 1 1.06 0.9 $true
Forever $logo.RenderTransform $scaleY 1 1.06 0.9 $true
Forever $shine.RenderTransform ([System.Windows.Media.TranslateTransform]::XProperty) -90 480 1.6
Forever $glow ([System.Windows.UIElement]::OpacityProperty) 0.14 0.3 2.4 $true
Forever $head ([System.Windows.UIElement]::OpacityProperty) 0.55 1 0.7 $true

# The badge bounces at each quarter of the way.
function Pop {
  $e = New-Object System.Windows.Media.Animation.ElasticEase; $e.Oscillations = 2; $e.Springiness = 5; $e.EasingMode = 'EaseOut'
  Animate $badgeScale $scaleX 1.18 1 0.7 $false $false $e
  Animate $badgeScale $scaleY 1.18 1 0.7 $false $false $e
  $flash.Opacity = 0.35
  Animate $flash.RenderTransform $scaleX 0.6 1.5 0.6
  Animate $flash.RenderTransform $scaleY 0.6 1.5 0.6
  FadeTo $flash 0 0.6
}
# A shake, for when it didn't work.
function Shake {
  $a = New-Object System.Windows.Media.Animation.DoubleAnimation
  $a.From = -14; $a.To = 14; $a.AutoReverse = $true
  $a.Duration = New-Object System.Windows.Duration ([TimeSpan]::FromMilliseconds(70))
  $a.RepeatBehavior = New-Object System.Windows.Media.Animation.RepeatBehavior 4
  $badgeShift.BeginAnimation([System.Windows.Media.TranslateTransform]::XProperty, $a)
}
function SetStage([string]$text) {
  if ($stage.Text -eq $text) { return }
  $stage.Text = $text; $stage.Opacity = 0; FadeTo $stage 0.8 0.35
}

# ---------------------------------------------------------------- the moving background (one of five styles)
$rand = New-Object Random
$script:things = New-Object System.Collections.ArrayList
$script:confetti = New-Object System.Collections.ArrayList
$script:lastSpawn = Get-Date
$script:litCount = 0

function Dot([double]$size, $brush, [double]$opacity) {
  $e = New-Object System.Windows.Shapes.Ellipse
  $e.Width = $size; $e.Height = $size; $e.Fill = $brush; $e.Opacity = $opacity
  $null = $sky.Children.Add($e)
  return $e
}

function SetUpBackground {
  $w = $win.ActualWidth; $h = $win.ActualHeight
  switch ($style) {
    'warp' {
      # Stars rushing out from the middle, faster as the bar fills.
      for ($i = 0; $i -lt 90; $i++) {
        $e = Dot 3 $accentBrush 0
        $null = $script:things.Add(@{ E = $e; A = $rand.NextDouble() * 6.2832; D = $rand.NextDouble() * [Math]::Max($w, $h) * 0.6; V = 1.5 + $rand.NextDouble() * 3 })
      }
    }
    'pulse' { }  # rings are made as they go
    'blocks' {
      # A wall of tiles that lights up as the update goes along.
      $cell = 96; $cols = [Math]::Ceiling($w / $cell); $rows = [Math]::Ceiling($h / $cell)
      $all = New-Object System.Collections.ArrayList
      for ($r = 0; $r -lt $rows; $r++) { for ($c = 0; $c -lt $cols; $c++) {
        $t = New-Object System.Windows.Shapes.Rectangle
        $t.Width = $cell - 10; $t.Height = $cell - 10; $t.RadiusX = 14; $t.RadiusY = 14; $t.Fill = $accentBrush; $t.Opacity = 0.03
        [System.Windows.Controls.Canvas]::SetLeft($t, $c * $cell + 5); [System.Windows.Controls.Canvas]::SetTop($t, $r * $cell + 5)
        $null = $sky.Children.Add($t); $null = $all.Add($t)
      } }
      # In a random order.
      $script:things = New-Object System.Collections.ArrayList
      foreach ($t in ($all | Sort-Object { $rand.Next() })) { $null = $script:things.Add($t) }
    }
    'aurora' {
      # Three soft lights drifting slowly.
      $colors = @($accent, $accent2, $accent3)
      for ($i = 0; $i -lt 3; $i++) {
        $e = New-Object System.Windows.Shapes.Ellipse
        $e.Width = $w * 0.7; $e.Height = $h * 0.8; $e.Opacity = 0.22
        $g = New-Object System.Windows.Media.RadialGradientBrush
        $g.GradientStops.Add((New-Object System.Windows.Media.GradientStop $colors[$i], 0))
        $g.GradientStops.Add((New-Object System.Windows.Media.GradientStop ([System.Windows.Media.Color]::FromArgb(0, 0, 0, 0)), 1))
        $e.Fill = $g
        $null = $sky.Children.Add($e)
        $null = $script:things.Add(@{ E = $e; P = $i * 2.1; S = 0.12 + $i * 0.05 })
      }
    }
    default {
      # orbit: sparks drifting up.
      for ($i = 0; $i -lt 46; $i++) {
        $e = Dot $rand.Next(2, 6) $accentBrush (0.15 + $rand.NextDouble() * 0.5)
        [System.Windows.Controls.Canvas]::SetLeft($e, $rand.NextDouble() * $w)
        [System.Windows.Controls.Canvas]::SetTop($e, $rand.NextDouble() * $h)
        $null = $script:things.Add(@{ E = $e; V = 0.4 + $rand.NextDouble() * 1.6 })
      }
    }
  }
}

function MoveBackground([double]$t) {
  $w = $win.ActualWidth; $h = $win.ActualHeight; $cx = $w / 2; $cy = $h / 2
  $boost = 1 + [Math]::Min([double]1, [double]$script:shown) * 2
  switch ($style) {
    'warp' {
      $max = [Math]::Sqrt($cx * $cx + $cy * $cy)
      foreach ($s in $script:things) {
        $s.D += $s.V * (1 + $s.D / 160) * $boost * 0.6
        if ($s.D -gt $max) { $s.D = $rand.NextDouble() * 30; $s.A = $rand.NextDouble() * 6.2832 }
        $size = 1.5 + $s.D / 220
        $s.E.Width = $size; $s.E.Height = $size; $s.E.Opacity = [Math]::Min([double]0.9, [double]($s.D / 260))
        [System.Windows.Controls.Canvas]::SetLeft($s.E, $cx + [Math]::Cos($s.A) * $s.D - $size / 2)
        [System.Windows.Controls.Canvas]::SetTop($s.E, $cy + [Math]::Sin($s.A) * $s.D - $size / 2)
      }
    }
    'pulse' {
      # A new ring every so often (more often near the end); each one grows and fades.
      $every = 900 / $boost
      if (((Get-Date) - $script:lastSpawn).TotalMilliseconds -gt $every) {
        $script:lastSpawn = Get-Date
        $e = New-Object System.Windows.Shapes.Ellipse
        $e.Stroke = $accentBrush; $e.StrokeThickness = 3; $e.Opacity = 0.5
        $null = $sky.Children.Add($e)
        $null = $script:things.Add(@{ E = $e; R = 120 })
      }
      $max = [Math]::Sqrt($cx * $cx + $cy * $cy)
      foreach ($s in @($script:things)) {
        $s.R += 4 + $boost * 2
        $size = $s.R * 2
        $s.E.Width = $size; $s.E.Height = $size; $s.E.Opacity = [Math]::Max([double]0, [double](0.5 * (1 - $s.R / $max)))
        [System.Windows.Controls.Canvas]::SetLeft($s.E, $cx - $s.R); [System.Windows.Controls.Canvas]::SetTop($s.E, $cy - $s.R)
        if ($s.R -gt $max) { $sky.Children.Remove($s.E); $script:things.Remove($s) }
      }
    }
    'blocks' {
      $want = [Math]::Floor([Math]::Min([double]1, [double]$script:shown) * $script:things.Count)
      while ($script:litCount -lt $want) {
        $tile = $script:things[$script:litCount]; $script:litCount++
        Animate $tile ([System.Windows.UIElement]::OpacityProperty) 0.5 0.18 0.6
      }
      # Now and then a lit tile twinkles.
      if ($script:litCount -gt 0 -and $rand.Next(6) -eq 0) {
        $tile = $script:things[$rand.Next($script:litCount)]
        Animate $tile ([System.Windows.UIElement]::OpacityProperty) 0.18 0.4 0.35 $false $true
      }
    }
    'aurora' {
      foreach ($s in $script:things) {
        $x = $cx + [Math]::Sin($t * $s.S + $s.P) * $w * 0.28 - $s.E.Width / 2
        $y = $cy + [Math]::Cos($t * $s.S * 0.8 + $s.P * 1.3) * $h * 0.22 - $s.E.Height / 2
        [System.Windows.Controls.Canvas]::SetLeft($s.E, $x); [System.Windows.Controls.Canvas]::SetTop($s.E, $y)
        $s.E.Opacity = 0.16 + 0.1 * [Math]::Sin($t * 0.7 + $s.P) + 0.08 * [Math]::Min([double]1, [double]$script:shown)
      }
    }
    default {
      foreach ($s in $script:things) {
        $y = [System.Windows.Controls.Canvas]::GetTop($s.E) - $s.V * $boost * 0.7
        if ($y -lt -10) { $y = $h + 10; [System.Windows.Controls.Canvas]::SetLeft($s.E, $rand.NextDouble() * $w) }
        [System.Windows.Controls.Canvas]::SetTop($s.E, $y)
      }
    }
  }
}

# Confetti from the badge when the update worked.
function Celebrate {
  $p = $badge.TranslatePoint((New-Object System.Windows.Point 120, 120), $fx)
  $colors = @((Brush $accent), (Brush $accent2), (Brush $accent3), (Brush ([System.Windows.Media.Colors]::White)))
  for ($i = 0; $i -lt 90; $i++) {
    $r = New-Object System.Windows.Shapes.Rectangle
    $r.Width = 6 + $rand.Next(6); $r.Height = 10 + $rand.Next(8); $r.RadiusX = 2; $r.RadiusY = 2
    $r.Fill = $colors[$rand.Next($colors.Count)]
    $r.RenderTransform = New-Object System.Windows.Media.RotateTransform ($rand.Next(360))
    [System.Windows.Controls.Canvas]::SetLeft($r, $p.X); [System.Windows.Controls.Canvas]::SetTop($r, $p.Y)
    $null = $fx.Children.Add($r)
    $angle = -1.5708 + ($rand.NextDouble() - 0.5) * 2.6
    $speed = 9 + $rand.NextDouble() * 12
    $null = $script:confetti.Add(@{ E = $r; X = $p.X; Y = $p.Y; VX = [Math]::Cos($angle) * $speed; VY = [Math]::Sin($angle) * $speed; Spin = $rand.Next(-14, 14) })
  }
  $flash.Opacity = 0.6
  Animate $flash.RenderTransform $scaleX 0.5 3 0.9
  Animate $flash.RenderTransform $scaleY 0.5 3 0.9
  FadeTo $flash 0 0.9
}

function MoveConfetti {
  if ($script:confetti.Count -eq 0) { return }
  foreach ($c in @($script:confetti)) {
    $c.VY += 0.45; $c.VX *= 0.985; $c.X += $c.VX; $c.Y += $c.VY
    [System.Windows.Controls.Canvas]::SetLeft($c.E, $c.X); [System.Windows.Controls.Canvas]::SetTop($c.E, $c.Y)
    $c.E.RenderTransform.Angle += $c.Spin
    if ($c.Y -gt $win.ActualHeight + 30) { $fx.Children.Remove($c.E); $script:confetti.Remove($c) }
  }
}

$win.Add_ContentRendered({
  Set-Content -Path '__READY__' -Value 'ready'
  SetUpBackground
  # Everything rises into place.
  $ease = New-Object System.Windows.Media.Animation.CubicEase; $ease.EasingMode = 'EaseOut'
  Animate $content ([System.Windows.UIElement]::OpacityProperty) 0 1 0.6 $false $false $ease
  Animate $content.RenderTransform ([System.Windows.Media.TranslateTransform]::YProperty) 40 0 0.7 $false $false $ease
})

# ---------------------------------------------------------------- the install
$steps = @{
  'InstallValidate' = @(0.12, 'Checking your system...'); 'InstallInitialize' = @(0.2, 'Getting ready...');
  'RemoveExistingProducts' = @(0.3, 'Removing the old version...'); 'RemoveFiles' = @(0.38, 'Removing old files...');
  'InstallFiles' = @(0.55, 'Copying new files...'); 'CreateShortcuts' = @(0.72, 'Adding shortcuts...');
  'WriteRegistryValues' = @(0.78, 'Saving settings...'); 'RegisterProduct' = @(0.84, 'Registering...');
  'PublishProduct' = @(0.88, 'Almost there...'); 'InstallFinalize' = @(0.93, 'Finishing up...')
}
$order = @('InstallValidate', 'InstallInitialize', 'RemoveExistingProducts', 'RemoveFiles', 'InstallFiles', 'CreateShortcuts',
           'WriteRegistryValues', 'RegisterProduct', 'PublishProduct', 'InstallFinalize')
$script:state = 'wait'; $script:target = 0.04; $script:shown = 0.0; $script:proc = $null; $script:pos = 0; $script:doneAt = $null; $script:code = -1
$script:started = Get-Date; $script:simIndex = 0; $script:simAt = Get-Date; $script:nextMilestone = 0.25

function ReachStep([string]$name) {
  if (-not $steps.ContainsKey($name)) { return }
  $s = $steps[$name]
  if ($s[0] -gt $script:target) { $script:target = $s[0]; SetStage $s[1]; StepTap }
}

function Read-Log {
  if (-not (Test-Path $log)) { return }
  try {
    $fs = [IO.File]::Open($log, 'Open', 'Read', 'ReadWrite')
    $null = $fs.Seek($script:pos, 'Begin')
    $reader = New-Object IO.StreamReader($fs, [Text.Encoding]::Default)
    $text = $reader.ReadToEnd(); $script:pos = $fs.Position
    $reader.Close(); $fs.Close()
    foreach ($m in [regex]::Matches($text, 'Action start [\d:]+: (\w+)\.')) { ReachStep $m.Groups[1].Value }
  } catch { }
}

function Finish([int]$code) {
  $script:code = $code
  if (-not $preview) { Set-Content -Path $result -Value "$code|$version" -Encoding UTF8 }
  $shine.Visibility = 'Collapsed'
  Quiet
  if ($code -eq 0 -or $code -eq 3010) {
    SuccessPattern
    $script:target = 1.0; $title.Text = 'SquareDeck is up to date'
    SetStage $(if ($preview) { 'Done! (This was a preview.)' } else { 'Done! Opening SquareDeck...' })
    FadeTo $ring 0 0.4
    FadeTo $ring2 0 0.4
    FadeTo $logoImage 0 0.3
    FadeTo $check 1 0.3
    FadeTo $head 0 0.4
    Celebrate
  } elseif ($code -eq 1602 -or $code -eq 1223) {
    $title.Text = 'Update cancelled'; SetStage 'Opening SquareDeck again...'
  } else {
    Pattern @(0.6, 0.2, 500, 0)
    Shake
    $title.Text = "The update didn't finish"; SetStage "Windows Installer error $code. Opening SquareDeck again..."
    $fill.Background = (New-Object System.Windows.Media.BrushConverter).ConvertFromString('#E04A3C')
  }
  $note.Text = ''
  $script:state = 'done'; $script:doneAt = Get-Date
}

function CloseAll {
  $timer.Stop()
  try { [SdRumble]::Set(0, 0) } catch { }
  $win.Close()
}
if ($preview) { $win.Add_KeyDown({ CloseAll }); $win.Add_MouseDown({ CloseAll }) }

$timer = New-Object System.Windows.Threading.DispatcherTimer
$timer.Interval = [TimeSpan]::FromMilliseconds(33)
$script:lastBeat = Get-Date
$timer.Add_Tick({
  $now = Get-Date
  $t = ($now - $script:started).TotalSeconds
  # Vibration: end a buzz on time, play a queued pattern, else the style's idle vibration while installing.
  if ($script:buzzUntil -and $now -ge $script:buzzUntil) { $script:buzzUntil = $null; try { [SdRumble]::Set(0, 0) } catch { } }
  if ((PatternPlaying) -and -not $script:buzzUntil -and $now -ge $script:patternAt) {
    $i = $script:patternIndex; $p = $script:pattern
    Buzz $p[$i] $p[$i + 1] ([int]$p[$i + 2])
    $script:patternAt = $now.AddMilliseconds($p[$i + 2] + $p[$i + 3])
    $script:patternIndex += 4
  }
  if ($script:state -eq 'install' -and -not $script:buzzUntil -and -not (PatternPlaying)) { IdleHaptic $now $t }
  elseif ($script:humming -and ($script:state -ne 'install' -or $script:buzzUntil)) { $script:humming = $false }

  MoveBackground $t
  MoveConfetti

  # A press closes the preview (after a moment, so the press that opened it doesn't).
  if ($preview -and $script:padOk -and $t -gt 1.2) { try { if ([SdRumble]::AnyButton()) { CloseAll; return } } catch { } }

  switch ($script:state) {
    'wait' {
      if ($preview) {
        if ($t -gt 1.2) { SetStage 'Starting the installer...'; $script:target = 0.08; $script:state = 'install'; $script:simAt = $now }
      }
      elseif (-not (Get-Process -Id $waitPid -ErrorAction SilentlyContinue) -or $t -gt 30) {
        SetStage 'Starting the installer...'; $script:target = 0.08
        try {
          $psi = New-Object System.Diagnostics.ProcessStartInfo 'msiexec.exe'
          $psi.Arguments = "/i `"$msi`" /qn /norestart /l*v `"$log`""
          $psi.UseShellExecute = $true; $psi.Verb = 'runas'
          $script:proc = [System.Diagnostics.Process]::Start($psi)
          $script:state = 'install'
        } catch { Finish 1223 }
      }
    }
    'install' {
      if ($preview) {
        if (($now - $script:simAt).TotalMilliseconds -gt 1100) {
          $script:simAt = $now
          if ($script:simIndex -lt $order.Count) { ReachStep $order[$script:simIndex]; $script:simIndex++ }
          else { Finish 0 }
        }
      } else {
        Read-Log
        if ($script:proc.HasExited) { Read-Log; Finish $script:proc.ExitCode }
      }
      # Between steps the bar keeps creeping toward the next one, so it never looks stuck.
      if ($script:state -eq 'install' -and $script:target -lt 0.92) { $script:target += 0.0006 }
    }
    'done' {
      $wait = if ($script:code -eq 0 -or $script:code -eq 3010) { 2.6 } else { 3.5 }
      if ($preview) { $wait += 1.5 }
      if (($now - $script:doneAt).TotalSeconds -gt $wait) {
        $timer.Stop()
        try { [SdRumble]::Set(0, 0) } catch { }
        if (-not $preview -and (Test-Path $exe)) { Start-Process -FilePath $exe -ArgumentList '__RESTART__' }
        $win.Close()
      }
    }
  }
  $script:shown += ($script:target - $script:shown) * 0.08
  if ([Math]::Abs($script:target - $script:shown) -lt 0.002) { $script:shown = $script:target } # arrives, instead of creeping forever
  if ($script:shown -ge $script:nextMilestone -and $script:nextMilestone -lt 1) {
    $script:nextMilestone += 0.25
    if ($script:state -eq 'install') { Pop; MilestoneTap }
  }
  # Every number here is a [double] on purpose: with a whole number first, PowerShell picked [Math]::Min(int, int),
  # which rounded the progress to 0 or 1, so the bar stayed empty (then jumped full) while the percentage counted up.
  $width = 480 * [Math]::Min([double]1, [double]$script:shown)
  $fill.Width = $width
  # The glowing dot rides inside the end of the fill. Centred on the end, it and its glow reached past the fill, so the
  # bar looked further along than the percentage said.
  $head.Margin = New-Object System.Windows.Thickness -ArgumentList ([Math]::Max([double]0, [double]($width - 18))), -4, 0, -4
  $head.Visibility = if ($width -ge 18) { 'Visible' } else { 'Hidden' }
  # Rounded down, like the bar: 100% only once it's full.
  $percent.Text = '{0}%' -f [int][Math]::Floor([Math]::Min([double]100, [double]($script:shown * 100 + 0.0001)))
})
$timer.Start()
$null = $win.ShowDialog()

""";
}
