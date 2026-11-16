using System;
using System.Collections.Generic;

namespace EasierFiltering
{
    internal sealed class DoseResult
    {
        public int[] PartsBefore;
        public int[] PartsAfter;
        public int Count;
        public int Available;
        public int Shortfall;
        public int MineralBefore;
        public int MineralAfter;
        public bool InRange;
        public bool OverLimit;
        public bool Overshoot;
        public bool Overflowed;
    }

    internal sealed class HydroPlan
    {
        public DoseResult Dose;
        public int[] PartsAfter;
        public int Cycles;
        public bool PlantSafeAfter;
        public List<string> UnsafeAfter = new List<string>();
        public bool CanConfirm;
        public string Warning;
    }

    /// <summary>
    /// 模拟水培营养片 DissolveInto，并估算植物无损与可维持周期。
    /// </summary>
    internal static class NutrientDosing
    {
        public const int MineralPerTabletUl = 15000;
        public const int HarmlessThreshold = 10;
        public const int MaxTablets = 200;
        public const int MaxCycles = 200;

        private static readonly string[] HarmfulIds =
        {
            Liquid.LIQUID_HEAVY_METAL,
            Liquid.LIQUID_CHEMICAL_CONTAMINANT,
            Liquid.LIQUID_MICROBE,
            Liquid.LIQUID_ORGANIC_WASTE,
            Liquid.LIQUID_ACID,
            Liquid.LIQUID_BASE
        };

        #region MARK: 计划
        public static HydroPlan Plan(HydroInfo info, FilterStock stock, int lo, int hi)
        {
            HydroPlan plan = new HydroPlan();
            if (info == null)
            {
                plan.Warning = ModEntry.L("hydro_warn_no_machine");
                return plan;
            }

            if (!info.HasSeed)
            {
                plan.Warning = ModEntry.L("hydro_warn_no_seed");
                return plan;
            }

            if (!info.HasWater)
            {
                plan.Warning = ModEntry.L("hydro_warn_no_water");
                return plan;
            }

            int[] source = WaterHelper.GetLiquidParts(info.Water);
            int available = stock != null ? stock.NutrientTabletCount : 0;
            plan.Dose = SimulateDose(source, info.CapacityUl, lo, hi, available);
            plan.PartsAfter = plan.Dose.PartsAfter ?? source;
            plan.UnsafeAfter = ListUnsafe(plan.PartsAfter, info.Sensitivity, info.Effect);
            plan.PlantSafeAfter = plan.UnsafeAfter.Count == 0;
            plan.Cycles = EstimateCycles(
                plan.PartsAfter,
                lo,
                info.MineralUseRate,
                info.WaterUsePerCycle,
                info.ProcessingSpeed);
            plan.Warning = BuildWarning(info, plan, lo, hi);
            plan.CanConfirm = CanConfirm(plan);
            return plan;
        }

        private static bool CanConfirm(HydroPlan plan)
        {
            if (plan?.Dose == null)
            {
                return false;
            }

            if (plan.Dose.Overshoot && !plan.Dose.InRange)
            {
                return false;
            }

            return plan.Dose.Count > 0;
        }

        private static string BuildWarning(HydroInfo info, HydroPlan plan, int lo, int hi)
        {
            int current = MineralPct(WaterHelper.GetLiquidParts(info.Water));
            if (plan.Dose.Overshoot && !plan.Dose.InRange)
            {
                return ModEntry.L("hydro_warn_overshoot");
            }

            if (current > hi)
            {
                return ModEntry.L("hydro_warn_over_limit");
            }

            if (plan.Dose.Count <= 0 && current < lo && plan.Dose.Available <= 0)
            {
                return ModEntry.L("hydro_warn_no_tablets");
            }

            if (plan.Dose.Shortfall > 0)
            {
                return ModEntry.L("hydro_warn_shortfall", plan.Dose.Shortfall);
            }

            if (!plan.PlantSafeAfter)
            {
                return ModEntry.L("hydro_warn_unsafe", string.Join(ModEntry.L("hydro_trait_sep"), plan.UnsafeAfter));
            }

            if (plan.Dose.Overflowed)
            {
                return ModEntry.L("hydro_warn_overflow");
            }

            if (!CanConfirm(plan) && plan.Dose.InRange)
            {
                return ModEntry.L("hydro_warn_already");
            }

            return "";
        }
        #endregion

        #region MARK: 投片模拟
        public static DoseResult SimulateDose(int[] parts, int capacityUl, int lo, int hi, int available)
        {
            int[] before = parts != null ? (int[])parts.Clone() : new int[Liquid.Liquids.Count];
            int mineralBefore = MineralPct(before);
            int target = lo + (hi - lo) / 2;
            int cap = Math.Max(0, available);
            DoseResult result = new DoseResult
            {
                PartsBefore = before,
                PartsAfter = (int[])before.Clone(),
                Available = cap,
                MineralBefore = mineralBefore,
                MineralAfter = mineralBefore,
                OverLimit = mineralBefore > hi
            };

            if (lo > hi)
            {
                int swap = lo;
                lo = hi;
                hi = swap;
            }

            if (mineralBefore > hi)
            {
                return result;
            }

            int bestCount = 0;
            int bestDist = Distance(mineralBefore, target);
            bool bestInRange = mineralBefore >= lo && mineralBefore <= hi;
            int[] bestParts = (int[])before.Clone();
            bool bestOverflow = false;
            int[] working = (int[])before.Clone();
            bool overflowed = false;
            int inRangeCount = bestInRange ? 0 : -1;

            for (int count = 1; count <= Math.Max(cap, MaxTablets); count++)
            {
                if (!ApplyOneTablet(working, capacityUl, ref overflowed))
                {
                    break;
                }

                int mineral = MineralPct(working);
                bool inRange = mineral >= lo && mineral <= hi;
                int dist = Distance(mineral, target);
                bool better = false;
                if (inRange)
                {
                    if (!bestInRange || dist < bestDist || (dist == bestDist && count < bestCount))
                    {
                        better = true;
                    }

                    inRangeCount = count;
                }
                else if (!bestInRange && dist < bestDist)
                {
                    better = true;
                }

                if (better && count <= cap)
                {
                    bestCount = count;
                    bestDist = dist;
                    bestInRange = inRange;
                    bestParts = (int[])working.Clone();
                    bestOverflow = overflowed;
                }

                if (mineral > hi && inRangeCount < 0 && count >= 1)
                {
                    result.Overshoot = mineralBefore < lo;
                    break;
                }

                if (mineral > hi && bestInRange)
                {
                    break;
                }
            }

            result.Count = bestCount;
            result.PartsAfter = bestParts;
            result.MineralAfter = MineralPct(bestParts);
            result.InRange = bestInRange;
            result.Overflowed = bestOverflow;
            if (!bestInRange && mineralBefore < lo)
            {
                result.Shortfall = CountToReach(before, capacityUl, lo, cap);
            }

            if (result.Overshoot && !bestInRange)
            {
                result.Count = 0;
                result.PartsAfter = (int[])before.Clone();
                result.MineralAfter = mineralBefore;
            }

            return result;
        }

        private static int CountToReach(int[] parts, int capacityUl, int lo, int available)
        {
            int[] working = (int[])parts.Clone();
            bool overflowed = false;
            int extra = 0;
            while (MineralPct(working) < lo && extra < MaxTablets)
            {
                if (!ApplyOneTablet(working, capacityUl, ref overflowed))
                {
                    break;
                }

                extra++;
            }

            if (MineralPct(working) < lo)
            {
                return Math.Max(1, extra - available);
            }

            return Math.Max(0, extra - available);
        }

        private static bool ApplyOneTablet(int[] parts, int capacityUl, ref bool overflowed)
        {
            int mineralIndex = IndexOf(Liquid.LIQUID_MINERAL);
            if (mineralIndex < 0 || mineralIndex >= parts.Length)
            {
                return false;
            }

            int total = Sum(parts);
            int free = capacityUl > 0 ? capacityUl - total : 0;
            if (MineralPerTabletUl > free)
            {
                int overflow = MineralPerTabletUl - Math.Max(0, free);
                int[] removed = WaterHelper.SplitProportionally(parts, overflow);
                int removedTotal = 0;
                for (int i = 0; i < parts.Length && i < removed.Length; i++)
                {
                    parts[i] = Math.Max(0, parts[i] - removed[i]);
                    removedTotal += removed[i];
                }

                if (removedTotal <= 0 && free <= 0)
                {
                    return false;
                }

                overflowed = true;
            }

            parts[mineralIndex] += MineralPerTabletUl;
            return true;
        }

        private static int Distance(int value, int target)
        {
            return Math.Abs(value - target);
        }
        #endregion

        #region MARK: 植物无损
        public static bool IsPlantSafe(int[] parts, double sensitivity, double effect)
        {
            return ListUnsafe(parts, sensitivity, effect).Count == 0;
        }

        public static List<string> ListUnsafe(int[] parts, double sensitivity, double effect)
        {
            List<string> names = new List<string>();
            if (effect <= 0.0)
            {
                return names;
            }

            foreach (string id in HarmfulIds)
            {
                int pct = ContaminantPct(parts, id);
                int adjusted = (int)Math.Min(1000000.0, Math.Round(pct * sensitivity));
                if (adjusted <= HarmlessThreshold)
                {
                    continue;
                }

                Liquid liquid = Liquid.GetLiquidFromId(id);
                string name = liquid != null ? liquid.displayName : id;
                names.Add(ModEntry.L("hydro_unsafe_item", name, FilterSimulator.FormatPurity(pct)));
            }

            return names;
        }

        public static int MineralPct(int[] parts)
        {
            return ContaminantPct(parts, Liquid.LIQUID_MINERAL);
        }

        public static int ContaminantPct(int[] parts, string liquidId)
        {
            int amount = Part(parts, liquidId);
            if (amount <= 0)
            {
                return 0;
            }

            int water = Part(parts, Liquid.LIQUID_WATER);
            if (water <= 0)
            {
                return 1000000;
            }

            return (int)Math.Min(1000000L, (long)amount * 10000L / water);
        }
        #endregion

        #region MARK: 周期
        public static int EstimateCycles(int[] parts, int lo, int mineralUseRate, int waterUseMl, int speed)
        {
            if (parts == null)
            {
                return 0;
            }

            int[] sim = (int[])parts.Clone();
            int ticks = Math.Max(1, speed);
            int waterUseUl = Math.Max(0, waterUseMl) * 1000;
            int mineralIndex = IndexOf(Liquid.LIQUID_MINERAL);
            int cycles = 0;
            while (cycles < MaxCycles)
            {
                if (MineralPct(sim) < lo)
                {
                    break;
                }

                int volumeMl = Sum(sim) / 1000;
                if (waterUseMl > 0 && volumeMl < waterUseMl)
                {
                    break;
                }

                if (mineralIndex >= 0 && mineralIndex < sim.Length)
                {
                    sim[mineralIndex] = Math.Max(0, sim[mineralIndex] - mineralUseRate * ticks);
                }

                if (waterUseUl > 0)
                {
                    int total = Sum(sim);
                    if (total <= waterUseUl)
                    {
                        for (int i = 0; i < sim.Length; i++)
                        {
                            sim[i] = 0;
                        }

                        cycles++;
                        break;
                    }

                    int[] removed = WaterHelper.SplitProportionally(sim, waterUseUl);
                    for (int i = 0; i < sim.Length && i < removed.Length; i++)
                    {
                        sim[i] = Math.Max(0, sim[i] - removed[i]);
                    }
                }

                cycles++;
            }

            return cycles;
        }
        #endregion

        #region MARK: 执行
        public static int Apply(GameItem container, int count)
        {
            if (container == null || count <= 0)
            {
                return 0;
            }

            int used = 0;
            foreach (GameItem tablet in FilterInventory.FindNutrientTablets())
            {
                if (used >= count)
                {
                    break;
                }

                if (tablet == null)
                {
                    continue;
                }

                ChemicalHelper.DissolveInto(tablet, container);
                used++;
            }

            if (used > 0)
            {
                WaterHelper.UpdateContainerGoodType(container);
                WaterFeatureHelper.OnLiquidChanged(container, exposeFeature: false);
                WaterFeatureHelper.SetPresumedPurity(container, WaterHelper.GetWaterPurity(container));
            }

            return used;
        }
        #endregion

        #region MARK: 工具
        private static int Part(int[] parts, string liquidId)
        {
            int index = IndexOf(liquidId);
            if (index < 0 || parts == null || index >= parts.Length)
            {
                return 0;
            }

            return Math.Max(0, parts[index]);
        }

        private static int Sum(int[] parts)
        {
            long total = 0;
            if (parts == null)
            {
                return 0;
            }

            foreach (int part in parts)
            {
                total += Math.Max(0, part);
            }

            return (int)Math.Min(int.MaxValue, total);
        }

        private static int IndexOf(string identifier)
        {
            for (int i = 0; i < Liquid.Liquids.Count; i++)
            {
                if (Liquid.Liquids[i].identifier == identifier)
                {
                    return i;
                }
            }

            return -1;
        }
        #endregion
    }
}
