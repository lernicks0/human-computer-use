using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

internal static partial class Native
{
    internal const int RestoreMessage = 0x8000 + 37;
    [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindow(string className, string title);
    [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] internal static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] internal static extern bool ReleaseCapture();
    [DllImport("user32.dll")] internal static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr icon);
}

internal static partial class Art
{
    internal static readonly Color Purple = Color.FromArgb(171, 126, 255);
    internal static GraphicsPath Round(RectangleF rect, float radius)
    {
        float d = radius * 2;
        GraphicsPath path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
    internal static Icon MakeIcon()
    {
        using (Bitmap bitmap = new Bitmap(64, 64))
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = Round(new RectangleF(1, 1, 62, 62), 16))
            using (Brush b = new SolidBrush(Color.FromArgb(117, 66, 214))) g.FillPath(b, p);
            using (Font f = new Font("Segoe UI", 33, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Brush b = new SolidBrush(Color.White))
            using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString("H", f, b, new RectangleF(0, -1, 64, 64), sf);
            IntPtr handle = bitmap.GetHicon();
            try { using (Icon icon = Icon.FromHandle(handle)) return (Icon)icon.Clone(); }
            finally { Native.DestroyIcon(handle); }
        }
    }
}

internal sealed class HumanContext : ApplicationContext
{
    readonly CursorHalo halo;
    readonly CursorSession cursorSession;
    readonly EscapeWatcher escapeWatcher;
    readonly System.Windows.Forms.Timer timer;
    readonly ControlPill pill;
    readonly NotifyIcon tray;
    readonly Icon icon;
    readonly ContextMenuStrip menu;
    readonly ToolStripMenuItem pauseItem;
    readonly ToolStripMenuItem barItem;
    bool exiting;
    bool paused;

    internal HumanContext()
    {
        float scale;
        using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;
        pill = new ControlPill(scale);
        halo = new CursorHalo(scale);
        timer = new System.Windows.Forms.Timer { Interval = 16 };
        timer.Tick += delegate { halo.Follow(Cursor.Position); };
        MainForm = pill;
        icon = Art.MakeIcon();
        pill.Icon = icon;
        pill.ToggleRequested += Toggle;
        pill.ExitRequested += ExitThread;
        pill.RestoreRequested += Restore;
        pill.HideRequested += ToggleBar;
        menu = new ContextMenuStrip();
        var label = menu.Items.Add("Human · Computer Use");
        label.Enabled = false;
        menu.Items.Add(new ToolStripSeparator());
        pauseItem = new ToolStripMenuItem("暂停光晕", null, delegate { Toggle(); });
        barItem = new ToolStripMenuItem("显示 Human 提示条", null, delegate { ToggleBar(); }) { Checked = true };
        menu.Items.Add(pauseItem);
        menu.Items.Add(barItem);
        menu.Items.Add("提示条回到顶部", null, delegate { PositionPill(); pill.Show(); barItem.Checked = true; });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, delegate { ExitThread(); });
        tray = new NotifyIcon { Icon = icon, Text = "Human 正在使用电脑", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += delegate { Restore(); };
        cursorSession = new CursorSession();
        escapeWatcher = new EscapeWatcher(pill.Handle);
        halo.Follow(Cursor.Position);
        halo.Show();
        PositionPill();
        pill.Show();
        timer.Start();
        SystemEvents.DisplaySettingsChanged += DisplayChanged;
    }
    void DisplayChanged(object sender, EventArgs e)
    {
        if (exiting || pill.IsDisposed) return;
        try { pill.BeginInvoke(new Action(delegate { if (!exiting) PositionPill(); })); }
        catch (InvalidOperationException) { }
    }
    void PositionPill()
    {
        Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
        pill.Location = new Point(area.Left + (area.Width - pill.Width) / 2, area.Top + (int)(12 * pill.ScaleFactor));
    }
    void Toggle() { SetPaused(!paused); }
    void Restore() { PositionPill(); SetPaused(false); pill.Show(); barItem.Checked = true; }
    void ToggleBar()
    {
        if (pill.Visible) pill.Hide(); else { PositionPill(); pill.Show(); }
        barItem.Checked = pill.Visible;
    }
    void SetPaused(bool value)
    {
        paused = value;
        cursorSession.SetEnabled(!paused);
        if (paused) { timer.Stop(); halo.Hide(); }
        else { halo.Follow(Cursor.Position); halo.Show(); timer.Start(); }
        pill.SetPaused(value);
        pauseItem.Text = paused ? "恢复光晕" : "暂停光晕";
        tray.Text = paused ? "Human 效果已暂停" : "Human 正在使用电脑";
    }
    protected override void ExitThreadCore()
    {
        if (exiting) return;
        exiting = true;
        SystemEvents.DisplaySettingsChanged -= DisplayChanged;
        escapeWatcher.Dispose();
        timer.Stop(); timer.Dispose();
        cursorSession.Dispose();
        tray.Visible = false;
        tray.Dispose();
        menu.Dispose();
        halo.Dispose();
        pill.Dispose();
        icon.Dispose();
        base.ExitThreadCore();
    }
}

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Native.SetProcessDPIAware();
        if (args.Length == 3 && args[0] == "--cursor-guard")
        {
            int parentId;
            if (int.TryParse(args[1], out parentId))
            {
                try { CursorSession.RunGuard(parentId, args[2]); } catch { Environment.ExitCode = 1; }
            }
            return;
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args.Length == 2 && args[0] == "--cursor-test-child")
        {
            try { CursorVerification.Child(args[1]); } catch { Environment.ExitCode = 1; }
            return;
        }
        if (args.Length == 2 && args[0] == "--verify-session")
        {
            Directory.CreateDirectory(args[1]);
            try { CursorVerification.Run(Path.GetFullPath(args[1])); }
            catch (Exception ex) { File.WriteAllText(Path.Combine(args[1], "cursor-session-checks.txt"), "FAIL\r\n" + ex); Environment.ExitCode = 1; }
            return;
        }
        if (args.Length == 1 && args[0] == "--exit-existing")
        {
            IntPtr existing = Native.FindWindow(null, ControlPill.WindowTitle);
            if (existing != IntPtr.Zero) Native.PostMessage(existing, 0x312, new IntPtr(1), IntPtr.Zero);
            return;
        }
        if (args.Length >= 2 && args[0] == "--self-test")
        {
            try { HaloVerification.Run(Path.GetFullPath(args[1])); }
            catch (Exception ex) { File.WriteAllText(Path.Combine(args[1], "verification.txt"), "FAIL\r\n" + ex); Environment.ExitCode = 1; }
            return;
        }
        bool created;
        using (Mutex mutex = new Mutex(true, "Local\\HumanComputerUse.DesktopOverlay.1", out created))
        {
            if (!created)
            {
                IntPtr handle = Native.FindWindow(null, ControlPill.WindowTitle);
                if (handle != IntPtr.Zero) Native.PostMessage(handle, Native.RestoreMessage, IntPtr.Zero, IntPtr.Zero);
                return;
            }
            try { using (HumanContext context = new HumanContext()) Application.Run(context); }
            catch (Exception ex) { MessageBox.Show("Human 无法启动：\r\n" + ex.Message, "Human", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { mutex.ReleaseMutex(); }
        }
    }
}
