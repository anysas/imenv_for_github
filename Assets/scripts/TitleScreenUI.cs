using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AlgorithmicGallery.Corruption
{
    public class TitleScreenUI : MonoBehaviour
    {
        [Header("Copy")]
        [SerializeField] private string _title = "Gallery Design Workshop Simulation:";
        [SerializeField] private string _subtitle = "Arranging Marketable Dioramas for Audience Engagement and Retention Utilizing Your Unique Creativity";

        [Header("Timings")]
        [SerializeField] private float _fadeOutDuration = 1.0f;

        private CanvasGroup _cg;
        private bool _dismissed;

        void Awake()
        {
            Build();
        }

        void Start()
        {
            PlayerInputFreeze.FreezePlayerLocomotion();

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Build()
        {
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var esGO = new GameObject("EventSystem");
                esGO.AddComponent<EventSystem>();
                esGO.AddComponent<StandaloneInputModule>();
            }

            var canvasGO = new GameObject("TitleScreenCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 300;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasGO.AddComponent<GraphicRaycaster>();

            _cg = canvasGO.AddComponent<CanvasGroup>();
            _cg.alpha = 1f;
            _cg.blocksRaycasts = true;
            _cg.interactable = true;

            var bg = new GameObject("Bg");
            bg.transform.SetParent(canvasGO.transform, false);
            var bgRect = bg.AddComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(0f, 0f, 0f, 0f);
            bgImg.raycastTarget = false;

            var titleGO = new GameObject("Title");
            titleGO.transform.SetParent(canvasGO.transform, false);
            var titleRect = titleGO.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(0f, 1f);
            titleRect.pivot = new Vector2(0f, 1f);
            titleRect.anchoredPosition = new Vector2(28f, -24f);
            titleRect.sizeDelta = new Vector2(1864f, 220f);
            var titleText = titleGO.AddComponent<Text>();
            titleText.font = UiFontResolver.LoadVt323OrFallback();
            titleText.fontSize = 118;
            titleText.fontStyle = FontStyle.Bold;
            titleText.alignment = TextAnchor.UpperLeft;
            titleText.color = new Color(1f, 0.55f, 0.2f, 1f);
            titleText.horizontalOverflow = HorizontalWrapMode.Wrap;
            titleText.verticalOverflow = VerticalWrapMode.Overflow;
            titleText.text = _title;

            var subGO = new GameObject("Subtitle");
            subGO.transform.SetParent(canvasGO.transform, false);
            var subRect = subGO.AddComponent<RectTransform>();
            subRect.anchorMin = new Vector2(0f, 1f);
            subRect.anchorMax = new Vector2(0f, 1f);
            subRect.pivot = new Vector2(0f, 1f);
            subRect.anchoredPosition = new Vector2(28f, -162f);
            subRect.sizeDelta = new Vector2(1864f, 260f);
            var subText = subGO.AddComponent<Text>();
            subText.font = UiFontResolver.LoadVt323OrFallback();
            subText.fontSize = 98;
            subText.fontStyle = FontStyle.Bold;
            subText.alignment = TextAnchor.UpperLeft;
            subText.color = new Color(1f, 0.55f, 0.2f, 0.95f);
            subText.horizontalOverflow = HorizontalWrapMode.Wrap;
            subText.verticalOverflow = VerticalWrapMode.Overflow;
            subText.text = _subtitle;

            var beginGO = new GameObject("BeginButton");
            beginGO.transform.SetParent(canvasGO.transform, false);
            var beginRect = beginGO.AddComponent<RectTransform>();
            beginRect.anchorMin = new Vector2(0.42f, 0.32f);
            beginRect.anchorMax = new Vector2(0.58f, 0.40f);
            beginRect.offsetMin = Vector2.zero;
            beginRect.offsetMax = Vector2.zero;
            var beginImg = beginGO.AddComponent<Image>();
            beginImg.color = new Color(1f, 0.55f, 0.2f, 1f);
            var beginBtn = beginGO.AddComponent<Button>();
            beginBtn.targetGraphic = beginImg;
            beginBtn.onClick.AddListener(Dismiss);

            var beginLabelGO = new GameObject("Label");
            beginLabelGO.transform.SetParent(beginGO.transform, false);
            var beginLabelRect = beginLabelGO.AddComponent<RectTransform>();
            beginLabelRect.anchorMin = Vector2.zero;
            beginLabelRect.anchorMax = Vector2.one;
            beginLabelRect.offsetMin = Vector2.zero;
            beginLabelRect.offsetMax = Vector2.zero;
            var beginLabel = beginLabelGO.AddComponent<Text>();
            beginLabel.font = UiFontResolver.LoadVt323OrFallback();
            beginLabel.fontSize = 34;
            beginLabel.fontStyle = FontStyle.Bold;
            beginLabel.alignment = TextAnchor.MiddleCenter;
            beginLabel.color = Color.black;
            beginLabel.text = "Begin";
        }

        private void Dismiss()
        {
            if (_dismissed) return;
            _dismissed = true;
            StartCoroutine(FadeOutAndDestroy());
        }

        private IEnumerator FadeOutAndDestroy()
        {
            if (_cg != null)
            {
                _cg.blocksRaycasts = false;
                _cg.interactable = false;

                float t = 0f;
                float dur = Mathf.Max(0.01f, _fadeOutDuration);
                while (t < dur)
                {
                    t += Time.deltaTime;
                    _cg.alpha = Mathf.Lerp(1f, 0f, Mathf.Clamp01(t / dur));
                    yield return null;
                }
                _cg.alpha = 0f;
            }

            PlayerInputFreeze.RestorePlayerLocomotion();

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            Destroy(gameObject, 0.05f);
        }
    }
}

