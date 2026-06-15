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
            var (canvas, root) = UiKit.CreateScreen("ShopCanvas");
            _canvas = canvas;

            UiKit.CreateText(root, "상점", 64, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.86f), new Vector2(0.9f, 0.97f)).fontStyle = FontStyle.Bold;

            _balance = UiKit.CreateText(root, "", 44, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.74f), new Vector2(0.9f, 0.83f));
            _balance.color = new Color(0.5f, 0.95f, 0.6f);

            _standardBtn = UiKit.CreateButton(root, "광고 보고 2,000P",
                new Vector2(0.3f, 0.54f), new Vector2(0.7f, 0.64f), OnStandard, 38);
            _bankruptBtn = UiKit.CreateButton(root, "구제 광고 10,000P (잔액 부족 시)",
                new Vector2(0.3f, 0.41f), new Vector2(0.7f, 0.51f), OnBankrupt, 30);

            UiKit.CreateButton(root, "뒤로", new Vector2(0.03f, 0.03f), new Vector2(0.15f, 0.11f), Back, 32);

            _status = UiKit.CreateText(root, "", 28, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.26f), new Vector2(0.9f, 0.36f));
            _status.color = new Color(1f, 0.8f, 0.5f);

            RenderBalance();
        }

        private void RenderBalance() => _balance.text = $"{Session.Balance:N0} 포인트";

        private void OnStandard() => Claim("Standard", "광고 시청 완료 — 2,000P 적립!");
        private void OnBankrupt() => Claim("Bankruptcy", "구제 완료 — 10,000P 적립!");

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
