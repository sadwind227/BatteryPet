// SPDX-License-Identifier: MIT
// Copyright (c) 2026 sadwind227
namespace BatteryPet;

static class AdaptationChecks
{
    public static void Run()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "adapt-check"); Directory.CreateDirectory(root);
        var results = new List<string>();
        try
        {
            string script = Path.Combine(root, "battery-fixture.js");
            File.WriteAllText(script, "// Synthetic evidence; not a real supported device\nasync function battery(device) { const data = await device.receiveFeatureReport(9); return data.getUint8(1); }\n");
            var analysis = AdaptationTool.Run(new[] { "analyze", "--program", script }).GetAwaiter().GetResult();
            if (analysis.GetProperty("evidence").GetArrayLength() == 0) throw new Exception("Desktop analysis process returned no fixture evidence");
            results.Add("PASS · 桌面程序完整调用链可分析脚本证据");
            string report = Path.Combine(root, "analysis.json"); File.WriteAllText(report, analysis.GetRawText());
            string workspace = Path.Combine(root, "workspace"); Directory.CreateDirectory(workspace);
            string sentinel = Path.Combine(workspace, "AGENTS.md"); File.WriteAllText(sentinel, "unchanged fixture workspace rules");
            var exported = AdaptationTool.Run(new[] { "export", "--report", report, "--workspace", workspace, "--agent", "codex", "--target", "1234:5678" }).GetAwaiter().GetResult();
            string bundle = exported.GetProperty("bundle").GetString()!;
            if (!File.Exists(Path.Combine(bundle, "TASK.md")) || !File.Exists(Path.Combine(bundle, "adapter.schema.json"))) throw new Exception("Incomplete task export");
            if (File.ReadAllText(sentinel) != "unchanged fixture workspace rules") throw new Exception("Existing workspace rules were modified");
            results.Add("PASS · 桌面程序完整调用链可导出独立任务包，原工作区规则保持原样");
            File.WriteAllText(Path.Combine(root, "一行命令示例.txt"), exported.GetProperty("command").GetString());
            results.Add("INFO · 已生成命令示例，但未执行真实 agent、未消耗模型额度");
            int apps = ProgramDiscovery.Discover().Count;
            results.Add($"INFO · 客户端安装信息枚举完成，{apps} 项；未运行任何厂商程序");
        }
        catch (Exception e) { results.Add("FAIL · " + e); }
        File.WriteAllLines(Path.Combine(root, "结果.txt"), results);
    }
}
