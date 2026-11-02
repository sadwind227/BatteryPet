// SPDX-License-Identifier: MIT
// Copyright (c) 2026 sadwind227
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BatteryPet;

static class NativeAdapters
{
    public static string Storage => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BatteryPet", "adapters");
    public static JsonObject Load(string path)
    {
        using var stream = File.OpenRead(path); if (stream.Length > 2_000_000) throw new InvalidDataException("JSON 文件超过大小限制");
        using var reader = new StreamReader(stream, Encoding.UTF8, true); return JsonNode.Parse(reader.ReadToEnd()) as JsonObject ?? throw new InvalidDataException("JSON 须为对象");
    }
    public static void Save(string path, JsonNode value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, value.ToJsonString(new() { WriteIndented = true })); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    static void Fields(JsonNode? node, params string[] fields)
    {
        if (node is not JsonObject obj || !obj.Select(p => p.Key).Order().SequenceEqual(fields.Order())) throw new InvalidDataException("字段不完整或含额外执行字段");
    }
    static int Number(JsonNode? node, int min, int max)
    {
        if (node == null || !int.TryParse(node.ToJsonString(), out int n) || n < min || n > max) throw new InvalidDataException("整数超出允许范围"); return n;
    }
    public static string Text(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : throw new InvalidDataException("须为字符串");
    public static JsonObject Validate(JsonObject d, HashSet<string>? evidenceIds = null)
    {
        Fields(d, "schema_version", "name", "kind", "device", "report", "evidence", "notes"); Number(d["schema_version"], 1, 1);
        string name = Text(d["name"]), kind = Text(d["kind"]); if (name.Length is < 1 or > 100 || kind is not ("hid_feature" or "hid_input" or "unsupported")) throw new InvalidDataException("适配名称或类型无效");
        Fields(d["device"], "vid", "pid", "usage_page", "usage"); var device = d["device"]!;
        foreach (var key in new[] { "vid", "pid" }) if (!System.Text.RegularExpressions.Regex.IsMatch(Text(device[key]), "\\A[0-9a-fA-F]{4}\\z")) throw new InvalidDataException("VID/PID 须为四位十六进制");
        Number(device["usage_page"], 0xFF00, 0xFFFF); Number(device["usage"], 1, 65535);
        Fields(d["report"], "id", "length", "battery_offset", "scale_num", "scale_den", "charge_offset", "charge_mask"); var r = d["report"]!;
        Number(r["id"], 0, 255); int length = Number(r["length"], 2, 512); Number(r["battery_offset"], 1, length - 1); Number(r["scale_num"], 1, 100); Number(r["scale_den"], 1, 1000);
        if ((r["charge_offset"] == null) != (r["charge_mask"] == null)) throw new InvalidDataException("充电偏移和掩码须同时提供");
        if (r["charge_offset"] != null) { Number(r["charge_offset"], 1, length - 1); Number(r["charge_mask"], 1, 255); }
        if (d["evidence"] is not JsonArray evidence || evidence.Count is < 1 or > 20) throw new InvalidDataException("须提供来源证据");
        foreach (var item in evidence) { Fields(item, "source_id", "reason"); string id = Text(item!["source_id"]), reason = Text(item["reason"]); if (reason.Length is < 1 or > 2000 || evidenceIds != null && !evidenceIds.Contains(id)) throw new InvalidDataException("证据编号或说明无效"); }
        if (Text(d["notes"]).Length > 8000) throw new InvalidDataException("适配备注过长"); return d;
    }
    // Matches Python json.dumps(sort_keys=True, ensure_ascii=False), including default spaces.
    static string Quote(string text)
    {
        var b = new StringBuilder("\""); foreach (char c in text) b.Append(c switch { '"' => "\\\"", '\\' => "\\\\", '\b' => "\\b", '\f' => "\\f", '\n' => "\\n", '\r' => "\\r", '\t' => "\\t", < ' ' => "\\u" + ((int)c).ToString("x4"), _ => c.ToString() }); return b.Append('"').ToString();
    }
    static string Canonical(JsonNode? n) => n switch
    {
        null => "null", JsonObject obj => "{" + string.Join(", ", obj.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => Quote(p.Key) + ": " + Canonical(p.Value))) + "}",
        JsonArray arr => "[" + string.Join(", ", arr.Select(Canonical)) + "]", JsonValue v when v.TryGetValue<string>(out var text) => Quote(text), _ => n.ToJsonString()
    };
    public static string Fingerprint(JsonObject d) => NativeLogitech.Hash(Canonical(Validate(d)));
    public static JsonObject Decode(JsonObject d, byte[] reply)
    {
        Validate(d); var r = d["report"]!; int N(string key) => r[key]!.GetValue<int>();
        if (reply.Length != N("length") || reply[0] != N("id")) throw new InvalidDataException("报告编号或长度不匹配");
        int raw = reply[N("battery_offset")]; double percent = raw * (double)N("scale_num") / N("scale_den");
        if (percent is < 0 or > 100) throw new InvalidDataException("电量超出 0–100，拒绝猜测或截断");
        return new() { ["percent"] = (int)(percent + .5), ["charging"] = r["charge_offset"] != null && (reply[N("charge_offset")] & N("charge_mask")) != 0, ["charge_known"] = r["charge_offset"] != null, ["battery_raw"] = raw };
    }
    public static JsonObject Probe(JsonObject d)
    {
        Validate(d); if (Text(d["kind"]) == "unsupported") throw new InvalidDataException("候选协议尚不支持，不能试读或启用");
        var match = d["device"]!; var r = d["report"]!; var errors = new List<string>();
        foreach (var iface in NativeHid.Enumerate().Where(i => i.Vid == Convert.ToInt32(Text(match["vid"]), 16) && i.Pid == Convert.ToInt32(Text(match["pid"]), 16) && i.UsagePage == match["usage_page"]!.GetValue<int>() && i.Usage == match["usage"]!.GetValue<int>()))
        {
            try
            {
                bool feature = Text(d["kind"]) == "hid_feature"; int length = r["length"]!.GetValue<int>(), id = r["id"]!.GetValue<int>();
                if ((feature ? iface.FeatureLength : iface.InputLength) != length) throw new InvalidDataException("实际 HID 报告长度不匹配");
                using var dev = new NativeHid(iface.Path); byte[]? reply = null;
                if (feature) reply = dev.Feature(id, length);
                else { var timer = Stopwatch.StartNew(); while (timer.ElapsedMilliseconds < 1200) { var input = dev.Read(); if (input != null && input.Length > 0 && input[0] == id) { reply = input; break; } } }
                if (reply == null) throw new IOException("未返回所需报告，可能需要查询命令；不会自动重放");
                var result = Decode(d, reply); string key = NativeLogitech.Hash(iface.Identity)[..16];
                result["id"] = "extension:" + Fingerprint(d)[..12] + ":" + key; result["name"] = Text(d["name"]); result["status"] = "已验证扩展"; result["connection"] = "HID"; result["device_key"] = key; result["firmware"] = iface.Version; result["report_hash"] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(reply)).ToLowerInvariant(); return result;
            }
            catch (Exception ex) { errors.Add(ex.Message); }
        }
        throw new IOException(errors.Count > 0 ? string.Join("；", errors) : "未找到精确匹配的设备接口");
    }
    public static JsonObject Status(JsonObject d, string? storage = null)
    {
        string fp = Fingerprint(d), path = Path.Combine(storage ?? Storage, "verification", fp + ".json"); var groups = new Dictionary<string, HashSet<int>>();
        if (File.Exists(path))
        {
            var receipt = Load(path);
            if (receipt["fingerprint"]?.GetValue<string>() == fp && receipt["observations"] is JsonArray arr)
            foreach (var sample in arr.OfType<JsonObject>())
            {
                try
                {
                    if (sample["matched"]?.GetValue<bool>() != true) continue;
                    int expected = Number(sample["expected"], 0, 100); if (expected != Number(sample["observed"], 0, 100)) continue;
                    string key = sample["device_key"]!.ToJsonString() + ":" + sample["firmware"]!.ToJsonString();
                    if (!groups.TryGetValue(key, out var values)) groups[key] = values = new(); values.Add(expected);
                }
                catch (Exception) { /* Invalid observations cannot grant verification. */ }
            }
        }
        int count = groups.Values.Select(v => v.Count).DefaultIfEmpty().Max(); return new() { ["verified"] = Text(d["kind"]) != "unsupported" && count >= 2, ["distinct_values"] = count };
    }
    public static JsonObject Observe(JsonObject d, JsonObject result, int expected, string? storage = null)
    {
        if (expected is < 0 or > 100) throw new InvalidDataException("对照电量须为 0–100 整数");
        string fp = Fingerprint(d), path = Path.Combine(storage ?? Storage, "verification", fp + ".json");
        var receipt = File.Exists(path) ? Load(path) : new JsonObject { ["fingerprint"] = fp, ["observations"] = new JsonArray() };
        var observations = receipt["observations"] as JsonArray ?? new(); while (observations.Count >= 20) observations.RemoveAt(0);
        observations.Add(new JsonObject { ["expected"] = expected, ["observed"] = result["percent"]!.DeepClone(), ["matched"] = expected == result["percent"]!.GetValue<int>(), ["device_key"] = result["device_key"]!.DeepClone(), ["firmware"] = result["firmware"]!.DeepClone(), ["report_hash"] = result["report_hash"]!.DeepClone(), ["time"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 });
        if (receipt["observations"] == null) receipt["observations"] = observations; Save(path, receipt); return Status(d, storage);
    }
    public static void Enable(JsonObject d, string? storage = null)
    {
        storage ??= Storage; if (!Status(d, storage)["verified"]!.GetValue<bool>()) throw new InvalidDataException("需同一设备两个不同真实电量对照成功才能启用");
        string folder = Path.Combine(storage, "enabled"), path = Path.Combine(folder, Fingerprint(d) + ".json");
        if (!File.Exists(path) && Directory.Exists(folder) && Directory.GetFiles(folder, "*.json").Length >= 4) throw new InvalidDataException("最多启用四个扩展适配"); Save(path, d);
    }
    public static void Disable(JsonObject d, string? storage = null) => File.Delete(Path.Combine(storage ?? Storage, "enabled", Fingerprint(d) + ".json"));
    public static async Task<ScanResult> ScanInstalled(CancellationToken token)
    {
        var result = new ScanResult(); string folder = Path.Combine(Storage, "enabled"); if (!Directory.Exists(folder)) return result;
        foreach (var path in Directory.GetFiles(folder, "*.json").Order(StringComparer.Ordinal).Take(4))
        {
            try
            {
                var d = Validate(Load(path)); if (!Status(d)["verified"]!.GetValue<bool>()) throw new InvalidDataException("适配验证记录缺失");
                var read = await NativeProcess.Json(new[] { "--adapter-read", path }, TimeSpan.FromSeconds(2.5), token);
                if (read.TryGetProperty("error", out var error)) throw new IOException(error.GetString());
                result.devices.Add(read.Deserialize<Reading>()!);
            }
            catch (Exception ex) when (!token.IsCancellationRequested) { result.message += Path.GetFileNameWithoutExtension(path)[..Math.Min(8, Path.GetFileNameWithoutExtension(path).Length)] + "：" + ex.Message + "；"; }
        }
        return result;
    }
}

static class NativeProcess
{
    public static async Task<JsonElement> Json(IEnumerable<string> args, TimeSpan timeout, CancellationToken cancellationToken, Action<Process?>? track = null)
    {
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "BatteryPet.exe")) { WorkingDirectory = AppContext.BaseDirectory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = start }; using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(timeout);
        try
        {
            process.Start(); track?.Invoke(process);
            using var stop = deadline.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch { } });
            async Task<string> Bounded(StreamReader reader)
            {
                var text = new StringBuilder(); var buffer = new char[4096]; int count;
                while ((count = await reader.ReadAsync(buffer)) > 0) { if (text.Length + count > 2_000_000) { try { process.Kill(true); } catch { } throw new InvalidDataException("组件结果超过限制"); } text.Append(buffer, 0, count); } return text.ToString();
            }
            var output = Bounded(process.StandardOutput); var errors = Bounded(process.StandardError);
            try { await process.WaitForExitAsync(deadline.Token); }
            catch (OperationCanceledException) { try { process.Kill(true); } catch { } await process.WaitForExitAsync(); await Task.WhenAll(output, errors); cancellationToken.ThrowIfCancellationRequested(); throw new TimeoutException("读取组件超时，未启用新适配"); }
            string data = await output, error = await errors;
            if (process.ExitCode != 0) throw new IOException("读取组件退出：" + process.ExitCode + " " + error[..Math.Min(error.Length, 200)]);
            using var document = JsonDocument.Parse(data); return document.RootElement.Clone();
        }
        finally { track?.Invoke(null); }
    }
}
