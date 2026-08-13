using UnityEngine;

namespace Bbong.Client
{
    /// <summary>
    /// 붙은 RectTransform을 기기의 안전 영역(Screen.safeArea) 중 세로 방향에만 맞춘다.
    /// 가로는 화면 끝까지 쓴다 — 좌우 인셋은 대개 한쪽에만 잡혀서, 따르면 화면이 쏠리고
    /// 양쪽에 맞추면 쓸 수 있는 폭이 통째로 줄어든다.
    /// 배경은 이 안에 두지 않는다 — 화면 끝까지 채워야 검은 띠가 안 생긴다.
    /// </summary>
    internal sealed class SafeArea : MonoBehaviour
    {
        private Rect _applied;
        private ScreenOrientation _orientation;

        /// <summary>화면 전체를 덮는 안전 영역 루트 생성. 이후 UI는 이 트랜스폼 밑에 만든다.</summary>
        public static Transform Wrap(Transform parent)
        {
            var go = new GameObject("SafeArea", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            UiKit.Stretch((RectTransform)go.transform);
            go.AddComponent<SafeArea>();
            return go.transform;
        }

        private void Start() => Apply();

        private void Update()
        {
            // 회전·멀티윈도·소프트키 표시로 안전 영역이 바뀔 수 있어 매 프레임 값만 비교한다
            if (Screen.safeArea != _applied || Screen.orientation != _orientation)
            {
                Apply();
            }
        }

        private void Apply()
        {
            var width = Screen.width;
            var height = Screen.height;
            if (width <= 0 || height <= 0)
            {
                return;
            }

            var area = Screen.safeArea;
            _applied = area;
            _orientation = Screen.orientation;

            // 가로는 화면 끝까지 쓴다. 안전 영역을 그대로 따르면 인셋이 한쪽에만 잡혀 화면이 쏠리고,
            // 양쪽을 큰 쪽에 맞추면 좌우가 통째로 잘려 나간다 — 상단바·톱니·인게임 모두 끝에 붙는 편이
            // 낫다는 판단이다. 세로만 큰 쪽 여백을 양쪽에 맞춰 대칭으로 남긴다.
            var vertical = Mathf.Max(area.yMin, height - area.yMax);

            var rt = (RectTransform)transform;
            rt.anchorMin = new Vector2(0f, vertical / height);
            rt.anchorMax = new Vector2(1f, (height - vertical) / height);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
