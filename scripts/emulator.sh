#!/bin/bash
# 안드로이드 에뮬레이터 조작 모음. Unity 번들 SDK와 별도로 ~/Library/Android/sdk에 설치돼 있다.
#
#   scripts/emulator.sh start [1|2|all]   에뮬레이터 기동(가로 고정)
#   scripts/emulator.sh stop              전부 종료
#   scripts/emulator.sh build             개발 APK 빌드(에디터가 닫혀 있어야 함)
#   scripts/emulator.sh install [1|2|all] APK 설치 + 실행
#   scripts/emulator.sh log               앱 로그만 실시간
#   scripts/emulator.sh shot [1|2]        스크린샷 → /tmp/bbong-shot-N.png
#
# 서버는 호스트에서 따로 띄운다. 에뮬레이터에서 호스트는 10.0.2.2 이며,
# 개발 빌드가 http://10.0.2.2:5080 을 자동으로 본다.

set -e
export ANDROID_HOME="$HOME/Library/Android/sdk"
ADB="$ANDROID_HOME/platform-tools/adb"
EMU="$ANDROID_HOME/emulator/emulator"
UNITY="/Applications/Unity/Hub/Editor/6000.4.10f1/Unity.app/Contents/MacOS/Unity"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
APK="$ROOT/client/Builds/bbong-dev.apk"
PKG="com.yogoloper.bbong"

serial() { [ "$1" = "2" ] && echo emulator-5556 || echo emulator-5554; }

boot_one() {
  local n=$1 port=$(( $1 == 2 ? 5556 : 5554 ))
  echo "▶ bbong-$n 기동 (포트 $port)"
  # -gpu host: Metal 사용. 소프트웨어 렌더러는 느리고 screencap이 검은 화면만 잡는다.
  nohup "$EMU" -avd "bbong-$n" -port $port -no-snapshot-save -no-boot-anim -gpu host \
    > "/tmp/emu$n.log" 2>&1 &
  local dev=$(serial $n)
  for _ in $(seq 1 60); do
    [ "$("$ADB" -s "$dev" shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')" = "1" ] && break
    sleep 5
  done
  "$ADB" -s "$dev" shell settings put system accelerometer_rotation 0
  "$ADB" -s "$dev" shell settings put system user_rotation 1   # 가로
  echo "  준비 완료: $dev"
}

install_one() {
  local dev=$(serial $1)
  [ -f "$APK" ] || { echo "APK 없음 — 먼저 build 하세요"; exit 1; }
  "$ADB" -s "$dev" install -r -g "$APK" | tail -1
  "$ADB" -s "$dev" shell monkey -p "$PKG" -c android.intent.category.LAUNCHER 1 >/dev/null 2>&1
  echo "  실행됨: $dev"
}

case "${1:-}" in
  start)
    case "${2:-1}" in
      all) boot_one 1; boot_one 2 ;;
      *) boot_one "${2:-1}" ;;
    esac
    ;;
  stop)
    for d in emulator-5554 emulator-5556; do "$ADB" -s $d emu kill 2>/dev/null || true; done
    echo "종료 요청 완료"
    ;;
  build)
    pgrep -x Unity >/dev/null && { echo "Unity 에디터가 열려 있어 CLI 빌드 불가"; exit 1; }
    rm -f "$APK"
    "$UNITY" -batchmode -quit -projectPath "$ROOT/client/BbongClient" \
      -executeMethod Bbong.Editor.CliBuild.Android -buildPath "$APK" -development \
      -logFile /tmp/bbong-android-build.log
    [ -f "$APK" ] && echo "APK: $(ls -lh "$APK" | awk '{print $5}')" || { echo "빌드 실패 — /tmp/bbong-android-build.log 확인"; exit 1; }
    ;;
  install)
    case "${2:-1}" in
      all) install_one 1; install_one 2 ;;
      *) install_one "${2:-1}" ;;
    esac
    ;;
  log)
    "$ADB" -s "$(serial "${2:-1}")" logcat -s Unity
    ;;
  shot)
    n="${2:-1}"
    "$ADB" -s "$(serial $n)" exec-out screencap -p > "/tmp/bbong-shot-$n.png"
    echo "/tmp/bbong-shot-$n.png"
    ;;
  *)
    sed -n '2,16p' "$0"
    ;;
esac
