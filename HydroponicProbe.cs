using System;
using System.Collections.Generic;
using System.Text;

namespace EasierFiltering
{
    internal sealed class HydroTraitInfo
    {
        public string Name;
        public int Rank;
        public string RankLabel;
    }

    /// <summary>
    /// 水培机、水槽与种子快照。数值全部走 HydroponicHelper，基因改造会自动生效。
    /// </summary>
    internal sealed class HydroInfo
    {
        public GameItem Machine;
        public GameItem Water;
        public GameItem Seed;
        public string PlantId;
        public string PlantName;
        public int Level;
        public int Range;
        public int BaseLevel;
        public int BaseRange;
        public int BaseWaterUse;
        public double Sensitivity;
        public double Effect;
        public int WaterUsePerCycle;
        public int MineralUseRate;
        public int ProcessingSpeed;
        public bool IsModified;
        public List<HydroTraitInfo> Traits = new List<HydroTraitInfo>();

        public int DefaultLo => Math.Max(0, Level - Range);
        public int DefaultHi => Level + Range;
        public int CapacityUl => Water != null ? WaterHelper.GetTotalCapacity(Water) : 0;
        public bool HasWater => Water != null && !WaterHelper.IsEmpty(Water);
        public bool HasSeed => Seed != null && Seed.IsTag(HydroponicHelper.SEED_ITEM_TAG);
        public string Signature => $"{PlantId}|{Level}|{Range}";
        public double RangeMultiplier => BaseRange > 0 ? Range / (double)BaseRange : 1.0;
        public double CombinedSensitivity => Effect * Sensitivity;
    }

    /// <summary>
    /// 从工具目标定位水培机，并读取种子的矿物质区间、敏感度与性状。
    /// </summary>
    internal static class HydroponicProbe
    {
        public const string MACHINE_ID = "hydroponic";
        public const string HYDROREED_ID = "hydroreed";
        public const string HYDROREED_SEED_ID = "hydroreed_seed";

        #region MARK: 查找
        public static bool IsHydroreed(HydroInfo info)
        {
            if (info == null)
            {
                return false;
            }

            if (IsHydroreedId(info.PlantId) || IsHydroreedId(info.Seed?.identifier))
            {
                return true;
            }

            return false;
        }

        private static bool IsHydroreedId(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return false;
            }

            return id == HYDROREED_ID
                || id == HYDROREED_SEED_ID
                || id.StartsWith("hydroreed", StringComparison.Ordinal);
        }

        public static bool IsHydroponicTarget(GameItem target)
        {
            if (target == null)
            {
                return false;
            }

            if (target.IsTag(MachineHydroponic.HYDROPONIC_MACHINE_TAG))
            {
                return true;
            }

            return FindMachine(target) != null;
        }

        public static HydroInfo FromTarget(GameItem target)
        {
            if (target == null)
            {
                return null;
            }

            try
            {
                GameItem machine = target.IsTag(MachineHydroponic.HYDROPONIC_MACHINE_TAG)
                    ? target
                    : FindMachine(target);
                if (machine == null)
                {
                    return null;
                }

                return Read(machine);
            }
            catch (Exception ex)
            {
                ModEntry.Instance?.Log?.Log($"HydroponicProbe failed: {ex.Message}");
                return null;
            }
        }

        private static GameItem FindMachine(GameItem item)
        {
            List<GameItem> parents = GraphUtils.FindParentsType<GameItem>(
                item,
                candidate => candidate != null && candidate.IsTag(MachineHydroponic.HYDROPONIC_MACHINE_TAG),
                _ => true);
            if (parents != null)
            {
                foreach (GameItem parent in parents)
                {
                    if (parent != null && OwnsSlot(parent, item))
                    {
                        return parent;
                    }
                }
            }

            if (EmporiumEntry.Instance == null)
            {
                return null;
            }

            foreach (GameItem candidate in EmporiumEntry.Instance.GetAllOwnedItems())
            {
                if (candidate == null || !candidate.IsTag(MachineHydroponic.HYDROPONIC_MACHINE_TAG))
                {
                    continue;
                }

                if (OwnsSlot(candidate, item))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static bool OwnsSlot(GameItem machine, GameItem item)
        {
            try
            {
                if (MachineHydroponic.GetWaterSlotItem(machine) == item)
                {
                    return true;
                }

                GameItem seed = MachineHydroponic.GetInputSlot(machine)?.childItem;
                return seed == item;
            }
            catch (Exception)
            {
                return false;
            }
        }
        #endregion

        #region MARK: 读取
        public static HydroInfo Read(GameItem machine)
        {
            GameItem water = null;
            GameItem seed = null;
            try
            {
                water = MachineHydroponic.GetWaterSlotItem(machine);
            }
            catch (Exception)
            {
            }

            try
            {
                seed = MachineHydroponic.GetInputSlot(machine)?.childItem;
            }
            catch (Exception)
            {
            }

            HydroInfo info = new HydroInfo
            {
                Machine = machine,
                Water = water,
                Seed = seed,
                ProcessingSpeed = ReadProcessingSpeed(machine)
            };

            if (seed == null || !seed.IsTag(HydroponicHelper.SEED_ITEM_TAG))
            {
                return info;
            }

            PlantDefinition plant = HydroponicHelper.GetPlant(seed);
            info.PlantId = HydroponicHelper.GetPlantId(seed);
            info.PlantName = string.IsNullOrEmpty(seed.name)
                ? (plant != null ? plant.displayName : info.PlantId)
                : seed.name;
            info.Level = HydroponicHelper.GetNutrimentLevel(seed);
            info.Range = HydroponicHelper.GetNutrimentRange(seed);
            info.BaseLevel = plant?.nutrimentLevel ?? info.Level;
            info.BaseRange = plant?.nutrimentRange ?? info.Range;
            info.BaseWaterUse = plant?.waterUseRate ?? HydroponicHelper.GetWaterUsePerCycle(seed);
            info.Sensitivity = HydroponicHelper.GetContaminantSensitivity(seed);
            info.Effect = HydroponicHelper.GetContaminantEffect(seed);
            info.WaterUsePerCycle = HydroponicHelper.GetWaterUsePerCycle(seed);
            info.MineralUseRate = HydroponicHelper.GetMineralUseRate(seed);
            info.IsModified = PlantGeneticsHelper.IsModified(seed);
            foreach (var pair in PlantGeneticsHelper.GetTraits(seed))
            {
                info.Traits.Add(new HydroTraitInfo
                {
                    Name = pair.trait.displayName,
                    Rank = pair.rank,
                    RankLabel = PlantGeneticsHelper.RankLabel(pair.rank)
                });
            }

            return info;
        }

        private static int ReadProcessingSpeed(GameItem machine)
        {
            if (machine == null)
            {
                return MachineHydroponic.BASE_PROCESSING_SPEED;
            }

            if (machine.IsTag(MachineProgressHelper.CURRENT_PROCESSING_SPEED_TAG))
            {
                int current = machine.GetTagReadonly(MachineProgressHelper.CURRENT_PROCESSING_SPEED_TAG).GetInt();
                if (current > 0)
                {
                    return current;
                }
            }

            if (machine.IsTag(MachineProgressHelper.BASE_PROCESSING_SPEED_TAG))
            {
                int baseline = machine.GetTagReadonly(MachineProgressHelper.BASE_PROCESSING_SPEED_TAG).GetInt();
                if (baseline > 0)
                {
                    return baseline;
                }
            }

            return MachineHydroponic.BASE_PROCESSING_SPEED;
        }

        public static string SensitivityLabel(HydroInfo info)
        {
            double value = info?.CombinedSensitivity ?? 1.0;
            if (value == 0.0)
            {
                return ModEntry.L("hydro_sens_immune");
            }

            if (value.Equals(1.0))
            {
                return ModEntry.L("hydro_sens_average");
            }

            if (value >= 1.5)
            {
                return ModEntry.L("hydro_sens_very_sensitive");
            }

            if (value > 1.25)
            {
                return ModEntry.L("hydro_sens_sensitive");
            }

            if (value > 1.0)
            {
                return ModEntry.L("hydro_sens_slightly_sensitive");
            }

            if (value <= 0.5)
            {
                return ModEntry.L("hydro_sens_very_resistant");
            }

            if (value < 0.75)
            {
                return ModEntry.L("hydro_sens_resistant");
            }

            if (value < 1.0)
            {
                return ModEntry.L("hydro_sens_slightly_resistant");
            }

            return ModEntry.L("hydro_sens_unknown");
        }

        public static string FormatTraits(HydroInfo info)
        {
            if (info?.Traits == null || info.Traits.Count == 0)
            {
                return ModEntry.L("hydro_traits_none");
            }

            StringBuilder builder = new StringBuilder();
            string sep = ModEntry.L("hydro_trait_sep");
            for (int i = 0; i < info.Traits.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(sep);
                }

                HydroTraitInfo trait = info.Traits[i];
                builder.Append(trait.Name);
                if (!string.IsNullOrEmpty(trait.RankLabel))
                {
                    builder.Append(' ');
                    builder.Append(trait.RankLabel);
                }
            }

            return builder.ToString();
        }
        #endregion
    }
}
