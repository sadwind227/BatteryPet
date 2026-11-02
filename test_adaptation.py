# SPDX-License-Identifier: MIT
# Copyright (c) 2026 sadwind227
"""Offline adaptation tests. Uses no LLM, real HID requests, credentials or user adapter storage."""
import copy
import json
import os
from pathlib import Path
import struct
import sys
import tempfile
import shutil
import uuid
import unittest
from unittest.mock import patch
import adapter_tool as tool
import adapter_runtime as runtime


def definition():
    return {'schema_version': 1, 'name': 'Test mouse', 'kind': 'hid_feature',
            'device': {'vid': '1234', 'pid': '5678', 'usage_page': 65280, 'usage': 2},
            'report': {'id': 9, 'length': 4, 'battery_offset': 2, 'scale_num': 1, 'scale_den': 1, 'charge_offset': 3, 'charge_mask': 1},
            'evidence': [{'source_id': 'ev-test', 'reason': 'fixture evidence'}], 'notes': 'A candidate, not verified.'}


class AdaptationTests(unittest.TestCase):
    def setUp(self):
        self.base = (Path(__file__).resolve().parent / 'build' / 'adapter-tests').resolve()
        self.root = self.base / uuid.uuid4().hex
        self.root.mkdir(parents=True)

    def tearDown(self):
        if self.root.resolve().parent != self.base:
            raise ValueError('Test cleanup target outside workspace fixture directory')
        shutil.rmtree(self.root)

    def report(self):
        return {'schema_version': 1, 'evidence': [{'source_id': 'ev-test', 'excerpt': 'battery = reply[2]', 'file': 'battery.js', 'line': 1, 'transport_hint': True}],
                'devices': [], 'selected_device': {'vid': '1234', 'pid': '5678'}}

    def test_analyzer_detects_script_evidence_and_redacts_secrets(self):
        (self.root / 'battery.js').write_text('const api_key="do-not-export"; function battery(){ return navigator.hid.receiveFeatureReport(9); }', encoding='utf-8')
        report = tool.analyze(self.root, devices=[])
        self.assertEqual(report['scanned_files'], 1)
        self.assertTrue(report['evidence'][0]['transport_hint'])
        self.assertNotIn('do-not-export', json.dumps(report))
        self.assertIn('REDACTED', json.dumps(report))

    def test_credential_and_agent_config_folders_are_not_read(self):
        for folder in ('.codex', '.dsh', 'credentials'):
            target = self.root / folder
            target.mkdir()
            (target / 'file.js').write_text('battery token private-value', encoding='utf-8')
        (self.root / 'auth.json').write_text('{"battery":"private"}', encoding='utf-8')
        self.assertEqual(tool.analyze(self.root, devices=[])['scanned_files'], 0)

    def test_executable_is_not_run_and_sibling_resources_are_analyzed(self):
        exe = self.root / 'vendor.exe'
        exe.write_bytes(b'not executable')
        (self.root / 'battery.js').write_text('function battery() { return getFeatureReport(9); }', encoding='utf-8')
        self.assertTrue(tool.analyze(exe, devices=[])['evidence'])

    def test_asar_text_is_analyzed_without_extraction(self):
        content = b'function battery(){return navigator.hid.receiveFeatureReport(9);}'
        header = json.dumps({'files': {'battery.js': {'size': len(content), 'offset': '0'}}}).encode()
        (self.root / 'app.asar').write_bytes(struct.pack('<4I', 4, 8 + len(header), 4 + len(header), len(header)) + header + content)
        report = tool.analyze(self.root, devices=[])
        self.assertIn('app.asar::battery.js', report['evidence'][0]['file'])
        self.assertEqual(list(self.root.glob('battery.js')), [])

    def test_plain_native_program_is_not_claimed_as_supported(self):
        (self.root / 'vendor.exe').write_bytes(b'opaque binary')
        report = tool.analyze(self.root, devices=[])
        self.assertEqual(report['evidence'], [])
        self.assertIn('未找到', report['summary'])

    def test_export_creates_new_child_without_touching_workspace_rules(self):
        (self.root / 'AGENTS.md').write_text('original workspace rules', encoding='utf-8')
        result = tool.export_job(self.report(), self.root, 'codex', '1234:5678')
        bundle = Path(result['bundle'])
        self.assertEqual(bundle.parent, self.root)
        self.assertTrue((bundle / 'TASK.md').exists())
        self.assertTrue((bundle / 'tools' / 'adapter_tool.py').exists())
        self.assertEqual((self.root / 'AGENTS.md').read_text(), 'original workspace rules')
        self.assertIn('run --job', result['command'])

    def test_ps_quoting_keeps_single_quotes_and_shell_metacharacters_literal(self):
        self.assertEqual(tool.ps_quote("a'b $(secret) `x`"), "'a''b $(secret) `x`'")

    def test_agent_commands_have_no_permission_bypass_flags(self):
        (self.root / 'TASK.md').write_text('untrusted $(do-not-execute)', encoding='utf-8')
        for agent in ('codex', 'claude', 'hermes', 'dsh'):
            args, stdin = tool.agent_command(agent, self.root, executable='fake-agent.exe')
            self.assertNotIn('--dangerously-bypass-approvals-and-sandbox', args)
            self.assertNotIn('--yolo', args)
            if agent == 'codex':
                self.assertIn('read-only', args)
                self.assertIn('untrusted', stdin)

    def test_workbuddy_uses_workspace_handoff_instead_of_fake_cli(self):
        (self.root / 'TASK.md').write_text('task', encoding='utf-8')
        with self.assertRaises(ValueError):
            tool.agent_command('workspace', self.root)

    def test_npm_shim_runs_node_entry_directly(self):
        entry = self.root / 'node_modules' / 'agent' / 'cli.js'
        entry.parent.mkdir(parents=True)
        entry.write_text('example')
        shim = self.root / 'dsh.cmd'
        shim.write_text('@"%dp0%\\node.exe" "%dp0%\\node_modules\\agent\\cli.js" %*')
        (self.root / 'node.exe').write_bytes(b'fixture')
        args = tool.resolve_launcher([str(shim), '--profile', 'headless', '$(keep literal)'])
        self.assertEqual(args[0], str(self.root / 'node.exe'))
        self.assertEqual(args[1], str(entry))
        self.assertEqual(args[-1], '$(keep literal)')

    def test_candidate_rejects_executable_fields_and_keyboard_usage(self):
        candidate = definition()
        candidate['requests'] = ['arbitrary write']
        with self.assertRaises(ValueError): runtime.validate(candidate)
        candidate = definition()
        candidate['device']['usage_page'] = 1
        with self.assertRaises(ValueError): runtime.validate(candidate)

    def test_candidate_rejects_bad_offsets_and_bool_numbers(self):
        for key, value in (('battery_offset', 0), ('battery_offset', 4), ('length', 99999), ('scale_num', True)):
            data = definition()
            data['report'][key] = value
            with self.subTest(key=key, value=value), self.assertRaises(ValueError): runtime.validate(data)

    def test_decode_uses_windows_report_id_offset_and_preserves_zero(self):
        self.assertEqual(runtime.decode(definition(), bytes([9, 222, 86, 1]))['percent'], 86)
        self.assertTrue(runtime.decode(definition(), bytes([9, 222, 86, 1]))['charging'])
        self.assertEqual(runtime.decode(definition(), bytes([9, 222, 0, 0]))['percent'], 0)
        with self.assertRaises(ValueError): runtime.decode(definition(), bytes([8, 0, 86, 0]))
        with self.assertRaises(ValueError): runtime.decode(definition(), bytes([9, 0, 200, 0]))

    def test_agent_json_requires_real_evidence_ids_and_selected_device(self):
        data = definition()
        result = tool.extract_candidate('```json\n' + json.dumps(data) + '\n```', self.report())
        self.assertEqual(result['name'], data['name'])
        data['evidence'][0]['source_id'] = 'invented'
        with self.assertRaises(ValueError): tool.extract_candidate(json.dumps(data), self.report())
        data = definition()
        data['device']['pid'] = '9999'
        with self.assertRaises(ValueError): tool.extract_candidate(json.dumps(data), self.report())

    def test_two_distinct_values_on_same_device_required_before_enable(self):
        data = definition()
        with self.assertRaises(ValueError): runtime.enable(data, self.root)
        sample = {'percent': 50, 'device_key': 'one', 'firmware': 1, 'report_hash': 'fixture'}
        runtime.record_observation(data, sample, 50, self.root)
        runtime.record_observation(data, sample, 50, self.root)
        self.assertFalse(runtime.verification_status(data, self.root)['verified'])
        runtime.record_observation(data, dict(sample, percent=49, device_key='two'), 49, self.root)
        self.assertFalse(runtime.verification_status(data, self.root)['verified'])
        runtime.record_observation(data, dict(sample, percent=49), 49, self.root)
        self.assertTrue(runtime.verification_status(data, self.root)['verified'])
        runtime.enable(data, self.root)
        self.assertTrue(list((self.root / 'enabled').glob('*.json')))
        runtime.disable(data, self.root)
        self.assertFalse(list((self.root / 'enabled').glob('*.json')))

    def test_mismatch_and_changed_definition_do_not_count_as_verified(self):
        data = definition()
        sample = {'percent': 50, 'device_key': 'one', 'firmware': 1, 'report_hash': 'fixture'}
        runtime.record_observation(data, sample, 30, self.root)
        self.assertEqual(runtime.verification_status(data, self.root)['distinct_values'], 0)
        modified = copy.deepcopy(data)
        modified['report']['battery_offset'] = 1
        self.assertFalse(runtime.verification_status(modified, self.root)['verified'])

    def test_agent_roundtrip_with_fake_cli_no_model_calls(self):
        exported = tool.export_job(self.report(), self.root, 'claude', '1234:5678')
        bundle = Path(exported['bundle'])
        fake = self.root / 'fake_agent.py'
        fake.write_text('import sys\nsys.stdin.read()\nprint(' + repr(json.dumps(definition())) + ')\n', encoding='utf-8')
        with patch.object(tool, 'agent_command', return_value=([sys.executable, str(fake)], 'fixture task')):
            result = tool.run_job(bundle / 'job.json')
        self.assertEqual(result['state'], 'candidate')
        self.assertEqual(runtime.load_json(bundle / 'candidate.json')['name'], 'Test mouse')
        self.assertFalse(runtime.verification_status(definition(), self.root)['verified'])

    def test_unsupported_cannot_probe(self):
        data = definition()
        data['kind'] = 'unsupported'
        with self.assertRaises(ValueError): runtime.probe(data)


if __name__ == '__main__':
    unittest.main()
