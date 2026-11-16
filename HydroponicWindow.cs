using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace EasierFiltering
{
    /// <summary>
    /// 水培机窗口：直接过滤（与容器预览相同）或种植调配。净水器只出现在容器预览里。
    /// </summary>
    internal static class HydroponicWindow
    {
        public const string WINDOW_ID = "easier_filtering.hydroponic";
        private const string TagMode = "easier_filtering.hydro_mode";
        private const string TagTier = "easier_filtering.hydro_tier";
        private const string TagLo = "easier_filtering.hydro_lo";
        private const string TagHi = "easier_filtering.hydro_hi";
        private const string TagSig = "easier_filtering.hydro_sig";
        private static readonly Color TipColor = new Color(0.56f, 0.64f, 0.66f);
        private static readonly int[] TierButtonOrder = { FilterSimulator.DefaultIndex, 1, 0 };

        private static HydroInfo _info;
        private static FilterStock _stock;
        private static CustomUIWindow _window;
        private static HydroMode _mode;
        private static int _lo;
        private static int _hi;
        private static bool _painting;
        private static HydroPlan _plan;
        private static FilterSimResult _filterResult;
        private static FilterTierInfo[] _tiers;
        private static int _selectedIndex = FilterSimulator.DefaultIndex;
        private static int _target;
        private static bool _useUv;
        private static bool _useTablets;
        private static bool _sizesReady;
        private static float _headerSize = 13f;
        private static float _bodySize = 12f;
        private static float _detailSize = 11f;
        private static readonly Dictionary<string, Image> ConsumeOverlays = new Dictionary<string, Image>();

        #region MARK: 打开
        public static void Open(GameItem target)
        {
            if (target == null || CustomUIManager.Instance == null)
            {
                return;
            }

            _info = HydroponicProbe.FromTarget(target);
            if (_info == null)
            {
                return;
            }

            _stock = FilterInventory.Collect();
            _useUv = FilterConfig.UseUv.Value && _stock.HasUv;
            _useTablets = FilterConfig.UseTablets.Value && _stock.TabletCount > 0;
            _sizesReady = false;
            ConsumeOverlays.Clear();
            LoadSettings();
            Evaluate(resetTier: false);
            Build();
        }
        #endregion

        #region MARK: 构建
        private static void Build()
        {
            CustomUIBuilder builder = CustomUIManager.Instance.CreateWindow(
                WINDOW_ID,
                ModEntry.L("hydro_title"),
                CustomUIManager.LAYER_PAPER,
                null,
                CustomUIManager.Style.Machinery);
            if (builder == null)
            {
                return;
            }

            int sliderMin = SliderMin();
            int sliderMax = SliderMax();
            builder
                .UseMachineryStyle()
                .SetSize(480, 620)
                .SetDraggable(true)
                .SetCloseOnEscape(true)
                .OnClose(ClearState);

            builder.AddLabel("", "header_line");

            builder.BeginPanel(-1f, -1f, TextAnchor.UpperLeft, 8f, 4f, "panel_seed")
                .AddHeader(ModEntry.L("hydro_seed"), "header_seed")
                .AddLabel("", "seed_range")
                .AddLabel("", "seed_stats")
                .AddLabel("", "seed_traits");
            builder.End();

            builder.BeginRow();
            builder.AddButton(ModEntry.L("mode_direct"), () => SetMode(HydroMode.Direct), "mode_direct");
            builder.AddButton(ModEntry.L("hydro_mode_plant"), () => SetMode(HydroMode.Planting), "mode_plant");
            builder.End();

            builder.BeginPanel(-1f, -1f, TextAnchor.MiddleLeft, 6f, 2f, "addons");
            builder.BeginRow(6f, TextAnchor.MiddleLeft);
            builder.AddToggle(ModEntry.L("addon_uv"), false, v => OnAddonToggled(uv: true, v), "opt_uv");
            builder.AddToggle(ModEntry.L("addon_tablets"), false, v => OnAddonToggled(uv: false, v), "opt_tablets");
            builder.End();
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
                .AddLabel("", "tier_status")
                .AddLabel("", "tip_default")
                .AddLabel("", "tip_stop_at_tier");
            builder.End();

            builder.BeginPanel(-1f, -1f, TextAnchor.UpperLeft, 8f, 4f, "panel_range")
                .AddHeader(ModEntry.L("hydro_range"), "header_range");
            builder.BeginRow(6f, TextAnchor.MiddleLeft)
                .AddLabel(ModEntry.L("hydro_lo"), "lo_caption")
                .AddSlider(sliderMin, sliderMax, _lo, OnLoChanged, true, "slider_lo")
                .AddLabel("", "lo_value")
                .End();
            builder.BeginRow(6f, TextAnchor.MiddleLeft)
                .AddLabel(ModEntry.L("hydro_hi"), "hi_caption")
                .AddSlider(sliderMin, sliderMax, _hi, OnHiChanged, true, "slider_hi")
                .AddLabel("", "hi_value")
                .End();
            builder.BeginRow(6f, TextAnchor.MiddleLeft)
                .AddLabel("", "target_line")
                .AddFlexibleSpace()
                .AddButton(ModEntry.L("hydro_restore"), RestoreDefaults, "btn_restore")
                .End();
            builder.End();

            builder.BeginPanel(-1f, -1f, TextAnchor.UpperLeft, 8f, 4f, "panel_stock")
                .AddHeader(ModEntry.L("preview_stock"), "header_stock");
            FilterStockUi.AppendRows(builder);
            builder.AddLabel("", "stock_nutrients");
            builder.End();

            builder.BeginPanel(-1f, -1f, TextAnchor.UpperLeft, 8f, 4f, "panel_result")
                .AddHeader(ModEntry.L("preview_result"), "header_result")
                .AddLabel("", "result_purity")
                .AddLabel("", "result_volume")
                .AddLabel("", "result_mineral")
                .AddLabel("", "result_unsafe")
                .AddLabel("", "result_consume")
                .BeginRow(8f, TextAnchor.MiddleRight)
                    .AddLabel("", "result_impurity")
                    .AddLabel("", "result_cycles")
                    .AddFlexibleSpace()
                    .AddButton(ModEntry.L("hydro_confirm"), Confirm, "btn_confirm")
                .End()
                .AddWarning("", "warn_any");
            builder.End();

            _window = builder.Show();
            Paint(_window);
        }

        private static int SliderMin()
        {
            return _info != null && _info.HasSeed ? _info.DefaultLo : 0;
        }

        private static int SliderMax()
        {
            int min = SliderMin();
            int max = _info != null && _info.HasSeed ? _info.DefaultHi : 100;
            return Math.Max(min + 1, max);
        }
        #endregion

        #region MARK: 交互
        private static void SetMode(HydroMode mode)
        {
            if (mode == HydroMode.Planting && (_info == null || !_info.HasSeed))
            {
                return;
            }

            if (_mode == mode)
            {
                return;
            }

            _mode = mode;
            SaveSettings();
            Refresh(resetTier: false);
        }

        private static void OnTierSelected(int index)
        {
            if (_mode != HydroMode.Direct || _window == null || !_window.IsAlive)
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
            _filterResult = info.Result;
            SaveSettings();
            Paint(_window);
        }

        private static void OnAddonToggled(bool uv, bool value)
        {
            if (_painting)
            {
                return;
            }

            if (uv)
            {
                _useUv = value;
            }
            else
            {
                _useTablets = value;
            }

            Refresh(resetTier: false);
        }

        private static void OnLoChanged(float value)
        {
            if (_painting)
            {
                return;
            }

            int next = ClampBound(Mathf.RoundToInt(value));
            if (next > _hi)
            {
                _hi = next;
            }

            _lo = next;
            SaveSettings();
            Refresh(resetTier: false);
        }

        private static void OnHiChanged(float value)
        {
            if (_painting)
            {
                return;
            }

            int next = ClampBound(Mathf.RoundToInt(value));
            if (next < _lo)
            {
                _lo = next;
            }

            _hi = next;
            SaveSettings();
            Refresh(resetTier: false);
        }

        private static void RestoreDefaults()
        {
            if (_info == null || !_info.HasSeed)
            {
                return;
            }

            _lo = _info.DefaultLo;
            _hi = _info.DefaultHi;
            SaveSettings();
            Refresh(resetTier: false);
        }

        private static int ClampBound(int value)
        {
            int min = _info != null && _info.HasSeed ? _info.DefaultLo : 0;
            int max = _info != null && _info.HasSeed ? _info.DefaultHi : value;
            return Mathf.Clamp(value, min, Math.Max(min, max));
        }

        private static void Refresh(bool resetTier)
        {
            if (_info == null || _window == null || !_window.IsAlive)
            {
                return;
            }

            Evaluate(resetTier);
            Paint(_window);
        }

        private static void Evaluate(bool resetTier)
        {
            _stock = FilterInventory.Collect();
            if (_mode == HydroMode.Planting && (_info == null || !_info.HasSeed))
            {
                _mode = HydroMode.Direct;
            }

            _plan = null;
            _filterResult = null;
            _tiers = null;
            if (_info == null || !_info.HasWater)
            {
                return;
            }

            if (_mode == HydroMode.Planting)
            {
                _plan = NutrientDosing.Plan(_info, _stock, _lo, _hi);
                return;
            }

            _tiers = FilterSimulator.EvaluateTiers(_info.Water, _stock, CurrentAddons());
            FilterTierInfo pick = resetTier ? null : FindTier(_selectedIndex);
            if (pick == null || ShouldDropTier(pick))
            {
                pick = FilterSimulator.PickDefaultTier(_tiers);
            }

            if (pick == null)
            {
                _selectedIndex = FilterSimulator.DefaultIndex;
                _target = 0;
                _filterResult = FilterSimulator.SimulateOnePass(_info.Water, _stock, CurrentAddons());
                return;
            }

            _selectedIndex = pick.Index;
            _target = pick.Threshold;
            _filterResult = pick.Result;
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
                && (info.Kind == FilterTierKind.Reached || info.Kind == FilterTierKind.Impossible);
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

        #region MARK: 刷新
        private static void Paint(CustomUIWindow window)
        {
            _painting = true;
            try
            {
                CaptureSizes(window);
                bool planting = _mode == HydroMode.Planting;
                SetText(window, "header_line", HeaderLine());
                PaintSeed(window);
                PaintModes(window);
                window.Get("addons")?.SetVisible(!planting);
                window.Get("panel_target")?.SetVisible(!planting);
                window.Get("panel_range")?.SetVisible(planting);
                if (!planting)
                {
                    PaintAddons(window);
                    PaintTiers(window);
                    SetText(window, "tier_status", TierStatusLine());
                    bool isDefault = FilterSimulator.IsDefault(_selectedIndex);
                    SetWarning(window, "tip_default", isDefault, ModEntry.L("tip_default"));
                    SetWarning(window, "tip_stop_at_tier", !isDefault, ModEntry.L("tip_stop_at_tier"));
                    FilterStockUi.Paint(window, _stock, _filterResult, _useTablets, ConsumeOverlays);
                    SetWarning(window, "stock_nutrients", false, "");
                }
                else
                {
                    FilterStockUi.Hide(window, ConsumeOverlays);
                    PaintRange(window);
                    PaintNutrientStock(window);
                }

                PaintResult(window);
                StyleBody(window, "header_line");
                StyleBody(window, "seed_range");
                StyleDetail(window, "seed_stats");
                StyleDetail(window, "seed_traits");
                StyleHeader(window, "header_seed");
                StyleHeader(window, "header_target");
                StyleHeader(window, "header_range");
                StyleHeader(window, "header_stock");
                StyleHeader(window, "header_result");
                StyleBody(window, "tier_status");
                StyleDetail(window, "tip_default");
                StyleDetail(window, "tip_stop_at_tier");
                StyleBody(window, "target_line");
                StyleBody(window, "stock_nutrients");
                StyleBody(window, "result_purity");
                StyleBody(window, "result_volume");
                StyleBody(window, "result_mineral");
                StyleDetail(window, "result_unsafe");
                StyleDetail(window, "result_consume");
                StyleDetail(window, "result_impurity");
                StyleBody(window, "result_cycles");
                StyleDetail(window, "warn_any");
                CustomUIElement confirm = window.Get("btn_confirm");
                confirm?.SetInteractable(CanConfirm());
                confirm?.SetText(ConfirmLabel());
                confirm?.SetPreferredSize(120f, 28f);
                window.Get("lo_caption")?.SetPreferredSize(72f, 22f);
                window.Get("hi_caption")?.SetPreferredSize(72f, 22f);
                window.Get("lo_value")?.SetPreferredSize(64f, 22f);
                window.Get("hi_value")?.SetPreferredSize(64f, 22f);
                StyleBody(window, "lo_caption");
                StyleBody(window, "hi_caption");
                StyleBody(window, "lo_value");
                StyleBody(window, "hi_value");
                FitWindow(window);
            }
            finally
            {
                _painting = false;
            }
        }

        private static void PaintSeed(CustomUIWindow window)
        {
            if (_info == null || !_info.HasSeed)
            {
                SetText(window, "seed_range", ModEntry.L("hydro_warn_no_seed"));
                SetText(window, "seed_stats", " ");
                SetText(window, "seed_traits", " ");
                return;
            }

            string currentRange = ModEntry.L(
                "hydro_seed_range",
                FilterSimulator.FormatPurity(_info.DefaultLo),
                FilterSimulator.FormatPurity(_info.DefaultHi));
            if (_info.IsModified && _info.BaseRange != _info.Range)
            {
                int baseLo = Math.Max(0, _info.BaseLevel - _info.BaseRange);
                int baseHi = _info.BaseLevel + _info.BaseRange;
                currentRange = ModEntry.L(
                    "hydro_seed_range_mod",
                    FilterSimulator.FormatPurity(_info.DefaultLo),
                    FilterSimulator.FormatPurity(_info.DefaultHi),
                    FilterSimulator.FormatPurity(baseLo),
                    FilterSimulator.FormatPurity(baseHi),
                    _info.RangeMultiplier.ToString("0.00"));
            }

            SetText(window, "seed_range", currentRange);

            string water = _info.WaterUsePerCycle == _info.BaseWaterUse || !_info.IsModified
                ? ModEntry.L("hydro_seed_water", _info.WaterUsePerCycle)
                : ModEntry.L("hydro_seed_water_mod", _info.WaterUsePerCycle, _info.BaseWaterUse);
            SetText(window, "seed_stats", ModEntry.L(
                "hydro_seed_stats",
                _info.CombinedSensitivity.ToString("0.00"),
                HydroponicProbe.SensitivityLabel(_info),
                water));
            SetText(window, "seed_traits", ModEntry.L("hydro_seed_traits", HydroponicProbe.FormatTraits(_info)));
        }

        private static void PaintModes(CustomUIWindow window)
        {
            PaintModeButton(
                window,
                "mode_direct",
                _mode == HydroMode.Direct,
                available: true,
                ModEntry.L("mode_direct"),
                null);
            bool canPlant = _info != null && _info.HasSeed;
            PaintModeButton(
                window,
                "mode_plant",
                _mode == HydroMode.Planting,
                canPlant,
                ModEntry.L("hydro_mode_plant"),
                canPlant ? ModEntry.L("hydro_tip_plant") : ModEntry.L("hydro_warn_no_seed"));
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
            button.SetTooltip(tip ?? "");
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

            toggle.SetText(available ? ModEntry.L(labelKey) : ModEntry.L(noneKey));
            toggle.SetInteractable(available);
            toggle.SetBool(value);
            toggle.SetTooltip(ModEntry.L(tipKey));
        }

        private static void PaintTiers(CustomUIWindow window)
        {
            foreach (int index in TierButtonOrder)
            {
                PaintTierButton(window, index);
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

            button.SetVisible(true);
            button.SetText(name);
            button.SetTextColor(MachineryStyle.BUTTON_TEXT);
            button.SetInteractable(selected || !locked);
        }

        private static void PaintRange(CustomUIWindow window)
        {
            bool enabled = _info != null && _info.HasSeed;
            window.Get("slider_lo")?.SetInteractable(enabled);
            window.Get("slider_hi")?.SetInteractable(enabled);
            window.Get("btn_restore")?.SetInteractable(enabled);
            if (enabled)
            {
                window.Get("slider_lo")?.SetFloat(_lo);
                window.Get("slider_hi")?.SetFloat(_hi);
            }

            SetText(window, "lo_value", FilterSimulator.FormatPurity(_lo));
            SetText(window, "hi_value", FilterSimulator.FormatPurity(_hi));
            int target = _lo + (_hi - _lo) / 2;
            SetText(window, "target_line", ModEntry.L("hydro_target", FilterSimulator.FormatPurity(target)));
        }

        private static void PaintNutrientStock(CustomUIWindow window)
        {
            int tablets = _stock != null ? _stock.NutrientTabletCount : 0;
            int used = _plan?.Dose != null ? _plan.Dose.Count : 0;
            SetWarning(window, "stock_nutrients", true, ModEntry.L("hydro_stock_tablets", used, tablets));
        }

        private static void PaintResult(CustomUIWindow window)
        {
            if (_mode == HydroMode.Planting)
            {
                SetWarning(window, "result_purity", false, "");
                SetWarning(window, "result_volume", false, "");
                SetWarning(window, "result_impurity", false, "");
                if (_plan?.Dose == null)
                {
                    SetText(window, "result_mineral", " ");
                    SetText(window, "result_unsafe", " ");
                    SetText(window, "result_consume", " ");
                    SetText(window, "result_cycles", " ");
                    SetWarning(window, "warn_any", !string.IsNullOrEmpty(_plan?.Warning), _plan?.Warning ?? "");
                    return;
                }

                string inRange = _plan.Dose.InRange ? ModEntry.L("hydro_in_range") : ModEntry.L("hydro_out_range");
                SetText(window, "result_mineral", ModEntry.L(
                    "hydro_result_mineral",
                    FilterSimulator.FormatPurity(_plan.Dose.MineralBefore),
                    FilterSimulator.FormatPurity(_plan.Dose.MineralAfter),
                    inRange));

                if (_plan.PlantSafeAfter)
                {
                    SetText(window, "result_unsafe", ModEntry.L("hydro_result_safe"));
                }
                else if (_plan.UnsafeAfter.Count > 0)
                {
                    SetText(window, "result_unsafe", ModEntry.L(
                        "hydro_result_unsafe",
                        string.Join(ModEntry.L("hydro_trait_sep"), _plan.UnsafeAfter)));
                }
                else
                {
                    SetText(window, "result_unsafe", " ");
                }

                SetText(window, "result_consume", ModEntry.L("hydro_result_consume", _plan.Dose.Count));
                SetText(window, "result_cycles", ModEntry.L("hydro_result_cycles", _plan.Cycles));
                SetWarning(window, "warn_any", !string.IsNullOrEmpty(_plan.Warning), _plan.Warning ?? "");
                return;
            }

            SetWarning(window, "result_mineral", false, "");
            SetWarning(window, "result_unsafe", false, "");
            SetWarning(window, "result_consume", false, "");
            SetWarning(window, "result_cycles", false, "");
            FilterSimResult result = _filterResult;
            if (result == null)
            {
                SetWarning(window, "result_purity", false, "");
                SetWarning(window, "result_volume", false, "");
                SetWarning(window, "result_impurity", false, "");
                string missing = _info != null && _info.HasWater
                    ? ""
                    : ModEntry.L("hydro_warn_no_water");
                SetWarning(window, "warn_any", !string.IsNullOrEmpty(missing), missing);
                return;
            }

            SetWarning(
                window,
                "result_purity",
                true,
                ModEntry.L(
                    "preview_result_purity",
                    FilterSimulator.FormatPurity(result.PurityBefore),
                    FilterSimulator.FormatPurity(result.PurityAfter),
                    FilterSimulator.QualityName(result.PurityAfter)));
            SetWarning(
                window,
                "result_volume",
                true,
                ModEntry.L(
                    "preview_result_volume",
                    result.VolumeBeforeUl / 1000,
                    result.VolumeAfterUl / 1000));
            SetWarning(window, "result_impurity", true, ImpurityLine(result));
            PaintFilterWarning(window, result);
        }

        private static string ImpurityLine(FilterSimResult result)
        {
            if (result == null || result.ImpurityRemovedUl <= 0)
            {
                return ModEntry.L("preview_impurity_none");
            }

            return ModEntry.L("preview_result_impurity", FilterSimulator.FormatMl(result.ImpurityRemovedUl));
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
                            string.Join(ModEntry.L("hydro_trait_sep"), info.Result.UnfilterableNames),
                            FilterSimulator.FormatPurity(info.Result.TheoreticalMaxPurity));
                    }

                    return ModEntry.L(
                        "tier_impossible_max",
                        name,
                        FilterSimulator.FormatPurity(info.Result.TheoreticalMaxPurity));
            }
        }

        private static void PaintFilterWarning(CustomUIWindow window, FilterSimResult result)
        {
            bool noFilter = result.NoMechanicalFilters && !result.HasUv && result.TabletsConsumed <= 0;
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
            else if (unfilterable && (!result.ReachedTarget || result.IsOnePass))
            {
                text = ModEntry.L(
                    "warn_unfilterable",
                    string.Join(ModEntry.L("hydro_trait_sep"), result.UnfilterableNames),
                    FilterSimulator.FormatPurity(result.TheoreticalMaxPurity));
            }
            else
            {
                text = "";
            }

            SetWarning(window, "warn_any", !string.IsNullOrEmpty(text), text);
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
            const float pad = 20f;
            float height = Mathf.Clamp(preferred + header + rule + pad * 2f, 360f, 720f);
            window.SetSize(480f, height);
        }
        #endregion

        #region MARK: 文案
        private static string HeaderLine()
        {
            if (_info == null)
            {
                return "";
            }

            string plant = _info.HasSeed ? _info.PlantName : ModEntry.L("hydro_no_seed");
            if (_info.HasSeed && _info.IsModified)
            {
                plant = ModEntry.L("hydro_modified", plant);
            }

            string water;
            if (!_info.HasWater)
            {
                water = ModEntry.L("hydro_no_water");
            }
            else
            {
                water = ModEntry.L(
                    "hydro_water_line",
                    WaterHelper.GetCurrentCapacityML(_info.Water),
                    WaterHelper.GetTotalCapacityML(_info.Water));
            }

            return ModEntry.L("hydro_header", plant, water);
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

        private static void CaptureSizes(CustomUIWindow window)
        {
            if (_sizesReady)
            {
                return;
            }

            float baseline = 14f;
            TMP_Text sample = window.Get("header_line")?.text;
            if (sample != null && sample.fontSize > 0f)
            {
                baseline = sample.fontSize;
            }

            _headerSize = Mathf.Max(11f, baseline - 1f);
            _bodySize = Mathf.Max(10f, baseline - 2f);
            _detailSize = Mathf.Max(9f, baseline - 3f);
            _sizesReady = true;
        }

        private static void StyleHeader(CustomUIWindow window, string id)
        {
            CustomUIElement element = window.Get(id);
            if (element?.text == null)
            {
                return;
            }

            element.text.richText = true;
            element.text.fontSize = _headerSize;
            element.text.color = MachineryStyle.VALUE_TEXT;
            element.text.fontStyle = FontStyles.Bold;
            element.text.enableWordWrapping = false;
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
            layout.minWidth = 80f;
            layout.minHeight = _headerSize + 6f;
            layout.preferredHeight = _headerSize + 8f;
        }

        private static void StyleBody(CustomUIWindow window, string id)
        {
            StyleText(window.Get(id), _bodySize, MachineryStyle.BODY_TEXT, bold: false, wrap: true);
        }

        private static void StyleDetail(CustomUIWindow window, string id)
        {
            StyleText(window.Get(id), _detailSize, TipColor, bold: false, wrap: true);
        }

        private static void StyleText(CustomUIElement element, float size, Color color, bool bold, bool wrap)
        {
            if (element?.text == null)
            {
                return;
            }

            element.text.richText = true;
            element.text.fontSize = size;
            element.text.color = color;
            element.text.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            element.text.enableWordWrapping = wrap;
            element.text.overflowMode = wrap ? TextOverflowModes.Overflow : TextOverflowModes.Truncate;
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
            layout.minHeight = size + 2f;
        }
        #endregion

        #region MARK: 记忆
        private static void LoadSettings()
        {
            _mode = PreferredMode();
            _selectedIndex = FilterSimulator.DefaultIndex;
            _lo = _info != null && _info.HasSeed ? _info.DefaultLo : 0;
            _hi = _info != null && _info.HasSeed ? _info.DefaultHi : 100;
            GameItem machine = _info?.Machine;
            if (machine == null)
            {
                return;
            }

            bool samePlant = _info.HasSeed
                && machine.IsTag(TagSig)
                && machine.GetTagReadonly(TagSig).GetString() == _info.Signature;
            if (samePlant && machine.IsTag(TagMode))
            {
                _mode = ReadMode(machine.GetTagReadonly(TagMode).GetInt());
            }

            if (samePlant && machine.IsTag(TagTier))
            {
                _selectedIndex = machine.GetTagReadonly(TagTier).GetInt();
            }

            if (!_info.HasSeed)
            {
                _mode = HydroMode.Direct;
                return;
            }

            if (samePlant)
            {
                if (machine.IsTag(TagLo))
                {
                    _lo = ClampBound(machine.GetTagReadonly(TagLo).GetInt());
                }

                if (machine.IsTag(TagHi))
                {
                    _hi = ClampBound(machine.GetTagReadonly(TagHi).GetInt());
                }

                if (_lo > _hi)
                {
                    int swap = _lo;
                    _lo = _hi;
                    _hi = swap;
                }
            }
        }

        private static HydroMode PreferredMode()
        {
            if (_info == null || !_info.HasSeed || HydroponicProbe.IsHydroreed(_info))
            {
                return HydroMode.Direct;
            }

            return FilterConfig.HydroDefaultMode;
        }

        private static void SaveSettings()
        {
            GameItem machine = _info?.Machine;
            if (machine == null)
            {
                return;
            }

            int mode = (int)_mode;
            int tier = _selectedIndex;
            machine.ModifyTag(TagMode, state =>
            {
                state.Enable().SetInt(mode);
            });
            machine.ModifyTag(TagTier, state =>
            {
                state.Enable().SetInt(tier);
            });

            if (!_info.HasSeed)
            {
                return;
            }

            int lo = _lo;
            int hi = _hi;
            string sig = _info.Signature;
            machine.ModifyTag(TagLo, state =>
            {
                state.Enable().SetInt(lo);
            });
            machine.ModifyTag(TagHi, state =>
            {
                state.Enable().SetInt(hi);
            });
            machine.ModifyTag(TagSig, state =>
            {
                state.Enable().SetString(sig);
            });
        }

        private static HydroMode ReadMode(int stored)
        {
            return stored == 2 || stored == (int)HydroMode.Planting
                ? HydroMode.Planting
                : HydroMode.Direct;
        }
        #endregion

        #region MARK: 确认 / 关闭
        private static bool CanConfirm()
        {
            if (_info == null || !_info.HasWater)
            {
                return false;
            }

            if (_mode == HydroMode.Planting)
            {
                return _plan != null && _plan.CanConfirm;
            }

            return _filterResult != null && !_filterResult.NoChange;
        }

        private static string ConfirmLabel()
        {
            return _mode == HydroMode.Planting
                ? ModEntry.L("hydro_confirm")
                : ModEntry.L("preview_confirm");
        }

        private static void Confirm()
        {
            if (_info == null || !_info.HasWater)
            {
                Close();
                return;
            }

            Evaluate(resetTier: false);
            if (!CanConfirm())
            {
                Close();
                return;
            }

            SaveSettings();
            if (_mode == HydroMode.Planting)
            {
                if (_plan != null && _plan.Dose.Count > 0)
                {
                    NutrientDosing.Apply(_info.Water, _plan.Dose.Count);
                }
            }
            else
            {
                FilterAddons addons = CurrentAddons();
                FilterSimResult result = FilterSimulator.IsDefault(_selectedIndex)
                    ? FilterSimulator.SimulateOnePass(_info.Water, _stock, addons)
                    : FilterSimulator.Simulate(_info.Water, _stock, _target, addons);
                if (!result.NoChange)
                {
                    FilterInventory.Apply(_info.Water, result);
                }
            }

            Close();
        }

        private static void Close()
        {
            CustomUIManager.Instance?.CloseWindow(WINDOW_ID);
            ClearState();
        }

        private static void ClearState()
        {
            _info = null;
            _stock = null;
            _window = null;
            _plan = null;
            _filterResult = null;
            _tiers = null;
            _useUv = false;
            _useTablets = false;
            _painting = false;
            _sizesReady = false;
            ConsumeOverlays.Clear();
        }
        #endregion
    }
}
