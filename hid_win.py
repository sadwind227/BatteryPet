# SPDX-License-Identifier: MIT
# Copyright (c) 2026 sadwind227
"""BatteryPet's Windows HID transport, independently implemented from Win32 APIs.

The module handles discovery and report I/O. Device commands belong in providers.
API references: Microsoft Learn, "Finding and Opening a HID Collection",
HidD_GetPreparsedData, HidP_GetCaps, and Win32 overlapped I/O.
"""

from __future__ import annotations

import ctypes as C
from ctypes import wintypes as W
from dataclasses import dataclass
import re
import sys

if sys.platform != "win32":
    raise RuntimeError("HID 通信仅支持 Windows")

setup = C.WinDLL("setupapi", use_last_error=True)
hid = C.WinDLL("hid", use_last_error=True)
kernel = C.WinDLL("kernel32", use_last_error=True)
PTR = C.c_void_p
INVALID = PTR(-1).value
ERROR_NO_MORE_ITEMS = 259
ERROR_IO_PENDING = 997
ERROR_OPERATION_ABORTED = 995
WAIT_TIMEOUT = 258
WAIT_OBJECT_0 = 0
HidP_Input, HidP_Output, HidP_Feature = 0, 1, 2


class HidError(OSError):
    pass


def _error(operation: str) -> HidError:
    number = C.get_last_error()
    return HidError(number, f"{operation}: {C.FormatError(number).strip()}")


class GUID(C.Structure):
    _fields_ = [("Data1", W.DWORD), ("Data2", W.WORD), ("Data3", W.WORD),
                ("Data4", C.c_ubyte * 8)]


class InterfaceData(C.Structure):
    _fields_ = [("cbSize", W.DWORD), ("InterfaceClassGuid", GUID),
                ("Flags", W.DWORD), ("Reserved", PTR)]


class Attributes(C.Structure):
    _fields_ = [("Size", W.DWORD), ("VendorID", W.WORD),
                ("ProductID", W.WORD), ("VersionNumber", W.WORD)]


class Caps(C.Structure):
    _fields_ = [("Usage", W.WORD), ("UsagePage", W.WORD),
                ("InputReportByteLength", W.WORD), ("OutputReportByteLength", W.WORD),
                ("FeatureReportByteLength", W.WORD), ("Reserved", W.WORD * 17),
                ("NumberLinkCollectionNodes", W.WORD),
                ("NumberInputButtonCaps", W.WORD), ("NumberInputValueCaps", W.WORD),
                ("NumberInputDataIndices", W.WORD), ("NumberOutputButtonCaps", W.WORD),
                ("NumberOutputValueCaps", W.WORD), ("NumberOutputDataIndices", W.WORD),
                ("NumberFeatureButtonCaps", W.WORD), ("NumberFeatureValueCaps", W.WORD),
                ("NumberFeatureDataIndices", W.WORD)]


class Overlapped(C.Structure):
    _fields_ = [("Internal", PTR), ("InternalHigh", PTR),
                ("Offset", W.DWORD), ("OffsetHigh", W.DWORD), ("hEvent", PTR)]


def _api(dll, name, result, arguments):
    fn = getattr(dll, name)
    fn.restype = result
    fn.argtypes = arguments
    return fn


get_guid = _api(hid, "HidD_GetHidGuid", None, [C.POINTER(GUID)])
get_set = _api(setup, "SetupDiGetClassDevsW", PTR, [C.POINTER(GUID), PTR, PTR, W.DWORD])
next_interface = _api(setup, "SetupDiEnumDeviceInterfaces", W.BOOL,
                      [PTR, PTR, C.POINTER(GUID), W.DWORD, C.POINTER(InterfaceData)])
detail = _api(setup, "SetupDiGetDeviceInterfaceDetailW", W.BOOL,
              [PTR, C.POINTER(InterfaceData), PTR, W.DWORD, C.POINTER(W.DWORD), PTR])
destroy_set = _api(setup, "SetupDiDestroyDeviceInfoList", W.BOOL, [PTR])
open_file = _api(kernel, "CreateFileW", PTR,
                 [W.LPCWSTR, W.DWORD, W.DWORD, PTR, W.DWORD, W.DWORD, PTR])
close_handle = _api(kernel, "CloseHandle", W.BOOL, [PTR])
get_attributes = _api(hid, "HidD_GetAttributes", W.BOOL, [PTR, C.POINTER(Attributes)])
get_preparsed = _api(hid, "HidD_GetPreparsedData", W.BOOL, [PTR, C.POINTER(PTR)])
free_preparsed = _api(hid, "HidD_FreePreparsedData", W.BOOL, [PTR])
get_caps = _api(hid, "HidP_GetCaps", W.LONG, [PTR, C.POINTER(Caps)])
get_product = _api(hid, "HidD_GetProductString", W.BOOL, [PTR, PTR, W.ULONG])
get_maker = _api(hid, "HidD_GetManufacturerString", W.BOOL, [PTR, PTR, W.ULONG])
get_serial = _api(hid, "HidD_GetSerialNumberString", W.BOOL, [PTR, PTR, W.ULONG])
get_feature = _api(hid, "HidD_GetFeature", W.BOOL, [PTR, PTR, W.DWORD])
read_file = _api(kernel, "ReadFile", W.BOOL,
                 [PTR, PTR, W.DWORD, C.POINTER(W.DWORD), C.POINTER(Overlapped)])
write_file = _api(kernel, "WriteFile", W.BOOL,
                  [PTR, PTR, W.DWORD, C.POINTER(W.DWORD), C.POINTER(Overlapped)])
create_event = _api(kernel, "CreateEventW", PTR, [PTR, W.BOOL, W.BOOL, W.LPCWSTR])
wait_event = _api(kernel, "WaitForSingleObject", W.DWORD, [PTR, W.DWORD])
finish_io = _api(kernel, "GetOverlappedResult", W.BOOL,
                 [PTR, C.POINTER(Overlapped), C.POINTER(W.DWORD), W.BOOL])
cancel_io = _api(kernel, "CancelIoEx", W.BOOL, [PTR, C.POINTER(Overlapped)])


@dataclass(frozen=True)
class HidInterface:
    path: str
    vid: int = 0
    pid: int = 0
    version: int = 0
    usage_page: int = 0
    usage: int = 0
    input_len: int = 0
    output_len: int = 0
    feature_len: int = 0
    manufacturer: str = ""
    product: str = ""
    serial: str = ""
    group: str = ""
    error: str = ""

    @property
    def display_name(self) -> str:
        return self.product or (self.manufacturer + " HID 设备").strip() or "HID 设备"


def _string(fn, handle) -> str:
    value = C.create_unicode_buffer(256)
    return value.value if fn(handle, value, C.sizeof(value)) else ""


def _open(path: str, write: bool = False, overlapped: bool = False):
    access = 0x80000000 | (0x40000000 if write else 0)
    flags = 0x40000000 if overlapped else 0
    handle = open_file(path, access, 0x00000001 | 0x00000002,
                       None, 3, flags, None)
    if handle == INVALID or handle is None:
        raise _error("无法打开 HID 接口")
    return handle


def _caps(handle) -> Caps:
    preparsed = PTR()
    if not get_preparsed(handle, C.byref(preparsed)):
        raise _error("无法读取 HID 描述信息")
    try:
        result = Caps()
        if get_caps(preparsed, C.byref(result)) & 0xFFFFFFFF != 0x00110000:
            raise HidError("无法解析 HID 报告长度")
        return result
    finally:
        free_preparsed(preparsed)


def _inspect(path: str) -> HidInterface:
    # A metadata-only handle can enumerate interfaces reserved by Windows.
    handle = open_file(path, 0, 3, None, 3, 0, None)
    if handle == INVALID or handle is None:
        match = re.search(r"vid_([0-9a-f]{4})&pid_([0-9a-f]{4})", path, re.I)
        vid, pid = (int(match[1], 16), int(match[2], 16)) if match else (0, 0)
        return HidInterface(path=path, vid=vid, pid=pid, group=path.split("&col")[0],
                            error=str(_error("读取接口信息失败")))
    try:
        attributes = Attributes()
        attributes.Size = C.sizeof(attributes)
        if not get_attributes(handle, C.byref(attributes)):
            return HidInterface(path=path, group=path, error=str(_error("读取设备标识失败")))
        error = ""
        try:
            caps = _caps(handle)
        except OSError as exc:
            caps = Caps()
            error = str(exc)
        return HidInterface(
            path=path, vid=attributes.VendorID, pid=attributes.ProductID,
            version=attributes.VersionNumber, usage_page=caps.UsagePage,
            usage=caps.Usage, input_len=caps.InputReportByteLength,
            output_len=caps.OutputReportByteLength,
            feature_len=caps.FeatureReportByteLength,
            manufacturer=_string(get_maker, handle), product=_string(get_product, handle),
            serial=_string(get_serial, handle), group=path.split("&col")[0], error=error)
    finally:
        close_handle(handle)


def enumerate_hid() -> list[HidInterface]:
    guid = GUID()
    get_guid(C.byref(guid))
    device_set = get_set(C.byref(guid), None, None, 0x02 | 0x10)
    if device_set == INVALID or device_set is None:
        raise _error("枚举 HID 接口失败")
    found = []
    try:
        index = 0
        while True:
            interface = InterfaceData()
            interface.cbSize = C.sizeof(interface)
            if not next_interface(device_set, None, C.byref(guid), index, C.byref(interface)):
                if C.get_last_error() == ERROR_NO_MORE_ITEMS:
                    break
                raise _error("读取 HID 接口列表失败")
            size = W.DWORD()
            detail(device_set, C.byref(interface), None, 0, C.byref(size), None)
            if not 8 <= size.value <= 65536:
                raise _error("读取 HID 接口路径长度失败")
            buffer = C.create_string_buffer(size.value)
            C.cast(buffer, C.POINTER(W.DWORD)).contents.value = 8 if C.sizeof(PTR) == 8 else 6
            if not detail(device_set, C.byref(interface), buffer, size.value,
                          C.byref(size), None):
                raise _error("读取 HID 接口路径失败")
            path = C.wstring_at(C.addressof(buffer) + 4)
            found.append(_inspect(path))
            index += 1
    finally:
        destroy_set(device_set)
    return found


class HidDevice:
    def __init__(self, path: str, write: bool = False):
        self.handle = _open(path, write=write, overlapped=True)
        try:
            self.caps = _caps(self.handle)
        except BaseException:
            close_handle(self.handle)
            self.handle = None
            raise

    def close(self):
        if self.handle is not None:
            close_handle(self.handle)
            self.handle = None

    def __enter__(self):
        return self

    def __exit__(self, *_):
        self.close()

    def _transfer(self, fn, payload, timeout_ms: int) -> bytes | None:
        if self.handle is None:
            raise HidError("HID 接口已关闭")
        event = create_event(None, True, False, None)
        if not event:
            raise _error("创建 HID 等待事件失败")
        pending = Overlapped()
        pending.hEvent = event
        transferred = W.DWORD()
        try:
            ok = fn(self.handle, payload, C.sizeof(payload), C.byref(transferred),
                    C.byref(pending))
            if not ok:
                error = C.get_last_error()
                if error != ERROR_IO_PENDING:
                    raise HidError(error, f"HID 报告读写失败: {C.FormatError(error).strip()}")
                outcome = wait_event(event, max(0, timeout_ms))
                if outcome == WAIT_TIMEOUT:
                    cancel_io(self.handle, C.byref(pending))
                    # Keep the report and OVERLAPPED alive until cancellation completes.
                    wait_event(event, 0xFFFFFFFF)
                    finish_io(self.handle, C.byref(pending), C.byref(transferred), False)
                    return None
                if outcome != WAIT_OBJECT_0:
                    raise _error("等待 HID 报告失败")
                if not finish_io(self.handle, C.byref(pending),
                                 C.byref(transferred), False):
                    error = C.get_last_error()
                    if error == ERROR_OPERATION_ABORTED:
                        return None
                    raise HidError(error, f"HID 报告读写失败: {C.FormatError(error).strip()}")
            return bytes(payload)[:transferred.value]
        finally:
            close_handle(event)

    def read_input(self, timeout_ms: int = 100) -> bytes | None:
        length = self.caps.InputReportByteLength
        if not length:
            return None
        return self._transfer(read_file, C.create_string_buffer(length), timeout_ms)

    def write_output(self, report: bytes, timeout_ms: int = 1000):
        length = self.caps.OutputReportByteLength
        if not length or len(report) != length:
            raise ValueError(f"Output 报告须为 {length} 字节")
        if self._transfer(write_file, C.create_string_buffer(report, length),
                          timeout_ms) is None:
            raise TimeoutError("发送 HID 报告超时")

    def get_feature_report(self, report_id: int, length: int) -> bytes:
        if not 0 <= report_id <= 255 or length != self.caps.FeatureReportByteLength:
            raise ValueError("Feature 报告编号或长度与设备描述不符")
        buffer = C.create_string_buffer(length)
        buffer[0] = bytes([report_id])
        if not get_feature(self.handle, buffer, length):
            raise _error("读取 HID Feature 报告失败")
        return bytes(buffer)

    def drain(self):
        for _ in range(8):
            if self.read_input(timeout_ms=5) is None:
                break


__all__ = ["HidInterface", "HidDevice", "HidError", "enumerate_hid",
           "HidP_Input", "HidP_Output", "HidP_Feature"]
