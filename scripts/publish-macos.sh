#!/usr/bin/env bash
# macOS 发布（默认 osx-arm64；可 RID=osx-x64）
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
# shellcheck source=pointmap-common.sh
source "$ROOT/scripts/pointmap-common.sh"

RID="${RID:-osx-arm64}"
CONFIG="${CONFIG:-Release}"
OUT="$ROOT/dist/$RID"
ARCHIVE="$ROOT/dist/EssSimulator-${RID}.tar.gz"

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
  # 组态工程/设备库与协议绑定：组态编辑器、端口页依赖这些文件
  rm -rf "$OUT/configs"
  mkdir -p "$OUT/configs"
  for c in protocol-bindings.json ems-strategy.json; do
    if [[ -f "$ROOT/configs/$c" ]]; then
      cp -f "$ROOT/configs/$c" "$OUT/configs/$c"
      echo "    configs/$c"
    fi
  done
  if [[ -d "$ROOT/configs/topology" ]]; then
    cp -R "$ROOT/configs/topology" "$OUT/configs/topology"
    rm -f "$OUT/configs/topology/generated/runtime-overlay.json"
    cat > "$OUT/configs/topology/runtime-mode.json" <<'JSON'
{
  "engineeringMode": false,
  "activeProjectId": null,
  "activeProjectName": null,
  "updatedAtUtc": "1970-01-01T00:00:00Z"
}
JSON
    echo "    configs/topology/"
  fi
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

echo "==> Publishing EssSimulator for macOS ($RID, $CONFIG, self-contained)..."
dotnet publish EssSimulator.csproj \
  -c "$CONFIG" \
  -r "$RID" \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o "$OUT"

copy_runtime_files

cp -f "$ROOT/scripts/linux/start.sh" "$OUT/start.sh"
cp -f "$ROOT/scripts/osx/README-macOS.txt" "$OUT/README-macOS.txt"
cp -f "$ROOT/scripts/osx/解除隔离.sh" "$OUT/解除隔离.sh"
chmod +x "$OUT/start.sh" "$OUT/解除隔离.sh" "$OUT/EssSimulator"

# 本地 ad-hoc 签名（不能替代 Apple 公证，但有助于部分环境识别）
if command -v codesign >/dev/null 2>&1; then
  codesign --force --sign - --timestamp=none "$OUT/EssSimulator" 2>/dev/null \
    || echo "    (codesign ad-hoc 跳过)"
fi

echo "==> Creating archive: $ARCHIVE"
rm -f "$ARCHIVE"
tar -czf "$ARCHIVE" -C "$ROOT/dist" "$(basename "$OUT")"

echo "Done."
echo "  Point maps: pointmaps/models/ (选型型回退 standard；LC 片段拼装)"
echo "  Folder:  $OUT"
echo "  Archive: $ARCHIVE"
ls -lh "$OUT/EssSimulator" "$ARCHIVE"
