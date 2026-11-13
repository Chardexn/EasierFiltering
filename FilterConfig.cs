namespace EasierFiltering
{
    internal enum SearchScope
    {
        StorageAndBack = 0,
        AllOwned = 1,
        StorageOnly = 2
    }

    internal enum DepletedDestination
    {
        Leave = 0,
        Trash = 1,
        Storage = 2,
        BackCounter = 3
    }

    /// <summary>
    /// 模组配置。Define 在 Init 调用；标签用延迟 Func，打开面板时才读本地化。
    /// </summary>
    internal static class FilterConfig
    {
        public static ConfigEntry<SearchScope> SearchScopeEntry { get; private set; }
        public static ConfigEntry<DepletedDestination> DepletedDestinationEntry { get; private set; }
        public static ConfigEntry<bool> UseUv { get; private set; }
        public static ConfigEntry<bool> UseRealDurability { get; private set; }
        public static ConfigEntry<bool> IncludeFabric { get; private set; }
        public static ConfigEntry<bool> GrantToolOnLoad { get; private set; }

        public static SearchScope SearchScope => SearchScopeEntry.Value;
        public static DepletedDestination DepletedDestination => DepletedDestinationEntry.Value;

        #region MARK: 定义
        public static void Define()
        {
            ModConfig cfg = ModHelper.GetConfig(ModEntry.MOD_ID);
            const string section = "General";

            SearchScopeEntry = cfg.DefineEnum(
                "search_scope",
                SearchScope.StorageAndBack,
                section,
                () => ModEntry.L("cfg_search_scope"),
                () => ModEntry.L("cfg_search_scope_tip"));

            DepletedDestinationEntry = cfg.DefineEnum(
                "depleted_destination",
                DepletedDestination.Leave,
                section,
                () => ModEntry.L("cfg_depleted"),
                () => ModEntry.L("cfg_depleted_tip"));

            UseUv = cfg.Define(
                "use_uv",
                true,
                section,
                () => ModEntry.L("cfg_use_uv"),
                () => ModEntry.L("cfg_use_uv_tip"));

            UseRealDurability = cfg.Define(
                "use_real_durability",
                true,
                section,
                () => ModEntry.L("cfg_real_dur"),
                () => ModEntry.L("cfg_real_dur_tip"));

            IncludeFabric = cfg.Define(
                "include_fabric",
                false,
                section,
                () => ModEntry.L("cfg_fabric"),
                () => ModEntry.L("cfg_fabric_tip"));

            GrantToolOnLoad = cfg.Define(
                "grant_tool_on_load",
                true,
                section,
                () => ModEntry.L("cfg_grant"),
                () => ModEntry.L("cfg_grant_tip"));
        }
        #endregion
    }
}
