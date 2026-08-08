#!/usr/bin/env python3
"""맞춤게임(판돈 방) UI 플로우 검증 (Playwright).

로비 → 맞춤게임 → 설정(기본 4인/1000) → 시작하기 → 매칭 대기(점 애니메이션) →
위장 봇 충원(10초+랜덤) → 5초 카운트다운 → 자동 시작까지 클라 플로우 검증.
판정: 게임 시작 로그 + 페이지 에러 0. 스크린샷 /tmp/bbong-stake-*.png.
"""

import argparse
import sys
import time

from playwright.sync_api import sync_playwright

ANCHORS = {
    "guest_login": (0.500, 0.548),
    "lobby_match": (0.418, 0.550),    # 3번째 카드 "맞춤게임"
    "match_cta": (0.500, 0.895),      # "방 만들기 / 입장"
    "friend_create": (0.500, 0.490),  # "방 만들기 (호스트)"
    "room_add_bot": (0.390, 0.770),
    "room_start": (0.500, 0.895),
}


def canvas_click(page, box, anchor):
    page.mouse.click(box["x"] + box["width"] * anchor[0], box["y"] + box["height"] * anchor[1])


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--url", default="http://localhost:8802")
    parser.add_argument("--server", default="http://localhost:5080")
    args = parser.parse_args()

    logs: list[str] = []
    errors: list[str] = []

    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        page = browser.new_page(viewport={"width": 1280, "height": 720})
        page.on("console", lambda m: logs.append(m.text))
        page.on("pageerror", lambda e: errors.append(str(e)))

        page.goto(f"{args.url.rstrip('/')}/?server={args.server}")
        page.wait_for_load_state("networkidle")
        page.wait_for_timeout(15_000)

        canvas = page.query_selector("#unity-canvas")
        if canvas is None:
            print("✗ 캔버스 없음")
            return 1

        box = canvas.bounding_box()
        canvas_click(page, box, ANCHORS["guest_login"])
        page.wait_for_timeout(4_000)
        canvas_click(page, box, ANCHORS["lobby_match"])
        page.wait_for_timeout(2_000)
        page.screenshot(path="/tmp/bbong-stake-setup.png")
        canvas_click(page, box, ANCHORS["match_cta"])  # "시작하기" — 빠른매칭 진입
        page.wait_for_timeout(3_000)
        page.screenshot(path="/tmp/bbong-stake-waiting.png")
        # 위장 봇 충원(10초 + 봇당 1~10초 랜덤 × 3) + 카운트다운 5초 → 최대 55초 대기
        page.wait_for_timeout(20_000)
        page.screenshot(path="/tmp/bbong-stake-filling.png")
        page.wait_for_timeout(30_000)
        page.screenshot(path="/tmp/bbong-stake-game.png")
        browser.close()

    game_logs = [line for line in logs if "BBONG" in line]
    started = any("게임 테이블 진입" in line or "라운드 시작" in line or "턴 시작" in line for line in game_logs)

    print(f"결과: 게임 로그 {len(game_logs)}건 / 게임 진입 {'O' if started else 'X'} / 에러 {len(errors)}건")
    for line in game_logs[-5:]:
        print(f"  {line.strip()[:110]}")

    if errors:
        for err in errors[:3]:
            print(f"  ✗ {err[:150]}")
        return 1

    if not started:
        print("✗ 판돈 방 게임 진입 실패 — 스크린샷 확인")
        return 1

    print("✓ 맞춤게임 플로우 통과")
    return 0


if __name__ == "__main__":
    sys.exit(main())
