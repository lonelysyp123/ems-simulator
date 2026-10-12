#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
# shellcheck source=pointmap-common.sh
source "$ROOT/scripts/pointmap-common.sh"

RID="${RID:-linux-arm64}"
CONFIG="${CONFIG:-Debug}"
OUT="$ROOT/dist/$RID"
ARCHIVE="$ROOT/dist/EssSimulator-${RID}.tar.gz"

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

echo "==> Publishing EssSimulator for Linux ($RID, $CONFIG, self-contained)..."
dotnet publish EssSimulator.csproj \
  -c "$CONFIG" \
  -r "$RID" \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o "$OUT"

copy_runtime_files

cp "$ROOT/scripts/linux/start.sh" "$OUT/start.sh"
cp "$ROOT/scripts/linux/README-Linux.txt" "$OUT/README-Linux.txt"
chmod +x "$OUT/start.sh" "$OUT/EssSimulator"

echo "==> Creating archive: $ARCHIVE"
rm -f "$ARCHIVE"
tar -czf "$ARCHIVE" -C "$ROOT/dist" "$(basename "$OUT")"

echo "Done."
echo "  Point maps: pointmaps/models/ (选型型回退 standard；LC 片段拼装)"
echo "  Folder:  $OUT"
echo "  Archive: $ARCHIVE"
echo "  Contents:"
ls -lh "$OUT/EssSimulator" "${RUNTIME_FILES[@]/#/$OUT/}" "$ARCHIVE"
