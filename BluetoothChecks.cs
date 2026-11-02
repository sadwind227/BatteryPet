// SPDX-License-Identifier: MIT
// Copyright (c) 2026 sadwind227
using System.Runtime.InteropServices;

namespace BatteryPet;

static class BluetoothChecks
{
    public static IReadOnlyList<string> Run()
    {
        var checks = new List<string>();
        void Require(bool value, string label) { if (!value) throw new Exception(label); checks.Add(label); }
        Require(BluetoothBatteryProvider.ValidPercent(0) && BluetoothBatteryProvider.ValidPercent(100) &&
            !BluetoothBatteryProvider.ValidPercent(101) && !BluetoothBatteryProvider.ValidPercent(255) &&
            !BluetoothBatteryProvider.ValidPercent(null), "蓝牙 0% 有效，未知及越界电量拒绝");
        Require(Marshal.SizeOf<WindowsBluetoothBackend.LeUuid>() == 20 &&
            Marshal.SizeOf<WindowsBluetoothBackend.Service>() == 24 &&
            Marshal.SizeOf<WindowsBluetoothBackend.Characteristic>() == 36, "Windows BLE 原生结构 ABI 长度正确");
        Require(new WindowsBluetoothBackend.LeUuid { IsShort = 1, Short = 0x180f }.Matches(0x180f) &&
            new WindowsBluetoothBackend.LeUuid { Long = new("00002a19-0000-1000-8000-00805f9b34fb") }.Matches(0x2a19),
            "BLE 短 UUID 与完整 UUID 均匹配标准电量服务");
        var fake = new Fake
        {
            Reports = { new("address:A", "耳机", 88, false) },
            Endpoints = { new("address:A", "BLE 服务", "a") },
            Read = _ => 40
        };
        var snapshots = new List<ScanResult>();
        var result = BluetoothBatteryProvider.Scan(fake, snapshots.Add);
        Require(result.devices.Count == 1 && result.devices[0].percent == 40 && result.devices[0].fresh &&
            result.devices[0].connection == "蓝牙 BLE", "同一蓝牙设备去重并优先使用直接读数");
        Require(snapshots[0].devices.Single().percent == 88 && !snapshots[0].devices.Single().fresh,
            "蓝牙先发布 Windows 报告快照，后续阻塞不丢失已读值");
        Require(!result.devices[0].charge_known && !result.devices[0].charging, "标准电量服务不伪造充电状态");
        var offline = BluetoothBatteryProvider.Scan(new Fake { Reports = { new("address:A", "键盘", 0, false), new("address:B", "键盘", 55, true), new("address:C", "坏值", 255, true) } });
        Require(offline.devices.Count == 2 && offline.devices.Count(d => d.fresh) == 1 &&
            offline.devices.Select(d => d.id).Distinct().Count() == 2,
            "同名设备保持独立身份，离线报告标灰且越界值不展示");
        Require(BluetoothBatteryProvider.Identity("address:ab") == BluetoothBatteryProvider.Identity("ADDRESS:AB"),
            "蓝牙身份不受地址字符串大小写影响");
        var fallback = BluetoothBatteryProvider.Scan(new Fake
        {
            Endpoints = { new("a", "设备", "service", true), new("a", "设备", "device") },
            Read = e => e.ServiceInterface ? throw new IOException("synthetic service failure") : 62
        });
        Require(fallback.devices.Single().percent == 62, "BLE 服务接口失败时可回退同一设备接口");
        var partial = BluetoothBatteryProvider.Scan(new Fake
        {
            FailReports = true, Endpoints = { new("a", "坏设备", "a"), new("b", "好设备", "b") },
            Read = e => e.Key == "a" ? throw new IOException("synthetic failure") : 73
        });
        Require(partial.devices.Single().percent == 73 && partial.message.Contains("不可用"),
            "Windows 报告与个别蓝牙设备失败不影响其他蓝牙读数");
        var failed = Task.FromException<ScanResult>(new IOException("synthetic failure"));
        var good = Task.FromResult(new ScanResult { devices = new() { new() { id = "test", percent = 50 } }, message = "ok" });
        Require(ProviderRunner.Collect(failed, good).GetAwaiter().GetResult().devices.Count == 1 &&
            ProviderRunner.Collect(good, failed).GetAwaiter().GetResult().devices.Count == 1,
            "蓝牙与 HID 采集故障相互隔离");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        bool propagated = false;
        try { ProviderRunner.Collect(Task.FromCanceled<ScanResult>(cancelled.Token), good, cancelled.Token).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { propagated = true; }
        Require(propagated, "退出取消不会被转换为成功的空扫描");
        return checks;
    }
    sealed class Fake : IBluetoothBatteryBackend
    {
        public List<BluetoothReported> Reports = new();
        public List<BluetoothEndpoint> Endpoints = new();
        public Func<BluetoothEndpoint, int?> Read = _ => null;
        public bool FailReports;
        public IEnumerable<BluetoothReported> WindowsReports() => FailReports ? throw new IOException("synthetic unavailable") : Reports;
        public IEnumerable<BluetoothEndpoint> BleEndpoints() => Endpoints;
        public int? ReadBle(BluetoothEndpoint endpoint) => Read(endpoint);
    }
}
