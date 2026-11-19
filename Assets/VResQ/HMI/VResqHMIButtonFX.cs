using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VResQ.HMI
{
    /// <summary>
    /// Tactile Human-Machine micro-interaction component attached to interactive HUD buttons.
    /// Provides smooth hover scale/brightness elevation, press punch, and audio/haptic ticks
    /// for Mouse, Meta Quest VR Ray, VR Poke, and Gamepad navigation.
    /// </summary>
    [DisallowMultipleComponent]
    public class VResqHMIButtonFX : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler,
        ISelectHandler, IDeselectHandler
    {
        public float hoverScale = 1.045f;
        public float pressScale = 0.95f;
        public float animSpeed = 16f;

        private Button _button;
        private Graphic _graphic;
        private Vector3 _baseScale = Vector3.one;
        private bool _baseInitialized;
        private bool _hovered;
        private bool _pressed;

        private void Awake()
        {
            _button = GetComponent<Button>();
            _graphic = _button != null && _button.targetGraphic != null
                ? _button.targetGraphic
                : GetComponent<Graphic>();
            CaptureBase();
        }

        private void OnEnable()
        {
            _hovered = false;
            _pressed = false;
            CaptureBase();
            transform.localScale = _baseScale;
        }

        private void OnDisable()
        {
            _hovered = false;
            _pressed = false;
            if (_baseInitialized) transform.localScale = _baseScale;
        }

        private void CaptureBase()
        {
            if (_baseInitialized) return;
            _baseScale = transform.localScale;
            if (_baseScale.sqrMagnitude < 0.0001f) _baseScale = Vector3.one;
            _baseInitialized = true;
        }

        private void Update()
        {
            if (!_baseInitialized) return;
            float mult = _pressed ? pressScale : (_hovered ? hoverScale : 1f);
            Vector3 target = _baseScale * mult;
            if ((transform.localScale - target).sqrMagnitude > 0.00001f)
            {
                transform.localScale = Vector3.Lerp(transform.localScale, target, Mathf.Clamp01(Time.unscaledDeltaTime * animSpeed));
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_button != null && !_button.interactable) return;
            _hovered = true;
            VResqHMIFeedback.Instance?.PlayHoverTick();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hovered = false;
            _pressed = false;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_button != null && !_button.interactable) return;
            _pressed = true;
            VResqHMIFeedback.Instance?.PlayClickConfirm();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _pressed = false;
        }

        public void OnSelect(BaseEventData eventData)
        {
            if (_button != null && !_button.interactable) return;
            _hovered = true;
        }

        public void OnDeselect(BaseEventData eventData)
        {
            _hovered = false;
            _pressed = false;
        }
    }
}
