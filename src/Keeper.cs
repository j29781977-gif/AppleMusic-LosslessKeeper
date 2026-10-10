// AppleMusicLosslessKeeper
// ---------------------------------------------------------------------------
// Apple Music for Windows (AppleInc.AppleMusicWin) does not restore the
// "Lossless audio" / audio-quality preferences at start-up: whatever is set in
// Settings > Playback is back to "High Quality (AAC 256 kbps)" on the next
// launch.  The value is written through the library agent but never read back,
// and no file in the package data folder carries it (verified by byte-level
// diffs of settings.dat, the silo registry hive User.dat, Library
// Preferences.musicdb and by restoring all of them in the "lossless" state with
// every Apple process stopped - the setting still came back off).  The only
// reliable way to change it is the app's own UI, which this program drives with
// UI Automation.
//
// To stay invisible it does NOT wait for the window and then push it aside:
// a WinEvent hook fires the moment the app's window is created/shown, and the
// window is moved off-screen within a millisecond or two - before Windows ever
// composites it.  A guard keeps pushing it back while the setting is applied,
// and the position the app chose is restored afterwards.
//
// Usage:
//   AppleMusicLosslessKeeper.exe                  keep watching (autostart use)
//   AppleMusicLosslessKeeper.exe --once           fix the running instance, exit
//   AppleMusicLosslessKeeper.exe --mode=offscreen move aside only (old behaviour)
//   AppleMusicLosslessKeeper.exe --mode=minimize  minimise instead of moving
//   AppleMusicLosslessKeeper.exe --mode=direct    do not touch the window
//   AppleMusicLosslessKeeper.exe --verbose        log every step
//   AppleMusicLosslessKeeper.exe --logdir=PATH    where keeper.log is written
// ---------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Automation;

public static class Keeper
{
    const string WindowTitle = "Apple Music";
    const string WindowClass = "WinUIDesktopWin32WindowClass";
    // UI labels, one array per setting, covering the locales listed in
    // src/strings.json.  Matching is case-insensitive substring matching, so a
    // build with several locales works on all of them.
    static readonly string[] LosslessLabels  = new string[] { __LOSSLESS__ };
    static readonly string[] StreamingLabels = new string[] { __STREAMING__ };
    static readonly string[] DownloadLabels  = new string[] { __DOWNLOAD__ };
    static readonly string[] HiResLabels     = new string[] { __HIRES__ };
    // Only the streaming ("流播放") quality is managed.  The download quality
    // ("下载") is left exactly as the app left it - set this to true to manage
    // both again.  (Not a const: a const would fold the branch below and the
    // compiler would flag the dead code as a warning, which Add-Type rejects.)
    static readonly bool SetDownloadQuality = false;
    const int SW_MINIMIZE = 6, SW_RESTORE = 9;

    static string LogDir = AppDomain.CurrentDomain.BaseDirectory;
    static string BaseDir = AppDomain.CurrentDomain.BaseDirectory;
    static string logFile;
    static bool verbose;
    static string mode = "direct";

    // ---------------------------------------------------------------- Win32
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool EnumWindows(EnumProc cb, IntPtr p);
    delegate bool EnumProc(IntPtr h, IntPtr p);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr h, int cmd);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool IsIconic(IntPtr h);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool IsWindow(IntPtr h);
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr h, out RECT r);
    const uint SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
    const int HideX = -32000, HideY = -32000;

    // WinEvent hook
    delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc cb, uint pid, uint tid, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool UnhookWinEvent(IntPtr hook);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern int GetMessageW(out MSG msg, IntPtr hwnd, uint min, uint max);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool TranslateMessage(ref MSG msg);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern IntPtr DispatchMessageW(ref MSG msg);
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int x; public int y; }
    const uint EVENT_OBJECT_CREATE = 0x8000, EVENT_OBJECT_SHOW = 0x8002,
               EVENT_OBJECT_LOCATIONCHANGE = 0x800B, WINEVENT_OUTOFCONTEXT = 0x0000;
    static WinEventProc hookProc;      // keep alive
    static WinEventProc rescueProc;    // keep alive
    static IntPtr hookHandle = IntPtr.Zero;

    // shared window bookkeeping (hook thread, guard timer, fixer)
    static readonly object winLock = new object();
    static IntPtr hiddenHwnd = IntPtr.Zero;      // the window we are currently hiding
    static IntPtr settledHwnd = IntPtr.Zero;     // window that is done - never touch it again
    static int hideGen;                          // bumped every time a window is hidden
    static int savedLeft = int.MinValue, savedTop = int.MinValue;
    static volatile bool hideActive;
    static Timer guardTimer;

    // ---------------------------------------------------------------- window helpers
    static bool IsAppleMusicProcess(int pid)
    {
        try { return Process.GetProcessById(pid).ProcessName == "AppleMusic"; }
        catch { return false; }
    }
    static bool IsAppWindow(IntPtr h)
    {
        var cls = new StringBuilder(256); GetClassNameW(h, cls, 256);
        if (cls.ToString() != WindowClass) return false;
        var title = new StringBuilder(512); GetWindowTextW(h, title, 512);
        // The main window is matched by its exact title.  The package also owns a
        // hidden helper window of the same class ("WinUI Desktop") which must be
        // left alone, otherwise it steals the hide session from the real window.
        return title.ToString() == WindowTitle;
    }
    static List<KeyValuePair<IntPtr, int>> FindAppWindows()
    {
        var list = new List<KeyValuePair<IntPtr, int>>();
        EnumWindows(delegate(IntPtr h, IntPtr p)
        {
            if (!IsAppWindow(h)) return true;
            uint pid; GetWindowThreadProcessId(h, out pid);
            list.Add(new KeyValuePair<IntPtr, int>(h, (int)pid));
            return true;
        }, IntPtr.Zero);
        return list;
    }
    static void HideAside(IntPtr h)
    {
        SetWindowPos(h, IntPtr.Zero, HideX, HideY, 0, 0,
            SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }
    static void RememberRect(IntPtr h)
    {
        RECT r;
        if (!GetWindowRect(h, out r)) return;
        if (r.Left <= -20000) return;              // that is our own hiding place
        lock (winLock) { savedLeft = r.Left; savedTop = r.Top; }
    }
    // Put the window back where the app wanted it, and make sure it stays there
    // (the app sometimes re-applies a position it saved while it was hidden).
    static void RevealWindow(IntPtr h, int l, int t)
    {
        for (int i = 0; i < 5; i++)
        {
            SetWindowPos(h, IntPtr.Zero, l, t, 0, 0,
                SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            Thread.Sleep(300);
            RECT r;
            if (GetWindowRect(h, out r) && r.Left > -20000 && r.Top > -20000)
            {
                if (i > 0) Log("window revealed on attempt " + (i + 1));
                return;
            }
        }
        Log("WARNING: window is still off screen after 5 reveal attempts");
    }

    // ------------------------------------------------- stored window position
    // The app stores its window rect in the UWP settings hive.  If a session was
    // hidden (or crashed while hidden) that value can be off-screen, which would
    // make the window appear nowhere on the next start - so put it back to a sane
    // default whenever the app is not running.
    [System.Runtime.InteropServices.DllImport("advapi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    static extern int RegLoadAppKeyW(string file, out IntPtr key, uint access, uint options, uint reserved);
    [System.Runtime.InteropServices.DllImport("advapi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    static extern int RegQueryValueExW(IntPtr key, string name, IntPtr res, out uint type, byte[] data, ref uint size);
    [System.Runtime.InteropServices.DllImport("advapi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    static extern int RegSetValueExW(IntPtr key, string name, uint res, uint type, byte[] data, uint size);
    [System.Runtime.InteropServices.DllImport("advapi32.dll")]
    static extern int RegFlushKey(IntPtr key);

    static string packageDir = null;
    // The packaged app lives in %LOCALAPPDATA%\Packages\AppleInc.AppleMusicWin_<hash>.
    // The hash is part of the signed package identity and is the same on every
    // machine, but it is looked up anyway so a different Apple build still works.
    static string PackageDir()
    {
        if (packageDir != null) return packageDir;
        try
        {
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
            if (Directory.Exists(root))
            {
                var dirs = Directory.GetDirectories(root, "AppleInc.AppleMusicWin_*");
                if (dirs.Length > 0) { packageDir = dirs[0]; return packageDir; }
            }
        }
        catch { }
        packageDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Packages", "AppleInc.AppleMusicWin_nzyj5cx40ttqa");
        return packageDir;
    }

    static string SettingsHive()
    {
        return Path.Combine(PackageDir(), "Settings", "settings.dat");
    }

    static DateTime lastRepair = DateTime.MinValue;
    static DateTime lastWindowRescue = DateTime.MinValue;
    static void RepairStoredWindowPos()
    {
        if ((DateTime.Now - lastRepair).TotalSeconds < 15) return;
        lastRepair = DateTime.Now;
        try
        {
            string hive = SettingsHive();
            if (!File.Exists(hive)) return;
            IntPtr hk;
            if (RegLoadAppKeyW(hive, out hk, 0x20006, 0, 0) != 0) return;   // locked while the app runs
            var safe = new Microsoft.Win32.SafeHandles.SafeRegistryHandle(hk, true);
            var root = Microsoft.Win32.RegistryKey.FromHandle(safe);
            try
            {
                var local = root.OpenSubKey("LocalState", true);
                if (local == null) return;
                try
                {
                    IntPtr lk = local.Handle.DangerousGetHandle();
                    uint t1 = 0, s1 = 12; var b1 = new byte[12];
                    if (RegQueryValueExW(lk, "InitialWindowPositionX", IntPtr.Zero, out t1, b1, ref s1) != 0) return;
                    float x = BitConverter.ToSingle(b1, 0);
                    if (x > -20000 && x < 30000) return;                    // already sane
                    uint t2 = 0, s2 = 12; var b2 = new byte[12];
                    float y = 100;
                    if (RegQueryValueExW(lk, "InitialWindowPositionY", IntPtr.Zero, out t2, b2, ref s2) == 0)
                        y = BitConverter.ToSingle(b2, 0);
                    if (y <= -20000 || y >= 30000) y = 100;
                    byte[] ts = BitConverter.GetBytes(DateTime.Now.ToFileTime());
                    foreach (var name in new string[] { "InitialWindowPositionX", "InitialWindowPositionY" })
                    {
                        var data = new byte[12];
                        BitConverter.GetBytes(name.EndsWith("X") ? 100f : y).CopyTo(data, 0);
                        ts.CopyTo(data, 4);
                        RegSetValueExW(lk, name, 0, 100000008, data, 12);
                    }
                    // a stored "minimised" state together with an off-screen position
                    // would leave the window nowhere to come back to
                    uint t3 = 0, s3 = 12; var b3 = new byte[12];
                    if (RegQueryValueExW(lk, "InitialWindowState", IntPtr.Zero, out t3, b3, ref s3) == 0)
                    {
                        uint state = BitConverter.ToUInt32(b3, 0);
                        if (state != 0)
                        {
                            var data3 = new byte[12];
                            BitConverter.GetBytes((uint)0).CopyTo(data3, 0);
                            ts.CopyTo(data3, 4);
                            RegSetValueExW(lk, "InitialWindowState", 0, 100000004, data3, 12);
                            Log("reset stored window state " + state + " -> 0 (normal)");
                        }
                    }
                    RegFlushKey(lk);
                    Log(string.Format("repaired stored window position (was {0:0},{1:0})", x, y));
                }
                finally { local.Close(); }
            }
            finally { root.Close(); }
        }
        catch { }
    }

    static void StartGuard()
    {
        if (guardTimer != null) return;
        guardTimer = new Timer(delegate(object o)
        {
            try
            {
                if (!hideActive) return;               // only while we are hiding it
                IntPtr h;
                lock (winLock) { h = hiddenHwnd; }
                if (h == IntPtr.Zero || !IsWindow(h)) return;
                RememberRect(h);                   // keep the app's latest choice
                HideAside(h);                      // ... and keep it out of sight
            }
            catch { }
        }, null, 60, 60);
    }
    static void StopGuard()
    {
        hideActive = false;
        try { if (guardTimer != null) { guardTimer.Dispose(); guardTimer = null; } } catch { }
    }

    // ---------------------------------------------------------------- WinEvent hook
    static void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        try
        {
            if (idObject != 0 || idChild != 0 || hwnd == IntPtr.Zero) return;   // window objects only
            if (!IsAppWindow(hwnd)) return;
            if (hwnd == settledHwnd) return;         // this one is finished - leave it visible
            // the app positions its window right before showing it, so watch moves
            // too - but ignore our own parking moves to avoid an event ping-pong
            if (evt == EVENT_OBJECT_LOCATIONCHANGE)
            {
                RECT r0;
                if (GetWindowRect(hwnd, out r0) && r0.Left <= -20000) return;
            }
            lock (winLock)
            {
                if (hiddenHwnd != hwnd)
                {
                    hiddenHwnd = hwnd;
                    hideGen++;                       // a newer window takes over the guard
                    savedLeft = int.MinValue; savedTop = int.MinValue;
                }
            }
            RememberRect(hwnd);
            if (mode == "stealth" || mode == "offscreen")
            {
                hideActive = true;
                HideAside(hwnd);                   // before it is ever composited
                StartGuard();
                if (verbose) Log("window " + hwnd + " caught on " + evt.ToString("X") + " and moved aside");
            }
        }
        catch { }
    }
    static void StartHook()
    {
        var t = new Thread(delegate()
        {
            try
            {
                hookProc = OnWinEvent;
                hookHandle = SetWinEventHook(EVENT_OBJECT_CREATE, EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero,
                                             hookProc, 0, 0, WINEVENT_OUTOFCONTEXT);
                MSG msg;
                while (GetMessageW(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref msg);
                    DispatchMessageW(ref msg);
                }
            }
            catch (Exception ex) { Log("hook thread error: " + ex.Message); }
        });
        t.IsBackground = true;
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }

    // ------------------------------------------------------- off-screen rescue
    // The app itself has been seen parking its window at -21333,-21333.  Whenever
    // that happens the window is put straight back, so the user never sees it
    // disappear.  This runs on its own thread, independent of the fix loop.
    static void OnRescueEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        try
        {
            if (idObject != 0 || idChild != 0 || hwnd == IntPtr.Zero) return;
            if (!IsAppWindow(hwnd)) return;
            if (IsIconic(hwnd)) return;
            RECT r;
            if (!GetWindowRect(hwnd, out r)) return;
            if (r.Left > -20000 && r.Top > -20000)
            {
                lock (winLock) { savedLeft = r.Left; savedTop = r.Top; }
                return;
            }
            if ((DateTime.Now - lastWindowRescue).TotalMilliseconds < 250) return;
            lastWindowRescue = DateTime.Now;
            int l, t;
            lock (winLock) { l = savedLeft; t = savedTop; }
            if (l <= -20000 || l == int.MinValue) { l = 100; t = 100; }
            SetWindowPos(hwnd, IntPtr.Zero, l, t, 0, 0,
                SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            Log("window was off screen - put it back at " + l + "," + t);
        }
        catch { }
    }
    static void StartRescueHook()
    {
        var t = new Thread(delegate()
        {
            try
            {
                rescueProc = OnRescueEvent;
                SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero,
                                rescueProc, 0, 0, WINEVENT_OUTOFCONTEXT);
                MSG msg;
                while (GetMessageW(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref msg);
                    DispatchMessageW(ref msg);
                }
            }
            catch (Exception ex) { Log("rescue hook error: " + ex.Message); }
        });
        t.IsBackground = true;
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }

    // ---------------------------------------------------------------- log
    static void Log(string msg)
    {
        try
        {
            var fi = new FileInfo(logFile);
            if (fi.Exists && fi.Length > 256 * 1024) File.Delete(logFile);
            File.AppendAllText(logFile,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + msg + Environment.NewLine,
                new UTF8Encoding(false));
        }
        catch { }
    }
    static void Trace(string msg) { if (verbose) Log(msg); }

    // ---------------------------------------------------------------- UIA helpers
    static AutomationElement ById(AutomationElement root, string id)
    {
        if (root == null) return null;
        try
        {
            return root.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, id));
        }
        catch { return null; }
    }
    static bool Matches(string name, string[] labels)
    {
        if (string.IsNullOrEmpty(name) || labels == null) return false;
        foreach (var l in labels)
        {
            if (string.IsNullOrEmpty(l)) continue;
            if (name.IndexOf(l, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }
        return false;
    }
    static string LabelName(string[] labels)
    {
        return (labels != null && labels.Length > 0) ? labels[0] : "?";
    }
    // Finds a control by any of the known labels, regardless of the UI language.
    static AutomationElement ByLabel(AutomationElement root, ControlType type, string[] labels)
    {
        if (root == null) return null;
        try
        {
            var all = root.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, type));
            foreach (AutomationElement el in all)
            {
                try { if (Matches(el.Current.Name, labels)) return el; }
                catch { }
            }
        }
        catch { }
        return null;
    }
    // Structural fallbacks for UI languages we do not have labels for.
    static AutomationElement AllByClass(AutomationElement root, string className, int index)
    {
        try
        {
            var all = root.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ClassNameProperty, className));
            if (index >= 0 && index < all.Count) return all[index];
        }
        catch { }
        return null;
    }
    static AutomationElement FindToggle(AutomationElement root)
    {
        var byLabel = ByLabel(root, ControlType.Button, LosslessLabels);
        if (byLabel != null) return byLabel;
        // Playback page order: crossfade, sound enhancer, sound check, lossless
        var el = AllByClass(root, "ToggleSwitch", 3);
        if (el != null) Log("lossless switch found by position (unknown UI language)");
        return el;
    }
    static AutomationElement FindCombo(AutomationElement root, string[] labels, int fallbackIndex)
    {
        var byLabel = ByLabel(root, ControlType.ComboBox, labels);
        if (byLabel != null) return byLabel;
        if (fallbackIndex < 0) return null;
        AutomationElement el = null;
        try
        {
            var all = root.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ComboBox));
            if (fallbackIndex < all.Count) el = all[fallbackIndex];
        }
        catch { }
        if (el != null) Log("combo " + fallbackIndex + " found by position (unknown UI language)");
        return el;
    }
    static AutomationElement ByTypeName(AutomationElement root, ControlType type, string name)
    {
        if (root == null) return null;
        try
        {
            return root.FindFirst(TreeScope.Descendants,
                new AndCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, type),
                    new PropertyCondition(AutomationElement.NameProperty, name)));
        }
        catch { return null; }
    }
    static bool WaitFor(Func<bool> cond, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            try { if (cond()) return true; } catch { }
            Thread.Sleep(60);
        }
        try { return cond(); } catch { return false; }
    }
    static void Invoke(AutomationElement el)
    {
        ((InvokePattern)el.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
    }
    static string SelectedName(AutomationElement el)
    {
        try
        {
            var sel = (SelectionPattern)el.GetCurrentPattern(SelectionPattern.Pattern);
            var cur = sel.Current.GetSelection();
            if (cur == null || cur.Length == 0) return "";
            return cur[0].Current.Name;
        }
        catch { return ""; }
    }

    class Attempt
    {
        public bool Done;
        public int Attempts;
        public DateTime Last = DateTime.MinValue;
    }

    // ------------------------------------------------- library prefs file guard
    // "Library Preferences.musicdb" carries the audio-quality settings.  Apple
    // Music overwrites it with default values at start-up and only then reads it,
    // which is why the setting is always back to "off".  Keeping a known-good
    // (lossless, high-res) copy in place and read-only makes the app read the
    // right values - with no UI automation at all.
    static string prefsFile = null;
    // The music library bundle can live anywhere under the user's Music folder.
    static string PrefsFile()
    {
        if (prefsFile != null && File.Exists(prefsFile)) return prefsFile;
        try
        {
            string music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            if (Directory.Exists(music))
            {
                foreach (var d in Directory.GetDirectories(music, "*.musiclibrary"))
                {
                    string p1 = Path.Combine(d, "Library Preferences.musicdb");
                    if (File.Exists(p1)) { prefsFile = p1; return prefsFile; }
                }
                foreach (var sub in Directory.GetDirectories(music))
                {
                    foreach (var d in Directory.GetDirectories(sub, "*.musiclibrary"))
                    {
                        string p2 = Path.Combine(d, "Library Preferences.musicdb");
                        if (File.Exists(p2)) { prefsFile = p2; return prefsFile; }
                    }
                }
            }
        }
        catch { }
        prefsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            "Apple Music", "Apple Music Library.musiclibrary", "Library Preferences.musicdb");
        return prefsFile;
    }
    static string TemplateFile()
    {
        return Path.Combine(BaseDir, "good-LibraryPreferences.musicdb");
    }
    static bool SameBytes(byte[] a, byte[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }
    static string lastPrefsState = "";
    static void EnsurePrefsReady()
    {
        try
        {
            string tmpl = TemplateFile();
            string target = PrefsFile();
            if (!File.Exists(tmpl))
            {
                if (lastPrefsState != "no-template") { lastPrefsState = "no-template"; Log("prefs template missing: " + tmpl); }
                return;
            }
            if (!File.Exists(target)) return;
            byte[] want = File.ReadAllBytes(tmpl);
            byte[] cur = File.ReadAllBytes(target);
            bool wrote = false;
            if (!SameBytes(cur, want))
            {
                var fi = new FileInfo(target);
                if ((fi.Attributes & FileAttributes.ReadOnly) != 0) fi.Attributes = fi.Attributes & ~FileAttributes.ReadOnly;
                File.WriteAllBytes(target, want);
                wrote = true;
            }
            var fi2 = new FileInfo(target);
            bool ro = (fi2.Attributes & FileAttributes.ReadOnly) != 0;
            if (!ro) fi2.Attributes = fi2.Attributes | FileAttributes.ReadOnly;
            string st = wrote ? "restored + read-only" : (ro ? "already in place" : "read-only set");
            if (st != lastPrefsState) { lastPrefsState = st; Log("prefs guard: " + st); }
        }
        catch (Exception ex)
        {
            if (lastPrefsState != "error") { lastPrefsState = "error"; Log("prefs guard error: " + ex.Message); }
        }
    }

    // ---------------------------------------------------------------- the fix
    static bool FixOnce(IntPtr hwnd, out string report)
    {
        report = "";
        var sw0 = Stopwatch.StartNew();
        AutomationElement root;
        try { root = AutomationElement.FromHandle(hwnd); }
        catch (Exception ex) { report = "no UIA root: " + ex.Message; return false; }
        if (root == null) { report = "no UIA root"; return false; }

        // the app must have finished building its main UI
        if (!WaitFor(delegate { return IsWindow(hwnd) && IsAppWindow(hwnd) && (ById(root, "NavView") != null || ById(root, "Sidebar_Home") != null); }, 60000))
        {
            report = "main UI never became ready";
            return false;
        }
        // Opening the sidebar menu while the app is still loading used to cost
        // several failed attempts (and made the menu flash up again and again).
        // Waiting for the home content to be populated makes the first real
        // attempt succeed, so the menu is opened once.
        WaitFor(delegate
        {
            var grid = ById(root, "ContentGridView");
            if (grid == null) return false;
            try { return grid.FindAll(TreeScope.Children, Condition.TrueCondition).Count > 2; }
            catch { return false; }
        }, 20000);
        Trace(string.Format("timing: home content at {0} ms", sw0.ElapsedMilliseconds));

        // ... and the sidebar menu exists (the retry loop below copes with a UI
        // that is not interactive yet, so we do not gate on IsEnabled here)
        if (!WaitFor(delegate { return IsWindow(hwnd) && IsAppWindow(hwnd) && ById(root, "MoreButton") != null; }, 10000))
        {
            report = "sidebar menu button never appeared";
            return false;
        }
        Thread.Sleep(300);
        Trace(string.Format("timing: sidebar ready at {0} ms", sw0.ElapsedMilliseconds));

        // remember which page the user was on so the view can be restored
        string backId = null;
        try
        {
            var items = root.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.IsSelectionItemPatternAvailableProperty, true));
            foreach (AutomationElement it in items)
            {
                var c = it.Current;
                if (c.AutomationId != null && c.AutomationId.StartsWith("Sidebar_"))
                {
                    if (((SelectionItemPattern)it.GetCurrentPattern(SelectionItemPattern.Pattern)).Current.IsSelected)
                    { backId = c.AutomationId; break; }
                }
            }
        }
        catch { }
        if (string.IsNullOrEmpty(backId)) backId = "Sidebar_Home";
        Trace("return page: " + backId);

        // --- keep the window out of sight while we navigate -------------------
        IntPtr hp = hwnd;
        bool moved = false, minimized = false;
        int myGen;
        lock (winLock)
        {
            if (hiddenHwnd == IntPtr.Zero) { hiddenHwnd = hp; hideGen++; }
            myGen = hideGen;
        }
        if (mode == "stealth" || mode == "offscreen")
        {
            RememberRect(hp);
            HideAside(hp);
            moved = true;
            if (mode == "stealth") { hideActive = true; StartGuard(); }
            Thread.Sleep(150);
        }
        else if (mode == "minimize")
        {
            minimized = !IsIconic(hp);
            if (minimized) { ShowWindow(hp, SW_MINIMIZE); Thread.Sleep(250); }
        }

        bool ok = false;
        string detail = "";
        try
        {
        // The app is still settling right after launch, so the whole navigation
        // is retried while the window stays out of the way.
        for (int attempt = 1; attempt <= 40 && !ok; attempt++)
        {
            if (attempt > 1)
            {
                try
                {
                    var b0 = ById(root, backId);
                    if (b0 != null)
                        ((SelectionItemPattern)b0.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
                }
                catch { }
                Thread.Sleep(250);
                HideAside(hp);
            }
            try
            {
            // 1. open Settings from the sidebar-actions menu
            var more = ById(root, "MoreButton");
            if (more == null) detail = "MoreButton not found";
            else
            {
                Invoke(more);
                var setting = WaitFor(delegate { return ById(root, "SettingMenu") != null; }, 5000)
                    ? ById(root, "SettingMenu") : null;
                Trace(string.Format("timing: settings menu at {0} ms", sw0.ElapsedMilliseconds));
                if (setting == null) detail = "Settings menu item not found";
                else
                {
                    Invoke(setting);
                    if (!WaitFor(delegate { return ById(root, "SettingsNavView") != null; }, 10000))
                        detail = "Settings pane did not open";
                    else
                    {
                        var playTxt = ById(root, "PlaybackNavigationViewItem");
                        if (playTxt == null) detail = "Playback section not found";
                        else
                        {
                            AutomationElement btn = playTxt;
                            var cw = TreeWalker.ControlViewWalker;
                            for (int i = 0; i < 6 && btn != null; i++)
                            {
                                btn = cw.GetParent(btn);
                                if (btn != null && btn.Current.ControlType == ControlType.Button) break;
                            }
                            if (btn != null) Invoke(btn);
                            WaitFor(delegate { return FindToggle(root) != null; }, 8000);

                            Trace(string.Format("timing: playback page at {0} ms", sw0.ElapsedMilliseconds));
                            var toggle = FindToggle(root);
                            if (toggle == null) detail = "lossless toggle not found";
                            else
                            {
                                var tp = (TogglePattern)toggle.GetCurrentPattern(TogglePattern.Pattern);
                                string before = tp.Current.ToggleState.ToString();
                                if (tp.Current.ToggleState != ToggleState.On)
                                {
                                    tp.Toggle();
                                    Thread.Sleep(400);
                                }
                                string after = ((TogglePattern)toggle.GetCurrentPattern(TogglePattern.Pattern))
                                                   .Current.ToggleState.ToString();

                                var combos = SetDownloadQuality
                                    ? new string[][] { StreamingLabels, DownloadLabels }
                                    : new string[][] { StreamingLabels };
                                var changed = new List<string>();
                                int comboIndex = 0;
                                foreach (var cn in combos)
                                {
                                    // without a label match: first combo below the switch is
                                    // streaming quality, the next one is download quality
                                    var box = FindCombo(root, cn, comboIndex++);
                                    if (box == null) { changed.Add(LabelName(cn) + "=absent"); continue; }
                                    if (Matches(SelectedName(box), HiResLabels)) { changed.Add(LabelName(cn) + "=already"); continue; }
                                    var ec = (ExpandCollapsePattern)box.GetCurrentPattern(ExpandCollapsePattern.Pattern);
                                    ec.Expand();
                                    Thread.Sleep(250);
                                    AutomationElement want = null;
                                    var list = box.FindAll(TreeScope.Descendants,
                                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
                                    foreach (AutomationElement li in list)
                                        if (li.Current.Name != null && Matches(li.Current.Name, HiResLabels)) { want = li; break; }
                                    if (want == null)
                                    {
                                        // highest quality is the last entry of the dropdown
                                        var inBox = box.FindAll(TreeScope.Descendants,
                                            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
                                        if (inBox.Count > 0) want = inBox[inBox.Count - 1];
                                    }
                                    if (want == null)
                                    {
                                        list = root.FindAll(TreeScope.Descendants,
                                            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
                                        foreach (AutomationElement li in list)
                                            if (li.Current.Name != null && Matches(li.Current.Name, HiResLabels)) { want = li; break; }
                                    }
                                    if (want != null)
                                    {
                                        ((SelectionItemPattern)want.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
                                        Thread.Sleep(200);
                                        changed.Add(LabelName(cn) + "=set");
                                    }
                                    else changed.Add(LabelName(cn) + "=no-option");
                                    try { ec.Collapse(); } catch { }
                                }
                                detail = string.Format("lossless {0}->{1}; {2}", before, after, string.Join(", ", changed.ToArray()));
                                ok = (after == "On");
                            }
                        }
                    }
                }
            }
            }
            catch (Exception exInner)
            {
                detail = string.Format(" attempt {0}: {1}: {2} (0x{3:X8})",
                    attempt, exInner.GetType().Name, exInner.Message, exInner.HResult);
                Thread.Sleep(500);
            }
        }   // end of the retry loop

            // 5. leave the settings page again
            try
            {
                var back = ById(root, backId);
                if (back != null)
                {
                    ((SelectionItemPattern)back.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
                    Thread.Sleep(250);
        Trace(string.Format("timing: sidebar ready at {0} ms", sw0.ElapsedMilliseconds));
                }
            }
            catch { }
        }
        catch (Exception ex)
        {
            detail += string.Format(" exception {0}: {1} (0x{2:X8})",
                ex.GetType().Name, ex.Message, ex.HResult);
        }
        finally
        {
            // If a newer window took the guard over meanwhile, leave it alone:
            // that window's own fix will reveal itself when it is done.
            bool mine;
            lock (winLock) { mine = (myGen == hideGen); }
            if (mine) { StopGuard(); settledHwnd = hp; }   // stop hiding this window for good
            Thread.Sleep(150);
            if (moved && mine)
            {
                int l, t;
                lock (winLock) { l = savedLeft; t = savedTop; }
                if (l == int.MinValue || t == int.MinValue || l == HideX || t == HideY)
                {
                    RECT r2;
                    if (GetWindowRect(hp, out r2) && r2.Left > -20000) { l = r2.Left; t = r2.Top; }
                    else { l = 100; t = 100; }
                }
                try { RevealWindow(hp, l, t); }
                catch { }
            }
            else if (minimized)
            {
                try { ShowWindow(hp, SW_RESTORE); } catch { }
            }
        }

        int fl, ft;
        lock (winLock) { fl = savedLeft; ft = savedTop; }
        report = detail + string.Format(" | window was at {0},{1} when we took over", fl, ft);
        return ok;
    }

    // -------------------------------------------------------- public entry points
    // Also callable from PowerShell (which has full UI Automation reach into the
    // packaged app): [Keeper]::FixNow()
    public static string FixNow()
    {
        var wins = FindAppWindows();
        if (wins.Count == 0) return "no Apple Music window";
        string report;
        bool ok = FixOnce(wins[0].Key, out report);
        return (ok ? "ok: " : "failed: ") + report;
    }

    public static string Version() { return "AppleMusicLosslessKeeper 1.2.0"; }

    // ---------------------------------------------------------------- main
    public static int Main(string[] args)
    {
        bool once = false, noMutex = false;
        foreach (var a in args)
        {
            if (a == "--once") once = true;
            else if (a == "--verbose") verbose = true;
            else if (a == "--no-mutex") noMutex = true;
            else if (a.StartsWith("--mode=")) mode = a.Substring(7);
            else if (a.StartsWith("--logdir=")) LogDir = a.Substring(9);
            else if (a.StartsWith("--dir=")) BaseDir = a.Substring(6);
        }
        logFile = Path.Combine(LogDir, "keeper.log");
        try
        {
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                Log("UNHANDLED: " + (e.ExceptionObject == null ? "?" : e.ExceptionObject.ToString()));
            };
        }
        catch { }
        Log(string.Format("main entered (version {0}, mode={1}, once={2}, log={3})",
            Version(), mode, once, logFile));

        try
        {
            bool created = true;
            if (!noMutex)
            {
                try { new Mutex(true, "Global\\AppleMusicLosslessKeeper", out created); }
                catch (Exception ex) { Log("mutex unavailable (" + ex.Message + "); continuing without it"); created = true; }
            }
            if (!created) { Log("another instance is already running; exiting"); return 0; }

            RepairStoredWindowPos();
            if (mode == "file")
            {
                Log("watching for Apple Music (file mode - no window is touched)");
            }
            else
            {
                if (mode == "stealth" || mode == "offscreen") StartHook(); else StartRescueHook();
                Log("watching for Apple Music (UI automation mode: " + mode + ")");
            }

            var state = new Dictionary<int, Attempt>();
            while (true)
            {
                try
                {
                    var wins = FindAppWindows();
                    Trace(string.Format("tick: {0} Apple Music window(s)", wins.Count));
                    // The app itself sometimes parks its window off screen (it has been
                    // seen at -21333,-21333 after a hidden session).  Never leave it
                    // there: put it back where it was last seen on screen.
                    if (mode != "stealth" && mode != "offscreen")
                    {
                        foreach (var w in wins)
                        {
                            if (IsIconic(w.Key)) continue;
                            RECT rr;
                            if (!GetWindowRect(w.Key, out rr)) continue;
                            if (rr.Left > -20000 && rr.Top > -20000)
                            {
                                lock (winLock) { savedLeft = rr.Left; savedTop = rr.Top; }
                            }
                            else if ((DateTime.Now - lastWindowRescue).TotalSeconds > 1.0)
                            {
                                lastWindowRescue = DateTime.Now;
                                int rl, rt;
                                lock (winLock) { rl = savedLeft; rt = savedTop; }
                                if (rl <= -20000 || rl == int.MinValue) { rl = 100; rt = 100; }
                                SetWindowPos(w.Key, IntPtr.Zero, rl, rt, 0, 0,
                                    SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                                Log("window was off screen - put it back at " + rl + "," + rt);
                            }
                        }
                    }
                    if (mode == "file")
                    {
                        // The app rewrites its library preferences with default values
                        // right before it reads them back, so the only way for it to
                        // start up lossless is to have the known-good file in place and
                        // write-protected at that moment.  Nothing else is touched - the
                        // window appears at normal speed.
                        if (wins.Count == 0)
                        {
                            RepairStoredWindowPos();
                            EnsurePrefsReady();
                        }
                        if (once) break;
                        Thread.Sleep(1500);
                        continue;
                    }
                    foreach (var w in wins)
                    {
                        int pid = w.Value;
                        if (!IsWindow(w.Key)) continue;
                        Attempt st;
                        if (!state.TryGetValue(pid, out st)) { st = new Attempt(); state[pid] = st; }
                        if (st.Done) continue;
                        if (st.Attempts >= 12) continue;   // give up on this session
                        if (st.Last != DateTime.MinValue &&
                            (DateTime.Now - st.Last).TotalSeconds < 2.5) continue;

                        st.Attempts++;
                        st.Last = DateTime.Now;
                        lock (winLock) { if (hiddenHwnd == IntPtr.Zero) { hiddenHwnd = w.Key; hideGen++; } }
                        string report;
                        bool ok = FixOnce(w.Key, out report);
                        if (ok)
                        {
                            st.Done = true;
                            Log(string.Format("pid {0}: lossless enabled ({1})", pid, report));
                        }
                        else
                        {
                            Log(string.Format("pid {0}: attempt {1} failed ({2})", pid, st.Attempts, report));
                        }
                    }
                    // forget windows/processes that are gone
                    var dead = new List<int>();
                    foreach (var kv in state)
                    {
                        bool alive = false;
                        foreach (var w in wins) if (w.Value == kv.Key) { alive = true; break; }
                        if (!alive) dead.Add(kv.Key);
                    }
                    foreach (var d in dead) state.Remove(d);
                    if (state.Count == 0) { lock (winLock) { hiddenHwnd = IntPtr.Zero; settledHwnd = IntPtr.Zero; } }
                }
                catch (Exception ex) { Log("loop error: " + ex); }

                if (once) break;
                Thread.Sleep(60);
            }
            Log("exiting");
            if (hookHandle != IntPtr.Zero) UnhookWinEvent(hookHandle);
        }
        catch (Exception ex)
        {
            Log("FATAL: " + ex.ToString());
            return 1;
        }
        return 0;
    }
}
