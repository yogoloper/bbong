# BBONG (뽕) — AI 활용 기술 문서

## 1. 사용 AI 도구

| 도구 | 용도 |
|---|---|
| **Claude Code** (Anthropic, Claude 모델 CLI 에이전트) | 기획 정리 → 룰 명세 → 코어 엔진 → 서버 → 클라이언트 → 배포까지 전 개발 과정의 페어 프로그래머 |

개발 전 과정을 Claude Code와의 대화형 세션으로 진행했으며, 커밋 메시지에
`Co-Authored-By: Claude`가 남아 있어 저장소 커밋 기록으로 활용 이력을 확인할 수 있습니다.

## 2. 프로젝트 구조와 AI의 역할

```
core/BbongCore     순수 C# 룰 엔진 — 게임 규칙 전부 (NUnit 테스트 124개)
server/BbongServer ASP.NET Core — 계정/지갑 API + WebSocket 실시간 멀티 (테스트 119개)
client/BbongClient Unity 6 — 코드 생성 UI, 공용 게임 테이블 뷰(GameTableView)
```

- **룰 명세 우선**: 구두로 설명한 전통 놀이 규칙을 AI와 문답으로 `docs/rules.md`(확정 룰 명세)로
  정리한 뒤, 이 문서를 단일 기준으로 코어를 구현했습니다. 애매한 규칙(또이또이 구성, 바가지 점수,
  공동 1등 절사 등)은 AI가 엣지 케이스를 제시하고 사람이 확정하는 방식으로 좁혔습니다.
- **TDD**: 코어와 서버는 Red(실패 테스트) → Green(구현) → Refactor 사이클로 AI가 테스트를
  먼저 작성하고 구현했습니다. 총 243개 자동화 테스트.
- **서버 권위 멀티**: 좌석별 개인화 스냅샷(RoundView) + 이벤트 프로토콜 설계, 단일 소비 루프로
  선착순 뽕 레이스 차단, 5초 턴 타이머·이탈 시 봇 대체 등 온라인 룰을 AI와 설계·구현했습니다.
- **UI 단일 틀**: 연습/멀티가 공용 `GameTableView` 하나를 사용하도록 AI가 리팩터링해
  모드 간 UI·문구 불일치를 구조적으로 제거했습니다.
- **디버깅**: Unity Editor.log를 AI가 직접 분석해 레이아웃 버그(WebGL 손패 뭉개짐 등)의
  원인을 코드 수준에서 특정하고 수정했습니다.
- **배포 자동화**: WebGL용 브라우저 WebSocket 브리지(jslib), GitHub Pages Actions 배포,
  fly.io 서버 컨테이너화까지 AI가 작성했습니다.

## 3. 게임 내 AI (봇)

- `core/BbongCore/Ai/Bot.cs` — 난이도 3종(Easy/Normal/Hard) 휴리스틱 봇.
  버릴 카드 선택(높은 숫자/페어 보존), 뽕·스톱 판단 로직을 AI와 함께 설계했습니다.
- 봇 토너먼트 시뮬레이터(`demo/BbongDemo`)로 난이도 간 승률을 검증해 밸런스를 조정했습니다.
- 온라인 게임 중 이탈/무응답 유저 자리는 이 봇이 이어받아 게임 중단을 방지합니다.

## 4. 주요 프롬프트/지시 예시

실제 개발 세션에서 사용한 지시의 대표 예시입니다 (한국어 원문 요지):

- "Red → Green → Refactor 순서를 지켜 구현해줘. 버그 수정도 재현 테스트부터." (전역 지침)
- "게임 판 UI를 통일하고 멘트도 통일해야 해. 하나의 틀로 연습/친구/일반 게임이 참여자만
  다르게 돌아가야 해." → 공용 GameTableView 추출 리팩터링
- "5초 안에 카드를 내려놓지 않으면 자동으로 진행되는 규칙, 그리고 이탈한 유저는 봇이
  대체해서 한 게임이 끝날 때까지 진행되게 해줘." → 턴 타이머 + 봇 대체 구현
- "족보 선언 문구가 어디서는 '스트레이트', 어디서는 'Straight'로 나오면 안 돼." →
  코어에 표시명 단일 출처(MeldNames) 신설
- "친구방을 웹으로 배포해서 GitHub Pages로 플레이할 수 있게 해줘." → WebGL WebSocket
  브리지 + 쿼리 토큰 인증 + Pages/fly.io 배포 구성

## 5. 외부 에셋 / 오픈소스 출처

| 항목 | 출처 | 라이선스 |
|---|---|---|
| UI 스프라이트(버튼·패널·코인) | Kenney (kenney.nl) | CC0 |
| 폰트 Pretendard | github.com/orioncactus/pretendard | SIL OFL 1.1 |
| 카드 색상 팔레트 | Okabe-Ito 색각 이상 안전 팔레트(공개 연구 자료) | 자유 사용 |
| 카드 아트 / 효과음 | 코드로 절차 생성(외부 에셋 아님) | 자체 제작 |
| 엔진/프레임워크 | Unity 6 (Personal), .NET 8, PostgreSQL, Npgsql/EF Core | 각 표준 라이선스 |
| 배포 | GitHub Pages, GitHub Actions, fly.io, Docker | 각 서비스 약관 |

타인의 게임 코드·아트를 복제한 부분은 없으며, 게임 규칙은 전통 민속 놀이 방식을
자체 정리한 것입니다.
