#!/usr/bin/env bash
set -euo pipefail

# Использование:
#   ./redeploy.sh [путь_к_проекту] [имя_образа]
# Примеры:
#   ./redeploy.sh
#   ./redeploy.sh /opt/WeekChgkSPB weekchgk-spb:latest

PROJECT_DIR="${1:-.}"
IMAGE="${2:-weekchgk-spb:latest}"
COMPOSE="docker compose"

if ! command -v docker >/dev/null 2>&1; then
  echo "Ошибка: docker не найден в PATH."
  exit 1
fi

pushd "$PROJECT_DIR" >/dev/null

echo "[1/3] Останавливаю стек..."
$COMPOSE down || true

echo "[2/3] Собираю образ без кеша: $IMAGE ..."
docker build --no-cache -t "$IMAGE" .

echo "[3/3] Поднимаю стек в фоне (с пересборкой сервисов)..."
$COMPOSE up -d --build

popd >/dev/null
echo "Готово ✅"
