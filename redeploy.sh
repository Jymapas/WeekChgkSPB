#!/usr/bin/env bash
set -euo pipefail

# Использование:
#   ./redeploy.sh [путь_к_проекту] [имя_образа] [--no-cache]
# Примеры:
#   ./redeploy.sh
#   ./redeploy.sh /opt/WeekChgkSPB weekchgk-spb:latest
#   ./redeploy.sh /opt/WeekChgkSPB weekchgk-spb:latest --no-cache

PROJECT_DIR="${1:-.}"
IMAGE="${2:-weekchgk-spb:latest}"
NO_CACHE="${3:-}"
COMPOSE="docker compose"

if ! command -v docker >/dev/null 2>&1; then
  echo "Ошибка: docker не найден в PATH."
  exit 1
fi

pushd "$PROJECT_DIR" >/dev/null

BUILD_ARGS=(-t "$IMAGE" .)
BUILD_MODE="c кешем"

if [[ "$NO_CACHE" == "--no-cache" ]]; then
  BUILD_ARGS=(--no-cache -t "$IMAGE" .)
  BUILD_MODE="без кеша"
fi

echo "[1/2] Собираю образ $BUILD_MODE: $IMAGE ..."
DOCKER_BUILDKIT=1 docker build "${BUILD_ARGS[@]}"

echo "[2/2] Пересоздаю контейнеры с новым образом..."
$COMPOSE up -d --no-build --force-recreate

popd >/dev/null
echo "Готово ✅"
