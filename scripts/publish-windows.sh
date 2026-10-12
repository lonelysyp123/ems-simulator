#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
# shellcheck source=pointmap-common.sh
source "$ROOT/scripts/pointmap-common.sh"

OUT="$ROOT/dist/win-x64"
ZIP="$ROOT/dist/EssSimulator-win-x64.zip"

# 点表：整棵 pointmaps/models 随发布携带。
# 选型型（emu/bms/em/pv）运行期按选型解析；LC 为片段拼装，不再走型号选型。

RUNTIME_FILES=(
  appsettings.json
  log4net.config
  autotest.json
)

copy_runtime_files() {
  echo "==> Copying runtime config..."
  for f in "${RUNTIME_FILES[@]}"; do
    cp -f "$ROOT/$f" "$OUT/$f"
    echo "    $f"
  done
  copy_pointmaps_to "$OUT"
  if [[ -d "$ROOT/docs" ]]; then
    rm -rf "$OUT/docs"
    cp -R "$ROOT/docs" "$OUT/docs"
    echo "    docs/"
  fi
}

validate_device_models

cd "$ROOT"

# 构建 B/S 前端（Vue 3 + Vite）到 wwwroot/
WEB_DIR="$ROOT/Web"
if [[ ! -d "$WEB_DIR" && -d "$ROOT/web" ]]; then
  WEB_DIR="$ROOT/web"
fi

build_frontend() {
  echo "==> Building web frontend (Vue3 + Vite)..."
  if [[ ! -d "$WEB_DIR" ]]; then
    echo "错误: 找不到前端目录 Web/（或 web/）" >&2
    exit 1
  fi
  if [[ ! -d "$WEB_DIR/node_modules" ]]; then
    echo "    installing npm dependencies..."
    (cd "$WEB_DIR" && npm install)
  fi
  (cd "$WEB_DIR" && npm run build)
  echo "    wwwroot/ populated."
}

build_frontend

echo "==> Cleaning previous output: $OUT"
rm -rf "$OUT"
mkdir -p "$OUT"

echo "==> Publishing EssSimulator for Windows x64 (self-contained)..."
dotnet publish EssSimulator.csproj \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -o "$OUT"

copy_runtime_files

cp -f "$ROOT/lib61850/native/win-x64/iec61850.dll" "$OUT/iec61850.dll"
mkdir -p "$OUT/runtimes/win-x64/native"
cp -f "$ROOT/lib61850/native/win-x64/iec61850.dll" "$OUT/runtimes/win-x64/native/iec61850.dll"

cp "$ROOT/scripts/windows/start.bat" "$OUT/start.bat"
cp "$ROOT/scripts/windows/README-Windows.txt" "$OUT/README-Windows.txt"

echo "==> Creating zip: $ZIP"
rm -f "$ZIP"
(cd "$ROOT/dist" && zip -r "$(basename "$ZIP")" win-x64)

echo "Done."
echo "  Point maps: pointmaps/models/ (选型型回退 standard；LC 片段拼装)"
echo "  Folder: $OUT"
echo "  Zip:    $ZIP"
ls -lh "$OUT/EssSimulator.exe" "$ZIP"
