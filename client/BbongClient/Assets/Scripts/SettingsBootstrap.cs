using UnityEngine;
using UnityEngine.UI;

namespace Bbong.Client
{
    /// <summary>
    /// 설정: 소리·진동 토글과 약관·규칙 안내. 소리를 못 끄는 게임은 공공장소에서 켜기 어렵고,
    /// 약관·개인정보 고지는 스토어 등록에 필요하다.
    /// </summary>
    public sealed class SettingsBootstrap : MonoBehaviour
    {
        private GameObject _canvas;
        private Text _notice;

        private void Start()
        {
            UiKit.EnsureEventSystem();
            Build();
        }

        private void Build()
        {
            var (canvas, root) = UiKit.CreateScreen("SettingsCanvas", topBar: true, settingsEntry: false);
            _canvas = canvas;

            UiKit.CreateText(root, "설정", 56, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.78f), new Vector2(0.9f, 0.87f)).fontStyle = FontStyle.Bold;

            Toggle(root, "소리", 0.60f, () => AppSettings.SoundOn, on => AppSettings.SoundOn = on);
            Toggle(root, "진동", 0.44f, () => AppSettings.VibrationOn, on =>
            {
                AppSettings.VibrationOn = on;
                AppSettings.Vibrate(); // 켜는 순간 한 번 울려 확인시킨다
            });

            // 규칙·약관은 앱 안에서 열어야 한다(스토어 정책상 외부 링크만 두면 반려 사유가 된다)
            UiKit.CreateButton(root, "게임 규칙", new Vector2(0.20f, 0.24f), new Vector2(0.47f, 0.36f),
                () => UiKit.GoTo<TutorialBootstrap>(_canvas, this), 32);
            UiKit.CreateButton(root, "이용약관 · 개인정보", new Vector2(0.53f, 0.24f), new Vector2(0.80f, 0.36f),
                ShowTerms, 30);

            _notice = UiKit.CreateText(root, "", 26, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.13f), new Vector2(0.9f, 0.22f));
            _notice.color = new Color(1f, 1f, 1f, 0.7f);

            UiKit.BackButton(root, () => UiKit.GoTo<MainLobbyBootstrap>(_canvas, this));
        }

        /// <summary>선택 상태가 색 반전으로 드러나는 좌/우 2지 토글(설정 화면 공통 형태).</summary>
        private void Toggle(Transform root, string label, float y, System.Func<bool> get, System.Action<bool> set)
        {
            // 라벨과 토글이 화면 양 끝으로 벌어져 한 줄로 안 읽히던 문제 — 행 배경으로 묶는다
            var rowBg = UiKit.CreatePanel(root, new Color(0f, 0f, 0f, 0.28f));
            if (UiArt.Panel9 != null)
            {
                rowBg.sprite = UiArt.Panel9;
                rowBg.type = Image.Type.Sliced;
            }

            UiKit.Anchor(rowBg.rectTransform, new Vector2(0.20f, y - 0.012f), new Vector2(0.80f, y + 0.112f));

            UiKit.CreateText(root, label, 36, TextAnchor.MiddleLeft,
                new Vector2(0.235f, y), new Vector2(0.50f, y + 0.10f));

            Button off = null, on = null;

            void Paint()
            {
                var isOn = get();
                off.GetComponent<Image>().color = isOn ? new Color(0.16f, 0.24f, 0.42f) : UiKit.Accent;
                off.GetComponentInChildren<Text>().color = isOn ? Color.white : Color.black;
                on.GetComponent<Image>().color = isOn ? UiKit.Accent : new Color(0.16f, 0.24f, 0.42f);
                on.GetComponentInChildren<Text>().color = isOn ? Color.black : Color.white;
            }

            off = UiKit.CreateButton(root, "끔", new Vector2(0.545f, y), new Vector2(0.655f, y + 0.10f),
                () => { set(false); Paint(); }, 30);
            on = UiKit.CreateButton(root, "켬", new Vector2(0.665f, y), new Vector2(0.775f, y + 0.10f),
                () => { set(true); Paint(); }, 30);
            Paint();
        }

        private void ShowTerms() =>
            _notice.text = "포인트는 게임 안에서만 쓰는 재화이며 환전되지 않습니다.\n" +
                           "전체 약관과 개인정보처리방침은 출시 시 앱 안에서 제공됩니다.";
    }
}
