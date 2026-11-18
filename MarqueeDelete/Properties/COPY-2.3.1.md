# Marquee Delete 2.3.1 商店文案（中英分离版）

用于 `Properties/PublishConfiguration.xml` 的 `<ShortDescription>` / `<LongDescription>`。
`mod.json` 的 `description` 只放英文版（平台不支持该字段多语言）。

---

## English — Short (one paragraph, for `mod.json` and ShortDescription)

Drag a box to bulk-delete buildings, roads, underground networks, painted surfaces, trees, props, decals, and vehicles in a single sweep. Work cleanly across ground and underground layers, see color-coded lot outlines, preview cascade impact, and keep outside connections and landmark buildings safe before anything is removed.

**Selective step-by-step undo.** Choose any mix of six static asset types — buildings (with their upgrades and attached lots), roads, surfaces, trees, props, and decals — and the mod rebuilds them in frame-paced batches across up to 5 stored steps of 8,000 records each. Undo restores geometry and basic properties only; households, economy, and simulation state are not reproduced, and vehicles and citizens cannot be undone.

---

## English — Long (for `LongDescription`)

### What it does

Hold the left mouse button and drag a rectangle over anything you want gone. Marquee Delete bulk-removes buildings, roads, underground networks, painted surfaces, trees, props, decals, and vehicles in one pass — no more clicking through a district one lot at a time.

### Pick exactly what gets removed

- **Asset filters** — roads and networks (roads, bridges, tunnels, tracks, pipes, power lines), trees, props, decals, surfaces (districts and map tiles are protected automatically), and vehicles or citizens. One click selects or resets everything.
- **Zone filters for buildings** — residential, commercial, industrial, office, and public or other. Selection outlines follow each building's real orientation and stay readable even on very large drags, with live lot dimensions in both cells and meters.
- **Ground and underground isolation** — the vanilla underground toggle splits surface buildings from tunnels and pipes, so reshaping the streets never takes your buried infrastructure with it.

### Safety before anything is removed

- **Delete confirmation** (on by default) — releasing the mouse locks the selection and totals up everything affected, cascade included. Nothing happens until you press Enter or click confirm.
- **Landmark warning** — selections containing service buildings, unique buildings, or major landmarks trigger a second confirmation, listing what was found (the first 10 are named, with a count for the rest).
- **Outside connection protection** (on by default) — highway, rail, water, and power connections at the map edge are skipped automatically.
- **Camera-aligned or world-aligned box** — match the box to your view, or keep it fixed to world north.

### Selective undo

Undo no longer has to be all-or-nothing. Pick the asset types you actually want back and the mod rebuilds only those.

- **Six undo categories** — buildings (their upgrades, sub-lots, and internal links always travel with them), roads, surfaces, trees, props, and decals. Each control shows how many records of that type the current snapshot holds, so you can see the trade-off before you commit.
- **Frame-paced rebuild** — reconstruction requests are submitted in steps sized to the current frame budget, with parent-entity lookups cached to avoid repeated scans. Up to 5 steps are kept, 8,000 records per step.
- **Pause and resume**, and new marquee deletions are blocked while an undo is running or paused.

### What undo actually does

Undo submits rebuild requests. It is a best-effort reconstruction, not a reload of the previous save.

- Only geometry and a subset of properties are restored. Households, economy, employment, and the wider simulation state are not recreated.
- Vehicles and citizens cannot be undone at all.
- The progress figure counts requests submitted, not entities the simulation has actually finished rebuilding. The tool cannot verify what the game did with them.
- Items that were never captured, or that were skipped while rebuilding, are reported to you rather than silently dropped.

If you need a guaranteed rollback, load an earlier save.

### Notes

- Developed against Cities: Skylines II 1.6.4f1. The mod checks the game assembly on startup and disables itself if the version does not match.
- Very large selections on a heavily modded city can still be heavy on a single frame. Deleting in a few smaller passes is safer than one enormous one.
- When reporting a problem, please attach `MarqueeDelete.status.log`, `Player.log`, your game version, and your mod list.

---

## 中文 — 短版（用于 `ShortDescription`）

按住鼠标左键拖出一个矩形，一次清除选区内的建筑、道路、地下管网、地表、树木、摆件、贴花与车辆市民。支持地上／地下分层操作、分区彩色地块轮廓、级联数量预览，以及外部连接与重要建筑的删除前保护。

**可选资产分步撤回。** 可从六类静态资产中自由勾选要恢复的目标——建筑（连同其扩建、附属院落与内部通道）、道路、地表、树木、摆件、贴花——模组按帧预算分步提交重建请求，最多保留 5 步、每步 8000 条记录。撤回仅重建几何与部分属性，不恢复住户、经济与模拟状态；车辆与市民不支持撤回。

---

## 中文 — 长版（用于 `LongDescription`）

### 这个模组做什么

按住鼠标左键拖出一个矩形，框住的东西一次清掉。建筑、道路、地下管网、地表、树木、摆件、贴花、车辆市民都能批量删除，不必再一个一个点。

### 想删什么，就只删什么

- **资产筛选**：道路与管网（道路、桥梁、隧道、轨道、管道、电力线）、树木、摆件、贴花、地表（行政区与地图区块自动保护）、车辆市民。支持一键全选与恢复默认。
- **建筑分区筛选**：住宅、商业、工业、办公、公共及其他。地块轮廓按建筑真实朝向绘制，大选区依然清晰可辨，并实时显示选区长宽（格数与米数）与各类目标数量。
- **地上／地下隔离**：用原版地下模式按钮切换，地面建筑与地下隧道、管网自动分开，改造地面时不会误拆地下设施。

### 删除之前，先拦住误操作

- **删除前确认**（默认开启）：松开鼠标后锁定选区、汇总全部受影响对象（含级联附属），按 Enter 或点击确认才真正执行。
- **重要建筑二次确认**：选区含市政服务建筑、独特建筑或重点地标时，弹出清单要求二次确认（前 10 个列出名称，其余只显示数量）。
- **外部连接保护**（默认开启）：自动跳过地图边缘的高速公路、铁路、水电连接节点。
- **选框朝向**：可跟随视角旋转，也可固定为世界南北方向。

### 可选资产的撤回

撤回不再只能整批回滚。你勾选要恢复的资产类型，模组只重建这些。

- **六类可撤资产**：建筑（扩建、附属院落与内部通道始终随建筑一起恢复）、道路管网、地表、树木、摆件、贴花。每个开关上显示当前快照中该类记录的数量，动手之前就能看到代价。
- **按帧分步重建**：重建请求按当前帧耗时预算分批提交，父实体查询带缓存以避免重复扫描。最多保留 5 步，每步上限 8000 条记录。
- **支持暂停与继续**，撤回进行中或暂停期间禁止新的框选删除。

### 关于撤回，需要说清楚的事

撤回提交的是**重建请求**，属于尽力恢复，不是读档。

- 仅重建几何与部分属性。住户、经济、就业以及更完整的模拟状态不会被还原。
- 车辆与市民完全不支持撤回。
- 进度数字统计的是**已提交的请求数**，不是游戏已完成重建的实体数——本工具无法校验游戏实际处理结果。
- 未能捕获、或重建过程中被跳过的对象，会明确告知你，而不是悄悄丢掉。

需要确定的回滚，请直接读取较早的存档。

### 其他说明

- 适配 Cities: Skylines II 1.6.4f1。启动时校验游戏程序集，版本不符会自动停用。
- 在多模组环境下对超大范围一次性删除，单帧压力仍然偏重。分几次小范围操作比一次性清空更稳妥。
- 反馈问题请附上 `MarqueeDelete.status.log`、`Player.log`、游戏版本与模组列表。
