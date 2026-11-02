// SPDX-License-Identifier: MIT
// Copyright (c) 2026 sadwind227
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace BatteryPet;

sealed record HidInterface(string Path, int Vid, int Pid, int Version, int UsagePage, int Usage,
    int InputLength, int OutputLength, int FeatureLength, string Product, string Serial, string Group, string Error)
{
    public string Identity => Serial.Length > 0 ? Serial : Group.Length > 0 ? Group : Path;
}

// Native Windows APIs; migrated from BatteryPet's own MIT transport. No kernel driver.
sealed class NativeHid : IDisposable
{
    readonly SafeFileHandle handle;
    public readonly Caps Capabilities;
    [StructLayout(LayoutKind.Sequential)] struct InterfaceData { public int Size; public Guid Class; public uint Flags; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] struct Attributes { public int Size; public ushort Vid, Pid, Version; }
    [StructLayout(LayoutKind.Sequential)] public struct Caps
    {
        public ushort Usage, UsagePage, InputLength, OutputLength, FeatureLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort Nodes, InputButtons, InputValues, InputIndices, OutputButtons, OutputValues, OutputIndices, FeatureButtons, FeatureValues, FeatureIndices;
    }
    [StructLayout(LayoutKind.Sequential)] struct Overlapped { public UIntPtr Internal, InternalHigh; public uint Offset, OffsetHigh; public IntPtr Event; }
    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr SetupDiGetClassDevsW(ref Guid guid, IntPtr enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr device, ref Guid guid, uint index, ref InterfaceData data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set, ref InterfaceData data, IntPtr detail, uint length, out uint required, IntPtr device);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_GetAttributes(SafeFileHandle handle, ref Attributes attrs);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr data);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data, out Caps caps);
    [DllImport("hid.dll")] static extern bool HidD_GetProductString(SafeFileHandle handle, [Out] byte[] buffer, int size);
    [DllImport("hid.dll")] static extern bool HidD_GetSerialNumberString(SafeFileHandle handle, [Out] byte[] buffer, int size);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_GetFeature(SafeFileHandle handle, [In, Out] byte[] report, int length);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadFile(SafeFileHandle handle, IntPtr buffer, int length, out int transferred, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool WriteFile(SafeFileHandle handle, IntPtr buffer, int length, out int transferred, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetOverlappedResult(SafeFileHandle handle, IntPtr overlapped, out int transferred, bool wait);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool CancelIoEx(SafeFileHandle handle, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateEventW(IntPtr attrs, bool manual, bool initial, IntPtr name);
    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    static Exception Error(string action) => new Win32Exception(Marshal.GetLastWin32Error(), action);
    static Caps ReadCaps(SafeFileHandle handle)
    {
        if (!HidD_GetPreparsedData(handle, out var ptr)) throw Error("无法读取 HID 描述");
        try { if (HidP_GetCaps(ptr, out var caps) != 0x110000) throw new IOException("无效的 HID 能力表"); return caps; }
        finally { HidD_FreePreparsedData(ptr); }
    }
    static string ReadString(SafeFileHandle handle, bool serial)
    {
        var bytes = new byte[512]; bool ok = serial ? HidD_GetSerialNumberString(handle, bytes, bytes.Length) : HidD_GetProductString(handle, bytes, bytes.Length);
        return ok ? Encoding.Unicode.GetString(bytes).Split('\0')[0] : "";
    }
    static HidInterface Inspect(string path)
    {
        string group = path.Split("&col")[0]; var match = Regex.Match(path, "vid_([0-9a-f]{4})&pid_([0-9a-f]{4})", RegexOptions.IgnoreCase);
        int vid = match.Success ? Convert.ToInt32(match.Groups[1].Value, 16) : 0, pid = match.Success ? Convert.ToInt32(match.Groups[2].Value, 16) : 0;
        using var h = CreateFileW(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (h.IsInvalid) return new(path, vid, pid, 0, 0, 0, 0, 0, 0, "", "", group, Error("无法读取接口信息").Message);
        var attrs = new Attributes { Size = Marshal.SizeOf<Attributes>() };
        if (!HidD_GetAttributes(h, ref attrs)) return new(path, vid, pid, 0, 0, 0, 0, 0, 0, "", "", group, Error("无法读取标识").Message);
        Caps caps = default; string error = ""; try { caps = ReadCaps(h); } catch (Exception ex) { error = ex.Message; }
        return new(path, attrs.Vid, attrs.Pid, attrs.Version, caps.UsagePage, caps.Usage, caps.InputLength, caps.OutputLength, caps.FeatureLength, ReadString(h, false), ReadString(h, true), group, error);
    }
    public static List<HidInterface> Enumerate()
    {
        HidD_GetHidGuid(out var guid); var set = SetupDiGetClassDevsW(ref guid, IntPtr.Zero, IntPtr.Zero, 0x12);
        if (set == new IntPtr(-1)) throw Error("无法枚举 HID");
        var list = new List<HidInterface>();
        try
        {
            for (uint i = 0; i < 4096; i++)
            {
                var data = new InterfaceData { Size = Marshal.SizeOf<InterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data)) { if (Marshal.GetLastWin32Error() == 259) break; throw Error("HID 枚举失败"); }
                SetupDiGetDeviceInterfaceDetailW(set, ref data, IntPtr.Zero, 0, out uint required, IntPtr.Zero);
                if (required < 8 || required > 65536) throw new IOException("无效的 HID 路径长度");
                var ptr = Marshal.AllocHGlobal((int)required);
                try
                {
                    Marshal.WriteInt32(ptr, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetailW(set, ref data, ptr, required, out _, IntPtr.Zero)) throw Error("HID 路径读取失败");
                    list.Add(Inspect(Marshal.PtrToStringUni(ptr + 4)!));
                }
                finally { Marshal.FreeHGlobal(ptr); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return list;
    }
    public NativeHid(string path, bool write = false)
    {
        handle = CreateFileW(path, 0x80000000u | (write ? 0x40000000u : 0), 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
        if (handle.IsInvalid) { var error = Error("无法打开 HID 接口"); handle.Dispose(); throw error; }
        try { Capabilities = ReadCaps(handle); } catch { handle.Dispose(); throw; }
    }
    byte[]? Transfer(byte[] buffer, bool write, int milliseconds)
    {
        IntPtr memory = Marshal.AllocHGlobal(buffer.Length), ov = Marshal.AllocHGlobal(Marshal.SizeOf<Overlapped>());
        IntPtr ev = CreateEventW(IntPtr.Zero, true, false, IntPtr.Zero);
        try
        {
            if (ev == IntPtr.Zero) throw Error("无法创建 HID 事件");
            Marshal.Copy(buffer, 0, memory, buffer.Length); Marshal.StructureToPtr(new Overlapped { Event = ev }, ov, false);
            bool ok = write ? WriteFile(handle, memory, buffer.Length, out _, ov) : ReadFile(handle, memory, buffer.Length, out _, ov);
            if (!ok && Marshal.GetLastWin32Error() != 997) throw Error("HID 报告读写失败");
            if (!ok)
            {
                uint state = WaitForSingleObject(ev, (uint)milliseconds);
                if (state != 0)
                {
                    CancelIoEx(handle, ov); GetOverlappedResult(handle, ov, out _, true); // Drain before freeing native memory.
                    if (state == 258) return null;
                    throw new IOException("HID 等待失败");
                }
            }
            if (!GetOverlappedResult(handle, ov, out int count, false)) throw Error("HID 完成失败");
            var result = new byte[count]; Marshal.Copy(memory, result, 0, count); return result;
        }
        finally { if (ev != IntPtr.Zero) CloseHandle(ev); Marshal.FreeHGlobal(ov); Marshal.FreeHGlobal(memory); }
    }
    public byte[]? Read(int timeout = 100) => Capabilities.InputLength == 0 ? null : Transfer(new byte[Capabilities.InputLength], false, timeout);
    public void Write(byte[] report)
    {
        if (report.Length != Capabilities.OutputLength) throw new InvalidDataException("Output 报告长度不匹配");
        if (Transfer(report, true, 1000) is not byte[] result || result.Length != report.Length) throw new IOException("HID 发送未完成");
    }
    public byte[] Feature(int id, int length)
    {
        if (length != Capabilities.FeatureLength || id is < 0 or > 255) throw new InvalidDataException("Feature 报告长度/编号不匹配");
        var result = new byte[length]; result[0] = (byte)id;
        if (!HidD_GetFeature(handle, result, length)) throw Error("HID Feature 读取失败"); return result;
    }
    public void Dispose() => handle.Dispose();
}

static class NativeLogitech
{
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    internal static byte[] Packet(int slot, int feature, int function, params byte[] parameters)
    {
        var packet = new byte[20]; packet[0] = 0x11; packet[1] = (byte)slot; packet[2] = (byte)feature; packet[3] = (byte)((function << 4) | 0x0D); parameters.CopyTo(packet, 4); return packet;
    }
    internal static bool Matches(byte[] reply, byte[] request) => reply.Length >= 4 && reply[0] == 0x11 && reply[1] == request[1] && reply[2] == request[2] && reply[3] == request[3];
    static byte[]? Call(NativeHid dev, int slot, int feature, int function, params byte[] parameters)
    {
        for (int i = 0; i < 8 && dev.Read(5) != null; i++) { }
        var packet = Packet(slot, feature, function, parameters); dev.Write(packet); var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 800)
        {
            var reply = dev.Read(); if (reply == null || reply.Length < 4) continue;
            if (Matches(reply, packet)) return reply[4..];
            if (reply[1] == slot && reply[2] is 0x8F or 0xFF) return null;
        }
        return null;
    }
    static int Feature(NativeHid dev, int slot, int id) => Call(dev, slot, 0, 0, (byte)(id >> 8), (byte)id)?.FirstOrDefault() ?? 0;
    internal static string MissingMessage(List<HidInterface> interfaces)
    {
        if (interfaces.Count == 0) return "未发现支持的罗技 C547 接收器";
        if (!interfaces.Any(d => d.UsagePage == 0xFF00 && d.OutputLength == 20))
            return "发现 C547 接收器，无法访问通信接口：" + string.Join("；", interfaces.Select(d => d.Error).Where(e => e.Length > 0).Distinct());
        return "鼠标暂未响应，请唤醒鼠标后刷新";
    }
    public static ScanResult Scan()
    {
        var interfaces = NativeHid.Enumerate().Where(d => d.Vid == 0x046D && d.Pid == 0xC547).ToList(); var result = new ScanResult();
        foreach (var iface in interfaces.Where(d => d.UsagePage == 0xFF00 && d.OutputLength == 20))
        {
            try
            {
                using var dev = new NativeHid(iface.Path, true);
                foreach (int slot in new[] { 1, 2 })
                {
                    string name = "罗技鼠标"; int nf = Feature(dev, slot, 5);
                    if (nf != 0)
                    {
                        int size = Call(dev, slot, nf, 0)?.FirstOrDefault() ?? 0;
                        if (size is > 0 and <= 100)
                        {
                            var raw = new List<byte>();
                            while (raw.Count < size) { var chunk = Call(dev, slot, nf, 1, (byte)raw.Count); if (chunk == null || chunk.Length == 0) break; raw.AddRange(chunk.Take(size - raw.Count)); }
                            if (raw.Count == size) name = Encoding.UTF8.GetString(raw.ToArray());
                        }
                    }
                    int fid = Feature(dev, slot, 0x1004); if (fid == 0) fid = Feature(dev, slot, 0x1004); if (fid == 0) continue;
                    var reply = Call(dev, slot, fid, 1); if (reply == null || reply.Length < 3 || reply[0] > 100) continue;
                    result.devices.Add(new() { id = Hash(iface.Identity)[..16] + ":" + slot, name = name, percent = reply[0], charging = reply[2] is 1 or 2, connection = "2.4G", status = "已连接" });
                }
            }
            catch (Exception ex) { result.message += "接收器访问失败：" + ex.Message + "；"; }
        }
        result.message = result.devices.Count > 0 ? "电量已更新" : result.message.Length > 0 ? result.message : MissingMessage(interfaces);
        return result;
    }
}
