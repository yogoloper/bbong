# BBONG 작업 일지

> 세션 인수인계용 요약. 상세 규칙=`rules.md`, 시스템=`architecture.md`, 리스크=`considerations.md`.

## 현재 상태 (2026-08-08)

- **Phase 0 규칙** ✅ / **1 코어** ✅ / **2 AI 봇** ✅ / **3 UI/UX** ✅ / **4 메타·수익화(서버)** 🚧 / **5 온라인 멀티 M1(친구방)** ✅ 동작
- 테스트: 코어 **129개**, 서버 **131개** (NUnit, PG round-trip·WS 통합 포함).
- 배포: 웹 GitHub Pages + 서버 fly.io(bbong.fly.dev, 단일 머신 필수) 운영 중. Playwright 스모크(scripts/web-smoke.py) 통과.
- M1 이후 완료(2026-08-07~08): 친구방 봇 추가/삭제(방장, 사람+봇 정원 6), 닉네임 풀 30×30(코어 NicknamePool 단일 관리),
  이탈 대체 봇 닉네임 유지, 사람 우선 뽕 창(봇 스나이핑 금지, 연습 모드 flow token 경합 수정),
  뽕 바가지 신규 규칙(뽕 후 3장 자연뽕 손 소진, §7 조건②), 용어 통일(라운드/스톱 바가지/뽕 바가지),
  전체 재셔플(§3, 맨 위 안 남김), 점수판 착지 후 1초 지연 노출 + 종료 버튼 게이팅,
  리더보드 3열 표, 좌석 라벨 확대·뒷면 카드 균일화(전용 소형 스프라이트), 셔플 리플 사운드, 튜토리얼 정산 설명 교정.
- 클라: 연습(봇전, 무료) + **친구방 온라인 멀티**(초대코드, 2~6인, 서버 권위) 3클라 수동 검증 완료. 메인 로비 + 5개 모드 화면.
- 서버: ASP.NET Core + EF Core/PostgreSQL + JWT + **WebSocket(/ws) 실시간**. 게스트/소셜 로그인·프로필·지갑·광고보상·매치 에스크로/정산 API.
- 부하/정합 검증: `tools/BbongLoadSim` — 게스트 20명 동시 × 5게임, 394요청 실패 0, 잔액 정합 불일치 0.

## 구조

```
core/BbongCore        순수 C#(netstandard2.1) 엔진 — 규칙 전부. dotnet test로 TDD.
  Cards/  Card,Deck,Shuffler,IRandom,SeededRandom
  Game/   Hand,Player,RoundState,GameState,StopResolver,RoundSettlement,StakePot
  Rules/  HandEvaluator,MeldType,MeldResult,Scoring,PlayerOutcome
  Config/ GameConfig (모든 설정 상수 단일 출처)
  Ai/     Bot,BotDifficulty (Easy/Normal/Hard)
client/BbongClient    Unity 6000.4.10f1 (URP). Assets/Scripts/ 부트스트랩별 화면 코드 생성.
  GameTableBootstrap.cs  게임 테이블 UI 전체(봇 게임). 서버 불필요.
  AuthBootstrap → MainLobbyBootstrap → Match/Profile/Shop/FriendRoom  정식 흐름(서버 필요).
  ServerApi.cs  서버 호출(BaseUrl=localhost:5080). Session.cs 토큰 보관.
  Assets/Plugins/BbongCore/BbongCore.dll  ← 코어 빌드 산출물(커밋 대상)
  Assets/Scenes/SampleScene  Auth(활성)+Lobby/GameTable(비활성) 부트스트랩 배선.
server/BbongServer    ASP.NET Core(.NET8). EF Core+PostgreSQL, JWT. Database.Migrate() 자동.
  엔드포인트: /auth/guest /auth/social /auth/link /me /me/nickname /shop/ad-reward
             /match/start(판돈 에스크로) /match/{id}/result(1회 정산, 절사=StakePot.Share)
demo/BbongDemo        봇 토너먼트(난이도 검증). dotnet run --project demo/BbongDemo
tools/BbongLoadSim    동시접속 부하/정합 시뮬. dotnet run --project tools/BbongLoadSim -- --users 20 --games 5
scripts/sync-core-dll.sh  코어 재빌드 + DLL을 Unity로 복사 (코어 수정 시 필수)
compose.yaml          dev 인프라 PG(5432)+Redis(6379). 서버 앱은 컨테이너 밖 dotnet run.
```

## 빌드/실행

- 코어 테스트: `cd core && dotnet test` (.NET 8, `~/.dotnet`, PATH는 ~/.zshrc)
- 코어 수정 → Unity 반영: **`./scripts/sync-core-dll.sh`**
- Unity Hub 등록 경로 = **`client/BbongClient`** (레포 루트 아님 — 루트 열면 빈 프로젝트 생김)
- 봇 게임만: 빈 GameObject에 `GameTableBootstrap`만 붙이고 Play (서버 불필요)
- 정식 흐름(로그인~): ① `docker compose up -d` ② `cd server/BbongServer && dotnet run` (포트 5080) ③ Unity에서 AuthBootstrap Play → 게스트 시작
- 게임 로그: Unity Console `[BBONG]` (화면 로그는 제거됨)

## 세션 (2026-08-06) — 튜토리얼 + 테이블 연출 대공사

- **튜토리얼**: 로비 첫 카드로 진입, 7개 레슨(기본 턴→뽕→자연뽕→손털기 박→족보→스톱→스톱 바가지).
  `RoundState.Restore`(신규 공개 팩토리, TDD 코어 124→**125**)로 수제 고정 덱 리깅 — 전 유저 동일 학습.
  실제 조작(카드/버튼)으로만 진행, 턴 카운트다운은 튜토리얼만 끔(ShowTurnCountdown).
- **덱 UI**: 엎어진 드로우 덱(카드백+남은 장수)을 중앙 왼쪽에, 버림 더미를 오른쪽에 한 세트로 배치.
- **카드 비행 연출(전 모드)**: 드로우=덱→좌석, 버림=좌석→더미(최종 위치·기울기 미리 추첨, 착지 점프
  없음, 클릭 즉시 손패에서 제거), 뽕/공개 패=그룹 동시 비행 후 손부채꼴(위 넓고 아래 좁게, 간격 68px),
  재셔플=버림 카드들이 뒷면으로 덱에 수렴. 스톱/족보 공개 패도 버림 더미 위로 쌓임.
- **봇 페이싱**: 트리거 카드 착지 기준 1초 통일(드로우 후 행동/스톱 선언/뽕 선언 — 경로별 선행 대기
  차감), 뽕 후 토스 1초, 토스 착지 후에야 다음 턴/내 2차 뽕 창. 서버 봇 대체 700→**1000ms**.
- 배포: fly 서버 + Pages 웹 빌드(빌드 완료 자동 감시 후 커밋) 최신화 — 심사 링크 전체 반영.

## 세션 (2026-08-05) — 해커톤 웹(WebGL) 배포 준비

- **용도**: 게임 해커톤 사전과제(웹 플레이 링크 필요). 웹은 임시 경로 — 정식 타깃은 스토어 출시.
- **WsClient WebGL 분기**: 브라우저 WebSocket jslib 브리지(`Plugins/WebGL/BbongWebSocket.jslib`,
  단일 연결 폴링). 헤더 불가 → 토큰 쿼리 전달. 기존 BCL 경로는 #if로 보존(에디터/모바일 무변화).
- **서버**: `/ws?access_token=` 쿼리 토큰 병행 허용(JwtBearer OnMessageReceived) + CORS AnyOrigin
  (Bearer라 무방). TDD 서버 118→**119**.
- **서버 주소 주입**: WebGL은 페이지 URL `?server=https://...`로 지정(ServerApi.ResolveBaseUrl) —
  재빌드 없이 서버 교체.
- **빌드/배포**: WebGL 압축 Gzip+Decompression Fallback(GitHub Pages 헤더 문제 대응),
  `webgl/**` push 시 Pages 자동 배포 워크플로(.github/workflows/pages.yml). 절차 = docs/web-deploy.md.
- 원격 연결: origin = github.com/yogoloper/bbong.

## 세션 (2026-08-04) — 테이블 UI 단일 틀 1단계

- **방향 확정(사용자)**: 게임 판 UI·문구·점수판은 **하나의 틀**. 연습/친구방/일반게임(상금)은
  참여자 구성만 다르게. 모드별 별도 UI 금지. **룰도 동일**(턴 타이머·이탈 처리 — rules.md §3, §9-4).
- **1단계 완료**: `GameTableView` 추출(캔버스/좌석/손패/버림 타임라인/멘트/버튼/점수판/콜아웃/효과음
  전부 소유, RoundView 입력 + 이벤트 출력). NetGameTableBootstrap은 WS↔뷰 연결 드라이버로 축소
  (826→315줄, 렌더 코드 0). 친구방 4클라 수동 검증 통과.
- **rules.md 갱신**: §9-4에 AFK(한 판 내내 무입력) 강퇴+봇 대체 추가 — 친구방 포함 전 모드,
  일반 게임은 몰수+상금 정산 유지. 턴 타이머는 기존 확정(5초, 드로우 카드 자동 버림) 그대로.
- **2단계 완료**: 연습 모드(GameTableBootstrap)도 GameTableView로 이식 — `BuildRoundView()`가
  로컬 코어 상태를 서버 ToView와 같은 RoundView로 합성. 1,366→약 700줄, 렌더 코드 0.
  문구·연출(족보 펼침/헤일로/플래시/셔플음) 전 모드 통일. 4클라+연습 수동 검증 통과.
- **턴 타이머 5초 구현(전 모드, rules.md §3)**: 미행동 시 자동 진행 — WaitingStop→자동 계속,
  WaitingDiscard→드로우 카드 자동 버림, 뽕/자연뽕 추가 버림→고정 패 제외 첫 카드.
  로컬=코루틴+토큰(GameConfig.TurnTimerSeconds), 서버=TurnTimeoutCmd+ArmTurnTimer(TDD,
  서버 107→**111**). 원래 미구현이었음(백로그) — 룰 동일화 첫 항목 해소.
- **턴 카운트다운 UI**: 공용 뷰가 내 행동 대기 상태를 감지해 안내 문구 옆에 남은 초 표시
  (뽕 버튼 카운트다운과 동일 컨셉, 상태 키로 리렌더 리셋 방지). 전 모드 자동 적용.
- **방장 위임**: 대기실에서 방장이 나가도 방 유지 — 다음 입장자에게 위임, 마지막 1명 퇴장 시 닫힘
  (TDD, 서버 111→**113**). rules.md §9-4에 명시. 게임 중 이탈은 여전히 방 해체(봇 대체가 후속).
- **이탈/AFK 봇 대체(§9-4) 완료**: 게임 중 이탈=방 해체 제거. 이탈(끊김/종료) 좌석은 그 판 동안
  5초 룰로 자리 보전(모바일 백그라운드 복귀 여지) → **판 종료 시 "직접 입력 0 + 턴 타임아웃 경험"
  좌석을 봇으로 전환**(턴이 안 와 기회 없던 좌석 보호). 봇(코어 Bot, Normal)이 세트 끝까지 대체
  플레이(BotActCmd 0.7초 간격), 닉네임 "(봇)" 표기, **우승 후보 제외**. 전원 이탈 시에만 방 해체,
  세트 종료 후 대기실 복귀 시 이탈자 정리+방장 위임. TDD 서버 113→**118**(WS 통합 포함).
  클라: BotTookOverMsg → 닉네임 갱신+"자리 교대" 콜아웃. 재접속(같은 유저 복귀)은 여전히 후속.

## 세션 (2026-08-03 심야) — 넷 테이블 폴리시 + 모바일 빌드 준비

- **턴 간격(서버)**: `RoundPhase.TurnGap` + `TurnGapCmd` + `RealtimeConfig.TurnGapMs=500` —
  버림 후 0.5초 무포커스 간격(뽕 없음/전원 패스/뽕창 타임아웃 경로). 서버 테스트 101→**107**.
- **코어**: `RoundView`에 TurnGap 페이즈 상수 추가, sync-core-dll.sh로 DLL 반영.
- **클라(NetGameTableBootstrap)**: TurnGap 동안 좌석 포커스 해제(로컬과 동일 연출),
  판 종료 점수판을 로컬과 같은 표(판별 히스토리+계, 5초 후 페이드)로 교체.
- **넷 손패 뭉개짐 수정**: 손패 HorizontalLayoutGroup이 `childControl*=false`라
  CreateCardFace의 `LayoutElement.preferred*`(130x200)가 무시되어 기본 100x100 정사각 +
  `childForceExpandWidth` 기본 true로 전폭 흩어짐 → 로컬 CreateRow와 동일하게
  childControl 유지 + `childForceExpand*=false`로 수정. macOS 빌드/에디터 모두 정상 확인.
- **모바일 준비**: Android 빌드 프로필 추가. NDK 유실 복구 — 6000.4.10f1의
  `PlaybackEngines/AndroidPlayer/NDK`가 비어 "Android NDK not found" → 6000.0.78f1의 동일
  버전(r27c, 27.2.12479018) 복사로 해결. 모바일 UI 확인은 Device Simulator(20:9)로 — 기기 불필요.
- **자연뽕 즉시 내려놓기(넷)**: 로컬은 선언 즉시 3장 내려놓는데 넷은 서버 확정까지 6장 유지 →
  선언 순간 낙관적으로 내려놓고(_naturalLaidLocally), 서버 확정 시 콜아웃/효과음 없이 치환,
  Error 시 원복. 일반 뽕과 동일한 흐름.
- **족보 표시명 단일 출처**: 코어 `Rules/MeldNames.Korean()` 신규(TDD, 코어 118→**124**) —
  로컬 중복 매핑 삭제, 서버 판 종료 사유 `[Straight]`→`[스트레이트]`, 넷 콜아웃도 로컬과 동일
  형식("닉\n스트레이트!"). DTO는 enum 문자열 유지(언어 중립), 표시 시점에만 변환.
- 검증: 새 macOS 빌드 다중 클라(최대 6개) + 서버 107 테스트 통과.
- 참고: 폰 실기기 테스트 시 `ServerApi.BaseUrl`을 LAN IP로 + Player Settings에서
  "Allow downloads over HTTP" 허용 필요.

## 세션 (2026-08-03 후반) — Phase 5 M1: 친구방 온라인 멀티

- **프로토콜**: 순수 WebSocket + JSON(신규 의존성 0). 공유 DTO는 `core/BbongCore/Online/`
  (JsonUtility 호환 public 필드). 좌석별 개인화 스냅샷(RoundView — 타인 손패는 장수만) + 연출 이벤트.
- **서버** `server/BbongServer/Realtime/`: RoomRegistry(6자리 초대코드) → Room(Channel 단일 소비 루프 —
  레이스 차단, 선착 뽕) → GameSession(서버 권위, 전송 무지·SessionOutput 반환 → WS 없이 단위테스트).
  뽕 창 5초 타이머, 판 사이 8초 자동, 종료 5종 전부, 세트 후 대기실 복귀. /ws는 기존 JWT 재사용.
- **클라**: WsClient(BCL ClientWebSocket, 수신 큐→Update 펌프, runInBackground), FriendRoomBootstrap
  실구현(만들기/코드 입장/대기실), NetGameTableBootstrap(RoundView 렌더+의도 전송, 좌석 회전 배치),
  TableArt 추출(카드 아트/정렬/효과음 — 로컬·넷 공용).
- 검증: 서버 101 테스트 + 3클라(호스트+2) 수동 — 방 생성/입장/시작/플레이 동작 확인.
- 끊김 = 방 해체(MVP). 알려진 것: macOS 창 임의 비율로 UI 어긋남(모바일 타깃은 정상).

## 세션 (2026-08-03) — 매치 API + 동시접속 검증

- **연습 무료화**: 봇전 판돈/로컬 지갑(PlayerWallet) 제거. 재화는 서버 원장만 진실.
- **매치 API**: `/match/start`(StakeEscrow 차감) → `/match/{id}/result`(1회 정산, StakePayout).
  공동 1등 절사 = 코어 `StakePot.Share` 공유(rules.md ❓OPEN 절사로 확정). matches 테이블(AddMatches).
- **동시접속 테스트 방안**: ① `tools/BbongLoadSim`(게스트 N명 동시, 잔액 정합 자동 검증)
  ② Unity File > Build And Run 스탠드얼론 2개 + 에디터 1개 = 게스트 3명 동시 육안 확인.
- 게임플레이 QoL: 턴 전환 0.5초 무포커스 연출, 뽕 추가버림 같은숫자 3장째 허용, 손털기 종료 엣지 2건 수정.
- 한계 문서화: considerations.md **R7**(지갑 동시성 레이스, 미정산 매치, 결과 신뢰).

## 세션 (2026-06-29) — Phase 4 안정화 + 로그

- **봇 타이밍 로그 추가**(GameTableBootstrap): 턴 진입(seat·이름·남은더미), 카드 드로우(카드·남은더미·손패). 기존 버림/뽕/뽕창/스톱/재셔플 로그는 그대로.
- **서버 포트 정합**: launchSettings 5030→**5080**(클라 ServerApi.BaseUrl과 일치). 이제 `dotnet run`만으로 연결.
- **SampleScene 배선**: 한 GameObject에 Auth(활성)+Lobby/GameTable(비활성) 부트스트랩, EngineSmokeTest 비활성.
- 인프라 검증: docker compose(PG/Redis healthy) + 서버 5080 기동 + `/auth/guest` 200 확인.
- 트러블슈팅: 폴더명 `06. bbong`→`06.bbong` 변경으로 Unity Hub 경로 깨짐 + 레포 루트를 열어 빈 프로젝트 생성됨 → 루트 잔여물 삭제, Hub에 `client/BbongClient` 재등록.

## Phase 4 서버 (0611 이후 누적)

- 스켈레톤(auth/wallet/JWT) → EF Core+PostgreSQL 영속화 → docker compose(PG/Redis) → 소셜 로그인(Google/Apple/Kakao, 게스트 승격) → 프로필 닉네임 변경 → 상점 광고보상(쿨다운·파산구제).
- 클라 연동: 서버 로그인 + 메인 로비 5개 모드 화면, 매치 셋업, 프로필/상점 화면.
- UI: navy→blue 테마 통일, Kenney CC0 스프라이트(버튼/패널/코인), 상단바, 모드 카드, CTA/뒤로 버튼 통일, 가로 고정, 연습봇 난이도 선택.

## 이전 세션 주요 작업 (Phase 3 + 규칙 보강)

UI: 코드생성 카드아트(색배경+모서리핍, 색약 안전 팔레트), 통합 버림 타임라인(겹침/폭맞춤),
절차 효과음, 점수 전광판(상시+판종료 표팝업, 게임종료시 유지), 내 차례 안내문구, 자동 드로우(드로우 버튼 제거).

규칙 확정/수정:
- 또이또이 = 2+2+2 **또는 3+3**
- 자연뽕 = 같은 숫자 3장이면 성립(**손패 6장 아니어도**, 뽕 후 3장 손소진 가능)
- 다음 판 선 = **직전 판 끝낸 사람**(스톱 바가지면 이긴 사람)
- 바닥 더미 **재셔플 2회 한도**, 초과 소진 시 강제 종료(전원 손패 합)
- 스톱 바가지 = **손패 합 + 30** (고정 30 아님 — 한번 바꿨다 되돌림)
- 턴 5초 타이머(turnTimerSec), 뽕 2초 창

버그 수정: Input System 버튼 이중발화(Resolving 가드), 봇 턴 stale NeedDiscard 오노출(CurrentSeat 가드),
고정 시드→랜덤, 7장 더블드로우, 봇 뽕/자연뽕 추가버림에도 내 뽕 기회.

## TODO — Phase 5 M2 (맞춤게임/판돈 멀티) 우선순위

> 2026-08-08 야간, `feature/m2-stake-multiplayer` 브랜치에서 1·2·3(코드)·5 완료.
> 브랜치 커밋: ba87a67(R7 락) → fc7be4f(판돈 방) → b74aff6(재접속+클라 흐름).

1. ~~R7 지갑 동시성 락~~ ✅ — ILedgerStore.WithWalletLockAsync(PG advisory / 인메모리 세마포어),
   매치 에스크로·정산·광고 적립 직렬화. 20-동시 에스크로 테스트로 과인출 0 증명.
2. ~~판돈 방 생성/입장/정산~~ ✅ (서버) — CreateRoomMsg.stake, 입장 선에스크로(WsEndpoint),
   거절/대기실 퇴장 환불, 이탈 몰수, 세트 종료 시 사람 우승자 균등 배당(절사) + 방 폭파.
   무료방은 기존 대기실 복귀 유지. IStakeBank(ScopedStakeBank) + StakeRoomTests 7건.
   실서버 WS E2E로 잔액 10000→9000→환불→전액 에스크로 검증.
3. **클라 맞춤게임 UI** 🚧 코드 완료·빌드 검증 대기 — 맞춤게임 설정 → 친구방 흐름 재사용
   (PendingStake 핸드오프), 대기실에 입장료·사람 기준 총상금 표시.
   ⏳ Unity 에디터가 열려 있어 CLI 빌드 불가 — `scripts/stake-room-check.py`로 검증 예정.
4. **방찾기/빠른매칭** — 공개 방 목록 + 조건 자동 입장.
5. ~~재접속~~ ✅ — 게임 중 좌석 보유자가 다시 joinRoom하면 새 소켓으로 자리 복귀,
   봇 자리 회수(사람 타이머 재무장), AFK 슬레이트 초기화, gameStarted+turnBegan 재동기화
   (클라 무변경으로 동작). 판돈 방 재접속은 재에스크로 금지. WS E2E 검증 완료.
6. **미정산 매치 타임아웃 정리 잡** — 방치된 에스크로 청소.
7. **머지/배포** — 사용자 확인 후 main 머지 → fly + 웹 배포(코어 프로토콜 변경 포함).

### 백로그 (급하지 않음)
- 봇 스톱 성향 과함(시뮬상 78% 스톱 종료) — 다양성 튜닝 여지
- 실제 아트 에셋(카드는 여전히 절차생성) / macOS 테스트 창 비율 대응(모바일 타깃은 무관)
- 족보 임계값 리밸런스(10이하→13이하, 66이상→63이상 — 확률 측정상 총통보다 희귀. 보류 중)
- WebGL WS 티켓 방식(현재 access_token 쿼리로 해결돼 있음 — 보안 강화 여지)

## 컴플라이언스 핵심 (잊지 말 것)

판돈 = 구매가능·**환전 절대 불가** 가상재화. 전체이용가 목표. 최대 리스크=GRAC 웹보드 분류(출시 전 법무검토). `considerations.md` R1.
