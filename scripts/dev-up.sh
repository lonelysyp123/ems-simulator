#!/usr/bin/env bash
# 开发态一键启动：后端 (dotnet) + 前端 (Vite HMR)
# 用法:
#   ./scripts/dev-up.sh              # 启动前后端，Ctrl+C 一起退出
#   ./scripts/dev-up.sh --backend    # 仅后端
#   ./scripts/dev-up.sh --frontend   # 仅前端（需后端已在跑）
#   ./scripts/dev-up.sh --no-open    # 不自动打开浏览器
#   HTTP_PORT=5050 VITE_PORT=5173 ./scripts/dev-up.sh
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
# shellcheck source=pointmap-common.sh
source "$ROOT/scripts/pointmap-common.sh"
cd "$ROOT"

HTTP_PORT="${HTTP_PORT:-5050}"
VITE_PORT="${VITE_PORT:-5173}"
BACKEND_URL="${VITE_BACKEND:-http://localhost:${HTTP_PORT}}"
WEB_DIR="$ROOT/Web"
# macOS 上 Web / web 可能是同一目录；优先用仓库内实际存在的路径
if [[ ! -d "$WEB_DIR" && -d "$ROOT/web" ]]; then
  WEB_DIR="$ROOT/web"
fi

MODE="all"
OPEN_BROWSER=1
for arg in "$@"; do
  case "$arg" in
    --backend)  MODE="backend" ;;
    --frontend) MODE="frontend" ;;
    --no-open)  OPEN_BROWSER=0 ;;
    -h|--help)
      cat <<'EOF'
开发态一键启动：后端 (dotnet) + 前端 (Vite HMR)

用法:
  ./scripts/dev-up.sh              # 启动前后端，Ctrl+C 一起退出
  ./scripts/dev-up.sh --backend    # 仅后端
  ./scripts/dev-up.sh --frontend   # 仅前端（需后端已在跑）
  ./scripts/dev-up.sh --no-open    # 不自动打开浏览器
  HTTP_PORT=5050 VITE_PORT=5173 ./scripts/dev-up.sh
EOF
      exit 0
      ;;
    *)
      echo "未知参数: $arg（可用 --help）" >&2
      exit 1
      ;;
  esac
done

BACKEND_PID=""
FRONTEND_PID=""

cleanup() {
  trap - EXIT INT TERM
  echo
  echo "==> 正在停止开发服务..."
  if [[ -n "${FRONTEND_PID}" ]] && kill -0 "$FRONTEND_PID" 2>/dev/null; then
    kill "$FRONTEND_PID" 2>/dev/null || true
    wait "$FRONTEND_PID" 2>/dev/null || true
  fi
  if [[ -n "${BACKEND_PID}" ]] && kill -0 "$BACKEND_PID" 2>/dev/null; then
    kill "$BACKEND_PID" 2>/dev/null || true
    wait "$BACKEND_PID" 2>/dev/null || true
  fi
  # 兜底：释放本脚本常用端口上的残留进程
  if command -v lsof >/dev/null 2>&1; then
    local p
    if [[ "$MODE" != "frontend" ]]; then
      p="$(lsof -tiTCP:"$HTTP_PORT" -sTCP:LISTEN 2>/dev/null || true)"
      [[ -n "$p" ]] && kill $p 2>/dev/null || true
    fi
    if [[ "$MODE" != "backend" ]]; then
      p="$(lsof -tiTCP:"$VITE_PORT" -sTCP:LISTEN 2>/dev/null || true)"
      [[ -n "$p" ]] && kill $p 2>/dev/null || true
    fi
  fi
  echo "==> 已退出"
}

port_in_use() {
  local port="$1"
  if command -v lsof >/dev/null 2>&1; then
    lsof -tiTCP:"$port" -sTCP:LISTEN >/dev/null 2>&1
  else
    return 1
  fi
}

wait_http() {
  local url="$1"
  local name="$2"
  local retries="${3:-60}"
  local i
  for ((i = 1; i <= retries; i++)); do
    if curl -sf -o /dev/null "$url" 2>/dev/null; then
      echo "    $name 就绪: $url"
      return 0
    fi
    sleep 0.5
  done
  echo "    警告: 等待 $name 超时 ($url)" >&2
  return 1
}

ensure_frontend_deps() {
  if [[ ! -d "$WEB_DIR/node_modules" ]]; then
    echo "==> 安装前端依赖 (npm install)..."
    (cd "$WEB_DIR" && npm install)
  fi
}

start_backend() {
  if port_in_use "$HTTP_PORT"; then
    echo "错误: 端口 $HTTP_PORT 已被占用，请先释放或设置 HTTP_PORT=..." >&2
    exit 1
  fi
  # 运行时从 pointmaps/models/ 解析：选型型回退 standard/，LC 扫描片段拼装
  if ! validate_device_models; then
    exit 1
  fi
  echo "==> 启动后端: dotnet run (http://localhost:${HTTP_PORT})"
  (
    cd "$ROOT"
    # 覆盖配置端口，避免与 appsettings / 环境不一致
    exec env "Simulator__Web__HttpPort=${HTTP_PORT}" \
      dotnet run --project EssSimulator.csproj --no-launch-profile
  ) &
  BACKEND_PID=$!
}

start_frontend() {
  if port_in_use "$VITE_PORT"; then
    echo "错误: 端口 $VITE_PORT 已被占用，请先释放或设置 VITE_PORT=..." >&2
    exit 1
  fi
  ensure_frontend_deps
  echo "==> 启动前端: Vite (http://localhost:${VITE_PORT} → ${BACKEND_URL})"
  (
    cd "$WEB_DIR"
    exec env "VITE_BACKEND=${BACKEND_URL}" npm run dev -- --port "$VITE_PORT" --host 0.0.0.0
  ) &
  FRONTEND_PID=$!
}

open_ui() {
  local url="$1"
  [[ "$OPEN_BROWSER" -eq 1 ]] || return 0
  if command -v open >/dev/null 2>&1; then
    (sleep 1 && open "$url") >/dev/null 2>&1 &
  elif command -v xdg-open >/dev/null 2>&1; then
    (sleep 1 && xdg-open "$url") >/dev/null 2>&1 &
  fi
}

trap cleanup EXIT INT TERM

echo "EssSimulator 开发启动"
echo "  根目录: $ROOT"
echo "  模式:   $MODE"
echo

case "$MODE" in
  backend)
    start_backend
    wait_http "${BACKEND_URL}/api/health" "后端" || true
    echo
    echo "后端运行中。Ctrl+C 退出。"
    echo "  UI(发布态同源): ${BACKEND_URL}/"
    wait "$BACKEND_PID"
    ;;
  frontend)
    start_frontend
    wait_http "http://127.0.0.1:${VITE_PORT}/" "前端" || true
    open_ui "http://localhost:${VITE_PORT}/"
    echo
    echo "前端运行中。Ctrl+C 退出。"
    echo "  开发 UI: http://localhost:${VITE_PORT}/"
    wait "$FRONTEND_PID"
    ;;
  all)
    start_backend
    wait_http "${BACKEND_URL}/api/health" "后端" || true
    start_frontend
    wait_http "http://127.0.0.1:${VITE_PORT}/" "前端" || true
    open_ui "http://localhost:${VITE_PORT}/"
    echo
    echo "开发服务已启动。Ctrl+C 同时停止前后端。"
    echo "  前端(HMR): http://localhost:${VITE_PORT}/"
    echo "  后端 API:  ${BACKEND_URL}/api/health"
    echo "  后端同源:  ${BACKEND_URL}/  (需已有 wwwroot 构建产物)"
    echo "  提示: 系统配置「应用到仿真」会重启后端；本脚本会自动拉起。"
    # 前端退出则结束；后端退出则自动重启（支持组态工程模式热应用）
    while true; do
      if ! kill -0 "$FRONTEND_PID" 2>/dev/null; then
        echo "前端已退出" >&2
        exit 1
      fi
      if ! kill -0 "$BACKEND_PID" 2>/dev/null; then
        echo "==> 后端已退出，正在按当前配置重启..." >&2
        sleep 1
        if port_in_use "$HTTP_PORT"; then
          echo "    等待端口 $HTTP_PORT 释放..." >&2
          sleep 2
        fi
        start_backend
        wait_http "${BACKEND_URL}/api/health" "后端" 90 || true
      fi
      sleep 1
    done
    ;;
esac
