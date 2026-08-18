using UnityEngine;

namespace Bbong.Client
{
    /// <summary>
    /// 메뉴·로비 화면 공용 색 토큰. 인게임 테이블(펠트 위)과 한 제품으로 읽히도록
    /// 네이비 램프는 UiArt.Backdrop과 GameTableView의 판 색에서 그대로 뽑았다.
    /// 규칙: 유채색은 Accent(골드) 하나 + Primary(액션 파랑) 하나뿐이다.
    /// 나머지는 전부 이 네이비 램프 위의 명도 차로만 표현한다.
    /// </summary>
    internal static class UiTheme
    {
        // ── 바탕: 배경 → 판 → 컨트롤 순으로 한 단계씩만 밝아진다 ──
        /// <summary>상단바·프로필 보드·설정 시트·튜토리얼 안내판의 단일 판 색.</summary>
        public static readonly Color PanelBg = new(0.10f, 0.15f, 0.29f, 0.95f);
        /// <summary>판 위에 얹는 면: 모드 카드·통계 타일·선택된 칩의 바탕.</summary>
        public static readonly Color Surface = new(0.14f, 0.20f, 0.38f, 0.95f);
        /// <summary>고르지 않은 칸·세그먼트의 비선택 상태. Surface보다 한 단 어둡다.</summary>
        public static readonly Color SurfaceDim = new(0.12f, 0.17f, 0.33f, 0.85f);
        /// <summary>기본 버튼(부차 액션). CTA보다 반드시 어두워야 한다.</summary>
        public static readonly Color Control = new(0.18f, 0.26f, 0.45f);

        // ── 홈·막·선 ──
        /// <summary>세그먼트 트랙·승률 막대의 홈. 파인 곳은 전부 이 한 값이다.</summary>
        public static readonly Color Trough = new(0f, 0f, 0f, 0.34f);
        /// <summary>모달 뒤 화면을 덮는 막.</summary>
        public static readonly Color Scrim = new(0f, 0f, 0f, 0.72f);
        /// <summary>구분선·헤어라인·표 머리 밑줄.</summary>
        public static readonly Color Divider = new(1f, 1f, 1f, 0.10f);
        /// <summary>표 짝수 행 줄무늬. 행 구분 이상의 의미를 주면 안 된다.</summary>
        public static readonly Color Stripe = new(1f, 1f, 1f, 0.04f);

        // ── 글자: 3단계뿐이다 ──
        /// <summary>골드 바탕 위에 얹는 글자(선택된 탭·칩·배지). 테이블 좌석 안쪽 판과 같은 값.</summary>
        public static readonly Color Ink = new(0.07f, 0.11f, 0.22f);
        /// <summary>본문·제목. 순백 대신 살짝 웜한 흰색 — 테이블 안내 문구와 같은 계열.</summary>
        public static readonly Color InkOn = new(0.97f, 0.96f, 0.93f);
        /// <summary>부제·표 값·캡션. PanelBg 위 6.6:1 — 4.5:1을 넘는 가장 낮은 알파.</summary>
        public static readonly Color InkMuted = new(1f, 1f, 1f, 0.62f);
        /// <summary>비활성·빈 자리처럼 "읽히면 안 되지만 있는 건 보여야" 하는 값. 3.0:1.</summary>
        public static readonly Color InkDisabled = new(1f, 1f, 1f, 0.34f);

        // ── 강조: 화면당 하나 ──
        /// <summary>소프트 골드. "값"이 아니라 "지금 선택된 것" 하나에만 쓴다.</summary>
        public static readonly Color Accent = new(0.94f, 0.83f, 0.55f);
        /// <summary>골드의 은은한 면(승리 행 바탕 등). 글자에는 쓰지 않는다.</summary>
        public static readonly Color AccentSubtle = new(0.94f, 0.83f, 0.55f, 0.18f);

        // ── 액션 ──
        /// <summary>주요 CTA. 흰 라벨 대비 5.2:1 (기존 0.24,0.50,0.88은 3.9:1이라 어둡게 잡음).</summary>
        public static readonly Color Primary = new(0.20f, 0.42f, 0.76f);
        /// <summary>판을 버리는 동작·오류 문구. 테이블의 스톱 붉은색과 같은 역할.</summary>
        public static readonly Color Danger = new(0.62f, 0.24f, 0.24f);
        /// <summary>완료 안내.</summary>
        public static readonly Color Success = new(0.30f, 0.58f, 0.44f);
    }
}
