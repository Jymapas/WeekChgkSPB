#!/bin/sh
set -e

echo "Waiting for network..."

while ! ping -c1 8.8.8.8 >/dev/null 2>&1; do
  echo "No network yet, retrying in 10s..."
  sleep 10
done

echo "Network is up. Starting bot..."
exec dotnet WeekChgkSPB.dll