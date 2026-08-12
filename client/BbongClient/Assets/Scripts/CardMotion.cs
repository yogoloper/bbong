using UnityEngine;
using UnityEngine.EventSystems;

namespace Bbong.Client
{
    /// <summary>
    /// 손패 카드 생동감(발라트로풍): 유휴 시 카드별 위상이 다른 미세한 좌우 흔들림,
    /// 포인터 오버 시 확대, 누르면 움츠렸다 복귀. HorizontalLayoutGroup과 충돌하지 않도록
    /// 위치는 건드리지 않고 회전/스케일만 움직인다.
    /// </summary>
    public sealed class CardMotion : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        private float _phase;
        private float _targetScale = 1f;
        private float _scale = 1f;
        private bool _hovered;

        /// <summary>
        /// 터치는 손을 떼도 Exit가 오지 않는 경우가 있어 확대가 그대로 남는다(카드 한 장만 커진 채 흔들림도 멈춤).
        /// 마우스에만 호버 개념을 적용하고, 터치는 누르는 동안만 반응하게 한다.
        /// </summary>
        private static bool IsTouch(PointerEventData eventData) => eventData.pointerId >= 0;

        private void Start() => _phase = transform.GetSiblingIndex() * 0.9f;

        private void Update()
        {
            var sway = _hovered ? 0f : Mathf.Sin(Time.time * 1.7f + _phase) * 2.2f;
            transform.localRotation = Quaternion.Euler(0f, 0f, sway);
            _scale = Mathf.Lerp(_scale, _targetScale, Time.deltaTime * 14f);
            transform.localScale = Vector3.one * _scale;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (IsTouch(eventData))
            {
                return;
            }

            _hovered = true;
            _targetScale = 1.12f;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hovered = false;
            _targetScale = 1f;
        }

        public void OnPointerDown(PointerEventData eventData) => _scale = 0.92f;

        public void OnPointerUp(PointerEventData eventData)
        {
            if (IsTouch(eventData))
            {
                _hovered = false; // 손을 떼면 즉시 평상 상태로
            }

            _targetScale = _hovered ? 1.12f : 1f;
        }
    }
}
