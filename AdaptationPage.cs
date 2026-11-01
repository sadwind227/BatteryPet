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
        Heading("1  选择厂商程序并分析");
        Row(programs, Button("发现已安装程序", async () => await FindPrograms()));
        Row(source);
        var cancel = new SeaButton("取消当前操作"); cancel.Click += (_, _) => operation?.Cancel();
        Row(Button("选择程序…", () => PickProgram()), Button("选择目录…", () => PickDirectory(source)), Button("分析所选程序", Analyze), cancel);
        Note("仅分析选中目录的脚本、JSON 和 ASAR，不运行厂商程序。原生二进制/加密握手可能无法自动识别。");
        Row(report);
        Heading("2  导出到 agent 工作区");
        Row(deviceTargets);
        Row(new Label { Text = "目标 VID:PID", Width = 105, ForeColor = Ui.Muted, Padding = new(0, 5, 0, 0) }, target, agents);
        agents.Items.AddRange(new object[] { "Codex CLI", "Claude Code CLI", "DeepSeek Harness CLI", "Hermes CLI", "WorkBuddy（工作区任务）", "其他 agent（工作区任务）" }); agents.SelectedIndex = 0;
        Row(workspace, Button("选择工作区…", () => PickDirectory(workspace)));
        Row(Button("导出适配任务", Export), Button("复制命令/指令", () => { if (command.Text.Length > 0) Clipboard.SetText(command.Text); }), Button("打开任务包", () => { if (Directory.Exists(exportedBundle)) Process.Start(new ProcessStartInfo(exportedBundle) { UseShellExecute = true }); }));
        Row(command);
        Note("CLI 模式：复制命令后在终端运行，会使用该 agent 已有登录/额度。WorkBuddy 等桌面模式：在工作区发送生成的指令。只创建新任务子目录，不读取 agent 登录配置；发送前请检查任务片段。");
        Heading("3  导入候选并只读验证");
        Row(candidate, Button("导入候选…", Import));
        Row(compare, expected, new Label { Text = "%（填写厂商软件当前显示值）", AutoSize = true, ForeColor = Ui.Muted, Padding = new(0, 3, 0, 0) });
        Row(Button("只读试读", Probe), Button("启用已验证适配", () => CandidateAction("enable")), Button("禁用此扩展", () => CandidateAction("disable")));
        Note("当前扩展仅支持无需发送准备命令的 HID Feature 读取或被动 Input 报告。未知握手/写入协议不会执行。两次不同真实电量与对照一致后才能启用；导入本身不代表适配成功。");
        status.Dock = DockStyle.Bottom; status.Height = 48; status.Padding = new(4); Controls.Add(status); status.BringToFront();
        programs.SelectedIndexChanged += (_, _) => { if (programs.SelectedItem is InstalledProgram app) source.Text = app.Directory; };
        deviceTargets.SelectedIndexChanged += (_, _) => { if (deviceTargets.SelectedItem is string device && device.Length >= 9) target.Text = device[..9]; };
        source.TextChanged += (_, _) => { analysis = null; report.Clear(); command.Clear(); exportedBundle = ""; };
        workspace.TextChanged += (_, _) => command.Clear(); agents.SelectedIndexChanged += (_, _) => command.Clear(); target.TextChanged += (_, _) => command.Clear();
    }
    static TextBox Box(int width) => new() { Width = width, ReadOnly = true, BackColor = Ui.Card, BorderStyle = BorderStyle.FixedSingle };
    void Heading(string text) => stack.Controls.Add(new Label { Text = text, Font = Ui.Font(11, FontStyle.Bold), ForeColor = Ui.Ink, Height = 35, Width = 670, Padding = new(0, 8, 0, 0) });
    void Note(string text) => stack.Controls.Add(new Label { Text = text, Width = 670, Height = 48, ForeColor = Ui.Muted, Font = Ui.Font(8), Padding = new(2, 3, 0, 0) });
    void Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new(0, 0, 0, 7) };
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
        report.Text = text.ToString(); status.Text = "分析完成。请选择实际外设 VID:PID，再导出任务；证据不足时 agent 应返回不支持。";
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
