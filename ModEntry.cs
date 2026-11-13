using System;

namespace EasierFiltering
{
    /// <summary>
    /// Mod 入口。Init / OnEnable 在主菜单运行，游戏内逻辑放进钩子。
    /// </summary>
    public class ModEntry : IMod
    {
        public const string MOD_ID = "easier_filtering";

        public static ModEntry Instance { get; private set; }
        public ModManifest Manifest { get; private set; }
        public ModLog Log { get; private set; }

        #region MARK: 生命周期
        public void Init(ModManifest manifest)
        {
            Instance = this;
            Manifest = manifest;
            Log = new ModLog(manifest);
            FilterConfig.Define();
            Log.Log($"{manifest.Name} v{manifest.ModVersion} loaded.");
        }

        public void OnEnable()
        {
            ModHook.OnModItemDirectoryInit += RegisterItems;
            ModHook.OnGameLoadedNormal += OnGameLoaded;
            FilterTool.RegisterDebugFunctions();
        }

        public void OnDisable()
        {
            ModHook.OnModItemDirectoryInit -= RegisterItems;
            ModHook.OnGameLoadedNormal -= OnGameLoaded;
            ModHelper.RemoveDebugFunctions(MOD_ID);
        }
        #endregion

        #region MARK: 钩子处理
        private void RegisterItems(ModItemDirectory dir)
        {
            dir.Add(FilterTool.ITEM_ID, FilterTool.Create);
        }

        private void OnGameLoaded()
        {
            FilterTool.TryGrantOnLoad();
        }
        #endregion

        #region MARK: 本地化
        public static string L(string key, params object[] args)
        {
            return ModHelper.GetLocalized(MOD_ID, key, args);
        }
        #endregion
    }
}
