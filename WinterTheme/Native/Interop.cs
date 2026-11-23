using System.Runtime.InteropServices;

namespace WinterTheme.Native;

[StructLayout(LayoutKind.Sequential)]
public struct CGPoint
{
    public double X, Y;
}

[StructLayout(LayoutKind.Sequential)]
public struct CGRect
{
    public double X, Y, W, H;

    public CGRect(double x, double y, double w, double h)
    {
        X = x; Y = y; W = w; H = h;
    }

    public override string ToString() => $"({X:0},{Y:0} {W:0}x{H:0})";
}

/// <summary>Minimal Objective-C runtime bridge (enough to tweak NSWindow from Avalonia).</summary>
internal static class ObjC
{
    private const string Lib = "/usr/lib/libobjc.A.dylib";
    private static readonly Dictionary<string, IntPtr> Sels = new();

    [DllImport(Lib)] private static extern IntPtr objc_getClass(string name);
    [DllImport(Lib)] private static extern IntPtr sel_registerName(string name);

    [DllImport(Lib, EntryPoint = "objc_msgSend")] private static extern IntPtr MsgPtr(IntPtr r, IntPtr s);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] private static extern IntPtr MsgPtrPtr(IntPtr r, IntPtr s, IntPtr a);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] private static extern IntPtr MsgPtrULong(IntPtr r, IntPtr s, ulong a);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] private static extern void MsgVoidLong(IntPtr r, IntPtr s, long a);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] private static extern void MsgVoidULong(IntPtr r, IntPtr s, ulong a);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] private static extern void MsgVoidPtr(IntPtr r, IntPtr s, IntPtr a);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] private static extern void MsgVoidBool(IntPtr r, IntPtr s, [MarshalAs(UnmanagedType.I1)] bool a);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] private static extern void MsgVoidRectBool(IntPtr r, IntPtr s, CGRect rect, [MarshalAs(UnmanagedType.I1)] bool b);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] private static extern bool MsgBool(IntPtr r, IntPtr s);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] private static extern ulong MsgULong(IntPtr r, IntPtr s);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] private static extern int MsgInt(IntPtr r, IntPtr s);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] private static extern CGRect MsgRect(IntPtr r, IntPtr s);
    [DllImport(Lib, EntryPoint = "objc_msgSend_stret")] private static extern void MsgRectStret(out CGRect ret, IntPtr r, IntPtr s);

    public static IntPtr Class(string name) => objc_getClass(name);

    public static IntPtr Sel(string name)
    {
        if (!Sels.TryGetValue(name, out var s))
            Sels[name] = s = sel_registerName(name);
        return s;
    }

    public static IntPtr Get(IntPtr o, string sel) => MsgPtr(o, Sel(sel));
    public static IntPtr Get(IntPtr o, string sel, IntPtr arg) => MsgPtrPtr(o, Sel(sel), arg);
    public static IntPtr GetAt(IntPtr o, string sel, ulong idx) => MsgPtrULong(o, Sel(sel), idx);
    public static bool GetBool(IntPtr o, string sel) => MsgBool(o, Sel(sel));
    public static ulong GetULong(IntPtr o, string sel) => MsgULong(o, Sel(sel));
    public static int GetInt(IntPtr o, string sel) => MsgInt(o, Sel(sel));

    public static CGRect GetRect(IntPtr o, string sel)
    {
        if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
            return MsgRect(o, Sel(sel));
        MsgRectStret(out var r, o, Sel(sel));
        return r;
    }

    public static void Set(IntPtr o, string sel, bool v) => MsgVoidBool(o, Sel(sel), v);
    public static void Set(IntPtr o, string sel, long v) => MsgVoidLong(o, Sel(sel), v);
    public static void SetU(IntPtr o, string sel, ulong v) => MsgVoidULong(o, Sel(sel), v);
    public static void Set(IntPtr o, string sel, IntPtr v) => MsgVoidPtr(o, Sel(sel), v);
    public static void Call(IntPtr o, string sel) => MsgPtr(o, Sel(sel));
    public static void SetFrame(IntPtr win, CGRect r) => MsgVoidRectBool(win, Sel("setFrame:display:"), r, true);
}

/// <summary>CoreGraphics / CoreFoundation bits: window list, mouse, modifier keys.</summary>
public static class Quartz
{
    private const string CG = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CF = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    [DllImport(CG)] private static extern IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);
    [DllImport(CG)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CGRectMakeWithDictionaryRepresentation(IntPtr dict, out CGRect rect);
    [DllImport(CG)] private static extern IntPtr CGEventCreate(IntPtr source);
    [DllImport(CG)] private static extern CGPoint CGEventGetLocation(IntPtr ev);
    [DllImport(CG)] private static extern ulong CGEventSourceFlagsState(int state);
    [DllImport(CG)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CGEventSourceButtonState(int state, uint button);

    [DllImport(CF)] private static extern long CFArrayGetCount(IntPtr a);
    [DllImport(CF)] private static extern IntPtr CFArrayGetValueAtIndex(IntPtr a, long i);
    [DllImport(CF)] private static extern IntPtr CFDictionaryGetValue(IntPtr d, IntPtr key);
    [DllImport(CF, EntryPoint = "CFNumberGetValue")] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CFNumberGetInt(IntPtr n, long type, out int v);
    [DllImport(CF, EntryPoint = "CFNumberGetValue")] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CFNumberGetDouble(IntPtr n, long type, out double v);
    [DllImport(CF)] private static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string s, uint enc);
    [DllImport(CF)] private static extern void CFRelease(IntPtr p);

    private const uint OnScreenOnly = 1, ExcludeDesktop = 16;
    private const long NumSInt32 = 3, NumDouble = 13;
    private const ulong FlagAlt = 0x80000, FlagCmd = 0x100000;

    private static IntPtr _kBounds, _kLayer, _kNumber, _kPid, _kAlpha;
    private static readonly int OwnPid = Environment.ProcessId;

    private static IntPtr Key(ref IntPtr cache, string name)
    {
        if (cache == IntPtr.Zero) cache = CFStringCreateWithCString(IntPtr.Zero, name, 0x08000100);
        return cache;
    }

    public readonly record struct WinInfo(int Id, CGRect Bounds);

    /// <summary>Visible normal windows (layer 0) of other apps, ordered front to back, in global top-left points.</summary>
    public static List<WinInfo> VisibleWindows()
    {
        var result = new List<WinInfo>(32);
        var arr = CGWindowListCopyWindowInfo(OnScreenOnly | ExcludeDesktop, 0);
        if (arr == IntPtr.Zero) return result;
        try
        {
            long n = CFArrayGetCount(arr);
            for (long i = 0; i < n; i++)
            {
                var d = CFArrayGetValueAtIndex(arr, i);
                if (Int(d, Key(ref _kLayer, "kCGWindowLayer")) != 0) continue;
                if (Int(d, Key(ref _kPid, "kCGWindowOwnerPID")) == OwnPid) continue;
                var alphaRef = CFDictionaryGetValue(d, Key(ref _kAlpha, "kCGWindowAlpha"));
                if (alphaRef != IntPtr.Zero && CFNumberGetDouble(alphaRef, NumDouble, out var a) && a < 0.05) continue;
                var b = CFDictionaryGetValue(d, Key(ref _kBounds, "kCGWindowBounds"));
                if (b == IntPtr.Zero || !CGRectMakeWithDictionaryRepresentation(b, out var rect)) continue;
                if (rect.W < 60 || rect.H < 40) continue;
                result.Add(new WinInfo(Int(d, Key(ref _kNumber, "kCGWindowNumber")), rect));
            }
        }
        finally
        {
            CFRelease(arr);
        }
        return result;
    }

    private static int Int(IntPtr dict, IntPtr key)
    {
        var v = CFDictionaryGetValue(dict, key);
        return v != IntPtr.Zero && CFNumberGetInt(v, NumSInt32, out var r) ? r : -1;
    }

    public static CGPoint MouseLocation()
    {
        var ev = CGEventCreate(IntPtr.Zero);
        if (ev == IntPtr.Zero) return default;
        var p = CGEventGetLocation(ev);
        CFRelease(ev);
        return p;
    }

    public static bool LeftButtonDown() => CGEventSourceButtonState(0, 0);
    public static bool OptionDown() => (CGEventSourceFlagsState(0) & FlagAlt) != 0;
}

/// <summary>AppKit helpers built on the ObjC bridge.</summary>
public static class Cocoa
{
    public sealed record ScreenInfo(int Index, CGRect CocoaFrame, CGRect Cg, float MenuBarH, float DockH);

    public static List<ScreenInfo> Screens()
    {
        var list = new List<ScreenInfo>();
        var screens = ObjC.Get(ObjC.Class("NSScreen"), "screens");
        ulong n = ObjC.GetULong(screens, "count");
        double mainH = 0;
        for (ulong i = 0; i < n; i++)
        {
            var s = ObjC.GetAt(screens, "objectAtIndex:", i);
            var f = ObjC.GetRect(s, "frame");
            var v = ObjC.GetRect(s, "visibleFrame");
            if (i == 0) mainH = f.H;
            var cg = new CGRect(f.X, mainH - (f.Y + f.H), f.W, f.H);
            var menu = (float)Math.Max(0, (f.Y + f.H) - (v.Y + v.H));
            var dock = (float)Math.Max(0, v.Y - f.Y);
            list.Add(new ScreenInfo((int)i, f, cg, menu, dock));
        }
        return list;
    }

    public static void ConfigureOverlay(IntPtr win, CGRect frame, bool showInScreenshots)
    {
        ObjC.Set(win, "setOpaque:", false);
        ObjC.Set(win, "setBackgroundColor:", ObjC.Get(ObjC.Class("NSColor"), "clearColor"));
        ObjC.Set(win, "setHasShadow:", false);
        ObjC.Set(win, "setHidesOnDeactivate:", false);
        ObjC.Set(win, "setIgnoresMouseEvents:", true);
        ApplyLevel(win, showInScreenshots);
        ObjC.SetFrame(win, frame);
        ObjC.Call(win, "orderFrontRegardless");
    }

    public static void ApplyLevel(IntPtr win, bool showInScreenshots)
    {
        ObjC.Set(win, "setLevel:", 25L); // NSStatusWindowLevel: above apps and the Dock, below menus
        // canJoinAllSpaces | stationary | ignoresCycle | fullScreenAuxiliary
        ObjC.SetU(win, "setCollectionBehavior:", 1UL | 16UL | 64UL | 256UL);
        ObjC.SetU(win, "setSharingType:", showInScreenshots ? 1UL : 0UL);
    }

    public static void SetIgnoresMouse(IntPtr win, bool ignore) => ObjC.Set(win, "setIgnoresMouseEvents:", ignore);

    public static IntPtr FrontmostApp()
    {
        var ws = ObjC.Get(ObjC.Class("NSWorkspace"), "sharedWorkspace");
        var app = ObjC.Get(ws, "frontmostApplication");
        if (app == IntPtr.Zero) return IntPtr.Zero;
        if (ObjC.GetInt(app, "processIdentifier") == Environment.ProcessId) return IntPtr.Zero;
        ObjC.Call(app, "retain");
        return app;
    }

    public static void Release(IntPtr obj)
    {
        if (obj != IntPtr.Zero) ObjC.Call(obj, "release");
    }

    /// <summary>Give focus back to the app that was in front before we swallowed a click.</summary>
    public static void RestoreFront(IntPtr app)
    {
        if (app == IntPtr.Zero) return;
        var nsApp = ObjC.Get(ObjC.Class("NSApplication"), "sharedApplication");
        if (ObjC.GetBool(nsApp, "isActive"))
            ObjC.SetU(app, "activateWithOptions:", 2UL);
    }
}
