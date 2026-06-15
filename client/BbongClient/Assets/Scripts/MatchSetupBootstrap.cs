using BbongCore.Config;
using UnityEngine;
using UnityEngine.UI;

namespace Bbong.Client
{
    /// <summary>맞춤게임 설정(인원·입장료 선택). 매칭/게임서버는 Phase 5 — 지금은 흐름 placeholder.</summary>
    public sealed class MatchSetupBootstrap : MonoBehaviour
    {
        private static readonly Color Selected = new(1f, 0.85f, 0.3f);
        private GameObject _canvas;
        private int _players = 4;
        private int _stake = 1000;
        private Text _status;

        private void Start()
        {
            UiKit.EnsureEventSystem();
            Build();
        }

        private void Build()
        {
            var (canvas, root) = UiKit.CreateScreen("MatchSetupCanvas");
            _canvas = canvas;

            UiKit.CreateText(root, "맞춤게임", 64, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.86f), new Vector2(0.9f, 0.97f)).fontStyle = FontStyle.Bold;

            UiKit.CreateText(root, "인원", 36, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.72f), new Vector2(0.9f, 0.78f));
            for (var i = 0; i < GameConfig.MaxPlayers - GameConfig.MinPlayers + 1; i++)
            {
                var n = GameConfig.MinPlayers + i;
                var x0 = 0.2f + i * 0.1f;
                UiKit.CreateButton(root, $"{n}", new Vector2(x0, 0.62f), new Vector2(x0 + 0.08f, 0.71f),
                    () => { _players = n; }, 34);
            }

            UiKit.CreateText(root, "입장료", 36, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.50f), new Vector2(0.9f, 0.56f));
            for (var i = 0; i < GameConfig.StakeOptions.Count; i++)
            {
                var s = GameConfig.StakeOptions[i];
                var x0 = 0.12f + i * 0.13f;
                UiKit.CreateButton(root, $"{s:N0}", new Vector2(x0, 0.40f), new Vector2(x0 + 0.12f, 0.49f),
                    () => { _stake = s; }, 28);
            }

            UiKit.CreateButton(root, "매칭 시작", new Vector2(0.34f, 0.22f), new Vector2(0.66f, 0.32f), OnMatch, 44);
            UiKit.CreateButton(root, "뒤로", new Vector2(0.03f, 0.03f), new Vector2(0.15f, 0.11f), Back, 32);

            _status = UiKit.CreateText(root, "", 30, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.12f), new Vector2(0.9f, 0.2f));
            _status.color = new Color(1f, 0.8f, 0.5f);
        }

        private void OnMatch() =>
            _status.text = $"매칭 대기 중... ({_players}인 · {_stake:N0})\n(온라인 매칭은 Phase 5에서 구현)";

        private void Back() => UiKit.GoTo<MainLobbyBootstrap>(_canvas, this);
    }
}
