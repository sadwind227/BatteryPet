// SPDX-License-Identifier: MIT
// Copyright (c) 2026 sadwind227
using System.Drawing.Drawing2D;

namespace BatteryPet;

static class Ui
{
    public static readonly Color Background = Color.FromArgb(242, 247, 247), Card = Color.White,
        Ink = Color.FromArgb(30, 55, 64), Muted = Color.FromArgb(112, 133, 139),
        Accent = Color.FromArgb(83, 195, 168), AccentDark = Color.FromArgb(27, 126, 110),
        Line = Color.FromArgb(224, 234, 233), Soft = Color.FromArgb(231, 245, 240);
    public static Font Font(float size, FontStyle style = FontStyle.Regular) => new("Microsoft YaHei UI", size, style);
    public static Font DrawFont(float size, FontStyle style = FontStyle.Regular) => new("Microsoft YaHei UI", size * 96 / 72, style, GraphicsUnit.Pixel);
    public static GraphicsPath Round(RectangleF r, float radius)
    {
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height)); var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
    }
    public static void Text(Graphics g, string text, Font font, Color color, RectangleF bounds, StringAlignment align = StringAlignment.Near)
    {
        using var brush = new SolidBrush(color); using var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, Alignment = align, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
        g.DrawString(text, font, brush, bounds, format);
    }
    public static void Progress(Graphics g, RectangleF rect, int value, Color color)
    {
        using var shape = Round(rect, rect.Height / 2); using var bg = new SolidBrush(Line); g.FillPath(bg, shape);
        if (value <= 0) return;
        var saved = g.Save(); g.SetClip(shape); using var brush = new SolidBrush(color);
        g.FillRectangle(brush, rect.X, rect.Y, rect.Width * Math.Clamp(value, 0, 100) / 100f, rect.Height); g.Restore(saved);
    }
}

sealed class SeaMenuColors : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => Ui.Card;
    public override Color ImageMarginGradientBegin => Ui.Card;
    public override Color ImageMarginGradientMiddle => Ui.Card;
    public override Color ImageMarginGradientEnd => Ui.Card;
    public override Color MenuItemSelected => Ui.Soft;
    public override Color MenuItemBorder => Ui.Soft;
    public override Color MenuBorder => Ui.Line;
}
sealed class SeaMenuRenderer : ToolStripProfessionalRenderer { public SeaMenuRenderer() : base(new SeaMenuColors()) { RoundedEdges = true; } }

class RoundedPanel : Panel
{
    public RoundedPanel() { DoubleBuffered = true; ResizeRedraw = true; BackColor = Ui.Card; Padding = new(20); }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Ui.Background); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = Ui.Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), 18);
        using var brush = new SolidBrush(BackColor); e.Graphics.FillPath(brush, shape); using var pen = new Pen(Ui.Line); e.Graphics.DrawPath(pen, shape);
    }
}

sealed class SeaButton : Button
{
    bool hover;
    public bool Primary { get; set; }
    public SeaButton(string text, bool primary = false)
    {
        Text = text; Primary = primary; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand; Font = Ui.Font(9, FontStyle.Bold); Size = new(126, 38); DoubleBuffered = true;
        MouseEnter += (_, _) => { hover = true; Invalidate(); }; MouseLeave += (_, _) => { hover = false; Invalidate(); };
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; e.Graphics.Clear(Parent?.BackColor ?? Ui.Background);
        using var path = Ui.Round(new RectangleF(0, 0, Width - 1, Height - 1), 10);
        using var brush = new SolidBrush(Primary ? (hover ? Ui.AccentDark : Ui.Accent) : (hover ? Ui.Line : Ui.Soft));
        e.Graphics.FillPath(brush, path);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? (Primary ? Color.White : Ui.AccentDark) : Ui.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -5, -5));
    }
}

sealed class SeaSwitch : CheckBox
{
    public SeaSwitch(string text)
    {
        Text = text; AutoSize = false; Size = new(228, 32); Font = Ui.Font(9); ForeColor = Ui.Ink; Cursor = Cursors.Hand; DoubleBuffered = true;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Ui.Card); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = DeviceDpi / 96f;
        using var path = Ui.Round(new RectangleF(0, 6 * scale, 34 * scale, 20 * scale), 10 * scale);
        using var fill = new SolidBrush(Checked ? Ui.Accent : Ui.Line); e.Graphics.FillPath(fill, path);
        e.Graphics.FillEllipse(Brushes.White, (Checked ? 17 : 3) * scale, 9 * scale, 14 * scale, 14 * scale);
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle((int)(44 * scale), 0, Width - (int)(44 * scale), Height), Ui.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -1, -1));
    }
}

sealed record DeviceView(string Id, string Name, int? Percent, bool Fresh, bool Charging, DateTime Updated, bool Focused, DeviceSettings Settings, string Connection = "2.4G", string Detail = "");

static class DeviceListPolicy
{
    public static bool Hidden(DeviceView device, Preferences prefs) => device.Settings.Hidden ||
        (prefs.HideInactiveBluetooth && device.Connection.StartsWith("蓝牙") && !device.Fresh);
    public static List<DeviceView> Ordered(IEnumerable<DeviceView> devices, Preferences prefs) => devices
        .OrderBy(d => { int index = prefs.DeviceOrder.IndexOf(d.Id); return index < 0 ? int.MaxValue : index; }).ToList();
    public static void Move(Preferences prefs, IReadOnlyList<DeviceView> devices, string id, int position)
    {
        var order = Ordered(devices, prefs).Select(d => d.Id).ToList();
        if (!order.Remove(id)) return;
        order.Insert(Math.Clamp(position - 1, 0, order.Count), id);
        // Retain remembered devices which are absent from this scan.
        prefs.DeviceOrder = order.Concat(prefs.DeviceOrder.Where(x => !order.Contains(x))).Distinct().ToList();
    }
}

sealed class DeviceCard : RoundedPanel
{
    DeviceView model;
    readonly Action<string, bool> changed;
    readonly SeaSwitch show = new("托盘图标"), notify = new("低电量提醒");
    readonly NumericUpDown threshold = new() { Minimum = 1, Maximum = 100, Width = 62, BorderStyle = BorderStyle.FixedSingle };
    readonly ComboBox style = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 108, FlatStyle = FlatStyle.Flat };
    readonly SeaButton focus = new("由小鲸鱼关注");
    bool syncing;
    public string DeviceId => model.Id;
    readonly NumericUpDown position = new() { Minimum = 1, Maximum = 10000, Width = 58 };
    readonly SeaButton hide = new("隐藏设备");
    bool dropHighlight;
    public void SetListState(int index) { position.Value = Math.Clamp(index, 1, 10000); hide.Text = model.Settings.Hidden ? "恢复显示" : "隐藏设备"; }
    public DeviceCard(DeviceView model, Action<string, bool> changed, Action<string> focusDevice, Action<string> hideDevice, Action<string, int> moveDevice)
    {
        this.model = model; this.changed = changed; Height = 340; Font = Ui.Font(9);
        style.Items.AddRange(new object[] { "数字", "圆环", "电池" });
        var right = new FlowLayoutPanel { Width = 270, Dock = DockStyle.Right, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new(14, 2, 0, 0), BackColor = Ui.Card };
        right.Controls.Add(show); right.Controls.Add(notify);
        var rule = new FlowLayoutPanel { Width = 242, Height = 35, WrapContents = false };
        rule.Controls.Add(new Label { Text = "低于", AutoSize = true, Padding = new(0, 4, 0, 0), ForeColor = Ui.Muted }); rule.Controls.Add(threshold);
        rule.Controls.Add(new Label { Text = "% 时提醒", AutoSize = true, Padding = new(0, 4, 0, 0), ForeColor = Ui.Muted }); right.Controls.Add(rule);
        var row = new FlowLayoutPanel { Width = 242, Height = 37, WrapContents = false };
        row.Controls.Add(new Label { Text = "图标样式", AutoSize = true, Padding = new(0, 4, 7, 0), ForeColor = Ui.Muted }); row.Controls.Add(style); right.Controls.Add(row);
        right.Controls.Add(focus); Controls.Add(right);
        var ordering = new FlowLayoutPanel { Width = 242, Height = 34, WrapContents = false };
        ordering.Controls.Add(new Label { Text = "序号", AutoSize = true, Padding = new(0, 4, 0, 0) });
        ordering.Controls.Add(position);
        var move = new SeaButton("移动") { Width = 65, Height = 28 };
        move.Click += (_, _) => moveDevice(model.Id, (int)position.Value); ordering.Controls.Add(move); right.Controls.Add(ordering);
        var management = new FlowLayoutPanel { Width = 242, Height = 38, WrapContents = false };
        var top = new SeaButton("置顶") { Width = 66, Height = 32 }; hide.Size = new(110, 32);
        top.Click += (_, _) => moveDevice(model.Id, 1); hide.Click += (_, _) => hideDevice(model.Id);
        management.Controls.Add(top); management.Controls.Add(hide); right.Controls.Add(management);
        var grip = new Label { Text = "⠿ 拖动排序", AutoSize = true, ForeColor = Ui.AccentDark, Cursor = Cursors.SizeAll, Location = new(24, 294) };
        Point down = Point.Empty;
        grip.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) down = e.Location; };
        grip.MouseMove += (_, e) => { if (e.Button == MouseButtons.Left && (Math.Abs(e.X - down.X) > SystemInformation.DragSize.Width / 2 || Math.Abs(e.Y - down.Y) > SystemInformation.DragSize.Height / 2)) {
            BackColor = Ui.Soft; Invalidate(true);
            try { grip.DoDragDrop(model.Id, DragDropEffects.Move); }
            finally { BackColor = Ui.Card; dropHighlight = false; Invalidate(true); }
        } };
        Controls.Add(grip);
        long lastScroll = 0;
        void DropTarget(Control control)
        {
            control.AllowDrop = true;
            control.DragEnter += (_, e) => { if (e.Data?.GetData(typeof(string)) is string id && id != model.Id) { e.Effect = DragDropEffects.Move; dropHighlight = true; Invalidate(); } };
            control.DragLeave += (_, _) => { dropHighlight = false; Invalidate(); };
            control.DragOver += (_, e) =>
            {
                if (Parent is not FlowLayoutPanel page || Environment.TickCount64 - lastScroll < 120) return;
                int y = page.PointToClient(new(e.X, e.Y)).Y;
                int step = y < 45 ? -30 : y > page.ClientSize.Height - 45 ? 30 : 0;
                if (step != 0) { lastScroll = Environment.TickCount64; page.AutoScrollPosition = new(0, Math.Max(0, -page.AutoScrollPosition.Y + step)); }
            };
            control.DragDrop += (_, e) => { dropHighlight = false; Invalidate(); if (e.Data?.GetData(typeof(string)) is string id) moveDevice(id, (int)position.Value); };
            foreach (Control child in control.Controls) DropTarget(child);
        }
        DropTarget(this);
        show.CheckedChanged += (_, _) => { if (!syncing) { model.Settings.Show = show.Checked; changed(model.Id, false); } };
        notify.CheckedChanged += (_, _) => { if (!syncing) { model.Settings.Notify = notify.Checked; changed(model.Id, false); } };
        threshold.ValueChanged += (_, _) => { if (!syncing) { model.Settings.Threshold = (int)threshold.Value; changed(model.Id, true); Invalidate(); } };
        style.SelectedIndexChanged += (_, _) => { if (!syncing) { model.Settings.Style = style.Text; changed(model.Id, false); } };
        focus.Click += (_, _) => focusDevice(model.Id);
        UpdateModel(model);
    }
    public void UpdateModel(DeviceView next)
    {
        model = next; syncing = true;
        show.Checked = model.Settings.Show; notify.Checked = model.Settings.Notify;
        threshold.Value = Math.Clamp(model.Settings.Threshold, 1, 100); style.SelectedItem = model.Settings.Style;
        focus.Text = model.Focused ? "桌宠正在关注" : "设为关注设备"; syncing = false; Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (dropHighlight)
        {
            using var pen = new Pen(Ui.Accent, 4 * DeviceDpi / 96f);
            e.Graphics.DrawLine(pen, 20, 3, Width - 20, 3);
            using var badge = new SolidBrush(Ui.Soft);
            e.Graphics.FillRectangle(badge, 20, Height - 40, Width - 40, 32);
            TextRenderer.DrawText(e.Graphics, "松开放到此位置", Font, new Rectangle(20, Height - 40, Width - 40, 32), Ui.AccentDark, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        float s = DeviceDpi / 96f; var g = e.Graphics; var saved = g.Save(); g.ScaleTransform(s, s);
        float width = Width / s - 310;
        using var title = Ui.DrawFont(12, FontStyle.Bold); using var small = Ui.DrawFont(8.5f); using var number = Ui.DrawFont(35, FontStyle.Bold);
        Ui.Text(g, model.Name, title, Ui.Ink, new(24, 20, width - 8, 28));
        string state = model.Fresh ? (model.Charging ? "充电中" : model.Detail.Length > 0 ? model.Detail : "已连接") :
            model.Detail.StartsWith("Windows 报告") ? "Windows 报告值 · 未确认在线" : "暂未响应 · 保留上次读数";
        Ui.Text(g, $"{(model.Fresh ? "●" : "○")}  {model.Connection} · {state}", small, model.Fresh ? Ui.AccentDark : Ui.Muted, new(25, 54, width, 24));
        var color = !model.Fresh ? Ui.Muted : model.Percent < model.Settings.Threshold ? Color.FromArgb(222, 104, 113) : Ui.AccentDark;
        Ui.Text(g, model.Percent.HasValue ? model.Percent + "%" : "—", number, color, new(19, 83, width, 67));
        Ui.Progress(g, new(26, 165, width - 20, 7), model.Percent ?? 0, color);
        Ui.Text(g, $"{(model.Detail.StartsWith("Windows 报告") ? "检查于" : "更新于")} {model.Updated:HH:mm:ss}", small, Ui.Muted, new(25, 190, width, 25));
        g.Restore(saved);
    }
}

sealed class WhalePreview : RoundedPanel
{
    readonly Preferences prefs;
    PetImage? image;
    string loaded = "";
    public WhalePreview(Preferences prefs) { this.prefs = prefs; BackColor = Ui.Soft; }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); float s = DeviceDpi / 96f; var saved = e.Graphics.Save(); e.Graphics.ScaleTransform(s, s);
        int w = (int)Math.Clamp(prefs.PetSize * 1.9, 114, 220);
        if (loaded != prefs.PetImagePath)
        {
            image?.Dispose(); image = null; loaded = prefs.PetImagePath;
            if (!string.IsNullOrEmpty(loaded)) try { image = PetImage.Load(loaded); } catch { }
        }
        var pictureBounds = new Rectangle(((int)(Width / s) - w) / 2, 74, w, w * 3 / 4);
        if (image != null) image.Draw(e.Graphics, pictureBounds, 0, false);
        else PetRenderer.Draw(e.Graphics, pictureBounds, prefs.PetTheme, 1, true, false, 0, PointF.Empty);
        using var title = Ui.DrawFont(11, FontStyle.Bold); using var small = Ui.DrawFont(8.5f);
        Ui.Text(e.Graphics, image != null ? "自定义桌宠" : "小鲸鱼", title, Ui.Ink, new(0, 24, Width / s, 30), StringAlignment.Center);
        Ui.Text(e.Graphics, $"{(image != null ? "导入图片" : prefs.PetTheme)} · {prefs.PetSize}%", small, Ui.AccentDark, new(0, 265, Width / s, 25), StringAlignment.Center);
        Ui.Text(e.Graphics, "平时安静陪伴", small, Ui.Muted, new(0, 306, Width / s, 24), StringAlignment.Center);
        Ui.Text(e.Graphics, "左键点击，才显示电量", small, Ui.Muted, new(0, 331, Width / s, 24), StringAlignment.Center);
        e.Graphics.Restore(saved);
    }
    protected override void Dispose(bool disposing) { if (disposing) image?.Dispose(); base.Dispose(disposing); }
}

sealed class DashboardForm : Form
{
    readonly Preferences prefs;
    readonly Action refresh, toggle, apply;
    readonly Action<string> focus;
    readonly Action<string, bool> changeDevice;
    readonly FlowLayoutPanel devicePage;
    readonly Panel lookPage;
    readonly Label footer, empty, sizeText;
    readonly SeaButton refreshButton, deviceTab, lookTab;
    readonly SeaSwitch showPet, topmost, animate;
    readonly TrackBar sizeSlider;
    readonly ComboBox theme;
    readonly NumericUpDown duration;
    readonly WhalePreview preview;
    readonly SettingsPage settingsPage;
    readonly AdaptationPage adaptationPage;
    readonly Func<bool>? actualPetVisible;
    readonly Dictionary<string, DeviceCard> cards = new();
    readonly FlowLayoutPanel listTools;
    readonly CheckBox showHidden = new() { Text = "查看隐藏设备", AutoSize = true };
    readonly Label listSummary = new() { AutoSize = true, ForeColor = Ui.Muted };
    List<DeviceView> currentDevices = new();
    bool syncing;
    public DashboardForm(Preferences prefs, Action refresh, Action toggle, Action apply, Action applyRefresh, Action<string> focus, Action<string, bool> changeDevice, Action<bool>? setAutoStart = null, Func<bool>? actualPetVisible = null)
    {
        this.prefs = prefs; this.refresh = refresh; this.toggle = toggle; this.apply = apply; this.focus = focus; this.changeDevice = changeDevice;
        this.actualPetVisible = actualPetVisible;
        Text = "电量桌宠 · 设备与外观"; ClientSize = new(820, 590); MinimumSize = new(800, 620);
        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.CenterScreen; BackColor = Ui.Background; ForeColor = Ui.Ink; Font = Ui.Font(9);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(26, 15, 26, 8), ColumnCount = 1, RowCount = 4, BackColor = Ui.Background };
        layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.Absolute, 83)); layout.RowStyles.Add(new(SizeType.Absolute, 52)); layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.Absolute, 48));
        Controls.Add(layout);
        var header = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        header.Controls.Add(new Label { Text = "电量桌宠", Font = Ui.Font(22, FontStyle.Bold), Location = new(0, 2), Size = new(260, 43) });
        header.Controls.Add(new Label { Text = "设备的电量，桌面上的一点陪伴。", ForeColor = Ui.Muted, Location = new(3, 50), AutoSize = true });
        refreshButton = new SeaButton("刷新电量", true) { Anchor = AnchorStyles.Top | AnchorStyles.Right, Location = new(630, 18) }; refreshButton.Click += (_, _) => refresh(); header.Controls.Add(refreshButton); layout.Controls.Add(header, 0, 0);
        header.SizeChanged += (_, _) => refreshButton.Location = new(Math.Max(0, header.ClientSize.Width - refreshButton.Width - 4), (int)(18 * DeviceDpi / 96f));
        var nav = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 5, RowCount = 1 };
        for (int i = 0; i < 5; i++) nav.ColumnStyles.Add(new(SizeType.Percent, 20));
        deviceTab = new SeaButton("设备电量", true); lookTab = new SeaButton("桌宠外观") { Margin = new(8, 3, 3, 3) };
        var settingsTab = new SeaButton("启动与素材") { Margin = new(8, 3, 3, 3) };
        var aboutTab = new SeaButton("关于与版本") { Margin = new(8, 3, 3, 3) };
        var aboutPage = new AboutPage { Visible = false };
        var adaptTab = new SeaButton("设备适配") { Margin = new(8, 3, 3, 3) };
        nav.Controls.Add(deviceTab); nav.Controls.Add(lookTab); nav.Controls.Add(settingsTab); nav.Controls.Add(adaptTab); nav.Controls.Add(aboutTab);
        foreach (Control tab in nav.Controls) tab.Dock = DockStyle.Fill;
        layout.Controls.Add(nav, 0, 1);
        var host = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }; layout.Controls.Add(host, 0, 2);
        devicePage = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new(0, 8, 0, 0) };
        listTools = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Margin = new(0, 0, 0, 12) };
        var autoHide = new CheckBox { Text = "显示不在线的蓝牙设备", AutoSize = true, Checked = !prefs.HideInactiveBluetooth };
        listTools.Controls.Add(autoHide); listTools.Controls.Add(showHidden); listTools.Controls.Add(listSummary);
        var tips = new ToolTip();
        tips.SetToolTip(autoHide, "包含 Windows 保留读数、暂未确认在线的蓝牙设备。");
        tips.SetToolTip(showHidden, "查看手动隐藏的设备并恢复。隐藏只影响列表，不改变托盘和提醒。");
        Disposed += (_, _) => tips.Dispose();
        foreach (Control item in listTools.Controls) item.Margin = new(4, 8, 18, 8);
        autoHide.CheckedChanged += (_, _) => { prefs.HideInactiveBluetooth = !autoHide.Checked; apply(); ArrangeDevices(); };
        showHidden.CheckedChanged += (_, _) => ArrangeDevices();
        devicePage.Controls.Add(listTools);
        empty = new Label { AutoSize = false, Height = 160, ForeColor = Ui.Muted, Padding = new(22), Text = "准备读取设备…" }; devicePage.Controls.Add(empty);
        lookPage = new Panel { Dock = DockStyle.Fill, Visible = false, Padding = new(0, 8, 0, 0) };
        settingsPage = new SettingsPage(prefs, () => { apply(); SyncAppearance(); }, applyRefresh, setAutoStart) { Visible = false };
        adaptationPage = new AdaptationPage { Visible = false };
        host.Controls.Add(aboutPage); host.Controls.Add(devicePage); host.Controls.Add(lookPage); host.Controls.Add(settingsPage); host.Controls.Add(adaptationPage);
        void ResetAbout() { aboutPage.Hide(); aboutTab.Primary = false; aboutTab.Invalidate(); }
        void ResetAdaptTab() { ResetAbout(); adaptationPage.Hide(); adaptTab.Primary = false; adaptTab.Invalidate(); }
        deviceTab.Click += (_, _) => { ResetAdaptTab(); settingsPage.Hide(); settingsTab.Primary = false; settingsTab.Invalidate(); Page(false); };
        lookTab.Click += (_, _) => { ResetAdaptTab(); settingsPage.Hide(); settingsTab.Primary = false; settingsTab.Invalidate(); Page(true); };
        settingsTab.Click += (_, _) => { ResetAdaptTab(); lookPage.Hide(); devicePage.Hide(); settingsPage.Show(); settingsPage.BringToFront(); settingsTab.Primary = true; deviceTab.Primary = lookTab.Primary = false; settingsTab.Invalidate(); deviceTab.Invalidate(); lookTab.Invalidate(); };
        adaptTab.Click += (_, _) => { ResetAbout(); settingsPage.Hide(); lookPage.Hide(); devicePage.Hide(); adaptationPage.Show(); adaptationPage.BringToFront(); adaptTab.Primary = true; settingsTab.Primary = deviceTab.Primary = lookTab.Primary = false; foreach (var button in new[] { adaptTab, settingsTab, deviceTab, lookTab }) button.Invalidate(); };
        aboutTab.Click += (_, _) =>
        {
            settingsPage.Hide(); lookPage.Hide(); devicePage.Hide(); adaptationPage.Hide();
            aboutPage.Show(); aboutPage.BringToFront();
            foreach (var tab in new[] { deviceTab, lookTab, settingsTab, adaptTab, aboutTab }) { tab.Primary = tab == aboutTab; tab.Invalidate(); }
        };
        footer = new Label { Dock = DockStyle.Fill, ForeColor = Ui.Muted, Font = Ui.Font(8), Margin = Padding.Empty, TextAlign = ContentAlignment.MiddleLeft }; layout.Controls.Add(footer, 0, 3);
        var lookLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        lookLayout.ColumnStyles.Add(new(SizeType.Absolute, 235)); lookLayout.ColumnStyles.Add(new(SizeType.Percent, 100)); lookPage.Controls.Add(lookLayout);
        preview = new WhalePreview(prefs) { Dock = DockStyle.Fill, Margin = new(0, 0, 18, 0) }; lookLayout.Controls.Add(preview, 0, 0);
        var settings = new RoundedPanel { Dock = DockStyle.Fill, Padding = new(18, 12, 12, 10), Margin = Padding.Empty, AutoScroll = true }; lookLayout.Controls.Add(settings, 1, 0);
        var stack = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        settings.Controls.Add(stack);
        showPet = new SeaSwitch("显示桌宠"); stack.Controls.Add(showPet);
        var sizeRow = new FlowLayoutPanel { Width = 420, Height = 28, WrapContents = false };
        sizeRow.Controls.Add(new Label { Text = "角色大小", Width = 100, ForeColor = Ui.Muted, Padding = new(0, 5, 0, 0) });
        sizeText = new Label { AutoSize = true, Padding = new(0, 5, 0, 0), ForeColor = Ui.AccentDark }; sizeRow.Controls.Add(sizeText); stack.Controls.Add(sizeRow);
        sizeSlider = new TrackBar { Minimum = 60, Maximum = 180, TickFrequency = 20, SmallChange = 5, LargeChange = 20, Width = 370, Height = 40, AutoSize = false, BackColor = Ui.Card }; stack.Controls.Add(sizeSlider);
        var themeRow = new FlowLayoutPanel { Width = 420, Height = 38, WrapContents = false };
        themeRow.Controls.Add(new Label { Text = "角色配色", Width = 110, Padding = new(0, 4, 0, 0), ForeColor = Ui.Muted });
        theme = new ComboBox { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat }; theme.Items.AddRange(SeaTheme.Names); themeRow.Controls.Add(theme); stack.Controls.Add(themeRow);
        var timeRow = new FlowLayoutPanel { Width = 420, Height = 38, WrapContents = false };
        timeRow.Controls.Add(new Label { Text = "气泡停留", Width = 110, Padding = new(0, 4, 0, 0), ForeColor = Ui.Muted });
        duration = new NumericUpDown { Minimum = 3, Maximum = 15, Width = 62, BorderStyle = BorderStyle.FixedSingle }; timeRow.Controls.Add(duration); timeRow.Controls.Add(new Label { Text = "秒", AutoSize = true, Padding = new(2, 4, 0, 0), ForeColor = Ui.Muted }); stack.Controls.Add(timeRow);
        animate = new SeaSwitch("呼吸与眨眼动画"); topmost = new SeaSwitch("保持在其他窗口上方"); stack.Controls.Add(animate); stack.Controls.Add(topmost);
        stack.Controls.Add(new Label { Text = "左键查看电量 · 按住拖动\n右键打开菜单 · Ctrl + 滚轮缩放", AutoSize = true, ForeColor = Ui.Muted, Font = Ui.Font(8), Padding = new(0, 8, 0, 0) });
        showPet.CheckedChanged += (_, _) => { if (!syncing) toggle(); };
        sizeSlider.ValueChanged += (_, _) => { if (!syncing) { prefs.PetSize = sizeSlider.Value; apply(); } };
        theme.SelectedIndexChanged += (_, _) => { if (!syncing) { prefs.PetTheme = theme.Text; apply(); } };
        duration.ValueChanged += (_, _) => { if (!syncing) { prefs.BubbleSeconds = (int)duration.Value; apply(); } };
        animate.CheckedChanged += (_, _) => { if (!syncing) { prefs.PetAnimate = animate.Checked; apply(); } };
        topmost.CheckedChanged += (_, _) => { if (!syncing) { prefs.PetTopMost = topmost.Checked; apply(); } };
        devicePage.SizeChanged += (_, _) => ResizeCards();
        SyncAppearance();
        float dpiScale = DeviceDpi / 96f;
        if (dpiScale != 1) Scale(new SizeF(dpiScale, dpiScale));
    }
    void Page(bool look)
    {
        lookPage.Visible = look; devicePage.Visible = !look; if (look) lookPage.BringToFront(); else devicePage.BringToFront();
        deviceTab.Primary = !look; lookTab.Primary = look; deviceTab.Invalidate(); lookTab.Invalidate();
    }
    public void SyncAppearance()
    {
        syncing = true; showPet.Checked = actualPetVisible?.Invoke() ?? prefs.Pet; sizeSlider.Value = Math.Clamp(prefs.PetSize, 60, 180);
        sizeText.Text = prefs.PetSize + "%"; theme.SelectedItem = prefs.PetTheme;
        duration.Value = Math.Clamp(prefs.BubbleSeconds, 3, 15); animate.Checked = prefs.PetAnimate; topmost.Checked = prefs.PetTopMost;
        syncing = false; preview.Invalidate(); settingsPage.Sync();
    }
    void ResizeCards()
    {
        int width = Math.Max(400, devicePage.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 6);
        foreach (var card in cards.Values) card.Width = width; empty.Width = width;
        listTools.MaximumSize = new(width, 0);
    }
    void MoveDevice(string id, int position)
    {
        DeviceListPolicy.Move(prefs, currentDevices, id, position); apply(); ArrangeDevices();
    }
    void HideDevice(string id)
    {
        var device = currentDevices.First(d => d.Id == id);
        device.Settings.Hidden = !device.Settings.Hidden;
        changeDevice(id, false); ArrangeDevices();
    }
    void ArrangeDevices()
    {
        devicePage.SuspendLayout();
        var ordered = DeviceListPolicy.Ordered(currentDevices, prefs);
        int hidden = ordered.Count(d => DeviceListPolicy.Hidden(d, prefs));
        listSummary.Text = $"共 {ordered.Count} 台 · 隐藏 {hidden} 台" + (showHidden.Checked ? " · 正在查看全部设备" : "");
        foreach (var card in cards.Values) card.Visible = false;
        for (int i = 0; i < ordered.Count; i++)
        {
            var card = cards[ordered[i].Id]; card.SetListState(i + 1);
            card.Visible = showHidden.Checked || !DeviceListPolicy.Hidden(ordered[i], prefs);
            devicePage.Controls.SetChildIndex(card, i + 2);
        }
        empty.Visible = ordered.Count == 0 || (!showHidden.Checked && hidden == ordered.Count);
        if (ordered.Count > 0 && hidden == ordered.Count) empty.Text = "设备已被隐藏。勾选“查看隐藏设备”可查看和恢复；\n自动隐藏的蓝牙设备确认在线后会重新显示，也可开启“显示不在线的蓝牙设备”。";
        devicePage.ResumeLayout(); ResizeCards();
    }
    public void SetDevices(List<DeviceView> devices, string message, bool busy, bool demo)
    {
        currentDevices = devices;
        refreshButton.Text = busy ? "读取中…" : "刷新电量"; refreshButton.Enabled = !busy;
        footer.Text = (demo ? "模拟演示 · 非真实设备电量" : message) + $"\n每 {Math.Clamp(prefs.RefreshSeconds, 30, 3600)} 秒刷新 · 关闭窗口后继续托盘运行";
        empty.Visible = devices.Count == 0;
        empty.Text = busy ? "正在读取 2.4G、HID 和已配对蓝牙设备…\n\n首次读取可能需要几秒。" : "尚未读到电量\n\n" + message + "\n\n请连接并唤醒外设后刷新；蓝牙设备需向 Windows 或标准电量服务提供读数。";
        foreach (var device in devices)
        {
            if (!cards.TryGetValue(device.Id, out var card))
            {
                card = new DeviceCard(device, changeDevice, focus, HideDevice, MoveDevice) { Margin = new(0, 0, 0, 14) };
                float scale = DeviceDpi / 96f; if (scale != 1) card.Scale(new SizeF(scale, scale));
                cards[device.Id] = card; devicePage.Controls.Add(card);
            }
            else card.UpdateModel(device);
        }
        ArrangeDevices();
    }
}


sealed class AboutPage : Panel
{
    public AboutPage()
    {
        Dock = DockStyle.Fill; BackColor = Ui.Background; Padding = new(0, 8, 0, 0);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new(16), BackColor = Ui.Card };
        layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.Absolute, 68));
        layout.RowStyles.Add(new(SizeType.Absolute, 34));
        layout.RowStyles.Add(new(SizeType.Absolute, 38));
        layout.RowStyles.Add(new(SizeType.Percent, 100));
        Controls.Add(layout);
        string version = typeof(AboutPage).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .Cast<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion.Split('+')[0] ?? Application.ProductVersion;
        layout.Controls.Add(new Label { Text = $"BatteryPet · 电量桌宠\n开发者：清风    ·    当前版本：v{version}", Dock = DockStyle.Fill, Font = Ui.Font(12, FontStyle.Bold), ForeColor = Ui.Ink }, 0, 0);
        layout.Controls.Add(new Label { Text = "版本目录 · 历史开发记录不代表已在 GitHub 发布", Dock = DockStyle.Fill, ForeColor = Ui.Muted }, 0, 1);
        var versions = new ComboBox { Name = "versionDirectory", DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Font = Ui.Font(10) };
        var details = new RichTextBox { Name = "versionDetails", Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Ui.Card, ForeColor = Ui.Ink, Font = Ui.Font(10), DetectUrls = false, ScrollBars = RichTextBoxScrollBars.Vertical };
        layout.Controls.Add(versions, 0, 2); layout.Controls.Add(details, 0, 3);
        var entries = new List<(string Title, string Text)>();
        using var stream = typeof(AboutPage).Assembly.GetManifestResourceStream("BatteryPet.Changelog");
        if (stream != null)
        {
            using var reader = new StreamReader(stream);
            foreach (var block in System.Text.RegularExpressions.Regex.Split(reader.ReadToEnd(), @"(?m)(?=^#{1,2} (?:v\d|\[\d))"))
            {
                var lines = block.Trim().Split('\n');
                if (!System.Text.RegularExpressions.Regex.IsMatch(lines[0], @"^#{1,2} (?:v\d|\[\d)")) continue;
                string title = lines[0].TrimStart('#', ' ').Split(" / ")[0];
                var body = new List<string>();
                foreach (string raw in lines.Skip(1))
                {
                    if (raw.StartsWith("# Changelog")) break;
                    if (!raw.StartsWith("- ")) continue;
                    string text = raw[2..].Trim();
                    if (text.Contains(" / ")) text = text.Split(" / ").Last();
                    body.Add("•  " + text.Replace("`", ""));
                }
                entries.Add((title, string.Join("\n\n", body)));
            }
        }
        foreach (var entry in entries) versions.Items.Add(entry.Title);
        versions.SelectedIndexChanged += (_, _) => details.Text = entries[versions.SelectedIndex].Text;
        if (entries.Count > 0) versions.SelectedIndex = 0;
        else details.Text = "暂无版本记录。";
    }
}
