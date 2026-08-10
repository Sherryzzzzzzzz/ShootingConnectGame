#!/usr/bin/env bash
# ============================================================
# 项目健康检查脚本（提交前运行）
# 用法: bash check-health.sh   或   ./check-health.sh
# 检查: git 卫生 / 包版本一致性 / 资产引用 / YAML 结构 / C# 语法嗅探
# ============================================================
set -u
cd "$(dirname "$0")"
FAIL=0
WARN=0

say()  { printf '%s\n' "$*"; }
ok()   { say "  ✅ $*"; }
bad()  { say "  ❌ $*"; FAIL=1; }
warn() { say "  ⚠️  $*"; WARN=1; }

# ------------------------------------------------------------
say "== 1. Git 卫生 =="
JUNK=$(git ls-files | grep -E '/(bin|obj)/|(^|/)\.idea/|(^|/)\.vscode/|\.slnx$|\.deps\.json$|\.runtimeconfig\.json$' 2>/dev/null)
if [ -n "$JUNK" ]; then
  bad "有编译产物/IDE 配置被跟踪："
  echo "$JUNK" | head -10 | sed 's/^/     /'
else
  ok "无 bin/obj/IDE 配置文件被跟踪"
fi

STRAY_DLL=$(git ls-files | grep -E '\.(dll|exe|pdb)$' | grep -v -E '^Assets/Plugins/|^Packages/' || true)
if [ -n "$STRAY_DLL" ]; then
  bad "有非插件位置的 dll/exe 被跟踪："
  echo "$STRAY_DLL" | head -5 | sed 's/^/     /'
else
  ok "无游离二进制被跟踪"
fi

# ------------------------------------------------------------
say "== 2. 包版本一致性 =="
if [ -f "Packages/manifest.json" ] && [ -f "Packages/packages-lock.json" ]; then
  MANIFEST_URP=$(grep -o '"com.unity.render-pipelines.universal": "[^"]*"' Packages/manifest.json | head -1)
  LOCK_URP=$(python -c "
import json
d = json.load(open('Packages/packages-lock.json'))
print(d.get('dependencies',{}).get('com.unity.render-pipelines.universal',{}).get('version','MISSING'))
" 2>/dev/null)
  if [ -z "$LOCK_URP" ] || [ "$LOCK_URP" = "MISSING" ]; then
    warn "packages-lock.json 中找不到 URP 版本（lock 可能过期）"
  else
    MANIFEST_V=$(echo "$MANIFEST_URP" | grep -o '"[0-9.]*"' | tr -d '"')
    if [ "$MANIFEST_V" = "$LOCK_URP" ]; then
      ok "URP 版本一致: manifest=$MANIFEST_V, lock=$LOCK_URP"
    else
      bad "URP 版本漂移: manifest=$MANIFEST_V, lock=$LOCK_URP（运行 dotnet/Unity 重新解析或统一版本）"
    fi
  fi
else
  warn "manifest.json / packages-lock.json 缺失"
fi

# ------------------------------------------------------------
say "== 3. 资产引用完整性（missing script 检测） =="
MISSING=0
# 收集所有脚本 GUID
GUIDS=$(mktemp)
find Assets Packages -name "*.cs.meta" 2>/dev/null | xargs grep -h "^guid:" 2>/dev/null | awk '{print $2}' | sort -u > "$GUIDS"
# 检查场景/prefab 里的 m_Script 引用
for f in $(git ls-files 'Assets/**/*.unity' 'Assets/**/*.prefab' 2>/dev/null | head -50); do
  for g in $(grep -o 'm_Script: {fileID: 11500000, guid: [0-9a-f]\{32\}' "$f" 2>/dev/null | sed 's/.*guid: //'); do
    if ! grep -qx "$g" "$GUIDS"; then
      # 排除内置/包内脚本（Packages 里的 meta 已收集，这里只报确实找不到的）
      say "  ❌ $f 引用缺失脚本 GUID: $g"
      MISSING=1
      FAIL=1
    fi
  done
done
rm -f "$GUIDS"
if [ "$MISSING" = "0" ]; then ok "场景/Prefab 无缺失脚本引用（抽查前 50 个资产）"; fi

# ------------------------------------------------------------
say "== 4. YAML 结构完整性 =="
YAML_FILES=$(git ls-files 'Assets/**/*.unity' 'Assets/**/*.asset' 'Assets/**/*.prefab' 'Assets/**/*.mat' 2>/dev/null)
if [ -n "$YAML_FILES" ] && ! python tools/check_yaml.py $YAML_FILES 2>/dev/null; then
  YAML_FAIL=1
else
  ok "全部 YAML 资产括号平衡"
fi

# ------------------------------------------------------------
say "== 5. C# 快速语法嗅探 =="
CS_FILES=$(git ls-files 'Assets/**/*.cs' 2>/dev/null | grep -v '\.g\.cs$')
if [ -n "$CS_FILES" ] && ! python tools/check_cs.py $CS_FILES 2>/dev/null; then
  CS_FAIL=1
else
  ok "全部 C# 括号平衡"
fi

# ------------------------------------------------------------
say "== 6. 碰撞数据 =="
for f in Assets/StreamingAssets/collision.bin Server/collision.bin; do
  if [ -f "$f" ]; then
    SIZE=$(du -k "$f" | cut -f1)
    if [ "$SIZE" -gt 5000 ]; then
      warn "$f 体积 ${SIZE}KB（>5MB，检查碰撞导出是否失控）"
    else
      ok "$f ${SIZE}KB"
    fi
  fi
done

# ------------------------------------------------------------
say ""
if [ "$FAIL" = "0" ]; then
  say "✅ 健康检查通过（$([ "$WARN" = 1 ] && echo "有警告" || echo "无警告")）"
else
  say "❌ 健康检查未通过，请修复上述问题后再提交"
  exit 1
fi
