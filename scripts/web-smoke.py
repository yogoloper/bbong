#!/usr/bin/env python3
"""BBONG WebGL 스모크 테스트 (Playwright).

게임 로드 → 게스트 로그인 → 연습 모드 진입 → 봇 게임 진행 관찰까지 자동으로 돌리고,
페이지 에러/게임 로그([BBONG])/스크린샷으로 판정한다.

Unity WebGL은 전부 <canvas> 렌더라 DOM 셀렉터가 없다. 대신 UI가 코드 생성이라
앵커 좌표(0~1)가 결정적이므로, 캔버스 위치를 읽어 좌표 클릭으로 조작한다.
UI 레이아웃(앵커)을 바꾸면 아래 ANCHORS도 함께 갱신할 것.

사용법:
  python3 scripts/web-smoke.py                       # 로컬 빌드 + 로컬 서버
  python3 scripts/web-smoke.py --url https://yogoloper.github.io/bbong \
      --server https://bbong.fly.dev                 # 배포본 검증

사전 준비: pip3 install playwright && python3 -m playwright install chromium
종료 코드: 0=통과, 1=실패.
"""

import argparse
import sys
import time

from playwright.sync_api import sync_playwright

# 클릭 지점 (캔버스 기준 정규화 좌표, 좌상단 원점) — 코드 생성 UI 앵커에서 유도
ANCHORS = {
    "guest_login": (0.500, 0.548),   # AuthBootstrap "게스트로 시작"
    "lobby_practice": (0.253, 0.550),  # 메인 로비 2번째 카드 "연습"
    "practice_start": (0.500, 0.895),  # 연습 설정 "게임 시작" CTA
}

UNITY_LOAD_WAIT_MS = 15_000  # WebGL 초기화 대기
GAME_OBSERVE_MS = 20_000     # 봇 게임 진행 관찰 시간


def canvas_click(page, canvas_box, anchor):
    x = canvas_box["x"] + canvas_box["width"] * anchor[0]
    y = canvas_box["y"] + canvas_box["height"] * anchor[1]
    page.mouse.click(x, y)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--url", default="http://localhost:8801", help="웹 빌드 URL")
    parser.add_argument("--server", default="http://localhost:5080", help="게임 서버 URL(?server= 파라미터)")
    parser.add_argument("--shots", default="/tmp/bbong-smoke", help="스크린샷 저장 경로 접두사")
    args = parser.parse_args()

    logs: list[str] = []
    errors: list[str] = []

    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        page = browser.new_page(viewport={"width": 1280, "height": 720})
        page.on("console", lambda m: logs.append(m.text))
        page.on("pageerror", lambda e: errors.append(str(e)))

        target = f"{args.url.rstrip('/')}/?server={args.server}"
        print(f"▶ 접속: {target}")
        page.goto(target)
        page.wait_for_load_state("networkidle")
        page.wait_for_timeout(UNITY_LOAD_WAIT_MS)

        canvas = page.query_selector("#unity-canvas")
        if canvas is None:
            print("✗ 실패: #unity-canvas 없음 — 페이지/빌드 로드 실패")
            page.screenshot(path=f"{args.shots}-fail.png")
            return 1

        box = canvas.bounding_box()
        page.screenshot(path=f"{args.shots}-title.png")

        print("▶ 게스트 로그인 → 로비 → 연습 게임 시작")
        canvas_click(page, box, ANCHORS["guest_login"])
        page.wait_for_timeout(4_000)
        page.screenshot(path=f"{args.shots}-lobby.png")
        canvas_click(page, box, ANCHORS["lobby_practice"])
        page.wait_for_timeout(2_500)
        canvas_click(page, box, ANCHORS["practice_start"])

        print(f"▶ 봇 게임 {GAME_OBSERVE_MS // 1000}초 관찰")
        page.wait_for_timeout(GAME_OBSERVE_MS)
        page.screenshot(path=f"{args.shots}-game.png")
        browser.close()

    game_logs = [line for line in logs if "BBONG" in line]
    started = any("라운드 시작" in line for line in game_logs)
    turns = sum("턴 시작" in line for line in game_logs)

    print(f"\n결과: 게임 로그 {len(game_logs)}건 / 라운드 시작 {'O' if started else 'X'} / 턴 진행 {turns}회 / 페이지 에러 {len(errors)}건")
    print(f"스크린샷: {args.shots}-title/-lobby/-game.png")
    for line in game_logs[-8:]:
        print(f"  {line.strip()[:120]}")

    if errors:
        print("\n✗ 페이지 에러:")
        for err in errors[:5]:
            print(f"  {err[:200]}")
        return 1

    if not started or turns < 2:
        print("\n✗ 실패: 게임이 정상 진행되지 않음 (로그인/로비 클릭 좌표 또는 서버 연결 확인)")
        return 1

    print("\n✓ 스모크 통과")
    return 0


if __name__ == "__main__":
    sys.exit(main())
