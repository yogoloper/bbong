# BBONG 작업 일지

> 세션 인수인계용 요약. 상세 규칙=`rules.md`, 시스템=`architecture.md`, 리스크=`considerations.md`.

## 현재 상태 (2026-08-03)

- **Phase 0 규칙** ✅ / **1 코어** ✅ / **2 AI 봇** ✅ / **3 UI/UX** ✅ / **4 메타·수익화(서버)** 🚧 진행 중
- 테스트: 코어 **116개**, 서버 **62개** (NUnit, PG round-trip 포함).
- 클라: 4인(사람+봇3) 한 게임(5판) 처음~끝 동작. 메인 로비 + 5개 모드 화면. **연습(봇전)은 무료 — 로컬 지갑(PlayerWallet) 삭제**.
- 서버: ASP.NET Core + EF Core/PostgreSQL + JWT. 게스트/소셜 로그인·프로필·지갑·광고보상 + **매치 에스크로/정산 API**. docker compose로 PG/Redis 기동.
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

## 미해결 / 다음 후보

- **Phase 5 온라인 멀티**: 대기실/방찾기/게임서버(Realtime). 맞춤게임에서 매치 API 소비 시작점.
- **R7 지갑 동시성**: advisory lock/SERIALIZABLE — 멀티 전 필수(considerations.md).
- **미정산 매치 타임아웃 정리 잡** (후속)
- **실제 아트 에셋**(카드는 여전히 절차생성)
- 봇 스톱 성향 과함(시뮬상 78% 스톱 종료) — 다양성 튜닝 여지

## 컴플라이언스 핵심 (잊지 말 것)

판돈 = 구매가능·**환전 절대 불가** 가상재화. 전체이용가 목표. 최대 리스크=GRAC 웹보드 분류(출시 전 법무검토). `considerations.md` R1.
