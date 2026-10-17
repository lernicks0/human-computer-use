using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

internal sealed class ControlPill : Form
{
    internal const string WindowTitle = "Human Computer Use · Desktop Overlay";
    internal const string ActiveText = "Human is using your computer · ";
    internal const string CancelText = "Esc to cancel";
    internal static readonly Color BannerBlue = Color.FromArgb(37, 99, 235);
    internal event Action ToggleRequested, ExitRequested, RestoreRequested, HideRequested;
    internal readonly float ScaleFactor;
    internal bool Paused;
    readonly Font textFont;
    readonly Font cancelFont;
    readonly Button cancel;
    readonly int leftPadding;
    readonly ToolTip tip = new ToolTip();

    internal ControlPill(float scale)
    {
        ScaleFactor = scale;
        Text = WindowTitle;
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        BackColor = BannerBlue;
        ForeColor = Color.White;
        textFont = new Font("Segoe UI", 14 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        cancelFont = new Font("Segoe UI", 14 * scale, FontStyle.Bold, GraphicsUnit.Pixel);
        leftPadding = (int)Math.Round(17 * scale);
        int prefixWidth = TextRenderer.MeasureText(ActiveText, textFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
        int cancelWidth = TextRenderer.MeasureText(CancelText, cancelFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width + (int)(10 * scale);
        ClientSize = new Size(leftPadding * 2 + prefixWidth + cancelWidth, (int)(36 * scale));
        using (GraphicsPath p = Art.Round(new RectangleF(0, 0, Width, Height), 8 * scale)) Region = new Region(p);
        AccessibleName = ActiveText + CancelText;
        cancel = new Button { Text = CancelText, AccessibleName = CancelText, Font = cancelFont,
            ForeColor = Color.White, BackColor = BannerBlue, FlatStyle = FlatStyle.Flat,
            Bounds = new Rectangle(leftPadding + prefixWidth, (int)(4 * scale), cancelWidth, (int)(28 * scale)), Cursor = Cursors.Hand };
        cancel.FlatAppearance.BorderSize = 0;
        cancel.FlatAppearance.MouseOverBackColor = Color.FromArgb(29, 84, 207);
        cancel.FlatAppearance.MouseDownBackColor = Color.FromArgb(24, 72, 181);
        cancel.Click += delegate { if (ExitRequested != null) ExitRequested(); };
        tip.SetToolTip(cancel, "Esc · 取消并恢复鼠标");
        Controls.Add(cancel);
        MouseDown += delegate(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            Native.ReleaseCapture();
            Native.SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero);
        };
    }
    internal void SetPaused(bool value)
    {
        Paused = value;
        AccessibleName = (value ? "Human is paused · " : ActiveText) + CancelText;
        Invalidate();
    }
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams
    {
        get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x00000080; return cp; }
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.RegisterHotKey(Handle, 1, 0x4003, (uint)Keys.Q);
        Native.RegisterHotKey(Handle, 2, 0x4003, (uint)Keys.H);
        Native.RegisterHotKey(Handle, 3, 0x4003, (uint)Keys.B);
    }
    protected override void OnHandleDestroyed(EventArgs e)
    {
        for (int id = 1; id <= 3; id++) Native.UnregisterHotKey(Handle, id);
        base.OnHandleDestroyed(e);
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.RestoreMessage) { if (RestoreRequested != null) RestoreRequested(); return; }
        if (m.Msg == Native.CancelMessage) { if (ExitRequested != null) ExitRequested(); return; }
        if (m.Msg == 0x312)
        {
            int id = m.WParam.ToInt32();
            if (id == 1 && ExitRequested != null) ExitRequested();
            if (id == 2 && ToggleRequested != null) ToggleRequested();
            if (id == 3 && HideRequested != null) HideRequested();
            return;
        }
        if (m.Msg == 0x21) { m.Result = new IntPtr(3); return; }
        base.WndProc(ref m);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        TextRenderer.DrawText(e.Graphics, Paused ? "Human is paused · " : ActiveText, textFont,
            new Rectangle(leftPadding, 0, cancel.Left - leftPadding, Height), Color.White,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) { tip.Dispose(); textFont.Dispose(); cancelFont.Dispose(); }
    }
}
