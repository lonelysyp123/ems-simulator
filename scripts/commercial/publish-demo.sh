#!/usr/bin/env bash
# 演示版打包：完整功能（商业版能力），无需 license，含组态编辑器预设工程与设备库。
# 演示版定位：对外试用/评估包 —— 打开即见效果、能玩组态编辑器、能连 Modbus。
#
# 用法:
#   ./scripts/commercial/publish-demo.sh                # 默认 osx-arm64（本机）
#   RID=win-x64  ./scripts/commercial/publish-demo.sh   # Windows x64
#   RID=linux-x64 ./scripts/commercial/publish-demo.sh  # Linux x64
#   RID=linux-arm64 ./scripts/commercial/publish-demo.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
# 复用点表复制等公共函数（不调用 validate_edition，演示版不在标准档位列表）
# shellcheck source=publish-common.sh
source "$(dirname "$0")/publish-common.sh"

EDITION="演示版"
RID="${RID:-osx-arm64}"
CONFIG="${CONFIG:-Release}"
OUT="$(dist_out_dir "$EDITION" "$RID")"
DEMO_CONFIG="$ROOT/configs/演示版.appsettings.json"

if [[ ! -f "$DEMO_CONFIG" ]]; then
  echo "缺少演示版配置: $DEMO_CONFIG（请先由 商业版.appsettings.json 生成）" >&2
  exit 1
fi

case "$RID" in
  win-*)   PLATFORM="windows" ;;
  osx-*)   PLATFORM="osx" ;;
  linux-*) PLATFORM="linux" ;;
  *)
    echo "不支持的 RID: $RID（可选 win-x64 / linux-x64 / linux-arm64 / osx-arm64 / osx-x64）" >&2
    exit 1
    ;;
esac

mkdir -p "$OUT"

echo "=========================================="
echo "  演示版发布: $EDITION / $RID ($PLATFORM)"
echo "=========================================="
cd "$ROOT"

echo "==> Publishing EssSimulator (self-contained single-file)..."
dotnet publish EssSimulator.csproj \
  -c "$CONFIG" \
  -r "$RID" \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o "$OUT"

echo "==> Copying runtime config and point maps..."
cp -f "$DEMO_CONFIG" "$OUT/appsettings.json"
echo "    appsettings.json (from configs/演示版.appsettings.json)"
for f in log4net.config autotest.json; do
  cp -f "$ROOT/$f" "$OUT/$f"
  echo "    $f"
done
copy_pointmaps_to "$OUT"
if [[ -d "$ROOT/docs" ]]; then
  rm -rf "$OUT/docs"
  cp -R "$ROOT/docs" "$OUT/docs"
  echo "    docs/"
fi

echo "==> Copying topology data (组态编辑器预设工程/设备库)..."
rm -rf "$OUT/configs"
mkdir -p "$OUT/configs"
if [[ -f "$ROOT/configs/protocol-bindings.json" ]]; then
  cp -f "$ROOT/configs/protocol-bindings.json" "$OUT/configs/protocol-bindings.json"
  echo "    configs/protocol-bindings.json"
fi
if [[ -d "$ROOT/configs/topology" ]]; then
  cp -R "$ROOT/configs/topology" "$OUT/configs/topology"
  echo "    configs/topology/"
else
  echo "    (无 configs/topology，跳过)" >&2
fi

# 演示包默认不进入工程模式；overlay 在「系统配置 → 应用」时才生成
cat > "$OUT/configs/topology/runtime-mode.json" <<'EOF'
{
  "engineeringMode": false,
  "activeProjectId": null,
  "activeProjectName": null,
  "updatedAtUtc": "1970-01-01T00:00:00Z"
}
EOF
rm -f "$OUT/configs/topology/generated/runtime-overlay.json"
echo "    runtime-mode.json -> engineeringMode=false"

echo "==> Copying platform files..."
cp -f "$ROOT/scripts/commercial/editions/$EDITION/README.txt" "$OUT/README.txt"
case "$PLATFORM" in
  windows)
    cp -f "$ROOT/scripts/windows/start.bat" "$OUT/start.bat"
    cp -f "$ROOT/scripts/windows/README-Windows.txt" "$OUT/README-Windows.txt"
    copy_iec61850_windows_native "$OUT"
    ;;
  linux)
    cp -f "$ROOT/scripts/linux/start.sh" "$OUT/start.sh"
    cp -f "$ROOT/scripts/linux/README-Linux.txt" "$OUT/README-Linux.txt"
    chmod +x "$OUT/start.sh"
    chmod +x "$OUT/EssSimulator"
    ;;
  osx)
    cp -f "$ROOT/scripts/linux/start.sh" "$OUT/start.sh"
    cp -f "$ROOT/scripts/osx/README-macOS.txt" "$OUT/README-macOS.txt"
    cp -f "$ROOT/scripts/osx/解除隔离.sh" "$OUT/解除隔离.sh"
    chmod +x "$OUT/start.sh" "$OUT/解除隔离.sh" "$OUT/EssSimulator"
    if command -v codesign >/dev/null 2>&1; then
      codesign --force --sign - --timestamp=none "$OUT/EssSimulator" 2>/dev/null \
        || echo "    (codesign ad-hoc 跳过)"
    fi
    ;;
esac

echo "==> Creating archive..."
if [[ "$PLATFORM" == "windows" ]]; then
  ZIP="$(dist_archive_path "$EDITION" "$RID" zip)"
  rm -f "$ZIP"
  (cd "$ROOT/dist" && zip -r "$(basename "$ZIP")" "$EDITION/$RID")
  echo "    $ZIP"
else
  TAR="$(dist_archive_path "$EDITION" "$RID" tar.gz)"
  rm -f "$TAR"
  tar -czf "$TAR" -C "$ROOT/dist/$EDITION" "$RID"
  echo "    $TAR"
fi

echo ""
echo "Done."
echo "  版本: $EDITION"
echo "  目录: $OUT"
echo "  包:   $(dist_archive_path "$EDITION" "$RID" "$([[ "$PLATFORM" == "windows" ]] && echo zip || echo tar.gz)")"
ls -lh "$OUT"/EssSimulator* "$(dist_archive_path "$EDITION" "$RID" "$([[ "$PLATFORM" == "windows" ]] && echo zip || echo tar.gz)")" 2>/dev/null || true
