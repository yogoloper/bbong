using System.Linq;
using BbongCore.Ai;
using BbongCore.Config;
using UnityEngine;
using UnityEngine.UI;

namespace Bbong.Client
{
    /// <summary>
    /// 로비/방 만들기 화면(architecture §4-2의 로컬 싱글 축소판, §4-4).
    /// 인원(2~6)·봇 난이도를 고르고 게임 시작 → GameTableBootstrap 생성. 연습은 판돈 없음(무료).
    /// 빈 GameObject에 이 컴포넌트 하나 붙이고 Play. 게임 종료 화면의 '로비로'로 복귀.
    /// 화면 뼈대는 맞춤게임 설정과 한 벌이다 — 같은 일(인원 고르고 시작)을 하는 화면이라
    /// 제목 높이·라벨 크기·칩 그리드를 UiKit 하나에서 받아 쓴다.
    /// </summary>
    public sealed class LobbyBootstrap : MonoBehaviour
    {
        // 봇 난이도 표시명(쉬움/보통/어려움)
        private static readonly (BotDifficulty value, string label)[] Difficulties =
        {
            (BotDifficulty.Easy, "쉬움"), (BotDifficulty.Normal, "보통"), (BotDifficulty.Hard, "어려움")
        };

        private int _playerCount = 4;
        private BotDifficulty _difficulty = BotDifficulty.Normal;

        private GameObject _canvasGo;
        private Text _summary;
        private Button[] _playerChoices;
        private Button[] _difficultyChoices;

        private void Start()
        {
            UiKit.EnsureEventSystem();
            UiKit.DestroyStrayTables(); // 연습 설정에 들어온 시점엔 이전 판이 남아 있으면 안 됨
            BuildUi();
            RefreshSelection();
        }

        private bool _started;

        private void OnStartGame()
        {
            if (!GameConfig.IsValidPlayerCount(_playerCount) || _started)
            {
                return;
            }

            _started = true; // 더블클릭으로 테이블이 2개 생기는 것 방지

            // 연습은 판돈 없음(무료). 봇 인원·난이도만 적용.
            var table = new GameObject("GameTable").AddComponent<GameTableBootstrap>();
            table.PlayerCount = _playerCount;
            table.Difficulty = _difficulty;
            Destroy(_canvasGo);
            Destroy(gameObject);
        }

        // ── UI 생성 ──

        private void BuildUi()
        {
            var (canvas, root) = UiKit.CreateScreen("LobbyCanvas", topBar: true);
            _canvasGo = canvas;

            UiKit.CreateText(root, "연습 게임", 56, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.78f), new Vector2(0.9f, 0.87f)).fontStyle = FontStyle.Bold;

            UiKit.CreateText(root, "봇 상대로 부담 없이 한 판, 포인트는 안 걸어요", 26, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.735f), new Vector2(0.9f, 0.775f)).color = UiKit.TextSub;

            // 라벨-칩 간격 < 그룹 간 간격(약 1:2) — 라벨이 아래 칩 무리로 묶여 읽히게 한다
            UiKit.SectionLabel(root, "인원", 0.66f, 0.71f);
            var counts = Enumerable.Range(GameConfig.MinPlayers, GameConfig.MaxPlayers - GameConfig.MinPlayers + 1)
                .ToArray();
            _playerChoices = UiKit.ChoiceRow(root, counts.Select(n => $"{n}명").ToArray(), 0.515f, 0.637f, 0.09f,
                i => { _playerCount = counts[i]; RefreshSelection(); });

            UiKit.SectionLabel(root, "봇 난이도", 0.425f, 0.475f);
            _difficultyChoices = UiKit.ChoiceRow(root, Difficulties.Select(d => d.label).ToArray(),
                0.28f, 0.402f, 0.09f,
                i => { _difficulty = Difficulties[i].value; RefreshSelection(); });

            _summary = UiKit.CreateText(root, "", 34, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.22f), new Vector2(0.9f, 0.268f));
            _summary.color = UiKit.Accent;
            _summary.fontStyle = FontStyle.Bold;

            UiKit.PrimaryCta(root, "게임 시작", OnStartGame);
            UiKit.BackButton(root, OnBack);
        }

        /// <summary>메인 로비로 돌아가기(연습 설정 취소).</summary>
        private void OnBack() => UiKit.GoTo<MainLobbyBootstrap>(_canvasGo, this);

        private void RefreshSelection()
        {
            for (var i = 0; i < _playerChoices.Length; i++)
            {
                UiKit.PaintChoice(_playerChoices[i], GameConfig.MinPlayers + i == _playerCount);
            }

            for (var i = 0; i < _difficultyChoices.Length; i++)
            {
                UiKit.PaintChoice(_difficultyChoices[i], Difficulties[i].value == _difficulty);
            }

            var label = Difficulties[System.Array.FindIndex(Difficulties, d => d.value == _difficulty)].label;
            _summary.text = $"나 + 봇 {_playerCount - 1}  ·  난이도 {label}";
        }
    }
}
