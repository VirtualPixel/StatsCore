using System;
using System.Collections;
using MenuLib;
using MenuLib.MonoBehaviors;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StatsCore
{
    // The stats screen: a MenuLib page with a view and a category picker on top and the stats under them,
    // name on the left and value on the right, grouped by category. Opened from the main and pause menus.
    internal static class StatsPage
    {
        private const string RowPrefix = "StatsRow";
        private static readonly Color Gold = new Color(1f, 0.78f, 0.26f);
        private static readonly Color Orange = new Color(1f, 0.55f, 0f);
        private static readonly Color Gray = new Color(0.72f, 0.72f, 0.72f);

        private static REPOPopupPage? _page;
        private static TMP_Text? _font;
        private static float _rowWidth = 254f;
        private static StatsView _view = StatsView.Career;
        private static string _category = StatsModel.AllCategories;

        internal static void Open()
        {
            if (MenuManager.instance == null) return;

            _view = StatsView.Career;
            _category = StatsModel.AllCategories;
            REPOPopupPage page = MenuAPI.CreateREPOPopupPage("Stats", REPOPopupPage.PresetSide.Left, false, true, 2f);
            _page = page;

            AddSelector(page, "View", StatsModel.ViewNames, StatsModel.ViewNames[0], choice =>
            {
                _view = (StatsView)Math.Max(0, Array.IndexOf(StatsModel.ViewNames, choice));
                Rebuild();
            });
            AddSelector(page, "Category", StatsModel.CategoryNames(), StatsModel.AllCategories, choice =>
            {
                _category = choice;
                Rebuild();
            });
            Fill(page);
            page.AddElement(parent => MenuAPI.CreateREPOButton("Back", () => page.ClosePage(true), parent, new Vector2(66f, 18f)));

            // The game only treats a page as interactive when its priority is at least the highest open
            // one, and MenuLib sets none. Without this the page has no hover, scrolling or navigation
            // when it opens over a page the game opened itself.
            page.menuPage.interactionPriority = 0;
            page.OpenPage(false);
            page.menuPage.interactionPriority = MenuManager.instance.HighestOpenPriority() + 1;

            // MenuLib turns off rows outside the visible window, and keyboard and controller navigation
            // only sees active ones. Keep them all on and let the mask clip what is out of view.
            page.scrollView.enabled = false;
            page.StartCoroutine(Remeasure(page));
        }

        private static void AddSelector(REPOPopupPage page, string label, string[] options, string current, Action<string> onChange)
        {
            page.AddElementToScrollView(sv =>
            {
                REPOSlider slider = MenuAPI.CreateREPOSlider(label, "", (string choice) => onChange(choice), sv,
                    stringOptions: options, defaultOption: current, localPosition: Vector2.zero,
                    barBehavior: REPOSlider.BarBehavior.UpdateWithValue);
                float width = slider.rectTransform.rect.width;
                if (width > 0f) _rowWidth = width;
                _font ??= slider.labelTMP;
                return slider.rectTransform;
            });
        }

        private static void Rebuild()
        {
            REPOPopupPage? page = _page;
            if (page == null) return;
            foreach (REPOScrollViewElement row in page.scrollView.GetComponentsInChildren<REPOScrollViewElement>(true))
            {
                if (!row.name.StartsWith(RowPrefix)) continue;
                row.transform.SetParent(null);
                UnityEngine.Object.Destroy(row.gameObject);
            }
            Fill(page);
            page.StartCoroutine(Remeasure(page));
        }

        private static void Fill(REPOPopupPage page)
        {
            foreach (Line line in StatsModel.Build(_view, _category))
            {
                switch (line.Kind)
                {
                    case LineKind.Header:
                        AddRow(page, line.Text, "", 22f, 34f, Orange, Orange, 0f, 10f, 2f, false);
                        break;
                    case LineKind.Sub:
                        AddRow(page, line.Text, line.Value, 14f, 20f, Gray, Gray, 12f, 0f, 0f, false);
                        break;
                    case LineKind.Note:
                        AddRow(page, line.Text, "", 15f, 32f, Gray, Gray, 0f, 8f, 0f, false);
                        break;
                    default:
                        Color name = line.Dim ? Fade(White()) : White();
                        Color value = line.Dim ? Fade(Gold) : Gold;
                        AddRow(page, line.Text, line.Value, 17f, 24f, name, value, 0f, 0f, 0f, true);
                        break;
                }
            }
        }

        private static Color White() => _font != null ? _font.color : Color.white;

        private static Color Fade(Color c) => new Color(c.r, c.g, c.b, 0.45f);

        // Each row is built here, not from MenuLib's label, so its slot is exactly `height` tall and the
        // text sits inside it.
        private static void AddRow(REPOPopupPage page, string name, string value, float size, float height,
            Color nameColor, Color valueColor, float indent, float top, float bottom, bool rule)
        {
            page.AddElementToScrollView(sv =>
            {
                int layer = sv.gameObject.layer;
                var root = new GameObject(RowPrefix + " - " + name, typeof(RectTransform)) { layer = layer };
                var rect = (RectTransform)root.transform;
                rect.SetParent(sv, false);
                rect.anchorMin = rect.anchorMax = Vector2.zero;
                rect.pivot = Vector2.zero;
                rect.sizeDelta = new Vector2(_rowWidth, height);
                rect.localPosition = Vector3.zero;

                float valueRoom = string.IsNullOrEmpty(value) ? 0f : _rowWidth * 0.36f;
                AddText(rect, name, size, nameColor, TextAlignmentOptions.MidlineLeft, indent, _rowWidth - indent - valueRoom - 6f, layer);
                if (valueRoom > 0f)
                    AddText(rect, value, size, valueColor, TextAlignmentOptions.MidlineRight, 0f, valueRoom, layer);

                if (rule)
                {
                    var line = new GameObject("Rule", typeof(RectTransform), typeof(Image)) { layer = layer };
                    var lineRect = (RectTransform)line.transform;
                    lineRect.SetParent(rect, false);
                    lineRect.anchorMin = Vector2.zero;
                    lineRect.anchorMax = new Vector2(1f, 0f);
                    lineRect.pivot = new Vector2(0.5f, 0f);
                    lineRect.sizeDelta = new Vector2(0f, 1f);
                    var image = line.GetComponent<Image>();
                    image.color = new Color(1f, 1f, 1f, 0.08f);
                    image.raycastTarget = false;
                }
                return rect;
            }, top, bottom);
        }

        private static void AddText(RectTransform parent, string text, float size, Color color,
            TextAlignmentOptions align, float indent, float room, int layer)
        {
            var child = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI)) { layer = layer };
            var rect = (RectTransform)child.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;

            var tmp = child.GetComponent<TextMeshProUGUI>();
            if (_font != null)
            {
                tmp.font = _font.font;
                tmp.fontSharedMaterial = _font.fontSharedMaterial;
                tmp.fontStyle = _font.fontStyle;
            }
            tmp.color = color;
            tmp.alignment = align;
            tmp.raycastTarget = false;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.margin = new Vector4(indent, 0f, align == TextAlignmentOptions.MidlineRight ? 2f : 0f, 0f);
            tmp.fontSize = size;
            tmp.text = text;

            float have = tmp.GetPreferredValues(text).x;
            if (room > 0f && have > room)
                tmp.fontSize = Mathf.Max(9f, size * room / have);
        }

        // The game measures where rows sit once, when the scroll box starts, and a MenuLib page is empty
        // then, so following the selection scrolled to the wrong place. Measure again after the rows settle.
        private static IEnumerator Remeasure(REPOPopupPage page)
        {
            yield return new WaitForSecondsRealtime(0.3f);
            if (!page) yield break;
            page.menuScrollBox.RecalculateScrollHeight();
            page.scrollView.UpdateElements();
        }
    }
}
