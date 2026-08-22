# 소셜 로그인 실행 계획

> 작성 2026-08-20. 목표: 기기 교체·재설치에도 계정(포인트·전적)이 이어지게 한다.
> 1차 범위는 **Android + Google**. Apple(Sign in with Apple)은 iOS 착수 시점에 별도.

## 이미 돼 있는 것 (서버)

2026-08-12 `SplitAccountSocials` 마이그레이션과 함께 서버 뼈대는 완성돼 있습니다.

- `POST /auth/social` — 소셜 idToken으로 기존 계정 반환 또는 신규 생성 + JWT 발급
- `POST /auth/link` — 로그인 상태의 게스트를 소셜로 승격(기존 id·잔액·전적 유지)
- `ISocialTokenVerifier` 추상화 — 현재 구현은 `DevBypassSocialVerifier`(개발용,
  `BBONG_SOCIAL_DEV_BYPASS=true`)와 `NotConfiguredSocialVerifier`(운영 기본, 거부)뿐

즉 남은 일은 세 덩어리입니다: **① 진짜 구글 검증기(서버) ② 클라이언트 연동(UI+플러그인)
③ 구글 콘솔 등록(사람만 가능)**.

## 역할 구분 — 사람이 직접 해야 하는 일

Claude가 대신할 수 없는 항목입니다. 이게 선행돼야 실키 테스트가 가능합니다.

| # | 작업 | 어디서 | 산출물 |
|---|---|---|---|
| H1 | Google Cloud 프로젝트 생성(또는 기존 지정) | console.cloud.google.com | 프로젝트 ID |
| H2 | OAuth 동의 화면 구성 — 앱 이름, 지원 이메일, 스코프(기본 profile) | GCP 콘솔 | 게시 상태(테스트 모드면 테스터 계정 등록) |
| H3 | **Android OAuth 클라이언트 ID** — 패키지 `com.yogoloper.bbong` + 서명 SHA-1 | GCP 콘솔 | Android 클라이언트 ID |
| H4 | **웹 애플리케이션 OAuth 클라이언트 ID** — 서버가 idToken `aud` 검증에 쓰는 값 | GCP 콘솔 | 웹 클라이언트 ID (서버 env로 전달) |
| H5 | 릴리스 keystore 생성·보관 결정 (분실 시 앱 업데이트 불가 — 백업 필수) | 로컬 | keystore + SHA-1 |
| H6 | 실기기에서 실계정으로 로그인 E2E 확인 | 실기기 | — |
| H7 | (M4와 연동) 개인정보처리방침에 구글 계정 식별자 수집 명시 | 문서 | — |

참고: 디버그 keystore SHA-1 추출(`keytool -list`)은 Claude가 로컬에서 해드릴 수 있습니다.
H3에는 디버그·릴리스 SHA-1을 **둘 다** 등록해야 개발 빌드와 스토어 빌드가 모두 통과합니다.

## 방식 결정 (착수 전 1가지)

- **A안 — Google Sign-In (Credential Manager 계열) 권장.** 계정 선택 시트 한 번으로 idToken을
  받는 표준 로그인. 서버가 이미 idToken 검증 구조라 그대로 맞고, Play Console 등록 없이
  GCP 콘솔만으로 개발을 시작할 수 있습니다.
- B안 — Google Play Games Services v2. 게임 특화(자동 로그인·업적 확장)지만 Play Console
  게임 서비스 구성이 선행돼야 하고 서버 교환 방식(auth code)이 달라 서버 수정 폭이 커집니다.
  업적·리더보드를 쓰게 되면 그때 추가 도입해도 됩니다.

## 단계별 계획

### P1. 서버 — GoogleSocialVerifier (Claude, H4만 있으면 착수 가능)
1. `Infrastructure/Social/GoogleSocialVerifier.cs` — 구글 JWKS로 idToken 서명·`iss`·`exp` 검증,
   `aud` = 웹 클라이언트 ID(env `BBONG_GOOGLE_CLIENT_ID`) 확인, `sub`를 소셜 ID로 반환.
   신규 패키지 없이 기존 `Microsoft.IdentityModel` 스택으로 구현(의존성 추가 시 사전 고지).
2. env 있으면 실검증기, 없으면 기존 NotConfigured 유지로 등록 분기.
3. 테스트: 위조 토큰 거부(서명·aud·만료), bypass 모드 회귀.

### P2. 클라이언트 — UI·플러그인 (Claude, 플러그인 의존성 추가는 사전 고지 후)
1. 플러그인 도입(A안: Google Sign-In용 Unity 플러그인 + EDM4R). Android 클라이언트 ID 연결.
2. `ServerApi.SocialLogin / LinkSocial` 추가, 성공 시 자격 저장 갱신.
3. `AuthBootstrap` — "소셜 로그인 (준비중)" 버튼 활성화 → 구글 로그인 → `/auth/social`.
4. **게스트 승격 진입점** — 설정(또는 프로필)에 "구글 계정 연동" 추가 → `/auth/link`.
   연동 완료 후 표시(연동됨 · 이메일 아님, 식별자만).
5. 충돌 UX: 게스트가 연동하려는 구글이 이미 다른 계정에 묶여 있으면 서버가 400을 주므로,
   "그 계정으로 전환(현재 게스트 포기)" 확인 흐름을 붙인다. ← **정책 결정 필요:
   전환 시 현재 게스트 잔액은 버림(병합 없음)이 기본 제안. 병합은 악용 여지가 있어 비권장.**

### P3. 검증 (Claude 선행 + 사람 마무리)
1. **콘솔 없이 먼저**: 로컬 서버 `BBONG_SOCIAL_DEV_BYPASS=true`로 전 흐름(로그인·승격·충돌·
   재시작 복귀) E2E — Claude가 에뮬레이터로 검증 가능.
2. 실키 검증: 에뮬레이터 AVD가 Google Play 이미지가 아니면 계정 시트가 안 떠서 **실기기 필요(H6)**.
3. 튜토리얼·매칭 등 기존 흐름 회귀 확인 후 커밋.

### P4. 마무리
- `mobile-roadmap.md`(게스트 유지 판단사항 반영)·worklog 갱신, H7 문서 반영.

## 리스크 메모
- SHA-1 불일치가 이 연동의 단골 장애 — 디버그/릴리스 지문 둘 다 H3에 등록.
- `aud` 검증을 Android 클라이언트 ID로 잘못 잡으면 서버 검증이 전부 실패(웹 클라이언트 ID가 맞음).
- 에뮬레이터 Play 서비스 부재 시 로그인 시트 자체가 안 뜸 — bypass 모드로 로직을 먼저 굳힌다.
- 승격 충돌 정책(P2-5)은 포인트가 걸린 결정이라 오너 확정 필요.
