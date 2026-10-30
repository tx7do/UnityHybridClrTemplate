using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Main.UI
{
    /// <summary>
    /// 运行时代码构建 uGUI 的公共辅助（PatchUI 与 GameHallUI 共用）。
    /// 不依赖任何场景、预制体或字体资产。
    /// </summary>
    internal static class UiBuilder
    {
        private static Sprite _whiteSprite;

        /// <summary>1x1 白图，所有纯色 Image 共用。</summary>
        public static Sprite WhiteSprite =>
            _whiteSprite != null ? _whiteSprite : (_whiteSprite = CreateWhiteSprite());

        /// <summary>
        /// 创建 Overlay Canvas（含 Scaler/Raycaster），并确保 EventSystem 存在。
        /// </summary>
        public static Canvas CreateOverlayCanvas(string name, int sortingOrder)
        {
            var canvasGO = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;

            EnsureEventSystem();
            return canvas;
        }

        /// <summary>确保场景中有 EventSystem，否则 Button 无法响应点击。</summary>
        public static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null)
            {
                return;
            }

            var go = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            // 项目只启用了新输入系统时 StandaloneInputModule 不可用；
            // 用反射添加 InputSystemUIInputModule，避免本程序集硬依赖 Unity.InputSystem 包
            var moduleType = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (moduleType != null)
            {
                go.AddComponent(moduleType);
                return;
            }
            go.AddComponent<StandaloneInputModule>();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
        }

        public static Text CreateText(Transform parent, string name, int fontSize, Color color, TextAnchor alignment)
        {
            var go = new GameObject(name, typeof(Text));
            go.transform.SetParent(parent, false);

            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        public static Image CreateImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(Image));
            go.transform.SetParent(parent, false);

            var image = go.GetComponent<Image>();
            image.sprite = WhiteSprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>创建带文字的按钮（返回按钮组件；文字在其子节点上）。</summary>
        public static Button CreateButton(Transform parent, string name, string label, Color color, Action onClick)
        {
            var image = CreateImage(parent, name, color);
            image.raycastTarget = true;

            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick?.Invoke());

            var text = CreateText(image.transform, "Label", 22, Color.white, TextAnchor.MiddleCenter);
            text.text = label;
            Stretch(text.rectTransform);
            return button;
        }

        private static Sprite CreateWhiteSprite()
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
        }

        // ── RectTransform 布局辅助 ────────────────────────────────

        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        public static void SetBox(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 anchoredPosition, Vector2 size)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPosition;
            rt.sizeDelta = size;
        }
    }
}
