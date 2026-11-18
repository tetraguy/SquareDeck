using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SquareDeck.Services;

/// <summary>
/// A storage speed test, like CrystalDiskMark's basic one: sequential read and write in 1 MB blocks, and random
/// 4 KB reads and writes (one at a time, which is what loading a game mostly looks like). It writes a test file to
/// the drive, reads it back past Windows' cache (so the numbers are the drive's, not the memory's), and deletes it.
/// </summary>
public static class DiskSpeed
{
    public enum Test { SeqRead, SeqWrite, RandRead, RandWrite }

    public sealed record Drive(string Root, string Label, string Kind, long Free, long Size);

    /// <param name="Test">Which test this is about.</param>
    /// <param name="MBps">Megabytes per second so far (or final).</param>
    /// <param name="Iops">Operations per second, for the random tests.</param>
    /// <param name="Fraction">How far the whole run has got, 0 to 1.</param>
    public sealed record Progress(Test Test, double MBps, double Iops, double Fraction, bool Done);

    private const int Block = 1024 * 1024, Small = 4096, Align = 4096;
    private const FileOptions NoBuffering = (FileOptions)0x20000000;

    /// <summary>Drives that can be tested: internal ones, memory cards and USB drives that are ready.</summary>
    public static List<Drive> Drives()
    {
        var list = new List<Drive>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady || d.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;
                string kind = d.DriveType == DriveType.Removable ? "Memory card / USB" : "Internal";
                string label = d.VolumeLabel.Length > 0 ? d.VolumeLabel : d.DriveType == DriveType.Removable ? "Removable drive" : "Local disk";
                list.Add(new Drive(d.RootDirectory.FullName, label, kind, d.AvailableFreeSpace, d.TotalSize));
            }
            catch (Exception) { } // a card being removed, or a drive that won't say
        }
        return list;
    }

    /// <summary>Runs all four tests on <paramref name="root"/> with a test file of <paramref name="sizeMb"/> MB.</summary>
    public static async Task RunAsync(string root, int sizeMb, IProgress<Progress> progress, CancellationToken ct)
    {
        string folder = Path.Combine(root, "SquareDeck speed test");
        // Not every drive lets us write at its top level (the system drive needs admin): use our own folder there.
        if (string.Equals(Path.GetPathRoot(Environment.SystemDirectory), root, StringComparison.OrdinalIgnoreCase))
            folder = Path.Combine(Path.GetTempPath(), "SquareDeck speed test");
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, "test.bin");
        long size = (long)sizeMb * Block;

        // One buffer, pinned and lined up on a 4 KB boundary, as unbuffered file access needs.
        var raw = GC.AllocateUninitializedArray<byte>(Block + Align, pinned: true);
        var pin = GCHandle.Alloc(raw, GCHandleType.Pinned);
        try
        {
            long address = pin.AddrOfPinnedObject().ToInt64();
            int offset = (int)((Align - address % Align) % Align);
            var buffer = new Memory<byte>(raw, offset, Block);
            Random.Shared.NextBytes(buffer.Span); // random data: drives that compress zeros don't get a head start

            await Task.Run(() =>
            {
                // 1. Sequential write (also makes the file the other tests read).
                using (var h = File.OpenHandle(file, FileMode.Create, FileAccess.ReadWrite, FileShare.None, NoBuffering | FileOptions.WriteThrough, size))
                    Sequential(h, size, write: true, buffer, progress, Test.SeqWrite, 0.00, ct);
                // 2. Sequential read.
                using (var h = File.OpenHandle(file, FileMode.Open, FileAccess.Read, FileShare.None, NoBuffering))
                    Sequential(h, size, write: false, buffer, progress, Test.SeqRead, 0.25, ct);
                // 3. and 4. Random 4 KB reads, then writes, for a few seconds each.
                using (var h = File.OpenHandle(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None, NoBuffering | FileOptions.WriteThrough))
                {
                    Random4K(h, size, write: false, buffer[..Small], progress, Test.RandRead, 0.50, ct);
                    Random4K(h, size, write: true, buffer[..Small], progress, Test.RandWrite, 0.75, ct);
                }
            }, ct);
        }
        finally
        {
            pin.Free();
            try { File.Delete(file); } catch (Exception) { }
            try { Directory.Delete(folder); } catch (Exception) { }
        }
    }

    private static void Sequential(SafeFileHandle h, long size, bool write, Memory<byte> buffer, IProgress<Progress> progress,
                                   Test test, double start, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        long done = 0, lastReport = 0;
        while (done < size)
        {
            ct.ThrowIfCancellationRequested();
            if (write) RandomAccess.Write(h, buffer.Span, done);
            else if (RandomAccess.Read(h, buffer.Span, done) <= 0) break;
            done += Block;
            if (watch.ElapsedMilliseconds - lastReport > 150 || done >= size)
            {
                lastReport = watch.ElapsedMilliseconds;
                double mbps = done / 1048576.0 / Math.Max(0.001, watch.Elapsed.TotalSeconds);
                progress.Report(new Progress(test, mbps, 0, start + 0.25 * done / size, done >= size));
            }
        }
    }

    private static void Random4K(SafeFileHandle h, long size, bool write, Memory<byte> buffer, IProgress<Progress> progress,
                                 Test test, double start, CancellationToken ct)
    {
        const double seconds = 5;
        long blocks = size / Small;
        var random = new Random(test == Test.RandRead ? 1 : 2);
        var watch = Stopwatch.StartNew();
        long count = 0, lastReport = 0;
        while (watch.Elapsed.TotalSeconds < seconds)
        {
            ct.ThrowIfCancellationRequested();
            long at = random.NextInt64(blocks) * Small;
            if (write) RandomAccess.Write(h, buffer.Span, at);
            else RandomAccess.Read(h, buffer.Span, at);
            count++;
            if (watch.ElapsedMilliseconds - lastReport > 150)
            {
                lastReport = watch.ElapsedMilliseconds;
                double iops = count / watch.Elapsed.TotalSeconds;
                progress.Report(new Progress(test, iops * Small / 1048576.0, iops, start + 0.25 * Math.Min(1, watch.Elapsed.TotalSeconds / seconds), false));
            }
        }
        double final = count / Math.Max(0.001, watch.Elapsed.TotalSeconds);
        progress.Report(new Progress(test, final * Small / 1048576.0, final, start + 0.25, true));
    }

    /// <summary>A plain-words idea of what kind of storage gives these numbers.</summary>
    public static string Describe(double seqRead, double rand4kRead)
    {
        if (seqRead >= 2500) return "Fast NVMe SSD: games load about as quickly as they can.";
        if (seqRead >= 900) return "NVMe SSD: quick loading for nearly every game.";
        if (seqRead >= 400) return "SATA SSD speeds: fine for games, a little slower to load the biggest ones.";
        if (seqRead >= 120 || rand4kRead >= 8) return "Fast memory card or USB drive: good for older and smaller games, slower loading for big ones.";
        if (seqRead >= 40) return "Typical memory card speeds: expect long loading for big games; a faster card (A2 / V30) would help.";
        return "Slow storage: games will load slowly and may stutter. A faster card or the internal drive is better for games.";
    }
}
