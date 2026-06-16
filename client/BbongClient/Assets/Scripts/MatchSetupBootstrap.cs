using System;
using System.Collections.Generic;
using BbongCore.Config;
using UnityEngine;
using UnityEngine.UI;

namespace Bbong.Client
{
    /// <summary>맞춤게임 설정(인원·입장료, 선택 강조). 매칭/게임서버는 Phase 5 — 흐름 placeholder.</summary>
    public sealed class MatchSetupBootstrap : MonoBehaviour
    {
        private static readonly Color Selected = new(1f, 0.85f, 0.3f);
        private static readonly Color Unselected = new(0.95f, 0.95f, 0.95f);

        private GameObject _canvas;
        private int _players = 4;
        private int _stake = 1000;
        private Text _prize;
        private Text _status;
        private readonly List<(int value, Button button)> _playerChoices = new();
        private readonly List<(int value, Button button)> _stakeChoices = new();

        private void Start()
        {
            UiKit.EnsureEventSystem();
            Build();
            RefreshSelection();
        }

        private void Build()
        {
            var (canvas, root) = UiKit.CreateScreen("MatchSetupCanvas", topBar: true);
            _canvas = canvas;

            UiKit.CreateText(root, "맞춤게임", 56, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.78f), new Vector2(0.9f, 0.87f)).fontStyle = FontStyle.Bold;

            UiKit.CreateText(root, "인원", 36, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.69f), new Vector2(0.9f, 0.74f));
            var playerCount = GameConfig.MaxPlayers - GameConfig.MinPlayers + 1;
            PlaceChoices(root, 0.58f, 0.67f, 0.09f, playerCount,
                i => GameConfig.MinPlayers + i, n => $"{n}", _playerChoices, v => _players = v);

            UiKit.CreateText(root, "입장료", 36, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.5f), new Vector2(0.9f, 0.55f));
            PlaceChoices(root, 0.39f, 0.48f, 0.1f, GameConfig.StakeOptions.Count,
                i => GameConfig.StakeOptions[i], s => $"{s:N0}", _stakeChoices, v => _stake = v);

            _prize = UiKit.CreateText(root, "", 38, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.3f), new Vector2(0.9f, 0.37f));
            _prize.color = UiKit.Accent;
            _prize.fontStyle = FontStyle.Bold;

            _status = UiKit.CreateText(root, "", 28, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.2f), new Vector2(0.9f, 0.28f));
            _status.color = new Color(1f, 0.8f, 0.5f);

            UiKit.PrimaryCta(root, "매칭 시작", OnMatch);
            UiKit.BackButton(root, Back);
        }

        /// <summary>선택지 버튼들을 가로 가운데 정렬로 배치 + 선택 강조 등록.</summary>
        private void PlaceChoices(Transform root, float y0, float y1, float w, int count,
            Func<int, int> valueAt, Func<int, string> format,
            List<(int value, Button button)> registry, Action<int> onPick)
        {
            const float gap = 0.012f;
            var start = 0.5f - (count * w + (count - 1) * gap) / 2f;
            for (var i = 0; i < count; i++)
            {
                var v = valueAt(i);
                var x0 = start + i * (w + gap);
                var btn = UiKit.CreateButton(root, format(v), new Vector2(x0, y0), new Vector2(x0 + w, y1),
                    () => { onPick(v); RefreshSelection(); }, 28);
                registry.Add((v, btn));
            }
        }

        private void RefreshSelection()
        {
            foreach (var (value, button) in _playerChoices)
            {
                button.GetComponent<Image>().color = value == _players ? Selected : Unselected;
            }

            foreach (var (value, button) in _stakeChoices)
            {
                button.GetComponent<Image>().color = value == _stake ? Selected : Unselected;
            }

            // winner-takes-all → 총상금 = 입장료 × 인원
            _prize.text = $"총상금 {(_stake * (long)_players):N0}";
        }

        private void OnMatch() =>
            _status.text = $"매칭 대기 중... ({_players}인 · {_stake:N0})\n(온라인 매칭은 Phase 5에서 구현)";

        private void Back() => UiKit.GoTo<MainLobbyBootstrap>(_canvas, this);
    }
}
