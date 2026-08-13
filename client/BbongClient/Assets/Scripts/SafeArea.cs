using UnityEngine;

namespace Bbong.Client
{
    /// <summary>
    /// 붙은 RectTransform을 기기의 안전 영역(Screen.safeArea)에 맞춘다.
    /// 노치·펀치홀·제스처 바가 있는 기기에서 상단바나 모서리 버튼이 잘리는 것을 막는다.
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

            // 노치·제스처 바는 대개 한쪽에만 있어서 안전 영역을 그대로 쓰면 화면이 한쪽으로 쏠린다.
            // 큰 쪽 여백을 양쪽에 똑같이 줘서 가운데 정렬을 유지한다 — 좌우 대칭이 무너지면
            // 화면 끝에 붙는 요소(설정 톱니, 잔액)가 한쪽만 모서리에 처박힌 것처럼 보인다.
            var side = Mathf.Max(area.xMin, width - area.xMax);
            var vertical = Mathf.Max(area.yMin, height - area.yMax);

            var rt = (RectTransform)transform;
            rt.anchorMin = new Vector2(side / width, vertical / height);
            rt.anchorMax = new Vector2((width - side) / width, (height - vertical) / height);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
