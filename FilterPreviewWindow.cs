using System;
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
        private static readonly Color TipColor = new Color(0.56f, 0.64f, 0.66f);
        private static readonly int[] TierButtonOrder = { FilterSimulator.DefaultIndex, 1, 0 };

        private static GameItem _container;
        private static FilterStock _stock;
        private static CustomUIWindow _window;
        private static int _target;
        private static int _selectedIndex;
        private static bool _useUv;
        private static bool _useTablets;
        private static bool _purifierMode;
        private static PurifierInfo _purifier;
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
            _useUv = FilterConfig.UseUv.Value && _stock.HasUv;
            _useTablets = FilterConfig.UseTablets.Value && _stock.TabletCount > 0;
            _purifier = PurifierProbe.Find(container);
            _purifierMode = _purifier != null;
            FilterTierInfo pick = EvaluateCurrent(resetTier: true);
            _selectedIndex = pick != null ? pick.Index : FilterSimulator.DefaultIndex;
            _target = pick != null ? pick.Threshold : 0;
            Build(pick != null ? pick.Result : FallbackResult());
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
                .SetSize(480, 560)
                .SetDraggable(true)
                .SetCloseOnEscape(true)
                .OnClose(ClearState);

            builder.AddLabel(ContainerLine(), "container_info");

            builder.BeginPanel(-1f, -1f, TextAnchor.MiddleLeft, 6f, 2f, "addons");
            builder.BeginRow(6f, TextAnchor.MiddleLeft);
            builder.AddToggle(ModEntry.L("addon_uv"), false, v => OnAddonToggled(uv: true, v), "opt_uv");
            builder.AddToggle(ModEntry.L("addon_tablets"), false, v => OnAddonToggled(uv: false, v), "opt_tablets");
            builder.End();
            builder.End();

            builder.BeginRow();
            builder.AddButton(ModEntry.L("mode_direct"), () => SetMode(false), "mode_direct");
            builder.AddButton(ModEntry.L("mode_purifier"), () => SetMode(true), "mode_purifier");
            builder.End();
            builder.AddLabel("", "purifier_info");

            builder.BeginPanel(-1f, -1f, TextAnchor.UpperLeft, 8f, 4f, "panel_stock")
                .AddHeader(ModEntry.L("preview_stock"), "header_stock");
            FilterStockUi.AppendRows(builder);
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
            AddTip(builder, "tip_default");
            AddTip(builder, "tip_stop_at_tier");
            AddTip(builder, "tip_purifier");
            builder.End();

            builder.BeginPanel(-1f, -1f, TextAnchor.UpperLeft, 8f, 4f, "panel_result")
                .AddHeader(ModEntry.L("preview_result"), "header_result")
                .AddLabel("", "result_purity")
                .AddLabel("", "result_after_night")
                .AddLabel("", "result_volume")
                .BeginRow(8f, TextAnchor.MiddleRight)
                    .AddLabel("", "result_impurity")
                    .AddFlexibleSpace()
                    .AddButton(ModEntry.L("preview_confirm"), Confirm, "btn_confirm")
                .End()
                .AddWarning("", "warn_any");
            builder.End();

            _window = builder.Show();
            Paint(_window, result);
        }

        private static void OnTierSelected(int index)
        {
            if (_container == null || _window == null || !_window.IsAlive)
            {
                return;
            }

            FilterTierInfo info = FindTier(index);
            if (info == null)
            {
                return;
            }

            _selectedIndex = index;
            _target = info.Threshold;
            Paint(_window, info.Result);
        }

        private static void OnAddonToggled(bool uv, bool value)
        {
            if (uv)
            {
                _useUv = value;
            }
            else
            {
                _useTablets = value;
            }

            RefreshAddons();
        }

        private static void SetMode(bool purifier)
        {
            if (purifier && _purifier == null)
            {
                return;
            }

            if (_purifierMode == purifier)
            {
                return;
            }

            _purifierMode = purifier;
            if (_container == null || _window == null || !_window.IsAlive)
            {
                return;
            }

            FilterTierInfo info = EvaluateCurrent(resetTier: true);
            _selectedIndex = info != null ? info.Index : FilterSimulator.DefaultIndex;
            _target = info != null ? info.Threshold : _target;
            Paint(_window, info != null ? info.Result : FallbackResult());
        }

        private static void RefreshAddons()
        {
            if (_container == null || _window == null || !_window.IsAlive)
            {
                return;
            }

            FilterTierInfo info = EvaluateCurrent(resetTier: false);
            _selectedIndex = info != null ? info.Index : FilterSimulator.DefaultIndex;
            _target = info != null ? info.Threshold : _target;
            Paint(_window, info != null ? info.Result : FallbackResult());
        }

        private static FilterTierInfo EvaluateCurrent(bool resetTier)
        {
            _stock = FilterInventory.Collect();
            if (_purifierMode && _purifier != null)
            {
                _tiers = FilterSimulator.EvaluatePurifierTiers(_container, _stock, CurrentAddons(), _purifier);
                FilterTierInfo info = resetTier ? null : FindTier(_selectedIndex);
                if (info == null || ShouldDropTier(info))
                {
                    info = FilterSimulator.PickPurifierTier(_tiers);
                }

                return info;
            }

            _purifierMode = false;
            _tiers = FilterSimulator.EvaluateTiers(_container, _stock, CurrentAddons());
            FilterTierInfo direct = resetTier ? null : FindTier(_selectedIndex);
            if (direct == null || ShouldDropTier(direct))
            {
                direct = FilterSimulator.PickDefaultTier(_tiers);
            }

            return direct;
        }

        private static FilterSimResult FallbackResult()
        {
            return FilterSimulator.SimulateOnePass(_container, _stock, CurrentAddons());
        }

        private static FilterAddons CurrentAddons()
        {
            return new FilterAddons
            {
                Uv = _useUv,
                Tablets = _useTablets
            };
        }

        private static bool ShouldDropTier(FilterTierInfo info)
        {
            return !FilterSimulator.IsDefault(info.Index)
                && (info.Kind == FilterTierKind.Impossible || info.Kind == FilterTierKind.Reached);
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
            PaintModes(window);
            PaintStock(window, result);
            PaintTiers(window);
            SetText(window, "tier_status", TierStatusLine());
            PaintAddons(window);
            bool isDefault = !_purifierMode && FilterSimulator.IsDefault(_selectedIndex);
            SetWarning(window, "tip_default", isDefault, ModEntry.L("tip_default"));
            SetWarning(window, "tip_stop_at_tier", !_purifierMode && !isDefault, ModEntry.L("tip_stop_at_tier"));
            SetWarning(window, "tip_purifier", _purifierMode, ModEntry.L("tip_purifier"));
            SetText(window, "result_purity", ModEntry.L(
                "preview_result_purity",
                FilterSimulator.FormatPurity(result.PurityBefore),
                FilterSimulator.FormatPurity(result.PurityAfter),
                FilterSimulator.QualityName(result.PurityAfter)));
            PaintPurifierResult(window, result);
            SetText(window, "result_volume", ModEntry.L(
                "preview_result_volume",
                result.VolumeBeforeUl / 1000,
                result.VolumeAfterUl / 1000));
            SetText(window, "result_impurity", ImpurityLine(result));
            EnableWrap(window, "container_info");
            EnableWrap(window, "purifier_info");
            EnableWrap(window, "tier_status");
            EnableWrap(window, "result_purity");
            EnableWrap(window, "warn_any");
            ShrinkDetail(window, "result_volume");
            ShrinkDetail(window, "result_impurity");
            ShrinkDetail(window, "result_after_night");
            ShrinkDetail(window, "purifier_info");
            PaintWarning(window, result);
            CustomUIElement confirm = window.Get("btn_confirm");
            confirm?.SetInteractable(!result.NoChange && _container != null);
            confirm?.SetPreferredSize(120f, 28f);
            FitWindow(window);
        }

        private static void FitWindow(CustomUIWindow window)
        {
            RectTransform content = window?.ContentRoot;
            if (content == null || window.Rect == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            float preferred = LayoutUtility.GetPreferredHeight(content);
            if (preferred < 1f)
            {
                return;
            }

            const float header = 40f;
            const float rule = 2f;
            const float pad = 16f;
            float height = Mathf.Clamp(preferred + header + rule + pad * 2f, 360f, 720f);
            window.SetSize(480f, height);
        }

        private static void PaintModes(CustomUIWindow window)
        {
            PaintModeButton(window, "mode_direct", !_purifierMode, available: true, ModEntry.L("mode_direct"), null);
            string purifierName = ModEntry.L("mode_purifier");
            string tip = _purifier == null ? ModEntry.L("mode_purifier_unavailable") : "";
            PaintModeButton(window, "mode_purifier", _purifierMode, _purifier != null, purifierName, tip);
            if (_purifierMode && _purifier != null)
            {
                FilterSimResult selected = FindTier(_selectedIndex)?.Result;
                int cap = selected != null && selected.PurifierCap > 0 ? selected.PurifierCap : _purifier.Cap;
                int removed = selected != null ? selected.PredictedRemovedUl : (int)Math.Min(int.MaxValue, _purifier.TonightBudgetUl);
                SetWarning(
                    window,
                    "purifier_info",
                    true,
                    ModEntry.L("purifier_info", FilterSimulator.FormatPurity(cap), FilterSimulator.FormatMl(removed)));
            }
            else
            {
                SetWarning(window, "purifier_info", false, "");
            }
        }

        private static void PaintModeButton(
            CustomUIWindow window,
            string id,
            bool selected,
            bool available,
            string name,
            string tip)
        {
            CustomUIElement button = window.Get(id);
            if (button == null)
            {
                return;
            }

            if (!available)
            {
                name += ModEntry.L("tier_mark_impossible");
            }

            if (selected)
            {
                name = "> " + name;
            }

            button.SetText(name);
            button.SetTextColor(MachineryStyle.BUTTON_TEXT);
            button.SetInteractable(selected || available);
            if (!string.IsNullOrEmpty(tip))
            {
                button.SetTooltip(tip);
            }
        }

        private static void PaintPurifierResult(CustomUIWindow window, FilterSimResult result)
        {
            if (!_purifierMode || result == null)
            {
                SetWarning(window, "result_after_night", false, "");
                return;
            }

            SetWarning(
                window,
                "result_after_night",
                true,
                ModEntry.L(
                    "purifier_after_night",
                    FilterSimulator.FormatPurity(result.PredictedPurityAfterNight),
                    FilterSimulator.QualityName(result.PredictedPurityAfterNight)));
        }

        private static void PaintTiers(CustomUIWindow window)
        {
            foreach (int index in TierButtonOrder)
            {
                CustomUIElement button = window.Get("tier_" + index);
                bool show = !_purifierMode || !FilterSimulator.IsDefault(index);
                button?.SetVisible(show);
                if (show)
                {
                    PaintTierButton(window, index);
                }
            }
        }

        private static void PaintTierButton(CustomUIWindow window, int index)
        {
            CustomUIElement button = window.Get("tier_" + index);
            if (button == null)
            {
                return;
            }

            FilterTierInfo info = FindTier(index);
            FilterTierKind kind = info != null ? info.Kind : FilterTierKind.Impossible;
            bool selected = index == _selectedIndex;
            bool locked = kind == FilterTierKind.Reached || kind == FilterTierKind.Impossible;
            string name = FilterSimulator.IsDefault(index)
                ? ModEntry.L("preview_default")
                : WaterFeatureHelper.WATER_DISPLAY_LOCALIZED[index];
            if (!selected && kind == FilterTierKind.Reached)
            {
                name += ModEntry.L("tier_mark_reached");
            }
            else if (!selected && kind == FilterTierKind.Impossible)
            {
                name += ModEntry.L("tier_mark_impossible");
            }

            if (selected)
            {
                name = "> " + name;
            }

            button.SetText(name);
            button.SetTextColor(MachineryStyle.BUTTON_TEXT);
            button.SetInteractable(selected || !locked);
        }

        private static void PaintStock(CustomUIWindow window, FilterSimResult result)
        {
            FilterStockUi.Paint(window, _stock, result, _useTablets, ConsumeOverlays);
        }

        private static void PaintWarning(CustomUIWindow window, FilterSimResult result)
        {
            bool noPower = _purifierMode && _purifier != null && !_purifier.HasPower;
            bool noFilter = result.NoMechanicalFilters && !result.HasUv && result.TabletsConsumed <= 0;
            bool shortfall = !result.ReachedTarget && result.DurabilityShortfall > 0 && result.TargetPurity <= result.TheoreticalMaxPurity;
            bool unfilterable = result.UnfilterableNames.Count > 0 && result.TheoreticalMaxPurity < 10000;
            string text;
            if (noPower)
            {
                text = ModEntry.L("warn_no_power");
            }
            else if (noFilter)
            {
                text = ModEntry.L("warn_no_filter");
            }
            else if (shortfall)
            {
                text = ModEntry.L("warn_shortfall", result.DurabilityShortfall, FilterSimulator.FormatPurity(result.PurityAfter));
            }
            else if (unfilterable && (!result.ReachedTarget || result.IsOnePass))
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

        private static void ShrinkDetail(CustomUIWindow window, string id)
        {
            CustomUIElement element = window.Get(id);
            if (element?.text == null)
            {
                return;
            }

            if (element.text.color != TipColor)
            {
                element.text.fontSize = Mathf.Max(10f, element.text.fontSize * 0.8f);
                element.text.color = TipColor;
            }

            EnableWrap(element);
        }

        private static void EnableWrap(CustomUIWindow window, string id)
        {
            EnableWrap(window.Get(id));
        }

        private static void EnableWrap(CustomUIElement element)
        {
            if (element?.text == null)
            {
                return;
            }

            element.text.richText = true;
            element.text.enableWordWrapping = true;
            element.text.overflowMode = TextOverflowModes.Overflow;
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

            if (FilterSimulator.IsDefault(info.Index))
            {
                switch (info.Kind)
                {
                    case FilterTierKind.Reached:
                        return ModEntry.L("tier_default_reached");
                    case FilterTierKind.Ok:
                        return ModEntry.L("tier_default_ok", info.Result.DurabilityConsumed);
                    case FilterTierKind.Short:
                        return ModEntry.L("tier_default_short", info.Result.DurabilityShortfall);
                    default:
                        return ModEntry.L("tier_default_impossible");
                }
            }

            string name = WaterFeatureHelper.WATER_DISPLAY_LOCALIZED[info.Index];
            string range = FilterSimulator.TierThresholdLabel(info.Threshold);
            if (_purifierMode)
            {
                return PurifierTierStatus(info, name, range);
            }

            switch (info.Kind)
            {
                case FilterTierKind.Reached:
                    return ModEntry.L("tier_reached", name, range);
                case FilterTierKind.Ok:
                    return ModEntry.L("tier_ok", name, range, info.Result.DurabilityConsumed);
                case FilterTierKind.Short:
                    return ModEntry.L("tier_short", name, range, info.Result.DurabilityShortfall);
                default:
                    if (info.Result.UnfilterableNames.Count > 0)
                    {
                        return ModEntry.L(
                            "tier_impossible",
                            name,
                            string.Join(", ", info.Result.UnfilterableNames),
                            FilterSimulator.FormatPurity(info.Result.TheoreticalMaxPurity));
                    }

                    return ModEntry.L(
                        "tier_impossible_max",
                        name,
                        FilterSimulator.FormatPurity(info.Result.TheoreticalMaxPurity));
            }
        }

        private static string PurifierTierStatus(FilterTierInfo info, string name, string range)
        {
            switch (info.Kind)
            {
                case FilterTierKind.Reached:
                    if (info.Result != null && info.Result.PurifierNeedsNoPrep)
                    {
                        return ModEntry.L("tier_no_prep", name, range);
                    }

                    return ModEntry.L("tier_reached", name, range);
                case FilterTierKind.Ok:
                    return ModEntry.L("tier_ok", name, range, info.Result.DurabilityConsumed);
                case FilterTierKind.Short:
                    if (info.Result != null && info.Result.NightsNeeded > 0)
                    {
                        return ModEntry.L(
                            "tier_nights",
                            name,
                            range,
                            FilterSimulator.FormatPurity(info.Result.PredictedPurityAfterNight),
                            info.Result.NightsNeeded);
                    }

                    return ModEntry.L(
                        "tier_nights_never",
                        name,
                        range,
                        FilterSimulator.FormatPurity(info.Result != null ? info.Result.PredictedPurityAfterNight : 0));
                default:
                    if (info.Result != null && info.Result.CapLimited)
                    {
                        return ModEntry.L(
                            "tier_cap_limited",
                            name,
                            FilterSimulator.FormatPurity(info.Result.PurifierCap),
                            info.Result.QualityBonus);
                    }

                    return ModEntry.L(
                        "tier_impossible_max",
                        name,
                        FilterSimulator.FormatPurity(info.Result != null ? info.Result.TheoreticalMaxPurity : 0));
            }
        }

        private static string ImpurityLine(FilterSimResult result)
        {
            if (result == null || result.ImpurityRemovedUl <= 0)
            {
                return ModEntry.L("preview_impurity_none");
            }

            return ModEntry.L("preview_result_impurity", FilterSimulator.FormatMl(result.ImpurityRemovedUl));
        }

        private static void PaintAddons(CustomUIWindow window)
        {
            bool showUv = FilterConfig.UseUv.Value;
            bool showTablets = FilterConfig.UseTablets.Value;
            window.Get("addons")?.SetVisible(showUv || showTablets);
            int tabletStock = _stock != null ? _stock.TabletCount : 0;
            PaintAddonChip(
                window,
                "opt_uv",
                showUv,
                _stock != null && _stock.HasUv,
                _useUv,
                "addon_uv",
                "addon_uv_none",
                "addon_uv_tip",
                v => _useUv = v);
            PaintAddonChip(
                window,
                "opt_tablets",
                showTablets,
                tabletStock > 0,
                _useTablets,
                "addon_tablets",
                "addon_tablets_none",
                "addon_tablets_tip",
                v => _useTablets = v);
        }

        private static void PaintAddonChip(
            CustomUIWindow window,
            string id,
            bool show,
            bool available,
            bool value,
            string labelKey,
            string noneKey,
            string tipKey,
            Action<bool> setValue)
        {
            CustomUIElement toggle = window.Get(id);
            toggle?.SetVisible(show);
            if (!show || toggle == null)
            {
                return;
            }

            if (!available)
            {
                setValue(false);
                value = false;
            }

            string tip = ModEntry.L(tipKey);
            toggle.SetText(available ? ModEntry.L(labelKey) : ModEntry.L(noneKey));
            toggle.SetInteractable(available);
            toggle.SetBool(value);
            toggle.SetTooltip(tip);
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
            FilterAddons addons = CurrentAddons();
            FilterSimResult result;
            if (_purifierMode && _purifier != null)
            {
                result = FilterSimulator.SimulateForPurifier(_container, _stock, _target, addons, _purifier);
            }
            else if (FilterSimulator.IsDefault(_selectedIndex))
            {
                result = FilterSimulator.SimulateOnePass(_container, _stock, addons);
            }
            else
            {
                result = FilterSimulator.Simulate(_container, _stock, _target, addons);
            }
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
            _useUv = false;
            _useTablets = false;
            _purifierMode = false;
            _purifier = null;
            ConsumeOverlays.Clear();
        }
        #endregion
    }
}
