"""Single-shot Logitech provider. JSON only on stdout; no device settings writes."""
import hashlib
import json
import sys
from hid_win import enumerate_hid, HidDevice
from read_logitech import call, feature


def scan_builtin():
    devices = enumerate_hid()
    receivers = [d for d in devices if d.vid == 0x046D and d.pid == 0xC547]
    candidates = [d for d in receivers if d.usage_page == 0xFF00 and d.output_len == 20]
    results = []
    for iface in candidates:
        try:
            with HidDevice(iface.path, write=True) as dev:
                for slot in (1, 2):
                    identity = iface.serial or iface.group or iface.path
                    key = hashlib.sha256(identity.encode()).hexdigest()[:16] + ':' + str(slot)
                    name = '罗技鼠标'
                    name_feature = feature(dev, slot, 5)
                    if name_feature:
                        size = call(dev, slot, name_feature, 0)
                        if size and 0 < size[0] <= 100:
                            raw = bytearray()
                            while len(raw) < size[0]:
                                chunk = call(dev, slot, name_feature, 1, [len(raw)])
                                if not chunk:
                                    break
                                raw.extend(chunk[:size[0] - len(raw)])
                            if len(raw) == size[0]:
                                name = raw.decode('utf-8', errors='replace')
                    fid = feature(dev, slot, 0x1004)
                    if not fid:
                        fid = feature(dev, slot, 0x1004)
                    if not fid:
                        continue
                    reply = call(dev, slot, fid, 1)
                    if not reply or len(reply) < 3 or reply[0] > 100:
                        continue
                    results.append({'id': key, 'name': name, 'percent': reply[0],
                                    'charging': reply[2] in (1, 2), 'status': '已连接 · 2.4G'})
        except OSError as error:
            return {'devices': results, 'message': '接收器访问失败：' + str(error)}
    if results:
        return {'devices': results, 'message': '电量已更新'}
    if receivers and not candidates:
        detail = '；'.join(sorted(set(d.error for d in receivers if d.error)))
        return {'devices': [], 'message': '发现 C547 接收器，无法访问通信接口' + ('：' + detail if detail else '')}
    return {'devices': [], 'message': '鼠标暂未响应，请唤醒鼠标后刷新' if receivers else '未发现支持的罗技 C547 接收器'}


def scan(include_custom=True):
    result = scan_builtin()
    if include_custom:
        try:
            from adapter_runtime import scan_installed
            readings, warnings = scan_installed()
            result['devices'].extend(readings)
            if readings:
                result['message'] = '电量已更新（包含已验证扩展）'
            if warnings:
                result['message'] += '；扩展读取失败：' + '；'.join(warnings)
        except Exception as error:
            result['message'] += '；扩展不可用：' + str(error)
    return result


if __name__ == '__main__':
    try:
        result = scan()
    except Exception as error:
        result = {'devices': [], 'message': '读取失败：' + str(error)}
    sys.stdout.buffer.write(json.dumps(result, ensure_ascii=False).encode('utf-8'))
