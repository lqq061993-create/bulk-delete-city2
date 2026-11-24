# MarqueeDelete 2.7.8 代码审查与修改意见

- 审查日期：2026-11-18（2.7.7 初版）／2026-11-18 修订（2.7.8 复核）
- 审查对象：`lqq061993-create/city2models` 分支 `feat/road-asset-filters`，提交 `3937781`
- 上一版审查对象：`main` 的 `17dc471`（版本 2.7.7）
- 本地版本与已安装版本：均为 **2.7.8**
- 游戏目标：Cities: Skylines II `1.6.4f1`，Game.dll MVID `46198354D4F94D23BF327E3C085D8B92`

## 版本一致性核对结论

本地已安装的 DLL 与 `feat/road-asset-filters` 的 `3937781` 构建产物**是同一份代码**。

验证方式为逐字节比对，非版本号推断：

| 项 | 已安装 | 分支构建 | 结论 |
|---|---|---|---|
| 文件大小 | 354304 | 354304 | 相同 |
| 差异字节 | — | 170 字节 / 8 段 | 0.048% |
| 差异位置 | 偏移 136、332372、352272–352646 | 同左 | 全部位于 PE 头 / MVID / 时间戳区 |
| 含 `RoadFacilities` | 是 | 是 | 一致 |

PE 头与 MVID 每次编译都会重新生成，因此同源代码的两次构建哈希必然不同。字节比对才是版本一致的证据，**哈希不等不等于版本不同**。

---

## 一、总体结论

**2.7.8 可做本机有限测试，不放行发布。**

2.7.8 相对 2.7.7 只做了一项新功能（路口设施独立移除），**上一版审查发现的阻断问题一个都没有修**。逐条复核结果如下。

### 2.7.7 → 2.7.8 问题复核

| 编号 | 问题 | 2.7.8 状态 |
|---|---|---|
| 2.1 | 道路撤回不比较邻边身份（损坏存档） | **未修** |
| 2.2 | 发布脚本不检查退出码 | **未修**（`release_publish.ps1` 内 `LASTEXITCODE` 出现次数：0） |
| 3.1 | 类型名检查三重失效 | **未修**（`run_tests.ps1` 仍未引用；缺 Game.dll 仍 `exit 0`） |
| 3.3 | `UIAccess` 组无消费点 | **未修** |
| 3.4 | `SoloFilterPolicy` 死代码 | **未修**（仍只有定义处 1 处，无调用） |
| 2.2附 | `verification.json` 与 DLL 不一致 | **已改善**（安装目录记录 `89DE2BE4…` 与实际一致；但工作区 `dist` 的旧记录仍需重建后同步） |

**结论：新一轮审查没有让风险下降，只是多了一个新功能。**

---

## 二、阻断级问题（沿用，均未修）

### 2.1 道路撤回不比较邻边身份，可能把道路接到错误的路口

**会损坏玩家存档，且不会报错。**

`DeletionPolicies.cs:360` 用相邻边**数量**判定：

```csharp
if (recordedAdjacentEdges >= 0 && actualAdjacentEdges >= recordedAdjacentEdges)
{
    return NetLinkStatus.ForeignSplice;
}
return NetLinkStatus.Reconnected;
```

**放行场景**：捕获时路口有 5 条边 → 删除后剩 0 条 → 玩家在该路口接 2 条新路（邻边数 2）→ `2 >= 5` 不成立 → 判定 `Reconnected` → 放行重建。

重建时 `DeletionUndo.cs:1349` 会把仍存在的节点实体直接放进 `NetCourse`，结果是**新恢复的道路接到了玩家后来新建的路口上**。

**测试为何没拦住**：`tests/DeletionRegression.cs:823` 只覆盖"少一条通过、相同或更多拒绝"，即只测数量维度，从未构造"数量相同、身份不同"。

**修复要求（从严：认不出就拒绝）**

1. `UndoNetRecord`（`DeletionUndo.cs:70`）新增：
   ```csharp
   /// <summary>Entity identities (index+version) of edges meeting the start node at capture.</summary>
   public Entity[] StartLinkIds;
   /// <summary>Entity identities (index+version) of edges meeting the end node at capture.</summary>
   public Entity[] EndLinkIds;
   ```
   保留现有 `StartLinks` / `EndLinks` 计数用于快速预筛。

2. 捕获时读取 `NetNode` 的连接边缓冲，连同 `Version` 一起存。**缓冲读不到时存 `null` 而非空数组**——用 `null` 表示"未能记录"，与"确实零条邻边"区分开。这一条与 `LaneCount` 初始化为 `0` 导致安全路径永不触发的教训相同。

3. `EndpointIntact`（`DeletionUndo.cs:832`）三段判定：
   ```csharp
   if (linkIds == null) return false;              // 未能记录 -> 拒绝
   // 记录中每条边必须仍存在且 Version 未变
   // 不得出现未记录的新边
   ```
   第三条是本次关键新增：现有逻辑只查"够不够"，不查"是不是原来那些"。

4. `NetConnectivityPolicy.EvaluateEndpoint` 增加接收身份数组的重载。

5. 跳过计数复用已有链路，不新建 binding：
   - `snap.SkippedCount++`（`DeletionUndo.cs:560`）
   - `undo-restore` 审计行（`DeletionUndo.cs:1017`）追加 `NetIdentityRejected: N`
   - 前端 `undoSkippedCount` / `undoFinishedSkippedCount`（`MarqueeDeleteUISystem.cs:320,323`）已存在

6. 补测试：邻边数量相同但身份不同 → 断言**拒绝**；`StartLinkIds == null` → 断言**拒绝**。

### 2.2 发布脚本不检查退出码，失败后仍会暂存甚至上传

`release_publish.ps1:77-90`：所有外部命令输出经管道接走，**全脚本无一处检查 `$LASTEXITCODE`**（已复核：出现 0 次），`$staged = $true` 无条件执行。

**必须知道的细节**（实测）：

```powershell
$out = & cmd /c "exit 17" 2>&1 | Select-Object -Last 1
"LASTEXITCODE=$LASTEXITCODE"   # 17
"pipeline-success=$?"           # True   <-- 管道后 $? 失真
```

**只检查 `$?` 等于没修。**

**修复要求**

1. 统一辅助函数，避免逐处遗漏：
   ```powershell
   function Invoke-Step {
       param([string]$Label, [scriptblock]$Action)
       & $Action
       if ($LASTEXITCODE -ne 0) { throw "$Label failed with exit code $LASTEXITCODE" }
   }
   ```
2. 暂存前校验：DLL 哈希、UI 产物、`mod.json` 版本三者必须对应本次构建
3. 只上传本次校验过的产物，不复用 `publish/` 残留
4. 补一条"故意失败"的回归：构建或测试返回非零时，断言不进入暂存/上传

---

## 三、2.7.8 新功能审查：路口设施独立移除

### 3.1 功能性质（先说清楚，与删除不同）

`RoadFacilities.cs` 实现的是**移除路口的交通信号灯或人行横道**。

关键区别：**它不删除任何实体，也不进入撤销历史。**

`TrySubmit`（`RoadFacilities.cs:204`）的动作是修改实体的 `Upgraded` 组件：

```csharp
upgrade.m_Flags.m_General = (CompositionFlags.General)RoadFacilityPolicy.WithRemovalOverride(
    (uint)upgrade.m_Flags.m_General, (uint)CompositionFlags.General.TrafficLights,
    (uint)CompositionFlags.General.RemoveTrafficLights);
...
if (hadUpgrade) buffer.SetComponent(entity, upgrade);
else buffer.AddComponent(entity, upgrade);
```

全项目检索确认：`FacilityTargets` / `FacilityMode` 在 `DeletionUndo.cs` 中**零出现**，即不参与撤销。

**这一点是好的设计**：改组件比删实体安全得多，出问题也能靠游戏自身的升级系统恢复。

### 3.2 新发现：路口设施操作完全没有审计记录

删除流程每一步都写审计行（`MarqueeDeleteToolSystem.cs:1607, 1658, 1683, 1989, 2000, 2206`），而**路口设施提交写了 0 条**。

`AdvanceRoadFacilities`（`RoadFacilities.cs:329`）完成后只设置 `SelectionNotice`，不调用 `AppendStatusLine`。

**后果**：玩家报告"我的信号灯没了/横道没了"时，日志里查不到任何记录，无法定位是哪次操作、改了多少个路口。这与删除操作的可审计性不对等。

**修复**：在 `AdvanceRoadFacilities` 结束时追加一条，格式与删除保持一致：

```
[State: facility-result, Mode: TrafficLights|Crosswalks, Submitted: N, Skipped: M, Targets: T]
```

### 3.3 新发现：批次中途失败时会静默丢弃剩余目标

`AdvanceRoadFacilities`（`RoadFacilities.cs:332-348`）：

```csharp
bool stopped = Keyboard.current?.escapeKey.wasPressedThisFrame == true || !CanSubmitDeletionNow;
if (!stopped) { /* 提交本批 */ }
m_FacilityBatch = null;
SelectionNotice = $"facility_result|{m_FacilitySubmitted}|{batch.Count - m_FacilitySubmitted}";
```

按 Esc 或兼容等级变化时，**剩余目标直接丢弃**，只报一个"未提交数量"。没有区分：

- 玩家主动取消（Esc）
- 兼容门闩拒绝（`!CanSubmitDeletionNow`）

这两种在中途发生时都是同一句 `facility_result`，玩家看不出**是系统拒绝还是自己按了取消**。

**这与用户明确要求冲突**："任何被拒绝或无效的操作都要在界面上给出可见提示，不能静默无反应。"

**修复**：区分两种终止原因，分别给出提示。兼容被拒时用已有的 `deletion_blocked_compat` 语义。

### 3.4 新发现：测试只覆盖纯策略函数

新增测试（`tests/MarqueeDelete.Tests.cs`）覆盖的是 `RoadFacilityPolicy.CrosswalkSlot`、`WithRemovalOverride`、`IsIndependentCrosswalk` 三个**纯函数**。

**未覆盖**：

- `TryResolve` 的真实实体解析（需要 `Node`、`Composition`、`NetCompositionData` 等真组件）
- `TrySubmit` 的组件写入效果
- `Revalidate` 在目标失效时的处理
- `AdvanceRoadFacilities` 的批处理与中断

与 3.4（`SoloFilterPolicy`）同属一类盲区：**测了纯逻辑，没测接线**。

### 3.5 观察项：`MarkJunctionUpdated` 会标记整个路口的边

`RoadFacilities.cs:242-248`：

```csharp
private static void MarkJunctionUpdated(EntityManager em, EntityCommandBuffer buffer, Entity node, HashSet<Entity> updated)
{
    if (node == Entity.Null) return;
    MarkUpdated(em, buffer, node, updated);
    foreach (var connection in em.GetBuffer<ConnectedEdge>(node, true))
        MarkUpdated(em, buffer, connection.m_Edge, updated);
}
```

移除一个横道，会给**该路口连接的所有边**加上 `Updated` 组件。这可能是必要的（让游戏重算几何），但会波及未被选中的道路。

**未验证**：这会不会导致未选中的道路也重算几何、改变外观或影响性能。建议在游戏内确认。

---

## 四、重要问题（沿用，均未修）

### 4.1 类型名检查脚本三重失效

`tests/verify_compat_typenames.ps1` 的三个问题（2.7.8 复核均仍在）：

1. **未接入任何构建或测试入口**（`run_tests.ps1` 中引用次数：0）
2. **找不到 Game.dll 时 `exit 0` 并报成功**（实测：`checked 0 type names, missing 0` / `OK: all exist` / `exit=0`）
3. **大小写不敏感 + 不递归嵌套类型**（PowerShell 哈希表默认大小写不敏感，已实测；脚本无 `NestedTypes` 递归）

**修复**：缺 DLL 时 `exit 1`；提取到 0 个类型名视为失败；改用 `Ordinal` 比较；递归 `NestedTypes`；接入 `run_tests.ps1`。

### 4.2 兼容层只验证"类型存在"

`Compatibility.cs:292` 的 `CheckType` 仅做 `Type.GetType` / `Assembly.GetType` 解析。类型存在不代表构造签名、成员、绑定注册在运行时可用。

建议报告区分三个层次：符号存在 / 启动解析成功 / 行为已验证。

### 4.3 `UIAccess` 组算了却没有消费点

`Compatibility.cs:124` 计算 `UIAccess` tier 并写进报告，但全项目无读取点。`CanRun` 只看 `SearchAndPick`。

### 4.4 `SoloFilterPolicy` 是死代码

`SelectionLogic.cs:3782` 定义，**全项目无调用点**。生产 `OnSoloFilter`（`MarqueeDeleteUISystem.cs:1045`）手写了一遍逻辑。

额外风险：`OnSoloFilter` 遇到未知非空 key 会先清空全部分类再保存。

### 4.5 撤销历史把"已提交"当成"已删除"

`MarqueeDeleteToolSystem.cs:2155` 无条件 `CommitDeletionCapture()`，不区分"确认删掉"与"只是提交了"。删除若延迟或未生效，撤销历史仍记着，撤回时会向仍存在的实体发重建请求。

**未在游戏内实证是否产生重复对象。**

### 4.6 同源测试不能证明生产状态机完整

`tests/DeletionRegression.cs:710-751` 部分检查通过源码文本匹配确认调用位置，证明不了运行时行为。

---

## 五、检查手段的系统性盲区

**所有已发生的失效都落在"编译期不检查、测试不覆盖"的字符串与运行时边界上。**

| 盲区 | 表现 | 2.7.6 是否命中 | 2.7.8 是否仍存在 |
|---|---|---|---|
| 反射类型名字符串 | `CheckType("...")` 写错编译不管 | **是** | 检查手段本身不可靠（见 4.1） |
| 前后端 binding 名 | 跨语言字符串，无编译校验 | 否 | 无自动比对机制 |
| 本地化 key | 同上 | 否 | 无自动比对机制 |
| 脚本退出码 | `$?` 与 `$LASTEXITCODE` 语义不同 | 否 | **是**（见 2.2） |
| 生产与测试不同源 | 死代码导致"测 A 跑 B" | 否 | **是**（见 4.4、3.4） |
| 审计覆盖不对等 | 有的操作写日志、有的不写 | 否 | **是**（见 3.2） |

**本轮人工比对（无脚本支撑）**

| 项目 | 数量 | 状态 |
|---|---|---|
| 后端 Value 绑定 | 116 | 与前端一致 |
| 后端 Trigger 绑定 | 32 | 前端使用 31 |
| 前端值绑定 | 115 | 与后端一致 |
| 前端触发器 | 31 | 与后端一致 |
| 本地化 key 定义 | 263+ | — |
| ActionName | 30 | 无重复 |

无害不对称：`compatMessage`（后端推、前端未订阅）、`toggleUnderground`（后端注册、前端未调用）。

**建议**：把上述比对做成 `tests/verify_bindings.ps1`、`tests/verify_locale_keys.ps1`，接入测试入口。成本极低、收益极大——正是能挡住 2.7.6 那类事故的手段。

---

## 六、建议级问题

### 6.1 `InterruptedDeletion` 统计最坏平方复杂度

`DeletionPolicies.cs:157-177` 对每个剩余目标回扫已出现单元。建议改用 `HashSet` 一次统计。

### 6.2 大选区与级联缺针对性验收

`Cascade.cs:294-376` 会把结构影响集合中的子实体直接纳入删除。离线替身证明不了游戏里共享端点与归属语义。

建议验收场景（同一备份存档）：建筑升级 → 所属子网 → 外部邻路 → 暂停 → 分类撤销 → 保存重载，核对无重复、无孤立子网、邻路可用。

### 6.3 车道证据对"无 Owner"采取宽松策略

`DeletionUndo.cs:976` 缺失或失效 lane 会跳过；`DeletionPolicies.cs:435` 对无外部 Owner 的 lane 返回允许。

这是明确降级取舍（否则所有道路撤回都会被拒），不是证明。修复 2.1 后需重新评估。

### 6.4 工作区残留

`NoSuchWrite.tmp`（0 字节，已被 Git 忽略），交付前清理。

---

## 七、分支管理提醒

`feat/road-asset-filters` 相对 `main` **删除了以下文档**：

- `MarqueeDelete/docs/2026-11-18-mod-review.md`（本文档的 2.7.7 版本）
- `MarqueeDelete/交接说明.md`
- `MarqueeDelete/发布指南.md`
- `MarqueeDelete/崩溃排查-20261114.md`
- `MarqueeDelete/Properties/COPY-2.3.1.md`

**合并前必须决定**：保留这些文档，还是接受删除。若直接合并，main 上的文档会一并消失。

---

## 八、内部标识符核对

**结论：均符合约定，未被改动。**

| 项目 | 值 | 状态 |
|---|---|---|
| 命名空间 | `MarqueeDelete` | 未改 |
| mod id | `MarqueeDelete` | 未改 |
| ModId | `167502` | 未改 |
| ToolID | `"Marquee Delete"` | 未改 |
| 本地化前缀 | `MarqueeDelete.` | 未改 |
| 显示名 | Bulk Delete · 批量删除 | 未改 |
| 版本号 | 2.7.8（四处一致） | 已升级 |

---

## 九、修改优先级

### 第一批（发布前必须）

| 序 | 项 | 章节 |
|---|---|---|
| 1 | 道路撤回邻边身份校验 | 2.1 |
| 2 | 发布脚本退出码检查 + 产物一致性 | 2.2 |
| 3 | 类型名检查修复并接入测试入口 | 4.1 |

### 第二批（发布前建议）

| 序 | 项 | 章节 |
|---|---|---|
| 4 | 路口设施审计日志 | 3.2 |
| 5 | 路口设施中途终止区分原因并提示 | 3.3 |
| 6 | `SoloFilterPolicy` 死代码消除 | 4.4 |
| 7 | `UIAccess` 组明确用途 | 4.3 |

### 第三批（发布后）

| 序 | 项 | 章节 |
|---|---|---|
| 8 | binding 与本地化 key 自动比对脚本 | 五 |
| 9 | 兼容层三层次报告 | 4.2 |
| 10 | 撤销历史三档状态 | 4.5 |
| 11 | `InterruptedDeletion` 复杂度优化 | 6.1 |
| 12 | 路口设施接线测试（`TrySubmit`/`Revalidate`/批处理） | 3.4 |
| 13 | 大选区与级联针对性验收 | 6.2 |

### 发布前验收清单

- [ ] 重新冻结构建，`verification.json` 的 `ModHash` 与实际 DLL 一致
- [ ] 完整复跑：C# 构建 0 警告、离线单测、删除回归、TypeScript、webpack、Cecil、类型名
- [ ] 新增回归：邻边身份不同 → 拒绝
- [ ] 新增回归：构建或测试失败 → 不进入暂存/上传
- [ ] 游戏内：工具按钮与面板出现、快捷键可用
- [ ] 游戏内：删除 → 撤回，结构正确，无重复、无孤立子网
- [ ] 游戏内：路口设施移除后，信号灯/横道确实消失且相邻路口未受影响
- [ ] 游戏内：路口设施操作中途按 Esc，界面给出明确提示
- [ ] 备份存档，保存重载后无异常

---

## 附：本轮审查的局限

1. **未做游戏内验收**。所有结论基于源码与离线测试。
2. **未验证 2.1 的实际损坏频率**。机制路径确定存在，实际触发条件需游戏内构造。
3. **未验证路口设施在真实路口数据下的行为**。`MarkJunctionUpdated` 波及未选中道路的影响未测（见 3.5）。
4. **4.5 的"重复对象"未实证**。
5. **无旧版源码基线**，无法判断标识符是否被历史改动。
6. **未重新跑完整测试套件**。本轮结论基于源码静态复核与针对性实跑；引用自上一轮的测试数字（单测 2806、回归 201）对应 2.7.7，2.7.8 的新增测试未计入。

### 本轮实测确认的结论（可复现）

- `release_publish.ps1` 内 `LASTEXITCODE` 出现次数为 0
- `run_tests.ps1` 未引用类型名检查
- 类型名脚本缺 Game.dll 时 `exit 0` 且打印成功
- PowerShell 哈希表大小写不敏感
- 管道后 `$?` 为 True 而 `$LASTEXITCODE` 保留真实值
- 已安装 DLL 与分支构建差异仅 170 字节，位于 PE 头/MVID 区
- `SoloFilterPolicy` 无调用点
- `FacilityTargets` 在 `DeletionUndo.cs` 中零出现
- 路口设施提交无 `AppendStatusLine` 调用
