# BBONG 메타·온라인 설계 (Phase 4~5)

> 회원/로비/온라인 멀티/상점/프로필 + 웹 빌드 설계. 클라(Unity)·서버(ASP.NET Core) 분담.
> 시스템 SSOT = `architecture.md`. 이 문서는 그 §3·§4를 사용자 요구(5개 로비 모드)에 맞춰 구체화.
> 규칙 SSOT = `rules.md`, 리스크 = `considerations.md`. **설계 단계 — 코드 없음.**

---

## 0. 한눈에 — 로비 5개 모드 vs 서버 의존

| 모드 | 서버 필요 | 포인트 | 상대 | Phase |
|---|---|---|---|---|
| **연습** | ❌(로컬 코어+AI) | 안 검 | 봇(수·레벨 선택) | 3.5 (지금 거의 됨) |
| **맞춤게임** | ✅ 매칭+게임서버 | 건다(에스크로) | 실유저(인원 선택) | 5 |
| **친구와 함께** | ✅ 방+게임서버 | **안 검**(담합 회피 R2) | 실유저(초대코드) | 5 |
| **상점** | ✅ 도메인 API | 적립 | — | 4 |
| **프로필** | ✅ 도메인 API | — | — | 4 |
| 회원가입/로그인 | ✅ Auth | — | — | 4 |

핵심: **연습은 지금도 동작**(서버 0). 나머지는 서버 필요. 그래서 구현 순서 = Auth/Wallet/Profile(4) → Shop(4) → 온라인 멀티(5). 연습은 로그인 붙여 지갑만 서버 연동.

---

## 1. 웹 빌드 가능한가 — 결론: 가능, 단 수익화는 플랫폼 분기

Unity **WebGL 빌드**로 브라우저 배포 가능. 코어 게임플레이·온라인 멀티(WebSocket)는 웹/모바일 공유. 다만 플랫폼 제약이 명확함:

| 기능 | 모바일(iOS/Android) | 웹(WebGL) | 대응 |
|---|---|---|---|
| 게임플레이·렌더 | ✅ | ✅ | 동일 코드 |
| 온라인 Realtime | ✅ WebSocket | ✅ WebSocket | **Native WebSocket** 권장(SignalR .NET 클라는 WebGL 스레딩 제약) |
| REST API | ✅ | ✅ UnityWebRequest | CORS 설정 필요 |
| **IAP 결제** | ✅ 스토어 IAP | ❌ 스토어 IAP 불가 | 웹은 **웹 PG**(토스페이먼츠/아임포트) 별도 |
| **보상형 광고** | ✅ AdMob+SSV | ⚠️ AdMob WebGL 미지원 | 웹은 광고 보상 보류 or 웹 광고망 |
| 로컬 저장 | PlayerPrefs | IndexedDB(자동) | 서버 지갑이라 영향 적음 |

**설계 원칙 — 클라에 플랫폼 추상화 레이어**:
```
IPurchaseProvider  → StoreIAP(모바일) / WebPgPurchase(웹) / NoopPurchase(연습빌드)
IAdProvider        → AdMobAds(모바일) / WebAds or NoAds(웹)
IPlatform          → 런타임 Application.platform로 분기
```
서버 API(영수증 검증·적립)는 **provider만 다르고 적립 경로는 동일**. 즉 웹/모바일이 같은 서버·같은 지갑 공유, 결제 입구만 다름.

**웹 배포 권장 단계**: 1차 = 연습+친구와함께+로그인/프로필(결제 없는 기능)을 웹으로 먼저 → 접근성·바이럴(초대코드 링크 공유) 확보. 결제/광고는 모바일 우선, 웹은 후속.

호스팅: WebGL 빌드는 **정적 파일**(Netlify/Vercel/S3+CloudFront/Cloudflare Pages). 서버 API와는 CORS로 통신. 빌드 용량(수~수십 MB) → Brotli 압축·점진 로딩.

---

## 2. 클라이언트 ↔ 서버 분담 원칙

```
클라(Unity): UI/UX·입력·연출·로컬AI(연습). 서버 상태를 "표시"만, 정산·검증은 안 함.
서버(ASP.NET): 권위(authority). 게임 진행·정산·재화·검증 전부. 코어 DLL 재사용.
코어(C#): 클라·서버 공유. 규칙 단일 진실.
```

- **연습 모드**: 클라가 코어+로컬AI 직접 구동(현재 `GameTableBootstrap`). 서버는 결과만(선택) 기록.
- **온라인 모드**: 클라는 의도(intent)만 전송, 서버가 코어로 검증→상태 브로드캐스트. 클라 코어는 **예측/표시용**(서버가 진실).
- 한 줄 요약: **연습=클라 권위, 온라인=서버 권위.** 같은 코어라 화면 코드 공유.

---

## 3. 화면(씬) 구조 — 클라

현재 단일 `GameTableBootstrap` + `LobbyBootstrap`을 다음으로 확장:

```
[Boot]        토큰 확인 → 자동 로그인 시도 → Auth or MainLobby
[Auth]        회원가입/로그인(소셜+게스트)
[MainLobby]   5개 모드 진입 허브 + 상단 프로필/잔액 바
  ├ [PracticeSetup]  봇 수·레벨 → GameTable(로컬)
  ├ [MatchSetup]     포인트·인원 → 매칭 대기 → GameTable(온라인)
  ├ [FriendRoom]     방 생성/초대코드 입장 → 대기실 → GameTable(온라인)
  ├ [Shop]           광고 보상·IAP 패키지
  └ [Profile]        아바타·닉네임·통계·기록
[GameTable]   로컬/온라인 공용(NetworkBinding 유무로 분기)
```

설계 포인트: `GameTable`이 **데이터 소스를 추상화**(`IGameSession`) — 로컬은 `LocalSession`(코어 직접), 온라인은 `RemoteSession`(서버 이벤트). UI 렌더링 코드는 그대로 재사용.

---

## 4. 서버 도메인 API (REST + Realtime)

ASP.NET Core 모듈러 모놀리식. 인증=JWT(Bearer).

### 4-1. Auth
```
POST /auth/guest              게스트 생성 → {accessToken, refreshToken}
POST /auth/social             소셜 로그인(Apple/Google/Kakao idToken) → 토큰
POST /auth/link               게스트 → 소셜 승격(기존 데이터 유지)
POST /auth/refresh            토큰 갱신
```

### 4-2. Profile / Wallet
```
GET  /me                      프로필+잔액+통계 한 번에(로그인 직후)
PATCH /me/nickname            닉네임 변경(GameConfig.IsValidNickname 서버 재검증)
PATCH /me/avatar              아바타 파츠 변경
GET  /me/history?cursor=      게임 기록(페이지네이션)
GET  /me/stats                승/패/게임수/최고기록
```
- 닉네임 규칙은 **코어 `GameConfig.MaxNicknameLength`**를 서버도 참조(단일 진실).
- 지갑 변동은 전부 `ledger`(append-only) — 감사·분쟁 대응(architecture §5).

### 4-3. Shop
```
GET  /shop/packages           IAP 패키지 목록
POST /shop/purchase/verify    {platform, receipt} 서버 영수증 검증 → 적립
POST /shop/ad-reward          광고 SSV 콜백(서버↔광고망) → 적립
```
- **클라는 절대 직접 적립 안 함.** 영수증/SSV 서버 검증 후에만 ledger 기록.
- 웹 PG는 `purchase/verify`에 `platform=web` + PG 결제키 → 서버가 PG사 검증 API 호출.

### 4-4. Lobby / Match (온라인)
```
POST /match/quick             {stake, playerCount} → 매칭 큐 진입(맞춤게임)
DELETE /match/quick           매칭 취소
POST /rooms                   친구방 생성 → {roomId, inviteCode}  (stake=0 고정)
POST /rooms/join              {inviteCode} → 입장
GET  /rooms/{id}              대기실 상태(인원/준비)
POST /rooms/{id}/start        호스트만 시작
```
- 매칭/방 레지스트리 = Redis. 정원 차면 GameSession 생성.
- **맞춤게임 입장 시 포인트 에스크로 차감**, 친구방은 차감 없음(R2 담합 회피).

#### 맞춤게임 = 매칭 큐 (자동 시작)
유저에겐 "방 찾기/만들기"가 안 보이고 **"매칭 중..."** 화면만. 서버가 큐로 직렬 처리.
```
[맞춤게임] 인원·판돈 선택 → POST /match/quick
서버: (인원,판돈) 조건별 큐에 등록 (Redis, 원자 연산)
  같은 조건 N명 모임 → 매칭 성사:
    1. N명 전원 에스크로 차감(입장료) — 한 명이라도 실패 시 롤백·큐 복귀
    2. GameSession 생성 + 자동 시작(WebSocket 연결 유도)
대기 타임아웃(예 30s): "더 적은 인원으로 시작" 제안 or 취소
실유저 전용(봇 없음, R2). 시작 직전 이탈 → 에스크로 환불 + 잔여 인원 큐 복귀
```
**동시성 주의**: "방 조회→없음→생성"을 그대로 구현하면 두 유저가 동시에 "없음" 판정 → 방 2개 생성 race. 반드시 **Redis 원자 연산(매칭 큐 pop / Lua)**으로 서버가 직렬화(서버 권위).

#### 친구와 함께 = 명시적 방 (수동 시작)
```
[친구와 함께] 방 생성 → {roomId, inviteCode}  (stake=0)
게스트: 초대코드 입력 → POST /rooms/join → 대기실
호스트: 친구 다 모이면 POST /rooms/{id}/start (수동)
포인트 X(R2 담합 회피). 초대코드는 링크 공유 가능(웹 빌드 바이럴)
```

**핵심 대비**: 자동 시작 = 맞춤게임(큐), 수동 시작 = 친구방(호스트). 친구방은 방이 1급 개념, 맞춤게임은 방을 숨기고 큐만 노출.

### 4-5. GameSession (Realtime, WebSocket)
```
연결: wss://.../game/{sessionId}   (JWT 쿼리 or 헤더)
클라→서버 (intent):  Draw / Discard / Pong / NaturalPong / Stop / DeclareMeld / Pass
서버→클라 (event):   StateSync / YourTurn / PongWindowOpen(2s) / RoundEnd / GameEnd / PlayerLeft
```
- 서버가 코어 reducer로 intent 검증→새 상태 브로드캐스트. 치트 불가.
- 뽕 인터럽트: 버림 이벤트 후 서버가 **2초 창**(rules §4-1) 오픈, 선언 수집, 서버 수신 시각 기준 판정(R6 레이턴시 보정).
- 이탈: AI 대체 + 패배 + (맞춤게임)판돈 몰수.

---

## 5. DB 스키마 (PostgreSQL, 핵심만)

```
users        (id, auth_provider, social_id, created_at, is_guest)
profiles     (user_id, nickname, avatar_json, level, exp)
wallets      (user_id, balance)                         -- 현재 잔액(캐시)
ledger       (id, user_id, delta, reason, ref_id, created_at)  -- append-only 진실
stats        (user_id, games, wins, losses, best_score)
purchases    (id, user_id, platform, product_id, receipt_hash, status, created_at)
ad_rewards   (id, user_id, ssv_id, amount, created_at)
games        (id, mode, stake, player_count, started_at, ended_at)
game_results (game_id, user_id, seat, final_debt, rank, payout)
friends      (user_id, friend_id, status)
```
- **잔액 = ledger 합산이 진실**, `wallets.balance`는 성능용 캐시(주기 검증).
- 라이브 게임 상태·방·presence = Redis(휘발성), 결과만 PG 영속.

---

## 6. 컴플라이언스 반영 (considerations.md 연계)

- **R1 웹보드 분류**: 맞춤게임 stake·일일 손실에 **한도 훅** 미리 설계(서버 정책 테이블). 출시 전 법무 검토.
- **R2 담합**: 친구방 **포인트 X**(사용자 요구와 일치 = 좋은 설계). 맞춤게임은 랜덤 매칭만 stake.
- **R3 경제**: 일일 무료 지급·파산 보너스(잔액 0 구제) 필수 — Shop/Wallet에 faucet 포함.
- **R4 개인정보**: 소셜 로그인 동의(PIPA), 미성년 결제 정책.
- **환전 출구 절대 없음**: 적립만. 출금 API 미구현(전체이용가 전제).

---

## 7. 구현 순서 (권장)

```
P4-a 서버 골격     ASP.NET Core + PG + Auth(게스트 먼저) + /me + Wallet + ledger
P4-b 클라 연동     Boot/Auth/MainLobby 씬, 로그인→로비, 잔액 표시. 연습 모드를 서버 지갑에 연결
P4-c Profile       닉네임/아바타/통계/기록
P4-d Shop          광고 보상(SSV) + faucet(일일·파산 구제). IAP/PG는 법무 검토 후 후속
P5-a 친구와 함께   방 생성/초대코드/대기실 + GameSession(WebSocket) + 온라인 GameTable. 포인트X라 정산 단순 → 멀티 검증에 최적 첫 타깃
P5-b 맞춤게임      매칭 큐 + 에스크로 + 정산 + 이탈 처리
P5-c 웹 빌드       WebGL + 플랫폼 추상화(결제/광고 분기) + 정적 호스팅
```
**첫 타깃 추천 = P5-a(친구와 함께)** 를 P4 직후. 포인트 정산이 없어 Realtime·재접속만 검증하면 됨 → 온라인 인프라를 가장 안전하게 깐 뒤 맞춤게임(돈 걸린) 진입.

---

## 8. 결정 사항 (2026-06-15 확정)

- **호스팅 = 매니지드**(Railway/Render 등). 서버+PostgreSQL+Redis 한 곳에. 트래픽 늘면 이전.
- **Realtime = Native WebSocket**. 웹 빌드는 후속이나, 나중에 안 갈아엎으려 처음부터 채택.
- **소셜 로그인 = Apple + Google + Kakao 전부**. 게스트 로그인도 지원(소셜 승격).
- **결제 = 1차 보류, 광고 먼저**. 이유: 사업자/PG심사/미성년 결제법(R4) 무겁고, R1 웹보드 분류 결과에 결제 한도 규제 직격 → **법무 검토 후 추가**. 지금은 `IPurchaseProvider` 인터페이스만 예약, 적립 경로는 광고로 완성.
- **아바타** = 추후 결정(파츠 조합 vs 프리셋). 프로필 착수 시.

### 8-1. Shop 1차 범위 (결제 제외)
```
광고 보상(AdMob 보상형 + SSV 서버검증) → 포인트 적립    ← 1차
faucet 필수: 일일 무료 지급 + 파산 보너스(잔액 0 구제, R3)  ← 1차
IAP / 웹 PG                                              ← 법무 검토 후 후속
```
- 적립 로직(ledger 기록)은 광고·결제 공통 → 결제는 나중에 provider만 추가, 서버·지갑 불변.
