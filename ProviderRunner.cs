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
    public static Task<ScanResult> Read(Action<Process?> track, CancellationToken cancellationToken = default)
        => Collect(ReadHid(track, cancellationToken), ReadBluetooth(cancellationToken), cancellationToken);

    static async Task<ScanResult> ReadHid(Action<Process?> track, CancellationToken token)
    {
        ScanResult builtin;
        try { var payload = await NativeProcess.Json(new[] { "--hid-scan" }, TimeSpan.FromSeconds(25), token, track); if (payload.TryGetProperty("error", out var error)) throw new IOException(error.GetString()); builtin = payload.Deserialize<ScanResult>() ?? new(); }
        catch (Exception ex) when (!token.IsCancellationRequested) { builtin = new() { message = "2.4G/HID 读取失败：" + ex.Message }; }
        return Merge(new[] { builtin, await NativeAdapters.ScanInstalled(token) });
    }

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

    public static void Log(string message)
    {
        try
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BatteryPet", "last-read.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, $"{DateTime.Now:O}\nProvider: native Windows HID + Bluetooth\n{message}");
        }
        catch { }
    }
}
