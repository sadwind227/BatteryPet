using System.Diagnostics;
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

    public static async Task<ScanResult> Read(Action<Process?> track)
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
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        try
        {
            process.Start(); track(process);
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(true); throw new Exception("读取超时，接收器尚未返回电量"); }
            var data = await output; var error = await errors;
            if (process.ExitCode != 0) throw new Exception("读取组件异常：" + error[..Math.Min(error.Length, 350)]);
            if (string.IsNullOrWhiteSpace(data)) throw new Exception("读取组件未输出数据，请检查 Python 安装");
            return JsonSerializer.Deserialize<ScanResult>(data) ?? throw new Exception("读取组件未返回有效数据");
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
