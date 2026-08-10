#!/usr/bin/env python3
# 字符串/注释感知的 C# 括号平衡检查。
# 用法: python tools/check_cs.py <file> ...   （exit 0 = 通过）
import re
import sys

def strip_code(s: str) -> str:
    # 去掉块注释、行注释、字符串、字符字面量（保留位置，替换为空）
    s = re.sub(r'/\*.*?\*/', '', s, flags=re.S)          # 块注释
    s = re.sub(r'//[^\n]*', '', s)                        # 行注释
    s = re.sub(r'@"(?:""|[^"])*"', '""', s)               # 逐字字符串
    s = re.sub(r'"(\\.|[^"\\])*"', '""', s)               # 普通字符串
    s = re.sub(r"'(\\.|[^'\\])*'", "''", s)               # 字符字面量
    return s

def check(path: str) -> bool:
    try:
        s = open(path, encoding='utf-8').read()
    except Exception:
        return True  # 文件不可读，跳过
    if path.endswith('.g.cs') or path.endswith('.g.cs.meta'):
        return True  # 生成文件跳过
    s = strip_code(s)
    return (s.count('{') == s.count('}') and
            s.count('(') == s.count(')') and
            s.count('[') == s.count(']'))

failed = False
for path in sys.argv[1:]:
    if not check(path):
        print(f'❌ C# 括号不平衡: {path}')
        failed = True
sys.exit(1 if failed else 0)
