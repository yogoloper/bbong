#!/usr/bin/env bash
# 코어 엔진(BbongCore)을 Release로 빌드해 Unity 클라이언트 Plugins로 복사합니다.
# 코어 로직을 수정할 때마다 실행해 Unity가 참조하는 DLL을 갱신합니다.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$PATH"

DLL="$ROOT/core/BbongCore/bin/Release/netstandard2.1/BbongCore.dll"
DEST="$ROOT/client/BbongClient/Assets/Plugins/BbongCore/"

echo "▶ 코어 빌드 (Release)..."
dotnet build "$ROOT/core/BbongCore" -c Release --nologo -v quiet

echo "▶ DLL 복사 → $DEST"
mkdir -p "$DEST"
cp "$DLL" "$DEST"

echo "✓ 완료. Unity 에디터가 열려 있으면 자동 리임포트됩니다."
