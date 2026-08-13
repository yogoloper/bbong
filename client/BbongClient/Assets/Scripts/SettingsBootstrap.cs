using UnityEngine;
using UnityEngine.UI;

namespace Bbong.Client
{
    /// <summary>
    /// 설정 오버레이: 소리·진동 토글과 약관·규칙 안내. 화면을 갈아엎지 않고 위에 덮는데,
    /// 방에 들어가 있는 화면에서 캔버스를 버리면 연결만 남고 상태가 어긋나기 때문이다.
    /// 덕분에 친구방·매칭 대기·게임 중에도 소리를 끌 수 있다.
    /// </summary>
    public sealed class SettingsBootstrap : MonoBehaviour
    {
        private const int SortOrder = 500; // 게임 테이블·모달보다 위

        private GameObject _canvas;
        private Text _notice;

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

            // 뒤 화면을 어둡게 덮고, 바깥을 눌러도 닫히게 — 뒤 버튼이 눌리는 사고를 막는다
            var scrim = UiKit.CreatePanel(canvasGo.transform, new Color(0f, 0f, 0f, 0.72f));
            UiKit.Stretch(scrim.rectTransform);
            var scrimBtn = scrim.gameObject.AddComponent<Button>();
            scrimBtn.transition = Selectable.Transition.None;
            scrimBtn.onClick.AddListener(Close);

            var root = SafeArea.Wrap(canvasGo.transform);

            var sheet = UiKit.CreatePanel(root, new Color(0.10f, 0.16f, 0.32f, 0.99f));
            if (UiArt.Panel9 != null)
            {
                sheet.sprite = UiArt.Panel9;
                sheet.type = Image.Type.Sliced;
            }

            UiKit.Anchor(sheet.rectTransform, new Vector2(0.24f, 0.10f), new Vector2(0.76f, 0.92f));
            sheet.gameObject.AddComponent<Button>().transition = Selectable.Transition.None; // 시트 클릭은 안 닫히게

            UiKit.CreateText(root, "설정", 48, TextAnchor.MiddleCenter,
                new Vector2(0.24f, 0.80f), new Vector2(0.76f, 0.89f)).fontStyle = FontStyle.Bold;

            Toggle(root, "소리", 0.63f, () => AppSettings.SoundOn, on => AppSettings.SoundOn = on);
            Toggle(root, "진동", 0.49f, () => AppSettings.VibrationOn, on =>
            {
                AppSettings.VibrationOn = on;
                AppSettings.Vibrate(); // 켜는 순간 한 번 울려 확인시킨다
            });

            // 규칙·약관은 앱 안에서 열어야 한다(스토어 정책상 외부 링크만 두면 반려 사유가 된다)
            UiKit.CreateButton(root, "게임 규칙", new Vector2(0.27f, 0.30f), new Vector2(0.485f, 0.41f),
                ShowRules, 30);
            UiKit.CreateButton(root, "이용약관 · 개인정보", new Vector2(0.515f, 0.30f), new Vector2(0.73f, 0.41f),
                ShowTerms, 28);

            _notice = UiKit.CreateText(root, "", 24, TextAnchor.UpperCenter,
                new Vector2(0.26f, 0.15f), new Vector2(0.74f, 0.29f));
            _notice.color = new Color(1f, 1f, 1f, 0.7f);

            // 턴 타이머는 서버가 돌린다. 오버레이를 열어도 판은 안 멈추니 미리 알려준다.
            if (FindAnyObjectByType<GameTableView>() != null)
            {
                _notice.text = "판은 계속 진행돼요. 내 차례를 놓치지 않게 곧 닫아주세요.";
                _notice.color = new Color(1f, 0.8f, 0.5f);
            }

            UiKit.CreateButton(root, "닫기", new Vector2(0.42f, 0.115f), new Vector2(0.58f, 0.19f), Close, 30);
            UiKit.BackAction = Close; // 기기 뒤로가기는 오버레이만 닫는다
        }

        /// <summary>선택 상태가 색 반전으로 드러나는 좌/우 2지 토글.</summary>
        private void Toggle(Transform root, string label, float y, System.Func<bool> get, System.Action<bool> set)
        {
            var rowBg = UiKit.CreatePanel(root, new Color(0f, 0f, 0f, 0.28f));
            if (UiArt.Panel9 != null)
            {
                rowBg.sprite = UiArt.Panel9;
                rowBg.type = Image.Type.Sliced;
            }

            UiKit.Anchor(rowBg.rectTransform, new Vector2(0.27f, y - 0.012f), new Vector2(0.73f, y + 0.112f));

            UiKit.CreateText(root, label, 34, TextAnchor.MiddleLeft,
                new Vector2(0.30f, y), new Vector2(0.46f, y + 0.10f));

            Button off = null, on = null;

            void Paint()
            {
                var isOn = get();
                off.GetComponent<Image>().color = isOn ? new Color(0.16f, 0.24f, 0.42f) : UiKit.Accent;
                off.GetComponentInChildren<Text>().color = isOn ? Color.white : Color.black;
                on.GetComponent<Image>().color = isOn ? UiKit.Accent : new Color(0.16f, 0.24f, 0.42f);
                on.GetComponentInChildren<Text>().color = isOn ? Color.black : Color.white;
            }

            off = UiKit.CreateButton(root, "끔", new Vector2(0.485f, y), new Vector2(0.595f, y + 0.10f),
                () => { set(false); Paint(); }, 30);
            on = UiKit.CreateButton(root, "켬", new Vector2(0.605f, y), new Vector2(0.715f, y + 0.10f),
                () => { set(true); Paint(); }, 30);
            Paint();
        }

        private void ShowRules() =>
            _notice.text = "1~10 두 벌(48장)로 3장씩 묶어 손패를 비우면 이깁니다.\n" +
                           "같은 숫자 3장 또또또, 같은 무늬 연속 3장 스트레이트.\n" +
                           "자세한 규칙은 로비의 튜토리얼에서 볼 수 있어요.";

        private void ShowTerms() =>
            _notice.text = "포인트는 게임 안에서만 쓰는 재화이며 환전되지 않습니다.\n" +
                           "전체 약관과 개인정보처리방침은 출시 시 앱 안에서 제공됩니다.";

        /// <summary>오버레이만 걷어낸다 — 뒤에 있던 화면은 그대로 살아 있다.</summary>
        private void Close()
        {
            UiKit.BackAction = UiKit.PreviousBackAction;
            Destroy(_canvas);
            Destroy(gameObject);
        }
    }
}
