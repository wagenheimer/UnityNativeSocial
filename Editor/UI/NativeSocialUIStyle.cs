using UnityEngine;
using UnityEngine.UIElements;

namespace Wagenheimer.NativeSocial.Editor.UI
{
    public static class NativeSocialUIStyle
    {
        public static readonly Color ColorBgDark = new(0.067f, 0.078f, 0.110f);
        public static readonly Color ColorCardDark = new(0.090f, 0.125f, 0.184f);
        public static readonly Color ColorCardBorder = new(0.157f, 0.224f, 0.314f);
        public static readonly Color ColorPrimary = new(0.000f, 0.706f, 0.847f);
        public static readonly Color ColorPrimaryHover = new(0.012f, 0.518f, 0.780f);
        public static readonly Color ColorText = new(0.886f, 0.910f, 0.941f);
        public static readonly Color ColorTextMuted = new(0.580f, 0.639f, 0.722f);
        public static readonly Color ColorCodeBg = new(0.043f, 0.059f, 0.098f);
        public static readonly Color ColorSuccess = new(0.220f, 0.741f, 0.447f);
        public static readonly Color ColorWarning = new(0.961f, 0.620f, 0.043f);
        public static readonly Color ColorError = new(0.937f, 0.267f, 0.267f);

        public static void SetRadius(this VisualElement el, float radius)
        {
            if (el == null) return;
            el.style.borderTopLeftRadius = radius;
            el.style.borderTopRightRadius = radius;
            el.style.borderBottomLeftRadius = radius;
            el.style.borderBottomRightRadius = radius;
        }

        public static void SetPadding(this VisualElement el, float horizontal, float vertical)
        {
            if (el == null) return;
            el.style.paddingLeft = horizontal;
            el.style.paddingRight = horizontal;
            el.style.paddingTop = vertical;
            el.style.paddingBottom = vertical;
        }

        public static void SetBorder(this VisualElement el, float width, Color color)
        {
            if (el == null) return;
            el.style.borderLeftWidth = width;
            el.style.borderRightWidth = width;
            el.style.borderTopWidth = width;
            el.style.borderBottomWidth = width;
            el.style.borderLeftColor = color;
            el.style.borderRightColor = color;
            el.style.borderTopColor = color;
            el.style.borderBottomColor = color;
        }
    }
}
