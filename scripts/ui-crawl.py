#!/usr/bin/env python3
"""배포된 웹 빌드의 주요 화면을 순회하며 스크린샷 수집 (디자인 리뷰용).

수집: 타이틀/로그인 → 메인 로비 → 튜토리얼 초입 → 연습 설정 → 친구방 입구 →
맞춤게임 설정 → 포인트(상점) → 프로필 → 게임 테이블(연습 인게임).
출력: /tmp/bbong-ui/NN-name.png

사용법: python3 scripts/ui-crawl.py [--url ...] [--server ...]
"""

import argparse
import os
import sys

from playwright.sync_api import sync_playwright

# 메인 로비 6카드: pad 0.012, w=(1-0.084)/6 — 카드 중심 x
CARD_X = [0.012 + i * ((1 - 0.084) / 6 + 0.012) + ((1 - 0.084) / 6) / 2 for i in range(6)]
CARD_Y = 0.535

ANCHORS = {
    "guest_login": (0.500, 0.625),
    "back": (0.067, 0.940),        # 좌하단 "← 뒤로"
    "practice_start": (0.500, 0.895),
}


def click(page, box, xy):
    page.mouse.click(box["x"] + box["width"] * xy[0], box["y"] + box["height"] * xy[1])


def shot(page, name):
    page.screenshot(path=f"/tmp/bbong-ui/{name}.png")
    print(f"  saved {name}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--url", default="https://yogoloper.github.io/bbong")
    parser.add_argument("--server", default="https://bbong.fly.dev")
    args = parser.parse_args()

    os.makedirs("/tmp/bbong-ui", exist_ok=True)

    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        page = browser.new_page(viewport={"width": 1280, "height": 720})
        page.goto(f"{args.url.rstrip('/')}/?server={args.server}")
        page.wait_for_load_state("networkidle")
        page.wait_for_timeout(15_000)

        canvas = page.query_selector("#unity-canvas")
        if canvas is None:
            print("캔버스 없음")
            return 1
        box = canvas.bounding_box()

        shot(page, "01-login")
        click(page, box, ANCHORS["guest_login"])
        page.wait_for_timeout(4_000)
        shot(page, "02-lobby")

        # 튜토리얼(카드 0) 초입 두 장
        click(page, box, (CARD_X[0], CARD_Y))
        page.wait_for_timeout(3_000)
        shot(page, "03-tutorial")
        page.reload()
        page.wait_for_timeout(12_000)
        click(page, box, ANCHORS["guest_login"])
        page.wait_for_timeout(4_000)

        # 연습 설정(카드 1)
        click(page, box, (CARD_X[1], CARD_Y))
        page.wait_for_timeout(2_000)
        shot(page, "04-practice-setup")
        click(page, box, ANCHORS["back"])
        page.wait_for_timeout(1_500)

        # 맞춤게임 설정(카드 2)
        click(page, box, (CARD_X[2], CARD_Y))
        page.wait_for_timeout(2_000)
        shot(page, "05-match-setup")
        click(page, box, ANCHORS["back"])
        page.wait_for_timeout(1_500)

        # 친구방 입구(카드 3)
        click(page, box, (CARD_X[3], CARD_Y))
        page.wait_for_timeout(2_000)
        shot(page, "06-friend-entry")
        click(page, box, ANCHORS["back"])
        page.wait_for_timeout(1_500)

        # 포인트/상점(카드 4)
        click(page, box, (CARD_X[4], CARD_Y))
        page.wait_for_timeout(2_000)
        shot(page, "07-shop")
        click(page, box, ANCHORS["back"])
        page.wait_for_timeout(1_500)

        # 프로필(카드 5)
        click(page, box, (CARD_X[5], CARD_Y))
        page.wait_for_timeout(2_000)
        shot(page, "08-profile")
        click(page, box, ANCHORS["back"])
        page.wait_for_timeout(1_500)

        # 연습 인게임(테이블)
        click(page, box, (CARD_X[1], CARD_Y))
        page.wait_for_timeout(2_000)
        click(page, box, ANCHORS["practice_start"])
        page.wait_for_timeout(6_000)
        shot(page, "09-game-table")
        page.wait_for_timeout(8_000)
        shot(page, "10-game-table-later")

        browser.close()

    print("완료: /tmp/bbong-ui/")
    return 0


if __name__ == "__main__":
    sys.exit(main())
