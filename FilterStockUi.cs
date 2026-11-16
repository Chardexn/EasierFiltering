using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace EasierFiltering
{
    /// <summary>
    /// 过滤预览与水培机共用的消耗物品量：按滤芯种类分行，含本次消耗叠层。
    /// </summary>
    internal static class FilterStockUi
    {
        public const int RowCount = 4;

        #region MARK: 构建
        public static void AppendRows(CustomUIBuilder builder)
        {
            if (builder == null)
            {
                return;
            }

            builder.AddLabel("", "stock_empty");
            for (int i = 0; i < RowCount; i++)
            {
                builder.AddLabel("", "stock_" + i + "_label");
                builder.AddProgressBar(0f, "stock_" + i + "_bar");
            }

            builder.AddLabel("", "stock_tablets_label");
            builder.AddProgressBar(0f, "stock_tablets_bar");
        }
        #endregion

        #region MARK: 刷新
        public static void Paint(
            CustomUIWindow window,
            FilterStock stock,
            FilterSimResult result,
            bool useTablets,
            Dictionary<string, Image> overlays)
        {
            if (window == null)
            {
                return;
            }

            List<IGrouping<string, FilterSnapshot>> groups = stock == null
                ? new List<IGrouping<string, FilterSnapshot>>()
                : stock.Filters
                    .GroupBy(f => f.Identifier)
                    .OrderBy(g => g.Key == FilterInventory.ADV_FILTER_ID ? 0 : 1)
                    .ThenBy(g => g.Key)
                    .ToList();

            bool showTablets = FilterConfig.UseTablets.Value && stock != null
                && (stock.TabletCount > 0 || (result != null && result.TabletsConsumed > 0));
            bool empty = groups.Count == 0 && !showTablets;
            SetVisible(window, "stock_empty", empty, empty ? ModEntry.L("preview_stock_empty") : " ");

            List<StockRow> rows = BuildRows(groups, result);
            for (int i = 0; i < RowCount; i++)
            {
                PaintRow(window, "stock_" + i, i < rows.Count ? rows[i] : (StockRow?)null, overlays);
            }

            if (showTablets)
            {
                int total = stock.TabletCount;
                int used = result != null && useTablets ? result.TabletsConsumed : 0;
                PaintRow(window, "stock_tablets", new StockRow
                {
                    Name = ModEntry.L("addon_tablets"),
                    Count = total,
                    Before = total,
                    After = Mathf.Max(0, total - used),
                    Max = Mathf.Max(total, 1),
                    Consumed = used
                }, overlays);
            }
            else
            {
                PaintRow(window, "stock_tablets", null, overlays);
            }
        }

        public static void Hide(CustomUIWindow window, Dictionary<string, Image> overlays)
        {
            SetVisible(window, "stock_empty", false, " ");
            for (int i = 0; i < RowCount; i++)
            {
                PaintRow(window, "stock_" + i, null, overlays);
            }

            PaintRow(window, "stock_tablets", null, overlays);
        }
        #endregion

        #region MARK: 行
        private struct StockRow
        {
            public string Name;
            public int Count;
            public int Before;
            public int After;
            public int Max;
            public int Consumed;
        }

        private static void PaintRow(
            CustomUIWindow window,
            string id,
            StockRow? row,
            Dictionary<string, Image> overlays)
        {
            CustomUIElement label = window.Get(id + "_label");
            CustomUIElement bar = window.Get(id + "_bar");
            bool visible = row.HasValue;
            label?.SetVisible(visible);
            bar?.SetVisible(visible);
            if (!visible)
            {
                if (overlays != null && overlays.TryGetValue(id + "_bar", out Image overlay) && overlay != null)
                {
                    overlay.gameObject.SetActive(false);
                }

                return;
            }

            StockRow data = row.Value;
            label?.SetText(ModEntry.L("preview_stock_bar", data.Name, data.Count, data.Consumed, data.Before));
            Wrap(label);
            int max = Mathf.Max(data.Max, 1);
            float afterRatio = Mathf.Clamp01(data.After / (float)max);
            float beforeRatio = Mathf.Clamp01(data.Before / (float)max);
            bar?.SetProgress(afterRatio, "");
            UpdateOverlay(id + "_bar", window, afterRatio, beforeRatio, overlays);
        }

        private static List<StockRow> BuildRows(List<IGrouping<string, FilterSnapshot>> groups, FilterSimResult result)
        {
            List<StockRow> rows = new List<StockRow>();
            if (groups.Count <= RowCount)
            {
                foreach (IGrouping<string, FilterSnapshot> group in groups)
                {
                    rows.Add(ToRow(group.First().DisplayName, group, result));
                }

                return rows;
            }

            for (int i = 0; i < RowCount - 1; i++)
            {
                IGrouping<string, FilterSnapshot> group = groups[i];
                rows.Add(ToRow(group.First().DisplayName, group, result));
            }

            IEnumerable<IGrouping<string, FilterSnapshot>> rest = groups.Skip(RowCount - 1);
            List<FilterSnapshot> restItems = rest.SelectMany(g => g).ToList();
            HashSet<string> restIds = new HashSet<string>(rest.Select(g => g.Key));
            int consumed = ConsumedFor(result, restIds);
            int before = restItems.Sum(f => f.Durability);
            int max = restItems.Sum(f => Mathf.Max(f.MaxDurability, f.Durability));
            rows.Add(new StockRow
            {
                Name = ModEntry.L("preview_stock_other"),
                Count = restItems.Count,
                Before = before,
                After = Mathf.Max(0, before - consumed),
                Max = max,
                Consumed = consumed
            });
            return rows;
        }

        private static StockRow ToRow(string name, IGrouping<string, FilterSnapshot> group, FilterSimResult result)
        {
            int before = group.Sum(f => f.Durability);
            int max = group.Sum(f => Mathf.Max(f.MaxDurability, f.Durability));
            int consumed = ConsumedFor(result, new HashSet<string> { group.Key });
            return new StockRow
            {
                Name = name,
                Count = group.Count(),
                Before = before,
                After = Mathf.Max(0, before - consumed),
                Max = max,
                Consumed = consumed
            };
        }

        private static int ConsumedFor(FilterSimResult result, HashSet<string> identifiers)
        {
            if (result?.FiltersAfter == null)
            {
                return 0;
            }

            return result.FiltersAfter
                .Where(f => identifiers.Contains(f.Identifier))
                .Sum(f => Mathf.Max(0, f.DurabilityBefore - f.Durability));
        }
        #endregion

        #region MARK: 消耗叠层
        private static void UpdateOverlay(
            string barId,
            CustomUIWindow window,
            float afterRatio,
            float beforeRatio,
            Dictionary<string, Image> overlays)
        {
            Image overlay = EnsureOverlay(barId, window, overlays);
            if (overlay == null)
            {
                return;
            }

            if (beforeRatio <= afterRatio + 0.001f)
            {
                overlay.gameObject.SetActive(false);
                return;
            }

            overlay.gameObject.SetActive(true);
            RectTransform rt = overlay.rectTransform;
            rt.anchorMin = new Vector2(Mathf.Clamp01(afterRatio), 0f);
            rt.anchorMax = new Vector2(Mathf.Clamp01(beforeRatio), 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static Image EnsureOverlay(string barId, CustomUIWindow window, Dictionary<string, Image> overlays)
        {
            if (overlays == null)
            {
                return null;
            }

            if (overlays.TryGetValue(barId, out Image existing) && existing != null)
            {
                return existing;
            }

            CustomUIElement bar = window.Get(barId);
            if (bar?.root == null)
            {
                return null;
            }

            Transform fill = bar.root.transform.Find("fill");
            Transform parent = fill != null ? fill : bar.root.transform;
            GameObject go = new GameObject("consume", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Image image = go.AddComponent<Image>();
            image.color = MachineryStyle.PROGRESS_FILL_DANGER;
            image.raycastTarget = false;
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.offsetMin = fill != null ? Vector2.zero : new Vector2(4f, 4f);
            rt.offsetMax = fill != null ? Vector2.zero : new Vector2(-4f, -4f);
            overlays[barId] = image;
            return image;
        }

        private static void SetVisible(CustomUIWindow window, string id, bool visible, string text)
        {
            CustomUIElement element = window.Get(id);
            if (element == null)
            {
                return;
            }

            element.SetText(visible ? text : " ");
            element.SetVisible(visible);
        }

        private static void Wrap(CustomUIElement element)
        {
            if (element?.text == null)
            {
                return;
            }

            element.text.richText = true;
            element.text.enableWordWrapping = true;
            element.text.overflowMode = TMPro.TextOverflowModes.Overflow;
            if (element.root == null)
            {
                return;
            }

            LayoutElement layout = element.root.GetComponent<LayoutElement>();
            if (layout == null)
            {
                layout = element.root.AddComponent<LayoutElement>();
            }

            layout.flexibleWidth = 1f;
            layout.minHeight = 0f;
        }
        #endregion
    }
}
