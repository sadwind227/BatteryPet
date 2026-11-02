# SPDX-License-Identifier: MIT
# Copyright (c) 2026 sadwind227
"""Provider contract tests. No hardware I/O, and no synthetic values in normal mode."""
import unittest
from types import SimpleNamespace
from unittest.mock import patch, MagicMock
import battery_bridge as provider


class ProviderTests(unittest.TestCase):
    def receiver(self, **overrides):
        values = dict(vid=0x046D, pid=0xC547, usage_page=0xFF00, output_len=20,
                      path='test', serial='receiver-A', group='test', error='')
        values.update(overrides)
        return SimpleNamespace(**values)

    def test_absent_receiver_does_not_fabricate_battery(self):
        with patch.object(provider, 'enumerate_hid', return_value=[]):
            result = provider.scan(include_custom=False)
        self.assertEqual(result['devices'], [])
        self.assertIn('未发现', result['message'])

    def test_access_denied_is_not_reported_as_missing_receiver(self):
        with patch.object(provider, 'enumerate_hid', return_value=[self.receiver(usage_page=0, error='access denied')]):
            result = provider.scan(include_custom=False)
        self.assertEqual(result['devices'], [])
        self.assertIn('access denied', result['message'])

    def sample(self, reply):
        dev = MagicMock()
        with patch.object(provider, 'enumerate_hid', return_value=[self.receiver()]), \
             patch.object(provider, 'HidDevice', return_value=dev), \
             patch.object(provider, 'feature', side_effect=lambda d, slot, fid: 7 if slot == 1 and fid == 0x1004 else None), \
             patch.object(provider, 'call', return_value=reply):
            return provider.scan(include_custom=False)

    def test_valid_zero_is_a_real_reading(self):
        result = self.sample(bytes([0, 0, 0]))
        self.assertEqual(result['devices'][0]['percent'], 0)

    def test_invalid_percent_and_short_frames_are_rejected(self):
        for reply in (bytes([101, 0, 0]), bytes([86]), None):
            with self.subTest(reply=reply):
                self.assertEqual(self.sample(reply)['devices'], [])

    def test_charging_is_decoded_and_id_is_stable(self):
        first = self.sample(bytes([86, 8, 1]))['devices'][0]
        second = self.sample(bytes([85, 8, 0]))['devices'][0]
        self.assertTrue(first['charging'])
        self.assertFalse(second['charging'])
        self.assertEqual(first['id'], second['id'])


if __name__ == '__main__':
    unittest.main()
