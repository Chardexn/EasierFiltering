using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace EasierFiltering
{
    /// <summary>
    /// 机器风格预览窗口：双色耐久条、水质挡位、一屏内显示完整。
    /// </summary>
    internal static class FilterPreviewWindow
    {
        public const string WINDOW_ID = "easier_filtering.preview";
        private const int StockRowCount = 4;
        private static readonly Color TipColor = new Color(0.56f, 0.64f, 0.66f);
        private static readonly int[] TierButtonOrder = { 2, 1, 0 };

        private static GameItem _container;
        private static FilterStock _stock;
        private static CustomUIWindow _window;
        private static int _target;
        private static int _selectedIndex;
        private static FilterTierInfo[] _tiers;
        private static readonly Dictionary<string, Image> ConsumeOverlays = new Dictionary<string, Image>();

        #region MARK: 打开
        public static void Open(GameItem container)
        {
            if (container == null || CustomUIManager.Instance == null)
            {
                return;
            }

            _container = container;
            _stock = FilterInventory.Collect();
            _tiers = FilterSimulator.EvaluateTiers(container, _stock);
            FilterTierInfo pick = FilterSimulator.PickDefaultTier(_tiers);
            _selectedIndex = pick.Index;
            _target = pick.Threshold;
            Build(pick.Result);
        }
        #endregion

        #region MARK: 构建
        private static void Build(FilterSimResult result)
        {
            if (CustomUIManager.Instance == null)
            {
                return;
            }

            ConsumeOverlays.Clear();
            CustomUIBuilder builder = CustomUIManager.Instance.CreateWindow(
                WINDOW_ID,
                ModEntry.L("preview_title"),
                CustomUIManager.LAYER_PAPER,
                null,
                CustomUIManager.Style.Machinery);
            if (builder == null)
            {
                return;
            }

            builder
                .UseMachineryStyle()
                .SetSize(480, 448)
                .SetDraggable(true)
                .SetCloseOnEscape(true)
                .OnClose(ClearState);

            builder.AddLabel(ContainerLine(), "container_info");

            builder.BeginPanel(-1f, -1f, TextAnchor.UpperLeft, 8f, 4f, "panel_stock")
                .AddHeader(ModEntry.L("preview_stock"), "header_stock")
                .AddLabel("", "stock_empty");
            for (int i = 0; i < StockRowCount; i++)
            {
                builder.AddLabel("", "stock_" + i + "_label");
                builder.AddProgressBar(0f, "stock_" + i + "_bar");
            }

            builder.AddLabel("", "stock_uv");
            builder.End();

            builder.BeginPanel(-1f, -1f, TextAnchor.UpperLeft, 8f, 4f, "panel_target")
                .AddHeader(ModEntry.L("preview_tier"), "header_target")
                .BeginRow();
            foreach (int index in TierButtonOrder)
            {
                int captured = index;
                builder.AddButton("", () => OnTierSelected(captured), "tier_" + captured);
            }

            builder.End()
                .AddLabel("", "tier_status");
            AddTip(builder, "tip_stop_at_tier");
            builder.End();

            builder.BeginPanel(-1f, -1f, TextAnchor.UpperLeft, 8f, 4f, "panel_result")
                .AddHeader(ModEntry.L("preview_result"), "header_result")
                .AddLabel("", "result_purity")
                .AddLabel("", "result_volume")
                .AddLabel("", "result_pours")
                .AddLabel("", "max_volume")
                .AddWarning("", "warn_any");
            builder.End();

            builder.AddSpace(8)
                .BeginRow()
                    .AddFlexibleSpace()
                    .AddButton(ModEntry.L("preview_confirm"), Confirm, "btn_confirm")
                    .AddButton(ModEntry.L("preview_cancel"), Close, "btn_cancel")
                .End();

            _window = builder.Show();
            Paint(_window, result);
        }

        private static void OnTierSelected(int index)
        {
            if (_container == null || _window == null || !_window.IsAlive)
            {
                return;
            }

            _selectedIndex = index;
            _target = WaterFeatureHelper.WATER_PURITIES[index];
            FilterTierInfo info = FindTier(index);
            FilterSimResult result = info != null
                ? info.Result
                : FilterSimulator.Simulate(_container, _stock, _target);
            Paint(_window, result);
        }

        private static void AddTip(CustomUIBuilder builder, string key)
        {
            builder.AddLabel(ModEntry.L(key), key);
            CustomUIElement last = builder.Last;
            if (last?.text == null)
            {
                return;
            }

            last.text.richText = true;
            last.text.fontSize = Mathf.Max(10f, last.text.fontSize * 0.75f);
            last.text.color = TipColor;
            last.text.enableWordWrapping = true;
            last.text.overflowMode = TextOverflowModes.Overflow;
        }
        #endregion

        #region MARK: 刷新
        private static void Paint(CustomUIWindow window, FilterSimResult result)
        {
            SetText(window, "container_info", ContainerLine());
            PaintStock(window, result);
            PaintTiers(window);
            SetText(window, "tier_status", TierStatusLine());
            SetText(window, "result_purity", ModEntry.L(
                "preview_result_purity",
                FilterSimulator.FormatPurity(result.PurityBefore),
                FilterSimulator.FormatPurity(result.PurityAfter),
                FilterSimulator.QualityName(result.PurityAfter)));
            SetText(window, "result_volume", ModEntry.L(
                "preview_result_volume",
                result.VolumeBeforeUl / 1000,
                result.VolumeAfterUl / 1000));
            SetText(window, "result_pours", ModEntry.L("preview_result_pours", result.PitcherPours));
            SetText(window, "max_volume", MaxVolumeLine(result));
            PaintWarning(window, result);
            window.Get("btn_confirm")?.SetInteractable(!result.NoChange && _container != null);
        }

        private static void PaintTiers(CustomUIWindow window)
        {
            foreach (int index in TierButtonOrder)
            {
                CustomUIElement button = window.Get("tier_" + index);
                if (button == null)
                {
                    continue;
                }

                FilterTierInfo info = FindTier(index);
                FilterTierKind kind = info != null ? info.Kind : FilterTierKind.Impossible;
                bool selected = index == _selectedIndex;
                string name = WaterFeatureHelper.WATER_DISPLAY_LOCALIZED[index];
                if (kind == FilterTierKind.Reached)
                {
                    name += ModEntry.L("tier_mark_reached");
                }
                else if (kind == FilterTierKind.Impossible)
                {
                    name += ModEntry.L("tier_mark_impossible");
                }

                if (selected)
                {
                    name = "> " + name;
                }

                button.SetText(name);
                bool interactable = !selected && kind != FilterTierKind.Reached && kind != FilterTierKind.Impossible;
                button.SetInteractable(interactable);
            }
        }

        private static void PaintStock(CustomUIWindow window, FilterSimResult result)
        {
            List<IGrouping<string, FilterSnapshot>> groups = _stock == null
                ? new List<IGrouping<string, FilterSnapshot>>()
                : _stock.Filters
                    .GroupBy(f => f.Identifier)
                    .OrderBy(g => g.Key == FilterInventory.ADV_FILTER_ID ? 0 : 1)
                    .ThenBy(g => g.Key)
                    .ToList();

            bool empty = groups.Count == 0 && (_stock == null || !_stock.HasUv);
            SetWarning(window, "stock_empty", empty, ModEntry.L("preview_stock_empty"));

            List<StockRow> rows = BuildStockRows(groups, result);
            for (int i = 0; i < StockRowCount; i++)
            {
                bool visible = i < rows.Count;
                CustomUIElement label = window.Get("stock_" + i + "_label");
                CustomUIElement bar = window.Get("stock_" + i + "_bar");
                label?.SetVisible(visible);
                bar?.SetVisible(visible);
                if (!visible)
                {
                    if (ConsumeOverlays.TryGetValue("stock_" + i + "_bar", out Image overlay) && overlay != null)
                    {
                        overlay.gameObject.SetActive(false);
                    }

                    continue;
                }

                StockRow row = rows[i];
                label?.SetText(ModEntry.L("preview_stock_bar", row.Name, row.Count, row.Consumed, row.Before));
                int maxDur = Mathf.Max(row.Max, 1);
                float afterRatio = Mathf.Clamp01(row.After / (float)maxDur);
                float beforeRatio = Mathf.Clamp01(row.Before / (float)maxDur);
                bar?.SetProgress(afterRatio, "");
                UpdateConsumeOverlay("stock_" + i + "_bar", window, afterRatio, beforeRatio);
            }

            bool hasUv = _stock != null && _stock.HasUv;
            SetWarning(window, "stock_uv", hasUv, hasUv ? ModEntry.L("preview_stock_uv", _stock.UvLamps.Count) : "");
        }

        private struct StockRow
        {
            public string Name;
            public int Count;
            public int Before;
            public int After;
            public int Max;
            public int Consumed;
        }

        private static List<StockRow> BuildStockRows(List<IGrouping<string, FilterSnapshot>> groups, FilterSimResult result)
        {
            List<StockRow> rows = new List<StockRow>();
            if (groups.Count <= StockRowCount)
            {
                foreach (IGrouping<string, FilterSnapshot> group in groups)
                {
                    rows.Add(ToStockRow(group.First().DisplayName, group, result));
                }

                return rows;
            }

            for (int i = 0; i < StockRowCount - 1; i++)
            {
                IGrouping<string, FilterSnapshot> group = groups[i];
                rows.Add(ToStockRow(group.First().DisplayName, group, result));
            }

            IEnumerable<IGrouping<string, FilterSnapshot>> rest = groups.Skip(StockRowCount - 1);
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

        private static StockRow ToStockRow(string name, IGrouping<string, FilterSnapshot> group, FilterSimResult result)
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

        private static void UpdateConsumeOverlay(string barId, CustomUIWindow window, float afterRatio, float beforeRatio)
        {
            Image overlay = EnsureConsumeOverlay(barId, window);
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

        private static Image EnsureConsumeOverlay(string barId, CustomUIWindow window)
        {
            if (ConsumeOverlays.TryGetValue(barId, out Image existing) && existing != null)
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
            ConsumeOverlays[barId] = image;
            return image;
        }

        private static void PaintWarning(CustomUIWindow window, FilterSimResult result)
        {
            bool noFilter = result.NoMechanicalFilters && !result.HasUv;
            bool shortfall = !result.ReachedTarget && result.DurabilityShortfall > 0 && result.TargetPurity <= result.TheoreticalMaxPurity;
            bool unfilterable = result.UnfilterableNames.Count > 0 && result.TheoreticalMaxPurity < 10000;
            string text;
            if (noFilter)
            {
                text = ModEntry.L("warn_no_filter");
            }
            else if (shortfall)
            {
                text = ModEntry.L("warn_shortfall", result.DurabilityShortfall, FilterSimulator.FormatPurity(result.PurityAfter));
            }
            else if (unfilterable && !result.ReachedTarget)
            {
                text = ModEntry.L(
                    "warn_unfilterable",
                    string.Join(", ", result.UnfilterableNames),
                    FilterSimulator.FormatPurity(result.TheoreticalMaxPurity));
            }
            else
            {
                text = "";
            }

            SetWarning(window, "warn_any", !string.IsNullOrEmpty(text), text);
        }

        private static void SetText(CustomUIWindow window, string id, string text)
        {
            window.Get(id)?.SetText(text);
        }

        private static void SetWarning(CustomUIWindow window, string id, bool visible, string text)
        {
            CustomUIElement element = window.Get(id);
            if (element == null)
            {
                return;
            }

            element.SetText(visible ? text : " ");
            element.SetVisible(visible);
        }
        #endregion

        #region MARK: 文案
        private static string ContainerLine()
        {
            if (_container == null)
            {
                return "";
            }

            int ml = WaterHelper.GetCurrentCapacityML(_container);
            int purity = WaterHelper.GetWaterPurity(_container);
            string name = string.IsNullOrEmpty(_container.name) ? _container.identifier : _container.name;
            return ModEntry.L(
                "preview_container_line",
                name,
                ml,
                FilterSimulator.FormatPurity(purity),
                FilterSimulator.QualityName(purity));
        }

        private static string TierStatusLine()
        {
            FilterTierInfo info = FindTier(_selectedIndex);
            if (info == null)
            {
                return "";
            }

            string name = WaterFeatureHelper.WATER_DISPLAY_LOCALIZED[info.Index];
            string range = FilterSimulator.TierThresholdLabel(info.Threshold);
            switch (info.Kind)
            {
                case FilterTierKind.Reached:
                    return ModEntry.L("tier_reached", name, range);
                case FilterTierKind.Ok:
                    return ModEntry.L("tier_ok", name, range, info.Result.DurabilityConsumed);
                case FilterTierKind.Short:
                    return ModEntry.L("tier_short", name, range, info.Result.DurabilityShortfall);
                default:
                    string contaminants = info.Result.UnfilterableNames.Count > 0
                        ? string.Join(", ", info.Result.UnfilterableNames)
                        : "";
                    return ModEntry.L(
                        "tier_impossible",
                        name,
                        contaminants,
                        FilterSimulator.FormatPurity(info.Result.TheoreticalMaxPurity));
            }
        }

        private static string MaxVolumeLine(FilterSimResult result)
        {
            if (result.TargetPurity <= result.PurityBefore)
            {
                return ModEntry.L("preview_max_already");
            }

            if (result.TargetPurity > result.TheoreticalMaxPurity)
            {
                return ModEntry.L("preview_max_impossible", FilterSimulator.FormatPurity(result.TheoreticalMaxPurity));
            }

            return ModEntry.L(
                "preview_max_volume",
                result.MaxFilterableMl,
                FilterSimulator.QualityName(result.TargetPurity));
        }

        private static FilterTierInfo FindTier(int index)
        {
            if (_tiers == null)
            {
                return null;
            }

            foreach (FilterTierInfo info in _tiers)
            {
                if (info.Index == index)
                {
                    return info;
                }
            }

            return null;
        }
        #endregion

        #region MARK: 确认 / 关闭
        private static void Confirm()
        {
            if (_container == null)
            {
                Close();
                return;
            }

            _stock = FilterInventory.Collect();
            FilterSimResult result = FilterSimulator.Simulate(_container, _stock, _target);
            if (result.NoChange)
            {
                Close();
                return;
            }

            FilterInventory.Apply(_container, result);
            Close();
        }

        private static void Close()
        {
            CustomUIManager.Instance?.CloseWindow(WINDOW_ID);
            ClearState();
        }

        private static void ClearState()
        {
            _container = null;
            _stock = null;
            _window = null;
            _tiers = null;
            ConsumeOverlays.Clear();
        }
        #endregion
    }
}
