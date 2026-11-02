// SPDX-License-Identifier: MIT
// Copyright (c) 2026 sadwind227
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace BatteryPet;

sealed record PetSnapshot(string Name, int? Percent, bool Fresh, bool Charging, bool Busy, string Detail, bool Demo, int LowThreshold = 20);

static class PetBounds
{
    public static Point Constrain(Point position, Size size, Rectangle workArea) => new(
        Math.Clamp(position.X, workArea.Left, Math.Max(workArea.Left, workArea.Right - size.Width)),
        Math.Clamp(position.Y, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - size.Height)));

    public static Point ConstrainVisible(Point position, Rectangle visible, Rectangle area, int sideGap)
    {
        int gap = Math.Min(sideGap, Math.Max(0, (area.Width - visible.Width) / 2));
        int left = area.Left + gap - visible.Left, top = area.Top - visible.Top;
        return new(Math.Clamp(position.X, left, Math.Max(left, area.Right - gap - visible.Right)),
            Math.Clamp(position.Y, top, Math.Max(top, area.Bottom - visible.Bottom)));
    }

    public static Rectangle PlaceBubble(Rectangle pet, Size desired, Rectangle area)
    {
        double scale = Math.Min(1, Math.Min(area.Width / (double)desired.Width, area.Height / (double)desired.Height));
        var size = new Size(Math.Max(1, (int)(desired.Width * scale)), Math.Max(1, (int)(desired.Height * scale)));
        int x = pet.Left + (pet.Width - size.Width) / 2;
        int y = pet.Top - size.Height;
        if (y < area.Top) y = pet.Bottom;
        return new(Constrain(new(x, y), size, area), size);
    }
}

static class VisiblePixels
{
    public static Rectangle Measure(Bitmap bitmap)
    {
        var data = bitmap.LockBits(new(Point.Empty, bitmap.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        int left = bitmap.Width, top = bitmap.Height, right = -1, bottom = -1;
        try
        {
            byte[] row = new byte[bitmap.Width * 4];
            for (int y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                for (int x = 0; x < bitmap.Width; x++) if (row[x * 4 + 3] > 0)
                { left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = y; }
            }
        }
        finally { bitmap.UnlockBits(data); }
        return right < left ? Rectangle.Empty : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
    }
}

// Gesture decisions are independent of rendering: dragging must never open a battery bubble.
sealed class PetInteraction
{
    public Point? Origin { get; private set; }
    public bool Dragging { get; private set; }
    public double BubbleUntil { get; private set; }
    public bool BubbleVisible(double now) => now < BubbleUntil;
    public void Down(Point point) { Origin = point; Dragging = false; }
    public bool Move(Point point, int threshold)
    {
        if (Origin is not Point start) return false;
        Dragging |= Math.Abs(point.X - start.X) > threshold || Math.Abs(point.Y - start.Y) > threshold;
        if (Dragging) BubbleUntil = 0;
        return Dragging;
    }
    public bool Up(double now, int seconds)
    {
        bool click = Origin.HasValue && !Dragging;
        Origin = null; Dragging = false;
        if (click) BubbleUntil = now + seconds;
        return click;
    }
    public void Cancel() { Origin = null; Dragging = false; }
    public void Dismiss() => BubbleUntil = 0;
}

// A per-pixel alpha surface avoids color-key fringes around the curved whale.
abstract class AlphaWindow : Form
{
    public bool LastPresentSucceeded { get; private set; }
    [StructLayout(LayoutKind.Sequential)] struct NativePoint { public int X, Y; public NativePoint(int x, int y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential)] struct NativeSize { public int Width, Height; public NativeSize(int w, int h) { Width = w; Height = h; } }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] struct Blend { public byte Op, Flags, Alpha, Format; }
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll", SetLastError = true)] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr target, ref NativePoint point, ref NativeSize size, IntPtr source, ref NativePoint origin, uint colorKey, ref Blend blend, uint flags);
    protected AlphaWindow()
    {
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
    }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x80000 | 0x08000000 | 0x80; return cp; } }
    protected abstract void DrawSurface(Graphics graphics);
    public Bitmap RenderBitmap()
    {
        var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        bitmap.SetResolution(96, 96);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            DrawSurface(graphics);
        }
        return bitmap;
    }
    public void Present(byte opacity = 255)
    {
        if (!IsHandleCreated || IsDisposed || Width < 1 || Height < 1) return;
        using var bitmap = RenderBitmap();
        IntPtr screen = GetDC(IntPtr.Zero), memory = CreateCompatibleDC(screen), image = bitmap.GetHbitmap(Color.FromArgb(0));
        IntPtr old = SelectObject(memory, image);
        try
        {
            var location = new NativePoint(Left, Top); var size = new NativeSize(Width, Height); var origin = new NativePoint(0, 0);
            var blend = new Blend { Alpha = opacity, Format = 1 };
            LastPresentSucceeded = UpdateLayeredWindow(Handle, screen, ref location, ref size, memory, ref origin, 0, ref blend, 2);
        }
        finally { SelectObject(memory, old); DeleteObject(image); DeleteDC(memory); ReleaseDC(IntPtr.Zero, screen); }
    }
    protected override void OnShown(EventArgs e) { base.OnShown(e); ShowWindow(Handle, 4); Present(); }
}

sealed class PetWindow : AlphaWindow
{
    readonly Preferences prefs;
    readonly Func<PetSnapshot> reading;
    readonly Action refresh, open, hide, save, change;
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly System.Windows.Forms.Timer timer = new() { Interval = 50 };
    readonly PetInteraction interaction = new();
    readonly BatteryBubble bubble;
    readonly ToolTip tip = new();
    readonly ClickAudio audio = new();
    PetImage? customImage;
    string loadedImagePath = "";
    double customImageStart;
    public string ResourceWarning { get; private set; } = "";
    Point windowOrigin;
    bool hovered;
    double pokedAt = -10;
    Rectangle visibleBounds;
    bool applyingSettings;
    public Rectangle VisibleBounds => visibleBounds;
    [DllImport("user32.dll")] static extern int GetSystemMetricsForDpi(int index, uint dpi);
    int SideGap => Math.Max(1, GetSystemMetricsForDpi(2, (uint)DeviceDpi));
    Rectangle VisibleScreenBounds => new(new Point(Left + visibleBounds.Left, Top + visibleBounds.Top), visibleBounds.Size);

    public PetWindow(Preferences prefs, Func<PetSnapshot> reading, Action refresh, Action open, Action hide, Action save, Action change)
    {
        this.prefs = prefs; this.reading = reading; this.refresh = refresh; this.open = open; this.hide = hide; this.save = save; this.change = change;
        Text = "电量桌宠 · 小鲸鱼";
        AccessibleName = "小鲸鱼桌宠，左键查看电量，拖动移动，右键设置";
        bubble = new BatteryBubble(reading);
        bubble.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) DismissBubble(); };
        tip.SetToolTip(this, "左键查看电量 · 拖动移动\n右键设置 · Ctrl + 滚轮调整大小");
        ContextMenuStrip = new ContextMenuStrip { Renderer = new SeaMenuRenderer() };
        ContextMenuStrip.Opening += (_, _) => BuildMenu();
        timer.Tick += (_, _) => TickFrame();
        VisibleChanged += (_, _) =>
        {
            if (Visible) { interaction.Dismiss(); timer.Start(); Present(); }
            else { timer.Stop(); audio.Stop(); interaction.Cancel(); DismissBubble(); }
        };
        ApplySettings();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplayChanged;
    }
    void DisplayChanged(object? sender, EventArgs e)
    {
        if (IsHandleCreated && !IsDisposed) BeginInvoke(() => { EnsureOnScreen(); save(); });
    }
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        if (!applyingSettings) { ApplySettings(); save(); }
    }
    double Now => clock.Elapsed.TotalSeconds;
    public void ApplySettings()
    {
        if (applyingSettings) return;
        applyingSettings = true;
        try
        {
        var oldArea = Screen.FromRectangle(VisibleScreenBounds).WorkingArea;
        bool right = visibleBounds.Width > 0 && Math.Abs(VisibleScreenBounds.Right - (oldArea.Right - SideGap)) <= 2;
        bool left = visibleBounds.Width > 0 && Math.Abs(VisibleScreenBounds.Left - (oldArea.Left + SideGap)) <= 2;
        bool bottom = visibleBounds.Height > 0 && Math.Abs(VisibleScreenBounds.Bottom - oldArea.Bottom) <= 2;
        bool top = visibleBounds.Height > 0 && Math.Abs(VisibleScreenBounds.Top - oldArea.Top) <= 2;
        if (loadedImagePath != prefs.PetImagePath)
        {
            customImage?.Dispose(); customImage = null; loadedImagePath = prefs.PetImagePath; ResourceWarning = ""; customImageStart = Now;
            if (!string.IsNullOrEmpty(loadedImagePath))
                try { customImage = PetImage.Load(loadedImagePath); }
                catch { ResourceWarning = "自定义图片不可用，已回退到内置小鲸鱼"; }
        }
        if (!prefs.ClickSoundEnabled) audio.Stop();
        var bottomCenter = new Point(Left + Width / 2, Bottom);
        double factor = Math.Clamp(prefs.PetSize, 60, 180) / 100.0 * DeviceDpi / 96.0;
        Size = new((int)(240 * factor), (int)(180 * factor));
        visibleBounds = customImage != null ? customImage.GetVisibleBounds(Size, prefs.PetAnimate) : MeasureBuiltin(Size, prefs.PetAnimate);
        if (IsHandleCreated) Location = new(bottomCenter.X - Width / 2, bottomCenter.Y - Height);
        if (right) Left = oldArea.Right - SideGap - visibleBounds.Right;
        else if (left) Left = oldArea.Left + SideGap - visibleBounds.Left;
        if (bottom) Top = oldArea.Bottom - visibleBounds.Bottom;
        else if (top) Top = oldArea.Top - visibleBounds.Top;
        TopMost = prefs.PetTopMost; bubble.TopMost = prefs.PetTopMost;
        ClampToScreen(); Present(); UpdateBubble();
        }
        finally { applyingSettings = false; }
    }
    static readonly Dictionary<(Size, bool), Rectangle> builtinBounds = new();
    internal static Rectangle MeasureBuiltin(Size size, bool animate)
    {
        if (builtinBounds.TryGetValue((size, animate), out var cached)) return cached;
        Rectangle union = Rectangle.Empty;
        using var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        foreach (double time in animate ? new[] { Math.PI / 4.4, 3 * Math.PI / 4.4 } : new[] { 1.0 })
        {
            g.Clear(Color.Transparent);
            PetRenderer.Draw(g, new(Point.Empty, size), "薄荷海", time, false, false, 0, PointF.Empty);
            var rect = VisiblePixels.Measure(bitmap);
            union = union.IsEmpty ? rect : Rectangle.Union(union, rect);
        }
        builtinBounds[(size, animate)] = union;
        return union;
    }
    void ClampToScreen()
    {
        var area = Screen.FromRectangle(VisibleScreenBounds).WorkingArea;
        Location = PetBounds.ConstrainVisible(Location, visibleBounds, area, SideGap);
    }
    public void EnsureOnScreen() { ClampToScreen(); Present(); UpdateBubble(); }
    public void MoveToBottomRight()
    {
        var area = Screen.FromRectangle(VisibleScreenBounds).WorkingArea;
        Location = new(area.Right - SideGap - visibleBounds.Right, area.Bottom - visibleBounds.Bottom);
        EnsureOnScreen();
    }
    void BuildMenu()
    {
        var menu = ContextMenuStrip!; foreach (ToolStripItem item in menu.Items.Cast<ToolStripItem>().ToArray()) item.Dispose(); menu.Items.Clear();
        menu.Items.Add("打开设备面板", null, (_, _) => open());
        menu.Items.Add("刷新电量", null, (_, _) => refresh());
        menu.Items.Add(new ToolStripSeparator());
        var sizes = new ToolStripMenuItem($"角色大小 · {prefs.PetSize}%");
        foreach (int size in new[] { 60, 80, 100, 125, 150, 180 })
            sizes.DropDownItems.Add(new ToolStripMenuItem(size + "%", null, (_, _) => { prefs.PetSize = size; change(); save(); }) { Checked = prefs.PetSize == size });
        menu.Items.Add(sizes);
        var colors = new ToolStripMenuItem("角色配色");
        foreach (var theme in SeaTheme.Names)
            colors.DropDownItems.Add(new ToolStripMenuItem(theme, null, (_, _) => { prefs.PetTheme = theme; change(); }) { Checked = prefs.PetTheme == theme });
        menu.Items.Add(colors);
        menu.Items.Add(new ToolStripMenuItem("呼吸与眨眼动画", null, (_, _) => { prefs.PetAnimate = !prefs.PetAnimate; change(); }) { Checked = prefs.PetAnimate });
        menu.Items.Add(new ToolStripMenuItem("点击音效", null, (_, _) => { prefs.ClickSoundEnabled = !prefs.ClickSoundEnabled; change(); }) { Checked = prefs.ClickSoundEnabled });
        menu.Items.Add(new ToolStripMenuItem("保持在其他窗口上方", null, (_, _) => { prefs.PetTopMost = !prefs.PetTopMost; change(); }) { Checked = prefs.PetTopMost });
        menu.Items.Add("回到右下角", null, (_, _) => { MoveToBottomRight(); save(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("隐藏桌宠", null, (_, _) => hide());
    }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Present(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Present(); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        tip.Hide(this);
        interaction.Down(Cursor.Position); windowOrigin = Location; Capture = true; Present();
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var origin = interaction.Origin;
        if (origin.HasValue && interaction.Move(Cursor.Position, Math.Max(4, DeviceDpi / 24)))
        {
            var cursor = Cursor.Position;
            var desired = new Point(windowOrigin.X + cursor.X - origin.Value.X, windowOrigin.Y + cursor.Y - origin.Value.Y);
            // Constrain before moving the layered window, including while the mouse is captured.
            Location = PetBounds.ConstrainVisible(desired, visibleBounds, Screen.FromPoint(cursor).WorkingArea, SideGap);
            bubble.Hide(); Present();
        }
        else if (hovered && prefs.PetAnimate) Present();
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || !interaction.Origin.HasValue) return;
        bool wasDrag = interaction.Dragging;
        bool click = interaction.Up(Now, prefs.BubbleSeconds); Capture = false;
        if (wasDrag) { ClampToScreen(); save(); }
        if (click) { pokedAt = Now; UpdateBubble(); if (prefs.ClickSoundEnabled) audio.Play(prefs.ClickSoundPath); refresh(); }
        Present();
    }
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture && interaction.Origin.HasValue)
        {
            bool wasDrag = interaction.Dragging; interaction.Cancel(); ClampToScreen();
            if (wasDrag) save();
            Present();
        }
    }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if ((ModifierKeys & Keys.Control) == 0) return;
        prefs.PetSize = Math.Clamp(prefs.PetSize + Math.Sign(e.Delta) * 5, 60, 180); change(); save();
    }
    void TickFrame()
    {
        if (prefs.PetAnimate || Now - pokedAt < 0.65) Present();
        if (interaction.BubbleVisible(Now)) UpdateBubble(); else if (bubble.Visible) bubble.Hide();
    }
    public void DataChanged() { Present(); UpdateBubble(); }
    void DismissBubble() { interaction.Dismiss(); bubble.Hide(); }
    void UpdateBubble()
    {
        if (!Visible || !interaction.BubbleVisible(Now)) { bubble.Hide(); return; }
        var area = Screen.FromRectangle(VisibleScreenBounds).WorkingArea;
        bubble.Bounds = PetBounds.PlaceBubble(VisibleScreenBounds, new((int)(258 * DeviceDpi / 96f), (int)(150 * DeviceDpi / 96f)), area);
        if (!bubble.Visible) bubble.Show(this);
        bubble.Present();
    }
    protected override void DrawSurface(Graphics graphics)
    {
        var state = graphics.Save();
        double poke = Math.Max(0, 1 - (Now - pokedAt) / .65);
        if (interaction.Dragging || poke > 0 || (hovered && customImage == null))
        {
            // Keep transient tilt, zoom and hearts inside both the layer and screen.
            // Geometry comes from the cached frame union, not a scan during dragging.
            RectangleF effects = ClientRectangle;
            if (customImage != null)
            {
                var corners = new[] { new PointF(visibleBounds.Left, visibleBounds.Top), new PointF(visibleBounds.Right, visibleBounds.Top),
                    new PointF(visibleBounds.Right, visibleBounds.Bottom), new PointF(visibleBounds.Left, visibleBounds.Bottom) };
                using var transform = new Matrix();
                transform.Translate(Width / 2f, Height / 2f);
                if (interaction.Dragging) transform.Rotate(-5);
                float zoom = 1 + (float)poke * .045f; transform.Scale(zoom, zoom);
                transform.Translate(-Width / 2f, -Height / 2f);
                transform.TransformPoints(corners);
                effects = RectangleF.FromLTRB(corners.Min(p => p.X) - 1, corners.Min(p => p.Y) - 1,
                    corners.Max(p => p.X) + 1, corners.Max(p => p.Y) + 1);
            }
            var area = Screen.FromRectangle(VisibleScreenBounds).WorkingArea;
            area.Offset(-Left, -Top);
            var viewport = Rectangle.Intersect(ClientRectangle, area);
            if (!viewport.IsEmpty)
            {
                float scale = Math.Min(1, Math.Min(viewport.Width / effects.Width, viewport.Height / effects.Height));
                float x = Math.Clamp(effects.Left, viewport.Left, Math.Max(viewport.Left, viewport.Right - effects.Width * scale));
                float y = Math.Clamp(effects.Top, viewport.Top, Math.Max(viewport.Top, viewport.Bottom - effects.Height * scale));
                graphics.TranslateTransform(x - effects.Left * scale, y - effects.Top * scale);
                graphics.ScaleTransform(scale, scale);
            }
        }
        var mouse = PointToClient(Cursor.Position);
        var gaze = new PointF(Math.Clamp((mouse.X - Width / 2f) / Width, -.5f, .5f), Math.Clamp((mouse.Y - Height / 2f) / Height, -.5f, .5f));
        if (customImage != null) customImage.Draw(graphics, ClientRectangle, Now - customImageStart, prefs.PetAnimate, interaction.Dragging, poke);
        else PetRenderer.Draw(graphics, ClientRectangle, prefs.PetTheme, prefs.PetAnimate ? Now : 1,
            hovered, interaction.Dragging, Math.Max(0, 1 - (Now - pokedAt) / .65), gaze);
        graphics.Restore(state);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplayChanged; timer.Dispose(); bubble.Dispose(); tip.Dispose(); audio.Dispose(); customImage?.Dispose(); ContextMenuStrip?.Dispose(); }
        base.Dispose(disposing);
    }
}

sealed class BatteryBubble : AlphaWindow
{
    readonly Func<PetSnapshot> reading;
    public BatteryBubble(Func<PetSnapshot> reading) { this.reading = reading; Text = "点击查看的电量气泡"; Size = new(258, 150); }
    protected override void DrawSurface(Graphics g)
    {
        g.ScaleTransform(Width / 258f, Height / 150f);
        var d = reading();
        using var shadow = Ui.Round(new RectangleF(5, 5, 248, 139), 20);
        using var shadowBrush = new SolidBrush(Color.FromArgb(36, 10, 21, 36)); g.FillPath(shadowBrush, shadow);
        using var body = Ui.Round(new RectangleF(3, 2, 252, 134), 19);
        using var fill = new SolidBrush(Color.FromArgb(249, 252, 252)); g.FillPath(fill, body);
        using var edge = new Pen(Color.FromArgb(218, 230, 230)); g.DrawPath(edge, body);
        using var title = Ui.DrawFont(9, FontStyle.Bold); using var number = Ui.DrawFont(29, FontStyle.Bold); using var small = Ui.DrawFont(8);
        Ui.Text(g, d.Demo ? "模拟设备 · 非真实电量" : d.Name, title, Ui.Ink, new(17, 13, 224, 21));
        string value = d.Percent.HasValue ? d.Percent + "%" : "—";
        var color = !d.Fresh ? Ui.Muted : d.Percent < d.LowThreshold ? Color.FromArgb(222, 104, 113) : Ui.AccentDark;
        Ui.Text(g, value, number, color, new(15, 34, 145, 48));
        Ui.Text(g, d.Busy ? "刷新中…" : !d.Percent.HasValue ? "等待连接" : !d.Fresh ? "上次读数" : d.Charging ? "正在充电" : d.Percent < d.LowThreshold ? "电量偏低" : "当前电量", small, Ui.Muted, new(159, 53, 81, 23));
        Ui.Progress(g, new(18, 88, 222, 6), d.Percent ?? 0, color);
        Ui.Text(g, d.Detail + " · 点击收起", small, Ui.Muted, new(17, 103, 226, 20));
    }
}

static class SeaTheme
{
    public static readonly string[] Names = { "薄荷海", "晴空蓝", "暮光紫" };
    public static (Color Light, Color Dark) Colors(string name) => name switch
    {
        "晴空蓝" => (Color.FromArgb(139, 211, 255), Color.FromArgb(61, 142, 220)),
        "暮光紫" => (Color.FromArgb(200, 181, 250), Color.FromArgb(135, 106, 204)),
        _ => (Color.FromArgb(137, 231, 210), Color.FromArgb(51, 172, 161))
    };
}

static class PetRenderer
{
    public static void Draw(Graphics g, Rectangle bounds, string theme, double time, bool hover, bool drag, double poke, PointF gaze)
    {
        var saved = g.Save();
        g.TranslateTransform(bounds.X, bounds.Y); g.ScaleTransform(bounds.Width / 240f, bounds.Height / 180f);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float bob = (float)Math.Sin(time * 2.2) * 2.2f;
        float squash = (float)poke * .10f;
        using (var shadow = new SolidBrush(Color.FromArgb(26, 23, 65, 69))) g.FillEllipse(shadow, 46, 151, 144, 10);
        g.TranslateTransform(120, 144 + bob); g.ScaleTransform(1 + squash, 1 - squash); g.TranslateTransform(-120, -144);
        var colors = SeaTheme.Colors(theme);
        using var outline = new Pen(Color.FromArgb(105, 30, 96, 106), 1.4f);
        using var shade = new SolidBrush(colors.Dark);
        using var tail = new GraphicsPath();
        tail.AddBezier(171, 103, 186, 101, 190, 86, 189, 72); tail.AddBezier(189, 72, 200, 76, 204, 84, 202, 94);
        tail.AddBezier(202, 94, 208, 88, 218, 89, 224, 94); tail.AddBezier(224, 94, 222, 107, 206, 113, 190, 113); tail.CloseFigure();
        g.FillPath(shade, tail); g.DrawPath(outline, tail);
        using var body = new GraphicsPath();
        body.AddBezier(35, 96, 31, 58, 62, 37, 105, 38); body.AddBezier(105, 38, 151, 37, 180, 61, 182, 100);
        body.AddBezier(182, 100, 185, 132, 154, 148, 108, 148); body.AddBezier(108, 148, 62, 149, 35, 131, 35, 96); body.CloseFigure();
        using var gradient = new LinearGradientBrush(new Rectangle(35, 38, 150, 110), colors.Light, colors.Dark, 82f);
        g.FillPath(gradient, body); g.DrawPath(outline, body);
        var clip = g.Save(); g.SetClip(body);
        using var belly = new SolidBrush(Color.FromArgb(224, 247, 237)); g.FillEllipse(belly, 37, 109, 140, 64);
        using var glow = new SolidBrush(Color.FromArgb(75, 255, 255, 255)); g.FillEllipse(glow, 58, 49, 55, 15); g.Restore(clip);
        using var fin = new GraphicsPath(); fin.AddBezier(137, 109, 151, 109, 160, 120, 157, 133); fin.AddBezier(157, 133, 142, 130, 135, 122, 137, 109); fin.CloseFigure(); g.FillPath(shade, fin);
        bool blink = time % 5.2 < .16;
        using var eye = new SolidBrush(Color.FromArgb(32, 67, 86)); using var smile = new Pen(Color.FromArgb(32, 67, 86), 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        foreach (int x in new[] { 65, 114 })
        {
            if (hover && !drag || poke > .1) g.DrawArc(smile, x, 85, 11, 8, 190, 160);
            else if (blink) g.DrawLine(smile, x, 91, x + 10, 91);
            else { g.FillEllipse(eye, x + gaze.X * 3, 83 + gaze.Y * 3, 10, drag ? 15 : 13); g.FillEllipse(Brushes.White, x + 2 + gaze.X * 3, 85 + gaze.Y * 3, 3, 4); }
        }
        using var blush = new SolidBrush(Color.FromArgb(125, 250, 149, 167)); g.FillEllipse(blush, 54, 100, 17, 8); g.FillEllipse(blush, 122, 100, 17, 8);
        if (drag) g.DrawEllipse(smile, 90, 104, 8, 10); else g.DrawArc(smile, 87, 100, 17, 11, 10, 160);
        if (poke > 0)
        {
            using var heart = new SolidBrush(Color.FromArgb((int)(220 * poke), 245, 133, 160));
            float y = 29 - (float)(1 - poke) * 25;
            g.FillEllipse(heart, 143, y, 10, 10); g.FillEllipse(heart, 151, y, 10, 10);
            g.FillPolygon(heart, new[] { new PointF(144, y + 7), new PointF(160, y + 7), new PointF(152, y + 18) });
        }
        if (hover)
        {
            using var dot = new SolidBrush(Color.FromArgb(120, colors.Dark)); g.FillEllipse(dot, 30, 50, 7, 7); g.FillEllipse(dot, 22, 37, 4, 4);
        }
        g.Restore(saved);
    }
}
