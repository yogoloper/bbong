# BBONG 시스템 아키텍처 맵 (v0.1)

> 클라이언트(Unity) + 서버 백엔드 전체 그림. 회원/프로필/대기실/방찾기/친구/상점·광고 포함.
> 게임 규칙은 `rules.md`(SSOT)를 따름. 이 문서는 **시스템 구성 SSOT**.
> MVP는 §6 단계표 참고(싱글 AI + 광고 먼저, 온라인 멀티는 후순위).

---

## 1. 3-레이어 큰 그림

```
┌─────────────────────────────────────────────────────────────┐
│                      CLIENT (Unity, C#)                       │
│  UI/연출 · 입력 · 로컬 AI(싱글) · 네트워크 클라이언트          │
└───────────────┬──────────────────────────┬───────────────────┘
                │ 참조(소스/DLL)            │ HTTPS / WebSocket
                ▼                          ▼
┌──────────────────────────┐   ┌──────────────────────────────┐
│   CORE ENGINE (순수 C#)   │   │      BACKEND (ASP.NET Core)   │
│  UnityEngine 비의존        │   │  REST(상태없는 도메인 API)     │
│  · 카드/덱                 │◄──┤  + Realtime(SignalR/WebSocket) │
│  · 턴 상태머신             │참조│  + 게임 서버(권위, 코어 재사용) │
│  · 족보/점수/뽕 판정       │   │                               │
│  · GameConfig             │   └───────────┬───────────────────┘
└──────────────────────────┘               │
   (클라·서버 공유 = 단일 진실)              ▼
                              ┌──────────────────────────────┐
                              │   DATA: PostgreSQL · Redis    │
                              │   외부: IAP검증 · 광고 SDK     │
                              └──────────────────────────────┘
```

**핵심 원칙**: 코어 엔진은 클라와 서버가 **동일 코드 공유**. 멀티플레이에서 서버가 클라 입력을 코어 엔진으로 재검증 → 치트 불가(서버 권위).

---

## 2. 코어 엔진 (순수 C# 라이브러리)

`rules.md` 로직 전부. UnityEngine·DB·네트워크 의존 0. `dotnet test`로 TDD.

```
BbongCore/
  Cards/      Card, CardColor, Deck
  Game/       GameState, RoundState, Player, Hand, TurnStateMachine
  Rules/      HandEvaluator(족보), Scoring(빚), PongResolver, StopResolver
  Config/     GameConfig (stopLimit, pongTimerSec, setRounds, ...)
  Actions/    GameAction(Draw/Discard/Pong/NaturalPong/Stop/DeclareMeld)
              + Reducer: (state, action) -> newState (결정적/순수)
```

- **결정적 reducer**: `(GameState, GameAction) → GameState`. 같은 입력 = 같은 결과 → 서버 권위 검증·리플레이·테스트 용이.
- 셔플만 시드 주입(`IRandom`) → 테스트 재현 가능, 서버는 보안 시드.

---

## 3. 백엔드 도메인 (마이크로 아닌 모듈러 모놀리식 권장 — 인디 규모)

| 도메인 | 책임 | 저장 | 비고 |
|---|---|---|---|
| **Auth** 회원 | 가입/로그인, 소셜(Apple/Google/Kakao), 토큰(JWT) | PG: `users` | 게스트→소셜 연동 승격 |
| **Profile** 프로필 | 닉네임, 아바타, 레벨, 전적(승/패/세트) | PG: `profiles`, `stats` | |
| **Wallet** 재화 | 포인트 잔액·거래원장. **환전 불가** | PG: `wallets`, `ledger` | 모든 변동 원장 기록(감사) |
| **Shop** 상점 | IAP 구매, **광고 시청 보상** 포인트 | PG: `purchases`, `ad_rewards` | 영수증 서버 검증 필수 |
| **Friends** 친구 | 추가/삭제/요청, 온라인 상태 | PG: `friends`, Redis presence | |
| **Lobby/Match** 대기실·방찾기 | 방 생성/목록/입장, 빠른매칭 | Redis: room registry | 방= 인원2~6·판돈택1 |
| **GameSession** 게임서버 | 권위 게임 진행, 코어 엔진 구동, 판돈 정산 | Redis: live state, PG: 결과 | Realtime 채널 |
| **Ranking**(선택) | 시즌 랭킹/리더보드 | PG/Redis | 후순위 |

> 규모 작으니 **단일 ASP.NET Core 앱 + 모듈 분리**로 시작. 트래픽 늘면 GameSession만 분리.

---

## 4. 주요 흐름

### 4-1. 회원 → 프로필 → 메인
```
앱 실행 → 토큰 확인
  없음 → 게스트 자동생성 or 소셜 로그인 → users/profiles/wallet 생성
  있음 → /me 로 프로필+잔액 로드 → 메인 로비
```

### 4-2. 방 찾기 / 대기실 / 게임 (온라인 멀티)
```
메인 → [방 찾기] 목록 조회(인원·판돈 필터)  ┐
     → [방 만들기] 인원2~6 + 판돈 선택       ├→ 대기실(Room)
     → [빠른 매칭] 조건 맞는 방 자동 입장      ┘
대기실: 입장 시 판돈 차감(에스크로). 정원 차면 시작.
게임시작 → GameSession 채널 연결
  클라: 의도 전송(Draw/Discard/Pong/Stop...)
  서버: 코어 엔진으로 검증 → 새 상태 브로드캐스트
  뽕 인터럽트: 버림 이벤트 후 서버가 2초 창 오픈, 선언 수집
세트(5판) 종료 → 빚 집계 → 1등에게 판돈 몰아주기(공동1등 균등분배)
  이탈자: AI 대체 + 패배 + 판돈 몰수
```

### 4-3. 상점 / 포인트 획득
```
IAP 구매:  클라 결제 → 스토어 영수증 → 서버 검증(Apple/Google) → Wallet 적립(원장)
광고 보상: 광고 시청 완료 → SSV(서버측 검증 콜백) → Wallet 적립(원장)
  ※ 환전 출구 없음(컴플라이언스). 적립만.
```

### 4-4. 싱글 플레이 (MVP, 서버 불필요)
```
클라 내부에서 코어 엔진 직접 구동 + 로컬 AI(Phase 2).
판돈은 로컬 지갑 또는 서버 지갑(로그인 시) 사용. 온라인 미연결.
```

---

## 5. 보안 · 컴플라이언스 체크

- **서버 권위**: 게임 결과·판돈 정산은 서버만 확정. 클라 신뢰 안 함.
- **환전 불가**: Wallet에 현금 출금 경로 절대 미구현(전체이용가 전제, `rules.md` §9).
- **IAP/광고 보상 = 서버 검증 후 적립**. 클라 직접 적립 금지(위조 방지).
- **원장(ledger)**: 모든 재화 변동 append-only 기록 → 분쟁·감사 대응.
- 시크릿(스토어 키, DB 비번)은 환경변수/시크릿 매니저. 코드·로그 하드코딩 금지.

---

## 6. 단계별 매핑 (로드맵 → 아키텍처)

| Phase | 범위 | 서버 필요 | 이 문서 해당 |
|---|---|---|---|
| 1 코어 엔진 | 카드/턴/족보/점수/뽕 (TDD) | ❌ | §2 |
| 2 AI 봇 | 휴리스틱 난이도 3단 (클라 내) | ❌ | §4-4 |
| 3 UI/UX | Unity 렌더·연출·뽕 인터럽트 | ❌ | §1 client |
| 4 메타/수익화 | 회원·프로필·지갑·상점·광고 | ✅ (도메인 API) | §3 Auth/Profile/Wallet/Shop |
| 5 온라인 멀티 | 대기실·방찾기·친구·게임서버 | ✅ (Realtime) | §3 Lobby/GameSession/Friends, §4-2 |
| 6 출시 | 스토어·심사·테스트 | ✅ | §5 |

> MVP = Phase 1~4(싱글 AI + 광고). 온라인 멀티(§4-2)는 Phase 5.

---

## 7. 기술 스택 (제안 — 확정 전)

| 영역 | 후보 | 근거 |
|---|---|---|
| 클라 | Unity (C#) | 결정됨 |
| 코어 | 순수 C# (.NET) | 결정됨, 클라·서버 공유 |
| 백엔드 | **ASP.NET Core** | 코어 DLL 그대로 재사용(치트방지·중복제거) |
| Realtime | SignalR or 순수 WebSocket | 턴제라 저빈도. SignalR 간편 |
| DB | PostgreSQL | 관계형(유저·지갑·원장) |
| 캐시/실시간 상태 | Redis | 방 레지스트리·presence·라이브 게임 |
| IAP 검증 | Apple/Google 서버 API | 영수증 위조 방지 |
| 광고 | AdMob (보상형 + SSV) | 서버측 검증 콜백 |
| 인증 | JWT + 소셜 OAuth | 게스트 승격 |

❓OPEN:
- 백엔드 호스팅(자체 VM / 매니지드 / 서버리스)
- Realtime 라이브러리 SignalR vs Unity 전용 솔루션(Mirror/Photon) 비교
- 소셜 로그인 제공자 범위(Apple 필수, Google, Kakao?)
- 리전/레이턴시(한국 우선)
