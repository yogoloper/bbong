#!/usr/bin/env python3
"""친구방 재셔플 시퀀스 검증 (Playwright).

친구방을 만들어 봇 5명을 채우고 게임을 돌리며, 재셔플이 발생하면
"재셔플 — 수렴 연출 대기" → (0.9초) → "재셔플 반영 — 드로우" 순서와 간격을 로그로 판정한다.
수정 전에는 뷰(손패)가 즉시 반영돼 이 페어 자체가 없다(반영 로그가 대기와 동시).

사용법: python3 scripts/net-reshuffle-check.py [--url ...] [--server ...] [--seconds 420]
종료 코드: 0=재셔플 관측 + 순서·간격 정상, 1=위반, 2=관측 실패(재셔플 미발생).
"""

import argparse
import sys
import time

from playwright.sync_api import sync_playwright

ANCHORS = {
    "guest_login": (0.500, 0.625),
    "lobby_friend": (0.582, 0.535),   # 메인 로비 4번째 카드 "친구와 함께"
    "friend_create": (0.500, 0.440),  # "방 만들기 (호스트)"
    "room_add_bot": (0.390, 0.770),   # 대기실 "봇 추가"
    "room_start": (0.500, 0.895),     # "게임 시작" CTA
}

UNITY_LOAD_WAIT_MS = 15_000
MIN_GAP_S = 0.8


def canvas_click(page, box, anchor):
    page.mouse.click(box["x"] + box["width"] * anchor[0], box["y"] + box["height"] * anchor[1])


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--url", default="http://localhost:8802")
    parser.add_argument("--server", default="http://localhost:5080")
    parser.add_argument("--seconds", type=int, default=420)
    args = parser.parse_args()

    logs: list[tuple[float, str]] = []
    errors: list[str] = []

    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        page = browser.new_page(viewport={"width": 1280, "height": 720})
        page.on("console", lambda m: logs.append((time.monotonic(), m.text)))
        page.on("pageerror", lambda e: errors.append(str(e)))

        page.goto(f"{args.url.rstrip('/')}/?server={args.server}")
        page.wait_for_load_state("networkidle")
        page.wait_for_timeout(UNITY_LOAD_WAIT_MS)

        canvas = page.query_selector("#unity-canvas")
        if canvas is None:
            print("✗ 캔버스 없음")
            return 2

        box = canvas.bounding_box()
        canvas_click(page, box, ANCHORS["guest_login"])
        page.wait_for_timeout(4_000)
        canvas_click(page, box, ANCHORS["lobby_friend"])
        page.wait_for_timeout(2_000)
        canvas_click(page, box, ANCHORS["friend_create"])
        page.wait_for_timeout(2_500)
        for _ in range(5):
            canvas_click(page, box, ANCHORS["room_add_bot"])
            page.wait_for_timeout(400)
        canvas_click(page, box, ANCHORS["room_start"])
        page.wait_for_timeout(2_000)

        started = any("gameStarted" in line or "라운드" in line or "BBONG-NET" in line for _, line in logs)
        print(f"▶ 친구방 6인(나+봇5) 시작됨={started} — 최대 {args.seconds}초 재셔플 대기")

        end = time.monotonic() + args.seconds
        while time.monotonic() < end:
            page.wait_for_timeout(2_000)
            if any("재셔플 반영" in line for _, line in logs):
                page.wait_for_timeout(3_000)  # 후속 로그 여유
                break

        page.screenshot(path="/tmp/bbong-netreshuffle.png")
        browser.close()

    waits = [(t, line) for t, line in logs if "재셔플 — 수렴 연출 대기" in line]
    applies = [(t, line) for t, line in logs if "재셔플 반영" in line]
    net_logs = sum("BBONG" in line for _, line in logs)

    print(f"\n결과: 게임 로그 {net_logs}건 / 재셔플 대기 {len(waits)}회 / 반영 {len(applies)}회 / 페이지 에러 {len(errors)}건")

    if errors:
        for err in errors[:3]:
            print(f"  ✗ 에러: {err[:150]}")
        return 1

    if not applies:
        print("△ 재셔플이 발생하지 않아 관측 실패 — 시간을 늘려 재시도 필요")
        return 2

    ok = True
    for wt, _ in waits:
        matched = [at for at, _ in applies if at > wt]
        if not matched:
            print(f"  ✗ 대기 후 반영 로그 없음 (t={wt:.1f})")
            ok = False
            continue
        gap = min(matched) - wt
        status = "✓" if gap >= MIN_GAP_S else "✗"
        print(f"  {status} 수렴 대기 → 반영 간격 {gap:.2f}s (기준 ≥{MIN_GAP_S}s)")
        if gap < MIN_GAP_S:
            ok = False

    if ok:
        print("\n✓ 재셔플 시퀀스 정상: 연출이 끝난 뒤에 손패 반영")
        return 0

    print("\n✗ 시퀀스 위반")
    return 1


if __name__ == "__main__":
    sys.exit(main())
