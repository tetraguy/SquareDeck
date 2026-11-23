using System.IO;
using System.Runtime.InteropServices;

namespace SquareDeck.Services;

/// <summary>Something in the Recycle Bin: where its file is now, its name, and where it came from.</summary>
public sealed record RecycledItem(string Path, string Name, string OriginalFolder, bool IsFolder, DateTime Deleted, long Size);

/// <summary>
/// The Recycle Bin, for Explorer: what's in it, putting things back, deleting them for good, and emptying it.
/// Read through Windows' own Shell (the same list File Explorer shows), on a thread of its own as the Shell needs.
/// </summary>
public static class RecycleBin
{
    /// <summary>The path Explorer uses for the Recycle Bin's "folder".</summary>
    public const string Key = "recycle:";

    public static bool Is(string? path) => string.Equals(path, Key, StringComparison.OrdinalIgnoreCase);

    private const int BitBucket = 10; // ShellSpecialFolderConstants.ssfBITBUCKET

    private static T Sta<T>(Func<T> work)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = work(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();
        if (error != null) throw error;
        return result;
    }

    private static dynamic Bin()
    {
        var type = Type.GetTypeFromProgID("Shell.Application") ?? throw new InvalidOperationException("Windows' Shell isn't available.");
        dynamic shell = Activator.CreateInstance(type)!;
        return shell.NameSpace(BitBucket);
    }

    public static List<RecycledItem> List() => Sta(() =>
    {
        var list = new List<RecycledItem>();
        dynamic bin = Bin();
        foreach (dynamic item in bin.Items())
        {
            try
            {
                string path = item.Path;
                string name = item.Name;
                bool folder = item.IsFolder;
                string from = "";
                DateTime deleted = DateTime.MinValue;
                long size = 0;
                try { from = Convert.ToString(item.ExtendedProperty("System.Recycle.DeletedFrom")) ?? ""; } catch (Exception) { }
                try { if (item.ExtendedProperty("System.Recycle.DateDeleted") is DateTime d) deleted = d.ToLocalTime(); } catch (Exception) { }
                try { size = Convert.ToInt64(item.Size); } catch (Exception) { }
                // The name the Shell gives can drop the extension (with extensions hidden); the original one is in the file.
                if (!folder && System.IO.Path.GetExtension(path) is { Length: > 0 } ext && !name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) name += ext;
                if (deleted == DateTime.MinValue) try { deleted = File.GetLastWriteTime(path); } catch (Exception) { }
                list.Add(new RecycledItem(path, name, from, folder, deleted, size));
            }
            catch (Exception) { }
        }
        return list;
    });

    /// <summary>Puts items back where they were deleted from.</summary>
    public static int Restore(IEnumerable<string> paths)
    {
        var wanted = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        return Sta(() =>
        {
            int done = 0;
            dynamic bin = Bin();
            foreach (dynamic item in bin.Items())
            {
                string path = item.Path;
                if (!wanted.Contains(path)) continue;
                bool restored = false;
                foreach (dynamic verb in item.Verbs())
                {
                    string name = Convert.ToString(verb.Name) ?? "";
                    // "undelete" is the verb's name in every language; the visible label is "Restore".
                    if (name.Replace("&", "").Equals("Restore", StringComparison.OrdinalIgnoreCase))
                    {
                        verb.DoIt();
                        restored = true;
                        break;
                    }
                }
                if (!restored) item.InvokeVerb("undelete");
                done++;
            }
            return done;
        });
    }

    /// <summary>Deletes items for good: the file in the Recycle Bin and the small record that remembers its name ($I…).</summary>
    public static void DeleteForever(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            string info = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path) ?? "", "$I" + System.IO.Path.GetFileName(path).Substring(Math.Min(2, System.IO.Path.GetFileName(path).Length)));
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            else if (File.Exists(path)) File.Delete(path);
            if (System.IO.Path.GetFileName(path).StartsWith("$R", StringComparison.OrdinalIgnoreCase) && File.Exists(info)) File.Delete(info);
        }
    }

    public static void Empty() => SHEmptyRecycleBin(IntPtr.Zero, null, 0x1 | 0x2 | 0x4);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? root, uint flags);
}
