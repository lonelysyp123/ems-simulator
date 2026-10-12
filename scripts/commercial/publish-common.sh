#!/usr/bin/env bash
# 发布脚本公共函数：dist 仅含三个版本目录 + 根目录压缩包
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
# shellcheck source=../pointmap-common.sh
source "$ROOT/scripts/pointmap-common.sh"

EDITION_COMMUNITY="社区版"
EDITION_COMMERCIAL="商业版"
EDITION_CUSTOM="定制版"
# 兼容旧名「充值版」→ 商业版
EDITION_RECHARGE_ALIAS="充值版"
ALL_EDITIONS=("$EDITION_COMMUNITY" "$EDITION_COMMERCIAL" "$EDITION_CUSTOM")

RUNTIME_FILES=(
  log4net.config
  autotest.json
)

edition_config_file() {
  local edition="$1"
  case "$edition" in
    "$EDITION_COMMUNITY") echo "$ROOT/configs/社区版.appsettings.json" ;;
    "$EDITION_COMMERCIAL"|"$EDITION_RECHARGE_ALIAS") echo "$ROOT/configs/商业版.appsettings.json" ;;
    "$EDITION_CUSTOM") echo "$ROOT/configs/定制版.appsettings.json" ;;
    *)
      echo "未知版本: $edition（可选: ${ALL_EDITIONS[*]}）" >&2
      return 1
      ;;
  esac
}

edition_readme_file() {
  local edition="$1"
  # 旧名充值版复用商业版说明
  if [[ "$edition" == "$EDITION_RECHARGE_ALIAS" ]]; then
    edition="$EDITION_COMMERCIAL"
  fi
  local readme="$ROOT/scripts/commercial/editions/$edition/README.txt"
  if [[ -f "$readme" ]]; then
    echo "$readme"
  else
    echo "$ROOT/scripts/README.txt"
  fi
}

dist_out_dir() {
  echo "$ROOT/dist/$1/$2"
}

dist_archive_path() {
  local edition="$1"
  local rid="$2"
  local ext="$3"
  echo "$ROOT/dist/EssSimulator-${edition}-${rid}.${ext}"
}

ensure_dist_layout() {
  mkdir -p "$ROOT/dist"
  local edition rid
  for edition in "${ALL_EDITIONS[@]}"; do
    mkdir -p "$ROOT/dist/$edition"
  done
}

validate_edition() {
  local edition="$1"
  edition="$(normalize_edition "$edition")"
  local e
  for e in "${ALL_EDITIONS[@]}"; do
    if [[ "$e" == "$edition" ]]; then
      edition_config_file "$edition" >/dev/null
      return 0
    fi
  done
  echo "未知版本: $edition（可选: ${ALL_EDITIONS[*]}；兼容旧名 充值版→商业版）" >&2
  return 1
}

# 将旧名「充值版」规范为「商业版」
normalize_edition() {
  case "$1" in
    "$EDITION_RECHARGE_ALIAS") echo "$EDITION_COMMERCIAL" ;;
    *) echo "$1" ;;
  esac
}

copy_runtime_files() {
  local out="$1"
  local edition="$2"
  local config
  config="$(edition_config_file "$edition")"

  echo "==> Copying runtime config and point maps ($edition, models + LC fragments)..."
  cp -f "$config" "$out/appsettings.json"
  echo "    appsettings.json (from $(basename "$config"))"

  mkdir -p "$out/configs"
  if [[ -f "$ROOT/configs/protocol-bindings.json" ]]; then
    cp -f "$ROOT/configs/protocol-bindings.json" "$out/configs/protocol-bindings.json"
    echo "    configs/protocol-bindings.json"
  fi

  local f
  for f in "${RUNTIME_FILES[@]}"; do
    cp -f "$ROOT/$f" "$out/$f"
    echo "    $f"
  done

  copy_pointmaps_to "$out"

  if [[ -f "$ROOT/pointmap.manifest.json" ]]; then
    cp -f "$ROOT/pointmap.manifest.json" "$out/pointmap.manifest.json"
    echo "    pointmap.manifest.json"
  fi

  if [[ -d "$ROOT/docs" ]]; then
    rm -rf "$out/docs"
    cp -R "$ROOT/docs" "$out/docs"
    echo "    docs/"
  fi
}

# Windows 单文件发布会把 runtimes/win-x64/native/iec61850.dll 打进 exe，运行时找不到。
# 再抄一份到 exe 同目录（DllImport 默认探测位置）。
copy_iec61850_windows_native() {
  local out="$1"
  local src="$ROOT/lib61850/native/win-x64/iec61850.dll"
  if [[ ! -f "$src" ]]; then
    echo "警告: 缺少 $src，Windows 包内 IEC 61850 将无法启动" >&2
    return 0
  fi
  cp -f "$src" "$out/iec61850.dll"
  mkdir -p "$out/runtimes/win-x64/native"
  cp -f "$src" "$out/runtimes/win-x64/native/iec61850.dll"
  echo "    iec61850.dll"
}

copy_platform_files() {
  local out="$1"
  local edition="$2"
  local platform="$3"

  cp -f "$(edition_readme_file "$edition")" "$out/README.txt"

  case "$platform" in
    windows)
      cp -f "$ROOT/scripts/windows/start.bat" "$out/start.bat"
      cp -f "$ROOT/scripts/windows/README-Windows.txt" "$out/README-Windows.txt"
      copy_iec61850_windows_native "$out"
      ;;
    linux)
      cp -f "$ROOT/scripts/linux/start.sh" "$out/start.sh"
      cp -f "$ROOT/scripts/linux/README-Linux.txt" "$out/README-Linux.txt"
      chmod +x "$out/start.sh"
      if [[ -f "$out/EssSimulator" ]]; then
        chmod +x "$out/EssSimulator"
      fi
      ;;
    osx|macos|mac)
      # start.sh 已支持 macOS 的 open 打开浏览器
      cp -f "$ROOT/scripts/linux/start.sh" "$out/start.sh"
      cp -f "$ROOT/scripts/osx/README-macOS.txt" "$out/README-macOS.txt"
      cp -f "$ROOT/scripts/osx/解除隔离.sh" "$out/解除隔离.sh"
      chmod +x "$out/start.sh" "$out/解除隔离.sh"
      if [[ -f "$out/EssSimulator" ]]; then
        chmod +x "$out/EssSimulator"
        # 本地 ad-hoc 签名（不能替代 Apple 公证，但有助于部分环境识别）
        if command -v codesign >/dev/null 2>&1; then
          codesign --force --sign - --timestamp=none "$out/EssSimulator" 2>/dev/null \
            || echo "    (codesign ad-hoc 跳过)"
        fi
      fi
      ;;
    *)
      echo "未知平台: $platform" >&2
      return 1
      ;;
  esac
}

create_zip_archive() {
  local edition="$1"
  local rid="$2"
  local zip
  zip="$(dist_archive_path "$edition" "$rid" zip)"
  echo "==> Creating zip: $zip"
  rm -f "$zip"
  (cd "$ROOT/dist" && zip -r "$(basename "$zip")" "$edition/$rid")
}

create_tar_archive() {
  local edition="$1"
  local rid="$2"
  local archive
  archive="$(dist_archive_path "$edition" "$rid" tar.gz)"
  echo "==> Creating archive: $archive"
  rm -f "$archive"
  tar -czf "$archive" -C "$ROOT/dist/$edition" "$rid"
}
