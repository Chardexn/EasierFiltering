using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace EasierFiltering
{
    internal enum FilterTierKind
    {
        Reached,
        Ok,
        Short,
        Impossible
    }

    internal sealed class FilterTierInfo
    {
        public int Index;
        public int Threshold;
        public FilterTierKind Kind;
        public FilterSimResult Result;
    }

    internal sealed class FilterSimResult
    {
        public int[] PartsBefore;
        public int[] PartsAfter;
        public int PurityBefore;
        public int PurityAfter;
        public int VolumeBeforeUl;
        public int VolumeAfterUl;
        public int Passes;
        public int PitcherPours;
        public int TargetPurity;
        public int TheoreticalMaxPurity;
        public List<string> UnfilterableNames = new List<string>();
        public List<FilterSnapshot> FiltersAfter = new List<FilterSnapshot>();
        public int DurabilityConsumed;
        public int FiltersDepleted;
        public bool ReachedTarget;
        public bool HasUv;
        public bool UsedUv;
        public int MaxFilterableMl;
        public int DurabilityShortfall;
        public bool NoMechanicalFilters;
        public bool NoChange;
    }

    /// <summary>
    /// 纯函数模拟。单趟公式对齐 WaterHelper.HandleFilter，达到挡位门槛即停。
    /// </summary>
    internal static class FilterSimulator
    {
        public const int MaxPasses = 100;
        public const int MaxTicksPerLiquid = 10000;
        public const int PitcherCapacityUl = 3000000;
        public const int CeilingIndex = -1;

        private static readonly int[] PreviewTierIndices = { 2, 1, 0 };

        public static bool IsCeiling(int index)
        {
            return index == CeilingIndex;
        }

        #region MARK: 入口
        public static FilterSimResult Simulate(GameItem container, FilterStock stock, int targetPurity)
        {
            int[] parts = WaterHelper.GetLiquidParts(container);
            return Simulate(parts, stock, targetPurity);
        }

        public static int[] ApplyOnePassForTest(int[] sourceParts, List<FilterSnapshot> filters)
        {
            int[] parts = (int[])sourceParts.Clone();
            List<FilterSnapshot> working = filters.Select(f => f.Clone()).ToList();
            RunOnePassVanilla(parts, working);
            for (int i = 0; i < filters.Count && i < working.Count; i++)
            {
                filters[i].Durability = working[i].Durability;
            }

            return parts;
        }

        public static FilterSimResult Simulate(int[] sourceParts, FilterStock stock, int targetPurity)
        {
            int[] partsBefore = (int[])sourceParts.Clone();
            List<FilterSnapshot> working = stock.Filters
                .Where(f => f.Durability > 0)
                .Select(f => f.Clone())
                .ToList();
            working = FilterInventory.SortForUse(working);

            bool hasUv = FilterConfig.UseUv.Value && stock.HasUv;
            int[] parts = (int[])partsBefore.Clone();
            bool usedUv = ApplyUv(parts, hasUv);

            int theoreticalMax = ComputeTheoreticalMaxPurity(partsBefore, working, hasUv);
            List<string> unfilterable = ListUnfilterableNames(partsBefore, working, hasUv);

            int clampedTarget = Mathf.Clamp(targetPurity, PurityOf(partsBefore), 10000);
            int passes = 0;
            int pours = 0;
            if (PurityOf(parts) < clampedTarget && clampedTarget <= theoreticalMax)
            {
                PassOutcome outcome = RunPasses(parts, working, clampedTarget);
                passes = outcome.Passes;
                pours = outcome.PitcherPours;
            }

            FilterSimResult result = BuildResult(partsBefore, parts, working, clampedTarget, theoreticalMax, unfilterable, hasUv, usedUv, passes, pours);
            result.DurabilityShortfall = ComputeShortfall(partsBefore, working, stock, hasUv, clampedTarget, result);
            result.MaxFilterableMl = ComputeMaxFilterableMl(partsBefore, stock, hasUv, clampedTarget, theoreticalMax);
            return result;
        }

        public static FilterTierInfo[] EvaluateTiers(GameItem container, FilterStock stock)
        {
            return EvaluateTiers(WaterHelper.GetLiquidParts(container), stock);
        }

        public static FilterTierInfo[] EvaluateTiers(int[] sourceParts, FilterStock stock)
        {
            int current = PurityOf(sourceParts);
            FilterTierInfo[] infos = new FilterTierInfo[PreviewTierIndices.Length + 1];
            int theoreticalMax = 0;
            for (int i = 0; i < PreviewTierIndices.Length; i++)
            {
                int index = PreviewTierIndices[i];
                int threshold = WaterFeatureHelper.WATER_PURITIES[index];
                FilterSimResult result = Simulate(sourceParts, stock, threshold);
                theoreticalMax = result.TheoreticalMaxPurity;
                FilterTierKind kind;
                if (current >= threshold)
                {
                    kind = FilterTierKind.Reached;
                }
                else if (threshold > result.TheoreticalMaxPurity)
                {
                    kind = FilterTierKind.Impossible;
                }
                else if (result.ReachedTarget)
                {
                    kind = FilterTierKind.Ok;
                }
                else
                {
                    kind = FilterTierKind.Short;
                }

                infos[i] = new FilterTierInfo
                {
                    Index = index,
                    Threshold = threshold,
                    Kind = kind,
                    Result = result
                };
            }

            infos[PreviewTierIndices.Length] = EvaluateCeiling(sourceParts, stock, current, theoreticalMax);
            return infos;
        }

        private static FilterTierInfo EvaluateCeiling(int[] sourceParts, FilterStock stock, int current, int theoreticalMax)
        {
            int target = Mathf.Max(current, theoreticalMax);
            FilterSimResult result = Simulate(sourceParts, stock, target);
            FilterTierKind kind;
            if (result.NoMechanicalFilters && !result.HasUv)
            {
                kind = FilterTierKind.Impossible;
            }
            else if (current >= theoreticalMax || result.NoChange)
            {
                kind = FilterTierKind.Reached;
            }
            else if (result.DurabilityShortfall > 0)
            {
                kind = FilterTierKind.Short;
            }
            else
            {
                kind = FilterTierKind.Ok;
            }

            return new FilterTierInfo
            {
                Index = CeilingIndex,
                Threshold = target,
                Kind = kind,
                Result = result
            };
        }

        public static FilterTierInfo PickDefaultTier(FilterTierInfo[] infos)
        {
            FilterTierInfo bestOk = null;
            FilterTierInfo lowestShort = null;
            FilterTierInfo bestReached = null;
            FilterTierInfo fallback = null;
            foreach (FilterTierInfo info in infos)
            {
                if (IsCeiling(info.Index))
                {
                    continue;
                }

                if (fallback == null)
                {
                    fallback = info;
                }

                if (info.Kind == FilterTierKind.Ok && (bestOk == null || info.Threshold > bestOk.Threshold))
                {
                    bestOk = info;
                }

                if (info.Kind == FilterTierKind.Short && (lowestShort == null || info.Threshold < lowestShort.Threshold))
                {
                    lowestShort = info;
                }

                if (info.Kind == FilterTierKind.Reached && (bestReached == null || info.Threshold > bestReached.Threshold))
                {
                    bestReached = info;
                }
            }

            return bestOk ?? lowestShort ?? bestReached ?? fallback ?? infos[0];
        }
        #endregion

        #region MARK: 多趟 / 单趟
        private struct PassOutcome
        {
            public int Passes;
            public int PitcherPours;
        }

        private struct ContaminantWork
        {
            public int Index;
            public int Floor;
            public float Efficiency;
        }

        private static PassOutcome RunPasses(int[] parts, List<FilterSnapshot> filters, int targetPurity)
        {
            int passes = 0;
            int pours = 0;
            while (passes < MaxPasses)
            {
                if (PurityOf(parts) >= targetPurity)
                {
                    break;
                }

                int[] before = (int[])parts.Clone();
                int volumeBefore = Sum(before);
                bool reached = RunOnePassTowardTarget(parts, filters, targetPurity);
                passes++;
                if (!SameParts(before, parts))
                {
                    pours += PitcherPoursForVolume(volumeBefore);
                }

                if (reached || SameParts(before, parts))
                {
                    break;
                }
            }

            return new PassOutcome { Passes = passes, PitcherPours = pours };
        }

        private static bool RunOnePassTowardTarget(int[] parts, List<FilterSnapshot> filters, int targetPurity)
        {
            List<ContaminantWork> work = new List<ContaminantWork>();
            for (int i = 0; i < Liquid.Liquids.Count && i < parts.Length; i++)
            {
                Liquid liquid = Liquid.Liquids[i];
                if (parts[i] <= 0)
                {
                    continue;
                }

                if (liquid.identifier == Liquid.LIQUID_WATER || liquid.identifier == Liquid.LIQUID_MICROBE)
                {
                    continue;
                }

                if (liquid.filterVolumeRemoved <= 0)
                {
                    continue;
                }

                int floor = (int)((double)parts[i] * liquid.filterMinimumPurity);
                if (parts[i] <= floor)
                {
                    continue;
                }

                if (FindUsable(filters, liquid.particleSize) == null)
                {
                    continue;
                }

                work.Add(new ContaminantWork
                {
                    Index = i,
                    Floor = floor,
                    Efficiency = liquid.filterVolumeRemoved / (float)Math.Max(1, liquid.filterDurabilityUsage)
                });
            }

            work.Sort((a, b) => b.Efficiency.CompareTo(a.Efficiency));

            foreach (ContaminantWork item in work)
            {
                if (PurityOf(parts) >= targetPurity)
                {
                    return true;
                }

                Liquid liquid = Liquid.Liquids[item.Index];
                int ticks = 0;
                while (parts[item.Index] > item.Floor && ticks < MaxTicksPerLiquid)
                {
                    if (PurityOf(parts) >= targetPurity)
                    {
                        return true;
                    }

                    FilterSnapshot filter = FindUsable(filters, liquid.particleSize);
                    if (filter == null)
                    {
                        break;
                    }

                    filter.Durability = Math.Max(0, filter.Durability - liquid.filterDurabilityUsage);
                    parts[item.Index] -= liquid.filterVolumeRemoved;
                    if (parts[item.Index] < item.Floor)
                    {
                        parts[item.Index] = item.Floor;
                    }

                    if (parts[item.Index] < 0)
                    {
                        parts[item.Index] = 0;
                    }

                    ticks++;
                }
            }

            return PurityOf(parts) >= targetPurity;
        }

        private static void RunOnePassVanilla(int[] parts, List<FilterSnapshot> filters)
        {
            for (int i = 0; i < Liquid.Liquids.Count && i < parts.Length; i++)
            {
                parts[i] = FilterOneLiquid(Liquid.Liquids[i], parts[i], filters);
            }
        }

        private static int FilterOneLiquid(Liquid liquid, int amount, List<FilterSnapshot> filters)
        {
            if (amount <= 0)
            {
                return 0;
            }

            if (liquid.identifier == Liquid.LIQUID_MICROBE)
            {
                return amount;
            }

            int original = amount;
            int floor = (int)((double)original * liquid.filterMinimumPurity);
            int ticks = 0;
            while (amount > floor && ticks < MaxTicksPerLiquid)
            {
                FilterSnapshot filter = FindUsable(filters, liquid.particleSize);
                if (filter == null)
                {
                    break;
                }

                if (liquid.filterVolumeRemoved <= 0)
                {
                    break;
                }

                filter.Durability = Math.Max(0, filter.Durability - liquid.filterDurabilityUsage);
                amount -= liquid.filterVolumeRemoved;
                ticks++;
            }

            amount = Mathf.Clamp(amount, floor, original);
            return Mathf.Max(0, amount);
        }

        private static FilterSnapshot FindUsable(List<FilterSnapshot> filters, int particleSize)
        {
            foreach (FilterSnapshot filter in filters)
            {
                if (filter.Durability > 0 && filter.PoreSize > 0 && filter.PoreSize <= particleSize)
                {
                    return filter;
                }
            }

            return null;
        }

        private static int PitcherPoursForVolume(int volumeUl)
        {
            if (volumeUl <= 0)
            {
                return 0;
            }

            return (volumeUl + PitcherCapacityUl - 1) / PitcherCapacityUl;
        }
        #endregion

        #region MARK: UV 与理论上限
        private static bool ApplyUv(int[] parts, bool hasUv)
        {
            if (!hasUv)
            {
                return false;
            }

            int microbeIndex = IndexOf(Liquid.LIQUID_MICROBE);
            int waterIndex = IndexOf(Liquid.LIQUID_WATER);
            if (microbeIndex < 0 || waterIndex < 0 || microbeIndex >= parts.Length || waterIndex >= parts.Length)
            {
                return false;
            }

            int microbes = parts[microbeIndex];
            if (microbes <= 0)
            {
                return false;
            }

            parts[waterIndex] += microbes;
            parts[microbeIndex] = 0;
            return true;
        }

        private static int ComputeTheoreticalMaxPurity(int[] sourceParts, List<FilterSnapshot> filters, bool hasUv)
        {
            int[] parts = (int[])sourceParts.Clone();
            ApplyUv(parts, hasUv);
            int bestPore = BestPore(filters);
            for (int i = 0; i < Liquid.Liquids.Count && i < parts.Length; i++)
            {
                Liquid liquid = Liquid.Liquids[i];
                if (CanEverFilter(liquid, bestPore, hasUv))
                {
                    parts[i] = 0;
                }
            }

            return PurityOf(parts);
        }

        private static List<string> ListUnfilterableNames(int[] parts, List<FilterSnapshot> filters, bool hasUv)
        {
            List<string> names = new List<string>();
            int bestPore = BestPore(filters);
            for (int i = 0; i < Liquid.Liquids.Count && i < parts.Length; i++)
            {
                if (parts[i] <= 0)
                {
                    continue;
                }

                Liquid liquid = Liquid.Liquids[i];
                if (liquid.identifier == Liquid.LIQUID_WATER)
                {
                    continue;
                }

                if (!CanEverFilter(liquid, bestPore, hasUv))
                {
                    names.Add(liquid.displayName);
                }
            }

            return names;
        }

        private static bool CanEverFilter(Liquid liquid, int bestPore, bool hasUv)
        {
            if (liquid.identifier == Liquid.LIQUID_WATER)
            {
                return false;
            }

            if (liquid.identifier == Liquid.LIQUID_MICROBE)
            {
                return hasUv;
            }

            return bestPore > 0 && bestPore <= liquid.particleSize;
        }

        private static int BestPore(List<FilterSnapshot> filters)
        {
            int best = int.MaxValue;
            foreach (FilterSnapshot filter in filters)
            {
                if (filter.PoreSize > 0 && filter.PoreSize < best)
                {
                    best = filter.PoreSize;
                }
            }

            return best == int.MaxValue ? 0 : best;
        }
        #endregion

        #region MARK: 消耗与最大水量
        private static int ComputeShortfall(
            int[] partsBefore,
            List<FilterSnapshot> usedFilters,
            FilterStock stock,
            bool hasUv,
            int target,
            FilterSimResult actual)
        {
            if (actual.ReachedTarget || target > actual.TheoreticalMaxPurity)
            {
                return 0;
            }

            int available = actual.DurabilityConsumed + usedFilters.Sum(f => Math.Max(0, f.Durability));
            FilterStock infinite = MakeInfiniteStock(stock);
            int[] parts = (int[])partsBefore.Clone();
            List<FilterSnapshot> working = infinite.Filters.Select(f => f.Clone()).ToList();
            ApplyUv(parts, hasUv);
            RunPasses(parts, working, target);
            int needed = working.Sum(f => Math.Max(0, f.DurabilityBefore - f.Durability));
            return Mathf.Max(0, needed - available);
        }

        private static int ComputeMaxFilterableMl(int[] partsBefore, FilterStock stock, bool hasUv, int target, int theoreticalMax)
        {
            int currentPurity = PurityOf(partsBefore);
            if (target <= currentPurity)
            {
                return Sum(partsBefore) / 1000;
            }

            if (target > theoreticalMax)
            {
                return 0;
            }

            long current = Sum(partsBefore);
            if (current <= 0)
            {
                return 0;
            }

            if (!CanReach(partsBefore, stock, hasUv, target, current))
            {
                long loFail = 0;
                long hiFail = current;
                while (loFail < hiFail)
                {
                    long mid = (loFail + hiFail + 1) / 2;
                    if (CanReach(partsBefore, stock, hasUv, target, mid))
                    {
                        loFail = mid;
                    }
                    else
                    {
                        hiFail = mid - 1;
                    }
                }

                return (int)(loFail / 1000);
            }

            long lo = current;
            long hi = current;
            const long cap = 1000000000L;
            while (hi < cap && CanReach(partsBefore, stock, hasUv, target, hi * 2))
            {
                hi *= 2;
            }

            if (hi < cap)
            {
                long probe = hi * 2;
                if (probe > cap)
                {
                    probe = cap;
                }

                if (CanReach(partsBefore, stock, hasUv, target, probe))
                {
                    hi = probe;
                }
            }

            while (lo < hi)
            {
                long mid = (lo + hi + 1) / 2;
                if (CanReach(partsBefore, stock, hasUv, target, mid))
                {
                    lo = mid;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            return (int)(lo / 1000);
        }

        private static bool CanReach(int[] sourceParts, FilterStock stock, bool hasUv, int target, long volumeUl)
        {
            int[] scaled = ScaleParts(sourceParts, volumeUl);
            List<FilterSnapshot> working = stock.Filters
                .Where(f => f.Durability > 0)
                .Select(f => f.Clone())
                .ToList();
            ApplyUv(scaled, hasUv);
            RunPasses(scaled, working, target);
            return PurityOf(scaled) >= target;
        }

        private static FilterStock MakeInfiniteStock(FilterStock stock)
        {
            FilterStock infinite = new FilterStock();
            int bestPore = BestPore(stock.Filters);
            if (bestPore <= 0)
            {
                return infinite;
            }

            infinite.Filters.Add(new FilterSnapshot
            {
                Identifier = "_infinite",
                DisplayName = "_infinite",
                PoreSize = bestPore,
                Durability = 1000000000,
                DurabilityBefore = 1000000000,
                MaxDurability = 1000000000
            });
            infinite.TotalDurability = 1000000000;
            infinite.UvLamps.AddRange(stock.UvLamps);
            return infinite;
        }
        #endregion

        #region MARK: 结果与工具
        private static FilterSimResult BuildResult(
            int[] partsBefore,
            int[] partsAfter,
            List<FilterSnapshot> working,
            int target,
            int theoreticalMax,
            List<string> unfilterable,
            bool hasUv,
            bool usedUv,
            int passes,
            int pours)
        {
            int consumed = working.Sum(f => Math.Max(0, f.DurabilityBefore - f.Durability));
            int depleted = working.Count(f => f.DurabilityBefore > 0 && f.Durability <= 0);
            int purityBefore = PurityOf(partsBefore);
            int purityAfter = PurityOf(partsAfter);
            return new FilterSimResult
            {
                PartsBefore = partsBefore,
                PartsAfter = partsAfter,
                PurityBefore = purityBefore,
                PurityAfter = purityAfter,
                VolumeBeforeUl = Sum(partsBefore),
                VolumeAfterUl = Sum(partsAfter),
                Passes = passes,
                PitcherPours = pours,
                TargetPurity = target,
                TheoreticalMaxPurity = theoreticalMax,
                UnfilterableNames = unfilterable,
                FiltersAfter = working,
                DurabilityConsumed = consumed,
                FiltersDepleted = depleted,
                ReachedTarget = purityAfter >= target,
                HasUv = hasUv,
                UsedUv = usedUv,
                NoMechanicalFilters = working.Count == 0,
                NoChange = purityAfter == purityBefore && consumed == 0 && !usedUv
            };
        }

        public static string FormatPurity(int purity)
        {
            return $"{purity / 100f:0.00}%";
        }

        public static string QualityName(int purity)
        {
            int[] purities = WaterFeatureHelper.WATER_PURITIES;
            for (int i = 0; i < purities.Length; i++)
            {
                if (purity >= purities[i])
                {
                    return WaterFeatureHelper.WATER_DISPLAY_LOCALIZED[i];
                }
            }

            return WaterFeatureHelper.WATER_DISPLAY_LOCALIZED[purities.Length - 1];
        }

        public static string TierThresholdLabel(int threshold)
        {
            return $"{threshold / 100f:0.#}%+";
        }

        public static int PurityOf(int[] parts)
        {
            int water = 0;
            int total = 0;
            int waterIndex = IndexOf(Liquid.LIQUID_WATER);
            for (int i = 0; i < parts.Length; i++)
            {
                int amount = Math.Max(0, parts[i]);
                total += amount;
                if (i == waterIndex)
                {
                    water = amount;
                }
            }

            if (total <= 0)
            {
                return 0;
            }

            return (int)((long)water * 10000L / total);
        }

        private static int[] ScaleParts(int[] source, long newTotal)
        {
            int[] dst = new int[source.Length];
            long old = Sum(source);
            if (old <= 0 || newTotal <= 0)
            {
                return dst;
            }

            long used = 0;
            int last = -1;
            for (int i = 0; i < source.Length; i++)
            {
                if (source[i] <= 0)
                {
                    continue;
                }

                dst[i] = (int)(source[i] * newTotal / old);
                used += dst[i];
                last = i;
            }

            if (last >= 0)
            {
                long next = dst[last] + (newTotal - used);
                if (next < 0)
                {
                    next = 0;
                }
                else if (next > int.MaxValue)
                {
                    next = int.MaxValue;
                }

                dst[last] = (int)next;
            }

            return dst;
        }

        private static int Sum(int[] parts)
        {
            long total = 0;
            foreach (int part in parts)
            {
                total += Math.Max(0, part);
            }

            return (int)Math.Min(int.MaxValue, total);
        }

        private static bool SameParts(int[] a, int[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
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
