# 版本号策略

## 两条版本线

| 用途 | 版本号 | 说明 |
|---|---|---|
| **本地开发/测试** | `2.7.x` | 装了本地 Mods 目录给自己用。含未发布的实验性改动，可能有 bug |
| **平台发布** | `2.6.x` | 上传到 Paradox Mods 的稳定版。2.6.1 已发布，下一版为 **2.6.2** |

**为什么分开**：本地版可以反复试错、装不稳定的改动；发布版必须是验证过的稳定状态。两者共用同一份源码，通过版本号区分。

## 当前状态

- 本地开发版：**2.7.7**（整理推土机面板，移除重复的地下模式按钮，修复弹窗重叠）
- 线上版：**2.6.1**（平台已发布）
- 下一发布版：**2.6.2**（等本地开发版验收通过后再由此源码产出）

## 发布流程

**不要直接改四处版本号。** 用脚本：

```powershell
# 1. 预演（不改任何文件）
pwsh -NoProfile -File .\release_publish.ps1 -DryRun

# 2. 构建并暂存到 publish/（不上传）
pwsh -NoProfile -File .\release_publish.ps1

# 3. 真正上传（需用户明确授权）
pwsh -NoProfile -File .\release_publish.ps1 -Upload
```

脚本会：
1. 把 4 处版本号（`Mod.cs`、`mod.json`、`ui/mod.json`、`PublishConfiguration.xml`）临时改为发布版本
2. 重建 UI 与 C# 产物
3. 跑测试与 Cecil 校验
4. 暂存到 `publish/`
5. **无论成功失败，finally 里都把版本号切回本地版并重建**

所以 `dist/` 永远是本地版，`publish/` 是发布版。

## 版本一致性检查

```powershell
pwsh -NoProfile -File .\tests\verify_version_sync.ps1
```

同时核对四处版本号，不一致直接失败。**发布前必跑。**

## 历史教训

Gemini 连续两轮只改 `mod.json` + `Mod.cs`，漏掉 `ui/mod.json` 和 `PublishConfiguration.xml`，导致「DLL 说 2.7.2、UI 包说 2.7.1」的错乱。所以加了自动检查脚本，不靠人记。
