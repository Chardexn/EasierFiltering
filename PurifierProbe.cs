using System;
using System.Collections.Generic;

namespace EasierFiltering
{
    internal sealed class PurifierInfo
    {
        public GameItem Machine;
        public int QualityBonus;
        public int PerfBonus;
        public int Cap;
        public bool HasPower;
        public long TonightBudgetUl;
    }

    /// <summary>
    /// 查找容器所在的净水器，并读取品质/性能加成、上限和电量。
    /// </summary>
    internal static class PurifierProbe
    {
        public const string MACHINE_ID = "water_purifier";
        public const string POWER_SOURCE_TAG = "power_source_item";

        #region MARK: 查找
        public static PurifierInfo Find(GameItem container)
        {
            if (container == null)
            {
                return null;
            }

            try
            {
                GameItem machine = FindMachine(container);
                if (machine == null)
                {
                    return null;
                }

                return ReadInfo(machine, container);
            }
            catch (Exception ex)
            {
                ModEntry.Instance?.Log?.Log($"PurifierProbe failed: {ex.Message}");
                return null;
            }
        }

        private static GameItem FindMachine(GameItem container)
        {
            List<GameItem> parents = GraphUtils.FindParentsType<GameItem>(
                container,
                item => item != null && item.identifier == MACHINE_ID,
                _ => true);
            if (parents != null)
            {
                foreach (GameItem parent in parents)
                {
                    if (parent != null)
                    {
                        return parent;
                    }
                }
            }

            if (EmporiumEntry.Instance == null)
            {
                return null;
            }

            foreach (GameItem item in EmporiumEntry.Instance.GetAllOwnedItems())
            {
                if (item == null || item.identifier != MACHINE_ID)
                {
                    continue;
                }

                if (ContainsContainer(item, container))
                {
                    return item;
                }
            }

            return null;
        }

        private static bool ContainsContainer(GameItem machine, GameItem container)
        {
            if (machine.contentWindow != null)
            {
                foreach (GameItem child in GraphUtils.FindChildrenType<GameItem>(machine.contentWindow))
                {
                    if (child == container)
                    {
                        return true;
                    }
                }
            }

            foreach (GameItem child in machine.FindAllChildItems())
            {
                if (child == container)
                {
                    return true;
                }
            }

            return false;
        }
        #endregion

        #region MARK: 读取
        private static PurifierInfo ReadInfo(GameItem machine, GameItem container)
        {
            int quality = MachineryHelper.GetCurrentQualityBonus(machine);
            int perf = MachineryHelper.GetCurrentPerformanceBonus(machine);
            int[] parts = WaterHelper.GetLiquidParts(container);
            return new PurifierInfo
            {
                Machine = machine,
                QualityBonus = quality,
                PerfBonus = perf,
                Cap = MachinePurifier.GetMaxPurityCap(quality),
                HasPower = HasUsablePower(machine),
                TonightBudgetUl = FilterSimulator.ComputePurifierBudget(parts, perf)
            };
        }

        private static bool HasUsablePower(GameItem machine)
        {
            GameItem battery = FindBattery(machine);
            if (battery == null)
            {
                return false;
            }

            int usage = MachineryHelper.GetMachinePowerUsage(machine);
            return PowerHelper.CanDrawPowerSource(battery, usage);
        }

        private static GameItem FindBattery(GameItem machine)
        {
            if (machine.contentWindow != null)
            {
                foreach (GameItem child in GraphUtils.FindChildrenType<GameItem>(machine.contentWindow))
                {
                    if (child != null && child.IsTag(POWER_SOURCE_TAG))
                    {
                        return child;
                    }
                }
            }

            foreach (GameItem child in machine.FindAllChildItems())
            {
                if (child != null && child.IsTag(POWER_SOURCE_TAG))
                {
                    return child;
                }
            }

            return null;
        }
        #endregion
    }
}
