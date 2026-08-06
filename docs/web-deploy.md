# 웹(WebGL) 배포 가이드 — 해커톤 임시 배포용

> 웹은 심사용 임시 경로. 정식 타깃은 플레이스토어/앱스토어(모바일 코드 경로는 그대로 유지됨).

## 준비된 것 (코드/설정에 반영 완료)

- **WsClient WebGL 분기**: 브라우저 WebSocket(jslib 브리지) + 토큰 쿼리 전달
  (`Assets/Plugins/WebGL/BbongWebSocket.jslib`). 에디터/스탠드얼론/모바일은 기존 BCL 경로 그대로.
- **서버**: `/ws?access_token=` 쿼리 토큰 병행 허용 + CORS(AnyOrigin). 헤더 방식도 계속 동작.
- **서버 주소 주입**: 웹 빌드는 페이지 URL 파라미터로 지정 — 재빌드 불필요.
  `https://yogoloper.github.io/bbong/?server=https://내서버주소`
- **WebGL 압축**: Gzip + Decompression Fallback (GitHub Pages가 Content-Encoding 헤더를
  안 줘도 로더가 자체 해제 — 설정 바꾸지 말 것).
- **Pages 자동 배포**: `webgl/**` push 시 `.github/workflows/pages.yml`이 배포.

## 배포 절차

### 1. 서버 배포 — fly.io (적용됨, 2026-08-05)

- 앱: **https://bbong.fly.dev** (nrt, shared-cpu-1x 512MB 1대) + PostgreSQL `bbong-db`(1노드).
- 인메모리 친구방이라 **머신 1대 고정 필수** — 배포는 항상 `fly deploy --ha=false`.
- 시크릿: `BBONG_JWT_KEY`(로컬 `~/.bbong-jwt-key`와 동일), `BBONG_DB_CONN`(Npgsql 키워드 형식,
  bbong-db.flycast). public 레포라 코드의 dev fallback 키 사용 금지.
- 재배포: 레포 루트에서 `fly deploy --ha=false` (Dockerfile/fly.toml 루트에 있음).
- 심사 종료 후 정리: `fly apps destroy bbong && fly apps destroy bbong-db`.
- (대안) 로컬+터널 최단 경로: `dotnet run`(5080) + `cloudflared tunnel --url http://localhost:5080`
  — URL이 터널 재시작마다 바뀌므로 제출용으론 부적합.

### 2. 클라이언트 웹 빌드 (Unity)

1. Build Profiles → **Web** 프로필 추가 → Switch Platform (Web Build Support 설치돼 있음).
2. Build 출력 폴더를 **레포 루트의 `webgl/`** 로 지정 (폴더명 그대로 — 워크플로가 이 경로를 배포).
3. 빌드 후 `webgl/index.html` 존재 확인.

### 3. GitHub Pages 켜기 (최초 1회)

1. 레포 Settings → Pages → Source = **GitHub Actions**.
2. `webgl/` 포함해서 push → Actions에서 "Deploy WebGL to Pages" 완료 대기.
3. 접속(제출용 최종 링크): `https://yogoloper.github.io/bbong/?server=https://bbong.fly.dev`

## 자동 스모크 테스트 (Playwright)

배포 전후로 게임이 실제로 도는지 자동 확인:

```bash
pip3 install playwright && python3 -m playwright install chromium   # 최초 1회
python3 scripts/web-smoke.py                                        # 로컬 빌드+서버
python3 scripts/web-smoke.py --url https://yogoloper.github.io/bbong --server https://bbong.fly.dev  # 배포본
```

헤드리스 브라우저로 로드 → 게스트 로그인 → 연습 게임 진입 → 봇 진행 20초를 돌리고,
페이지 에러 0건 + 판/턴 로그로 통과 판정(종료 코드 0/1). 스크린샷은 /tmp/bbong-smoke-*.png.
캔버스 렌더라 좌표 클릭 방식 — 로그인/로비 UI 앵커를 바꾸면 스크립트의 ANCHORS도 갱신할 것.

## 확인 체크리스트

- [ ] `?server=` 없이 열면 연습 모드(봇전)만 동작(서버 불필요) — 이것만으로도 심사 플레이 가능
- [ ] `?server=` 붙이면 게스트 로그인 → 친구방 생성/입장/멀티 동작
- [ ] 모바일 브라우저는 가로로 돌려서 플레이(UI가 가로 설계)

## 주의

- 서버 URL이 바뀌면 링크의 `?server=` 값만 바꾸면 됨(재빌드/재배포 불필요).
- 심사 종료까지 서버·Pages 링크 유지할 것(제출 안내 요구사항).
