// SPDX-License-Identifier: MIT
// Copyright (c) 2026 sadwind227
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace BatteryPet;

sealed record InstalledProgram(string Name, string Publisher, string Version, string Directory)
{
    public override string ToString() => Name + (string.IsNullOrEmpty(Version) ? "" : " · " + Version);
}
static class ProgramDiscovery
{
    public static List<InstalledProgram> Discover()
    {
        var found = new List<InstalledProgram>();
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall == null) continue;
                foreach (var id in uninstall.GetSubKeyNames().Take(2000))
                {
                    try
                    {
                        using var entry = uninstall.OpenSubKey(id);
                        string name = entry?.GetValue("DisplayName") as string ?? "";
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        string directory = entry?.GetValue("InstallLocation") as string ?? "";
                        if (string.IsNullOrWhiteSpace(directory))
                        {
                            string icon = entry?.GetValue("DisplayIcon") as string ?? "";
                            string exe = icon.StartsWith('"') ? icon.Split('"').ElementAtOrDefault(1) ?? "" : icon.Split(',')[0];
                            directory = Path.GetDirectoryName(exe) ?? "";
                        }
                        directory = directory.Trim().Trim('"');
                        if (Path.IsPathFullyQualified(directory) && !directory.StartsWith(@"\\") && System.IO.Directory.Exists(directory))
                            found.Add(new(name, entry?.GetValue("Publisher") as string ?? "", entry?.GetValue("DisplayVersion") as string ?? "", directory));
                    }
                    catch { }
                }
            }
            catch { }
        }
        return found.DistinctBy(p => (p.Name.ToUpperInvariant(), p.Directory.ToUpperInvariant())).OrderByDescending(p => new[] { "logitech", "罗技", "razer", "雷蛇", "incott", "mouse", "鼠标" }.Any(k => (p.Name + p.Publisher).Contains(k, StringComparison.OrdinalIgnoreCase))).ThenBy(p => p.Name).ToList();
    }
}

static class AdaptationTool
{
    public static async Task<JsonElement> Run(IEnumerable<string> arguments, CancellationToken cancellationToken = default)
    {
        var start = new ProcessStartInfo(ProviderRunner.PythonPath()) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = AppContext.BaseDirectory };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "adapter_tool.py"));
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        start.Environment["PYTHONIOENCODING"] = "utf-8";
        using var process = new Process { StartInfo = start };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(TimeSpan.FromSeconds(45));
        process.Start(); var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException) { if (!process.HasExited) process.Kill(true); throw new Exception("适配操作已取消或超时，未启用新适配"); }
        string data = await output, error = await errors;
        if (data.Length > 2_000_000) throw new Exception("适配结果超过大小限制");
        if (string.IsNullOrWhiteSpace(data)) throw new Exception("适配组件没有返回结果：" + error[..Math.Min(error.Length, 200)]);
        using var json = JsonDocument.Parse(data);
        var result = json.RootElement.Clone();
        if (result.TryGetProperty("error", out var failed)) throw new Exception(failed.GetString());
        if (process.ExitCode != 0) throw new Exception("适配组件返回错误：" + error[..Math.Min(error.Length, 200)]);
        return result;
    }
}

sealed class AdaptationPage : Panel
{
    readonly ComboBox programs = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 360 };
    readonly ComboBox agents = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };
    readonly ComboBox deviceTargets = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 530 };
    readonly TextBox source = Box(530), workspace = Box(530), command = Box(670), candidate = Box(530);
    readonly TextBox target = new() { Text = "046D:C547", Width = 110, MaxLength = 9 };
    readonly CheckBox compare = new() { Text = "记录对照电量", AutoSize = true, ForeColor = Ui.Ink };
    readonly NumericUpDown expected = new() { Minimum = 0, Maximum = 100, Value = 50, Width = 60 };
    readonly TextBox report = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Width = 670, Height = 150, BackColor = Ui.Card, ForeColor = Ui.Ink, BorderStyle = BorderStyle.FixedSingle };
    readonly Label status = new() { AutoSize = false, Width = 680, Height = 45, ForeColor = Ui.Muted };
    readonly FlowLayoutPanel stack;
    readonly CancellationTokenSource lifetime = new();
    CancellationTokenSource? operation;
    bool busy;
    JsonElement? analysis;
    string exportedBundle = "";
    readonly List<Button> actions = new();
    public AdaptationPage()
    {
        source.PlaceholderText = "选择厂商程序或资源目录"; workspace.PlaceholderText = "选择已有 agent 工作区";
        candidate.PlaceholderText = "导入 candidate.json"; command.PlaceholderText = "导出后显示一行命令或工作区指令";
        status.Text = "先选择厂商程序并分析；已有内置支持的设备无需重复适配。";
        Dock = DockStyle.Fill; BackColor = Ui.Background; Padding = new(0, 8, 0, 0);
        stack = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Ui.Background };
        Controls.Add(stack);
        Heading("设备适配 · 新手操作指南");
        Note("设备已在“设备电量”页显示？无需适配。只有读不到电量时才使用本页。请先连接并唤醒外设，确认厂商客户端能显示电量。");
        Note("操作顺序：① 选择真正管理外设的厂商客户端 → ② 分析电量线索 → ③ 获取候选 JSON → ④ 试读并对照 → ⑤ 启用。找到线索不等于已经适配成功。");
        Heading("1  选择厂商客户端");
        Note("先点“发现已安装程序”，再从列表选择平时查看电量的客户端。列表也会包含无关程序和安装组件；ASUS Mouse HAL 等名称不代表一定包含可分析的电量代码。");
        Row(programs, Button("发现已安装程序", async () => await FindPrograms()));
        Row(Button("选择程序…", () => PickProgram()), Button("选择目录…", () => PickDirectory(source)));
        Note("列表里找不到？任选一种：选择程序 = 客户端 EXE；选择目录 = 客户端安装文件夹。这里只选择位置，不会运行厂商程序。");
        Row(source);
        Note("上面是将要分析的位置。若路径含 Package Cache、只有安装包或检查了 0 个脚本，请改选真正安装目录。可右键厂商客户端快捷方式 → 打开文件所在的位置。");
        var cancel = new SeaButton("取消当前操作"); cancel.Click += (_, _) => operation?.Cancel();
        Heading("2  开始分析，查看结果");
        var analyze = Button("分析所选程序", Analyze); analyze.Primary = true;
        Row(analyze, cancel);
        Note("点击“分析所选程序”查找电量线索；“取消当前操作”只停止本次分析，不删除文件。");
        Note("• 找到线索：继续下一步，尚不代表适配成功。\n• 检查 0 个脚本：先换成真正的安装目录。\n• 没有线索：客户端可能使用原生或加密代码，当前分析无法识别。");
        Row(report);
        Heading("3  获取候选适配文件（可选辅助）");
        Note("当前版本的本地分析只收集线索，尚不能直接生成全部私有协议。下面是现有的可选 agent 辅助入口；已有可信候选 JSON 可直接跳到第 4 步，无需导出任务。");
        Row(deviceTargets);
        Note("从上方设备列表选择本次要适配的外设。VID:PID 是设备身份编号，选中后自动填写；不清楚编号时不要猜填，也不要选择无关设备。");
        Row(new Label { Text = "目标 VID:PID", Width = 105, ForeColor = Ui.Muted, Padding = new(0, 5, 0, 0) }, target, agents);
        agents.Items.AddRange(new object[] { "Codex CLI", "Claude Code CLI", "DeepSeek Harness CLI", "Hermes CLI", "WorkBuddy（工作区任务）", "其他 agent（工作区任务）" }); agents.SelectedIndex = 0;
        Row(workspace, Button("选择工作区…", () => PickDirectory(workspace)));
        Note("“选择工作区”：选一个你准备用来处理适配任务的现有文件夹，不是厂商安装目录。“导出适配任务”：在该文件夹下新建任务包，不会修改原工作区配置。");
        Row(Button("导出适配任务", Export), Button("复制命令/指令", () => { if (command.Text.Length > 0) Clipboard.SetText(command.Text); }), Button("打开任务包", () => { if (Directory.Exists(exportedBundle)) Process.Start(new ProcessStartInfo(exportedBundle) { UseShellExecute = true }); }));
        Row(command);
        Note("“打开任务包”：查看 TASK.md（操作要求）和 analysis.json（分析片段）。“复制命令/指令”：复制上框内容，再交给所选 agent 处理。导出不会自动运行；完成后找到 candidate.json，再回到本页导入。");
        Note("CLI 模式：复制命令后在终端运行，会使用该 agent 已有登录/额度。WorkBuddy 等桌面模式：在工作区发送生成的指令。只创建新任务子目录，不读取 agent 登录配置；发送前请检查任务片段。");
        Heading("4  导入候选，对照真实电量");
        Note("“导入候选”：选择 candidate.json，只检查格式，不会立即启用。“只读试读”：尝试读取一次电量；失败时按底部错误处理，不表示鼠标没有连接。");
        Row(candidate, Button("导入候选…", Import));
        Row(compare, expected, new Label { Text = "%（填写厂商软件当前显示值）", AutoSize = true, ForeColor = Ui.Muted, Padding = new(0, 3, 0, 0) });
        Note("先在厂商客户端确认当前电量，再勾选“记录对照电量”、填写同一设备的数值并点“只读试读”。例如今天 80% 对照成功，电量自然降到 79% 后再对照；重复 80% 不算第二个值，请勿为验证刻意深度放电。");
        Row(Button("只读试读", Probe));
        Heading("5  启用或停用适配");
        Row(Button("启用已验证适配", () => CandidateAction("enable")), Button("禁用此扩展", () => CandidateAction("disable")));
        Note("“启用已验证适配”：两个不同真实电量均对照成功后，加入日常自动刷新；可回“设备电量”页查看。“禁用此扩展”：停止采集当前导入的扩展，不卸载厂商客户端，也不影响内置适配。");
        Note("当前扩展仅支持无需发送准备命令的 HID Feature 读取或被动 Input 报告。未知握手/写入协议不会执行。两次不同真实电量与对照一致后才能启用；导入本身不代表适配成功。");
        status.Dock = DockStyle.Bottom; status.Height = 48; status.Padding = new(4); Controls.Add(status); status.BringToFront();
        programs.SelectedIndexChanged += (_, _) => { if (programs.SelectedItem is InstalledProgram app) source.Text = app.Directory; };
        deviceTargets.SelectedIndexChanged += (_, _) => { if (deviceTargets.SelectedItem is string device && device.Length >= 9) target.Text = device[..9]; };
        source.TextChanged += (_, _) => { analysis = null; report.Clear(); command.Clear(); exportedBundle = ""; };
        workspace.TextChanged += (_, _) => command.Clear(); agents.SelectedIndexChanged += (_, _) => command.Clear(); target.TextChanged += (_, _) => command.Clear();
        stack.SizeChanged += (_, _) =>
        {
            int width = Math.Max(260, stack.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 12);
            foreach (Control child in stack.Controls)
            {
                if (child is Label) child.Width = width;
                if (child is FlowLayoutPanel row)
                {
                    row.MaximumSize = new(width, 0);
                    foreach (Control field in row.Controls)
                        if (field is TextBox || field is ComboBox)
                            field.Width = Math.Min((int)((field == target ? 110 : field == agents ? 210 : field == programs ? 360 : field == command || field == report ? 670 : 530) * DeviceDpi / 96f), width - 8);
                }
            }
        };
    }
    static TextBox Box(int width) => new() { Width = width, ReadOnly = true, BackColor = Ui.Card, BorderStyle = BorderStyle.FixedSingle };
    void Heading(string text) => stack.Controls.Add(new Label { Text = text, Font = Ui.Font(11, FontStyle.Bold), ForeColor = Ui.Ink, Height = 35, Width = 670, Padding = new(0, 8, 0, 0) });
    void Note(string text)
    {
        var label = new Label { Text = text, Width = 670, ForeColor = Ui.Muted, Font = Ui.Font(9), Padding = new(2, 5, 4, 5) };
        void Fit() => label.Height = TextRenderer.MeasureText(label.Text, label.Font, new Size(label.Width - label.Padding.Horizontal, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height + label.Padding.Vertical + 6;
        label.SizeChanged += (_, _) => Fit();
        Fit(); stack.Controls.Add(label);
    }
    void Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, MaximumSize = new(700, 0), Margin = new(0, 0, 0, 7) };
        row.Controls.AddRange(controls); stack.Controls.Add(row);
    }
    SeaButton Button(string text, Action action) => Button(text, () => { action(); return Task.CompletedTask; });
    SeaButton Button(string text, Func<Task> action)
    {
        var button = new SeaButton(text); actions.Add(button);
        button.Click += async (_, _) =>
        {
            if (busy) return;
            busy = true; operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); foreach (var b in actions) b.Enabled = false;
            try { await action(); }
            catch (Exception e) { if (!IsDisposed) status.Text = e.Message; }
            finally { operation.Dispose(); operation = null; busy = false; if (!IsDisposed) foreach (var b in actions) b.Enabled = true; }
        };
        return button;
    }
    async Task FindPrograms()
    {
        status.Text = "正在读取已安装程序列表…";
        var list = await Task.Run(ProgramDiscovery.Discover, lifetime.Token);
        if (IsDisposed) return;
        programs.Items.Clear(); programs.Items.AddRange(list.Cast<object>().ToArray());
        if (list.Count > 0) programs.SelectedIndex = 0;
        status.Text = $"发现 {list.Count} 个有安装位置的程序；未列出时可手动选择程序或目录。";
    }
    void PickProgram()
    {
        using var picker = new OpenFileDialog { Title = "选择要分析的厂商程序", Filter = "厂商程序或资源|*.exe;*.asar;*.js;*.json;*.py|所有文件|*.*", CheckFileExists = true };
        if (picker.ShowDialog(FindForm()) == DialogResult.OK) source.Text = picker.FileName;
    }
    void PickDirectory(TextBox box)
    {
        using var picker = new FolderBrowserDialog { Description = box == workspace ? "选择已有 agent 工作区（只创建新的任务子目录）" : "选择厂商程序资源目录", UseDescriptionForTitle = true };
        if (picker.ShowDialog(FindForm()) == DialogResult.OK) box.Text = picker.SelectedPath;
    }
    async Task<JsonElement> Tool(params string[] args) => await AdaptationTool.Run(args, operation?.Token ?? lifetime.Token);
    async Task Analyze()
    {
        if (string.IsNullOrEmpty(source.Text)) throw new Exception("请先选择厂商程序或目录");
        analysis = null; status.Text = "正在进行本地只读分析…";
        var result = await Tool("analyze", "--program", source.Text);
        if (IsDisposed) return;
        analysis = result;
        deviceTargets.Items.Clear();
        var targetList = result.GetProperty("devices").EnumerateArray().Where(d => d.GetProperty("vid").GetString() != "0000")
            .Select(d => d.GetProperty("vid").GetString() + ":" + d.GetProperty("pid").GetString() + " · " + d.GetProperty("name").GetString()).Distinct().ToArray();
        deviceTargets.Items.AddRange(targetList.Cast<object>().ToArray());
        if (targetList.Length > 0) deviceTargets.SelectedIndex = Math.Max(0, Array.FindIndex(targetList, t => t.StartsWith("046D:C547")));
        var text = new StringBuilder();
        text.AppendLine(result.GetProperty("summary").GetString()); text.AppendLine(result.GetProperty("known_match").GetString());
        text.AppendLine($"检查 {result.GetProperty("scanned_files")} 个脚本/配置，读取 {result.GetProperty("read_bytes")} 字节。");
        foreach (var evidence in result.GetProperty("evidence").EnumerateArray())
            text.AppendLine($"\r\n[{evidence.GetProperty("source_id")}] {evidence.GetProperty("file")}:{evidence.GetProperty("line")}\r\n{evidence.GetProperty("excerpt").GetString()}");
        report.Text = text.ToString();
        status.Text = result.GetProperty("evidence").GetArrayLength() == 0
            ? "未找到可用线索，尚未生成适配。若检查 0 个脚本，请重新选择客户端安装目录；已有内置支持则直接回“设备电量”页。"
            : "已找到候选线索，尚未适配成功。可选择实际外设并导出任务；得到 candidate.json 后按第 4 步验证。";
    }
    async Task Export()
    {
        if (!analysis.HasValue) throw new Exception("请先分析选中的厂商程序");
        if (string.IsNullOrEmpty(workspace.Text)) throw new Exception("请选择 agent 工作区目录");
        string reportPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BatteryPet", "adaptation", "latest-analysis.json");
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!); File.WriteAllText(reportPath, analysis.Value.GetRawText());
        string agent = new[] { "codex", "claude", "dsh", "hermes", "workspace", "workspace" }[Math.Clamp(agents.SelectedIndex, 0, 5)];
        var result = await Tool("export", "--report", reportPath, "--workspace", workspace.Text, "--agent", agent, "--target", target.Text.Trim());
        if (IsDisposed) return;
        exportedBundle = result.GetProperty("bundle").GetString()!;
        command.Text = result.GetProperty(agent == "workspace" ? "workspace_prompt" : "command").GetString();
        status.Text = "任务包已创建。先检查 TASK.md 与 analysis.json，再复制命令/指令交给 agent；结果不会自动启用。";
    }
    async Task Import()
    {
        using var picker = new OpenFileDialog { Title = "导入 agent 生成的候选适配", Filter = "适配 JSON|*.json", CheckFileExists = true };
        if (picker.ShowDialog(FindForm()) != DialogResult.OK) return;
        var result = await Tool("check", "--adapter", picker.FileName);
        if (IsDisposed) return;
        candidate.Text = picker.FileName; status.Text = $"候选：{result.GetProperty("name").GetString()} · {result.GetProperty("kind").GetString()}。导入未启用。\n{result.GetProperty("message").GetString()}";
    }
    async Task Probe()
    {
        if (string.IsNullOrEmpty(candidate.Text)) throw new Exception("请先导入候选适配");
        status.Text = "正在只读试读选定的厂商 HID 接口…";
        var args = new List<string> { "probe", "--adapter", candidate.Text };
        if (compare.Checked) { args.Add("--expected"); args.Add(((int)expected.Value).ToString()); }
        var result = await Tool(args.ToArray());
        if (IsDisposed) return;
        status.Text = $"读取到 {result.GetProperty("reading").GetProperty("percent")}% · 对照一致的不同电量值 {result.GetProperty("distinct_values")} 个\n{result.GetProperty("message").GetString()}";
    }
    async Task CandidateAction(string action)
    {
        if (string.IsNullOrEmpty(candidate.Text)) throw new Exception("请先导入候选适配");
        var result = await Tool(action, "--adapter", candidate.Text);
        if (!IsDisposed) status.Text = result.GetProperty("message").GetString();
    }
    protected override void Dispose(bool disposing) { if (disposing) { lifetime.Cancel(); lifetime.Dispose(); } base.Dispose(disposing); }
}
