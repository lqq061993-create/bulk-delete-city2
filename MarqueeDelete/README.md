# Bulk Delete · 批量删除

`MarqueeDelete` 是《城市：天际线 II》的批量拆除模组。打开原版推土机后，可使用矩形、多边形或自由笔画选区，按建筑分区和资产类型筛选，并复核删除范围。静态资产可按类别撤回，撤回范围为几何与部分属性。

本源码版本为 **2.7.8**，兼容性验证对应游戏 **1.6.4f1**。

在地面视图打开批量删除，展开 **管网细分**，在 **路口设施** 中选择红绿灯或过街斑马线。画出选区并确认后，模组会分批修改所选设施。斑马线筛选适用于可独立修改的普通路口。再次点击选中的设施类型可返回普通筛选。恢复设施可用原版道路服务。

[代码问题与修复建议](docs/2026-11-18-mod-review.md)

## 本地构建

在 Windows 上准备游戏本体、Roslyn 的 `csc.exe`、PowerShell 7、Node.js 18 或以上及 .NET 8 SDK。先根据本机环境修改 `build.ps1` 中的 `$managed` 和 `$csc`，分别指向游戏的 `Cities2_Data\Managed` 目录和编译器文件。然后在项目目录中运行：

```powershell
.\build.ps1
$env:CSII_USERDATAPATH = Join-Path $PWD.Path 'dist\_ui_stage'
npm --prefix ui ci
npm --prefix ui run build
```

关闭游戏后可运行 `.\install.ps1` 安装构建产物。

## 验证

```powershell
dotnet restore .\tests\MarqueeDelete.Tests.csproj
dotnet restore .\tests\DeletionRegression.csproj
.\tests\run_tests.ps1
.\tests\verify_version_sync.ps1
```

参见 [使用说明](使用说明.md) 和 [版本策略](VERSIONING.md)。
