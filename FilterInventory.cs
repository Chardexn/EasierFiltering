using System;
using System.Collections.Generic;
using System.Linq;

namespace EasierFiltering
{
    internal sealed class FilterSnapshot
    {
        public GameItem Source;
        public string Identifier;
        public string DisplayName;
        public int PoreSize;
        public int Durability;
        public int DurabilityBefore;
        public int MaxDurability;
        public bool IsUv;

        public FilterSnapshot Clone()
        {
            return new FilterSnapshot
            {
                Source = Source,
                Identifier = Identifier,
                DisplayName = DisplayName,
                PoreSize = PoreSize,
                Durability = Durability,
                DurabilityBefore = Durability,
                MaxDurability = MaxDurability,
                IsUv = IsUv
            };
        }
    }

    internal sealed class FilterStock
    {
        public List<FilterSnapshot> Filters = new List<FilterSnapshot>();
        public List<FilterSnapshot> UvLamps = new List<FilterSnapshot>();
        public List<FilterSnapshot> Tablets = new List<FilterSnapshot>();
        public int TotalDurability;
        public bool HasUv => UvLamps.Count > 0;
        public int TabletCount => Tablets.Count;
    }

    /// <summary>
    /// 按配置从玩家库存收集过滤器，并处理耗尽滤芯的去向。
    /// </summary>
    internal static class FilterInventory
    {
        public const string WATER_FILTER_TAG = "WATER_FILTER";
        public const string TABLET_TAG = "WATER_PURIFICATION_TABLET";
        public const string TABLET_ID = "water_tablet";
        public const string TABLET_BOX_ID = "water_tablet_container";
        public const string UV_FILTER_ID = "uv_filter";
        public const string ADV_FILTER_ID = "water_filter_adv";
        public const string BASIC_FILTER_ID = "water_filter";
        public const string HYDROREED_FILTER_ID = "hydroreed_filter";
        public const string KOTTON_FABRIC_ID = "kotton_fabric";

        private static readonly HashSet<string> StandardFilterIds = new HashSet<string>
        {
            BASIC_FILTER_ID,
            ADV_FILTER_ID,
            HYDROREED_FILTER_ID
        };

        #region MARK: 收集
        public static FilterStock Collect()
        {
            FilterStock stock = new FilterStock();
            foreach (GameItem item in EnumerateOwnedItems())
            {
                if (item == null)
                {
                    continue;
                }

                if (item.identifier == UV_FILTER_ID)
                {
                    stock.UvLamps.Add(ToSnapshot(item, isUv: true));
                    continue;
                }

                if (item.IsTag(TABLET_TAG) || item.identifier == TABLET_ID)
                {
                    stock.Tablets.Add(ToSnapshot(item, isUv: false));
                    continue;
                }

                if (!item.IsTag(WATER_FILTER_TAG))
                {
                    continue;
                }

                if (!IsEligibleMechanicalFilter(item))
                {
                    continue;
                }

                FilterSnapshot snap = ToSnapshot(item, isUv: false);
                if (snap.PoreSize <= 0)
                {
                    continue;
                }

                stock.Filters.Add(snap);
            }

            stock.Filters = SortForUse(stock.Filters);
            stock.TotalDurability = stock.Filters.Sum(f => Math.Max(0, f.Durability));
            return stock;
        }

        public static List<FilterSnapshot> SortForUse(List<FilterSnapshot> filters)
        {
            return filters
                .OrderBy(Rank)
                .ThenBy(f => f.Durability)
                .ThenBy(f => f.MaxDurability)
                .ToList();
        }

        private static int Rank(FilterSnapshot filter)
        {
            if (filter.Identifier == ADV_FILTER_ID)
            {
                return 0;
            }

            if (filter.Identifier == BASIC_FILTER_ID)
            {
                return 1;
            }

            if (filter.Identifier == HYDROREED_FILTER_ID)
            {
                return 2;
            }

            return 3;
        }

        private static bool IsEligibleMechanicalFilter(GameItem item)
        {
            if (StandardFilterIds.Contains(item.identifier))
            {
                return true;
            }

            return FilterConfig.IncludeFabric.Value;
        }

        private static FilterSnapshot ToSnapshot(GameItem item, bool isUv)
        {
            int durability = FilterConfig.UseRealDurability.Value
                ? DurabilityHelper.GetRealDurability(item)
                : DurabilityHelper.GetFakeDurability(item);
            if (isUv)
            {
                durability = 0;
            }

            return new FilterSnapshot
            {
                Source = item,
                Identifier = item.identifier,
                DisplayName = string.IsNullOrEmpty(item.name) ? item.identifier : item.name,
                PoreSize = isUv ? 0 : item.GetTagReadonly(Liquid.FILTER_SIZE_TAG).GetInt(),
                Durability = Math.Max(0, durability),
                DurabilityBefore = Math.Max(0, durability),
                MaxDurability = isUv ? 0 : DurabilityHelper.GetMaxDurability(item),
                IsUv = isUv
            };
        }
        #endregion

        #region MARK: 搜索范围
        private static IEnumerable<GameItem> EnumerateOwnedItems()
        {
            EmporiumEntry shop = EmporiumEntry.Instance;
            if (shop == null)
            {
                yield break;
            }

            List<GameInventory> roots = new List<GameInventory>();
            switch (FilterConfig.SearchScope)
            {
                case SearchScope.StorageOnly:
                    AddRoot(roots, shop.invElement);
                    break;
                case SearchScope.AllOwned:
                    AddRoot(roots, shop.invElement);
                    AddRoot(roots, shop.backInvinvElement);
                    AddRoot(roots, shop.backInvinvElementCounter);
                    AddRoot(roots, shop.frontInvinvElement);
                    AddRoot(roots, shop.showcaseElement);
                    AddRoot(roots, shop.hiddenElement);
                    break;
                default:
                    AddRoot(roots, shop.invElement);
                    AddRoot(roots, shop.backInvinvElement);
                    AddRoot(roots, shop.backInvinvElementCounter);
                    break;
            }

            HashSet<GameItem> seen = new HashSet<GameItem>();
            foreach (GameInventory root in roots)
            {
                foreach (GameItem item in GraphUtils.FindAllChildrenType<GameItem>(root))
                {
                    if (item != null && GeneralHelper.IsItemOwned(item) && seen.Add(item))
                    {
                        yield return item;
                    }
                }
            }
        }

        private static void AddRoot(List<GameInventory> roots, GameInventory inventory)
        {
            if (inventory != null)
            {
                roots.Add(inventory);
            }
        }
        #endregion

        #region MARK: 执行写回
        public static void Apply(GameItem container, FilterSimResult result)
        {
            if (container == null || result == null || result.PartsAfter == null)
            {
                return;
            }

            for (int i = 0; i < Liquid.Liquids.Count && i < result.PartsAfter.Length; i++)
            {
                int amount = result.PartsAfter[i];
                container.ModifyTag(Liquid.Liquids[i].currentPart, state =>
                {
                    state.Enable().SetInt(amount);
                }, skipValidate: true);
            }

            foreach (FilterSnapshot filter in result.FiltersAfter)
            {
                if (filter == null || filter.IsUv || filter.Source == null)
                {
                    continue;
                }

                int consumed = filter.DurabilityBefore - filter.Durability;
                if (consumed > 0)
                {
                    DurabilityHelper.ChangeDurability(filter.Source, -consumed);
                }
            }

            ConsumeTablets(result);
            WaterHelper.UpdateContainerGoodType(container);
            WaterFeatureHelper.OnLiquidChanged(container, exposeFeature: false);
            WaterFeatureHelper.SetPresumedPurity(container, result.PurityAfter);
            AudioHelper.UseWaterFilter();
            RelocateDepleted(result);
        }

        private static void ConsumeTablets(FilterSimResult result)
        {
            int remaining = result.TabletsConsumed;
            if (remaining <= 0)
            {
                return;
            }

            foreach (GameItem item in EnumerateOwnedItems())
            {
                if (remaining <= 0)
                {
                    break;
                }

                if (item == null || (!item.IsTag(TABLET_TAG) && item.identifier != TABLET_ID))
                {
                    continue;
                }

                item.Destroy();
                remaining--;
            }
        }

        private static void RelocateDepleted(FilterSimResult result)
        {
            DepletedDestination destination = FilterConfig.DepletedDestination;
            if (destination == DepletedDestination.Leave)
            {
                return;
            }

            foreach (FilterSnapshot filter in result.FiltersAfter)
            {
                if (filter == null || filter.IsUv || filter.Source == null)
                {
                    continue;
                }

                if (filter.DurabilityBefore <= 0 || filter.Durability > 0)
                {
                    continue;
                }

                switch (destination)
                {
                    case DepletedDestination.Trash:
                        ModHelper.MoveItemToTrash(filter.Source);
                        break;
                    case DepletedDestination.Storage:
                        ModHelper.MoveItemToInventory(filter.Source);
                        break;
                    case DepletedDestination.BackCounter:
                        ModHelper.MoveItemToBackCounter(filter.Source);
                        break;
                }
            }
        }
        #endregion
    }
}
