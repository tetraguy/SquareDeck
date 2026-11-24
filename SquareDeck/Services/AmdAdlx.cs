using System.Runtime.InteropServices;

namespace SquareDeck.Services;

/// <summary>
/// Radeon Image Sharpening and Radeon Chill, switched through AMD's own ADLX library (amdadlx64.dll, installed with
/// the Radeon driver), the way AMD Software itself changes them. The interfaces are called straight through their
/// tables of functions, in the order AMD's ADLX SDK headers give them (github.com/GPUOpen-LibrariesAndSDKs/ADLX,
/// SDK/Include/ISystem.h and I3DSettings.h, MIT license). Everything answers null/false where there's no AMD driver.
/// </summary>
public static class AmdAdlx
{
    // ADLX 2.0.0.125, the SDK version these tables come from.
    private const ulong Version = (2UL << 48) | (0UL << 32) | (0UL << 16) | 125UL;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int InitFn(ulong version, out IntPtr system);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetOut(IntPtr self, out IntPtr result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetForGpu(IntPtr self, IntPtr gpu, out IntPtr result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint SizeFn(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int AtFn(IntPtr self, uint location, out IntPtr item);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int BoolOut(IntPtr self, out byte value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int BoolIn(IntPtr self, byte value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ReleaseFn(IntPtr self);

    // Positions in each table (IADLXInterface's Acquire, Release and QueryInterface come first, except on IADLXSystem).
    private const int SystemGetGPUs = 1, System3DSettings = 7;
    private const int Release = 1;
    private const int ListSize = 3, ListAtGpu = 11;
    private const int ServicesGetChill = 4, ServicesGetImageSharpening = 6;
    private const int ChillSupported = 3, ChillEnabled = 4, ChillSetEnabled = 8;
    private const int SharpenSupported = 3, SharpenEnabled = 4, SharpenSetEnabled = 7;

    private static readonly object Lock = new();
    private static bool _tried;
    private static IntPtr _system, _gpu, _services;

    private static T Fn<T>(IntPtr iface, int index) where T : Delegate
    {
        IntPtr table = Marshal.ReadIntPtr(iface);
        return Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(table, index * IntPtr.Size));
    }

    private static bool Ok(int result) => result is 0 or 1 or 2; // OK, already enabled, already initialized

    /// <summary>Starts ADLX once (on first use) and finds the first AMD GPU and its 3D settings.</summary>
    private static bool Ready()
    {
        if (_services != IntPtr.Zero) return true;
        if (_tried) return false;
        _tried = true;
        try
        {
            if (!NativeLibrary.TryLoad("amdadlx64.dll", out var lib)) return false;
            IntPtr init = NativeLibrary.TryGetExport(lib, "ADLXInitialize", out var a) ? a : IntPtr.Zero;
            if (init == IntPtr.Zero) return false;
            int r = Marshal.GetDelegateForFunctionPointer<InitFn>(init)(Version, out _system);
            if (!Ok(r) && NativeLibrary.TryGetExport(lib, "ADLXInitializeWithIncompatibleDriver", out var older))
                r = Marshal.GetDelegateForFunctionPointer<InitFn>(older)(Version, out _system);
            if (!Ok(r) || _system == IntPtr.Zero) return false;
            if (!Ok(Fn<GetOut>(_system, SystemGetGPUs)(_system, out var list)) || list == IntPtr.Zero) return false;
            try
            {
                if (Fn<SizeFn>(list, ListSize)(list) == 0) return false;
                if (!Ok(Fn<AtFn>(list, ListAtGpu)(list, 0, out _gpu)) || _gpu == IntPtr.Zero) return false;
            }
            finally { Fn<ReleaseFn>(list, Release)(list); }
            if (!Ok(Fn<GetOut>(_system, System3DSettings)(_system, out _services))) { _services = IntPtr.Zero; return false; }
            return _services != IntPtr.Zero;
        }
        catch (Exception) { _services = IntPtr.Zero; return false; }
    }

    /// <summary>Gets a feature's interface, does something with it and lets it go.</summary>
    private static T? With<T>(int getter, Func<IntPtr, T?> use)
    {
        lock (Lock)
        {
            if (!Ready()) return default;
            IntPtr feature = IntPtr.Zero;
            try
            {
                if (!Ok(Fn<GetForGpu>(_services, getter)(_services, _gpu, out feature)) || feature == IntPtr.Zero) return default;
                return use(feature);
            }
            catch (Exception) { return default; }
            finally { if (feature != IntPtr.Zero) try { Fn<ReleaseFn>(feature, Release)(feature); } catch (Exception) { } }
        }
    }

    private static bool? Get(int getter, int supported, int enabled) => With<bool?>(getter, f =>
    {
        if (!Ok(Fn<BoolOut>(f, supported)(f, out var sup)) || sup == 0) return null;
        return Ok(Fn<BoolOut>(f, enabled)(f, out var on)) ? on != 0 : null;
    });

    private static bool Set(int getter, int supported, int setter, bool on) => With(getter, f =>
        Ok(Fn<BoolOut>(f, supported)(f, out var sup)) && sup != 0 && Ok(Fn<BoolIn>(f, setter)(f, on ? (byte)1 : (byte)0)));

    /// <summary>Radeon Image Sharpening: on, off, or null where it isn't available.</summary>
    public static bool? ImageSharpening => Get(ServicesGetImageSharpening, SharpenSupported, SharpenEnabled);
    public static bool SetImageSharpening(bool on) => Set(ServicesGetImageSharpening, SharpenSupported, SharpenSetEnabled, on);

    /// <summary>Radeon Chill: on, off, or null where it isn't available. (Turning it on turns Anti-Lag off, as AMD Software does.)</summary>
    public static bool? Chill => Get(ServicesGetChill, ChillSupported, ChillEnabled);
    public static bool SetChill(bool on) => Set(ServicesGetChill, ChillSupported, ChillSetEnabled, on);
}
