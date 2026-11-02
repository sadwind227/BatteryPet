// SPDX-License-Identifier: MIT
// Copyright (c) 2026 sadwind227
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class Setup
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "BatteryPet.Desktop";
    static void CopyTree(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)), false);
        foreach (string dir in Directory.GetDirectories(source))
        {
            if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) throw new IOException("数据目录包含链接，请先检查：" + dir);
            CopyTree(dir, Path.Combine(target, Path.GetFileName(dir)));
        }
    }
    static void Extract(string target)
    {
        Directory.CreateDirectory(target);
        using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
        foreach (var entry in zip.Entries)
        {
            string name = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            const string prefix = "BatteryPet\\";
            if (!name.StartsWith(prefix, StringComparison.Ordinal)) throw new IOException("安装包路径无效");
            name = name.Substring(prefix.Length);
            if (name.Length == 0) continue;
            string dest = Path.GetFullPath(Path.Combine(target, name));
            if (!dest.StartsWith(Path.GetFullPath(target) + "\\", StringComparison.OrdinalIgnoreCase)) throw new IOException("安装包路径越界");
            if (entry.Name.Length == 0) { Directory.CreateDirectory(dest); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            entry.ExtractToFile(dest, false);
        }
        if (!File.Exists(Path.Combine(target, "BatteryPet.exe"))) throw new IOException("安装包缺少主程序");
    }
    const string InstallKey = @"Software\BatteryPet\Installer";
    static string InstallDirectory(string root, string stamp, string data)
    {
        if (String.IsNullOrWhiteSpace(root) || !Path.IsPathRooted(root) || root.StartsWith(@"\\")) throw new IOException("请选择本机磁盘上的完整安装路径。");
        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        string personal = Path.GetFullPath(data).TrimEnd(Path.DirectorySeparatorChar);
        if (root.Equals(personal, StringComparison.OrdinalIgnoreCase) || root.StartsWith(personal + "\\", StringComparison.OrdinalIgnoreCase)) throw new IOException("安装位置不能位于个人数据目录内。");
        return Path.Combine(root + Path.DirectorySeparatorChar, "0.7.0-dev-" + stamp);
    }
    static string ChooseLocation(string local)
    {
        string location = Path.Combine(local, "Programs", "BatteryPet");
        using (var key = Registry.CurrentUser.OpenSubKey(InstallKey))
            if (key != null && key.GetValue("InstallRoot") is string) location = (string)key.GetValue("InstallRoot");
        using (var form = new Form { Text = "电量桌宠 · 安装位置", ClientSize = new System.Drawing.Size(580, 210), StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, AutoScaleMode = AutoScaleMode.Dpi })
        {
            var label = new Label { Text = "选择安装位置（升级时沿用上次位置）", Left = 20, Top = 20, Width = 530 };
            var path = new TextBox { Text = location, Left = 20, Top = 52, Width = 440 };
            var browse = new Button { Text = "浏览…", Left = 470, Top = 50, Width = 85 };
            browse.Click += delegate { using (var picker = new FolderBrowserDialog { Description = "选择电量桌宠安装根目录", SelectedPath = path.Text }) { if (picker.ShowDialog(form) == DialogResult.OK) path.Text = picker.SelectedPath; } };
            var note = new Label { Text = "将在所选位置创建独立版本子目录，保留旧版供回退。\n个人设置和设备适配继续保存在当前 Windows 账户，不因位置变化而丢失。", Left = 20, Top = 90, Width = 535, Height = 50 };
            var ok = new Button { Text = "安装 / 升级", Left = 350, Top = 160, Width = 110 };
            var cancel = new Button { Text = "取消", Left = 470, Top = 160, Width = 85, DialogResult = DialogResult.Cancel };
            ok.Click += delegate { try { InstallDirectory(path.Text, "validate", Path.Combine(local, "BatteryPet")); form.DialogResult = DialogResult.OK; } catch (Exception ex) { MessageBox.Show(form, ex.Message, "安装位置无效"); } };
            form.Controls.AddRange(new Control[] { label, path, browse, note, ok, cancel }); form.AcceptButton = ok; form.CancelButton = cancel;
            return form.ShowDialog() == DialogResult.OK ? Path.GetFullPath(path.Text) : null;
        }
    }
    static bool Running()
    {
        foreach (var p in Process.GetProcessesByName("BatteryPet"))
        using (p) { try { if (p.SessionId == Process.GetCurrentProcess().SessionId) return true; } catch { return true; } }
        return false;
    }
    static void CreateShortcut(string path, string exe)
    {
        Type type = Type.GetTypeFromProgID("WScript.Shell", true);
        object shell = Activator.CreateInstance(type);
        object link = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
        try
        {
            Type lt = link.GetType();
            lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { exe });
            lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { Path.GetDirectoryName(exe) });
            lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
        }
        finally { System.Runtime.InteropServices.Marshal.ReleaseComObject(link); System.Runtime.InteropServices.Marshal.ReleaseComObject(shell); }
    }
    static int Test(string root)
    {
        root = Path.GetFullPath(root);
        if (Directory.Exists(root)) throw new IOException("测试目录必须不存在");
        Directory.CreateDirectory(root);
        string data = Path.Combine(root, "data");
        Directory.CreateDirectory(Path.Combine(data, "adapters", "enabled"));
        Directory.CreateDirectory(Path.Combine(data, "adapters", "verification"));
        File.WriteAllText(Path.Combine(data, "settings.json"), "{\"AutoStart\":true}");
        File.WriteAllText(Path.Combine(data, "adapters", "enabled", "custom.json"), "custom-device-protocol");
        File.WriteAllText(Path.Combine(data, "adapters", "verification", "receipt.json"), "verified-receipt");
        CopyTree(data, Path.Combine(root, "backup"));
        string installRoot = Path.Combine(root, "custom path 安装");
        string oldInstall = Path.Combine(installRoot, "old-version"); Directory.CreateDirectory(oldInstall);
        File.WriteAllText(Path.Combine(oldInstall, "previous.txt"), "keep-old-version");
        string selected = InstallDirectory(installRoot, "test-first", data); Extract(selected);
        Extract(InstallDirectory(installRoot, "test-upgrade", data));
        if (File.ReadAllText(Path.Combine(oldInstall, "previous.txt")) != "keep-old-version") throw new Exception("旧版目录受损");
        bool blocked = false; try { InstallDirectory(data, "invalid", data); } catch (IOException) { blocked = true; }
        if (!blocked) throw new Exception("未保护个人数据目录");
        foreach (string f in Directory.GetFiles(data, "*", SearchOption.AllDirectories))
            if (Convert.ToBase64String(File.ReadAllBytes(f)) != Convert.ToBase64String(File.ReadAllBytes(Path.Combine(root, "backup", f.Substring(data.Length + 1))))) throw new Exception("备份不一致");
        File.WriteAllText(Path.Combine(root, "PASS.txt"), "PASS: custom install path with spaces/Unicode; side-by-side upgrade; old program retained; personal-data destination rejected; payload extraction; settings, enabled adapters and verification backup; original data preserved. No real user settings or registry changed.");
        return 0;
    }
    [STAThread]
    static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        try
        {
            if (args.Length == 2 && args[0] == "--self-test") return Test(args[1]);
            using (var mutex = new System.Threading.Mutex(true, "BatteryPet.Setup", out first))
            {
                if (!first) throw new IOException("已有安装程序正在运行。");
                if (MessageBox.Show("安装或升级电量桌宠 0.7.0-dev（开发预览版）。\n\n保留设备适配、验证记录和个人设置，安装前自动备份。\n已自带 .NET 运行环境，无需安装 Python 或 .NET。\n\n继续前请从托盘菜单退出旧版电量桌宠。", "电量桌宠安装", MessageBoxButtons.OKCancel) != DialogResult.OK) return 0;
                while (Running()) if (MessageBox.Show("请从托盘菜单退出所有电量桌宠，再点重试。", "程序仍在运行", MessageBoxButtons.RetryCancel) != DialogResult.Retry) return 0;
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string installRoot = ChooseLocation(local);
                if (installRoot == null) return 0;
                string data = Path.Combine(local, "BatteryPet");
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                string backup = Path.Combine(local, "BatteryPet-Backups", stamp);
                if (Directory.Exists(data)) CopyTree(data, Path.Combine(backup, "data"));
                else Directory.CreateDirectory(backup);
                string install = InstallDirectory(installRoot, stamp, data);
                Extract(install);
                string exe = Path.Combine(install, "BatteryPet.exe");
                string shortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "电量桌宠.lnk");
                byte[] previousShortcut = File.Exists(shortcut) ? File.ReadAllBytes(shortcut) : null;
                using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    string previousRun = key.GetValue(RunName) as string;
                    File.WriteAllText(Path.Combine(backup, "startup.txt"), previousRun ?? "", Encoding.UTF8);
                    if (previousShortcut != null) File.WriteAllBytes(Path.Combine(backup, "shortcut.lnk"), previousShortcut);
                    try
                    {
                        CreateShortcut(shortcut, exe);
                        if (!String.IsNullOrEmpty(previousRun)) key.SetValue(RunName, "\"" + exe + "\" --autostart");
                    }
                    catch
                    {
                        if (previousRun != null) key.SetValue(RunName, previousRun); else key.DeleteValue(RunName, false);
                        if (previousShortcut != null) File.WriteAllBytes(shortcut, previousShortcut); else if (File.Exists(shortcut)) File.Delete(shortcut);
                        throw;
                    }
                }
                using (var key = Registry.CurrentUser.CreateSubKey(InstallKey)) key.SetValue("InstallRoot", installRoot);
                File.WriteAllText(Path.Combine(backup, "installation.txt"), "Installed: " + install + "\r\nOriginal user data remains at: " + data, Encoding.UTF8);
                if (MessageBox.Show("安装完成，原有适配和个人设置已保留。\n开始菜单：电量桌宠\n备份位置：" + backup + "\n\n现在启动？", "安装完成", MessageBoxButtons.YesNo) == DialogResult.Yes)
                    Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = install });
                return 0;
            }
        }
        catch (Exception ex)
        {
            if (args.Length == 2 && args[0] == "--self-test") { File.WriteAllText(args[1] + "-error.txt", ex.ToString()); return 1; }
            MessageBox.Show("安装未完成：" + ex.Message + "\n\n原有个人数据未被替换。", "电量桌宠安装"); return 1;
        }
    }
    static bool first;
}

