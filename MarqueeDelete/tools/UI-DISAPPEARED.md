# 找出导致「UI 全部消失」的模组

## 症状与原理

`Player.log` / `Logs\UI.log` 中出现：

```
[ERROR]  JS Error: assetdb://gameui/index.js:1: Module GameTopLeft was not found.
```

**含义**：某个模组的 UI 模块调用了 `moduleRegistry.append("GameTopLeft", X)`，但第二个参数 `X` **不是函数**（常见于把 `React.memo(...)` 的返回值、`undefined`、字符串、数字直接传进去）。

游戏内注册表 `append` 的实现是：

```js
append(e, t, n, s) {
  const i = typeof t === "function";       // t 不是函数 => i = false
  const a = typeof n === "function";
  let o = i ? "game-ui/modding/modding-hook.tsx" : e;   // o = "GameTopLeft"
  let r = i ? "ModdingHook" : t;                        // r = 那个非函数对象
  this.extend(o, r, ...);                               // extend -> $.get("GameTopLeft") -> 抛异常
}
```

`extend` 内部第一行就是 `const s = $.get(e)`，而 `GameTopLeft` 是**挂载点（hook target）**，不是注册表里的模块路径，所以必定抛 `Module GameTopLeft was not found.`。

**后果**：该异常会中断整轮模组 UI 注册/渲染，于是**所有**模组的 HUD 按钮、面板、悬浮控件一起消失；而 C# 侧模组逻辑（含框选删除工具本身）仍然正常工作——这正是「工具能用、UI 全没了」的表现。

> 注意：这与游戏的「Options → Mods」设置页无关，设置页由 C# 的 `ModSetting` 提供，不受该 JS 异常影响。

## 使用方法

```powershell
# 1) 只读扫描：列出所有已装模组里“危险”的 append 调用
pwsh -NoProfile -ExecutionPolicy Bypass -File .\tools\Diagnose-UiHookError.ps1

# 2) 二分定位：生成一半启用的播放集建议（手动在游戏里切换）
pwsh -NoProfile -ExecutionPolicy Bypass -File .\tools\Diagnose-UiHookError.ps1 -Bisect
```

脚本只读文件，不修改任何模组或存档。

## 手动验证步骤

1. 关闭游戏。
2. 把 `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\.cache\Mods\pdx_mods`
   下的可疑目录**改名**（例如 `87190_43` → `87190_43.off`），或在 Paradox Mods 播放集里只保留一半模组。
3. 启动游戏，看 `Logs\UI.log` 是否还出现 `Module GameTopLeft was not found.`。
4. 仍然出现 → 元凶在保留的那一半里；消失 → 在关掉的那一半里。反复二分直到只剩一个。
5. 找到后：把该模组更新到最新版，或暂时停用。

## 已知的高风险写法（供对照）

| 写法 | 结果 |
| :--- | :--- |
| `append("GameTopLeft", MyMemoComponent)`（`MyMemoComponent = React.memo(...)`） | ❌ 抛异常 |
| `append("GameTopLeft", undefined)`（导入失败 / 重命名后变量为空） | ❌ 抛异常 |
| `append("GameTopLeft", () => <MyComponent/>)` | ✅ 正常 |
| `append("GameTopLeft", MyFunctionComponent)` | ✅ 正常 |
| `extend("game-ui/.../xxx.tsx", "ExportName", cb)` | ✅ 正常（注意第一个参数必须是**模块路径**，不是 `GameTopLeft`） |

## MarqueeDelete 的处理

本模组 v2.1.0 起**完全不使用 `append("GameTopLeft", ...)`**：

- 浮动按钮与工具面板改为 `extend("game-ui/game/components/tool-options/mouse-tool-options/mouse-tool-options.tsx", "MouseToolOptions", ...)`，直接注入到**官方拆除工具的选项面板**里，与 Better Bulldozer 使用同一套稳定挂载点。
- 所有注册语句包在 `try/catch` 中，即使失败也不会向注册表抛异常，不会连累其他模组。
