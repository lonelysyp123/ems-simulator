#!/usr/bin/env bash
# 点位表管理与复制（供发布脚本、sync-pointmaps-to-root.sh 引用）
# 运行时从 pointmaps/models/{设备类型}/{型号或片段}/ 解析，不再把 CSV 摊到目标根目录。
#   - 选型型（emu / bms / em / pv）：互斥型号，未选型回退 standard/
#   - 拼装型（lc）：子目录为互补片段，运行期按组展开后拼装，不是选型
# 注意：仅使用 bash 3.2 兼容语法（macOS 自带 bash 为 3.2，不支持 declare -A）。
set -euo pipefail

_POINTMAP_SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]:-$0}")" && pwd)"
POINTMAP_ROOT="${POINTMAP_ROOT:-$(cd "$_POINTMAP_SCRIPT_DIR/.." && pwd)/pointmaps}"

DEFAULT_ROOT_MODEL="standard"
# 空格分隔。这些类型不要求 standard/，改为校验每个片段目录含声明的点表文件。
COMPOSE_TYPES="lc"

# 设备类型 → 型号/片段目录内必须存在的点表文件（每行 "类型 文件1 [文件2 ...]"）
DEVICE_TYPE_FILES="
bms bms_bank.csv bms_rack.csv
emu emu.csv
em em.csv
lc lc.csv
pv pv_logger.csv pv_apm810.csv
"

device_model_dir() {
  local type="$1"
  local model="${2:-$DEFAULT_ROOT_MODEL}"
  echo "$POINTMAP_ROOT/models/$type/$model"
}

is_compose_type() {
  local type="$1"
  local t
  for t in $COMPOSE_TYPES; do
    if [[ "$t" == "$type" ]]; then
      return 0
    fi
  done
  return 1
}

# 列出某类型下的子目录（型号或拼装片段）。无匹配时不回传字面 glob。
list_type_subdirs() {
  local type_dir="$1"
  local d
  for d in "$type_dir"/*; do
    [[ -d "$d" ]] || continue
    echo "$d"
  done
}

validate_compose_type() {
  local type="$1"
  local files="$2"
  local type_dir="$POINTMAP_ROOT/models/$type"
  local found=0
  local model_dir f

  if [[ ! -f "$type_dir/type.json" ]]; then
    echo "拼装类型缺少 type.json: $type_dir/type.json" >&2
    return 1
  fi

  while IFS= read -r model_dir; do
    [[ -z "$model_dir" ]] && continue
    found=1
    for f in $files; do
      if [[ ! -f "$model_dir/$f" ]]; then
        echo "拼装片段 [$type/$(basename "$model_dir")] 缺少文件: $f" >&2
        return 1
      fi
    done
  done < <(list_type_subdirs "$type_dir")

  if [[ "$found" -eq 0 ]]; then
    echo "拼装类型 [$type] 没有任何片段目录（期望如 group / system / unit_10MW）" >&2
    return 1
  fi
  return 0
}

validate_selection_type() {
  local type="$1"
  local files="$2"
  local dir f
  dir="$(device_model_dir "$type")"
  if [[ ! -d "$dir" ]]; then
    echo "设备型号点表目录不存在: $dir" >&2
    return 1
  fi
  for f in $files; do
    if [[ ! -f "$dir/$f" ]]; then
      echo "设备型号点表 [$type/$DEFAULT_ROOT_MODEL] 缺少文件: $f" >&2
      return 1
    fi
  done
  return 0
}

validate_device_models() {
  local type files
  while read -r type files; do
    [[ -z "$type" ]] && continue
    if [[ ! -d "$POINTMAP_ROOT/models/$type" ]]; then
      echo "设备类型点表目录不存在: $POINTMAP_ROOT/models/$type" >&2
      return 1
    fi
    if is_compose_type "$type"; then
      validate_compose_type "$type" "$files" || return 1
    else
      validate_selection_type "$type" "$files" || return 1
    fi
  done <<< "$DEVICE_TYPE_FILES"
  return 0
}

# 将 pointmaps/models 复制到目标目录（发布输出）。不再摊平 CSV 到目标根。
# 第二个参数（旧点位表版本名）已废弃，仅为兼容旧调用保留。
copy_pointmaps_to() {
  local out="$1"
  if [[ $# -ge 2 && "$2" != "$DEFAULT_ROOT_MODEL" ]]; then
    echo "提示: 点位表版本参数 [$2] 已废弃，运行时从 pointmaps/models 解析（选型型按选型，LC 片段拼装）" >&2
  fi

  validate_device_models
  copy_device_models_to "$out"
}

# 设备型号点表（pointmaps/models/{设备类型}/{型号或片段}/）：整目录随发布携带。
# 选型型运行期可在系统配置界面切换；LC 由 Composer 扫描全部片段按组展开拼装。
copy_device_models_to() {
  local out="$1"
  local models_src="$POINTMAP_ROOT/models"
  if [[ ! -d "$models_src" ]]; then
    echo "==> Skip device models (not found: $models_src)"
    return 0
  fi
  echo "==> Copying device model point maps from $models_src ..."
  rm -rf "$out/pointmaps/models"
  mkdir -p "$out/pointmaps"
  cp -R "$models_src" "$out/pointmaps/models"
}

list_device_types() {
  local type files
  while read -r type files; do
    [[ -z "$type" ]] && continue
    echo "$type"
  done <<< "$DEVICE_TYPE_FILES"
}
