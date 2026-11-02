// SPDX-License-Identifier: MIT
// Copyright (c) 2026 sadwind227
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace BatteryPet;

sealed record BluetoothEndpoint(string Key, string Name, string Path, bool ServiceInterface = false);
sealed record BluetoothReported(string Key, string Name, int? Percent, bool Connected);
interface IBluetoothBatteryBackend
{
    IEnumerable<BluetoothReported> WindowsReports();
    IEnumerable<BluetoothEndpoint> BleEndpoints();
    int? ReadBle(BluetoothEndpoint endpoint);
}

static class BluetoothBatteryProvider
{
    public static bool ValidPercent(int? value) => value is >= 0 and <= 100;
    public static string Identity(string key) => "bluetooth:" +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key.ToUpperInvariant())))[..16].ToLowerInvariant();

    public static ScanResult Scan(IBluetoothBatteryBackend? backend = null, Action<ScanResult>? progress = null)
    {
        backend ??= new WindowsBluetoothBackend();
        var readings = new Dictionary<string, Reading>();
        var discovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        ScanResult Snapshot() => new()
        {
            devices = readings.Values.ToList(),
            message = discovered.Count == 0 && errors.Count == 0 ? "未发现已配对的蓝牙外设" :
                $"蓝牙：发现 {discovered.Count} 个设备，读取 {readings.Count} 个电量" +
                (readings.Values.Any(r => !r.fresh) ? "；灰色为 Windows 报告值，未确认在线" : "") +
                (errors.Count > 0 ? "；" + string.Join("；", errors.Distinct().Take(2)) :
                 readings.Count == 0 ? "；设备未提供可读取电量，请先连接并唤醒设备" : "")
        };
        try
        {
            foreach (var item in backend.WindowsReports())
            {
                discovered.Add(item.Key);
                if (!ValidPercent(item.Percent)) continue;
                string id = Identity(item.Key);
                if (readings.TryGetValue(id, out var previous) && (previous.fresh || !item.Connected)) continue;
                readings[id] = new Reading { id = id, name = item.Name, percent = item.Percent,
                    connection = "蓝牙", fresh = item.Connected, charge_known = false,
                    status = item.Connected ? "Windows 报告电量" : "Windows 报告值（未确认在线）" };
            }
        }
        catch (Exception ex) { errors.Add("Windows 蓝牙报告不可用：" + ex.Message); }
        progress?.Invoke(Snapshot());
        try
        {
            // Prefer service interfaces; group by physical address/container, never by display name.
            var groups = backend.BleEndpoints().OrderByDescending(e => e.ServiceInterface)
                .GroupBy(e => e.Key, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var group in groups) discovered.Add(group.Key);
            foreach (var group in groups.Take(16))
            {
                string? failure = null;
                bool read = false;
                foreach (var endpoint in group)
                {
                    try
                    {
                        int? percent = backend.ReadBle(endpoint);
                        if (!ValidPercent(percent)) continue;
                        string id = Identity(endpoint.Key);
                        string name = readings.TryGetValue(id, out var prior) ? prior.name : endpoint.Name;
                        readings[id] = new Reading { id = id, name = name, percent = percent,
                            connection = "蓝牙 BLE", fresh = true, charge_known = false, status = "标准电量服务直接读取" };
                        progress?.Invoke(Snapshot());
                        read = true;
                        break;
                    }
                    catch (Exception ex) { failure = endpoint.Name + "：" + ex.Message; }
                }
                if (!read && failure != null) errors.Add(failure);
            }
            if (groups.Count > 16) errors.Add("本轮最多读取 16 个 BLE 设备");
        }
        catch (Exception ex) { errors.Add("BLE 枚举失败：" + ex.Message); }
        return Snapshot();
    }
}

// Independently implemented from Microsoft SetupAPI, Bluetooth and GATT declarations.
// Calls only enumeration, property queries and GATT reads. Runs in a bounded helper process.
sealed class WindowsBluetoothBackend : IBluetoothBatteryBackend
{
    static readonly Guid LeDevice = new("781aee18-7733-4ce4-add0-91f41c67b592");
    static readonly Guid BatteryService = new("0000180f-0000-1000-8000-00805f9b34fb");
    static readonly PropertyKey FriendlyName = new("a45c254e-df1c-4efd-8020-67d146a850e0", 14);
    static readonly PropertyKey Description = new("a45c254e-df1c-4efd-8020-67d146a850e0", 2);
    static readonly PropertyKey Container = new("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c", 2);
    static readonly PropertyKey BatteryLife = new("49cd1f76-5626-4b17-a4e8-18b4aa1a2213", 10);
    // Optional Windows Bluetooth driver property; not a guaranteed public contract.
    static readonly PropertyKey BluetoothBattery = new("104ea319-6ee2-4701-bd47-8ddbf425bbe5", 2);
    static readonly Regex Address = new(@"DEV_([0-9A-F]{12})(?:[^0-9A-F]|$)", RegexOptions.IgnoreCase);
    static readonly IntPtr Invalid = new(-1);
    const int MoreData = unchecked((int)0x800700EA);

    [StructLayout(LayoutKind.Sequential)] struct PropertyKey
    {
        public Guid Format; public uint Id;
        public PropertyKey(string format, uint id) { Format = new(format); Id = id; }
    }
    [StructLayout(LayoutKind.Sequential)] struct DeviceInfo
    {
        public uint Size; public Guid Class; public uint DevInst; public UIntPtr Reserved;
        public static DeviceInfo Create() => new() { Size = (uint)Marshal.SizeOf<DeviceInfo>() };
    }
    [StructLayout(LayoutKind.Sequential)] struct InterfaceInfo
    {
        public uint Size; public Guid Class; public uint Flags; public UIntPtr Reserved;
    }
    [StructLayout(LayoutKind.Explicit, Size = 20)] internal struct LeUuid
    {
        [FieldOffset(0)] public byte IsShort;
        [FieldOffset(4)] public ushort Short;
        [FieldOffset(4)] public Guid Long;
        public readonly bool Matches(ushort id) => IsShort != 0 ? Short == id : Long == new Guid($"0000{id:x4}-0000-1000-8000-00805f9b34fb");
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Service
    {
        public LeUuid Uuid; public ushort Handle;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Characteristic
    {
        public ushort ServiceHandle; public LeUuid Uuid; public ushort Handle, ValueHandle;
        public byte Broadcastable, Readable, Writable, WritableNoResponse, SignedWritable, Notifiable, Indicatable, Extended;
    }
    [StructLayout(LayoutKind.Sequential)] struct SearchParams
    {
        public uint Size;
        public int Authenticated, Remembered, Unknown, Connected, Inquiry;
        public byte Timeout; public IntPtr Radio;
    }
    [StructLayout(LayoutKind.Sequential)] struct SystemTime
    {
        public ushort Year, Month, Weekday, Day, Hour, Minute, Second, Milliseconds;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct ClassicInfo
    {
        public uint Size; public ulong Address; public uint Class;
        public int Connected, Remembered, Authenticated; public SystemTime Seen, Used;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)] public string Name;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr SetupDiGetClassDevsW(IntPtr guid, string? enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref DeviceInfo info);
    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr device, ref Guid guid, uint index, ref InterfaceInfo info);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set, ref InterfaceInfo info, IntPtr detail, uint bytes, out uint needed, ref DeviceInfo device);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] static extern bool SetupDiGetDeviceInstanceIdW(IntPtr set, ref DeviceInfo device, StringBuilder id, uint count, out uint needed);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] static extern bool SetupDiGetDevicePropertyW(IntPtr set, ref DeviceInfo info, ref PropertyKey key, out uint type, byte[] data, uint bytes, out uint needed, uint flags);
    [DllImport("setupapi.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("cfgmgr32.dll")] static extern uint CM_Get_Parent(out uint parent, uint node, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern uint CM_Get_Device_IDW(uint node, StringBuilder buffer, uint count, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("BluetoothApis.dll", SetLastError = true)] static extern IntPtr BluetoothFindFirstDevice(ref SearchParams search, ref ClassicInfo info);
    [DllImport("BluetoothApis.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] static extern bool BluetoothFindNextDevice(IntPtr search, ref ClassicInfo info);
    [DllImport("BluetoothApis.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool BluetoothFindDeviceClose(IntPtr search);
    [DllImport("BluetoothApis.dll")] static extern int BluetoothGATTGetServices(SafeFileHandle handle, ushort count, IntPtr services, out ushort actual, uint flags);
    [DllImport("BluetoothApis.dll")] static extern int BluetoothGATTGetCharacteristics(SafeFileHandle handle, IntPtr service, ushort count, IntPtr chars, out ushort actual, uint flags);
    [DllImport("BluetoothApis.dll")] static extern int BluetoothGATTGetCharacteristicValue(SafeFileHandle handle, ref Characteristic characteristic, uint bytes, IntPtr value, out ushort needed, uint flags);

    static HashSet<string> ConnectedClassic()
    {
        var connected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var search = new SearchParams { Size = (uint)Marshal.SizeOf<SearchParams>(), Authenticated = 1, Remembered = 1, Connected = 1 };
        var info = new ClassicInfo { Size = (uint)Marshal.SizeOf<ClassicInfo>(), Name = "" };
        IntPtr handle = BluetoothFindFirstDevice(ref search, ref info);
        if (handle == IntPtr.Zero) return connected;
        try
        {
            do { if (info.Connected != 0) connected.Add("address:" + (info.Address & 0xffffffffffff).ToString("X12")); }
            while (BluetoothFindNextDevice(handle, ref info));
        }
        finally { BluetoothFindDeviceClose(handle); }
        return connected;
    }
    static byte[]? Property(IntPtr set, DeviceInfo device, PropertyKey key, out uint type)
    {
        var bytes = new byte[4096];
        if (!SetupDiGetDevicePropertyW(set, ref device, ref key, out type, bytes, (uint)bytes.Length, out uint size, 0) || size > bytes.Length) return null;
        return bytes[..(int)size];
    }
    static string Text(IntPtr set, DeviceInfo device, PropertyKey key)
    {
        var bytes = Property(set, device, key, out uint type);
        return type == 0x12 && bytes != null ? Encoding.Unicode.GetString(bytes).TrimEnd('\0') : "";
    }
    static int? Battery(IntPtr set, DeviceInfo device, PropertyKey key)
    {
        var bytes = Property(set, device, key, out uint type);
        long? value = type == 3 && bytes?.Length == 1 ? bytes[0] :
                      type == 7 && bytes?.Length == 4 ? BitConverter.ToUInt32(bytes) : null;
        return value is >= 0 and <= 100 ? (int)value : null;
    }
    static string Key(IntPtr set, DeviceInfo device, string instance)
    {
        string current = instance;
        uint node = device.DevInst;
        for (int n = 0; n < 6; n++)
        {
            var match = Address.Match(current);
            if (match.Success) return "address:" + match.Groups[1].Value.ToUpperInvariant();
            if (CM_Get_Parent(out node, node, 0) != 0) break;
            var id = new StringBuilder(1024);
            if (CM_Get_Device_IDW(node, id, 1024, 0) != 0) break;
            current = id.ToString();
        }
        byte[]? container = Property(set, device, Container, out uint type);
        return container?.Length == 16 && type == 0x0d ? "container:" + new Guid(container) : "node:" + instance;
    }
    static string Instance(IntPtr set, ref DeviceInfo device)
    {
        var id = new StringBuilder(1024);
        if (!SetupDiGetDeviceInstanceIdW(set, ref device, id, 1024, out _)) throw new Win32Exception();
        return id.ToString();
    }
    static string Name(IntPtr set, DeviceInfo device)
    {
        string name = Text(set, device, FriendlyName);
        if (name.Length == 0) name = Text(set, device, Description);
        return name.Length == 0 ? "蓝牙外设" : name;
    }

    public IEnumerable<BluetoothReported> WindowsReports()
    {
        var connected = ConnectedClassic();
        foreach (string enumerator in new[] { "BTHENUM", "BTHLE", "BTHLEDEVICE" })
        {
            IntPtr set = SetupDiGetClassDevsW(IntPtr.Zero, enumerator, IntPtr.Zero, 2 | 4);
            if (set == Invalid) throw new Win32Exception();
            try
            {
                for (uint i = 0; i < 512; i++)
                {
                    var device = DeviceInfo.Create();
                    if (!SetupDiEnumDeviceInfo(set, i, ref device))
                    {
                        if (Marshal.GetLastWin32Error() != 259) throw new Win32Exception();
                        break;
                    }
                    string instance = Instance(set, ref device);
                    string key = Key(set, device, instance);
                    int? percent = Battery(set, device, BatteryLife) ?? Battery(set, device, BluetoothBattery);
                    yield return new(key, Name(set, device), percent, connected.Contains(key));
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
        }
    }
    public IEnumerable<BluetoothEndpoint> BleEndpoints()
    {
        foreach (Guid kind in new[] { BatteryService, LeDevice })
        {
            Guid guid = kind;
            IntPtr ptr = Marshal.AllocHGlobal(16);
            IntPtr set;
            try { Marshal.StructureToPtr(guid, ptr, false); set = SetupDiGetClassDevsW(ptr, null, IntPtr.Zero, 2 | 16); }
            finally { Marshal.FreeHGlobal(ptr); }
            if (set == Invalid) throw new Win32Exception();
            try
            {
                for (uint index = 0; index < 128; index++)
                {
                    var iface = new InterfaceInfo { Size = (uint)Marshal.SizeOf<InterfaceInfo>() };
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index, ref iface))
                    {
                        if (Marshal.GetLastWin32Error() != 259) throw new Win32Exception();
                        break;
                    }
                    var device = DeviceInfo.Create();
                    SetupDiGetDeviceInterfaceDetailW(set, ref iface, IntPtr.Zero, 0, out uint needed, ref device);
                    if (needed is < 8 or > 65536) throw new Win32Exception();
                    IntPtr detail = Marshal.AllocHGlobal((int)needed);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetailW(set, ref iface, detail, needed, out _, ref device)) throw new Win32Exception();
                        string path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4)) ?? throw new InvalidDataException("蓝牙接口路径为空");
                        string instance = Instance(set, ref device);
                        yield return new(Key(set, device, instance), Name(set, device), path, kind == BatteryService);
                    }
                    finally { Marshal.FreeHGlobal(detail); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
        }
    }
    public int? ReadBle(BluetoothEndpoint endpoint)
    {
        using var handle = CreateFileW(endpoint.Path, 0x80000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception();
        if (endpoint.ServiceInterface) return ReadService(handle, IntPtr.Zero);
        int hr = BluetoothGATTGetServices(handle, 0, IntPtr.Zero, out ushort count, 0);
        if (hr == 0 && count == 0) return null;
        if (hr != MoreData || count is 0 or > 128) Marshal.ThrowExceptionForHR(hr < 0 ? hr : unchecked((int)0x80004005));
        IntPtr services = Marshal.AllocHGlobal(Marshal.SizeOf<Service>() * count);
        try
        {
            Marshal.ThrowExceptionForHR(BluetoothGATTGetServices(handle, count, services, out ushort actual, 0));
            for (int i = 0; i < Math.Min(count, actual); i++)
            {
                IntPtr service = IntPtr.Add(services, i * Marshal.SizeOf<Service>());
                if (Marshal.PtrToStructure<Service>(service).Uuid.Matches(0x180f))
                {
                    int? percent = ReadService(handle, service);
                    if (percent.HasValue) return percent;
                }
            }
            return null;
        }
        finally { Marshal.FreeHGlobal(services); }
    }
    static int? ReadService(SafeFileHandle handle, IntPtr service)
    {
        int hr = BluetoothGATTGetCharacteristics(handle, service, 0, IntPtr.Zero, out ushort count, 0);
        if (hr == 0 && count == 0) return null;
        if (hr != MoreData || count is 0 or > 128) Marshal.ThrowExceptionForHR(hr < 0 ? hr : unchecked((int)0x80004005));
        IntPtr chars = Marshal.AllocHGlobal(Marshal.SizeOf<Characteristic>() * count);
        try
        {
            Marshal.ThrowExceptionForHR(BluetoothGATTGetCharacteristics(handle, service, count, chars, out ushort actual, 0));
            for (int i = 0; i < Math.Min(count, actual); i++)
            {
                var characteristic = Marshal.PtrToStructure<Characteristic>(IntPtr.Add(chars, i * Marshal.SizeOf<Characteristic>()));
                if (!characteristic.Uuid.Matches(0x2a19) || characteristic.Readable == 0) continue;
                IntPtr value = Marshal.AllocHGlobal(8);
                try
                {
                    Marshal.WriteInt64(value, 0);
                    Marshal.ThrowExceptionForHR(BluetoothGATTGetCharacteristicValue(handle, ref characteristic, 8, value, out _, 4));
                    if (Marshal.ReadInt32(value) == 1)
                    {
                        byte percent = Marshal.ReadByte(value, 4);
                        if (percent <= 100) return percent;
                    }
                }
                finally { Marshal.FreeHGlobal(value); }
            }
            return null;
        }
        finally { Marshal.FreeHGlobal(chars); }
    }
}
