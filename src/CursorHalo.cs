using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static partial class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct XY { internal int X, Y; internal XY(int x, int y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential)] internal struct WH { internal int Width, Height; internal WH(int w, int h) { Width = w; Height = h; } }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] internal struct Blend { internal byte Operation, Flags, Alpha, Format; }
    [DllImport("user32.dll")] internal static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] internal static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] internal static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] internal static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] internal static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dc, ref XY destination, ref WH size, IntPtr sourceDC, ref XY source, uint key, ref Blend blend, uint flags);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
}

internal static partial class Art
{
    // Calibrated against the user's 2559 x 1439 screenshot, in physical pixels.
    internal static readonly Color HaloColor = Color.FromArgb(115, 124, 214);
    internal static Bitmap Halo(float scale)
    {
        int diameter = (int)Math.Ceiling(144 * scale);
        if ((diameter & 1) != 0) diameter++;
        Bitmap image = new Bitmap(diameter, diameter, PixelFormat.Format32bppPArgb);
        BitmapData data = image.LockBits(new Rectangle(0, 0, diameter, diameter), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        try
        {
            byte[] pixels = new byte[data.Stride * diameter];
            double sigma = 22 * scale, center = diameter / 2.0;
            for (int y = 0; y < diameter; y++)
            for (int x = 0; x < diameter; x++)
            {
                double dx = x - center, dy = y - center;
                double r = Math.Sqrt(dx * dx + dy * dy);
                double taper = Math.Min(1, Math.Max(0, (center - r) / (8 * scale)));
                int a = (int)Math.Round(255 * 0.46 * Math.Exp(-(dx * dx + dy * dy) / (2 * sigma * sigma)) * taper);
                int i = y * data.Stride + x * 4;
                pixels[i] = (byte)((HaloColor.B * a + 127) / 255);
                pixels[i + 1] = (byte)((HaloColor.G * a + 127) / 255);
                pixels[i + 2] = (byte)((HaloColor.R * a + 127) / 255);
                pixels[i + 3] = (byte)a;
            }
            Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        }
        finally { image.UnlockBits(data); }
        return image;
    }
}

internal sealed class CursorHalo : Form
{
    readonly float scale;
    IntPtr surfaceDC, surfaceBitmap, oldBitmap;
    internal Point LastCursor { get; private set; }
    internal int MoveCount;
    internal bool Uploaded;
    internal CursorHalo(float scaleFactor)
    {
        scale = scaleFactor;
        Text = "Human · Cursor Halo";
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        AccessibleName = "Human 鼠标光晕";
        using (Bitmap image = Art.Halo(scale))
        {
            Size = image.Size;
            surfaceDC = Native.CreateCompatibleDC(IntPtr.Zero);
            surfaceBitmap = image.GetHbitmap(Color.FromArgb(0));
            if (surfaceDC == IntPtr.Zero || surfaceBitmap == IntPtr.Zero) throw new Win32Exception("无法创建光晕图层。");
            oldBitmap = Native.SelectObject(surfaceDC, surfaceBitmap);
        }
    }
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= 0x00080000 | 0x00000020 | 0x08000000 | 0x00000080;
            return cp;
        }
    }
    internal Point AtCursor(Point cursor)
    {
        return new Point(cursor.X + (int)Math.Round(5 * scale) - Width / 2,
                         cursor.Y + (int)Math.Round(8 * scale) - Height / 2);
    }
    internal void Follow(Point cursor)
    {
        Point location = AtCursor(cursor);
        if (!Uploaded)
        {
            IntPtr screenDC = Native.GetDC(IntPtr.Zero);
            try
            {
                Native.XY destination = new Native.XY(location.X, location.Y), origin = new Native.XY(0, 0);
                Native.WH size = new Native.WH(Width, Height);
                Native.Blend blend = new Native.Blend { Operation = 0, Flags = 0, Alpha = 255, Format = 1 };
                if (!Native.UpdateLayeredWindow(Handle, screenDC, ref destination, ref size, surfaceDC, ref origin, 0, ref blend, 2))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                Uploaded = true;
            }
            finally { Native.ReleaseDC(IntPtr.Zero, screenDC); }
        }
        else if (cursor == LastCursor) return;
        if (!Native.SetWindowPos(Handle, new IntPtr(-1), location.X, location.Y, Width, Height, 0x0010 | 0x0200))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        // Layered-window positioning does not always update WinForms' cached
        // bounds before first Show(); keep them in sync to avoid a jump to 0,0.
        UpdateBounds(location.X, location.Y, Width, Height);
        LastCursor = cursor;
        MoveCount++;
    }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) { }
    protected override void OnHandleDestroyed(EventArgs e) { Uploaded = false; base.OnHandleDestroyed(e); }
    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x84) { message.Result = new IntPtr(-1); return; }
        if (message.Msg == 0x21) { message.Result = new IntPtr(3); return; }
        base.WndProc(ref message);
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (surfaceDC != IntPtr.Zero)
        {
            Native.SelectObject(surfaceDC, oldBitmap);
            Native.DeleteObject(surfaceBitmap);
            Native.DeleteDC(surfaceDC);
            surfaceDC = surfaceBitmap = oldBitmap = IntPtr.Zero;
        }
    }
}

internal static class HaloVerification
{
    static void Check(bool value, string label, List<string> results)
    {
        if (!value) throw new InvalidOperationException(label);
        results.Add("PASS  " + label);
    }
    internal static void Run(string output)
    {
        Directory.CreateDirectory(output);
        List<string> results = new List<string>();
        using (Icon icon = Art.MakeIcon())
        using (FileStream file = File.Create(Path.Combine(output, "Human.ico"))) icon.Save(file);
        using (Bitmap halo = Art.Halo(1))
        {
            halo.Save(Path.Combine(output, "halo.png"), ImageFormat.Png);
            Check(halo.GetPixel(72, 72).A == 117, "Measured 46% center opacity", results);
            Check(halo.GetPixel(0, 72).A == 0 && halo.GetPixel(0, 0).A == 0, "Fully transparent outer edges", results);
            Check(halo.GetPixel(87, 72).A > halo.GetPixel(102, 72).A && halo.GetPixel(102, 72).A > halo.GetPixel(122, 72).A, "Smooth Gaussian falloff", results);
        }
        using (CursorHalo halo = new CursorHalo(1))
        {
            int style = Native.GetWindowLong(halo.Handle, -20);
            Check((style & 0x80020) == 0x80020 && (style & 0x08000000) != 0, "Layered click-through surface without focus stealing", results);
            halo.Follow(new Point(400, 300));
            Check(halo.Uploaded && halo.Location == halo.AtCursor(new Point(400, 300)), "Native alpha surface uploads and follows coordinates; actual=" + halo.Location + "; expected=" + halo.AtCursor(new Point(400, 300)) + "; uploaded=" + halo.Uploaded, results);
            int moves = halo.MoveCount;
            halo.Follow(new Point(400, 300));
            Check(halo.MoveCount == moves, "Stationary pointer avoids redundant surface updates", results);
            halo.Follow(new Point(-100, 25));
            Check(halo.Location.X < 0 && halo.LastCursor.X == -100, "Negative coordinates for additional monitors", results);
        }
        foreach (float scale in new float[] { 1, 1.25f, 1.5f, 2 })
        using (ControlPill pill = new ControlPill(scale))
        {
            IntPtr hwnd = pill.Handle;
            Check(pill.Controls.Count == 1 && pill.Controls[0].Right <= pill.Width, "English banner fits at scale " + scale, results);
            Check(pill.BackColor == ControlPill.BannerBlue && pill.Controls[0].ForeColor == Color.White && pill.Controls[0].Font.Bold,
                "Blue background, white text, bold Esc to cancel at scale " + scale, results);
            Check(pill.AccessibleName == "Human is using your computer · Esc to cancel", "Exact status text at scale " + scale, results);
            bool toggled = false, hidden = false, exited = false, restored = false;
            pill.ToggleRequested += delegate { toggled = true; };
            pill.HideRequested += delegate { hidden = true; };
            pill.ExitRequested += delegate { exited = true; };
            pill.RestoreRequested += delegate { restored = true; };
            for (int i = 1; i <= 3; i++) Native.SendMessage(hwnd, 0x312, new IntPtr(i), IntPtr.Zero);
            Native.SendMessage(hwnd, Native.RestoreMessage, IntPtr.Zero, IntPtr.Zero);
            Check(toggled && hidden && exited && restored, "Pause / hide / exit / restore message routes at scale " + scale, results);
            exited = false;
            Native.SendMessage(hwnd, Native.CancelMessage, IntPtr.Zero, IntPtr.Zero);
            Check(exited, "Escape cancellation message route at scale " + scale, results);
            pill.SetPaused(true);
            Check(pill.Paused && pill.AccessibleName.StartsWith("Human is paused"), "Pause state at scale " + scale, results);
            pill.SetPaused(false);
            using (Bitmap image = RenderPill(pill)) image.Save(Path.Combine(output, "pill-" + (int)(scale * 100) + ".png"), ImageFormat.Png);
        }
        IntPtr arrow = Art.LargeArrow(48);
        try
        {
            Check(arrow != IntPtr.Zero && Art.CursorSize(arrow) == new Size(48, 48), "Enlarged native 48px cursor created without changing system cursors", results);
        }
        finally { Native.DestroyCursor(arrow); }

        Preview(Path.Combine(output, "preview.png"));
        using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) results.Add("System DPI: " + g.DpiX + "; monitors: " + Screen.AllScreens.Length);
        results.Add("No visible windows displayed during these checks; live pointer tracking and click-through require interactive verification.");
        File.WriteAllLines(Path.Combine(output, "verification.txt"), results.ToArray());
    }
    static Bitmap RenderPill(ControlPill pill)
    {
        Bitmap image = new Bitmap(pill.Width, pill.Height);
        pill.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
        using (Graphics g = Graphics.FromImage(image))
        foreach (Control c in pill.Controls)
        using (Bitmap child = new Bitmap(c.Width, c.Height))
        {
            c.DrawToBitmap(child, new Rectangle(Point.Empty, child.Size));
            GraphicsState state = g.Save();
            if (c.Region != null)
            using (Region region = c.Region.Clone()) { region.Translate(c.Left, c.Top); g.SetClip(region, CombineMode.Replace); }
            g.DrawImage(child, c.Left, c.Top);
            g.Restore(state);
        }
        return image;
    }
    static void DrawDemoPointer(Graphics g, float x, float y, float size = 1)
    {
        Art.Pointer(g, x, y, size);
    }
    static void Preview(string path)
    {
        using (Bitmap image = new Bitmap(1360, 850))
        using (Graphics g = Graphics.FromImage(image))
        using (ControlPill pill = new ControlPill(1))
        {
            g.Clear(Color.FromArgb(249, 246, 239));
            using (Brush gray = new SolidBrush(Color.FromArgb(242, 242, 245))) g.FillRectangle(gray, 0, 0, 1360, 54);
            using (Font font = new Font("Segoe UI", 16, FontStyle.Regular, GraphicsUnit.Pixel)) g.DrawString("Human · Computer Use", font, Brushes.DimGray, 30, 17);
            int bannerX = (image.Width - pill.Width) / 2;
            using (Bitmap bar = RenderPill(pill))
            using (GraphicsPath clip = Art.Round(new RectangleF(bannerX, 12, pill.Width, pill.Height), 8))
            {
                GraphicsState saved = g.Save(); g.SetClip(clip); g.DrawImageUnscaled(bar, bannerX, 12); g.Restore(saved);
            }
            using (Bitmap halo = Art.Halo(1)) g.DrawImageUnscaled(halo, 748, 448);
            DrawDemoPointer(g, 815, 512, 1.5f);
            using (Font title = new Font("Microsoft YaHei UI", 28, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Brush ink = new SolidBrush(Color.FromArgb(75, 71, 88))) g.DrawString("Human 正在使用电脑", title, ink, 65, 640);
            using (Font body = new Font("Microsoft YaHei UI", 15, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                g.DrawString("放大鼠标 · 蓝底白字提示 · Esc 取消", body, Brushes.Gray, 67, 695);
                g.DrawString("界面示意图 · 按 Esc 退出后恢复原鼠标", body, Brushes.Gray, 67, 780);
            }
            image.Save(path, ImageFormat.Png);
        }
    }
}
