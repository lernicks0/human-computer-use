using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

internal static partial class Native
{
    internal const int CancelMessage = 0x8000 + 38;
    [StructLayout(LayoutKind.Sequential)] internal struct IconInfo
    {
        [MarshalAs(UnmanagedType.Bool)] internal bool IsIcon;
        internal uint HotX, HotY;
        internal IntPtr Mask, Color;
    }
    internal delegate IntPtr KeyboardCallback(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr LoadCursor(IntPtr instance, IntPtr name);
    [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr CopyImage(IntPtr source, uint type, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool GetIconInfo(IntPtr icon, out IconInfo info);
    [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr CreateIconIndirect(ref IconInfo info);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetSystemCursor(IntPtr cursor, uint id);
    [DllImport("user32.dll")] internal static extern bool DestroyCursor(IntPtr cursor);
    [DllImport("user32.dll")] internal static extern bool SystemParametersInfo(uint action, uint param, IntPtr data, uint flags);
    [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetWindowsHookEx(int id, KeyboardCallback callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr GetModuleHandle(string name);
}

internal sealed class EscapeWatcher : IDisposable
{
    readonly Native.KeyboardCallback callback;
    readonly IntPtr target;
    IntPtr hook;
    internal EscapeWatcher(IntPtr window)
    {
        target = window;
        callback = OnKey;
        hook = Native.SetWindowsHookEx(13, callback, Native.GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法启用 Esc 取消快捷键。");
    }
    IntPtr OnKey(int code, IntPtr message, IntPtr data)
    {
        // Read only the key code to recognize Escape; never store key input.
        if (code >= 0 && (message.ToInt32() == 0x100 || message.ToInt32() == 0x104) && Marshal.ReadInt32(data) == 0x1B)
        {
            Native.PostMessage(target, Native.CancelMessage, IntPtr.Zero, IntPtr.Zero);
            return new IntPtr(1);
        }
        return Native.CallNextHookEx(hook, code, message, data);
    }
    public void Dispose()
    {
        if (hook != IntPtr.Zero) { Native.UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
        GC.KeepAlive(callback);
    }
}

internal static partial class Art
{
    internal static void Pointer(Graphics g, float x, float y, float size)
    {
        GraphicsState saved = g.Save();
        g.TranslateTransform(x, y); g.ScaleTransform(size, size); g.SmoothingMode = SmoothingMode.AntiAlias;
        using (GraphicsPath p = new GraphicsPath())
        {
            p.AddPolygon(new PointF[] { new PointF(0, 0), new PointF(15, 5), new PointF(7, 8), new PointF(4, 16) });
            using (Pen outline = new Pen(Color.White, 2.2f) { LineJoin = LineJoin.Round })
            {
                g.DrawPath(outline, p); g.FillPath(Brushes.Black, p);
            }
        }
        g.Restore(saved);
    }
    internal static IntPtr LargeArrow(int canvasSize)
    {
        using (Bitmap bitmap = new Bitmap(canvasSize, canvasSize, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            float scale = canvasSize / 32f;
            Pointer(g, 2 * scale, 2 * scale, scale);
            IntPtr icon = bitmap.GetHicon();
            Native.IconInfo info;
            try
            {
                if (!Native.GetIconInfo(icon, out info)) throw new Win32Exception(Marshal.GetLastWin32Error());
                try
                {
                    info.IsIcon = false;
                    info.HotX = (uint)Math.Round(2 * scale); info.HotY = (uint)Math.Round(2 * scale);
                    IntPtr cursor = Native.CreateIconIndirect(ref info);
                    if (cursor == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                    return cursor;
                }
                finally { Native.DeleteObject(info.Mask); Native.DeleteObject(info.Color); }
            }
            finally { Native.DestroyIcon(icon); }
        }
    }
    internal static Size CursorSize(IntPtr cursor)
    {
        Native.IconInfo info;
        if (!Native.GetIconInfo(cursor, out info)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            using (Bitmap bitmap = Image.FromHbitmap(info.Color == IntPtr.Zero ? info.Mask : info.Color))
                return new Size(bitmap.Width, info.Color == IntPtr.Zero ? bitmap.Height / 2 : bitmap.Height);
        }
        finally { Native.DeleteObject(info.Mask); Native.DeleteObject(info.Color); }
    }
}

// The child process owns cursor backups. If the UI exits or is terminated,
// the child restores them independently. Nothing is written to cursor settings.
internal sealed class CursorSession : IDisposable
{
    readonly EventWaitHandle ready, enable, disable, stop, error;
    readonly Process guard;
    bool disposed;
    internal CursorSession()
    {
        string token = Guid.NewGuid().ToString("N");
        string prefix = "Local\\HumanCursor." + token + ".";
        ready = new EventWaitHandle(false, EventResetMode.ManualReset, prefix + "Ready");
        enable = new EventWaitHandle(true, EventResetMode.ManualReset, prefix + "Enable");
        disable = new EventWaitHandle(false, EventResetMode.AutoReset, prefix + "Disable");
        stop = new EventWaitHandle(false, EventResetMode.AutoReset, prefix + "Stop");
        error = new EventWaitHandle(false, EventResetMode.ManualReset, prefix + "Error");
        try
        {
            guard = Process.Start(new ProcessStartInfo(Application.ExecutablePath,
                "--cursor-guard " + Process.GetCurrentProcess().Id + " " + token)
                { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
            if (!ready.WaitOne(5000) || error.WaitOne(0) || guard.HasExited)
                throw new InvalidOperationException("无法临时放大鼠标，原鼠标已保留或恢复。");
        }
        catch { Dispose(); throw; }
    }
    internal void SetEnabled(bool active)
    {
        if (disposed) return;
        if (active) enable.Set(); else enable.Reset();
        disable.Set(); // Wake the guardian; the manual-reset event holds the latest desired state.
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        stop.Set();
        if (guard != null) { guard.WaitForExit(2000); guard.Dispose(); }
        ready.Dispose(); enable.Dispose(); disable.Dispose(); stop.Dispose(); error.Dispose();
    }
    internal static void RunGuard(int parentId, string token)
    {
        Guid parsed;
        if (!Guid.TryParseExact(token, "N", out parsed)) return;
        string prefix = "Local\\HumanCursor." + token + ".";
        using (EventWaitHandle ready = EventWaitHandle.OpenExisting(prefix + "Ready"))
        using (EventWaitHandle enable = EventWaitHandle.OpenExisting(prefix + "Enable"))
        using (EventWaitHandle disable = EventWaitHandle.OpenExisting(prefix + "Disable"))
        using (EventWaitHandle stop = EventWaitHandle.OpenExisting(prefix + "Stop"))
        using (EventWaitHandle error = EventWaitHandle.OpenExisting(prefix + "Error"))
        {
            try
            {
                using (Process parent = Process.GetProcessById(parentId))
                using (CursorBackups cursors = new CursorBackups())
                {
                    if (parent.HasExited || stop.WaitOne(0)) return;
                    cursors.Apply(); ready.Set();
                    while (!parent.HasExited)
                    {
                        int signal = WaitHandle.WaitAny(new WaitHandle[] { stop, disable }, 80);
                        if (signal == 0) break;
                        if (signal == 1) { if (enable.WaitOne(0)) cursors.Apply(); else cursors.Restore(); }
                    }
                }
            }
            catch { error.Set(); }
            finally { ready.Set(); }
        }
    }
}

internal sealed class CursorBackups : IDisposable
{
    sealed class Entry { internal uint Id; internal IntPtr Original, Enlarged; internal bool Changed; }
    readonly List<Entry> cursors = new List<Entry>();
    internal CursorBackups()
    {
        try
        {
            foreach (uint id in new uint[] { 32512, 32513, 32514, 32515, 32516, 32642, 32643, 32644, 32645, 32646, 32648, 32649, 32650 })
            {
                IntPtr source = Native.LoadCursor(IntPtr.Zero, new IntPtr(id));
                if (source == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                Entry entry = new Entry { Id = id, Original = Copy(source) };
                cursors.Add(entry);
                Size size = Art.CursorSize(source);
                int width = (int)Math.Round(size.Width * 1.5), height = (int)Math.Round(size.Height * 1.5);
                entry.Enlarged = id == 32512 ? Art.LargeArrow(width) : Native.CopyImage(source, 2, width, height, 0);
                if (entry.Enlarged == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        catch { Dispose(); throw; }
    }
    static IntPtr Copy(IntPtr cursor)
    {
        IntPtr copy = Native.CopyImage(cursor, 2, 0, 0, 0);
        if (copy == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        return copy;
    }
    internal void Apply()
    {
        try
        {
            foreach (Entry entry in cursors)
            {
                if (entry.Changed) continue;
                IntPtr copy = Copy(entry.Enlarged);
                if (!Native.SetSystemCursor(copy, entry.Id)) { Native.DestroyCursor(copy); throw new Win32Exception(Marshal.GetLastWin32Error()); }
                entry.Changed = true;
            }
        }
        catch { Restore(); throw; }
    }
    internal void Restore()
    {
        bool failed = false;
        foreach (Entry entry in cursors)
        {
            if (!entry.Changed) continue;
            IntPtr copy = Native.CopyImage(entry.Original, 2, 0, 0, 0);
            if (copy == IntPtr.Zero || !Native.SetSystemCursor(copy, entry.Id)) { if (copy != IntPtr.Zero) Native.DestroyCursor(copy); failed = true; }
            entry.Changed = false;
        }
        if (failed) Native.SystemParametersInfo(0x0057, 0, IntPtr.Zero, 0); // Reload saved scheme if restoring an individual backup fails.
    }
    public void Dispose()
    {
        Restore();
        foreach (Entry entry in cursors) { if (entry.Original != IntPtr.Zero) Native.DestroyCursor(entry.Original); if (entry.Enlarged != IntPtr.Zero) Native.DestroyCursor(entry.Enlarged); }
        cursors.Clear();
    }
}

internal static class CursorVerification
{
    static string Fingerprint()
    {
        System.Text.StringBuilder result = new System.Text.StringBuilder();
        foreach (uint id in new uint[] { 32512, 32513, 32514, 32515, 32516, 32642, 32643, 32644, 32645, 32646, 32648, 32649, 32650 })
        {
            Native.IconInfo info;
            if (!Native.GetIconInfo(Native.LoadCursor(IntPtr.Zero, new IntPtr(id)), out info)) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                result.Append(id).Append(':').Append(info.HotX).Append(',').Append(info.HotY);
                foreach (IntPtr handle in new IntPtr[] { info.Mask, info.Color })
                {
                    if (handle == IntPtr.Zero) continue;
                    using (Bitmap bitmap = Image.FromHbitmap(handle))
                    using (MemoryStream stream = new MemoryStream())
                    using (SHA256 hash = SHA256.Create())
                    {
                        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                        result.Append(Convert.ToBase64String(hash.ComputeHash(stream.ToArray())));
                    }
                }
            }
            finally { Native.DeleteObject(info.Mask); Native.DeleteObject(info.Color); }
        }
        return result.ToString();
    }
    static void Require(bool passed, string label, List<string> results)
    {
        if (!passed) throw new InvalidOperationException(label);
        results.Add("PASS  " + label);
    }
    internal static void Child(string marker)
    {
        using (CursorSession session = new CursorSession())
        {
            File.WriteAllText(marker, "ready");
            Thread.Sleep(30000); // The verifier terminates this owned child to exercise crash recovery.
        }
    }
    internal static void Run(string output)
    {
        Directory.CreateDirectory(output);
        List<string> results = new List<string>();
        string before = Fingerprint();
        Size originalSize = Art.CursorSize(Native.LoadCursor(IntPtr.Zero, new IntPtr(32512)));
        using (CursorSession session = new CursorSession())
        {
            Size enlarged = Art.CursorSize(Native.LoadCursor(IntPtr.Zero, new IntPtr(32512)));
            Require(enlarged.Width == (int)Math.Round(originalSize.Width * 1.5), "System cursor actually enlarged to 150%", results);
            session.SetEnabled(false);
            Require(SpinWait.SpinUntil(delegate { return Fingerprint() == before; }, 3000), "Pausing restores all 13 cursors and hotspots", results);
            session.SetEnabled(true);
            Require(SpinWait.SpinUntil(delegate { return Art.CursorSize(Native.LoadCursor(IntPtr.Zero, new IntPtr(32512))).Width == enlarged.Width; }, 3000), "Resuming reapplies enlarged cursor", results);
        }
        Require(Fingerprint() == before, "Normal exit restores every original cursor", results);
        string marker = Path.Combine(output, "cursor-child-" + Guid.NewGuid().ToString("N") + ".txt");
        using (Process child = Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--cursor-test-child \"" + marker + "\"")
            { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden }))
        {
            try
            {
                Require(SpinWait.SpinUntil(delegate { return File.Exists(marker) || child.HasExited; }, 6000) && File.Exists(marker), "Recovery guardian starts for owned test process", results);
                child.Kill(); child.WaitForExit(2000);
                Require(SpinWait.SpinUntil(delegate { return Fingerprint() == before; }, 3000), "Guardian restores every cursor after UI process termination", results);
            }
            finally { if (!child.HasExited) { child.Kill(); child.WaitForExit(2000); } }
        }
        File.WriteAllLines(Path.Combine(output, "cursor-session-checks.txt"), results.ToArray());
    }
}
