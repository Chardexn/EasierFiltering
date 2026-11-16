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

    internal sealed class ImpurityDelta
    {
        public string Name;
        public int BeforeUl;
        public int AfterUl;
        public bool Unfilterable;
        public int RemovedUl => Math.Max(0, BeforeUl - AfterUl);
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
        public List<ImpurityDelta> ImpurityDeltas = new List<ImpurityDelta>();
        public int ImpurityBeforeUl;
        public int ImpurityAfterUl;
        public int ImpurityRemovedUl;
        public int DurabilityConsumed;
        public int FiltersDepleted;
        public bool ReachedTarget;
        public bool HasUv;
        public bool UsedUv;
        public int TabletsConsumed;
        public int MaxFilterableMl;
        public int DurabilityShortfall;
        public bool NoMechanicalFilters;
        public bool NoChange;
        public bool IsOnePass;
        public bool IsPurifierMode;
        public bool PurifierNeedsNoPrep;
        public bool CapLimited;
        public int PurifierCap;
        public int PredictedPurityAfterNight;
        public int PredictedRemovedUl;
        public int NightsNeeded;
        public int QualityBonus;
    }

    internal struct FilterAddons
    {
        public bool Uv;
        public bool Tablets;
    }

    /// <summary>
    /// 纯函数模拟。单趟公式对齐 WaterHelper.HandleFilter，达到挡位门槛即停。
    /// </summary>
    internal static class FilterSimulator
    {
        public const int MaxPasses = 100;
        public const int MaxTicksPerLiquid = 10000;
        public const int PitcherCapacityUl = 3000000;
        public const int DefaultIndex = -2;
        public const int ChlorinePerTabletUl = 15000;
        public const int MaxPurifierNights = 30;
        public const int PureQualityBonusRequired = 100;

        private const long FallbackFilterValue = 25;
        private const long FallbackTabletValue = 6;

        private static readonly int[] PreviewTierIndices = { 1, 0 };
        private static readonly int[] PurifierTierIndices = { 1, 0 };
        private static readonly HashSet<string> PurifierRemovedLast = new HashSet<string>
        {
            Liquid.LIQUID_MINERAL,
            Liquid.LIQUID_CHEMICAL_CONTAMINANT,
            Liquid.LIQUID_HEAVY_METAL
        };

        public static bool IsDefault(int index)
        {
            return index == DefaultIndex;
        }

        #region MARK: 入口
        public static FilterSimResult Simulate(GameItem container, FilterStock stock, int targetPurity, FilterAddons addons = default)
        {
            int[] parts = WaterHelper.GetLiquidParts(container);
            return Simulate(parts, stock, targetPurity, addons);
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

        public static FilterSimResult Simulate(int[] sourceParts, FilterStock stock, int targetPurity, FilterAddons addons = default)
        {
            int[] partsBefore = (int[])sourceParts.Clone();
            List<FilterSnapshot> working = stock.Filters
                .Where(f => f.Durability > 0)
                .Select(f => f.Clone())
                .ToList();
            working = FilterInventory.SortForUse(working);

            bool hasUv = addons.Uv && stock.HasUv;
            int tabletCount = addons.Tablets ? stock.TabletCount : 0;
            int[] parts = (int[])partsBefore.Clone();
            bool usedUv = ApplyUv(parts, hasUv);

            int theoreticalMax = ComputeTheoreticalMaxPurity(partsBefore, working, hasUv, tabletCount);
            List<string> unfilterable = ListUnfilterableNames(partsBefore, working, hasUv, tabletCount > 0);

            int clampedTarget = Mathf.Clamp(targetPurity, PurityOf(partsBefore), 10000);
            int passes = 0;
            int pours = 0;
            if (PurityOf(parts) < clampedTarget && clampedTarget <= theoreticalMax)
            {
                PassOutcome outcome = RunPasses(parts, working, p => PurityOf(p) >= clampedTarget);
                passes = outcome.Passes;
                pours = outcome.PitcherPours;
            }

            int tablets = addons.Tablets ? ApplyTablets(parts, stock.TabletCount) : 0;
            FilterSimResult result = BuildResult(partsBefore, parts, working, clampedTarget, theoreticalMax, unfilterable, hasUv, usedUv, passes, pours, onePass: false, tablets);
            result.DurabilityShortfall = ComputeShortfall(partsBefore, working, stock, hasUv, clampedTarget, result);
            result.MaxFilterableMl = ComputeMaxFilterableMl(partsBefore, stock, hasUv, clampedTarget, theoreticalMax);
            return result;
        }

        public static FilterSimResult SimulateOnePass(GameItem container, FilterStock stock, FilterAddons addons = default)
        {
            int[] parts = WaterHelper.GetLiquidParts(container);
            return SimulateOnePass(parts, stock, addons);
        }

        public static FilterSimResult SimulateOnePass(int[] sourceParts, FilterStock stock, FilterAddons addons = default)
        {
            int[] partsBefore = (int[])sourceParts.Clone();
            List<FilterSnapshot> working = stock.Filters
                .Where(f => f.Durability > 0)
                .Select(f => f.Clone())
                .ToList();
            working = FilterInventory.SortForUse(working);

            bool hasUv = addons.Uv && stock.HasUv;
            int tabletCount = addons.Tablets ? stock.TabletCount : 0;
            int[] parts = (int[])partsBefore.Clone();
            bool usedUv = ApplyUv(parts, hasUv);
            int theoreticalMax = ComputeTheoreticalMaxPurity(partsBefore, working, hasUv, tabletCount);
            List<string> unfilterable = ListUnfilterableNames(partsBefore, working, hasUv, tabletCount > 0);

            int[] beforePass = (int[])parts.Clone();
            int volumeBefore = Sum(beforePass);
            RunOnePassVanilla(parts, working);
            int tablets = addons.Tablets ? ApplyTablets(parts, stock.TabletCount) : 0;
            bool changed = !SameParts(beforePass, parts) || usedUv || tablets > 0;
            int passes = changed ? 1 : 0;
            int pours = SameParts(beforePass, parts) ? 0 : PitcherPoursForVolume(volumeBefore);

            FilterSimResult result = BuildResult(partsBefore, parts, working, PurityOf(parts), theoreticalMax, unfilterable, hasUv, usedUv, passes, pours, onePass: true, tablets);
            result.DurabilityShortfall = ComputeOnePassShortfall(partsBefore, working, stock, hasUv, result);
            result.ReachedTarget = result.DurabilityShortfall <= 0;
            return result;
        }

        public static FilterSimResult SimulateUntil(GameItem container, FilterStock stock, Func<int[], bool> done, FilterAddons addons = default)
        {
            int[] parts = WaterHelper.GetLiquidParts(container);
            return SimulateUntil(parts, stock, done, addons);
        }

        public static FilterSimResult SimulateUntil(int[] sourceParts, FilterStock stock, Func<int[], bool> done, FilterAddons addons = default)
        {
            int[] partsBefore = (int[])sourceParts.Clone();
            List<FilterSnapshot> working = stock.Filters
                .Where(f => f.Durability > 0)
                .Select(f => f.Clone())
                .ToList();
            working = FilterInventory.SortForUse(working);

            bool hasUv = addons.Uv && stock.HasUv;
            int tabletCount = addons.Tablets ? stock.TabletCount : 0;
            int[] parts = (int[])partsBefore.Clone();
            bool usedUv = ApplyUv(parts, hasUv);
            int theoreticalMax = ComputeTheoreticalMaxPurity(partsBefore, working, hasUv, tabletCount);
            List<string> unfilterable = ListUnfilterableNames(partsBefore, working, hasUv, tabletCount > 0);

            int passes = 0;
            int pours = 0;
            bool alreadyDone = done != null && done(parts);
            if (!alreadyDone)
            {
                PassOutcome outcome = RunPasses(parts, working, p => done != null && done(p));
                passes = outcome.Passes;
                pours = outcome.PitcherPours;
            }

            int tablets = addons.Tablets ? ApplyTablets(parts, stock.TabletCount) : 0;
            FilterSimResult result = BuildResult(
                partsBefore,
                parts,
                working,
                PurityOf(parts),
                theoreticalMax,
                unfilterable,
                hasUv,
                usedUv,
                passes,
                pours,
                onePass: false,
                tablets);
            result.ReachedTarget = done != null && done(parts);
            return result;
        }

        public static FilterSimResult SimulateForPurifier(
            GameItem container,
            FilterStock stock,
            int targetPurity,
            FilterAddons addons,
            PurifierInfo purifier)
        {
            int[] parts = WaterHelper.GetLiquidParts(container);
            return SimulateForPurifier(parts, stock, targetPurity, addons, purifier);
        }

        public static FilterTierInfo[] EvaluateTiers(GameItem container, FilterStock stock, FilterAddons addons = default)
        {
            return EvaluateTiers(WaterHelper.GetLiquidParts(container), stock, addons);
        }

        public static FilterTierInfo[] EvaluateTiers(int[] sourceParts, FilterStock stock, FilterAddons addons = default)
        {
            int current = PurityOf(sourceParts);
            FilterTierInfo[] infos = new FilterTierInfo[PreviewTierIndices.Length + 1];
            infos[0] = EvaluateDefault(sourceParts, stock, addons);
            for (int i = 0; i < PreviewTierIndices.Length; i++)
            {
                int index = PreviewTierIndices[i];
                int threshold = WaterFeatureHelper.WATER_PURITIES[index];
                FilterSimResult result = Simulate(sourceParts, stock, threshold, addons);
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
                else if (result.DurabilityShortfall > 0)
                {
                    kind = FilterTierKind.Short;
                }
                else
                {
                    kind = FilterTierKind.Impossible;
                }

                infos[i + 1] = new FilterTierInfo
                {
                    Index = index,
                    Threshold = threshold,
                    Kind = kind,
                    Result = result
                };
            }

            return infos;
        }

        private static FilterTierInfo EvaluateDefault(int[] sourceParts, FilterStock stock, FilterAddons addons)
        {
            FilterSimResult result = SimulateOnePass(sourceParts, stock, addons);
            FilterTierKind kind;
            if (result.NoMechanicalFilters && !result.HasUv && result.TabletsConsumed <= 0 && !addons.Uv && !addons.Tablets)
            {
                kind = FilterTierKind.Impossible;
            }
            else if (result.NoChange)
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
                Index = DefaultIndex,
                Threshold = result.PurityAfter,
                Kind = kind,
                Result = result
            };
        }

        public static FilterTierInfo PickDefaultTier(FilterTierInfo[] infos)
        {
            if (infos == null || infos.Length == 0)
            {
                return null;
            }

            foreach (FilterTierInfo info in infos)
            {
                if (IsDefault(info.Index))
                {
                    return info;
                }
            }

            return infos[0];
        }

        public static FilterTierInfo[] EvaluatePurifierTiers(
            GameItem container,
            FilterStock stock,
            FilterAddons addons,
            PurifierInfo purifier)
        {
            return EvaluatePurifierTiers(WaterHelper.GetLiquidParts(container), stock, addons, purifier);
        }

        public static FilterTierInfo[] EvaluatePurifierTiers(
            int[] sourceParts,
            FilterStock stock,
            FilterAddons addons,
            PurifierInfo purifier)
        {
            FilterTierInfo[] infos = new FilterTierInfo[PurifierTierIndices.Length];
            for (int i = 0; i < PurifierTierIndices.Length; i++)
            {
                int index = PurifierTierIndices[i];
                int threshold = WaterFeatureHelper.WATER_PURITIES[index];
                FilterSimResult result = SimulateForPurifier(sourceParts, stock, threshold, addons, purifier);
                FilterTierKind kind;
                if (result.CapLimited)
                {
                    kind = FilterTierKind.Impossible;
                }
                else if (result.PurifierNeedsNoPrep || PurityOf(sourceParts) >= threshold)
                {
                    kind = FilterTierKind.Reached;
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

            return infos;
        }

        public static FilterTierInfo PickPurifierTier(FilterTierInfo[] infos)
        {
            if (infos == null || infos.Length == 0)
            {
                return null;
            }

            FilterTierInfo quality = null;
            FilterTierInfo pure = null;
            foreach (FilterTierInfo info in infos)
            {
                if (info.Index == 1)
                {
                    quality = info;
                }
                else if (info.Index == 0)
                {
                    pure = info;
                }
            }

            if (quality != null && quality.Kind == FilterTierKind.Reached
                && pure != null && (pure.Kind == FilterTierKind.Ok || pure.Kind == FilterTierKind.Short))
            {
                return pure;
            }

            if (quality != null && quality.Kind != FilterTierKind.Impossible)
            {
                return quality;
            }

            if (pure != null && pure.Kind != FilterTierKind.Impossible)
            {
                return pure;
            }

            return quality ?? infos[0];
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

        private static PassOutcome RunPasses(int[] parts, List<FilterSnapshot> filters, Func<int[], bool> isDone)
        {
            int passes = 0;
            int pours = 0;
            while (passes < MaxPasses)
            {
                if (isDone(parts))
                {
                    break;
                }

                int[] before = (int[])parts.Clone();
                int volumeBefore = Sum(before);
                bool reached = RunOnePassTowardTarget(parts, filters, isDone);
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

        private static bool RunOnePassTowardTarget(int[] parts, List<FilterSnapshot> filters, Func<int[], bool> isDone)
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
                if (isDone(parts))
                {
                    return true;
                }

                Liquid liquid = Liquid.Liquids[item.Index];
                int ticks = 0;
                while (parts[item.Index] > item.Floor && ticks < MaxTicksPerLiquid)
                {
                    if (isDone(parts))
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

            return isDone(parts);
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

        private static int ApplyTablets(int[] parts, int available)
        {
            if (available <= 0)
            {
                return 0;
            }

            int microbeIndex = IndexOf(Liquid.LIQUID_MICROBE);
            int chemIndex = IndexOf(Liquid.LIQUID_CHEMICAL_CONTAMINANT);
            int used = 0;
            while (used < available)
            {
                int microbes = microbeIndex >= 0 && microbeIndex < parts.Length ? parts[microbeIndex] : 0;
                int chem = chemIndex >= 0 && chemIndex < parts.Length ? parts[chemIndex] : 0;
                int[] probe = (int[])parts.Clone();
                ApplyOneTablet(probe);
                int microbesAfter = microbeIndex >= 0 && microbeIndex < probe.Length ? probe[microbeIndex] : 0;
                int chemAfter = chemIndex >= 0 && chemIndex < probe.Length ? probe[chemIndex] : 0;
                if (microbesAfter >= microbes && chemAfter >= chem)
                {
                    break;
                }

                for (int i = 0; i < parts.Length && i < probe.Length; i++)
                {
                    parts[i] = probe[i];
                }

                used++;
            }

            return used;
        }

        private static void ApplyOneTablet(int[] parts)
        {
            int chlorineIndex = IndexOf(Liquid.LIQUID_CHLORINE);
            if (chlorineIndex < 0 || chlorineIndex >= parts.Length)
            {
                return;
            }

            parts[chlorineIndex] += ChlorinePerTabletUl;
            SimulateChlorineTick(parts);
        }

        private static void SimulateChlorineTick(int[] parts)
        {
            int chlorineIndex = IndexOf(Liquid.LIQUID_CHLORINE);
            int microbeIndex = IndexOf(Liquid.LIQUID_MICROBE);
            int chemIndex = IndexOf(Liquid.LIQUID_CHEMICAL_CONTAMINANT);
            int waterIndex = IndexOf(Liquid.LIQUID_WATER);
            if (chlorineIndex < 0 || waterIndex < 0 || chlorineIndex >= parts.Length || waterIndex >= parts.Length)
            {
                return;
            }

            if (parts[waterIndex] <= 0)
            {
                return;
            }

            int chlorine = parts[chlorineIndex];
            if (chlorine <= 0)
            {
                return;
            }

            int microbes = microbeIndex >= 0 && microbeIndex < parts.Length ? parts[microbeIndex] : 0;
            int chem = chemIndex >= 0 && chemIndex < parts.Length ? parts[chemIndex] : 0;
            int reacted = Math.Min(chlorine, ChlorinePerTabletUl);
            long budget = (long)(reacted * 5.1);
            int total = Sum(parts);
            int chemFloor = (int)(total * 0.007f);
            int chemRemoved = chem > chemFloor ? (int)Math.Min(chem - chemFloor, budget) : 0;
            budget -= chemRemoved;
            int microbesDestroyed = (int)Math.Min(microbes, budget);
            int chlorineAfter = chlorine - reacted;
            int selfDegraded = Math.Min(chlorineAfter, Math.Max(1000, (int)(chlorineAfter * 0.2)));
            parts[chlorineIndex] = Math.Max(0, chlorineAfter - selfDegraded);
            if (microbeIndex >= 0 && microbeIndex < parts.Length)
            {
                parts[microbeIndex] = Math.Max(0, microbes - microbesDestroyed);
            }

            if (chemIndex >= 0 && chemIndex < parts.Length)
            {
                parts[chemIndex] = Math.Max(0, chem - chemRemoved);
            }
        }

        private static int ComputeTheoreticalMaxPurity(int[] sourceParts, List<FilterSnapshot> filters, bool hasUv, int tabletCount)
        {
            int[] parts = (int[])sourceParts.Clone();
            ApplyUv(parts, hasUv);
            int bestPore = BestPore(filters);
            for (int i = 0; i < Liquid.Liquids.Count && i < parts.Length; i++)
            {
                Liquid liquid = Liquid.Liquids[i];
                if (CanEverFilter(liquid, bestPore, hasUv, useTablets: false))
                {
                    parts[i] = 0;
                }
            }

            if (tabletCount > 0)
            {
                ApplyTablets(parts, tabletCount);
            }

            return PurityOf(parts);
        }

        private static List<string> ListUnfilterableNames(int[] parts, List<FilterSnapshot> filters, bool hasUv, bool useTablets)
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

                if (!CanEverFilter(liquid, bestPore, hasUv, useTablets))
                {
                    names.Add(liquid.displayName);
                }
            }

            return names;
        }

        private static bool CanEverFilter(Liquid liquid, int bestPore, bool hasUv, bool useTablets)
        {
            if (liquid.identifier == Liquid.LIQUID_WATER)
            {
                return false;
            }

            if (liquid.identifier == Liquid.LIQUID_MICROBE)
            {
                return hasUv || useTablets;
            }

            if (liquid.identifier == Liquid.LIQUID_CHEMICAL_CONTAMINANT)
            {
                return false;
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
            RunPasses(parts, working, p => PurityOf(p) >= target);
            int needed = working.Sum(f => Math.Max(0, f.DurabilityBefore - f.Durability));
            return Mathf.Max(0, needed - available);
        }

        private static int ComputeOnePassShortfall(
            int[] partsBefore,
            List<FilterSnapshot> usedFilters,
            FilterStock stock,
            bool hasUv,
            FilterSimResult actual)
        {
            if (actual.NoMechanicalFilters)
            {
                return 0;
            }

            FilterStock infinite = MakeInfiniteStock(stock);
            if (infinite.Filters.Count == 0)
            {
                return 0;
            }

            int[] parts = (int[])partsBefore.Clone();
            List<FilterSnapshot> working = infinite.Filters.Select(f => f.Clone()).ToList();
            ApplyUv(parts, hasUv);
            RunOnePassVanilla(parts, working);
            int needed = working.Sum(f => Math.Max(0, f.DurabilityBefore - f.Durability));
            int available = actual.DurabilityConsumed + usedFilters.Sum(f => Math.Max(0, f.Durability));
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
            RunPasses(scaled, working, p => PurityOf(p) >= target);
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

        #region MARK: 净水器
        public static FilterSimResult SimulateForPurifier(
            int[] sourceParts,
            FilterStock stock,
            int targetPurity,
            FilterAddons addons,
            PurifierInfo purifier)
        {
            int[] partsBefore = (int[])sourceParts.Clone();
            List<FilterSnapshot> working = stock.Filters
                .Where(f => f.Durability > 0)
                .Select(f => f.Clone())
                .ToList();
            working = FilterInventory.SortForUse(working);

            bool hasUv = addons.Uv && stock.HasUv;
            int tabletLeft = addons.Tablets ? stock.TabletCount : 0;
            int[] parts = (int[])partsBefore.Clone();
            int theoreticalMax = purifier != null ? purifier.Cap : 0;
            List<string> unfilterable = ListUnfilterableNames(partsBefore, working, hasUv, tabletLeft > 0);

            if (purifier == null)
            {
                FilterSimResult empty = BuildResult(
                    partsBefore, parts, working, targetPurity, theoreticalMax, unfilterable,
                    hasUv, usedUv: false, passes: 0, pours: 0, onePass: false, tabletsConsumed: 0);
                empty.IsPurifierMode = true;
                empty.CapLimited = true;
                empty.NightsNeeded = -1;
                return empty;
            }

            bool capLimited = targetPurity > purifier.Cap;
            bool usedUv = false;
            int passes = 0;
            int pours = 0;
            int tablets = 0;
            Func<int[], bool> isDone = p => PurityOf(PredictAfterNight(p, purifier)) >= targetPurity;

            if (!capLimited)
            {
                if (hasUv)
                {
                    int[] uvProbe = (int[])parts.Clone();
                    if (ApplyUv(uvProbe, true))
                    {
                        int beforeNight = PurityOf(PredictAfterNight(parts, purifier));
                        int afterNight = PurityOf(PredictAfterNight(uvProbe, purifier));
                        if (afterNight >= beforeNight)
                        {
                            ApplyUv(parts, true);
                            usedUv = true;
                        }
                    }
                }

                if (!isDone(parts))
                {
                    GreedyOutcome greedy = RunPurifierGreedy(parts, working, isDone, tabletLeft, stock);
                    passes = greedy.Passes;
                    pours = greedy.PitcherPours;
                    tablets = greedy.Tablets;
                }
            }

            int[] predicted = PredictAfterNight(parts, purifier);
            int predictedPurity = PurityOf(predicted);
            int nights = NightsToTarget(parts, purifier, targetPurity);
            FilterSimResult result = BuildResult(
                partsBefore, parts, working, targetPurity, theoreticalMax, unfilterable,
                hasUv, usedUv, passes, pours, onePass: false, tablets);
            result.IsPurifierMode = true;
            result.CapLimited = capLimited;
            result.PurifierCap = purifier.Cap;
            result.QualityBonus = purifier.QualityBonus;
            result.PredictedPurityAfterNight = predictedPurity;
            result.PredictedRemovedUl = Math.Max(0, ImpurityOf(parts) - ImpurityOf(predicted));
            result.NightsNeeded = nights;
            result.ReachedTarget = !capLimited && predictedPurity >= targetPurity;
            result.PurifierNeedsNoPrep = result.ReachedTarget && result.NoChange && PurityOf(partsBefore) < targetPurity;
            return result;
        }

        public static int[] PredictAfterNight(int[] sourceParts, PurifierInfo purifier)
        {
            int[] parts = (int[])sourceParts.Clone();
            SimulateChlorineTick(parts);
            if (purifier != null)
            {
                SimulatePurifierCycle(parts, purifier.QualityBonus, purifier.PerfBonus);
            }

            return parts;
        }

        public static int NightsToTarget(int[] sourceParts, PurifierInfo purifier, int targetPurity, int maxNights = MaxPurifierNights)
        {
            int[] cursor = (int[])sourceParts.Clone();
            if (PurityOf(cursor) >= targetPurity)
            {
                return 0;
            }

            for (int night = 1; night <= maxNights; night++)
            {
                int[] next = PredictAfterNight(cursor, purifier);
                if (SameParts(next, cursor))
                {
                    return PurityOf(next) >= targetPurity ? night : -1;
                }

                cursor = next;
                if (PurityOf(cursor) >= targetPurity)
                {
                    return night;
                }
            }

            return -1;
        }

        public static long ComputePurifierBudget(int[] parts, int perfBonus)
        {
            int total = Sum(parts);
            int purity = PurityOf(parts);
            double dirtBonus = MachinePurifier.GetDirtBonus(purity) + perfBonus / 100.0;
            if (dirtBonus < 0)
            {
                dirtBonus = 0;
            }

            dirtBonus *= MachinePurifier.REMOVAL_MULTIPLIER;
            return (long)((Math.Min(total, MachinePurifier.BUDGET_VOLUME_CAP_UL) * (long)MachinePurifier.BUDGET_VOLUME_PERMILLE / 1000
                + MachinePurifier.BUDGET_FLAT_UL) * dirtBonus);
        }

        private static void SimulatePurifierCycle(int[] parts, int qualityBonus, int perfBonus)
        {
            int total = Sum(parts);
            if (total <= 0)
            {
                return;
            }

            int cap = MachinePurifier.GetMaxPurityCap(qualityBonus);
            long budget = ComputePurifierBudget(parts, perfBonus);
            RemoveLargestFirst(parts, cap, budget);
        }

        private static int RemoveLargestFirst(int[] parts, int cap, long budget)
        {
            int total = Sum(parts);
            int waterIndex = IndexOf(Liquid.LIQUID_WATER);
            if (total <= 0 || waterIndex < 0 || waterIndex >= parts.Length)
            {
                return 0;
            }

            int water = Math.Max(0, parts[waterIndex]);
            if (water <= 0)
            {
                return 0;
            }

            long impurityFloor = (long)total * (10000 - cap) / 10000;
            long toRemove = Math.Min(budget, total - water - impurityFloor);
            if (toRemove <= 0)
            {
                return 0;
            }

            SortedDictionary<int, List<Liquid>> groups = new SortedDictionary<int, List<Liquid>>();
            foreach (Liquid liquid in Liquid.Liquids)
            {
                int index = IndexOf(liquid.identifier);
                if (index < 0 || index >= parts.Length || parts[index] <= 0)
                {
                    continue;
                }

                if (liquid.identifier == Liquid.LIQUID_WATER || !liquid.canBeRemovedByPurifier)
                {
                    continue;
                }

                int key = (PurifierRemovedLast.Contains(liquid.identifier) ? 1000000 : 0) - liquid.particleSize;
                if (!groups.TryGetValue(key, out List<Liquid> list))
                {
                    list = new List<Liquid>();
                    groups[key] = list;
                }

                list.Add(liquid);
            }

            long removed = 0L;
            foreach (List<Liquid> group in groups.Values)
            {
                if (toRemove <= 0)
                {
                    break;
                }

                long groupTotal = 0L;
                foreach (Liquid liquid in group)
                {
                    int index = IndexOf(liquid.identifier);
                    groupTotal += parts[index];
                }

                long take = Math.Min(groupTotal, toRemove);
                long assigned = 0L;
                for (int i = 0; i < group.Count; i++)
                {
                    int index = IndexOf(group[i].identifier);
                    int amount = parts[index];
                    long share = i == group.Count - 1 ? take - assigned : amount * take / groupTotal;
                    share = Math.Min(share, amount);
                    assigned += share;
                    parts[index] = amount - (int)share;
                }

                removed += assigned;
                toRemove -= assigned;
            }

            parts[waterIndex] = water + (int)removed;
            return (int)removed;
        }

        private struct GreedyOutcome
        {
            public int Passes;
            public int PitcherPours;
            public int Tablets;
        }

        private static GreedyOutcome RunPurifierGreedy(
            int[] parts,
            List<FilterSnapshot> working,
            Func<int[], bool> isDone,
            int tabletLeft,
            FilterStock stock)
        {
            int passes = 0;
            int pours = 0;
            int tablets = 0;
            int maxSteps = MaxPasses + Math.Max(0, tabletLeft) + 8;
            for (int step = 0; step < maxSteps; step++)
            {
                if (isDone(parts))
                {
                    break;
                }

                bool filterOk = false;
                double filterUnit = 0;
                int[] filterParts = null;
                List<FilterSnapshot> filterWorking = null;
                int filterPours = 0;
                int volumeBefore = Sum(parts);
                {
                    int[] probe = (int[])parts.Clone();
                    List<FilterSnapshot> probeFilters = working.Select(f => f.Clone()).ToList();
                    RunOnePassTowardTarget(probe, probeFilters, isDone);
                    if (!SameParts(parts, probe))
                    {
                        int removed = ImpurityOf(parts) - ImpurityOf(probe);
                        if (removed > 0)
                        {
                            double cost = CreditCostForFilters(probeFilters);
                            filterOk = true;
                            filterUnit = cost / removed;
                            filterParts = probe;
                            filterWorking = probeFilters;
                            filterPours = PitcherPoursForVolume(volumeBefore);
                        }
                    }
                }

                bool tabletOk = false;
                double tabletUnit = 0;
                int[] tabletParts = null;
                if (tabletLeft > 0)
                {
                    int[] probe = (int[])parts.Clone();
                    ApplyOneTablet(probe);
                    int removed = ImpurityOf(parts) - ImpurityOf(probe);
                    if (removed > 0)
                    {
                        tabletOk = true;
                        tabletUnit = TabletCredit(stock) / (double)removed;
                        tabletParts = probe;
                    }
                }

                if (!filterOk && !tabletOk)
                {
                    break;
                }

                bool useFilter = filterOk && (!tabletOk || filterUnit <= tabletUnit);
                if (useFilter)
                {
                    CopyParts(filterParts, parts);
                    CopyDurability(filterWorking, working);
                    passes++;
                    pours += filterPours;
                }
                else
                {
                    CopyParts(tabletParts, parts);
                    tabletLeft--;
                    tablets++;
                }
            }

            return new GreedyOutcome { Passes = passes, PitcherPours = pours, Tablets = tablets };
        }

        private static double CreditCostForFilters(List<FilterSnapshot> after)
        {
            double cost = 0;
            foreach (FilterSnapshot filter in after)
            {
                int used = Math.Max(0, filter.DurabilityBefore - filter.Durability);
                if (used <= 0)
                {
                    continue;
                }

                long value = filter.UnitValue > 0 ? filter.UnitValue : FallbackFilterValue;
                int maxDur = Math.Max(1, filter.MaxDurability);
                cost += used * (double)value / maxDur;
            }

            return cost;
        }

        private static long TabletCredit(FilterStock stock)
        {
            if (stock?.Tablets != null)
            {
                foreach (FilterSnapshot tablet in stock.Tablets)
                {
                    if (tablet.UnitValue > 0)
                    {
                        return tablet.UnitValue;
                    }
                }
            }

            return FallbackTabletValue;
        }

        private static int ImpurityOf(int[] parts)
        {
            int waterIndex = IndexOf(Liquid.LIQUID_WATER);
            int total = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                if (i == waterIndex)
                {
                    continue;
                }

                total += Math.Max(0, parts[i]);
            }

            return total;
        }

        private static void CopyParts(int[] source, int[] dest)
        {
            int n = Math.Min(source.Length, dest.Length);
            for (int i = 0; i < n; i++)
            {
                dest[i] = source[i];
            }
        }

        private static void CopyDurability(List<FilterSnapshot> source, List<FilterSnapshot> dest)
        {
            int n = Math.Min(source.Count, dest.Count);
            for (int i = 0; i < n; i++)
            {
                dest[i].Durability = source[i].Durability;
            }
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
            int pours,
            bool onePass,
            int tabletsConsumed = 0)
        {
            int consumed = working.Sum(f => Math.Max(0, f.DurabilityBefore - f.Durability));
            int depleted = working.Count(f => f.DurabilityBefore > 0 && f.Durability <= 0);
            int purityBefore = PurityOf(partsBefore);
            int purityAfter = PurityOf(partsAfter);
            FilterSimResult result = new FilterSimResult
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
                ReachedTarget = onePass || purityAfter >= target,
                HasUv = hasUv,
                UsedUv = usedUv,
                TabletsConsumed = tabletsConsumed,
                NoMechanicalFilters = working.Count == 0,
                NoChange = purityAfter == purityBefore && consumed == 0 && !usedUv && tabletsConsumed <= 0,
                IsOnePass = onePass
            };
            FillImpurity(result);
            return result;
        }

        private static void FillImpurity(FilterSimResult result)
        {
            int waterIndex = IndexOf(Liquid.LIQUID_WATER);
            int beforeImp = 0;
            int afterImp = 0;
            HashSet<string> unfilterable = new HashSet<string>(result.UnfilterableNames ?? new List<string>());
            int length = Math.Max(result.PartsBefore?.Length ?? 0, result.PartsAfter?.Length ?? 0);
            for (int i = 0; i < Liquid.Liquids.Count && i < length; i++)
            {
                if (i == waterIndex)
                {
                    continue;
                }

                int before = result.PartsBefore != null && i < result.PartsBefore.Length
                    ? Math.Max(0, result.PartsBefore[i])
                    : 0;
                int after = result.PartsAfter != null && i < result.PartsAfter.Length
                    ? Math.Max(0, result.PartsAfter[i])
                    : 0;
                if (before <= 0 && after <= 0)
                {
                    continue;
                }

                beforeImp += before;
                afterImp += after;
                result.ImpurityDeltas.Add(new ImpurityDelta
                {
                    Name = Liquid.Liquids[i].displayName,
                    BeforeUl = before,
                    AfterUl = after,
                    Unfilterable = unfilterable.Contains(Liquid.Liquids[i].displayName)
                });
            }

            result.ImpurityDeltas.Sort((a, b) => b.RemovedUl.CompareTo(a.RemovedUl));
            result.ImpurityBeforeUl = beforeImp;
            result.ImpurityAfterUl = afterImp;
            result.ImpurityRemovedUl = Math.Max(0, beforeImp - afterImp);
        }

        public static string FormatPurity(int purity)
        {
            return $"{purity / 100f:0.00}%";
        }

        public static string FormatMl(int ul)
        {
            return $"{ul / 1000f:0.##}";
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
