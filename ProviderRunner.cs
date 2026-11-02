// SPDX-License-Identifier: MIT
// Copyright (c) 2026 sadwind227
using System.Diagnostics;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace BatteryPet;

static class ProviderRunner
{
    public static string PythonPath()
    {
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        foreach (var family in new[] { @"Software\Python\PythonCore", @"Software\WOW6432Node\Python\PythonCore" })
        {
            try
            {
                using var core = root.OpenSubKey(family);
                if (core == null) continue;
                foreach (var version in core.GetSubKeyNames().OrderByDescending(x => x))
                {
                    using var key = core.OpenSubKey(version + @"\InstallPath");
                    string? path = key?.GetValue("ExecutablePath") as string;
                    if (string.IsNullOrEmpty(path) && key?.GetValue("") is string folder) path = Path.Combine(folder, "python.exe");
                    if (path != null && File.Exists(path)) return path;
                }
            }
            catch (System.Security.SecurityException) { }
            catch (UnauthorizedAccessException) { }
        }
        return "python";
    }

    public static Task<ScanResult> Read(Action<Process?> track, CancellationToken cancellationToken = default)
        => Collect(ReadPython(track, cancellationToken), ReadBluetooth(cancellationToken), cancellationToken);

    public static async Task<ScanResult> Collect(Task<ScanResult> hidTask, Task<ScanResult> bluetoothTask, CancellationToken cancellationToken = default)
    {
        var hid = Guard(hidTask, "2.4G/HID", cancellationToken);
        var bluetooth = Guard(bluetoothTask, "蓝牙", cancellationToken);
        return Merge(await Task.WhenAll(hid, bluetooth));
    }

    static async Task<ScanResult> Guard(Task<ScanResult> task, string label, CancellationToken cancellationToken)
    {
        try { return await task; }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            return new() { message = label + "读取失败：" + ex.Message };
        }
    }

    public static ScanResult Merge(IEnumerable<ScanResult> sources) => new()
    {
        devices = sources.SelectMany(s => s.devices).Where(r => r.percent is >= 0 and <= 100)
            .GroupBy(r => r.id).Select(g => g.OrderByDescending(r => r.fresh).First()).ToList(),
        message = string.Join("；", sources.Select(s => s.message).Where(s => !string.IsNullOrWhiteSpace(s)))
    };

    static void Stop(Process process)
    {
        try { if (!process.HasExited) process.Kill(true); } catch { }
    }

    static async Task<ScanResult> ReadBluetooth(CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "BatteryPet.exe"))
        {
            WorkingDirectory = AppContext.BaseDirectory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        start.ArgumentList.Add("--bluetooth-scan");
        using var process = new Process { StartInfo = start };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        process.Start();
        using var stop = timeout.Token.Register(() => Stop(process));
        ScanResult? latest = null;
        async Task ReadProgress()
        {
            int bytes = 0;
            while (await process.StandardOutput.ReadLineAsync() is string line)
            {
                bytes += line.Length;
                if (bytes > 1_000_000) { Stop(process); throw new InvalidDataException("蓝牙结果超过大小限制"); }
                if (!string.IsNullOrWhiteSpace(line)) latest = JsonSerializer.Deserialize<ScanResult>(line);
            }
        }
        var output = ReadProgress();
        var errors = process.StandardError.ReadToEndAsync();
        bool timedOut = false;
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { timedOut = true; Stop(process); }
        await output;
        string error = await errors;
        cancellationToken.ThrowIfCancellationRequested();
        if (timedOut)
        {
            latest ??= new();
            latest.message += "；蓝牙查询超时，保留本轮已取得的读数，请连接并唤醒设备后重试";
            return latest;
        }
        if (process.ExitCode != 0) throw new Exception("蓝牙组件退出：" + process.ExitCode + " " + error[..Math.Min(error.Length, 200)]);
        return latest ?? throw new Exception("蓝牙组件未返回结果");
    }

    static async Task<ScanResult> ReadPython(Action<Process?> track, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(PythonPath())
        {
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "battery_bridge.py"));
        start.Environment["PYTHONIOENCODING"] = "utf-8";
        using var process = new Process { StartInfo = start };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        try
        {
            try { process.Start(); }
            catch (Win32Exception ex) { throw new Exception("无法启动 Python 读取组件：" + ex.Message, ex); }
            track(process);
            using var stop = timeout.Token.Register(() => Stop(process));
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { Stop(process); cancellationToken.ThrowIfCancellationRequested(); throw new Exception("读取超时，接收器尚未返回电量"); }
            var data = await output; var error = await errors;
            if (process.ExitCode != 0)
            {
                var detail = string.IsNullOrWhiteSpace(error) ? data : error;
                detail = detail.Trim();
                throw new Exception("读取组件退出（代码 " + process.ExitCode + "）：" +
                    (detail.Length == 0 ? "未返回错误信息；请检查程序目录中的 Python 文件" :
                     detail[^Math.Min(detail.Length, 600)..]));
            }
            if (string.IsNullOrWhiteSpace(data))
                throw new Exception("读取组件未输出数据；请检查 Python 安装及程序目录");
            try { return JsonSerializer.Deserialize<ScanResult>(data) ?? throw new JsonException("结果为空"); }
            catch (JsonException ex) { throw new Exception("读取组件返回的数据格式无效：" + ex.Message, ex); }
        }
        finally { track(null); }
    }

    public static void Log(string message)
    {
        try
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BatteryPet", "last-read.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, $"{DateTime.Now:O}\nPython: {PythonPath()}\n{message}");
        }
        catch { }
    }
}
