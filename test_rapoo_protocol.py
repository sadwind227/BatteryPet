# SPDX-License-Identifier: MIT
# Copyright (c) 2026 sadwind227
"""Synthetic boundary cases, not evidence of live Rapoo compatibility."""
import unittest
from rapoo_protocol import decode_status


class RapooTests(unittest.TestCase):
    def frame(self, percent=50):
        frame = bytearray(19)
        frame[:9] = bytes([7, 0x10, 0, 0x20, 3, 0x20, 3, 0, percent])
        return frame

    def test_percent_boundaries(self):
        for value in (0, 50, 100):
            self.assertEqual(decode_status(self.frame(value))['percent'], value)

    def test_invalid_packets(self):
        for index, value in ((0, 8), (1, 0), (2, 6), (8, 101)):
            frame = self.frame()
            frame[index] = value
            with self.assertRaises(ValueError):
                decode_status(frame)
        for length in (0, 8, 18, 20):
            with self.assertRaises(ValueError):
                decode_status(bytes(length))

    def test_charge_flags(self):
        self.assertFalse(decode_status(self.frame())['charging'])
        for index, value in ((7, 2), (9, 1), (9, 2)):
            frame = self.frame()
            frame[index] = value
            self.assertTrue(decode_status(frame)['charging'])
