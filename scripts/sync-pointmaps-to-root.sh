#!/usr/bin/env bash
# 可选：校验 pointmaps/models 完整性。开发与运行时不再把点表同步到仓库根。
# 选型型要求 {type}/standard/ 齐全；LC 为拼装型，校验 models/lc/*/lc.csv。
# 发布请用 copy_pointmaps_to（publish-*.sh 已调用），只把 models 目录带到输出。
# 用法:
#   ./scripts/sync-pointmaps-to-root.sh
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
# shellcheck source=pointmap-common.sh
source "$(dirname "$0")/pointmap-common.sh"

validate_device_models

fragments=""
while IFS= read -r d; do
  fragments="$fragments $(basename "$d")"
done < <(list_type_subdirs "$POINTMAP_ROOT/models/lc")

echo "OK. Runtime point maps live in pointmaps/models/ (no root CSV copy needed)."
echo "  选型型: emu/bms/em/pv 回退 $DEFAULT_ROOT_MODEL/"
echo "  拼装型: lc 片段$fragments"
echo "Publish output still gets a full models tree via copy_pointmaps_to."
