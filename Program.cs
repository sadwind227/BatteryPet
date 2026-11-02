// SPDX-License-Identifier: MIT
// Copyright (c) 2026 sadwind227
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace BatteryPet;

static class Program
{
    [STAThread] static void Main(string[] args)
    {
        if (args.Contains("--bluetooth-scan"))
        {
            using var writer = new StreamWriter(Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false)) { AutoFlush = true };
            try
            {
                var result = BluetoothBatteryProvider.Scan(progress: result => writer.WriteLine(JsonSerializer.Serialize(result)));
                writer.WriteLine(JsonSerializer.Serialize(result));
            }
            catch (Exception ex) { writer.WriteLine(JsonSerializer.Serialize(new ScanResult { message = "蓝牙读取失败：" + ex.Message })); }
            return;
        }
        ApplicationConfiguration.Initialize();
        if (args.Contains("--ui-check")) { UiChecks.Run(); return; }
        if (args.Contains("--icon-check")) { IconPreview.Export(); return; }
        if (args.Contains("--adapt-check")) { AdaptationChecks.Run(); return; }
        if (args.Contains("--diagnose"))
        {
            try { var result = ProviderRunner.Read(_ => { }).GetAwaiter().GetResult(); File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "诊断结果.json"), JsonSerializer.Serialize(result)); }
            catch (Exception e) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "诊断结果.json"), e.ToString()); }
            return;
        }
        using var mutex = new Mutex(true, args.Contains("--demo") ? "BatteryPet.Desktop.Demo" : "BatteryPet.Desktop.v1", out bool first);
        if (!first)
        {
            bool quiet = args.Contains("--autostart") || args.Contains("--silent");
            try
            {
                string name = args.Contains("--demo") ? "settings-demo.json" : "settings.json";
                string config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BatteryPet", name);
                quiet |= JsonSerializer.Deserialize<Preferences>(File.ReadAllText(config))?.SilentStart == true;
            }
            catch { }
            if (!quiet) MessageBox.Show("电量桌宠已经在运行，请查看系统托盘。", "电量桌宠");
            return;
        }
        Application.Run(new App(args.Contains("--demo"), args.Contains("--silent")));
    }
}

sealed class Preferences
{
    public bool HideInactiveBluetooth { get; set; } = true;
    public List<string> DeviceOrder { get; set; } = new();
    public bool Pet { get; set; } = true;
    public int X { get; set; } = -1;
    public int Y { get; set; } = -1;
    public bool PetPositionSaved { get; set; }
    public int PetSize { get; set; } = 100;
    public string PetTheme { get; set; } = "薄荷海";
    public bool PetTopMost { get; set; } = true;
    public bool PetAnimate { get; set; } = true;
    public int BubbleSeconds { get; set; } = 6;
    public int RefreshSeconds { get; set; } = 60;
    public bool AutoStart { get; set; }
    public bool SilentStart { get; set; }
    public string PetImagePath { get; set; } = "";
    public string PetImageName { get; set; } = "";
    public bool ClickSoundEnabled { get; set; }
    public string ClickSoundPath { get; set; } = "";
    public string ClickSoundName { get; set; } = "";
    public Dictionary<string, DeviceSettings> Devices { get; set; } = new();
}
sealed class DeviceSettings
{
    public bool Hidden { get; set; }
    public string Style { get; set; } = "数字";
    public int Threshold { get; set; } = 20;
    public bool Notify { get; set; } = true;
    public bool Show { get; set; } = true;
}
sealed class Reading
{
    public string id { get; set; } = "";
    public string name { get; set; } = "外设";
    public int? percent { get; set; }
    public bool charging { get; set; }
    public string status { get; set; } = "未知";
    public string connection { get; set; } = "HID";
    public bool fresh { get; set; } = true;
    public bool charge_known { get; set; } = true;
}
sealed class ScanResult
{
    public List<Reading> devices { get; set; } = new();
    public string message { get; set; } = "";
}
sealed class DeviceState
{
    public Reading Reading = new();
    public DateTime Updated;
    public bool Fresh;
    public bool Alerted;
    public NotifyIcon Tray = new();
    public Icon? Icon;
}

sealed class App : ApplicationContext
{
    const int MinRefreshSeconds = 30, MaxRefreshSeconds = 3600;
    readonly string config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BatteryPet", "settings.json");
    readonly Preferences prefs;
    readonly Dictionary<string, DeviceState> devices = new();
    readonly NotifyIcon manager = new();
    readonly System.Windows.Forms.Timer timer = new() { Interval = 60000 };
    readonly Form dispatcher = new();
    readonly PetWindow pet;
    DashboardForm? panel;
    bool busy, closing;
    readonly bool demo;
    string message = "准备读取设备…";
    string? focused;
    Process? worker;
    readonly CancellationTokenSource shutdown = new();

    public App(bool demo, bool forcedSilent = false)
    {
        this.demo = demo;
        if (demo) config = Path.Combine(Path.GetDirectoryName(config)!, "settings-demo.json");
        try { prefs = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(config)) ?? new(); }
        catch { prefs = new(); }
        if (!demo) { try { prefs.AutoStart = StartupRegistration.IsEnabled(); } catch { prefs.AutoStart = false; } }
        var startup = StartupPolicy.Resolve(prefs, forcedSilent, demo);
        _ = dispatcher.Handle;
        prefs.PetSize = Math.Clamp(prefs.PetSize, 60, 180);
        prefs.BubbleSeconds = Math.Clamp(prefs.BubbleSeconds, 3, 15);
        prefs.RefreshSeconds = Math.Clamp(prefs.RefreshSeconds, MinRefreshSeconds, MaxRefreshSeconds);
        timer.Interval = prefs.RefreshSeconds * 1000;
        pet = new PetWindow(prefs, PetReading, Refresh, OpenPanel, TogglePet, SavePosition, ApplyPetSettings);
        var area = Screen.PrimaryScreen!.WorkingArea;
        bool hasPosition = prefs.PetPositionSaved || prefs.X != -1 || prefs.Y != -1;
        pet.Location = hasPosition ? new Point(prefs.X, prefs.Y) : area.Location;
        if (!hasPosition || !Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(pet.Bounds))) pet.MoveToBottomRight();
        pet.EnsureOnScreen();
        manager.Icon = PixelIcon.Create(null, 20, "圆环", true, false);
        manager.Text = demo ? "电量桌宠 · 模拟演示" : "电量桌宠";
        manager.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) OpenPanel(); };
        manager.Visible = true;
        if (startup.ShowPet) pet.Show();
        manager.ContextMenuStrip = CommonMenu();
        timer.Tick += (_, _) => Refresh(); timer.Start();
        dispatcher.BeginInvoke(() => { if (startup.ShowPanel) OpenPanel(); Refresh(); });
    }

    DeviceSettings Settings(string id)
    {
        if (!prefs.Devices.TryGetValue(id, out var settings)) prefs.Devices[id] = settings = new();
        return settings;
    }
    void CheckAlert(DeviceState state, DeviceSettings settings)
    {
        if (!state.Fresh || DateTime.Now - state.Updated > TimeSpan.FromMinutes(2)) return;
        var r = state.Reading;
        if (r.percent >= Math.Min(100, settings.Threshold + 5)) state.Alerted = false;
        if (!demo && settings.Notify && r.percent < settings.Threshold && !state.Alerted)
        {
            manager.ShowBalloonTip(6000, "设备电量偏低", $"{r.name} 剩余 {r.percent}%（提醒阈值 {settings.Threshold}%）", ToolTipIcon.Warning);
            state.Alerted = true;
        }
    }
    void Save()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(config)!); File.WriteAllText(config + ".tmp", JsonSerializer.Serialize(prefs, new JsonSerializerOptions { WriteIndented = true })); File.Move(config + ".tmp", config, true); }
        catch (Exception e) { message = "设置保存失败：" + e.Message; UpdatePanel(); }
    }
    void SavePosition() { prefs.X = pet.Left; prefs.Y = pet.Top; prefs.PetPositionSaved = true; Save(); }
    void ApplyPetSettings()
    {
        pet.ApplySettings(); SavePosition(); panel?.SyncAppearance();
    }
    void ApplyRefreshSettings()
    {
        prefs.RefreshSeconds = Math.Clamp(prefs.RefreshSeconds, MinRefreshSeconds, MaxRefreshSeconds);
        timer.Stop();
        timer.Interval = prefs.RefreshSeconds * 1000;
        timer.Start();
        Save();
        UpdatePanel();
    }
    void SetAutoStart(bool enabled)
    {
        try { if (!demo) StartupRegistration.SetEnabled(enabled); prefs.AutoStart = enabled; Save(); }
        catch (Exception e) { MessageBox.Show("无法更改开机启动设置：" + e.Message, "电量桌宠", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        panel?.SyncAppearance();
    }
    void TogglePet() { prefs.Pet = !pet.Visible; if (prefs.Pet) pet.Show(); else pet.Hide(); Save(); panel?.SyncAppearance(); manager.ContextMenuStrip?.Dispose(); manager.ContextMenuStrip = CommonMenu(); foreach (var pair in devices) UpdateTray(pair.Key, pair.Value); }
    ContextMenuStrip CommonMenu()
    {
        var menu = new ContextMenuStrip { Renderer = new SeaMenuRenderer() };
        menu.Items.Add("打开设备面板", null, (_, _) => OpenPanel());
        menu.Items.Add("刷新全部", null, (_, _) => Refresh());
        menu.Items.Add(new ToolStripMenuItem("显示桌宠", null, (_, _) => TogglePet()) { Checked = pet.Visible });
        menu.Items.Add("退出", null, (_, _) => ExitThread());
        return menu;
    }
    async void Refresh()
    {
        if (busy || closing) return;
        busy = true; message = "正在刷新…"; UpdatePanel(); pet.DataChanged();
        try
        {
            ScanResult result;
            if (demo) result = new() { message = "模拟演示 · 数值不是实际设备电量", devices = new() { new Reading { id = "demo", name = "演示鼠标（模拟）", percent = 68, status = "模拟数据" } } };
            else
            {
                result = await ProviderRunner.Read(p => worker = p, shutdown.Token);
            }
            if (closing) return;
            foreach (var state in devices.Values) state.Fresh = false;
            foreach (var read in result.devices.Where(r => r.percent is >= 0 and <= 100))
            {
                if (!devices.TryGetValue(read.id, out var state))
                {
                    // Remembered Bluetooth reports with unknown connectivity stay in the panel first.
                    if (!read.fresh && read.connection.StartsWith("蓝牙") && !prefs.Devices.ContainsKey(read.id))
                        prefs.Devices[read.id] = new DeviceSettings { Show = false };
                    state = new(); devices.Add(read.id, state);
                    var id = read.id;
                    state.Tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) Refresh(); };
                    focused ??= id;
                }
                state.Reading = read; state.Fresh = read.fresh; state.Updated = DateTime.Now;
                var s = Settings(read.id);
                CheckAlert(state, s);
            }
            message = result.message;
            ProviderRunner.Log(JsonSerializer.Serialize(result));
            foreach (var pair in devices) UpdateTray(pair.Key, pair.Value);
        }
        catch (Exception e) { ProviderRunner.Log(e.ToString()); if (!closing) { message = e.Message; foreach (var pair in devices) { pair.Value.Fresh = false; UpdateTray(pair.Key, pair.Value); } } }
        finally { worker = null; busy = false; if (!closing) { UpdatePanel(); pet.DataChanged(); } }
    }
    void UpdateTray(string id, DeviceState state)
    {
        var s = Settings(id); var r = state.Reading;
        var old = state.Icon;
        state.Icon = PixelIcon.Create(r.percent, s.Threshold, s.Style, state.Fresh, r.charging);
        state.Tray.Icon = state.Icon; old?.Dispose(); state.Tray.Visible = s.Show;
        string text = $"{r.name} · {r.percent}% · {r.connection} · {(state.Fresh ? (r.charging ? "充电中" : r.status) : "上次/Windows报告值")} · {state.Updated:HH:mm:ss}";
        state.Tray.Text = text[..Math.Min(text.Length, 63)];
        state.Tray.ContextMenuStrip?.Dispose();
        var menu = CommonMenu();
        menu.Items.Insert(0, new ToolStripMenuItem("在桌宠中显示此设备", null, (_, _) => { focused = id; pet.DataChanged(); }));
        var styles = new ToolStripMenuItem("图标样式");
        foreach (var style in new[] { "数字", "圆环", "电池" }) styles.DropDownItems.Add(new ToolStripMenuItem(style, null, (_, _) => { s.Style = style; Save(); UpdateTray(id, state); UpdatePanel(); }) { Checked = s.Style == style });
        menu.Items.Insert(0, styles); state.Tray.ContextMenuStrip = menu;
    }
    PetSnapshot PetReading()
    {
        if (focused == null || !devices.TryGetValue(focused, out var d))
            return new("等待设备连接", null, false, false, busy, "在设备面板查看连接状态", demo);
        return new(d.Reading.name, d.Reading.percent, d.Fresh, d.Reading.charging, busy,
            d.Fresh ? $"更新于 {d.Updated:HH:mm:ss}" : $"上次读数 {d.Updated:HH:mm:ss}", demo, Settings(focused).Threshold);
    }
    void OpenPanel()
    {
        if (panel == null)
        {
            panel = new DashboardForm(prefs, Refresh, TogglePet, ApplyPetSettings, ApplyRefreshSettings,
                id => { focused = id; pet.DataChanged(); UpdatePanel(); },
                (id, recheck) => { if (devices.TryGetValue(id, out var d)) { if (recheck) d.Alerted = false; CheckAlert(d, Settings(id)); UpdateTray(id, d); } Save(); },
                SetAutoStart, () => pet.Visible);
            panel.FormClosing += (_, e) => { if (!closing) { e.Cancel = true; panel.Hide(); } };
        }
        UpdatePanel(); panel.Show(); panel.Activate();
    }
    void UpdatePanel()
    {
        panel?.SetDevices(devices.Select(p => new DeviceView(p.Key, p.Value.Reading.name,
            p.Value.Reading.percent, p.Value.Fresh, p.Value.Reading.charging, p.Value.Updated,
            focused == p.Key, Settings(p.Key), p.Value.Reading.connection, p.Value.Reading.status)).ToList(), message, busy, demo);
    }
    protected override void ExitThreadCore()
    {
        closing = true; timer.Stop(); timer.Dispose();
        shutdown.Cancel(); shutdown.Dispose();
        try { if (worker is { HasExited: false }) worker.Kill(true); } catch { }
        SavePosition(); foreach (var d in devices.Values) { d.Tray.Dispose(); d.Icon?.Dispose(); }
        manager.Dispose(); pet.Close(); pet.Dispose(); panel?.Dispose(); dispatcher.Dispose(); base.ExitThreadCore();
    }
}

