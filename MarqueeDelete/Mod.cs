using System;
using System.Collections.Generic;
using Colossal.IO.AssetDatabase;
using Colossal.Localization;
using Game;
using Game.Modding;
using Game.SceneFlow;

namespace MarqueeDelete
{
    public sealed class Mod : IMod
    {
        public const string ModName = "MarqueeDelete";
        public const string ModVersion = "2.7.7";
        public const string TargetGameMvid = Compatibility.VerifiedGameMvid;
        public const string TargetGameVersion = Compatibility.VerifiedGameVersion;
        public static string DetectedGameMvid => Compatibility.DetectedMvid;
        public static Settings Settings { get; private set; }
        public static bool RuntimeActive { get; private set; }

        /// <summary>
        /// 模组当前是否处于仅预览模式（未通过目标游戏构建验证，禁止破坏性拆除与撤回重建写入）。
        /// </summary>
        public static bool IsPreviewOnly => Compatibility.IsPreviewOnly;

        /// <summary>
        /// 是否允许物理提交拆除（要求 DeletionSubmit 边界达到 Verified 级别）。
        /// </summary>
        public static bool CanSubmitDeletion => Compatibility.CanSubmitDeletion;

        /// <summary>
        /// 是否允许撤回重建写入（要求 UndoRebuild 边界达到 Verified 级别）。
        /// </summary>
        public static bool CanUndoRebuild => Compatibility.CanUndoRebuild;

        internal static readonly DeferredSaveCoordinator ColorSaveCoordinator = new DeferredSaveCoordinator();

        private static readonly Dictionary<string, ModLocaleSource> s_LocaleSources =
            new Dictionary<string, ModLocaleSource>();
        private static LocalizationManager s_LocalizationManager;
        private static InterfaceLanguage s_AppliedLanguage;

        public void OnLoad(UpdateSystem updateSystem)
        {
            // 启动时初始化四组兼容层检测与三态分级缓存（方案第 1 节）
            Compatibility.Initialize(TargetGameMvid, TargetGameVersion);

            var defaults = new Settings(this);
            Settings = new Settings(this);
            Settings.RegisterKeyBindings();
            AssetDatabase.global.LoadSettings(ModName, Settings, defaults);
            Settings.SanitizeColors();
            RegisterLocalizations();
            Settings.RegisterInOptionsUI();

            // 只要搜索拾取组可用即可进入运行（支持预览模式），不因 MVID 变动一票否决；
            // 处于 PreviewOnly 时，物理删除与撤回重建写入将被后端门闩硬拦截。
            RuntimeActive = Compatibility.CanRun;

            if (RuntimeActive)
            {
                updateSystem.UpdateAt<MarqueeDeleteToolSystem>(SystemUpdatePhase.ToolUpdate);
                updateSystem.UpdateAt<MarqueeDeleteUndoSystem>(SystemUpdatePhase.ToolUpdate);
                updateSystem.UpdateAt<MarqueeDeleteUISystem>(SystemUpdatePhase.UIUpdate);
            }
        }

        public void OnDispose()
        {
            // Flush deferred color saves BEFORE unregistering or clearing Settings.
            ColorSaveCoordinator.Flush();
            RuntimeActive = false;
            if (Settings != null)
            {
                Settings.UnregisterInOptionsUI();
                Settings = null;
            }

            if (s_LocalizationManager != null)
            {
                foreach (var source in s_LocaleSources)
                    s_LocalizationManager.RemoveSource(source.Key, source.Value);

                s_LocaleSources.Clear();
                s_LocalizationManager = null;
            }
        }

        private static void RegisterLocalizations()
        {
            LocalizationManager loc = GameManager.instance?.localizationManager;
            if (loc == null || Settings == null)
            {
                return;
            }

            s_LocalizationManager = loc;
            s_AppliedLanguage = Settings.Language;
            foreach (string locale in loc.GetSupportedLocales())
            {
                var source = new ModLocaleSource(Settings, locale);
                s_LocaleSources.Add(locale, source);
                loc.AddSource(locale, source);
            }
        }

        internal static void ApplyInterfaceLanguage(Settings settings)
        {
            if (settings != Settings || s_LocalizationManager == null || s_AppliedLanguage == settings.Language)
                return;

            s_AppliedLanguage = settings.Language;
            s_LocalizationManager.ReloadActiveLocale();
        }
    }
}
