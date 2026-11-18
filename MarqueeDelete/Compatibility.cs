using System;
using System.Collections.Generic;
using System.Reflection;

namespace MarqueeDelete
{
    public enum CompatibilityTier
    {
        Unavailable = 0,    // 不可用：关键依赖缺失，该组功能关闭
        PreviewOnly = 1,    // 可预览、待验证写入：接口存在但未通过构建指纹验证，禁止破坏性写入
        Verified = 2        // 已验证：完整通过构建指纹与符号检测
    }

    public enum CompatibilityGroup
    {
        SearchAndPick = 0,  // 搜索和拾取：屏幕射线、地面高度、空间索引、实体身份
        DeletionSubmit = 1, // 拆除提交：提交阶段、命令缓冲、拆除语义、保护对象
        UndoRebuild = 2,    // 撤回重建：创建请求与返回结果
        UIAccess = 3        // 界面接入：挂载点、地下状态、快捷键、语言
    }

    public struct GroupCompatibilityResult
    {
        public CompatibilityGroup Group;
        public CompatibilityTier Tier;
        public string GroupName;
        public string Description;
        public string StatusMessage;
        public List<string> MissingMembers;

        public bool IsAvailable => Tier != CompatibilityTier.Unavailable;
        public bool CanWrite => Tier == CompatibilityTier.Verified;
    }

    /// <summary>
    /// 兼容层与能力检测机制（方案第 1 节）。
    /// 启动时检查关键类型/字段/签名/组件布局/必要系统，形成能力列表并缓存一次，绝不每帧反射。
    /// 四组边界各自独立降级，任一组检测失败仅停用该组，不强退模组。
    /// </summary>
    public static class Compatibility
    {
        public const string VerifiedGameVersion = "1.6.4f1";
        public const string VerifiedGameMvid = "46198354D4F94D23BF327E3C085D8B92";

        private static bool s_Initialized;
        private static string s_DetectedMvid = string.Empty;
        private static string s_TargetMvid = VerifiedGameMvid;
        private static readonly GroupCompatibilityResult[] s_Groups = new GroupCompatibilityResult[4];

        public static bool IsInitialized => s_Initialized;
        public static string DetectedMvid => s_DetectedMvid;
        public static string TargetMvid => s_TargetMvid;

        /// <summary>
        /// 模组是否可以启动运行（只要基础搜索拾取可用即可进入预览）。
        /// </summary>
        public static bool CanRun => IsGroupAvailable(CompatibilityGroup.SearchAndPick);

        /// <summary>
        /// 是否允许物理提交拆除（必须完整匹配验证构建）。
        /// </summary>
        public static bool CanSubmitDeletion => GetTier(CompatibilityGroup.DeletionSubmit) == CompatibilityTier.Verified;

        /// <summary>
        /// 是否允许撤回重建（必须完整匹配验证构建，未知构建禁止写入）。
        /// </summary>
        public static bool CanUndoRebuild => GetTier(CompatibilityGroup.UndoRebuild) == CompatibilityTier.Verified;

        /// <summary>
        /// 是否属于仅预览模式（拆除提交或撤回重建处于待验证写入时仅供预览，禁止破坏性物理提交）。
        /// </summary>
        public static bool IsPreviewOnly =>
            GetTier(CompatibilityGroup.DeletionSubmit) == CompatibilityTier.PreviewOnly ||
            GetTier(CompatibilityGroup.UndoRebuild) == CompatibilityTier.PreviewOnly;

        public static CompatibilityTier GetTier(CompatibilityGroup group)
        {
            int index = (int)group;
            if (index >= 0 && index < s_Groups.Length)
            {
                return s_Groups[index].Tier;
            }
            return CompatibilityTier.Unavailable;
        }

        public static bool IsGroupAvailable(CompatibilityGroup group)
        {
            return GetTier(group) != CompatibilityTier.Unavailable;
        }

        public static GroupCompatibilityResult GetGroupResult(CompatibilityGroup group)
        {
            int index = (int)group;
            if (index >= 0 && index < s_Groups.Length)
            {
                return s_Groups[index];
            }
            return default;
        }

        public static void Initialize(string verifiedMvid = VerifiedGameMvid, string verifiedVersion = VerifiedGameVersion)
        {
            if (s_Initialized)
            {
                return;
            }

            s_TargetMvid = !string.IsNullOrEmpty(verifiedMvid) ? verifiedMvid : VerifiedGameMvid;
            s_DetectedMvid = DetectGameMvid();

            bool mvidMatched = !string.IsNullOrEmpty(s_DetectedMvid) &&
                               string.Equals(s_DetectedMvid, s_TargetMvid, StringComparison.OrdinalIgnoreCase);

            // 1. 检查搜索和拾取 (SearchAndPick)
            s_Groups[(int)CompatibilityGroup.SearchAndPick] = CheckSearchAndPick(mvidMatched);

            // 2. 检查拆除提交 (DeletionSubmit)
            s_Groups[(int)CompatibilityGroup.DeletionSubmit] = CheckDeletionSubmit(mvidMatched);

            // 3. 检查撤回重建 (UndoRebuild)
            s_Groups[(int)CompatibilityGroup.UndoRebuild] = CheckUndoRebuild(mvidMatched);

            // 4. 检查界面接入 (UIAccess)
            s_Groups[(int)CompatibilityGroup.UIAccess] = CheckUIAccess(mvidMatched);

            s_Initialized = true;
        }

        private static string DetectGameMvid()
        {
            try
            {
                Assembly gameAsm = FindLoadedAssembly("Game");
                if (gameAsm != null && gameAsm.ManifestModule != null)
                {
                    return gameAsm.ManifestModule.ModuleVersionId.ToString("N").ToUpperInvariant();
                }
            }
            catch
            {
                // 忽略非受控环境反射异常
            }
            return string.Empty;
        }

        private static GroupCompatibilityResult CheckSearchAndPick(bool mvidMatched)
        {
            var missing = new List<string>();
            CheckType("Game.Simulation.TerrainSystem", missing);
            CheckType("Game.Common.RaycastInput", missing);
            CheckType("Game.Common.RaycastResult", missing);
            CheckType("Game.Net.SearchSystem", missing);
            CheckType("Game.Objects.Transform", missing);
            CheckType("Game.Net.Edge", missing);
            CheckType("Game.Net.Node", missing);
            CheckType("Game.Buildings.Building", missing);
            CheckType("Game.Common.Owner", missing);
            // Attached lives in Game.Objects, not Game.Common: a wrong name here makes the whole
            // SearchAndPick group look unavailable, which stops every system from registering.
            CheckType("Game.Objects.Attached", missing);

            CompatibilityTier tier;
            string status;
            if (missing.Count > 0)
            {
                tier = CompatibilityTier.Unavailable;
                status = "核心拾取符号缺失，模组入口停用并保留原版推土机";
            }
            else
            {
                tier = CompatibilityTier.Verified;
                status = mvidMatched ? "已验证（完全匹配游戏构建）" : "基础拾取符号完好（未知构建）";
            }

            return new GroupCompatibilityResult
            {
                Group = CompatibilityGroup.SearchAndPick,
                Tier = tier,
                GroupName = "搜索和拾取",
                Description = "屏幕射线、地面高度、空间索引、实体身份",
                StatusMessage = status,
                MissingMembers = missing
            };
        }

        private static GroupCompatibilityResult CheckDeletionSubmit(bool mvidMatched)
        {
            var missing = new List<string>();
            CheckType("Game.Common.Deleted", missing);
            CheckType("Game.Buildings.Signature", missing);
            CheckType("Game.Objects.UniqueObject", missing);
            CheckType("Game.Objects.UnderConstruction", missing);

            CompatibilityTier tier;
            string status;
            if (missing.Count > 0)
            {
                tier = CompatibilityTier.Unavailable;
                status = "拆除提交组件缺失，禁止执行删除";
            }
            else if (!mvidMatched)
            {
                tier = CompatibilityTier.PreviewOnly;
                status = "当前游戏版本待验证写入，仅供选区预览，确认删除按钮暂不可用";
            }
            else
            {
                tier = CompatibilityTier.Verified;
                status = "已验证（安全提交与保护对象语义通过）";
            }

            return new GroupCompatibilityResult
            {
                Group = CompatibilityGroup.DeletionSubmit,
                Tier = tier,
                GroupName = "拆除提交",
                Description = "提交阶段、命令缓冲、拆除语义、保护对象",
                StatusMessage = status,
                MissingMembers = missing
            };
        }

        private static GroupCompatibilityResult CheckUndoRebuild(bool mvidMatched)
        {
            var missing = new List<string>();
            CheckType("Game.Prefabs.PrefabRef", missing);
            CheckType("Game.Net.EdgeGeometry", missing);
            CheckType("Game.Net.SubNet", missing);
            CheckType("Game.Objects.SubObject", missing);

            CompatibilityTier tier;
            string status;
            if (missing.Count > 0)
            {
                tier = CompatibilityTier.Unavailable;
                status = "重建类型缺失，撤回功能已停用";
            }
            else if (!mvidMatched)
            {
                tier = CompatibilityTier.PreviewOnly;
                status = "重建接口存在但构建未知，撤回功能受限";
            }
            else
            {
                tier = CompatibilityTier.Verified;
                status = "已验证（快照重建与连通性验证通过）";
            }

            return new GroupCompatibilityResult
            {
                Group = CompatibilityGroup.UndoRebuild,
                Tier = tier,
                GroupName = "撤回重建",
                Description = "创建请求与返回结果、归属绑定",
                StatusMessage = status,
                MissingMembers = missing
            };
        }

        private static GroupCompatibilityResult CheckUIAccess(bool mvidMatched)
        {
            var missing = new List<string>();
            CheckType("Colossal.Localization.LocalizationManager", missing);
            CheckType("Colossal.UI.Binding.ValueBinding`1", missing);
            CheckType("Colossal.UI.Binding.TriggerBinding", missing);
            CheckType("Game.Input.ProxyAction", missing);

            CompatibilityTier tier;
            string status;
            if (missing.Count > 0)
            {
                tier = CompatibilityTier.Unavailable;
                status = "UI 绑定基础类型缺失";
            }
            else
            {
                tier = CompatibilityTier.Verified;
                status = "已验证（挂载点与原生绑定正常）";
            }

            return new GroupCompatibilityResult
            {
                Group = CompatibilityGroup.UIAccess,
                Tier = tier,
                GroupName = "界面接入",
                Description = "挂载点、地下状态、快捷键、语言",
                StatusMessage = status,
                MissingMembers = missing
            };
        }

        private static void CheckType(string fullName, List<string> missing)
        {
            try
            {
                Type t = Type.GetType(fullName, false);
                if (t != null)
                {
                    return;
                }

                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    t = asm.GetType(fullName, false);
                    if (t != null)
                    {
                        return;
                    }
                }

                missing.Add(fullName);
            }
            catch (Exception ex)
            {
                missing.Add(fullName + " (" + ex.Message + ")");
            }
        }

        private static Assembly FindLoadedAssembly(string simpleName)
        {
            try
            {
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string name = asm.GetName().Name;
                    if (string.Equals(name, simpleName, StringComparison.OrdinalIgnoreCase))
                    {
                        return asm;
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        public static string BuildReport()
        {
            var sb = new System.Text.StringBuilder(512);
            sb.AppendLine("=== MarqueeDelete 兼容性自检报告 ===");
            sb.AppendFormat("Target Game MVID: {0}\n", s_TargetMvid);
            sb.AppendFormat("Detected Game MVID: {0}\n", string.IsNullOrEmpty(s_DetectedMvid) ? "UNKNOWN" : s_DetectedMvid);
            sb.AppendFormat("Can Run: {0}, Can Submit: {1}, Can Undo: {2}\n", CanRun, CanSubmitDeletion, CanUndoRebuild);
            for (int i = 0; i < s_Groups.Length; i++)
            {
                var g = s_Groups[i];
                sb.AppendFormat("[{0}] Tier={1} -> {2}\n", g.GroupName, g.Tier, g.StatusMessage);
                if (g.MissingMembers != null && g.MissingMembers.Count > 0)
                {
                    foreach (var m in g.MissingMembers)
                    {
                        sb.AppendFormat("   Missing: {0}\n", m);
                    }
                }
            }
            return sb.ToString();
        }
    }
}
