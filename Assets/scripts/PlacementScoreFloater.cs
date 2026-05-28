using UnityEngine;
using UnityEngine.UI;

namespace AlgorithmicGallery.Corruption
{
    /// <summary>Floating "+72 Nostalgic" style feedback at a world position (world-space canvas, billboarded).</summary>
    public class PlacementScoreFloater : MonoBehaviour
    {
        private float _riseSpeed = 1.15f;
        private float _lifetime = 1.7f;
        private float _startScale = 0.017f;
        private const float SpawnClearanceAboveModelTop = 0.1f;

        public static void Spawn(
            Vector3 worldPosition,
            int points,
            string groupLabel,
            bool useCorporateBlue = false,
            Vector3? worldOffset = null)
        {
            var host = new GameObject("PlacementScoreFloater");
            host.transform.position = worldPosition
                + Vector3.up * SpawnClearanceAboveModelTop
                + (worldOffset ?? Vector3.zero);
            var floater = host.AddComponent<PlacementScoreFloater>();
            floater.Build(points, groupLabel, useCorporateBlue);
        }

        private Text _text;
        private CanvasGroup _cg;
        private float _age;

        private void Build(int points, string groupLabel, bool useCorporateBlue)
        {
            var canvasGo = new GameObject("Canvas");
            canvasGo.transform.SetParent(transform, false);
            canvasGo.transform.localPosition = Vector3.zero;
            canvasGo.transform.localRotation = Quaternion.identity;

            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 520;

            var rt = canvasGo.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(780f, 200f);
            transform.localScale = Vector3.one * _startScale;

            var cg = canvasGo.AddComponent<CanvasGroup>();
            cg.alpha = 1f;
            _cg = cg;

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(canvasGo.transform, false);
            var trt = textGo.AddComponent<RectTransform>();
            Stretch(trt);
            _text = textGo.AddComponent<Text>();
            _text.font = UiFontResolver.LoadVt323OrFallback();
            _text.fontSize = 82;
            _text.fontStyle = FontStyle.Bold;
            _text.alignment = TextAnchor.MiddleCenter;
            _text.color = useCorporateBlue
                ? new Color(0.2f, 0.62f, 1f, 1f)
                : new Color(1f, 0.42f, 0.02f, 1f);
            _text.text = $"+{points}  {groupLabel}";

            var outline = textGo.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.92f);
            outline.effectDistance = new Vector2(4f, -4f);

            var shadow = textGo.AddComponent<Shadow>();
            shadow.effectColor = useCorporateBlue
                ? new Color(0.65f, 0.86f, 1f, 0.55f)
                : new Color(1f, 0.85f, 0.35f, 0.55f);
            shadow.effectDistance = new Vector2(3f, -3f);
        }

        private static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;
        }

        void Update()
        {
            _age += Time.deltaTime;
            if (_cg != null)
                _cg.alpha = 1f - Mathf.Clamp01((_age - (_lifetime * 0.42f)) / (_lifetime * 0.58f));

            transform.position += Vector3.up * (_riseSpeed * Time.deltaTime);

            if (Camera.main != null)
            {
                Vector3 toCam = Camera.main.transform.position - transform.position;
                if (toCam.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.LookRotation(-toCam.normalized, Vector3.up);
            }

            if (_age >= _lifetime)
                Destroy(gameObject);
        }
    }
}
