// SPDX-License-Identifier: MIT
// Copyright (c) 2026 sadwind227
using System.Text.Json.Nodes;
using System.Text;

namespace BatteryPet;

static class NativeChecks
{
    public static List<string> Run(string output)
    {
        var checks = new List<string>();
        void Require(bool ok, string message) { if (!ok) throw new Exception(message); checks.Add(message); }
        void Reject(Action action, string message) { bool rejected = false; try { action(); } catch (Exception ex) when (ex is InvalidDataException or IOException or InvalidOperationException) { rejected = true; } Require(rejected, message); }
        string root = Path.Combine(output, "native-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        using var resource = typeof(NativeChecks).Assembly.GetManifestResourceStream("BatteryPet.MigrationFixtures.json")!;
        using var reader = new StreamReader(resource); var fixtures = JsonNode.Parse(reader.ReadToEnd())!.AsArray();
        foreach (var fixture in fixtures)
            Require(NativeAdapters.Fingerprint(fixture!["adapter"]!.AsObject()) == fixture["fingerprint"]!.GetValue<string>(), "原 Python 适配指纹与 C# 一致（Unicode、转义与排序兼容）");
        var d = fixtures[0]!["adapter"]!.DeepClone().AsObject();
        var decoded = NativeAdapters.Decode(d, new byte[] { 9, 0, 0, 1 });
        Require(decoded["percent"]!.GetValue<int>() == 0 && decoded["charging"]!.GetValue<bool>(), "0% 有效值和充电掩码正确解码");
        Reject(() => NativeAdapters.Decode(d, new byte[] { 9, 0, 101, 0 }), "越界电量拒绝，不截断成 100%");
        Reject(() => NativeAdapters.Decode(d, new byte[] { 8, 0, 50, 0 }), "错误报告 ID 拒绝");
        Reject(() => NativeAdapters.Decode(d, new byte[] { 9, 50 }), "短报告拒绝");
        var bad = d.DeepClone().AsObject(); bad["execute"] = "ignored";
        Reject(() => NativeAdapters.Validate(bad), "候选不接受额外执行字段");
        bad = d.DeepClone().AsObject(); bad["device"]!["usage_page"] = 1;
        Reject(() => NativeAdapters.Validate(bad), "候选不能监听标准键鼠输入接口");
        bad = d.DeepClone().AsObject(); bad["report"]!["charge_mask"] = null;
        Reject(() => NativeAdapters.Validate(bad), "充电字段须成对提供");
        Require(!NativeAdapters.Status(d, root)["verified"]!.GetValue<bool>(), "无记录不允许启用");
        Reject(() => NativeAdapters.Enable(d, root), "未经验证的适配不能启用");
        JsonObject Observation(int percent, string key = "device-a", int firmware = 1) => new() { ["percent"] = percent, ["device_key"] = key, ["firmware"] = firmware, ["report_hash"] = "fixture" };
        NativeAdapters.Observe(d, Observation(80), 80, root); NativeAdapters.Observe(d, Observation(80), 80, root);
        Require(!NativeAdapters.Status(d, root)["verified"]!.GetValue<bool>(), "重复相同电量不计第二个值");
        NativeAdapters.Observe(d, Observation(79, "device-b"), 79, root); NativeAdapters.Observe(d, Observation(78, "device-a", 2), 78, root);
        Require(!NativeAdapters.Status(d, root)["verified"]!.GetValue<bool>(), "不同物理设备或固件记录不混合");
        NativeAdapters.Observe(d, Observation(79), 79, root); NativeAdapters.Enable(d, root);
        string enabled = Path.Combine(root, "enabled", NativeAdapters.Fingerprint(d) + ".json");
        Require(File.Exists(enabled), "同设备两种不同电量可启用，使用原指纹文件名");
        NativeAdapters.Disable(d, root); Require(!File.Exists(enabled), "禁用只移除所选扩展");
        string receipt = Path.Combine(root, "verification", NativeAdapters.Fingerprint(d) + ".json");
        var original = NativeAdapters.Load(receipt); original["fingerprint"] = "wrong"; NativeAdapters.Save(receipt, original);
        Require(!NativeAdapters.Status(d, root)["verified"]!.GetValue<bool>(), "验证记录指纹不匹配时失效");
        Require(NativeLogitech.MissingMessage(new() { new("fixture", 0x046D, 0xC547, 0, 0, 0, 0, 0, 0, "", "", "fixture", "access denied") }).Contains("access denied"), "接收器访问被拒绝与未连接区分显示");
        var packet = NativeLogitech.Packet(1, 7, 1);
        Require(packet.Length == 20 && packet.Take(4).SequenceEqual(new byte[] { 0x11, 1, 7, 0x1D }), "HID++ 查询帧与原版一致");
        Require(NativeLogitech.Matches(packet, packet) && !NativeLogitech.Matches(new byte[] { 0x10, 1, 7, 0x1D }, packet), "HID++ 回应同时匹配类型、槽位、功能及软件 ID");
        string scripts = Path.Combine(root, "sources"); Directory.CreateDirectory(scripts);
        File.WriteAllText(Path.Combine(scripts, "battery.js"), "const api_key=\"do-not-export\"; function battery(){return navigator.hid.receiveFeatureReport(9);}");
        Directory.CreateDirectory(Path.Combine(scripts, ".codex")); File.WriteAllText(Path.Combine(scripts, ".codex", "private.js"), "battery private"); File.WriteAllText(Path.Combine(scripts, "auth.json"), "battery private");
        var analysis = NativeAdaptation.Analyze(scripts, false);
        Require(analysis["scanned_files"]!.GetValue<int>() == 1 && analysis["evidence"]!.AsArray().Count > 0, "原生分析提取脚本证据并跳过凭据及 agent 配置");
        Require(!analysis.ToJsonString().Contains("do-not-export") && analysis.ToJsonString().Contains("REDACTED"), "原生分析遮盖片段中的密钥值");
        File.WriteAllBytes(Path.Combine(scripts, "vendor.exe"), new byte[] { 0, 1 });
        Require(NativeAdaptation.Analyze(Path.Combine(scripts, "vendor.exe"), false)["evidence"]!.AsArray().Count > 0, "选择 EXE 仅分析同目录资源，不运行程序");
        string asar = Path.Combine(root, "asar"); Directory.CreateDirectory(asar); byte[] content = Encoding.UTF8.GetBytes("function battery(){return getFeatureReport(9);}");
        byte[] header = Encoding.UTF8.GetBytes(new JsonObject { ["files"] = new JsonObject { ["battery.js"] = new JsonObject { ["size"] = content.Length, ["offset"] = "0" } } }.ToJsonString());
        using (var stream = File.Create(Path.Combine(asar, "app.asar"))) using (var writer = new BinaryWriter(stream)) { writer.Write(4); writer.Write(8 + header.Length); writer.Write(4 + header.Length); writer.Write(header.Length); writer.Write(header); writer.Write(content); }
        var archive = NativeAdaptation.Analyze(asar, false);
        Require(archive["evidence"]!.AsArray().Count > 0 && !File.Exists(Path.Combine(asar, "battery.js")), "ASAR 原生只读分析，不提取或执行内容");
        string workspace = Path.Combine(root, "workspace ' 空格"); Directory.CreateDirectory(workspace); File.WriteAllText(Path.Combine(workspace, "AGENTS.md"), "unchanged");
        var export = NativeAdaptation.Export(analysis, workspace, "codex", "1234:5678"); string bundle = export["bundle"]!.GetValue<string>();
        Require(File.Exists(Path.Combine(bundle, "TASK.md")) && File.Exists(Path.Combine(bundle, "adapter.schema.json")) && File.ReadAllText(Path.Combine(workspace, "AGENTS.md")) == "unchanged", "任务包独立生成，保留工作区规则");
        Require(!export["command"]!.GetValue<string>().Contains("python") && export["command"]!.GetValue<string>().Contains("--adapter-tool run"), "一行命令通过本机 EXE 运行，无 Python");
        var report = NativeAdapters.Load(Path.Combine(bundle, "analysis.json")); string sourceId = report["evidence"]![0]!["source_id"]!.GetValue<string>(); d["evidence"]![0]!["source_id"] = sourceId;
        Require(NativeAdaptation.ExtractCandidate("```json\n" + d.ToJsonString() + "\n```", report)["kind"]!.GetValue<string>() == "hid_feature", "从 agent 文本提取并校验候选 JSON");
        d["device"]!["pid"] = "9999"; Reject(() => NativeAdaptation.ExtractCandidate(d.ToJsonString(), report), "Agent 结果必须匹配选定设备");
        string shimFolder = Path.Combine(root, "shim"); Directory.CreateDirectory(shimFolder); File.WriteAllText(Path.Combine(shimFolder, "cli.js"), "fixture"); File.WriteAllText(Path.Combine(shimFolder, "node.exe"), "fixture");
        string shim = Path.Combine(shimFolder, "agent.cmd"); File.WriteAllText(shim, "@\"%dp0%\\node.exe\" \"%dp0%\\cli.js\" %*");
        var command = NativeAdaptation.ResolveLauncher(new() { shim, "$(keep literal)" });
        Require(command[0].EndsWith("node.exe") && command[1].EndsWith("cli.js") && command[2] == "$(keep literal)", "npm shim 直接解析 Node 入口，不使用 shell");
        return checks;
    }
}
