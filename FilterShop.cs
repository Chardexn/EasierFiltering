using System;
using System.Collections.Generic;
using HarmonyLib;

namespace EasierFiltering
{
    /// <summary>
    /// 杰克逊博士 / 退休水商上架便携过滤装置。已有则不上架（含工具箱内）。
    /// </summary>
    internal static class FilterShop
    {
        public const int JacksonChance = 50;
        public const int RetiredMerchantChance = 75;

        #region MARK: 上架
        public static void TryOfferAtJackson()
        {
            TryOffer(JacksonChance);
        }

        public static void TryOfferAtRetiredMerchant()
        {
            TryOffer(RetiredMerchantChance);
        }

        private static void TryOffer(int chancePercent)
        {
            if (PlayerStore.Instance == null || !NetworkUpgrade.IsWaterTraderUnlocked())
            {
                return;
            }

            if (PlayerOwnsTool())
            {
                return;
            }

            if (!RNG.Roll(chancePercent))
            {
                return;
            }

            PlayerStore.Instance.AddDirectSellingItemToTable(FilterTool.ITEM_ID);
        }
        #endregion

        #region MARK: 是否已拥有
        /// <summary>
        /// 遍历已拥有物品及其容器（含工具箱格子），避免只扫外层漏掉箱内工具。
        /// </summary>
        public static bool PlayerOwnsTool()
        {
            HashSet<GameItem> seen = new HashSet<GameItem>();
            if (EmporiumEntry.Instance != null)
            {
                foreach (GameItem item in EmporiumEntry.Instance.GetAllOwnedItems())
                {
                    if (ContainsTool(item, seen))
                    {
                        return true;
                    }
                }
            }

            if (PlayerStore.Instance != null && PlayerStore.Instance.IsPlayerOwnThisItem(FilterTool.ITEM_ID))
            {
                return true;
            }

            ItemSelectHandler handler = ItemSelectHandler.current;
            GameItem equipped = handler != null ? handler.GetEquippedItem() : null;
            return equipped != null && equipped.identifier == FilterTool.ITEM_ID;
        }

        private static bool ContainsTool(GameItem item, HashSet<GameItem> seen)
        {
            if (item == null || !seen.Add(item))
            {
                return false;
            }

            if (item.identifier == FilterTool.ITEM_ID)
            {
                return true;
            }

            foreach (GameItem child in item.FindAllChildItems())
            {
                if (child != null && child.identifier == FilterTool.ITEM_ID)
                {
                    return true;
                }
            }

            if (!item.IsTag(ToolboxHelper.TOOLBOX_TAG))
            {
                return false;
            }

            foreach (GameSlotInventory slot in ToolboxHelper.GetToolboxSlots(item))
            {
                if (ContainsTool(slot?.childItem, seen))
                {
                    return true;
                }
            }

            return false;
        }
        #endregion
    }

    #region MARK: 杰克逊库存
    [HarmonyPatch(typeof(StoreClientList), nameof(StoreClientList.PlaceInventorInventory))]
    internal static class JacksonInventoryPatch
    {
        private static void Postfix()
        {
            FilterShop.TryOfferAtJackson();
        }
    }
    #endregion

    #region MARK: 退休水商
    [HarmonyPatch(typeof(StoreClientListSpec), nameof(StoreClientListSpec.CreateRetiredWaterMerchant))]
    internal static class RetiredWaterMerchantPatch
    {
        private static void Postfix(StoreClient __result)
        {
            if (__result?.mainDialogue == null)
            {
                return;
            }

            Action original = __result.mainDialogue.endAction;
            __result.mainDialogue.endAction = () =>
            {
                original?.Invoke();
                FilterShop.TryOfferAtRetiredMerchant();
            };
        }
    }
    #endregion
}
