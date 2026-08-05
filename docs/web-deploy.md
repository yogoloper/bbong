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

### 1. 서버 임시 배포 (사용자)

- GitHub Pages는 https라서 서버도 **https/wss 필수** (Cloudflare Tunnel, ngrok, fly.io 등).
- PostgreSQL 필요(compose.yaml 참고). Redis는 현재 미사용이라 없어도 기동됨.
- 로컬+터널 최단 경로: `docker compose up -d` → `dotnet run`(5080) →
  `cloudflared tunnel --url http://localhost:5080` → 발급된 https URL 사용.

### 2. 클라이언트 웹 빌드 (Unity)

1. Build Profiles → **Web** 프로필 추가 → Switch Platform (Web Build Support 설치돼 있음).
2. Build 출력 폴더를 **레포 루트의 `webgl/`** 로 지정 (폴더명 그대로 — 워크플로가 이 경로를 배포).
3. 빌드 후 `webgl/index.html` 존재 확인.

### 3. GitHub Pages 켜기 (최초 1회)

1. 레포 Settings → Pages → Source = **GitHub Actions**.
2. `webgl/` 포함해서 push → Actions에서 "Deploy WebGL to Pages" 완료 대기.
3. 접속: `https://yogoloper.github.io/bbong/?server=https://<서버주소>`

## 확인 체크리스트

- [ ] `?server=` 없이 열면 연습 모드(봇전)만 동작(서버 불필요) — 이것만으로도 심사 플레이 가능
- [ ] `?server=` 붙이면 게스트 로그인 → 친구방 생성/입장/멀티 동작
- [ ] 모바일 브라우저는 가로로 돌려서 플레이(UI가 가로 설계)

## 주의

- 서버 URL이 바뀌면 링크의 `?server=` 값만 바꾸면 됨(재빌드/재배포 불필요).
- 심사 종료까지 서버·Pages 링크 유지할 것(제출 안내 요구사항).
