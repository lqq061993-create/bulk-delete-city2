# MarqueeDelete 2.7.7 代码审查与修改意见

- 审查日期：2026-11-18
- 审查对象：仓库 `lqq061993-create/city2models` 提交 `17dc471` 中的 `MarqueeDelete/`
- 本地版本：2.7.7（上传版 2.6.2 待发）
- 游戏目标：Cities: Skylines II `1.6.4f1`，Game.dll MVID `46198354D4F94D23BF327E3C085D8B92`
- 审查来源：防御龙（GPT-6.1-Sol）、GPTb（gpt-6.1-sol）两份独立审查，加 Chief 独立复核
- 当前产物：`dist/MarqueeDelete.dll` SHA256 `40FE051BFEEB50333C06D18282F997E5530CB489793A73CCD8AE0CE9494D2050`

---

## 一、总体结论

**2.7.7 可以在本机做有限度的游戏内测试，但不放行发布。**

主要理由：存在一个**会造成存档损坏**的阻断问题（道路撤回可能接到错误的路口），以及一个**会让坏包被上传**的发布流程缺陷。两者都不是"理论上可能"，而是代码里能指到具体行的确定路径。

同时要纠正一个认知：**"单位测试 2806 全过 + 回归 201 全过 + Cecil 解析 0 未解析"不能作为可用性证明。** 2.7.6 的阻断级故障（`Game.Common.Attached` 命名空间写错导致整套 UI 消失）正是在这组全绿数字下发生的。本文档第 4 节列出了这类检查手段的系统性盲区。

复跑数字（本轮独立构建）：

| 项目 | 结果 |
|---|---|
| C# 构建 | 通过，0 警告 / 0 错误 |
| 离线单测 | 2806 通过 / 0 失败 |
| 删除回归 | 201 通过 |
| TypeScript / webpack | 通过（1 条 Sass 弃用警告） |
| Cecil 解析 | 859 成员 / 317 类型，未解析 0 |
| 类型名核对 | 22 个，缺失 0（但见 3.3：该检查本身不可靠） |
| 四处版本号 | 均为 2.7.7 |

---

## 二、阻断级问题

### 2.1 道路撤回不比较邻边身份，可能把道路接到错误的路口

**这是本次最重要的问题：它会损坏玩家存档，而且不会报错。**

**判定逻辑的缺陷**

`DeletionPolicies.cs:360`：

```csharp
if (recordedAdjacentEdges >= 0 && actualAdjacentEdges >= recordedAdjacentEdges)
{
    return NetLinkStatus.ForeignSplice;
}
return NetLinkStatus.Reconnected;
```

判定用的是**相邻边的数量比较**：当前邻边数 ≥ 记录值就判定为"别人接进来了，拒绝"；否则判定为"自己接回去了，放行"。

**为什么这个判定会放行错误情况**

设捕获时某路口连接 5 条边。玩家删除了这批道路，旧路口上现在只剩下 0 条或少量的边。之后玩家又在这个路口附近接了 2 条新路，此时邻边数为 2。

`2 >= 5` 不成立 → 返回 `Reconnected` → **放行重建**。

重建时 `DeletionUndo.cs:1349` 会把**仍然存在的那个节点实体**直接放进 `NetCourse`。结果就是：新恢复的道路接到了玩家后来新建的路口上，几何形状与拓扑都错，玩家看到的是"路回来了但接错地方"。

**为什么测试没拦住**

`tests/DeletionRegression.cs:823` 附近的用例覆盖了"少一条通过、相同或更多拒绝"，即只测了**数量**维度。测试里从来没有构造过"邻边数量对了但身份不同"的场景。

这是典型的"测了能测的，没测该测的"。

**修复要求（从严策略，已确认方向）**

原则：**认不出就拒绝**。宁可少恢复一段，也不能接错。

1. `UndoNetRecord`（`DeletionUndo.cs:70`）新增两个字段，保存捕获时每个邻边的实体身份：
   ```csharp
   /// <summary>Entity identities (index+version) of edges meeting the start node at capture.</summary>
   public Entity[] StartLinkIds;
   /// <summary>Entity identities (index+version) of edges meeting the end node at capture.</summary>
   public Entity[] EndLinkIds;
   ```
   注意：现有 `StartLinks` / `EndLinks` 是 `int` 计数，保留不动，用于快速预筛；身份数组是精确判定依据。

2. 捕获时读取 `NetNode` 的连接边缓冲，连同 `Version` 一起存。若缓冲读不到（实体失效、缓冲为空等），存 `null` 而不是空数组——**用 `null` 表示"未能记录"，与"确实零条邻边"区分开**。这一点与之前 `LaneCount` 初始化为 `0` 导致安全路径永不触发的教训相同。

3. `EndpointIntact`（`DeletionUndo.cs:832`）改为三段判定：
   ```csharp
   // 1) 未记录身份 -> 无法证明安全 -> 拒绝（从严）
   if (linkIds == null) return false;
   // 2) 逐条比对：记录中的每条边必须仍存在且 Version 未变
   // 3) 不得出现未记录的新边（避免把道路接到玩家后建的路上）
   ```
   第 3 条是本次的关键新增：现有逻辑只检查"够不够"而不检查"是不是原来那些"。

4. `NetConnectivityPolicy.EvaluateEndpoint` 增加一个接收身份数组的重载，保留原重载供位置判定的回退路径使用。

5. 跳过计数接入**已有的**上报链路（不新建）：
   - 走 `snap.SkippedCount++`（`DeletionUndo.cs:560`）
   - `undo-restore` 审计行（`DeletionUndo.cs:1017`）追加 `NetIdentityRejected: N`
   - 前端已有的 `undoSkippedCount` / `undoFinishedSkippedCount` 绑定（`MarqueeDeleteUISystem.cs:320,323`）直接复用，不需要新增 binding

6. 补测试：构造"邻边数量相同但身份不同"的场景，断言**被拒绝**。这是当前测试完全缺失的维度。

**验收标准**

- 新增回归用例：邻边数相同、身份不同 → `SkippedCount` 增加，无重建请求发出
- 新增回归用例：`StartLinkIds == null` → 拒绝
- 游戏内：备份存档，删除一段路 → 在路口另接新路 → 撤回，确认恢复的路**不接**到新路上，且界面显示跳过提示

---

### 2.2 发布脚本不检查退出码，失败后仍会暂存甚至上传坏包

`release_publish.ps1:77-90`：

```powershell
& npm run build 2>&1 | Select-Object -Last 3
Pop-Location
& pwsh -NoProfile -File (Join-Path $root 'build.ps1') 2>&1 | Select-Object -Last 2
# ...测试、Cecil 校验同样写法...
& pwsh -NoProfile -File (Join-Path $root 'publish.ps1') -Mode NewVersion -DryRun 2>&1 | Select-Object -Last 6
$staged = $true      # 无条件
```

**问题**：所有外部命令的输出都被管道接走，**没有任何一处检查 `$LASTEXITCODE`**。`$staged = $true` 是无条件执行的。加了 `-Upload` 就会进入上传分支。

**一个必须知道的细节**（我实测确认）：

```powershell
$out = & cmd /c "exit 17" 2>&1 | Select-Object -Last 1
"LASTEXITCODE=$LASTEXITCODE"   # 17
"pipeline-success=$?"           # True   <-- 注意
```

**管道之后 `$?` 是 `True`，只有 `$LASTEXITCODE` 保留了真实退出码。** 所以修复时如果只检查 `$?`，等于没修。这个坑很容易踩。

**修复要求**

1. 每个外部命令后立即检查 `$LASTEXITCODE`，非零则 `throw`
2. 用统一的辅助函数包住，避免逐处遗漏：
   ```powershell
   function Invoke-Step {
       param([string]$Label, [scriptblock]$Action)
       & $Action
       if ($LASTEXITCODE -ne 0) { throw "$Label failed with exit code $LASTEXITCODE" }
   }
   ```
3. 暂存前校验一致性：DLL 哈希、UI 产物时间戳与内容、`mod.json` 版本，三者必须与本次构建对应
4. 只允许上传**本次校验过的这一份**产物，不得复用 `publish/` 里的历史残留
5. 补一条"故意失败"的回归：让构建或测试返回非零，断言脚本不会走到暂存/上传

**当前已存在的不一致（必须一起处理）**

| 项 | 值 |
|---|---|
| `dist/MarqueeDelete.dll` 实际 SHA256 | `40FE051BFEEB50333C06D18282F997E5530CB489793A73CCD8AE0CE9494D2050` |
| `verification.json` 记录的 `ModHash` | `ABB57F9FC58CC992D326951599759A5963D746B7701D825A9FB8E7AA538E4893` |

**两者不一致**，说明 `verification.json` 是更早一次构建的产物。在重新冻结构建并更新记录之前，不得把它当作当前产物的证明。

---

## 三、重要问题

### 3.1 类型名检查脚本会假通过（三重失效）

`tests/verify_compat_typenames.ps1` 是为了防止 2.7.6 那次事故而写的，但它本身有三个问题，导致它**目前起不到保险作用**。

**失效一：没有接入任何构建或测试入口**

`build.ps1` 与 `tests/run_tests.ps1` **都不调用**它。不手动跑就不会执行。也就是说：同样的错误今天再犯一次，构建照样成功、测试照样全绿。

**失效二：找不到 Game.dll 时返回 0 并报告成功**

我把它指向不存在的路径实测：

```
checked 0 type names, missing 0
OK: all referenced game type names exist
exit=0
```

`scripts:8` 的 `if (-not (Test-Path $gameDll)) { Write-Host "SKIP: Game.dll not found"; exit 0 }` —— **检查不了却报告通过**。

**失效三：大小写不敏感 + 不递归嵌套类型**

- 脚本用 PowerShell 哈希表做索引（`$known[$ty.FullName] = $true`），而 **PowerShell 哈希表默认大小写不敏感**（已实测确认）。所以 `game.objects.attached` 这种错误写法会被放过——恰恰是 2.7.6 那类错误。
- 只扫 `$t.Types` 顶层类型，**没有递归 `NestedTypes`**，嵌套类型全部查不到。

**修复要求**

1. `Game.dll` 缺失时 `exit 1`（或在发布校验中把 SKIP 视为失败），不允许静默通过
2. 提取到 0 个类型名时视为失败（防止正则失配导致空跑通过）
3. 比较改为**大小写敏感**（`[System.StringComparer]::Ordinal` 或 `-cmatch`）
4. 递归收集 `NestedTypes`
5. 接入 `tests/run_tests.ps1`，让它在默认测试入口执行

### 3.2 兼容层只验证"类型存在"，证明不了"接线可用"

`Compatibility.cs:292` 的 `CheckType` 只做 `Type.GetType` / `Assembly.GetType` 解析。一个类型存在，不代表：

- 它的构造函数签名还是那样
- 它引用的成员、字段、方法还在
- 绑定注册在实际运行时能成功

`UIAccess` 组（`Compatibility.cs:260`）尤其能说明问题：它检查 `LocalizationManager`、`ValueBinding\`1`、`TriggerBinding`、`ProxyAction` 四个 CLR 类型，但**真正的 UI 注册发生在 `ui/src/index.tsx` 的前端私有注册点**，那部分完全没有被检查。

**修复建议**

在报告里明确区分三个层次，不要用一个 `Verified` 混为一谈：

| 层次 | 当前 | 应有 |
|---|---|---|
| 符号存在 | 有检查 | 有检查 |
| 启动解析成功 | 未验证 | 增加运行时探测（记录 Initialize 后实际解析到的成员） |
| 行为已验证 | 未验证 | 游戏内实机验收 |

### 3.3 `UIAccess` 组算了却没有任何消费点

`Compatibility.cs:124` 计算了 `UIAccess` 的 tier，写进报告。全项目搜索下来，**这个 tier 没有任何地方被读取**：

- `CanRun` 只看 `SearchAndPick`（`Compatibility.cs:57`）
- `CanSubmitDeletion` 看 `DeletionSubmit`
- `CanUndoRebuild` 看 `UndoRebuild`
- `IsPreviewOnly` 看后两组

**后果**：这 4 个 UI 类型真缺失时，模组判定"可以运行"，照样注册三个系统，然后在 UI 绑定时出问题。

要么让它参与判定，要么明确标注"仅诊断用途"——不能保持现状这种似是而非的状态。

### 3.4 `SoloFilterPolicy` 是死代码，测试测的与生产跑的不是同一份

- 定义：`SelectionLogic.cs:3782` 的 `public static class SoloFilterPolicy`
- **全项目无调用点**
- 生产处理器 `OnSoloFilter`（`MarqueeDeleteUISystem.cs:1045`）自己手写了 11 行分类清零 + switch

**后果**：测试覆盖的是 `SoloFilterPolicy`，生产运行的是 `OnSoloFilter` 里的手写逻辑。测 A 绿了证明不了 B 对。

防御龙另外指出一个具体风险：`OnSoloFilter` 遇到未知的非空 key 时，会**先清空全部分类再保存**——即传入未识别的 key 会把所有分类关掉。当前网络子项按钮走的是普通开关，不经过这里，所以玩家尚未触碰到；但这是一处潜在陷阱。

**修复**：让 `OnSoloFilter` 调用 `SoloFilterPolicy`，消除两份逻辑；并补测试"未知 key 不改变配置"。

### 3.5 撤销历史把"已提交"当成"已删除"

`MarqueeDeleteToolSystem.cs:2155`：

```csharp
m_UndoManager.CommitDeletionCapture();   // 无条件
```

`FinishDeletionBatches` 不区分"确认删掉了"还是"只是提交了删除请求"。删除若延迟或未生效（例如实体被其他模组保护、批量排队失败），撤销历史里仍然记着这条记录，撤回时会向一个**仍然存在**的实体发重建请求。

**风险**：可能产生重复对象。实际是否发生**尚未在游戏内复现验证**。

**修复建议**：把状态明确分三档——「未提交」「已提交待观察」「已确认删除」，只有第三档进入可撤销历史。同时重建前按 `Entity(Index, Version)` 再做一次存活检查。

### 3.6 同源测试不能证明生产状态机完整

`tests/DeletionRegression.cs:710-751` 的部分"检查"是通过**源码文本匹配**确认调用位置的，即读源码字符串断言"这里应该调用了 X"。这能防止误删调用，但证明不了运行时行为。

下游门闩测试通过手动设置兼容等级来验证策略（如 `MarqueeDeleteToolSystem.cs:2204` 的测试接缝），也不是完整的生产状态转换。

**建议补齐**：`PreviewOnly` 与 `Unavailable` 下，自动删除、确认删除、分批续交、撤回四条路径**均不产生写命令**；再加 `Verified` 正例证明放行路径确实可用。

---

## 四、检查手段的系统性盲区

这一节单独列出，因为它是 2.7.6 事故的根因，也是最容易重犯的地方。

**共同特征：所有失效都发生在"编译期不检查、测试不覆盖"的字符串与运行时边界上。**

| 盲区类型 | 具体表现 | 2.7.6 是否命中 |
|---|---|---|
| 反射类型名字符串 | `CheckType("...")` 写错，编译器不管 | **是**，`Game.Common.Attached` |
| 前后端 binding 名 | 字符串跨语言边界，无编译期校验 | 否（本轮比对一致，但无自动机制） |
| 本地化 key | 同上 | 否（本轮 158 个引用全部有定义） |
| 配置键 / ActionName | 存进存档的字符串，改了就断引用 | 否（本轮 30 个 ActionName 无重复） |
| 脚本退出码 | `$?` 与 `$LASTEXITCODE` 语义不同，管道后失真 | 否（本轮新发现） |
| 生产代码与测试策略不同源 | 死代码导致"测 A 跑 B" | 否（`SoloFilterPolicy` 本轮新发现） |

**本轮人工比对结果**（目前只靠人肉，没有脚本）

| 项目 | 数量 | 状态 |
|---|---|---|
| 后端 Value 绑定 | 116 | 与前端一致 |
| 后端 Trigger 绑定 | 32 | 前端使用 31 |
| 前端值绑定 | 115 | 与后端一致 |
| 前端触发器 | 31 | 与后端一致 |
| 本地化 key 定义 | 263 | — |
| 前端 `t()` 引用 | 158 | 全部有定义 |
| ActionName | 30 | 无重复 |

两处不对称（均无害，但应知晓）：
- `compatMessage`：后端推送，前端未订阅
- `toggleUnderground`：后端注册，前端未调用

**建议**：把上面的比对做成脚本（`tests/verify_bindings.ps1`、`tests/verify_locale_keys.ps1`），接入测试入口。这类检查成本极低、收益极大——正是能挡住 2.7.6 那类事故的手段。

---

## 五、建议级问题

### 5.1 `InterruptedDeletion` 统计最坏为平方复杂度

`DeletionPolicies.cs:157-177` 对每个剩余目标回扫已出现的单元。选区很大时是 O(n²)。建议改用 `HashSet` 一次统计。实际选区规模下影响有限，属于优化项。

### 5.2 大选区与级联缺针对性验收

`Cascade.cs:294-376` 会把结构影响集合中的子实体直接纳入删除。离线替身证明不了游戏里共享端点与归属语义的真实行为。

**建议的验收场景**（用同一个备份存档）：
建筑升级 → 所属子网 → 外部邻路 → 暂停 → 分类撤销 → 保存重载。核对：无重复对象、无孤立子网、邻路仍可用。

### 5.3 车道证据对"无 Owner"采取宽松策略

`DeletionUndo.cs:976` 遇到缺失或失效的 lane 会跳过；`DeletionPolicies.cs:435` 对没有记录外部 Owner 的 lane 返回允许。

这是**明确的降级取舍**（否则所有道路撤回都会被拒），不是证明。结合 2.1 的邻边身份问题，这个门闩整体强度仍然不足。修复 2.1 后需要重新评估这一条。

### 5.4 工作区残留文件

存在一个被 Git 忽略的 0 字节临时文件 `NoSuchWrite.tmp`。不是模组源码，但交付前应清理。

---

## 六、内部标识符核对

**结论：均符合约定，未被改动。**

| 项目 | 值 | 状态 |
|---|---|---|
| 命名空间 | `MarqueeDelete` | 未改 |
| mod id（`mod.json`） | `MarqueeDelete` | 未改 |
| ModId | `167502` | 未改 |
| ToolID | `"Marquee Delete"` | 未改 |
| 本地化前缀 | `MarqueeDelete.` | 未改 |
| ActionName | 30 个，无重复 | 未改 |
| 显示名 | Bulk Delete · 批量删除 | 未改 |

注：没有旧版完整源码基线，无法声称**逐项历史一致**，只能确认当前值符合约定且内部自洽。

---

## 七、修改优先级与验收

### 第一批（发布前必须完成）

| 序 | 项 | 依赖 |
|---|---|---|
| 1 | 道路撤回邻边身份校验（章节 2.1） | 无 |
| 2 | 发布脚本退出码检查 + 产物一致性（章节 2.2） | 无 |
| 3 | 类型名检查脚本修复并接入测试入口（章节 3.1） | 无 |

### 第二批（发布前建议完成）

| 序 | 项 | 依赖 |
|---|---|---|
| 4 | `SoloFilterPolicy` 死代码消除（章节 3.4） | 无 |
| 5 | `UIAccess` 组明确用途（章节 3.3） | 无 |
| 6 | 撤销历史三档状态（章节 3.5） | 需游戏内复现验证 |

### 第三批（发布后可做）

| 序 | 项 |
|---|---|
| 7 | binding 与本地化 key 自动比对脚本（章节 4） |
| 8 | 兼容层三层次报告（章节 3.2） |
| 9 | `InterruptedDeletion` 复杂度优化（章节 5.1） |
| 10 | 大选区与级联的针对性验收（章节 5.2） |

### 发布前验收清单

- [ ] 重新冻结构建，`verification.json` 的 `ModHash` 与实际 DLL 一致
- [ ] 完整复跑：C# 构建 0 警告、离线单测、删除回归、TypeScript、webpack、Cecil、类型名
- [ ] 新增回归：邻边身份不同 → 拒绝
- [ ] 新增回归：构建或测试失败 → 不进入暂存/上传
- [ ] 游戏内：工具按钮与面板出现、快捷键可用
- [ ] 游戏内：删除 → 撤回，结构正确，无重复、无孤立子网
- [ ] 游戏内：降级场景（若可构造）下不发写命令
- [ ] 备份存档，保存重载后无异常

---

## 附：本轮审查的局限

为避免把"没验证"当成"已通过"，明确列出：

1. **未做游戏内验收**。所有结论基于源码与离线测试，运行时行为未在游戏里实测。
2. **未验证 2.1 的实际损坏发生频率**。机制路径确实存在，但实际能触发多少次、什么条件下触发，需要游戏内构造。
3. **未验证新旧邻边身份三维比对在实际数据下的表现**。游戏网络实体的邻边缓冲在交叉口、地下网络、多车道场景下的语义稳定性，本轮未测。
4. **3.5 的"重复对象"未实证**。机制上有可能，实际是否发生未复现。
5. **没有旧版源码基线**，无法判断标识符是否被历史改动。
6. 本文档中所有"实测确认"的结论（PowerShell 大小写不敏感、管道后 `$LASTEXITCODE` 语义、类型名脚本假通过、DLL 哈希不一致）均为本机实跑结果，可复现。
