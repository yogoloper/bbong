#!/usr/bin/env python3
"""연습 모드 '드로우 없는 버림' 버그 재현/검증 (Playwright).

시나리오: 연습 게임에 들어가 상대(봇) 턴 동안 내 손패 위치를 계속 난타한다.
버그가 있으면 뽕 대기/턴 전환 중 낡은 '버릴 카드를 클릭하세요' 상태가 남아
드로우 없이 카드가 버려진다("내 버림"이 "P0 드로우" 없이 발생, 손패 5→4).

판정(콘솔 [BBONG] 로그):
  - "P0 턴 시작" 이후 "P0 드로우" 없이 "내 버림"이 찍히면 → 버그 재현(FAIL)
  - "P0 드로우 ... 손패 N" 에서 N != 6 (뽕 상태 3장 제외) → 손패 개수 붕괴(FAIL)
  - 재셔플 로그와 직후 드로우 로그 간격 < 0.8초 → 셔플 연출 중 진행(WARN)

사용법: python3 scripts/bug-hunt-stale-discard.py [--url ...] [--server ...] [--seconds 90]
종료 코드: 0=버그 미재현(통과), 1=버그 재현/에러.
"""

import argparse
import re
import sys

from playwright.sync_api import sync_playwright

ANCHORS = {
    "guest_login": (0.500, 0.548),
    "lobby_practice": (0.253, 0.550),
    "practice_start": (0.500, 0.895),
}

# 내 손패 줄(하단 중앙) — 카드가 있을 법한 x 지점들을 순회 난타
HAND_CLICK_XS = [0.38, 0.44, 0.50, 0.56, 0.62]
HAND_CLICK_Y = 0.87  # 마우스 좌표는 좌상단 원점 — 내 손패는 화면 하단

# 우하단 버튼 기둥(뽕/자연뽕/패스 등) — 가끔 눌러 뽕 경로(3장 손패)도 태운다
BUTTON_CLICKS = [(0.90, 0.82), (0.90, 0.88), (0.90, 0.93)]

UNITY_LOAD_WAIT_MS = 15_000


def canvas_click(page, box, anchor):
    page.mouse.click(box["x"] + box["width"] * anchor[0], box["y"] + box["height"] * anchor[1])


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--url", default="http://localhost:8801")
    parser.add_argument("--server", default="http://localhost:5080")
    parser.add_argument("--seconds", type=int, default=90, help="난타하며 관찰할 시간")
    args = parser.parse_args()

    logs: list[tuple[float, str]] = []
    errors: list[str] = []

    with sync_playwright() as p:
        import time

        browser = p.chromium.launch(headless=True)
        page = browser.new_page(viewport={"width": 1280, "height": 720})
        page.on("console", lambda m: logs.append((time.monotonic(), m.text)))
        page.on("pageerror", lambda e: errors.append(str(e)))

        page.goto(f"{args.url.rstrip('/')}/?server={args.server}")
        page.wait_for_load_state("networkidle")
        page.wait_for_timeout(UNITY_LOAD_WAIT_MS)

        canvas = page.query_selector("#unity-canvas")
        if canvas is None:
            print("✗ 캔버스 없음 — 로드 실패")
            return 1

        box = canvas.bounding_box()
        canvas_click(page, box, ANCHORS["guest_login"])
        page.wait_for_timeout(4_000)
        canvas_click(page, box, ANCHORS["lobby_practice"])
        page.wait_for_timeout(2_500)
        canvas_click(page, box, ANCHORS["practice_start"])
        page.wait_for_timeout(2_000)

        print(f"▶ {args.seconds}초 동안 손패 좌표 난타(봇 턴 포함 무차별 클릭)")
        end = time.monotonic() + args.seconds
        i = 0
        while time.monotonic() < end:
            if i % 4 == 3:
                canvas_click(page, box, BUTTON_CLICKS[(i // 4) % len(BUTTON_CLICKS)])
            else:
                canvas_click(page, box, (HAND_CLICK_XS[i % len(HAND_CLICK_XS)], HAND_CLICK_Y))
            i += 1
            page.wait_for_timeout(180)

        page.screenshot(path="/tmp/bbong-bughunt.png")
        browser.close()

    game = [(t, line) for t, line in logs if "BBONG" in line]

    # ── 판정 1: 드로우 없는 내 버림 ──
    stale_discards = []
    drew_since_turn = False
    for t, line in game:
        if "P0 턴 시작" in line:
            drew_since_turn = False
        elif re.search(r"P0 드로우", line):
            drew_since_turn = True
        elif "내 버림" in line:
            if not drew_since_turn:
                stale_discards.append(line.strip()[:110])
            drew_since_turn = False # 정상 버림 후 다시 클릭돼도 잡히게

    # ── 판정 2: 내 드로우 후 손패 수 이상(정상 6, 뽕 뒤 3) ──
    bad_counts = [
        line.strip()[:110]
        for _, line in game
        if (m := re.search(r"P0 드로우 .*손패 (\d+)", line)) and int(m.group(1)) not in (3, 6)
    ]

    # ── 판정 3: 재셔플 → 드로우 간격 ──
    rush = []
    shuffle_at = None
    for t, line in game:
        if "재셔플" in line:
            shuffle_at = t
        elif "드로우" in line and shuffle_at is not None:
            if t - shuffle_at < 0.8:
                rush.append(f"{t - shuffle_at:.2f}s 만에 드로우: {line.strip()[:90]}")
            shuffle_at = None

    turns = sum("턴 시작" in line for _, line in game)
    my_pongs = sum("뽕 완료" in line or "뽕! " in line and "P0" not in line for _, line in game)
    three_draws = sum(bool(re.search(r"P0 드로우 .*손패 3", line)) for _, line in game)
    print(f"\n결과: 게임 로그 {len(game)}건 / 턴 {turns}회 / 페이지 에러 {len(errors)}건")
    print(f"  경로 커버리지: 내 뽕 {my_pongs}회 / 3장 드로우 {three_draws}회")
    print(f"  드로우 없는 내 버림: {len(stale_discards)}건")
    for line in stale_discards[:5]:
        print(f"    ✗ {line}")
    print(f"  손패 수 이상 드로우: {len(bad_counts)}건")
    for line in bad_counts[:5]:
        print(f"    ✗ {line}")
    print(f"  셔플 연출 중 드로우(0.8초 미만): {len(rush)}건")
    for line in rush[:5]:
        print(f"    ⚠ {line}")

    if errors:
        for err in errors[:3]:
            print(f"  페이지 에러: {err[:150]}")

    if stale_discards or bad_counts or errors:
        print("\n✗ 버그 재현됨")
        return 1

    if turns < 3:
        print("\n✗ 게임 진행 부족(좌표/로드 확인)")
        return 1

    print("\n✓ 버그 미재현 (셔플 러시는 경고만)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
