# SPDX-License-Identifier: GPL-3.0-only
# Modified for BatteryPet, 2026-11-02, sadwind227.
# Ported from FerrisPulse RapooProtocol.cs, commit
# 4fcda58d8166d32423dc1175f0110b567f728975, under GPLv3.
# Upstream credits rapoo-tray: Copyright (c) 2026 Iris, MIT.
# See licenses/ferrispulse/README.md and rapoo-tray-MIT.md.
"""Rapoo status decoder. No discovery, device commands or hardware access.

Not enabled as a provider until endpoint and live-device validation is complete.
"""


def decode_status(data):
    if len(data) != 19 or data[0] != 0x07:
        raise ValueError('Expected a 19-byte Rapoo status report')
    if data[1] & 0xF0 not in (0x10, 0x20) or data[2] >= 6:
        raise ValueError('Invalid status mode or stage')
    x = int.from_bytes(data[3:5], 'little')
    y = int.from_bytes(data[5:7], 'little')
    if x < 50 or y < 50 or data[8] > 100:
        raise ValueError('Invalid status fields')
    return {'percent': data[8], 'charging': bool(data[7] & 2) or data[9] in (1, 2)}
