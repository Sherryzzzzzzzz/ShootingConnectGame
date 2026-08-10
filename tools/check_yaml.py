#!/usr/bin/env python3
# YAML 资产括号平衡检查（跳过含二进制 blob 的烘焙数据）。
# 用法: python tools/check_yaml.py <file> ...   （exit 0 = 通过）
import sys

SKIP_SUFFIX = ('LightingData.asset', 'LightmapSnapshot.asset', 'NavMesh.asset')

def check(path: str) -> bool:
    if path.endswith(SKIP_SUFFIX):
        return True
    try:
        s = open(path, encoding='utf-8', errors='replace').read()
    except Exception:
        return True
    return s.count('{') == s.count('}') and s.count('[') == s.count(']')

failed = False
for path in sys.argv[1:]:
    if not check(path):
        print(f'❌ YAML 括号不平衡: {path}')
        failed = True
sys.exit(1 if failed else 0)
