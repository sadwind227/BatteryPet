# SPDX-License-Identifier: MIT
# Copyright (c) 2026 sadwind227
"""Query-only HID++ diagnostic using BatteryPet's Windows HID transport."""
import json
import time
from hid_win import enumerate_hid, HidDevice


def call(dev, index, feature, function, params=()):
    dev.drain()
    packet = bytes([0x11, index, feature, (function << 4) | 0x0D] + list(params))
    dev.write_output(packet.ljust(20, b'\0'))
    deadline = time.monotonic() + 0.8
    while time.monotonic() < deadline:
        reply = dev.read_input(timeout_ms=100)
        if not reply or len(reply) < 4:
            continue
        if reply[1] == index and reply[2] == feature and reply[3] == packet[3]:
            return reply[4:]
        if reply[1] == index and reply[2] in (0x8F, 0xFF):
            return None
    return None


def feature(dev, index, identifier):
    result = call(dev, index, 0, 0, [identifier >> 8, identifier & 255])
    return result[0] if result and result[0] else None


def main():
    interfaces = [d for d in enumerate_hid() if d.vid == 0x046D and d.pid == 0xC547
                  and d.usage_page == 0xFF00 and d.output_len == 20]
    for iface in interfaces:
        with HidDevice(iface.path, write=True) as dev:
            for index in (1, 2):
                result = {'receiver': '046D:C547', 'slot': index}
                name_feature = feature(dev, index, 5)
                if name_feature:
                    size = call(dev, index, name_feature, 0)
                    if size:
                        name = bytearray()
                        while len(name) < size[0]:
                            chunk = call(dev, index, name_feature, 1, [len(name)])
                            if not chunk:
                                break
                            name.extend(chunk[:size[0]-len(name)])
                        result['name'] = name.decode('utf-8', errors='replace')
                for identifier in (0x1004, 0x1000, 0x1001):
                    fid = feature(dev, index, identifier)
                    if fid:
                        reply = call(dev, index, fid, 1 if identifier == 0x1004 else 0)
                        result[hex(identifier)] = list(reply) if reply else None
                        if identifier == 0x1004 and reply and len(reply) >= 3:
                            result['battery_percent'] = reply[0]
                            result['status'] = {0: 'discharging', 1: 'charging', 2: 'charging', 3: 'full'}.get(reply[2], 'unknown')
                            result['queried_at'] = time.strftime('%Y-%m-%d %H:%M:%S%z')
                print(json.dumps(result, ensure_ascii=False), flush=True)


if __name__ == '__main__':
    main()
