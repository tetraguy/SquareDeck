using System.IO;
using System.Media;

namespace SquareDeck.Services;

/// <summary>
/// The companion's own little noises when it's petted, made on the spot (no sound files), a different voice for
/// each character: the blob bloops, the cat mews or purrs, the bunny squeaks, the ghost goes "ooOOoo", the robot
/// beeps and boops, the dino gives a tiny rawr. Each has a few versions, and the pitch shifts a touch every time,
/// so it never sounds quite the same twice. Quiet, and only with interface sounds on.
/// </summary>
public static class CompanionSounds
{
    private const int Rate = 22050;
    private static readonly Random Rng = new();
    private static readonly object Gate = new();
    private static SoundPlayer? _playing; // kept until the next one, so it isn't cleaned up mid-sound

    /// <summary>What it sounds like: a short description for each character, for its page.</summary>
    public static string Describe(string species) => species switch
    {
        "cat" => "mews and purrs",
        "bunny" => "squeaks",
        "ghost" => "goes ooOOoo",
        "robot" => "beeps and boops",
        "dino" => "gives a tiny rawr",
        _ => "bloops",
    };

    /// <summary>A pet's noise for the character (<paramref name="excited"/> for the fourth pet in a row: a bigger one).</summary>
    public static void Pet(string species, bool excited = false)
    {
        if (!UiSound.Enabled) return;
        double pitch = 0.9 + Rng.NextDouble() * 0.22;
        _ = Task.Run(() =>
        {
            try
            {
                var wav = Wav(Make(species, pitch, excited));
                var player = new SoundPlayer(new MemoryStream(wav));
                player.Load();
                lock (Gate)
                {
                    _playing = player;
                    player.Play();
                }
            }
            catch (Exception) { }
        });
    }

    // ---------------------------------------------------------------- the voices

    /// <summary>
    /// A little sound effect that goes with a reaction: boing (a hop), whoosh (a spin), chime (sparkles), pop,
    /// giggle (quick rising chirps), drum (a ba-dum-tss), slide (a slide whistle up), kiss (a smooch), zap.
    /// </summary>
    /// <param name="delayMs">Waits this long first, so it follows the character's own noise instead of cutting it off.</param>
    public static void Effect(string id, int delayMs = 0)
    {
        if (!UiSound.Enabled) return;
        double p = 0.95 + Rng.NextDouble() * 0.1;
        _ = Task.Run(async () =>
        {
            try
            {
                if (delayMs > 0) await Task.Delay(delayMs);
                float[] samples = id switch
                {
                    "boing" => Sweep(180 * p, 520 * p, 0.28, Shape.Sine, 0.5, vibrato: 0.08),
                    "whoosh" => Whoosh(0.35),
                    "chime" => Join(new List<float[]> { Sweep(1320 * p, 1320 * p, 0.12, Shape.Sine, 0.3, 0), Sweep(1760 * p, 1760 * p, 0.12, Shape.Sine, 0.3, 0), Sweep(2640 * p, 2640 * p, 0.22, Shape.Sine, 0.25, 0) }),
                    "pop" => Sweep(900 * p, 240 * p, 0.05, Shape.Sine, 0.6, 0),
                    "giggle" => Join(Enumerable.Range(0, 5).Select(i => Join(new List<float[]> { Sweep((700 + i * 90) * p, (900 + i * 90) * p, 0.05, Shape.Sine, 0.4, 0), Silence(0.03) })).ToList()),
                    "drum" => Join(new List<float[]> { Thud(0.09), Silence(0.05), Thud(0.09), Silence(0.07), Noise(0.25, 0.25) }),
                    "slide" => Sweep(400 * p, 1400 * p, 0.42, Shape.Sine, 0.4, vibrato: 0.01),
                    "kiss" => Join(new List<float[]> { Noise(0.03, 0.4), Sweep(1500 * p, 2200 * p, 0.06, Shape.Sine, 0.35, 0) }),
                    "zap" => Sweep(2200 * p, 300 * p, 0.16, Shape.Square, 0.18, 0),
                    "clap" => Join(new List<float[]> { Noise(0.04, 0.8), Thud(0.05) }),
                    "slurp" => Join(Enumerable.Range(0, 4).Select(_ => Join(new List<float[]> { Noise(0.05, 0.25), Sweep(600 * p, 300 * p, 0.04, Shape.Sine, 0.2, 0) })).ToList()),
                    "crunch" => Join(Enumerable.Range(0, 3).Select(_ => Join(new List<float[]> { Noise(0.035, 0.45), Silence(0.05) })).ToList()),
                    _ => Array.Empty<float>(),
                };
                if (samples.Length == 0) return;
                var player = new SoundPlayer(new MemoryStream(Wav(samples)));
                player.Load();
                lock (Gate) { _effect = player; player.Play(); }
            }
            catch (Exception) { }
        });
    }

    private static SoundPlayer? _effect;

    private static float[] Whoosh(double seconds)
    {
        // Filtered noise that swells and fades, like air rushing past.
        int n = (int)(Rate * seconds);
        var data = new float[n];
        double low = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;
            double cut = 0.05 + 0.25 * Math.Sin(Math.PI * t);
            low += (Rng.NextDouble() * 2 - 1 - low) * cut;
            data[i] = (float)(low * Math.Sin(Math.PI * t) * 0.9);
        }
        return data;
    }

    private static float[] Thud(double seconds)
    {
        int n = (int)(Rate * seconds);
        var data = new float[n];
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;
            phase += 2 * Math.PI * (140 - 80 * t) / Rate;
            data[i] = (float)(Math.Sin(phase) * Math.Pow(1 - t, 2) * 0.8);
        }
        return data;
    }

    private static float[] Noise(double seconds, double volume)
    {
        int n = (int)(Rate * seconds);
        var data = new float[n];
        for (int i = 0; i < n; i++) data[i] = (float)((Rng.NextDouble() * 2 - 1) * volume * Math.Pow(1 - (double)i / n, 2));
        return data;
    }

    private static float[] Make(string species, double p, bool excited)
    {
        // Now and then one of the character's extra noises (8.5.30).
        if (!excited && Rng.Next(3) == 0) return Extra(species, p);
        int variant = Rng.Next(3);
        switch (species)
        {
            case "cat":
                if (variant == 2 || excited) return Purr(excited ? 1.1 : 0.8);
                return Meow(p * (variant == 0 ? 1 : 1.25), variant == 0 ? 0.42 : 0.28);
            case "bunny":
            {
                // Two or three quick, high squeaks.
                var parts = new List<float[]>();
                int count = excited ? 4 : 2 + variant % 2;
                for (int i = 0; i < count; i++)
                {
                    parts.Add(Sweep(1700 * p, 2500 * p, 0.065, Shape.Sine, 0.5, vibrato: 0));
                    parts.Add(Silence(0.045));
                }
                return Join(parts);
            }
            case "ghost":
                // A soft, wavering "ooOOoo", rising and falling, with a breathy edge.
                return Ghost(p * (variant == 1 ? 1.2 : 1), excited ? 0.9 : 0.62);
            case "robot":
            {
                // Square-wave beeps: beep-boop, a rising trill, or a happy little tune when excited.
                var notes = excited ? new[] { 660.0, 880, 1100, 1320 }
                    : variant switch { 0 => new[] { 880.0, 660 }, 1 => new[] { 520.0, 780, 1040 }, _ => new[] { 1200.0, 900, 1200 } };
                var parts = new List<float[]>();
                foreach (var n in notes)
                {
                    parts.Add(Sweep(n * p, n * p, 0.075, Shape.Square, 0.22, vibrato: 0));
                    parts.Add(Silence(0.025));
                }
                return Join(parts);
            }
            case "dino":
                // A tiny, cute rawr: a low buzzy glide up and down with a growly rumble.
                return Rawr(p * (variant == 2 ? 1.25 : 1), excited ? 0.55 : 0.38);
            default:
            {
                // The blob: bubbly bloops that glide up.
                var parts = new List<float[]>();
                int count = excited ? 3 : 1 + variant % 2;
                for (int i = 0; i < count; i++)
                {
                    parts.Add(Sweep(260 * p * (1 + i * 0.25), 720 * p * (1 + i * 0.25), 0.11, Shape.Sine, 0.6, vibrato: 0));
                    parts.Add(Silence(0.04));
                }
                return Join(parts);
            }
        }
    }

    /// <summary>The characters' extra noises: a cat's trill, a bunny's thump, a ghost's "boo!", a robot computing, a dino's chomp, a blob's wobble.</summary>
    private static float[] Extra(string species, double p)
    {
        switch (species)
        {
            case "cat":
            {
                // A trill: a rolling, rising "brrrp".
                var a = Sweep(380 * p, 700 * p, 0.32, Shape.Saw, 0.25, vibrato: 0);
                for (int i = 0; i < a.Length; i++) a[i] *= (float)(0.55 + 0.45 * Math.Sin(2 * Math.PI * 28 * i / Rate));
                return a;
            }
            case "bunny": return Join(new List<float[]> { Thud(0.08), Silence(0.06), Thud(0.08), Silence(0.05), Sweep(2000 * p, 2600 * p, 0.05, Shape.Sine, 0.45, 0) });
            case "ghost": return Join(new List<float[]> { Noise(0.06, 0.15), Sweep(520 * p, 260 * p, 0.38, Shape.Sine, 0.5, vibrato: 0.04) });
            case "robot":
            {
                var parts = new List<float[]>();
                for (int i = 0; i < 7; i++) { double f = (500 + Rng.Next(10) * 120) * p; parts.Add(Sweep(f, f, 0.04, Shape.Square, 0.18, 0)); parts.Add(Silence(0.015)); }
                parts.Add(Sweep(1400 * p, 1400 * p, 0.12, Shape.Sine, 0.3, 0)); // ding: done
                return Join(parts);
            }
            case "dino": return Join(new List<float[]> { Noise(0.025, 0.6), Silence(0.05), Noise(0.025, 0.6), Silence(0.04), Rawr(p * 1.3, 0.25) });
            default: return Sweep(300 * p, 300 * p, 0.45, Shape.Sine, 0.55, vibrato: 0.12); // the blob's wobble
        }
    }

    private enum Shape { Sine, Square, Saw }

    /// <summary>A note gliding from one pitch to another, with a quick attack and a fade.</summary>
    private static float[] Sweep(double fromHz, double toHz, double seconds, Shape shape, double volume, double vibrato)
    {
        int n = (int)(Rate * seconds);
        var data = new float[n];
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;
            double hz = fromHz + (toHz - fromHz) * t;
            hz *= 1 + vibrato * Math.Sin(2 * Math.PI * 7 * i / Rate);
            phase += 2 * Math.PI * hz / Rate;
            double s = shape switch
            {
                Shape.Square => Math.Sin(phase) >= 0 ? 1 : -1,
                Shape.Saw => 2 * (phase / (2 * Math.PI) % 1) - 1,
                _ => Math.Sin(phase),
            };
            double env = Math.Min(1, i / (Rate * 0.006)) * Math.Pow(1 - t, 1.4);
            data[i] = (float)(s * env * volume);
        }
        return data;
    }

    private static float[] Meow(double p, double seconds)
    {
        // "Mee-ow": up, then down, through a bright, nasal tone (a sine with a few overtones), with a little vibrato.
        int n = (int)(Rate * seconds);
        var data = new float[n];
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;
            double contour = t < 0.35 ? 500 + 450 * (t / 0.35) : 950 - 420 * ((t - 0.35) / 0.65);
            double hz = contour * p * (1 + 0.02 * Math.Sin(2 * Math.PI * 6 * i / Rate));
            phase += 2 * Math.PI * hz / Rate;
            double s = Math.Sin(phase) + 0.45 * Math.Sin(phase * 2) + 0.25 * Math.Sin(phase * 3) + 0.12 * Math.Sin(phase * 4);
            double env = Math.Min(1, t / 0.08) * Math.Min(1, (1 - t) / 0.25);
            data[i] = (float)(s / 1.8 * env * 0.5);
        }
        return data;
    }

    private static float[] Purr(double seconds)
    {
        // A purr: a low rumble pulsing about 25 times a second.
        int n = (int)(Rate * seconds);
        var data = new float[n];
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;
            phase += 2 * Math.PI * 95 / Rate;
            double pulse = Math.Pow(Math.Max(0, Math.Sin(2 * Math.PI * 24 * i / Rate)), 2);
            double s = (Math.Sin(phase) * 0.7 + (Rng.NextDouble() * 2 - 1) * 0.3) * pulse;
            double env = Math.Min(1, t / 0.1) * Math.Min(1, (1 - t) / 0.2);
            data[i] = (float)(s * env * 0.55);
        }
        return data;
    }

    private static float[] Ghost(double p, double seconds)
    {
        int n = (int)(Rate * seconds);
        var data = new float[n];
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;
            double hz = (300 + 170 * Math.Sin(Math.PI * t)) * p * (1 + 0.05 * Math.Sin(2 * Math.PI * 5 * i / Rate));
            phase += 2 * Math.PI * hz / Rate;
            double s = Math.Sin(phase) * 0.85 + (Rng.NextDouble() * 2 - 1) * 0.08;
            double env = Math.Sin(Math.PI * t) * (0.75 + 0.25 * Math.Sin(2 * Math.PI * 4 * i / Rate));
            data[i] = (float)(s * env * 0.45);
        }
        return data;
    }

    private static float[] Rawr(double p, double seconds)
    {
        int n = (int)(Rate * seconds);
        var data = new float[n];
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;
            double hz = (t < 0.4 ? 150 + 140 * (t / 0.4) : 290 - 170 * ((t - 0.4) / 0.6)) * p;
            phase += 2 * Math.PI * hz / Rate;
            double saw = 2 * (phase / (2 * Math.PI) % 1) - 1;
            double growl = 1 + 0.5 * Math.Sin(2 * Math.PI * 32 * i / Rate);
            double s = (saw * 0.6 + (Rng.NextDouble() * 2 - 1) * 0.15) * growl;
            double env = Math.Min(1, t / 0.06) * Math.Pow(1 - t, 0.8);
            data[i] = (float)(s * env * 0.32);
        }
        return data;
    }

    private static float[] Silence(double seconds) => new float[(int)(Rate * seconds)];

    private static float[] Join(List<float[]> parts) => parts.SelectMany(x => x).ToArray();

    /// <summary>A 16-bit mono WAV of the samples.</summary>
    private static byte[] Wav(float[] samples)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + samples.Length * 2);
        w.Write("WAVEfmt "u8.ToArray());
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(Rate);
        w.Write(Rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8.ToArray());
        w.Write(samples.Length * 2);
        foreach (var s in samples) w.Write((short)(Math.Clamp(s, -1f, 1f) * short.MaxValue * 0.8));
        w.Flush();
        return ms.ToArray();
    }
}
