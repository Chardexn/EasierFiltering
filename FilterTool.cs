using System;
using System.Collections.Generic;

namespace EasierFiltering
{
    /// <summary>
    /// 手持过滤工具。对准装水容器后打开预览窗口。
    /// </summary>
    internal static class FilterTool
    {
        public const string ITEM_ID = "easier_filtering.filter_tool";

        #region MARK: 注册
        public static GameItem Create()
        {
            GameItem item = ItemDirectory.CreateEmptyItem(ITEM_ID);
            item.name = ModEntry.L("tool_name");
            item.shortDescription = ModEntry.L("tool_desc");
            item.flavorText = ModEntry.L("tool_flavor");
            item.unitValue = 300L;
            item.SetGameItemType(TypeHelper.TOOL);
            item.SetGameItemType(TypeHelper.WATER_PURIFICATION_SUPPLY);
            item.SetSpriteAndShapeFromMod(ModEntry.MOD_ID, "filter_tool");
            item.onLoaded = (Action<GameItem>)Delegate.Combine(item.onLoaded, new Action<GameItem>(RefreshSprite));
            AudioHelper.InitScanner(item);

            item.maySelectSlotItemFunc = (GameItem s, GameInventory _, SlotMarker __) => GeneralHelper.IsItemOwned(s);
            item.canSelectSlotItemFunc = (GameItem s, GameInventory _, SlotMarker __) => GeneralHelper.IsItemOwned(s);
            item.mayItemTargetItemFunc = (GameItem s, GameItem t) => CanTarget(s, t);
            item.canItemTargetItemFunc = (GameItem s, GameItem t) => CanTarget(s, t);
            item.onItemTargetItemFunc = (GameItem s, GameItem t) => OnTarget(t);
            return item;
        }

        private static bool CanTarget(GameItem tool, GameItem target)
        {
            if (!GeneralHelper.IsItemOwned(tool) || !GeneralHelper.IsItemOwned(target))
            {
                return false;
            }

            if (target != null && target.IsTag(MachineHydroponic.HYDROPONIC_MACHINE_TAG))
            {
                return true;
            }

            if (target == null || !target.IsTag(WaterHelper.LIQUID_CONTAINER_TAG))
            {
                return false;
            }

            return !WaterHelper.IsEmpty(target);
        }

        private static void OnTarget(GameItem target)
        {
            if (HydroponicProbe.IsHydroponicTarget(target))
            {
                HydroponicWindow.Open(target);
                return;
            }

            FilterPreviewWindow.Open(target);
        }

        private static void RefreshSprite(GameItem item)
        {
            if (item != null)
            {
                item.unitValue = 300L;
                item.SetSpriteAndShapeFromMod(ModEntry.MOD_ID, "filter_tool");
            }
        }
        #endregion

        #region MARK: F9
        public static void RegisterDebugFunctions()
        {
            ModHelper.AddDebugFunction(ModEntry.MOD_ID, ModHelper.DEBUG_CATEGORY_SPAWN, "Spawn Filter Tool",
                () => Place(ModHelper.SpawnItem(ITEM_ID)));
            ModHelper.AddDebugFunction(ModEntry.MOD_ID, ModHelper.DEBUG_CATEGORY_SPAWN, "Offer Filter Tool (Jackson)",
                () =>
                {
                    if (PlayerStore.Instance != null)
                    {
                        PlayerStore.Instance.AddDirectSellingItemToTable(ITEM_ID);
                    }
                });
            ModHelper.AddDebugFunction(ModEntry.MOD_ID, ModHelper.DEBUG_CATEGORY_SPAWN, "Spawn Water Filters x4",
                () =>
                {
                    for (int i = 0; i < 4; i++)
                    {
                        Place(PreBuiltItemHelper.WaterFilter());
                    }
                });
            ModHelper.AddDebugFunction(ModEntry.MOD_ID, ModHelper.DEBUG_CATEGORY_SPAWN, "Spawn Adv Filter",
                () => Place(ModHelper.SpawnItem("water_filter_adv")));
            ModHelper.AddDebugFunction(ModEntry.MOD_ID, ModHelper.DEBUG_CATEGORY_SPAWN, "Spawn UV Lamp",
                () => Place(ModHelper.SpawnItem("uv_filter")));
            ModHelper.AddDebugFunction(ModEntry.MOD_ID, ModHelper.DEBUG_CATEGORY_SPAWN, "Spawn Tablet Box",
                () => Place(PreBuiltItemHelper.FullWaterPurificationTabletBox()));
            ModHelper.AddDebugFunction(ModEntry.MOD_ID, ModHelper.DEBUG_CATEGORY_SPAWN, "Spawn Dirty Water Jug",
                () =>
                {
                    GameItem jug = ModHelper.SpawnItem("water_jug");
                    if (jug != null)
                    {
                        WaterHelper.AddWater(jug, 5);
                        Place(jug);
                    }
                });
            ModHelper.AddDebugFunction(ModEntry.MOD_ID, ModHelper.DEBUG_CATEGORY_SPAWN, "Spawn Water Purifier",
                () => Place(PreBuiltItemHelper.CreateWaterPurifier()));
            ModHelper.AddDebugFunction(ModEntry.MOD_ID, ModHelper.DEBUG_CATEGORY_SPAWN, "Spawn Hydroponic",
                () => Place(PreBuiltItemHelper.CreateHydroponic()));
            ModHelper.AddDebugFunction(ModEntry.MOD_ID, ModHelper.DEBUG_CATEGORY_SPAWN, "Spawn Nutrifruit Seed",
                () => Place(ModHelper.SpawnItem("nutrifruit_seed")));
            ModHelper.AddDebugFunction(ModEntry.MOD_ID, ModHelper.DEBUG_CATEGORY_SPAWN, "Spawn Nutrient Tablets",
                () => Place(PreBuiltItemHelper.FullNutrientTabletBox()));
            ModHelper.AddDebugFunction(ModEntry.MOD_ID, ModHelper.DEBUG_CATEGORY_SPAWN, "Spawn Battery",
                () => Place(ModHelper.SpawnItem("energy_credit")));
            ModHelper.AddDebugFunction(ModEntry.MOD_ID, ModHelper.DEBUG_CATEGORY_SPAWN, "Spawn Quality Module",
                () => Place(PreBuiltItemHelper.RandomQualityModule()));
            ModHelper.AddDebugFunction(ModEntry.MOD_ID, ModHelper.DEBUG_CATEGORY_COMMAND, "Self-test Filter Math", RunSelfTest);
            ModHelper.AddDebugFunction(ModEntry.MOD_ID, ModHelper.DEBUG_CATEGORY_COMMAND, "Self-test Hydro Dosing", RunHydroSelfTest);
        }

        private static void RunSelfTest()
        {
            ModLog log = ModEntry.Instance?.Log;
            GameItem jug = ModHelper.SpawnItem("water_jug");
            if (jug == null)
            {
                log?.Error("Self-test: failed to spawn water_jug.");
                return;
            }

            WaterHelper.AddWater(jug, 5);
            GameItem vanillaFilter = PreBuiltItemHelper.WaterFilter();
            GameItem simFilter = PreBuiltItemHelper.WaterFilter();
            int[] parts = WaterHelper.GetLiquidParts(jug);
            int[] vanilla = (int[])parts.Clone();
            for (int i = 0; i < Liquid.Liquids.Count; i++)
            {
                vanilla[i] = WaterHelper.HandleFilter(Liquid.Liquids[i].identifier, vanilla[i], jug, vanillaFilter);
            }

            FilterSnapshot snap = new FilterSnapshot
            {
                Identifier = simFilter.identifier,
                DisplayName = simFilter.name,
                PoreSize = simFilter.GetTagReadonly(Liquid.FILTER_SIZE_TAG).GetInt(),
                Durability = DurabilityHelper.GetRealDurability(simFilter),
                DurabilityBefore = DurabilityHelper.GetRealDurability(simFilter),
                MaxDurability = DurabilityHelper.GetMaxDurability(simFilter)
            };
            int[] ours = FilterSimulator.ApplyOnePassForTest(parts, new List<FilterSnapshot> { snap });
            int vanillaPurity = FilterSimulator.PurityOf(vanilla);
            int ourPurity = FilterSimulator.PurityOf(ours);
            int vanillaDur = DurabilityHelper.GetRealDurability(vanillaFilter);
            bool partsMatch = vanilla.Length == ours.Length;
            if (partsMatch)
            {
                for (int i = 0; i < vanilla.Length; i++)
                {
                    if (vanilla[i] != ours[i])
                    {
                        partsMatch = false;
                        break;
                    }
                }
            }

            bool durMatch = vanillaDur == snap.Durability;
            if (partsMatch && durMatch && vanillaPurity == ourPurity)
            {
                log?.Log($"Self-test passed. Purity {FilterSimulator.FormatPurity(vanillaPurity)}, durability left {vanillaDur}.");
            }
            else
            {
                log?.Error($"Self-test mismatch. vanilla purity={vanillaPurity} ours={ourPurity} vanillaDur={vanillaDur} simDur={snap.Durability} partsMatch={partsMatch}");
            }

            ModHelper.MoveItemToTrash(jug);
            ModHelper.MoveItemToTrash(vanillaFilter);
            ModHelper.MoveItemToTrash(simFilter);
        }

        private static void RunHydroSelfTest()
        {
            ModLog log = ModEntry.Instance?.Log;
            GameItem jug = ModHelper.SpawnItem("water_jug");
            if (jug == null)
            {
                log?.Error("Hydro self-test: failed to spawn water_jug.");
                return;
            }

            WaterHelper.AddWater(jug, 5);
            int[] parts = WaterHelper.GetLiquidParts(jug);
            int lo = 50;
            int hi = 250;
            DoseResult dose = NutrientDosing.SimulateDose(parts, WaterHelper.GetTotalCapacity(jug), lo, hi, 12);
            bool inRange = dose.InRange && dose.MineralAfter >= lo && dose.MineralAfter <= hi;
            int mid = lo + (hi - lo) / 2;
            if (inRange)
            {
                log?.Log($"Hydro self-test passed. Tablets {dose.Count}, mineral {FilterSimulator.FormatPurity(dose.MineralBefore)} → {FilterSimulator.FormatPurity(dose.MineralAfter)} (target {FilterSimulator.FormatPurity(mid)}).");
            }
            else
            {
                log?.Error($"Hydro self-test failed. Tablets {dose.Count}, mineral {dose.MineralAfter}, inRange={dose.InRange} overshoot={dose.Overshoot}");
            }

            ModHelper.MoveItemToTrash(jug);
        }

        private static void Place(GameItem item)
        {
            if (item == null)
            {
                return;
            }

            ModHelper.SetItemOwnerShip(item, true);
            ModHelper.MoveItemToBackCounter(item);
            if (item.parentInventory == null)
            {
                ModHelper.MoveItemToInventory(item);
            }
        }
        #endregion
    }
}
