using System;
using HarmonyLib;

namespace EasierFiltering
{
    /// <summary>
    /// 整瓶净水药片可直接拖到装水容器上，消耗一颗，效果与单独拖药片相同。
    /// </summary>
    internal static class TabletBottle
    {
        #region MARK: 接线
        public static void Wire(GameItem bottle)
        {
            if (bottle == null)
            {
                return;
            }

            bottle.mayThisTargetItemFunc = (Func<GameItem, GameItem, bool>)Delegate.Combine(
                bottle.mayThisTargetItemFunc,
                new Func<GameItem, GameItem, bool>(MayUse));
            bottle.canThisTargetItemFunc = (Func<GameItem, GameItem, bool>)Delegate.Combine(
                bottle.canThisTargetItemFunc,
                new Func<GameItem, GameItem, bool>(MayUse));
            bottle.onItemTargetItemFunc = (Action<GameItem, GameItem>)Delegate.Combine(
                bottle.onItemTargetItemFunc,
                new Action<GameItem, GameItem>(UseOne));
        }

        private static bool MayUse(GameItem bottle, GameItem target)
        {
            if (bottle == null || target == null)
            {
                return false;
            }

            if (FilterConfig.UseBottleDrag == null || !FilterConfig.UseBottleDrag.Value)
            {
                return false;
            }

            if (!GeneralHelper.IsItemOwned(bottle) || !GeneralHelper.IsItemOwned(target))
            {
                return false;
            }

            if (!target.IsTag(WaterHelper.LIQUID_CONTAINER_TAG) || WaterHelper.IsEmpty(target))
            {
                return false;
            }

            return FindTablet(bottle) != null;
        }

        private static void UseOne(GameItem bottle, GameItem target)
        {
            if (!MayUse(bottle, target))
            {
                return;
            }

            GameItem tablet = FindTablet(bottle);
            if (tablet == null)
            {
                return;
            }

            ChemicalHelper.DissolveInto(tablet, target);
        }

        private static GameItem FindTablet(GameItem bottle)
        {
            foreach (GameItem child in bottle.FindAllChildItems())
            {
                if (child != null && (child.IsTag(FilterInventory.TABLET_TAG) || child.identifier == FilterInventory.TABLET_ID))
                {
                    return child;
                }
            }

            return null;
        }
        #endregion
    }

    #region MARK: 工厂补丁
    [HarmonyPatch(typeof(AmenitiesItemDirectory), "WaterPurificationTabletContainer")]
    internal static class TabletBottlePatch
    {
        private static void Postfix(GameItem __result)
        {
            TabletBottle.Wire(__result);
        }
    }
    #endregion
}
