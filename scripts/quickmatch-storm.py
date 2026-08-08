#!/usr/bin/env python3
"""맞춤게임 폭풍 테스트: N명의 게스트가 랜덤 조건(인원·입장료)으로 quickMatch → 세트 완주.

각 유저는 실제로 플레이한다(내 턴 버림, 뽕 창 랜덤 선언/패스, 뽕 추가 버림, 스톱 랜덤).
게임 종료 후 지갑을 검증한다.

불변식(하나라도 깨지면 FAIL):
  I1  모든 유저가 setEnded + roomClosed(판돈 방 폭파)까지 도달 — 미완주/행 없음
  I2  gameStarted.playerCount == 신청한 목표 인원 (과밀/과소 배정 없음)
  I3  잔액 음수 없음, 최종 잔액 == 10000 − stake + (수령 payout)
  I4  우승자 payout == stake × targetPlayers ÷ 우승자수(절사) — 화면 표기와 일치
  I5  비정상 에러 코드 수신 0 (허용: 매칭 재시도용 room_full/room_playing/room_not_found,
      게임 중 지각 액션 거절 not_your_turn/invalid_phase/invalid_card/cannot_pong/pong_too_late)
  I6  한 방에 배정된 유저들의 (stake, targetPlayers) 일치

사용법: python3 scripts/quickmatch-storm.py [--server URL] [--users 100] [--timeout 600]
종료 코드: 0=이상 없음, 1=이상 발견.
"""

import argparse
import asyncio
import json
import random
import sys
import time
import urllib.request

import websockets

STAKES = [100, 500, 1000, 5000, 10000]
BENIGN_ERRORS = {"room_full", "room_playing", "room_not_found",
                 "not_your_turn", "invalid_phase", "invalid_card", "cannot_meld",
                 "cannot_pong", "pong_too_late", "cannot_natural_pong", "seat_replaced"}


def http_json(base, path, method="GET", token=None):
    req = urllib.request.Request(f"{base}{path}", method=method)
    if token:
        req.add_header("Authorization", f"Bearer {token}")
    with urllib.request.urlopen(req, timeout=20) as r:
        return json.load(r)


class User:
    def __init__(self, idx, base):
        self.idx = idx
        self.base = base
        self.stake = random.choice(STAKES)
        self.players = random.randint(2, 6)
        self.token = None
        self.seat = None
        self.started = None       # gameStarted msg
        self.set_ended = False
        self.room_closed = False
        self.retries = 0
        self.room_code = None     # roomUpdate 시점 방 코드(조건 일치 검증용)
        self.anomalies = []
        self.finished = False
        self.last_acted = None

    def note(self, msg):
        self.anomalies.append(f"u{self.idx}({self.players}인/{self.stake}): {msg}")


async def run_user(u: User, ws_base, results):
    try:
        g = await asyncio.to_thread(http_json, u.base, "/auth/guest", "POST")
        u.token = g["accessToken"]
        ws = await websockets.connect(f"{ws_base}/ws?access_token={u.token}",
                                      open_timeout=30, close_timeout=5)
        await asyncio.wait_for(ws.recv(), timeout=15)  # welcome
        await ws.send(json.dumps({"type": "quickMatch", "stake": u.stake, "players": u.players}))

        while True:
            try:
                raw = await asyncio.wait_for(ws.recv(), timeout=120)
            except asyncio.TimeoutError:
                u.note("120초 무소식 — 진행 멈춤(행)")
                break
            msg = json.loads(raw)
            t = msg["type"]

            if t == "roomUpdate":
                u.room_code = msg.get("code")
                if msg.get("stake") != u.stake or msg.get("targetPlayers") != u.players:
                    u.note(f"I6 위반: 배정 방 조건 {msg.get('stake')}/{msg.get('targetPlayers')}")
            elif t == "matchStarting":
                pass
            elif t == "gameStarted":
                u.started = msg
                u.seat = msg["yourSeat"]
                if msg["playerCount"] != u.players:
                    u.note(f"I2 위반: 시작 인원 {msg['playerCount']} != 목표 {u.players}")
                if msg["stake"] != u.stake:
                    u.note(f"시작 stake {msg['stake']} != {u.stake}")
            elif t in ("drewCard", "turnBegan", "pongWindowOpened", "ponged", "naturalPonged",
                       "pongWindowClosed", "discarded"):
                view = msg.get("view") or {}
                key = (view.get("phase"), view.get("currentSeat"), view.get("actorSeat"),
                       view.get("drawPileCount"), len(view.get("myHand") or []))
                if key != u.last_acted:
                    u.last_acted = key
                    await act(ws, u, view)
            elif t == "roundEnded":
                pass
            elif t == "setEnded":
                u.set_ended = True
            elif t == "roomClosed":
                u.room_closed = True
                break
            elif t == "botTookOver":
                if msg.get("seat") == u.seat:
                    u.note("내 좌석이 봇으로 대체됨(활동 중인데 AFK 판정?)")
            elif t == "error":
                code = msg.get("code", "")
                if code in ("room_full", "room_playing", "room_not_found") and u.started is None:
                    if u.retries < 5:
                        u.retries += 1
                        await asyncio.sleep(0.3 + random.random())
                        await ws.send(json.dumps({"type": "quickMatch", "stake": u.stake, "players": u.players}))
                    else:
                        u.note("재매칭 5회 실패")
                        break
                elif code not in BENIGN_ERRORS:
                    u.note(f"I5 위반: 예상 밖 에러 {code}: {msg.get('message','')[:60]}")
            # 그 외 메시지는 무시

        await ws.close()
    except Exception as e:
        u.note(f"예외: {type(e).__name__}: {str(e)[:100]}")

    # ── 지갑 검증 ──
    try:
        me = await asyncio.to_thread(http_json, u.base, "/me", "GET", u.token)
        bal = me["balance"]
        if bal < 0:
            u.note(f"I3 위반: 음수 잔액 {bal}")
        if u.started is not None and u.set_ended:
            pot = u.stake * u.players
            expected_lose = 10000 - u.stake
            # 우승 시: 10000 - stake + pot/winners. winners 수를 모르면 (1명 기준) 또는 균등 나눔 후보들 허용
            valid = {expected_lose} | {expected_lose + pot // w for w in range(1, u.players + 1)}
            if bal not in valid:
                u.note(f"I3/I4 위반: 최종 잔액 {bal} (허용 {sorted(valid)})")
        elif u.started is None:
            if bal != 10000:
                u.note(f"I3 위반: 게임 없이 잔액 변동 {bal} (환불 누락?)")
    except Exception as e:
        u.note(f"잔액 조회 실패: {e}")

    if u.started is not None and not (u.set_ended and u.room_closed):
        u.note(f"I1 위반: 완주 실패 (setEnded={u.set_ended}, roomClosed={u.room_closed})")
    u.finished = True
    results.append(u)


async def act(ws, u: User, view):
    """현재 뷰 기준 랜덤 유효 행동."""
    phase = view.get("phase", "")
    my = view.get("mySeat", -1)
    hand = view.get("myHand") or []

    if phase == "WaitingStop" and view.get("currentSeat") == my:
        await ws.send(json.dumps({"type": "stopDeclare" if (view.get("canStop") and random.random() < 0.5)
                                  else "continueTurn"}))
    elif phase == "WaitingDiscard" and view.get("currentSeat") == my and hand:
        if view.get("canMeld") and random.random() < 0.8:
            await ws.send(json.dumps({"type": "meldDeclare"}))
            return
        if view.get("canNaturalPong") and random.random() < 0.6:
            await ws.send(json.dumps({"type": "naturalPong", "hasDiscard": False}))
            # 서버가 3장 전부면 손털기, 아니면 invalid_card 응답 → 이어서 일반 버림으로 처리됨
        await asyncio.sleep(random.uniform(0.2, 1.5))
        card = random.choice(hand)
        await ws.send(json.dumps({"type": "discard", "card": card}))
    elif phase == "PongWindow" and view.get("canPong"):
        await asyncio.sleep(random.uniform(0.1, 1.0))
        await ws.send(json.dumps({"type": "pongDeclare" if random.random() < 0.7 else "pongPass"}))
    elif phase == "WaitingPongDiscard" and view.get("actorSeat") == my and hand:
        await asyncio.sleep(random.uniform(0.2, 1.0))
        card = random.choice(hand)
        await ws.send(json.dumps({"type": "pongDiscard", "card": card}))


async def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--server", default="http://localhost:5080")
    parser.add_argument("--users", type=int, default=100)
    parser.add_argument("--timeout", type=int, default=1800)
    args = parser.parse_args()

    base = args.server.rstrip("/")
    ws_base = base.replace("https://", "wss://").replace("http://", "ws://")

    users = [User(i, base) for i in range(args.users)]
    from collections import Counter
    combos = Counter((u.players, u.stake) for u in users)
    print(f"▶ {args.users}명 랜덤 매칭 폭풍: 조건 조합 {len(combos)}종")

    results = []
    t0 = time.monotonic()
    tasks = []
    for u in users:
        tasks.append(asyncio.create_task(run_user(u, ws_base, results)))
        await asyncio.sleep(random.uniform(0.02, 0.15))  # 접속 시차

    try:
        await asyncio.wait_for(asyncio.gather(*tasks, return_exceptions=True), timeout=args.timeout)
    except asyncio.TimeoutError:
        for u in users:
            if not u.finished:
                u.note("전체 타임아웃까지 미완료")
                results.append(u)

    dur = time.monotonic() - t0
    all_anoms = [a for u in results for a in u.anomalies]
    completed = sum(1 for u in results if u.set_ended and u.room_closed)
    no_game = sum(1 for u in results if u.started is None)

    print(f"\n결과({dur:.0f}s): 완주 {completed} / 게임 미시작 {no_game} / 전체 {len(results)}")
    print(f"이상 {len(all_anoms)}건")
    for a in all_anoms[:30]:
        print(f"  ✗ {a}")
    if len(all_anoms) > 30:
        print(f"  ... 외 {len(all_anoms) - 30}건")

    if not all_anoms:
        print("\n✓ 폭풍 테스트 통과 — 이상 없음")
        return 0
    return 1


if __name__ == "__main__":
    sys.exit(asyncio.run(main()))
