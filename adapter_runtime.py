# SPDX-License-Identifier: MIT
# Copyright (c) 2026 sadwind227
"""Bounded, declarative HID battery adapters. Never executes imported code or sends reports."""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import time

KINDS = ('hid_feature', 'hid_input', 'unsupported')
FIELDS = {'schema_version', 'name', 'kind', 'device', 'report', 'evidence', 'notes'}


def storage():
    return Path(os.environ.get('LOCALAPPDATA', str(Path.home()))) / 'BatteryPet' / 'adapters'


def load_json(path, limit=2_000_000):
    with open(path, 'rb') as handle:
        data = handle.read(limit + 1)
    if len(data) > limit:
        raise ValueError('JSON 文件超过大小限制')
    return json.loads(data.decode('utf-8-sig'))


def atomic_json(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_name(path.name + '.tmp')
    temp.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding='utf-8')
    temp.replace(path)


def integer(value, low, high):
    return type(value) is int and low <= value <= high


def validate(data, evidence_ids=None):
    if not isinstance(data, dict) or set(data) != FIELDS or type(data['schema_version']) is not int or data['schema_version'] != 1:
        raise ValueError('适配格式不正确；不接受脚本、请求帧或额外执行字段')
    if data['kind'] not in KINDS or not isinstance(data['name'], str) or not 1 <= len(data['name']) <= 100:
        raise ValueError('适配名称或类型无效')
    device = data['device']
    if not isinstance(device, dict) or set(device) != {'vid', 'pid', 'usage_page', 'usage'}:
        raise ValueError('设备匹配条件不完整')
    for key in ('vid', 'pid'):
        if not isinstance(device[key], str) or len(device[key]) != 4 or any(c not in '0123456789ABCDEFabcdef' for c in device[key]):
            raise ValueError('VID/PID 须为四位十六进制')
    if not integer(device['usage_page'], 0xFF00, 0xFFFF) or not integer(device['usage'], 1, 65535):
        raise ValueError('此版扩展只允许明确匹配的厂商 HID 接口，不能采集键盘/鼠标原始输入')
    r = data['report']
    if not isinstance(r, dict) or set(r) != {'id', 'length', 'battery_offset', 'scale_num', 'scale_den', 'charge_offset', 'charge_mask'}:
        raise ValueError('报告字段不完整')
    if not integer(r['id'], 0, 255) or not integer(r['length'], 2, 512) or not integer(r['battery_offset'], 1, r['length'] - 1):
        raise ValueError('报告编号、长度或电量偏移无效（偏移含 Windows report ID 字节）')
    if not integer(r['scale_num'], 1, 100) or not integer(r['scale_den'], 1, 1000):
        raise ValueError('电量换算范围无效')
    if (r['charge_offset'] is None) != (r['charge_mask'] is None):
        raise ValueError('充电偏移与掩码须同时提供或同时为 null')
    if r['charge_offset'] is not None and (not integer(r['charge_offset'], 1, r['length'] - 1) or not integer(r['charge_mask'], 1, 255)):
        raise ValueError('充电状态字段无效')
    evidence = data['evidence']
    if not isinstance(evidence, list) or not 1 <= len(evidence) <= 20:
        raise ValueError('须提供来源证据')
    for item in evidence:
        if not isinstance(item, dict) or set(item) != {'source_id', 'reason'} or not isinstance(item['source_id'], str) or not isinstance(item['reason'], str) or not 1 <= len(item['reason']) <= 2000:
            raise ValueError('来源证据格式无效')
        if evidence_ids is not None and item['source_id'] not in evidence_ids:
            raise ValueError('证据编号不在本次分析结果中')
    if not isinstance(data['notes'], str) or len(data['notes']) > 8000:
        raise ValueError('适配备注无效')
    return data


def fingerprint(data):
    return hashlib.sha256(json.dumps(validate(data), sort_keys=True, ensure_ascii=False).encode()).hexdigest()


def decode(data, reply):
    r = data['report']
    if len(reply) != r['length'] or reply[0] != r['id']:
        raise ValueError('报告编号或长度不匹配')
    raw = reply[r['battery_offset']]
    percent = raw * r['scale_num'] / r['scale_den']
    if not 0 <= percent <= 100:
        raise ValueError('电量字段不在 0–100 范围，拒绝猜测或截断')
    return {'percent': int(percent + .5), 'charging': bool(reply[r['charge_offset']] & r['charge_mask']) if r['charge_offset'] is not None else False,
            'battery_raw': raw, 'charge_known': r['charge_offset'] is not None}


def probe(data):
    validate(data)
    if data['kind'] == 'unsupported':
        raise ValueError('候选说明此协议尚不支持，不能试读或启用')
    from hid_win import enumerate_hid, HidDevice
    d, r = data['device'], data['report']
    matches = [i for i in enumerate_hid() if (i.vid, i.pid, i.usage_page, i.usage) == (int(d['vid'], 16), int(d['pid'], 16), d['usage_page'], d['usage'])]
    errors = []
    for iface in matches:
        try:
            if data['kind'] == 'hid_feature' and iface.feature_len != r['length']:
                raise ValueError('实际 Feature Report 长度不匹配')
            if data['kind'] == 'hid_input' and iface.input_len != r['length']:
                raise ValueError('实际 Input Report 长度不匹配')
            with HidDevice(iface.path, write=False) as device:
                if data['kind'] == 'hid_feature':
                    reply = device.get_feature_report(r['id'], r['length'])
                else:
                    reply = None
                    until = time.monotonic() + 1.2
                    while time.monotonic() < until:
                        candidate = device.read_input(timeout_ms=100)
                        if candidate and candidate[0] == r['id']:
                            reply = candidate
                            break
                if not reply:
                    raise ValueError('设备没有返回所需报告；可能需厂商查询命令，本版不会自动重放')
                result = decode(data, reply)
                device_key = hashlib.sha256((iface.serial or iface.group or iface.path).encode()).hexdigest()[:16]
                result.update(id=f'extension:{fingerprint(data)[:12]}:{device_key}', name=data['name'], status='已验证扩展', connection='HID', device_key=device_key,
                              firmware=iface.version, report_hash=hashlib.sha256(reply).hexdigest())
                return result
        except (OSError, ValueError) as error:
            errors.append(str(error))
    raise ValueError('；'.join(errors) if errors else '未找到精确匹配的设备接口')


def receipt_path(data, root=None):
    return Path(root or storage()) / 'verification' / (fingerprint(data) + '.json')


def record_observation(data, result, expected, root=None):
    if not integer(expected, 0, 100):
        raise ValueError('对照电量须为 0–100 整数')
    path = receipt_path(data, root)
    receipt = load_json(path) if path.exists() else {'fingerprint': fingerprint(data), 'observations': []}
    receipt['observations'] = receipt.get('observations', [])[-19:]
    receipt['observations'].append({'expected': expected, 'observed': result['percent'], 'matched': expected == result['percent'],
                                  'device_key': result['device_key'], 'firmware': result['firmware'], 'report_hash': result['report_hash'], 'time': time.time()})
    atomic_json(path, receipt)
    return verification_status(data, root)


def verification_status(data, root=None):
    path = receipt_path(data, root)
    if not path.exists():
        return {'verified': False, 'distinct_values': 0}
    receipt = load_json(path)
    groups = {}
    if receipt.get('fingerprint') == fingerprint(data):
        for sample in receipt.get('observations', []):
            if sample.get('matched') is True and integer(sample.get('expected'), 0, 100) and sample['expected'] == sample.get('observed'):
                key = (sample.get('device_key'), sample.get('firmware'))
                groups.setdefault(key, set()).add(sample['expected'])
    count = max((len(values) for values in groups.values()), default=0)
    return {'verified': data['kind'] != 'unsupported' and count >= 2, 'distinct_values': count}


def enable(data, root=None):
    root = Path(root or storage())
    if not verification_status(data, root)['verified']:
        raise ValueError('需在同一设备的两个不同真实电量下，与厂商显示值对照一致后才能启用')
    target = root / 'enabled' / (fingerprint(data) + '.json')
    if not target.exists() and len(list(target.parent.glob('*.json'))) >= 4:
        raise ValueError('此版最多同时启用四个扩展适配')
    atomic_json(target, data)


def disable(data, root=None):
    root = Path(root or storage()) / 'enabled'
    target = root / (fingerprint(data) + '.json')
    if target.resolve().parent != root.resolve():
        raise ValueError('适配目录不匹配')
    target.unlink(missing_ok=True)


def scan_installed(root=None):
    root = Path(root or storage())
    results, messages = [], []
    for path in sorted((root / 'enabled').glob('*.json'))[:4]:
        try:
            data = validate(load_json(path))
            if not verification_status(data, root)['verified']:
                raise ValueError('适配验证记录缺失')
            worker = subprocess.run([sys.executable, str(Path(__file__).resolve()), '--read', str(path)], capture_output=True,
                                    timeout=2.5, creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0))
            reply = json.loads(worker.stdout.decode('utf-8'))
            if 'error' in reply:
                raise ValueError(reply['error'])
            results.append(reply)
        except Exception as error:
            messages.append(path.stem[:8] + '：' + str(error)[:200])
    return results, messages


if __name__ == '__main__':
    try:
        if len(sys.argv) != 3 or sys.argv[1] != '--read':
            raise ValueError('仅支持 --read <adapter.json>')
        output = probe(validate(load_json(sys.argv[2])))
    except Exception as error:
        output = {'error': str(error)}
    sys.stdout.buffer.write(json.dumps(output, ensure_ascii=False).encode('utf-8'))
