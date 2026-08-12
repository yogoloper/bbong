#!/bin/bash
# 안드로이드 에뮬레이터 조작 모음. Unity 번들 SDK와 별도로 ~/Library/Android/sdk에 설치돼 있다.
#
#   scripts/emulator.sh start [N|all]     에뮬레이터 기동(가로 고정). N=1~6, all=6대
#   scripts/emulator.sh stop              전부 종료
#   scripts/emulator.sh build             개발 APK 빌드(에디터가 닫혀 있어야 함)
#   scripts/emulator.sh install [N|all]   APK 설치 + 실행
#   scripts/emulator.sh log [N]           앱 로그만 실시간
#   scripts/emulator.sh shot [N|all]      스크린샷 → /tmp/bbong-shot-N.png
#   scripts/emulator.sh list              기동 상태 확인
#
# 6대는 게임 최대 정원과 같다(2~6인). 대당 1.5GB라 6대면 9GB를 쓴다 — 24GB 기기에서
# Unity 에디터나 빌드와 동시에 돌리면 스왑이 발생하니 한쪽만 쓰는 게 낫다.
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

DEVICES=6
serial() { echo "emulator-$(( 5554 + ($1 - 1) * 2 ))"; }
each() { for n in $(seq 1 $DEVICES); do echo $n; done; }

boot_one() {
  local n=$1 port=$(( 5554 + ($1 - 1) * 2 ))
  echo "▶ bbong-$n 기동 (포트 $port)"
  # -gpu host: Metal 사용. 소프트웨어 렌더러는 느리고 screencap이 검은 화면만 잡는다.
  nohup "$EMU" -avd "bbong-$n" -port $port -no-snapshot-save -no-boot-anim -gpu host \
    > "/tmp/emu$n.log" 2>&1 &
  local dev=$(serial $n)
  for _ in $(seq 1 90); do
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
      all) for n in $(each); do boot_one $n; done ;;
      *) boot_one "${2:-1}" ;;
    esac
    ;;
  stop)
    for n in $(each); do "$ADB" -s "$(serial $n)" emu kill 2>/dev/null || true; done
    echo "종료 요청 완료"
    ;;

  list)
    "$ADB" devices | grep emulator || echo "기동 중인 에뮬레이터 없음"
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
      # 기동 중인 대수만 대상으로 한다(6대를 다 띄우지 않았을 수 있다)
      all) for n in $(each); do
             "$ADB" devices | grep -q "$(serial $n)\s*device" && install_one $n
           done ;;
      *) install_one "${2:-1}" ;;
    esac
    ;;
  log)
    "$ADB" -s "$(serial "${2:-1}")" logcat -s Unity
    ;;
  shot)
    case "${2:-1}" in
      all) for n in $(each); do
             "$ADB" devices | grep -q "$(serial $n)\s*device" || continue
             "$ADB" -s "$(serial $n)" exec-out screencap -p > "/tmp/bbong-shot-$n.png"
             echo "/tmp/bbong-shot-$n.png"
           done ;;
      *) n="${2:-1}"
         "$ADB" -s "$(serial $n)" exec-out screencap -p > "/tmp/bbong-shot-$n.png"
         echo "/tmp/bbong-shot-$n.png" ;;
    esac
    ;;
  *)
    sed -n '2,20p' "$0"
    ;;
esac
