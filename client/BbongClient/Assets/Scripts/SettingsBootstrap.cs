using UnityEngine;
using UnityEngine.UI;

namespace Bbong.Client
{
    /// <summary>
    /// 설정 오버레이: 소리·진동 토글과 약관·규칙 안내. 화면을 갈아엎지 않고 위에 덮는데,
    /// 방에 들어가 있는 화면에서 캔버스를 버리면 연결만 남고 상태가 어긋나기 때문이다.
    /// 덕분에 친구방·매칭 대기·게임 중에도 소리를 끌 수 있다.
    /// 겉모습은 프로필 보드와 같은 문법(네이비 판 + 골드 헤어라인 + 칩 세그먼트)을 쓴다.
    /// </summary>
    public sealed class SettingsBootstrap : MonoBehaviour
    {
        private const int SortOrder = 500; // 게임 테이블·모달보다 위

        // 시트 네 변 — 안쪽 요소가 전부 여기 물려 있어 한곳에서 잡는다
        private const float SheetLeft = 0.27f;
        private const float SheetRight = 0.73f;
        private const float SheetTop = 0.92f;
        private const float SheetBottom = 0.10f;

        private static readonly Color Ink = new(0.07f, 0.11f, 0.22f);            // 골드 바탕 위 글자
        private static readonly Color Sheet = new(0.10f, 0.15f, 0.30f, 0.97f);   // 판 — 프로필 보드와 동일
        private static readonly Color SurfaceDim = new(0.13f, 0.19f, 0.36f, 0.75f);
        private static readonly Color Track = new(0f, 0f, 0f, 0.45f);

        private GameObject _canvas;
        private Text _notice;
        private AudioSource _audio;
        private AudioClip _chime;

        private void Start()
        {
            UiKit.EnsureEventSystem();
            Build();
        }

        private void Build()
        {
            var canvasGo = new GameObject("SettingsCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvas = canvasGo;
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortOrder;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            // 소리를 켜는 순간 들려줄 확인음 — "켰는데 왜 조용하지"를 그 자리에서 판별하게 해 준다
            _audio = canvasGo.AddComponent<AudioSource>();
            _chime = TableArt.Tone("settings-chime", 660f, 0.09f, 18f);

            // 뒤 화면을 어둡게 덮고, 바깥을 눌러도 닫히게 — 뒤 버튼이 눌리는 사고를 막는다
            var scrim = UiKit.CreatePanel(canvasGo.transform, new Color(0f, 0f, 0f, 0.72f));
            UiKit.Stretch(scrim.rectTransform);
            var scrimBtn = scrim.gameObject.AddComponent<Button>();
            scrimBtn.transition = Selectable.Transition.None;
            scrimBtn.onClick.AddListener(Close);

            var root = SafeArea.Wrap(canvasGo.transform);

            // Panel9 스프라이트는 반투명이라 뒤 화면이 비친다 — 단색 백킹을 먼저 깔아 가린다
            var backing = UiKit.CreatePanel(root, new Color(0.07f, 0.11f, 0.23f, 0.995f));
            UiKit.Anchor(backing.rectTransform,
                new Vector2(SheetLeft, SheetBottom), new Vector2(SheetRight, SheetTop));

            var sheet = UiKit.CreatePanel(root, Sheet);
            if (UiArt.Panel9 != null)
            {
                sheet.sprite = UiArt.Panel9;
                sheet.type = Image.Type.Sliced;
            }

            UiKit.Anchor(sheet.rectTransform,
                new Vector2(SheetLeft, SheetBottom), new Vector2(SheetRight, SheetTop));
            sheet.gameObject.AddComponent<Button>().transition = Selectable.Transition.None; // 시트 클릭은 안 닫히게

            // 머리: 톱니 + 제목, 아래로 골드 헤어라인(상단바와 같은 마감)
            var gear = UiKit.CreateIcon(root, UiArt.IconGear,
                new Vector2(0.40f, 0.822f), new Vector2(0.435f, 0.884f));
            gear.color = new Color(1f, 1f, 1f, 0.9f);
            UiKit.CreateText(root, "설정", 42, TextAnchor.MiddleLeft,
                new Vector2(0.447f, 0.812f), new Vector2(0.60f, 0.894f)).fontStyle = FontStyle.Bold;
            UiKit.Anchor(UiKit.CreatePanel(root, Gold(0.30f)).rectTransform,
                new Vector2(SheetLeft + 0.02f, 0.796f), new Vector2(SheetRight - 0.02f, 0.799f));

            Toggle(root, "소리", 0.645f, () => AppSettings.SoundOn, on =>
            {
                AppSettings.SoundOn = on;
                if (on)
                {
                    _audio.PlayOneShot(_chime, 0.6f);
                }
            });
            Toggle(root, "진동", 0.505f, () => AppSettings.VibrationOn, on =>
            {
                AppSettings.VibrationOn = on;
                AppSettings.Vibrate(); // 켜는 순간 한 번 울려 확인시킨다
            });

            // 토글 층과 문서 층 사이 옅은 구분선
            UiKit.Anchor(UiKit.CreatePanel(root, new Color(1f, 1f, 1f, 0.10f)).rectTransform,
                new Vector2(SheetLeft + 0.02f, 0.468f), new Vector2(SheetRight - 0.02f, 0.470f));

            // 규칙·약관은 앱 안에서 열어야 한다(스토어 정책상 외부 링크만 두면 반려 사유가 된다)
            ChipButton(root, "게임 규칙", new Vector2(0.30f, 0.345f), new Vector2(0.492f, 0.435f), ShowRules);
            ChipButton(root, "이용약관 · 개인정보", new Vector2(0.508f, 0.345f), new Vector2(0.70f, 0.435f),
                ShowTerms);

            _notice = UiKit.CreateText(root, "", 23, TextAnchor.UpperCenter,
                new Vector2(0.295f, 0.25f), new Vector2(0.705f, 0.335f));
            _notice.color = new Color(1f, 1f, 1f, 0.7f);

            // 턴 타이머는 서버가 돌린다. 오버레이를 열어도 판은 안 멈추니 미리 알려준다.
            if (FindAnyObjectByType<GameTableView>() != null)
            {
                _notice.text = "판은 계속 진행돼요. 내 차례를 놓치지 않게 곧 닫아주세요.";
                _notice.color = new Color(1f, 0.8f, 0.5f);
            }

            // 게임 중일 때만 나가기. 판을 버리는 동작이라 확인 모달은 테이블이 갖고 있다.
            if (UiKit.ExitGameAction != null)
            {
                var exit = UiKit.CreateButton(root, "게임 나가기",
                    new Vector2(0.30f, 0.115f), new Vector2(0.492f, 0.237f), ExitGame, 28);
                exit.GetComponent<Image>().color = new Color(0.42f, 0.24f, 0.26f);
                Primary(UiKit.CreateButton(root, "닫기",
                    new Vector2(0.508f, 0.115f), new Vector2(0.70f, 0.237f), Close, 28));
            }
            else
            {
                Primary(UiKit.CreateButton(root, "닫기",
                    new Vector2(0.42f, 0.115f), new Vector2(0.58f, 0.237f), Close, 28));
            }

            UiKit.BackAction = Close; // 기기 뒤로가기는 오버레이만 닫는다
        }

        private static Color Gold(float alpha) =>
            new(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, alpha);

        /// <summary>닫기는 이 시트의 주 동작 — 골드로 채워 시선이 끝나는 자리를 만든다.</summary>
        private static void Primary(Button btn)
        {
            btn.GetComponent<Image>().color = UiKit.Accent;
            var text = btn.GetComponentInChildren<Text>();
            text.color = Ink;
            text.fontStyle = FontStyle.Bold;
        }

        /// <summary>둥근 칩 모양의 부차 버튼(규칙·약관) — 프로필 세그먼트와 같은 재질.</summary>
        private void ChipButton(Transform root, string label, Vector2 min, Vector2 max, System.Action onClick)
        {
            var btn = UiKit.CreateButton(root, label, min, max, () => onClick(), 25);
            var img = btn.GetComponent<Image>();
            img.sprite = UiArt.Chip;
            img.color = SurfaceDim;
            btn.GetComponentInChildren<Text>().color = new Color(1f, 1f, 1f, 0.85f);
        }

        /// <summary>
        /// 소리·진동 한 줄: 라벨 왼쪽, 오른쪽은 트랙에 담긴 끔/켬 칩 세그먼트.
        /// 버튼 두 개를 그냥 늘어놓는 대신 홈에 끼워 "상태를 고르는 스위치"로 읽히게 한다.
        /// </summary>
        private void Toggle(Transform root, string label, float y, System.Func<bool> get, System.Action<bool> set)
        {
            UiKit.CreateText(root, label, 32, TextAnchor.MiddleLeft,
                new Vector2(0.31f, y), new Vector2(0.46f, y + 0.10f));

            var track = UiKit.CreatePanel(root, Track);
            track.sprite = UiArt.Chip;
            track.type = Image.Type.Sliced;
            UiKit.Anchor(track.rectTransform, new Vector2(0.495f, y - 0.008f), new Vector2(0.695f, y + 0.108f));

            Button off = null, on = null;

            void Paint()
            {
                var isOn = get();
                PaintSegment(off, !isOn);
                PaintSegment(on, isOn);
            }

            off = SegmentButton(root, "끔", new Vector2(0.503f, y), new Vector2(0.593f, y + 0.092f),
                () => { set(false); Paint(); });
            on = SegmentButton(root, "켬", new Vector2(0.597f, y), new Vector2(0.687f, y + 0.092f),
                () => { set(true); Paint(); });
            Paint();
        }

        private static Button SegmentButton(Transform root, string label, Vector2 min, Vector2 max,
            System.Action onClick)
        {
            var btn = UiKit.CreateButton(root, label, min, max, () => onClick(), 26);
            btn.GetComponent<Image>().sprite = UiArt.Chip;
            return btn;
        }

        private static void PaintSegment(Button btn, bool selected)
        {
            btn.GetComponent<Image>().color = selected ? UiKit.Accent : new Color(0f, 0f, 0f, 0f);
            var text = btn.GetComponentInChildren<Text>();
            text.color = selected ? Ink : new Color(1f, 1f, 1f, 0.55f);
            text.fontStyle = selected ? FontStyle.Bold : FontStyle.Normal;
        }

        private void ShowRules() =>
            _notice.text = "1~10 두 벌(48장)로 3장씩 묶어 손패를 비우면 이깁니다.\n" +
                           "같은 숫자 3장 또또또, 같은 무늬 연속 3장 스트레이트.\n" +
                           "자세한 규칙은 로비의 튜토리얼에서 볼 수 있어요.";

        private void ShowTerms() =>
            _notice.text = "포인트는 게임 안에서만 쓰는 재화이며 환전되지 않습니다.\n" +
                           "전체 약관과 개인정보처리방침은 출시 시 앱 안에서 제공됩니다.";

        /// <summary>오버레이를 먼저 걷고 테이블의 나가기 확인 모달로 넘긴다.</summary>
        private void ExitGame()
        {
            var exit = UiKit.ExitGameAction;
            Close();
            exit?.Invoke();
        }

        /// <summary>오버레이만 걷어낸다 — 뒤에 있던 화면은 그대로 살아 있다.</summary>
        private void Close()
        {
            UiKit.BackAction = UiKit.PreviousBackAction;
            Destroy(_canvas);
            Destroy(gameObject);
        }
    }
}
