# BBONG 작업 일지

> 세션 인수인계용 요약. 상세 규칙=`rules.md`, 시스템=`architecture.md`, 리스크=`considerations.md`.

## 현재 상태 (2026-06-11)

- **Phase 0 규칙** ✅ / **Phase 1 코어 엔진** ✅ / **Phase 2 AI 봇** ✅ / **Phase 3 UI/UX** ✅ 플레이 가능 빌드
- 코어 테스트 **106개 통과**. 4인(사람+봇3) 한 게임(5판) 처음~끝 동작, 점수/판돈/전광판까지.

## 구조

```
core/BbongCore        순수 C#(netstandard2.1) 엔진 — 규칙 전부. dotnet test로 TDD.
  Cards/  Card,Deck,Shuffler,IRandom,SeededRandom
  Game/   Hand,Player,RoundState,GameState,StopResolver,RoundSettlement,StakePot
  Rules/  HandEvaluator,MeldType,MeldResult,Scoring,PlayerOutcome
  Config/ GameConfig (모든 설정 상수 단일 출처)
  Ai/     Bot,BotDifficulty (Easy/Normal/Hard)
client/BbongClient    Unity 6000.4.10f1. GameTableBootstrap.cs 단일 스크립트가 UI 전체 코드 생성.
  Assets/Plugins/BbongCore/BbongCore.dll  ← 코어 빌드 산출물(커밋 대상)
demo/BbongDemo        봇 토너먼트(난이도 검증). dotnet run --project demo/BbongDemo
scripts/sync-core-dll.sh  코어 재빌드 + DLL을 Unity로 복사 (코어 수정 시 필수)
```

## 빌드/실행

- 코어 테스트: `cd core && dotnet test` (.NET 8, `~/.dotnet`, PATH는 ~/.zshrc)
- 코어 수정 → Unity 반영: **`./scripts/sync-core-dll.sh`**
- Unity: `client/BbongClient` 열고, 빈 GameObject에 `GameTableBootstrap` 추가 후 Play
- 게임 로그: Unity Console `[BBONG]` (화면 로그는 제거됨)

## 이번 세션 주요 작업 (Phase 3 + 규칙 보강)

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

- **로비/방 생성 화면**(인원·판돈 선택, architecture §4-2)
- **실제 아트 에셋**(현재 절차생성)
- **Phase 4**: 회원/프로필/지갑/상점/광고 (서버 ASP.NET Core)
- 봇 스톱 성향 과함(시뮬상 78% 스톱 종료) — 다양성 튜닝 여지
- rules.md ❓OPEN: 공동 1등 판돈 나머지 절사 처리

## 컴플라이언스 핵심 (잊지 말 것)

판돈 = 구매가능·**환전 절대 불가** 가상재화. 전체이용가 목표. 최대 리스크=GRAC 웹보드 분류(출시 전 법무검토). `considerations.md` R1.
