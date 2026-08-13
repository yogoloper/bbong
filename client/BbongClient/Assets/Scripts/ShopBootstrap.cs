using UnityEngine;
using UnityEngine.UI;

namespace Bbong.Client
{
    /// <summary>상점: 광고 보상(서버 연동). 일반 2000P/30분, 구제 10000P(잔액 부족 시).</summary>
    public sealed class ShopBootstrap : MonoBehaviour
    {
        private GameObject _canvas;
        private Text _balance;
        private Text _status;
        private Button _standardBtn;
        private Button _bankruptBtn;

        private void Start()
        {
            UiKit.EnsureEventSystem();
            Build();
        }

        private void Build()
        {
            var (canvas, root) = UiKit.CreateScreen("ShopCanvas", topBar: true);
            _canvas = canvas;

            UiKit.CreateText(root, "포인트 얻기", 60, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.76f), new Vector2(0.9f, 0.86f)).fontStyle = FontStyle.Bold;

            // 광고 카드 2개(루미큐브 코인 카드 레이아웃 차용)
            AdCard(root, "광고 보고\n2,000P", "30분마다", new Vector2(0.24f, 0.34f), new Vector2(0.49f, 0.68f), OnStandard, out _standardBtn);
            AdCard(root, "구제 광고\n10,000P", "포인트 떨어졌을 때 · 하루 3번", new Vector2(0.51f, 0.34f), new Vector2(0.76f, 0.68f), OnBankrupt, out _bankruptBtn);

            UiKit.BackButton(root, Back);

            _balance = UiKit.CreateText(root, "", 36, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.7f), new Vector2(0.9f, 0.75f));
            _balance.color = UiKit.Accent;

            _status = UiKit.CreateText(root, "", 28, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.22f), new Vector2(0.9f, 0.3f));
            _status.color = new Color(1f, 0.8f, 0.5f);

            RenderBalance();
        }

        /// <summary>광고 카드: 제목 + 조건. 클릭 = 보상 수령.</summary>
        private void AdCard(Transform root, string title, string sub, Vector2 min, Vector2 max,
            UnityEngine.Events.UnityAction onClick, out Button btn)
        {
            btn = UiKit.CreateButton(root, "", min, max, onClick);
            btn.GetComponent<Image>().color = new Color(0.12f, 0.22f, 0.42f, 0.95f);
            var t = UiKit.CreateText(btn.transform, title, 40, TextAnchor.MiddleCenter,
                new Vector2(0f, 0.38f), new Vector2(1f, 0.92f));
            t.color = Color.white;
            t.fontStyle = FontStyle.Bold;
            var s = UiKit.CreateText(btn.transform, sub, 22, TextAnchor.MiddleCenter,
                new Vector2(0f, 0.1f), new Vector2(1f, 0.34f));
            s.color = new Color(1f, 1f, 1f, 0.6f);
        }

        private void RenderBalance() => _balance.text = $"보유 {Session.Balance:N0} P";

        private void OnStandard() => Claim("Standard", "2,000P 받았어요!");
        private void OnBankrupt() => Claim("Bankruptcy", "10,000P 받았어요!");

        private void Claim(string kind, string okMsg)
        {
            SetButtons(false);
            _status.text = "광고 시청 중...";
            StartCoroutine(ServerApi.ClaimAdReward(kind,
                _ => { RenderBalance(); _status.text = okMsg; SetButtons(true); },
                err => { _status.text = err; SetButtons(true); }));
        }

        private void SetButtons(bool on)
        {
            _standardBtn.interactable = on;
            _bankruptBtn.interactable = on;
        }

        private void Back() => UiKit.GoTo<MainLobbyBootstrap>(_canvas, this);
    }
}
