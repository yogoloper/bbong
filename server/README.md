# server — ASP.NET Core 백엔드 (Phase 4~5)

REST(도메인 API) + Realtime(SignalR/WebSocket) + 권위 게임 서버.

- 공유 엔진 `../core/BbongCore`를 참조 → 클라 입력을 서버가 동일 로직으로 재검증(치트 방지).
- 도메인: Auth/Profile/Wallet/Shop/Friends/Lobby/GameSession (`../docs/architecture.md` §3).
- 착수 시점: **Phase 4 (회원·상점)** → **Phase 5 (멀티)**. 그 전까지 비어 있음.
- 데이터: PostgreSQL · Redis.
