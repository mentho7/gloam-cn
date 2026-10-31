# Gloam 架构与代码通读报告

> **对象**：`D:\WorkSpace\gloam` —— Gloam，一个修 Windows HDR 模式下 SDR 内容发灰问题的系统托盘应用。C# / .NET 10 / WPF，8 个项目（7 个 `src/` + 1 个测试工程），311 个 `.cs`/`.xaml` 文件，85,062 行。
>
> **方法与边界**：全文为**静态阅读**结论——源码、XAML、项目文件、构建脚本、CI 工作流、既有文档与测试。未执行 `dotnet build` / `dotnet test` / `package.ps1`，未运行程序，未连接色度计或真实显示器。凡属推断而非可直接观察的，文中标注「推断」。
>
> **校验方式**：所有结论都可对照源码复核；类型名、方法名、配置键、路径均取原文，可直接 `grep`。行内引用格式 `path:line`。
>
> **章节结构**：§1–§6 是跨切面总览（先读这里）；§7–§12 按子系统逐文件展开；§13 是测试与工程化资产；§14 是附录索引。

## 目录

- [§1 文档定位与阅读指南](#1-文档定位与阅读指南)
- [§2 项目是什么：问题域与产品面](#2-项目是什么问题域与产品面)
- [§3 仓库全景](#3-仓库全景)
- [§4 四条端到端主链路](#4-四条端到端主链路)
- [§5 启动与运行时模型](#5-启动与运行时模型)
- [§6 横切约定、不变量与风险](#6-横切约定不变量与风险)
- [§7 核心运行时：Windows 互操作、Gamma 管线与设置](#7-核心运行时windows-互操作gamma-管线与设置)
- [§8 夜间模式与 Game Lab 的色彩科学](#8-夜间模式与-game-lab-的色彩科学)
- [§9 校准子系统（一）：领域核心](#9-校准子系统一领域核心)
- [§10 校准子系统（二）：UI、报告与状态](#10-校准子系统二ui报告与状态)
- [§11 WPF UI（一）：组合根、托盘、Dashboard、基础设施与主题](#11-wpf-ui一组合根托盘dashboard基础设施与主题)
- [§12 WPF UI（二）：Game Lab、夜间模式、设置与本地化](#12-wpf-ui二game-lab夜间模式设置与本地化)
- [§13 测试、构建、CI/CD、工具链与文档](#13-测试构建cicd工具链与文档)
- [§14 附录：索引、术语表、常用命令与未验证事项](#14-附录索引术语表常用命令与未验证事项)

## 摘要（三十秒版）

**它是什么**：Gloam 是 Windows 托盘应用，修"HDR 模式下 SDR 内容发灰"——问题不在 SDR 内容本身，而在 Windows 把它按 sRGB 解码后送进 HDR 信号，显示端又按 gamma 2.2 解释，于是多解了一次幂。修复就是在输出端补一条每显示器独立的 gamma 2.2 校正曲线。在此之上叠了夜间模式（CAT16 色适应 + mired 渐变 + 褪黑素剂量）与色度计校准（驱动 ArgyllCMS，测量→建模→安装 MHC2→复测），Game Lab 则是把三者按"前台游戏"组合起来。

**架构上的三个事实**：

1. **依赖方向是单向的**：`Gloam.Interop`（P/Invoke）→ `Gloam.Core`（领域）→ `src/Gloam`（WPF 壳）；`Gloam.Localization` 刻意无平台依赖。UI 按特性垂直切分，Core 不知道 WPF 存在。
2. **三条产品线收敛到同一条输出通道**：`GammaApplyService.RequestApply` 是唯一策略入口，`LatestValueCoalescer` 合并+限速（250 ms 下限），`DispwinRunner` 是唯一硬件写实现，10 秒 ramp guard 兜底自愈。任何新功能都必须落到这条通道上，不允许旁路写 ramp。
3. **校准是整个仓库的重心**：`Gloam.Core/Calibration` 57 个文件约 2.5 万行，加上 UI 侧 37 个文件，是仓库最大、最深的子系统；它的商业主张是"报告里的每个数字都是测出来的"，由金标夹具（真实面板录制数据）在 CI 里钉死。

**规模**：8 个项目 / 311 个 `.cs`、`.xaml` / 85,062 行 / 1,216 个测试方法（`[Fact]` 1,125 + `[Theory]` 91）。

**动手前最该知道的五件事**：

1. **启动顺序是契约**：迁移数据 → 日志 → ICM 模板 → DI → 主题 → 本地化 → 启动项 → HDR 迁移 → 窗口。调换会破坏数据或首帧渲染（§5.2）。
2. **校准期间有四处 bypass 检查**，任何新增的硬件写路径都必须接进 `CalibrationStateManager.IsDeviceInBypass`（§7.2.8）。
3. **改色彩/校准数学要同时改测试与白皮书**：`CONTRIBUTING.md` 要求先加"钉住现状"的测试，再改行为，并说明实测了什么。
4. **UI 文本必须进 `src/Gloam/Resources/i18n/*.json`**，硬编码会被测试判失败（§12.7）。
5. **配置文件与测量输入都是不可信输入**：写入用 write-then-move，读失败保留原文件，profile ID 必须是 GUID（§6.2、§6.5）。

**最值得优先处理的四件事**（详见 §6.7 与各章风险表）：三个超大 UI 文件（157KB/95KB/94KB）、`HdrPatchRenderer` 放错工程、`GammaApplyService` 策略过度集中、夜间模式地点检测直连 `http://ip-api.com` 明文 HTTP。

## 1 文档定位与阅读指南

目标读者是第一次接手这个仓库、需要在不运行程序的前提下建立可靠心智模型的工程师。

阅读路线：

1. **§2–§3**：建立产品问题域与模块地图（15 分钟）。
2. **§4**：四条端到端主链路，决定你关心哪一条（10 分钟）。
3. **§5**：启动顺序、DI、线程模型——这是读代码时最容易踩坑的部分（15 分钟）。
4. **§6**：横切约定 + 不变量清单。动手改代码前必读。
5. 之后按需跳进 §7–§12 的对应子系统；§13 解释"改完怎么验证"。

三条经验之谈，先放在这里：

- **这个仓库的注释是资产**，不是噪音。大量 `// 为什么这么做` 的注释记录了真实事故（`robocopy /MIR` 删掉解压模板、SDK 升级冒出 48 条诊断、Windows 明暗切换不触发 `UserPreferenceChanged`……）。`CONTRIBUTING.md` 明确要求"保留既有注释，不要剥离"。
- **顺序即契约**：启动顺序、写入前检查点、关停顺序，改一个就可能破坏数据或渲染。
- **数字必须能测**：整个项目的商业主张是"输出是量出来的，不是断言的"，代码里的容差、金标夹具、验证流程都服务于此。

## 2 项目是什么：问题域与产品面

### 2.1 它解决的那个物理问题

Windows 在开启 HDR 后，会把 SDR 内容按 sRGB 曲线**解码**成线性光，再以 HDR（PQ / scRGB）信号送显。但显示端在 SDR 亮度区间仍按 gamma 2.2 左右的曲线解释这段编码值，于是同一段 SDR 像素被"多解了一次 gamma"：暗部整体抬亮，黑位从 0 变成一层棕灰雾——用户看到的就是"发灰、洗白"。

Gloam 的修复直接：在输出端补一条 **gamma 2.2 校正曲线**（每台显示器独立），把多解的那次幂运算抵掉。README 的 hero 图是**渲染**出来的、不是截图——因为问题恰恰在于"合成器送进面板的信号"与"屏幕捕获到的内容"不一致，截图无法复现。

这个诊断来自 Dylan Raga 的 `win11hdr-srgb-to-gamma2.2-icm`，Gloam 是其 fork 演化而来。仓库根目录 6 个 `.icm` 文件就是这条链路的产物：MHC2（Windows 每显示器校准曲线容器）格式的预置 gamma 模板，按 SDR 白点（100/200/300/400 nits）与 sRGB 域（`srgb_to_gamma2p2_sdr.icm`）分类，作为嵌入式资源随程序分发。

### 2.2 三条产品主线 + 一层组合

| 主线 | 用户价值 | 代码归属 |
|---|---|---|
| **Gamma 校正** | 修好 SDR-in-HDR 发灰；每显示器 Gamma 2.2 / 2.4 / Windows Default | `TransferFunctions` → `LutGenerator` → `DispwinRunner`/`NativeGammaRamp`/MHC2 → `GammaApplyService` |
| **夜间模式** | 按固定时间或太阳位置做暖色偏移；CAT16 色适应 + mired 空间渐变 + Ultra Night 光谱模式 | `NightModeService`、`ColorAdjustments`、`SunCalculator`、`CircadianDoseGovernor`、`JndPacedFade`、`MelanopicMonitorService` |
| **校准** | 驱动真实色度计（经 ArgyllCMS），测量→建模→安装 MHC2→**复测**，报告里每个数字都是量出来的 | `src/Gloam.Core/Calibration`（57 文件，仓库最大区域）+ `src/Gloam/Features/Calibration`（42 文件） |
| **Game Lab（组合层）** | 把上面三条做成"按游戏生效的画面意图"：前台进程检测 → 选中显示器集合 → 组合 gamma/暗部可见度/夜间策略 → 会话回执 | `GamerMode`、`GamerModeCoordinator`、`GameDiscoveryService`、`GammaApplyService.UpdateActiveGamerSessions`、`Features/GameLab` |

三条主线共用一个输出通道：**每显示器一个数据模型（`MonitorProfileData` + `CalibrationSettings`）→ 一个 LUT → 一次硬件写入**。这是全架构最重要的收敛点，见 §4.1。

### 2.3 明确的非目标（产品边界）

- **不做注入**：不进游戏进程、不 hook 交换链、不装 overlay、不读游戏内存。`docs/gamer-mode-design.md` 把这条当作反作弊兼容性的前提。
- **不联网、不上报**：本地运行、无账号、无遥测；唯一网络访问是 ArgyllCMS 下载与 DisplayCAL 社区校正库查询，都有大小/摘要约束。
- **不声称"完美"**：校准报告区分"实测"与"不确定度"；README 明确列出"已有金标夹具验证"（Gigabyte M27Q-P、MSI MAG271QPX28）与"期待数据验证"（OLED/QD-OLED/广色域/mini-LED）两类显示器。

## 3 仓库全景

### 3.1 顶层布局

```
gloam/
├─ src/                       7 个 .NET 项目（见 §3.2）
├─ tests/Gloam.Tests/         xunit 工程（105 个 .cs，含 Golden 真实测量夹具）
├─ docs/                      内部技术文档 + 截图 + 8 份 release-notes
├─ site/                      静态营销站（含 292KB whitepaper.html）
├─ deploy/                    Caddy 站点块 + 部署说明（273 行）
├─ scripts/                   发布校验、白皮书生成、对比图渲染
├─ ai/memory-bank/            任务清单（历史规划，非当前状态）
├─ .github/                   CI 工作流 + issue/PR 模板
├─ .simplify/                 外部工具配置（test/warn 命令、golden 路径）
├─ package.ps1                本地打包入口（Velopack）
├─ Directory.Build.props      MSBuild 全局策略（分析器、严格构建）
├─ Directory.Packages.props   集中包版本管理
├─ global.json                SDK 版本闸门
├─ *.icm                      6 个预置 MHC2 模板（嵌入 + 复制到输出）
└─ README.md / CONTRIBUTING.md / RELEASE.md / SECURITY.md / THIRD_PARTY_NOTICES.txt
```

`.gitignore` 排除 `artifacts/`、`publish/`、`dist/`、`*.log`、`argyll_cache*`；但显式反排除 `tests/Gloam.Tests/Fixtures/**/*.csv`（金标夹具是真实测量数据，必须入库）。`ROADMAP.md` 被刻意排除在仓库之外（内部规划，含竞争定位与未验证结论）。

### 3.2 项目依赖图（编译期）

```
                 ┌──────────────────────────────┐
                 │ Gloam (WinExe, net10.0-win)  │  WPF UI + 组合根
                 │  Shell/ Features/ Infra/ ... │
                 └───┬──────────┬──────────┬────┘
                     │          │          │
      ┌──────────────┘          │          └───────────────┐
      ▼                         ▼                          ▼
┌─────────────────┐   ┌──────────────────────┐   ┌────────────────────────┐
│ Gloam.Core      │   │ Gloam.Localization   │   │ Gloam.Localization.Wpf │
│ (net10.0-win)   │   │ (net10.0, 无 WPF/注册表)│  │ (net10.0-win, WPF 适配)│
└────────┬────────┘   └──────────────────────┘   └───────────┬────────────┘
         │                                                    │
         ▼                                              (引用 Localization)
┌─────────────────┐
│ Gloam.Interop   │  P/Invoke：DisplayConfig / DXGI / GDI32 / User32 / WCS
│ (net10.0)       │
└─────────────────┘

独立可执行：
  Gloam.Cli          → 引用 Gloam.Core                无头色彩管线诊断（CI 友好）
  Gloam.DebugMonitors→ 引用 Gloam.Core + Gloam.Interop 显示器枚举调试
  Gloam.Tests        → 引用 Core + Localization + Gloam(UI)
```

依赖方向是硬约束：**Interop 在最底、不认识任何上层；Core 不认识 WPF；Localization 无平台依赖**。`Gloam.Localization.csproj` 的注释原文是"deliberately platform-neutral: no WPF, no System.Windows, no registry"，因为无头 CLI 也要消费它。

### 3.3 规模（实测，排除 `bin/obj`）

| 项目 | `.cs`/`.xaml` 文件 | 行数 | 备注 |
|---|---:|---:|---|
| `src/Gloam.Core` | 93 | 31,754 | 其中 `Calibration/` 占 57 文件 |
| `src/Gloam`（UI） | 89 | 25,202 | 其中 `Features/Calibration` 占 37 文件 |
| `tests/Gloam.Tests` | 105 | 24,721 | `[Fact]` 1,125 + `[Theory]` 91（332 行 `[InlineData]`）→ 1,216 个测试方法 |
| `src/Gloam.Localization` | 12 | 1,472 | 纯本地化引擎 |
| `src/Gloam.Interop` | 5 | 714 | 5 个 P/Invoke 文件 |
| `src/Gloam.Localization.Wpf` | 5 | 368 | XAML 适配层 |
| `src/Gloam.Cli` | 1 | 790 | 单文件 CLI |
| `src/Gloam.DebugMonitors` | 1 | 41 | 调试入口 |
| **合计** | **311** | **85,062** | |

单体量最大的 5 个文件（读代码时的体力活）：`Features/Calibration/CalibrationReportWindow.xaml.cs`（157KB）、`Features/Dashboard/DashboardWindow.xaml`（95KB）、`Features/Calibration/CalibrationWindow.xaml.cs`（94KB）、`Core/Calibration/CalibrationOrchestrator.cs`（79KB）、`Core/Calibration/Lut3DGenerator.cs`（71KB）。

> 注意：`CalibrationReportWindow.xaml.cs` 的 157KB 不是"没拆分"，而是深度测量工作流的呈现层（视图导航、图表绑定、打印/导出、历史、快照、主题）。见 §10.3。

### 3.4 磁盘上的运行时资产

所有路径必须经 `Gloam.Core/AppPaths`，不允许在别处拼字符串（该类的 xmldoc 原文："Every on-disk path the app owns must route through this class so the folder name lives in exactly one place"）：

| 路径 | 内容 |
|---|---|
| `%LOCALAPPDATA%\Gloam\` | `settings.json`、`app.log`、校准报告、校正文件、下载的 Argyll、解压出的 `.icm` 模板、`i18n/` 外部语言覆盖目录 |
| `%APPDATA%\Gloam\` | 漫游数据（校准 profile JSON） |
| `%LOCALAPPDATA%\GloamApp\` | Velopack 安装根 |
| 进程内覆盖 | `AppPaths.UseDataDirectoriesForCurrentProcess()` —— `--smoke` 启动冒烟与测试用它隔离真实用户数据 |

另有一条**一次性品牌迁移**：`AppPaths.MigrateLegacyData()` 把 `%LocalAppData%\HDRGammaController` 与漫游目录搬到 `Gloam`（`Directory.Move`；被锁则退化为递归复制并保留旧目录）。它必须在 `Log.Initialize()` 之前跑——否则新日志目录先被创建，`Directory.Move` 会因目标已存在而整个放弃。这是"启动顺序即数据安全"的典型例子。

### 3.5 构建与质量闸门速览

- **SDK**：`global.json` 固定 `10.0.100` + `rollForward: latestFeature`，即 SDK 11/12 会被**拒绝**而不是被采用。动机：编译器不能悄悄偏离 CI 跑的那个。
- **目标框架**：全仓 `net10.0` / `net10.0-windows`（.NET 10 为 LTS，支持到 2028-11-14）。仓库有过 `net8.0` 拖过 EOL 的历史，规则因此定为"只跟 LTS 走"。
- **分析器**：`AnalysisLevel` 钉在 `10.0-recommended`（不是 `latest-recommended`）。注释记录理由：SDK 升级曾一次性冒出 48 条新诊断，而 `GloamStrictBuild=true` 把警告当错误 → 无人改动却红构建。已知积压规则在 `KnownAnalyzerBacklog`。
- **严格构建**：`-p:GloamStrictBuild=true` → `TreatWarningsAsErrors`，仅放过 NuGet 漏洞类 `NU1901..NU1904`。
- **包版本**：`Directory.Packages.props` 集中管理（CommunityToolkit.Mvvm 8.4.2、Hardcodet.NotifyIcon.Wpf 1.1.0、MS.Extensions.DI 8.0.1、Velopack 1.2.0、xunit 2.9.3）。
- **本地化强制**：UI 文本必须在 `src/Gloam/Resources/i18n/*.json`，测试会对硬编码字符串判失败（见 §12.7）。
- 细节（`package.ps1`、CI 门禁链、发布脚本）见 §13。

## 4 四条端到端主链路

### 4.1 应用链路：选择 gamma 模式 → 硬件写入

这是全系统的心脏，也是三条产品线最终汇合处。

```
用户动作（托盘菜单 / 热键 / 夜间模式定时器 / 游戏前台切换 / 校准完成）
   │
   ▼
TrayViewModel / GamerModeCoordinator / CalibrationSessionCoordinator
   │  更新 SettingsManager（MonitorProfileData / CalibrationSettings / GammaMode）
   ▼
GammaApplyService.RequestApply(monitor, mode, calibration, whiteLevel)
   │  ① 策略解析：内置 profile？夜间模式暖色？应用排除？游戏会话锁定色温？
   │  ② CalibrationStateManager.IsDeviceInBypass → 校准中直接跳过
   │     （不能踩掉色度计正在读的 identity ramp）
   ▼
LatestValueCoalescer<IntPtr, (...)>        每显示器"最新值合并"，minIntervalMs = 250
   │  滑块连拖只保留最后一帧；对 SetDeviceGammaRamp 设速率下限，
   │  防止显示变更反馈环把合成器卡死（注释原文：can hang the machine）
   ▼
DispwinRunner.ApplyGamma(...)
   ├─ 走已安装的 MHC2/WCS profile（校准产物）
   ├─ 或直接 SetDeviceGammaRamp（LUT 由 LutGenerator 现算）
   └─ 记录 applied state（供 ramp guard 比对）
   ▼
硬件（GPU VCGT / Windows 高级颜色校准曲线）
   ▲
   └── 10 秒 System.Timers.Timer ramp guard：
       VerifyAndRestoreRamps() 回读并恢复被全屏游戏 / 驱动重置踩掉的曲线
```

恢复触发点：开机启动、`WM_DISPLAYCHANGE`（`MainWindow.WndProc`）、`PBT_APMRESUMEAUTOMATIC` 恢复、热键 `Win+Shift+F4` panic 清空、驱动/游戏重置后的 ramp guard 兜底。细节见 §7.2。

### 4.2 夜间模式链路：时间 → 色温 → 色适应 → LUT

`NightModeService` 持有调度状态（固定时间点或 `SunCalculator` 太阳位置），产出当前有效 kelvin；`ColorAdjustments` 把它实现为 **CAT16 色适应**而不是砍蓝通道，所以中性色保持中性；过渡在 **mired 空间**匀速推进（`JndPacedFade`），避免一端快一端慢。`GammaApplyService.ApplyNightModeToCalibration` 把夜间色温叠加进 `CalibrationSettings`，再走 §4.1 的同一条写入路径。Ultra Night 走光谱路径（`.ccss` 面板采样）；`CircadianDoseGovernor` 可对褪黑素剂量设上限，并反过来调整亮度/白点。细节见 §8。

### 4.3 校准链路：驱动色度计 → 建模 → 安装 → 复测

```
CalibrationSetupWindow → CalibrationWindow → CalibrationOrchestrator（状态机）
   ├─ PatchSetGenerator / AdaptivePatchPlanner    生成测量色块序列
   ├─ PatchDisplayWindow / HdrPatchRenderer       满屏显示色块（SDR 或 FP16 HDR）
   ├─ SpotreadSession / ColorimeterService        经 ArgyllCMS spotread 读回 XYZ
   ├─ ColorMath / Cam16Ucs / ClosedLoopCorrector  建模、闭环修正、漂移补偿
   ├─ Mhc2ProfileBuilder / HdrMhc2LutBuilder / Lut3DGenerator   生成 profile/LUT
   ├─ CalibrationProfileInstaller / AdvancedColorProfileAssociation  安装并让 Windows 真正启用
   └─ CalibrationVerifier / UncertaintyBudget / CalibrationReport    复测 + 不确定度 + 报告
```

校准期间用 `CalibrationStateManager` 的 bypass 机制摘掉 Gloam 自己的校正——否则"用校正后的状态去测校正"。细节见 §9、§10。

### 4.4 游戏链路：前台进程 → 会话 → 组合意图

`GameDiscoveryService` 扫安装库；`AppDetectionService` 挂前台窗口钩子；`GamerModeCoordinator` 每 2 秒校验启用中 profile 的可执行路径；命中后组装 `GamerSessionAssignment` 交给 `GammaApplyService.UpdateActiveGamerSessions`，后者**语义相同则不重置会话**（保住 Gameplay Lock 捕获的色温）。画面意图（Reference / Competitive Clarity / Cinematic HDR / Night Ops / Custom）只是三组参数的预设：gamma 策略、线性光暗部提升曲线、夜间策略。细节见 §8.4。

## 5 启动与运行时模型

> 本章给结论与理由；`Program.cs` / `App.xaml.cs` / `MainWindow` 的逐行展开、DI 表与消息处理细节见 §11.1。

### 5.1 进程启动（`src/Gloam/Shell/Program.cs`）

1. **Velopack hooks 必须最先跑**：`VelopackApp.Build().OnAfterInstallFastCallback(...).OnBeforeUninstallFastCallback(...).OnFirstRun(...).Run()`。安装/更新/卸载时它处理参数后直接结束进程，早于任何窗口创建。因此 `Gloam.csproj` 设了 `<StartupObject>Gloam.Shell.Program</StartupObject>`，而不是用 App.xaml 生成的 `Main`。
2. **`--smoke` 冒烟模式**：不建窗口，只把数据目录指到临时路径、初始化日志、`ResourceExtractor.ExtractIcmProfiles()`、`App.ConfigureServices()` 并解析 `SettingsManager` / `NightModeService` / `ProfileManager` / `ILocalizer`，最后校验日志文件存在。这是 CI 里唯一能验证"组合根没炸"的手段。
3. **单实例互斥**：`Mutex("Local\\Gloam-SingleInstance")`。注释写明动机——两个托盘会抢 GPU ramp 并互相覆写 `settings.json`。`Local\` 前缀把互斥体限定在当前登录会话（每用户托盘应用的正确定义域）。
4. `new App(); app.InitializeComponent(); app.Run();`

### 5.2 `App.OnStartup` 顺序（顺序本身是架构契约）

`src/Gloam/Shell/App.xaml.cs`：

1. `AppPaths.MigrateLegacyData()` —— 必须在日志初始化前（见 §3.4）。
2. `Log.Initialize()` —— WinExe 没有控制台，文件日志是唯一诊断出口，所以第一个真正的初始化动作是它。
3. `ResourceExtractor.ExtractIcmProfiles()` —— 嵌入式 `.icm` 模板落地/更新。
4. 订阅 `SystemEvents.UserPreferenceChanged` / `TimeChanged` / `BrutalistTheme.Changed`（并在 `Exit` 时退订）。
5. `ConfigureServices()` → `App.Services`（组合根，纯 DI：无窗口、无主题状态）。
6. 主题：`BrutalistTheme.Persist = ...`、`BrutalistTheme.Initialize(settings.DarkTheme ?? ThemeDetector.IsDarkMode())`、`ApplyTrayMenuTheme()`。
7. 本地化：`LocalizedStrings.Default = ...`、`LanguageService.ApplyStartup(settings.Language)`。注释明确"必须在第一个 XAML 被解析之前"——`{loc:Tr}` 绑定走 `LocalizedStrings.Default`，且文化字体策略会改写 `BodyFont`/`DisplayFont` token。
8. 启动项自愈：`StartupManager.EnableByDefaultForFreshInstall()`、`StartupManager.RepairIfStale(updateService.IsInstalled)`（只在已安装构建上修 HKCU Run；便携/开发运行不许抢注册）。
9. `AdvancedColorProfileMigrationService.Run()` —— 修复旧版 HDR profile 特征化，并**验证 Windows 确实在查包含该 profile 的那个列表**。
10. `new MainWindow()` —— 唯一真实窗口（不可见，只承载托盘图标与消息循环）。
11. `WarnIfLegacyInstallPresent()` —— 检测 `C:\Program Files\HDR-Gamma-Adjust\Gloam.exe` 残留（旧解析器会在加载失败时破坏 `settings.json`），用 toast 警告一次，**不尝试删除**（需提权）。

任何一步抛异常 → `Log.Error("CRITICAL STARTUP ERROR")` + `Shutdown(-1)`。

### 5.3 窗口与消息循环

`App.xaml` 设 `ShutdownMode="OnExplicitShutdown"`：MainWindow 关掉不等于退出。

`MainWindow` 构造函数：`EnsureHandle()` 提前建 HWND（`HotkeyManager` 要它）→ `new HotkeyManager(handle)` → `ActivatorUtilities.CreateInstance<TrayViewModel>(App.Services, _hotkeyManager)`（`HotkeyManager` 是**窗口绑定**的、不能进容器，这是显式的 DI 例外）→ 把 `NotificationRequested` 接到 `IToastService`（Core→UI 的 toast 边界）→ `HwndSource.AddHook(WndProc)`。

`WndProc` 处理三条消息：

- `WM_DISPLAYCHANGE` → `HandleDisplayChange()`（重算显示器集合与模式）；
- `WM_POWERBROADCAST` + `PBT_APMRESUMEAUTOMATIC` → `HandleResume()`；
- `WM_SETTINGCHANGE` 且 `lParam` 字符串 == `"ImmersiveColorSet"` → `RefreshThemeFromSystem()`。注释解释了为什么不靠 `SystemEvents`：Windows 的明暗切换**经常不触发** `UserPreferenceChanged`，靠它会把托盘菜单卡在启动时的外观上。

关停：`Application.Exit` 与 `OnClosed` 都调幂等 `Cleanup()`（`_cleanedUp` 守卫），dispose `TrayViewModel`（停夜间模式与 ramp guard 定时器、解前台钩子、顺带应用待安装更新）与 `HotkeyManager`；`OnExit` 再 `_serviceProvider?.Dispose()` 兜底。

### 5.4 线程模型（读代码时最容易踩的坑）

| 执行体 | 谁 | 约束 |
|---|---|---|
| STA UI 线程 | 全部 WPF 窗口、`Dispatcher` | 跨线程回调必须 `Dispatcher.BeginInvoke/Invoke`；`App.OnTimeChanged` 是显式 marshal 的例子 |
| `System.Timers.Timer` | `GammaApplyService._rampGuard`（10s）、夜间模式定时器 | 回调在 threadpool；已被 try/catch 包住，只做回读/恢复 |
| threadpool（coalescer） | `LatestValueCoalescer`，每键单飞 | `minIntervalMs: 250` 硬下限；dispose 时取消在飞工作 |
| 专用线程 | `MelanopicMonitorService._worker` | 有界队列 + 关闭时与入队同步（审计报告专门记录过这个竞态） |
| 系统广播线程 | `SystemEvents.*` | `App` 里一律先 marshal 到 dispatcher |
| 子进程 | `BoundedProcess` / Argyll / dispwin | 输出有上限、可取消；`ProcessStartInfo.ArgumentList` + 禁用 shell |

测试工程用 `DisableTestParallelization`（`tests/Gloam.Tests/AssemblyInfo.cs`）：主题 token、UI culture、`static` 路径都是进程级状态，并发只会制造 flake。

### 5.5 DI 注册表（`App.ConfigureServices`，全部 singleton）

| 服务 | 构造要点 |
|---|---|
| `MonitorManager` | 显示器枚举与缓存 |
| `SettingsManager` | 设置门面（磁盘 I/O、迁移、规范化已拆到 `SettingsPersistence`/`SettingsNormalization`） |
| `AdvancedColorProfileMigrationService` | 启动时 `.Run()` 一次 |
| `DispwinRunner` | 自动探测 Argyll 路径；真正执行硬件写入 |
| `NightModeService` | `new NightModeService(settings.NightMode)` |
| `ProfileManager` | `new ProfileManager(<BaseDirectory>\srgb_to_gamma2p2_100_mhc2.icm)`；注册但**全仓无调用点**（现役安装走 `CalibrationProfileInstaller.Install`，见 §7.5） |
| `GammaApplyService` | 依赖 dispwin + settings + nightmode；内部持有 coalescer 与 ramp guard |
| `MelanopicMonitorService` | 订阅 `GammaApplyService` 的状态快照（注释标为 roadmap 3.1） |
| `AppDetectionService` | 前台进程检测 |
| `UpdateService` | Velopack 静默更新 |
| `IToastService` → `ToastService` | 接口在 Core，实现在 UI |
| `TrayViewModel` | **由 MainWindow 用 `ActivatorUtilities` 创建**；直接 resolve 会得到没有热键的实例（注释原文） |
| `ILocalizer` / `LocalizedStrings` / `LanguageService` | 本地化；`LanguageService` 注入设置写回回调 |

注册是**显式**的，没有程序集扫描/约定注册。

## 6 横切约定、不变量与风险

### 6.1 分层与命名空间

- UI 按**特性垂直切分**（`Features/<Name>/`），不是按技术层。理由见 `docs/project-structure.md`：老布局把 40 个文件堆在一个目录，评审"夜间模式改动"要滚过一堆无关文件。
- UI 工程把每个 feature 命名空间声明成项目级 `<Using>`（`src/Gloam/Gloam.csproj`）。这是**刻意的例外**：UI 是一个可部署模块，文件在同一 feature 内换目录不该引发几十处 using 改动。`Gloam.Core` / `Gloam.Localization` 保持显式 using（模块边界上这是对的）。
- 资源字典必须用**绝对 pack URI**：`pack://application:,,,/Gloam;component/Themes/...`。相对 URI 会按**窗口所在目录**解析，移动窗口会静默丢主题且编译器不报错。
- `Themes/`、`Fonts/` 必须留在工程根：它们的 pack URI 与字体族名是运行时身份。
- **领域代码不许住进 UI 工程**。现存唯一例外是 `Features/Calibration/HdrPatchRenderer.cs`，已在 `docs/project-structure.md` 记为搬迁候选。

### 6.2 持久化

- 写入一律 **write-then-move**：`TextFileStore` 先写 `path.<guid>.tmp` 再 `File.Move(..., overwrite: true)`。
- 新 schema 不被旧二进制覆写：`SettingsPersistence.Load` 返回 `NewerSchema` 标志，调用方据此拒绝降级写入。
- **读不出来的设置保留原文件而不是重置成默认**（审计报告把这条当作数据安全底线）。
- 校准 profile ID 必须是 GUID：历史上出现过 `..\outside` 形式路径穿越，现在非法 ID 在设置校验阶段被清空。

### 6.3 错误处理

- `Log.Info/Error` 是唯一日志出口，写 `%LOCALAPPDATA%\Gloam\app.log`，`LogFileRotator` 负责滚动。
- WPF 未处理异常走 `App_DispatcherUnhandledException`：**每次会话只弹一次** crash dialog（`_crashDialogShown`），之后只记录并吞掉，避免对话框刷屏循环；用户选"退出"才让异常继续传播。
- 大量 `try { } catch (Exception ex) { Log.Error(...) }` 是刻意的最佳努力语义（尤其启动清理、主题、迁移路径）。新增 catch 应说明"为什么可以降级"。

### 6.4 本地化

平台无关引擎（`Gloam.Localization`：`Localizer`、`StringCatalog`、`CatalogSources`、`CultureChain`、`PluralRules`、`MessageTemplate`、`SourceAudit`、`CatalogValidator`、`MissingKeyReport`）+ 极薄 WPF 适配（`Gloam.Localization.Wpf`：`Loc`、`TrExtension`、`LanguageService`、`CultureFontPolicy`、`LocalizedStrings`）。基线目录内嵌在程序里（`en-US.json` 59KB / `zh-Hans.json` 58KB）；`%LOCALAPPDATA%\Gloam\i18n` 可在不改构建的前提下覆盖（`CompositeCatalogSource` 先内嵌后目录；目录里的坏文件是"跳过"而不是"致命"）。细节见 §12.4–§12.6。

### 6.5 安全边界（审计已确认，改动时不要破坏）

- **ArgyllCMS**：HTTPS 下载 + 压缩包/解压后大小与条目数上限 + 固定 SHA-256 摘要 + ZIP 穿越防护 + 暂存后原子替换。
- **更新包**：校验包身份、版本、大小、摘要；签名走仓库的可信签名工作流，仓库里没有凭据。
- **外部进程**：`ProcessStartInfo.ArgumentList` + 禁用 shell；唯一的 shell/提权路径是 Argyll USB 驱动安装器。
- **全部外部输入**（settings/profile JSON、测量 CSV、CCSS/CCMX、社区校正库响应）都有字节/行/列/字段上限；坏文件保留、不被默认值覆盖。

### 6.6 关键不变量清单（改动前必读）

1. **输出只有一条通道**：任何功能最终都必须落到"每显示器一个 LUT → 一次 coalesced 硬件写入"，不允许旁路写 ramp。
2. **校准期间必须 bypass**：`IsDeviceInBypass` 在 **四处**检查——`RequestApply`（调用线程）、coalescer 回调、`DispwinRunner.ApplyGamma` 系统调用前（`skipIfBypassed`）、ramp guard 每 tick。每一处都在关掉上一处与硬件写之间的 TOCTOU 窗口（详见 §7.2.8）。
3. **写入速率有下限**：`LatestValueCoalescer` 的 `minIntervalMs` 不是优化，是防挂死保护。
4. **数字必须实测**：校准报告里每个数都来自穿过已安装 profile 的测量，不是模型预测；`GoldenReplayTests` 用真实面板录制数据把这些数钉死。
5. **打包模板不能只靠解压**：`.icm` 同时以 `EmbeddedResource` 与 `None CopyToOutputDirectory` 存在，因为 `robocopy /MIR` 会删掉不在 publish 目录里的解压结果（历史 bug："No MHC2 template found"）。
6. **UI 文本不许硬编码**：`LocalizationAuditTests` 会失败。
7. **启动顺序是契约**：迁移→日志→ICM→DI→主题→本地化→启动项→HDR 迁移→窗口，调换会破坏数据或渲染。
8. **`GloamStrictBuild` 下零警告**是 CI 闸门；新增诊断要连同修复一起提交。

### 6.7 风险与可改进点（读代码时观察到的）

| 观察 | 影响 | 建议 |
|---|---|---|
| 三个巨型文件：`CalibrationReportWindow.xaml.cs` 157KB、`CalibrationWindow.xaml.cs` 94KB、`DashboardWindow.xaml` 95KB | 改动面大、合并冲突高、静态阅读成本高 | 审计报告已把 probe/session 生命周期、验证、报告持久化、profile 安装策略抽成协作者；剩余部分应沿"UI 能力"继续切，而不是按行数切 |
| `Features/Calibration/HdrPatchRenderer.cs` 是 Core 职责却住在 UI 工程 | 依赖方向被打破，CLI/测试无法复用 | 搬到 `Gloam.Core`（`docs/project-structure.md` 已登记） |
| `MainWindow` 同时持有 3 个 Win32 消息分支、托盘宿主与 DI 例外创建 | 组合根之外还有真实逻辑 | 消息分发可抽 `SystemEventBridge`；`HotkeyManager` 用工厂注册可消除 `ActivatorUtilities` 例外 |
| 多线程共享 `SettingsManager` / 显示器集合、`static` 主题与文化 | 无锁读取路径多，测试只能串行 | 已用 `LatestValueCoalescer` + `_policyLock` 收敛写入；新增共享状态应沿用同一模式 |
| 校准/夜间/游戏三条策略都在 `GammaApplyService` 里解析（35KB） | 改动需理解全部策略 | 短期是正确收敛点；若继续增长，按"策略 → 有效状态"抽策略对象 |
| 大量"最佳努力"catch 只记日志 | 真故障可能被降级成静默 | 保持现有语义，但新增 catch 必须写明为什么可以降级 |

---

## 7 核心运行时：Windows 互操作、Gamma 管线与设置

> **本章范围**：`src/Gloam.Interop`（5 个 P/Invoke 文件）、`src/Gloam.Core` 根目录里的运行时文件（LUT 生成、下发、监控枚举、设置、日志、诊断、模板补丁、Windows Night Light）、`src/Gloam.Cli` 与 `src/Gloam.DebugMonitors`。
>
> **本章回答的问题**：用户在滑块上动一下，到显示器硬件真正改变之间发生了什么；哪些状态必须跨重启记住；外部把 ramp 冲掉时谁把它修回来；失败时兜底路径是什么。
>
> **边界**：`src/Gloam.Core/Calibration/`（校准闭环、ICC 安装细节）、夜间模式的调度/剂量算法、游戏模式的前景判定属于领域章节；本章只在它们与运行时的**接缝**处引用具体类型和行号。行内引用格式 `path:line`，所有类型名、方法名、DLL 名、注册表键均取自源码可直接 `grep`。

### 7.1 Interop 层：四个 DLL 与五份文件

Interop 层的设计立场很明确：**把 Windows 调用面收窄成十几个具名入口**，业务代码不直接 `DllImport`。`src/Gloam.Interop` 只有 5 个文件，项目 TFM 是 `net10.0`（`src/Gloam.Interop/Gloam.Interop.csproj`）而不是 `net10.0-windows`，且 `AllowUnsafeBlocks=true`——这个项目不引用 WPF/WinForms，只有 P/Invoke、结构体和 COM 接口声明；真正的窗口/托盘代码在 `src/Gloam`（`net10.0-windows`）。这个分层意味着：**Interop 的编译不依赖 Windows 桌面框架，但它的任何调用都只能在 Windows 上执行**。

```
src/Gloam (WPF 托盘)  ─┐
src/Gloam.Cli          ─┼─►  Gloam.Core  ──►  Gloam.Interop  ──►  gdi32 / dxgi / user32 / mscms / kernel32
src/Gloam.DebugMonitors─┘        (无 DllImport)     (唯一 P/Invoke 所在)
```

三处"谁调谁"的例外值得记住：`Gloam.Cli` 只依赖 `Gloam.Core` 的**纯计算**部分（不触达 Interop 的调用入口，见 §7.4.3）；`Gloam.DebugMonitors` 直接 `new MonitorManager()` 不经 DI（应用里也有几处校准窗口同样直接 new，见 `src/Gloam/Features/Calibration/CalibrationReportWindow.xaml.cs:1482`）；`src/Gloam.Core` 自己没有任何一处 `DllImport`——所有系统调用都必须先落进 `Gloam.Interop` 的具名方法。

#### 7.1.1 Gdi32.cs：唯一的硬件 ramp 写入口

`Gdi32` 只做一件事：声明 GDI 的 256 项 16 位 gamma ramp 接口（`src/Gloam.Interop/Gdi32.cs:18`）：

- `GammaRamp` 结构：`Red/Green/Blue` 各 `[MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] ushort[]`，`GammaRamp.Create()` 负责分配三个数组。
- `CreateDC` / `DeleteDC` / `SetDeviceGammaRamp` / `GetDeviceGammaRamp`，全部 `SetLastError = true`。
- 文件头注释写明了选择理由：`SetDeviceGammaRamp` **就是 ArgyllCMS `dispwin` 加载 VCGT 时调用的同一个 API**，直接调用可以得到逐位相同的硬件状态，省掉 `~100-500ms` 的进程启动（`src/Gloam.Interop/Gdi32.cs:8-10`）。
- 真正调用它的是 `src/Gloam.Core/NativeGammaRamp.cs`（见 §7.2.5），业务代码通过 `NativeGammaRamp.TryApply/TryRead/TryClear` 访问。

#### 7.1.2 Dxgi.cs：显示器枚举与 HDR 事实

`Dxgi` 手工声明了 `CreateDXGIFactory1` 和三个 COM 接口（`IDXGIFactory1`、`IDXGIAdapter1`、`IDXGIOutput6`）：`[ComImport]` + `[Guid]` 手写接口，方法按 vtable 顺序逐个 `[PreserveSig]` 声明，没有用 CsWin32 之类的生成器（`src/Gloam.Interop/Dxgi.cs:71-160`）。关键点：

- `LUID`、`DXGI_ADAPTER_DESC1`、`DXGI_OUTPUT_DESC1`、`RECT` 四个结构就是 `MonitorInfo` 的原始来源。`DXGI_OUTPUT_DESC1` 带 `MinLuminance/MaxLuminance/MaxFullFrameLuminance`，即 HDR 元数据（`src/Gloam.Interop/Dxgi.cs:39-60`）。
- 常量 `DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020 = 12`。但判断 HDR 不是只认 12：`src/Gloam.Core/DxgiColorSpaceInfo.cs:10` 用 `12/13/14/16/18/19` 六种 G2084/GHLG 色彩空间认定 HDR，覆盖 RGB/studio RGB/YCbCr 与 left/topleft 排列——TV、驱动、传输方式都会改变这个值。
- 唯一调用方是 `MonitorManager.EnumerateMonitors()`：`CreateDXGIFactory1` → `EnumAdapters1` → `EnumOutputs` → QI 到 `IDXGIOutput6` → `GetDesc1`（`src/Gloam.Core/MonitorManager.cs:13-224`）。RCW 生命周期在每层都显式 `Marshal.Release` / `ReleaseComObject`，失败分支也会释放（`MonitorManager.cs:68-99`，注释说明了不释放会漏一个 refcount）。
- 可读性风险：枚举结束的 `DXGI_ERROR_NOT_FOUND` 被写成魔法数 `-2147024896`（即 `0x887A0002`），在 `MonitorManager.cs:56` 与 `:121` 重复出现。修改时不能把"任意负 HRESULT"当结束——真正的错误也应记录并中断该层枚举。

#### 7.1.3 DisplayConfig.cs：GDI 名字 → (adapter LUID, source id) 与 SDR 白点

GDI 设备名（`\\.\DISPLAY2`）不足以做颜色管理：Advanced Color 的 profile 关联 API 要的是 **DisplayConfig 的 adapter LUID + source id**；HDR 下"SDR 内容有多亮"也只有 DisplayConfig 能读。`DisplayConfig` 因此提供三个入口（`src/Gloam.Interop/DisplayConfig.cs`）：

| 方法 | 读什么 | 谁用 |
|---|---|---|
| `TryGetPathForGdiName` | 遍历 `QDC_ONLY_ACTIVE_PATHS` 的活动 path，用 `DISPLAYCONFIG_SOURCE_DEVICE_NAME` 匹配 GDI 名，返回 `adapterId/sourceId/targetId` | `MonitorManager.EnrichWithDisplayConfig`、`ProfileManager`/校准安装侧 |
| `TryGetAdvancedColorInfo` | `GET_ADVANCED_COLOR_INFO`（type 9），解析 bit0 `advancedColorSupported`、bit1 `advancedColorEnabled`、bit3 `advancedColorForceDisabled` | 区分 HDR 与 Win11 22H2+ 的 SDR "ACM"（注释在 `DisplayConfig.cs:163-168`） |
| `TryGetSdrWhiteLevelNits` | `GET_SDR_WHITE_LEVEL`（type 11），`nits = SDRWhiteLevel / 1000.0 * 80.0` | HDR 显示器真实 SDR 白点，替换 `MonitorInfo.DefaultSdrWhiteLevel = 200` |

两个实现细节值得记：

1. **buffer 竞态重试**：显示拓扑可能在 `GetDisplayConfigBufferSizes` 与 `QueryDisplayConfig` 之间变化。代码对 `ERROR_INSUFFICIENT_BUFFER (122)` 最多重试 3 次，其余错误直接返回 false（`DisplayConfig.cs:124-138`）。
2. **结构体对齐事故**：`DISPLAYCONFIG_PATH_TARGET_INFO` 里的刷新率被声明成两个 `uint` 而不是 `long`。源码注释（`DisplayConfig.cs:38-40`）写明：`long` 会强制 8 字节对齐，把结构体从 48 字节撑到 56 字节，`QueryDisplayConfig` 会以 `ERROR_INVALID_PARAMETER (87)` 拒绝整个调用。这是典型的 P/Invoke 对齐陷阱，改结构体前必须重读这段注释。

`DISPLAYCONFIG_MODE_INFO` 用一个 `[StructLayout(Size = 64)]` 的空结构占位——不读 modes，但 `QueryDisplayConfig` 要求 buffer 尺寸正确（`DisplayConfig.cs:56-59`）。

#### 7.1.4 Wcs.cs：ICC 安装、关联与 MHC2 的入口

`Wcs` 声明 `mscms.dll` 的颜色管理 API（`src/Gloam.Interop/Wcs.cs`），分成三组：

- **传统 SDR 路径**：`InstallColorProfile` / `UninstallColorProfile` / `AssociateColorProfileWithDevice` / `DisassociateColorProfileFromDevice` / `WcsSetDefaultColorProfile` / `WcsGetDefaultColorProfile(Size)`。
- **Advanced Color（HDR）关联**：`ColorProfileAddDisplayAssociation` / `ColorProfileRemoveDisplayAssociation` / `ColorProfileSetDisplayDefaultAssociation` / `ColorProfileGetDisplayList` / `ColorProfileGetDisplayDefault` / `ColorProfileGetDisplayUserScope`。这些以 `(LUID adapterID, uint sourceID)` 定位显示器，`associateAsAdvancedColor: true` 时写的是注册表 `ICMProfileAC` 这条 **Advanced Color 关联列表**——Windows 在 HDR 下真正查询的那一条（`Wcs.cs:90-97`）。`GetDisplayDefault` 用 `CPST_EXTENDED_DISPLAY_COLOR_MODE` 读回 HDR 默认（`src/Gloam.Core/Calibration/AdvancedColorProfileAssociation.cs:458-474`）。
- **杂项**：`GetColorDirectory`（颜色存储目录，默认 `%SystemRoot%\System32\spool\drivers\color`）、`LocalFree`（释放 API 分配的 `LPWSTR`/数组，见 `AdvancedColorProfileAssociation.cs:452-455`）、`WcsGetUsePerUserProfiles` / `WcsSetUsePerUserProfiles`、`CLASS_MONITOR = 0x6D6E7472`（ASCII `'mntr'`）。

调用者全在 `src/Gloam.Core`：`ProfileManager`（旧链路，见 §7.2.6）与 `CalibrationProfileInstaller` / `AdvancedColorProfileAssociation`（现役校准安装链路，属 Calibration 章）。

#### 7.1.5 User32.cs：显示枚举、热键、前景窗口、监视器矩形

`User32` 是杂项集合，但每块都有明确调用方：

- `EnumDisplayDevices` + `DISPLAY_DEVICE`（`cb` 必须先 `Initialize()`）：`MonitorManager.EnrichWithGdiData` 用它拿 `DeviceID`（形如 `MONITOR\GSM5B08\{GUID}\0008`，持久化键 `MonitorInfo.MonitorDevicePath` 的来源）。
- `RegisterHotKey` / `UnregisterHotKey`：`src/Gloam/Features/Tray/HotkeyManager.cs:44,55`；`TrayViewModel` 注册 Win+Shift+F4（panic）、Win+Shift+N、Win+Shift+G 及每显示器 gamma 热键。注册失败不会崩：`TrayViewModel` 对每个 id 判 `<= 0` 后跳过绑定，并在日志里记"另一个应用可能占用了快捷键"（`TrayViewModel.cs:490-499`）；`HotkeyManager.Dispose` 统一反注册（`HotkeyManager.cs:75`）。
- `GetForegroundWindow` / `GetWindowThreadProcessId` / `GetWindowRect` / `SetWinEventHook(EVENT_SYSTEM_FOREGROUND, WINEVENT_OUTOFCONTEXT)`：`AppDetectionService` 的前景进程检测（`src/Gloam.Core/AppDetectionService.cs:20-28,59-84`），游戏模式与"应用排除"都吃这条事件流。
- `MonitorFromWindow` / `MonitorFromPoint` / `GetMonitorInfo`：包装成 `TryGetMonitorBounds`（返回 `rcMonitor`，含任务栏）与 `TryGetMonitorWorkArea`（返回 `rcWork`，排除任务栏，用于通知 toast 定位）。注释解释了为什么要区分：HMONITOR 值在显示配置变化后会 stale，必须用矩形重新匹配（`src/Gloam.Interop/User32.cs:70-103`）。
- `User32Safe`：把 `Marshal.GetLastWin32Error()` 收进 `out int errorCode` 或抛 `Win32Exception`，失败信息带 `id/modifiers/vk`。

#### 7.1.6 这一层的两条硬约束

1. **结构体布局是 ABI，不是代码风格。** 所有结构都 `LayoutKind.Sequential`，字符串用 `ByValTStr` 定长，`GammaRamp` 用 `ByValArray` 定长；`DisplayConfig` 的对齐注释（§7.1.3）说明改动会直接让 API 返回参数错误。
2. **COM 引用计数必须显式管理。** `MonitorManager` 里每个 `GetObjectForIUnknown` 得到的 RCW 都有配对的 `ReleaseComObject`，raw `IntPtr` 有配对的 `Marshal.Release`；失败分支单独释放，因为"cast 失败但仍持有 refcount"是真实泄漏点（`MonitorManager.cs:68-99`）。`Marshal.GetObjectForIUnknown` 返回的 RCW 与 `Marshal.Release` 的 raw pointer 是两套计数，不能混用。

### 7.2 Gamma 管线

#### 7.2.1 端到端：一次 apply 经过谁

```
调用方（TrayViewModel / NightModeService 淡入 / GamerModeCoordinator / dashboard 滑块）
   │  RequestApply(monitor, mode, manualCalibration?, nightKelvinOverride?, nightModeSettingsOverride?)
   ▼
GammaApplyService.RequestApply           ← 同步解析策略（设置、夜间模式、应用排除、游戏会话、旁路检查）
   │  LatestValueCoalescer.Submit(HMonitor, (monitor, effectiveMode, calibration, whiteLevel))
   ▼
LatestValueCoalescer（每显示器单飞 + 250ms 下限，最新值胜出）
   │  work 回调：再查一次 bypass → DispwinRunner.ApplyGamma(..., skipIfBypassed: true)
   ▼
DispwinRunner.ApplyGamma                 ← 生成 LUT（有校准 profile 走 GenerateCalibratedLut）
   │  去重（与上次相同则跳过）→ bypass 前检查 → NativeGammaRamp.TryApply
   ▼                                  ↘ 失败：EnsureConfigured → 写临时 .cal → dispwin -d N <cal>
SetDeviceGammaRamp（GDI，256×16bit）      失败：dispwin -d N -c
   ▼
RecordApplied：_lastApplied + _deviceNameToPath ← 供 10s ramp guard 校验与"校准中不恢复"
```

这条链上有四个"用户可见"的性质，全部来自源码约束：**同一显示器同一时刻最多一个硬件写在跑**（coalescer 每键一把 `SemaphoreSlim`）、**写频率有下限**（`minIntervalMs: 250`）、**相同 LUT 不重写**（`DispwinRunner` 的 `_lastApplied` 比较）、**校准测量期间绝不覆盖 identity ramp**（多点 bypass 检查，见 §7.2.8）。

每一跳的失败行为是显式分层的：**策略解析阶段**的编程错误会冒泡（那是调用方 bug），**落地阶段**的失败一律降级不抛——coalescer 捕获回调异常并继续服务后续值（`LatestValueCoalescer.cs:243-248`）、原生 ramp 失败退 dispwin、dispwin 失败清 `_lastApplied` 让下次重试（`DispwinRunner.cs:397-408`）、ramp guard 单台异常不中断其他显示器（`DispwinRunner.cs:72-127`）、快照发布异常只记 info（`GammaApplyService.cs:513-518`）。净效果：**下发路径不会因单台显示器失败而中断**。

#### 7.2.2 模式选择与 LUT 生成（`LutGenerator`）

`GammaMode` 只有三个值（`src/Gloam.Core/GammaMode.cs`）：`Gamma22`（通用 PC）、`Gamma24`（BT.1886 暗室）、`WindowsDefault`（恒等/旁路）。它同时决定两件事：是否做 gamma 幂运算，以及 SDR/校准分支里"解码用的目标 gamma"。`DispwinRunner.GammaModeToValue` 把它映射成 `1.0/2.2/2.4`（`src/Gloam.Core/DispwinRunner.cs:431-440`），供 `GenerateCalibratedLut(double targetGamma, ...)` 使用。

`LutGenerator.GenerateLut(GammaMode, double sdrWhiteLevel, CalibrationSettings, bool isHdr)` 返回 `(R, G, B, Grey)` 四组 **1024 点** LUT（`src/Gloam.Core/LutGenerator.cs:96-127`）。缓存是工业级的：

- key 是完整参数元组，`CalibrationCacheKey` 把 `CalibrationSettings` 的每个标量、算法枚举、CCSS 的 9 个 melanopic/亮度系数、`CalibrationProfileId`、`MeasuredCorrectionLut` 引用都放进去（`LutGenerator.cs:34-63`）。注释写明：**光用 hash 会碰撞并返回错误的 LUT**，所以必须逐字段比较。
- 缓存上限 100 项，超限**整体清空**而不是假装 LRU——`ConcurrentDictionary.Keys` 没有顺序，"淘汰最旧"实际是随机（`LutGenerator.cs:65-69,119-125`）。
- **返回值是共享只读数组**：`LutGenerator.cs:26-28` 明确契约"调用方不得修改"，因为滑块拖动频率下每次都克隆 4×1024 个 double 不划算。`DispwinRunner`、校准闭环拿到的都是同一份引用。

三条生成分支，语义完全不同：

1. **`WindowsDefault` 且无任何调整** → 恒等 LUT（`LutGenerator.cs:143-155`）。
2. **SDR（`isHdr == false`）** → "按目标 gamma 解码 → 线性光里做校准 → 按显示器原生 ~2.2 再编码"（`LutGenerator.cs:157-198`）。注释里有一句关键语义：Windows 的 SDR 内容是按 ~2.2 母版制作的，显示端**始终**按 2.2 解释，所以 `gammaMode` 控制的是**解码**信号，不是显示器；`Gamma22` 与 `WindowsDefault` 因此净值等于直通，`Gamma24` 才真正压暗阴影。曾经这里两条分支都硬编码 2.2，导致 SDR 下选 2.4 是空操作（`LutGenerator.cs:166-168`）。
3. **HDR（默认）** → 六步管线（`LutGenerator.cs:8-18` 的摘要 + 233-316 的实现）：`PQ EOTF → 线性 nits → (非 WindowsDefault) sRGB 反 EOTF + 目标 gamma 幂 + 缩放回 nits → 线性空间校准（Rec.2020 基底）→ PQ 反 EOTF 编码 → SDR 白点以上只淡出"修正量"、保留 HDR headroom`。

HDR 分支里唯一出现在源码中的完整公式是 PQ（SMPTE ST 2084），`src/Gloam.Core/TransferFunctions.cs:46,51-69`：

```
L = ( max(N^(1/m2) − c1, 0) / (c2 − c3 · N^(1/m2)) )^(1/m1) · 10000      （N ∈ [0,1] → nits ∈ [0,10000]）
N = ( (c1 + c2 · L^m1) / (1 + c3 · L^m1) )^m2                             （PqInverseEotf）
```

其中 `m1 = 2610/4096/4 = 0.1593017578125`、`m2 = 2523/4096·128 = 78.84375`、`c1 = 3424/4096 = 0.8359375`、`c2 = 2413/4096·32 = 18.8515625`、`c3 = 2392/4096·32 = 18.6875`（`TransferFunctions.cs:22-35`）。sRGB 编解码是标准两段式（`TransferFunctions.cs:136-158`）：线性段 `12.92·L`，幂段 `1.055·L^(1/2.4) − 0.055`，阈值 0.0031308 / 0.04045。BT.1886 是 `gamma = 2.4` 的 white/black 锚定形式（`TransferFunctions.cs:164-214`），黑位为 0 时退化成 `white · signal^2.4`。

`TransferFunctions` 的每个入口都先过 `ClampFinite`（非有限值回退、越界夹取、`whiteLevel <= blackLevel` 直接返回 0），这是让用户输入/测量垃圾值不会把 LUT 变成 `NaN` 的防线。`GenerateLut` 还会校验 `sdrWhiteLevel > 0`（`LutGenerator.cs:605-610`）。

**Grey 通道的语义**是这条管线里最容易被误解的一处：它不是三个编码值的平均，而是"产生平均线性光所需的那条信号"（注释标为 E6）——SDR 分支是 `Math.Pow((r+g+b)/3.0, 1.0/2.2)`（`LutGenerator.cs:192-195`），HDR 分支是 `PqInverseEotf((outputR+outputG+outputB)/3.0)`（`LutGenerator.cs:286`）。两者的平均值都取在**线性光**上。CLI 的 `lut`/`check-lut` 把 `Grey` 当第四通道单独校验（§7.4.3）。

**HDR headroom 的 constant-Y boost**（`PreserveNightLuminance` 开启时）是另一处有公式的代码路径。目标很具体：暖色偏移会降低亮度，用户选择"保亮度"后要把这部分补回来，但补只能在面板真实 headroom 内补。做法是先求"最亮调整后白通道"作为锚点 `boostAnchorNits = max(wR,wG,wB) · sdrWhiteLevel`，且仅当它高于普通调暗后的白点才启用（`LutGenerator.cs:214-228`）；随后把 `[sdrWhite, 10000]` 的直通区间用幂重映射压到 `[anchor, 10000]`，两端精确、中间单调（`LutGenerator.cs:331-344`）：

```
gammaPrime = ln(10000 / anchor) / ln(10000 / sdrWhiteLevel)
remapped   = anchor · (linearNits / sdrWhiteLevel)^gammaPrime
headroom   = PqInverseEotf(min(remapped, 10000))
```

注释说明了这个选择的语义：constant-Y 重新定义了 diffuse white 更亮，所以高光 headroom 真的变窄；保留的是"等级的排序"，PQ 顶端仍然落在真实直通。`anchor` 已经包含亮度调暗，因此不再叠第二次调暗（`LutGenerator.cs:333-341`）。上限由 `ApplyDoseCeiling` 之外的 `NightLuminanceCeiling` 控制：HDR 取 `min(2.0, HdrPeakNits/SdrWhiteLevel)`，SDR 固定 1.0（见 §7.2.3）。

SDR 白点以上为什么必须"只淡出修正量"而不把 SDR 值夹到白点：注释给了两个物理理由——夹取会压平高光下段；而 `Gamma22/24` 在纯 gamma 情况下残余修正为零，因此 HDR 高光保持**逐级恒等**（`LutGenerator.cs:288-315`、`ComputeHeadroomTarget` 注释 `327-357`）。

**校准 LUT 分支（`GenerateCalibratedLut`）**：当显示器有实测 profile 时，`DispwinRunner` 改为调这条重载。它有三份签名，语义不同（`LutGenerator.cs:371-427`）：
1. `GenerateCalibratedLut(double targetGamma, DisplayCalibrationProfile profile, ...)`：把 profile 转成 `DisplayCharacterization`，服务"用户偏好 regrade 路径"（GammaMode → 2.2/2.4 的裸幂）。
2. `(double targetGamma, DisplayCharacterization, ...)`：同上但直接收 characterization。
3. `(CalibrationTarget target, DisplayCharacterization, ...)`：**校准流程的权威路径**，用 `target.ApplyEotf` 作为目标 EOTF（BT.1886 with measured black、piecewise sRGB 等），保证闭环校正器、验证器、安装的 LUT 对"什么是 on-target"理解一致（注释在 `LutGenerator.cs:405-412`）。

共享实现 `GenerateCalibratedLutCore` 的步骤（`LutGenerator.cs:433-603`）：`targetEotf` 把 SDR 信号 `[0,1]` 映射成相对线性输出 → 线性域做用户调整（SDR 用 sRGB 基底）→ **对实测 tone curve 求逆**（`measuredR.InverseLookup(fitR)`；tone curve 是在黑位扣除后的归一化亮度上拟合的，逆查必须用同样的归一化域，否则阴影误差最大）→ PQ 编码 → 白点以上套 `ComputeHeadroomTarget` 并只淡出残余。SDR 分支不做 PQ，直接用 `measured*.InverseLookup` 得到要发送的信号（`LutGenerator.cs:470-509`）。

两条容易忽略的约束：

- 这条路径上 `NightLuminanceCeiling` 被**强制压回 1.0**：tone curve 只在 `[black, measured white]` 区间拟合，`InverseLookup` 无法可靠外推到白点以上，所以实测校准路径不启用 constant-Y headroom boost（`LutGenerator.cs:447-456`）。
- 校准 LUT **不进 `_lutCache`**：缓存只包住 `GenerateLut`（`_lutCache` 的全部引用出现在 `LutGenerator.cs:29,84,112,120,125`），带 profile 的生成每次都真算。这是有意的——profile 对象可能被重新生成/替换，缓存反而会成为正确性负担；`DispwinRunner` 的 `_lastApplied` 去重已经承担了"值没变就别写"的职责。

#### 7.2.3 策略解析：`GammaApplyService.RequestApply`

`RequestApply` 是**唯一**的 live apply 入口，它的注释说明为什么必须收口：滑块、夜间模式、应用排除、显示变更/resume 重放全部经过这里，因此一次 bypass 检查就能覆盖所有 live 路径（`src/Gloam.Core/GammaApplyService.cs:331-340`）。解析顺序（同文件 345-444）：

1. **校准旁路检查**：`CalibrationStateManager.IsDeviceInBypass(monitor)` 为真则直接返回并记日志。
2. **快照策略状态**（`_policyLock`）：该显示器的游戏会话 `_activeGamerSessions[monitor.HMonitor]`、是否被"应用排除"挡掉夜间模式 `_blockedMonitors.Contains(monitor.HMonitor)`。先同步取快照，是因为合流后的硬件工作可能晚几秒才跑，而策略必须反映"请求这一刻"的状态。
3. **显式预览优先**：`manualCalibration != null || nightKelvinOverride != null` 时忽略游戏会话，让设置窗口的预览不被前台游戏"抢走"（`GammaApplyService.cs:353-357`）。
4. **色温合成**：`nightKelvinOverride ?? _nightModeService.CurrentNightKelvin`；被应用排除的显示器强制 6500K；游戏会话按 `ResolveGamerKelvin` 决定 `ForceDaylight → 6500`、`NightOps → min(基线, NightOpsKelvin)`（不把已经更暖的日程"调冷"）、`GameplayLock` 用会话开始时锁定的 kelvin（`GammaApplyService.cs:600-615`）。游戏 profile 的 `OverrideGamma` 可覆盖 `GammaMode`，`NightOps` 时重建成一套独立的 `NightModeSettings`（`BuildNightOpsSettings`，`GammaApplyService.cs:617-653`）。
5. **全局热键是最终权威**：`NightModeManuallyDisabled` 为真时强制 6500K，能从任何 Night Ops 游戏 profile 里恢复中性输出（`GammaApplyService.cs:373-375`）。
6. **校准设置来源**：手动传入则 `Clone()`（防止夜间模式/偏移的后续改写泄漏回调用方对象）；否则读持久化的 `MonitorProfileData.ToCalibrationSettings()`，`ToCalibrationSettings` 缺失时用 `new MonitorProfileData()` 默认值（`GammaApplyService.cs:387-397`）。
7. **夜间模式合成**：`ApplyNightModeToCalibration` 在 **mired 空间**叠加夜间偏移（`(currentKelvin − 6500) / 70`），而不是线性 Kelvin 差值——同样的刻度距离在暖端是更大的感知步长（`GammaApplyService.cs:586-598`）。同时写入算法、UltraWarm、感知强度、保亮度标志，加载 CCSS melanopic 系数，并按显示器计算 `NightLuminanceCeiling`：HDR 且面板峰值 > SDR 白点时取 `min(2.0, HdrPeakNits / SdrWhiteLevel)`，否则 1.0（`GammaApplyService.cs:405-423`）。这是"夜间模式在 HDR 上允许用真实 headroom 补回暖色损失，SDR 上 GDI ramp 不能超过 1.0"的落点。
8. **色温范围**：`Temperature` 夹到 `CalibrationSettings.MinimumTemperatureScale..MaximumTemperatureScale`（约 1900K..10000K 的扩展刻度，`CalibrationSettings.cs:12-13,427-430`）。
9. **持久化**：只有非预览 apply 才写 `monitor.CurrentGamma` 和 `_settingsManager.SetProfileForMonitor`——滑块拖动不会反复写 settings.json（`GammaApplyService.cs:432-440`）。
10. **发布快照** `PublishSnapshot`（供 melanopic dashboard 消费，见 §7.5.2），然后 `_coalescer.Submit(monitor.HMonitor, ...)`。

`ApplyAll(IEnumerable<MonitorInfo>)` 是批量版本：读取 `CurrentNightKelvin`、按 monitor profile 的 `GammaMode` 逐台调 `RequestApply`（`GammaApplyService.cs:655-668`）。`ClearAll` 是 panic 路径：逐台 `_dispwinRunner.ClearGamma`，单台失败只记日志、不抛（`GammaApplyService.cs:670-681`）。

**剂量上限（`ApplyDoseCeiling`）**：夜间模式还允许用户设一个 melanopic EDI 上限（mel-lx）。`RequestApply` 在夜间分支里会把它交给 `CircadianDoseGovernor.Solve(...)`：以 CCSS 光谱（缺失时用 `MelanopicCalculator.GenericPrimaries(wideGamut: monitor.IsHdrActive)` 兜底）、算法、感知强度、UltraWarm、保亮度、**已合成后的有效 kelvin**、亮度、SDR 白点、视角立体角为输入，求一个"感知代价最小且满足上限"的 (kelvin, brightness) 工作点；解出来就把 `Temperature` 改写成 `(solution.Kelvin − 6500)/70`、`TemperatureOffset` 折零、`Brightness` 改成解值（`GammaApplyService.cs:532-577`）。三个设计细节：

- 每台显示器单独求，因为剂量取决于该面板的光谱、白点与亮度。
- 解不出（`!solution.Adjusted`）就保持原计划；求解抛异常只记日志，"最坏情况是未治理的日程状态照常应用"（`GammaApplyService.cs:578-583`）。
- 日志按 `(kelvin, brightness, ceilingMet)` 去重、每显示器只留最后一条 key，因为淡入期每秒可能求解多次（`GammaApplyService.cs:566-572`）。

**快照的消费者（`MelanopicMonitorService`）**：`StateApplied` 的唯一订阅者。它的注释记录了一次真实性能事故：如果把 melanopic 数学和 JSONL 追加放在 apply 路径的回调里，夜间淡入时**每次 tick 都在 UI 线程做计算和磁盘写，会把整个系统冻住**（`src/Gloam.Core/MelanopicMonitorService.cs:41-46`）。因此设计成：回调只把最新快照塞进 `_pending` 并 `Set` 一个 `AutoResetEvent`，独立后台线程（`Name = "MelanopicWorker"`）合并处理；磁盘采样另有节流——`KeepaliveMinutes = 5.0` 的保活定时器让剂量曲线在长时间静态时也前进，但每台显示器两次落盘至少间隔 `MinAppendIntervalSeconds = 60.0`，所以 30 分钟淡入约写 30 行而不是几千行（`MelanopicMonitorService.cs:52-65`）。同一文件里也做了 bypass 检查（`MelanopicMonitorService.cs:152`）。

#### 7.2.4 合流与限速：`LatestValueCoalescer`

`LatestValueCoalescer<TKey, TValue>` 是为"拖动滑块快过硬件写入"设计的（`src/Gloam.Core/LatestValueCoalescer.cs`）。文件头写明三条保证：每次 `Submit` 至少触发一次带"该次或更新值"的调用（不丢更新）；每 key 最多一个 work 并发；work 拿到的值是该 key 出队时最新的值（`LatestValueCoalescer.cs:13-20`）。

实现模型（`LatestValueCoalescer.cs:21-31,146-262`）：

- 每 key 一个 `Slot`：一把 0/1 `SemaphoreSlim` 门、锁保护的 `Pending/HasPending`、`ActiveCts`、`RunnerActive`、`LastWorkTicks`。
- 生产者 `Submit`：`_lifetimeLock` 下取 slot、`slot` 锁下换 `Pending`、取消当前 `ActiveCts`，然后 `TryStartRunner`（拿不到门说明已有 runner，直接返回）。**同一 key 的旧 work 会被取消**，不同 key 互不影响。
- runner 循环：检查 disposed → 无 pending 直接返回（不惩罚单次 apply）→ 若 `minIntervalMs > 0` 且距上次完成不足则 `Thread.Sleep` 补足（**持门睡眠**，期间生产者只更新 Pending，不丢值）→ 取走 pending 并新建 CTS → 调 work → 记录 `LastWorkTicks`。
- 退出时在 `finally` 里再检查一次 pending：生产者的更新如果落在"runner 最后一次空检查"与"释放门"之间，会看到门被占而放弃启动，必须由 runner 自己 re-kick，否则更新被孤儿化（`LatestValueCoalescer.cs:183-192`）。
- `Dispose` 两阶段：置 `_disposed`、清空 `_slots`、取消所有 pending/active CTS；**空闲 slot 由 Dispose 释放，活跃 slot 由 runner 的 finally 释放**，避免释放一把仍被 `Wait` 的 semaphore（`LatestValueCoalescer.cs:264-291`）。

`GammaApplyService` 用 `minIntervalMs: 250`（`GammaApplyService.cs:94-98`）。注释解释了为什么这是安全边界：夜间模式淡入只需要每秒几次写入，而任何失控触发（比如显示变更反馈循环）都不可能把 `SetDeviceGammaRamp` 打到足以卡住合成器的频率。work 回调里还做了三件事：`cancellationToken` 检查、**coalescer 时 bypass 复查**（TOCTOU 收口），然后以 `skipIfBypassed: true` 调 `ApplyGamma`，让 `DispwinRunner` 在真正系统调用前再查一次（`GammaApplyService.cs:65-98`）。

`Cancel(key)` 是给"立即安全边界"用的：取消活动 work 并丢弃 pending，且后续 `Submit` 仍然有效。Game 模式暂停走这条路，防止一个已经稳定的旧前台候选在暂停后复活（`LatestValueCoalescer.cs:105-123`）。

`Submit` 的一个静默语义：dispose 之后再提交**不抛异常，直接丢弃**（`Submit` 在 `_lifetimeLock` 内与 slot 锁内各查一次 `_disposed`，`LatestValueCoalescer.cs:86-103`）。这是为了关停竞态下的安全（不会对已释放的 semaphore `Wait`），代价是调用方不能靠"提交成功"判断"一定执行"——真正的确认信号是 `StateApplied` 或日志。

#### 7.2.5 下发：`NativeGammaRamp` 优先，`dispwin` 兜底

`DispwinRunner.ApplyGamma(monitor, mode, whiteLevel, calibration, calibrationProfile, skipIfBypassed, cancellationToken)` 是下发的唯一实现（`src/Gloam.Core/DispwinRunner.cs:279-426`），顺序是：

1. 取消检查 → 校准 profile 可用性检查（`LutGenerator.CanUseCalibratedLut` 失败就丢弃 profile，退回普通 LUT）。
2. 生成 LUT：有 profile 走 `GenerateCalibratedLut(targetGamma, characterization, calibration, whiteLevel, monitor.IsHdrActive)`，否则 `GenerateLut(...)`。
3. **去重**：`_lastApplied[monitor.DeviceName]` 与本次三个通道逐项比较（`LutsEqual`），完全相同则直接返回。注释说明这不是无操作：每次 dispwin 启动都会重写 GPU ramp，用户能看到闪烁，所以能跳就跳（`DispwinRunner.cs:335-343`）。
4. **bypass 检查**（`skipIfBypassed && IsDeviceInBypass`）。
5. **原生快路径** `NativeGammaRamp.TryApply(monitor.DeviceName, lutR, lutG, lutB)`，成功则 `RecordApplied` 返回——不需要 Argyll。
6. **兜底**：`EnsureConfigured()` 找 `dispwin.exe`（找不到时触发 `DispwinUnavailable` 事件，托盘弹出"请安装 ArgyllCMS"），把 LUT 写成 Argyll `.cal` 文本（`GenerateCalContent`，`CAL` 头 + `RGB_I RGB_R RGB_G RGB_B` 四字段 + 1024 组数据，`DispwinRunner.cs:588-618`），临时文件名 `HDRGamma_{GUID:N}.cal`、`FileMode.CreateNew + FileShare.None` 防竞态，然后 `dispwin -d <index> <cal>`；finally 删临时文件。失败时从 `_lastApplied`/`_deviceNameToPath` 移除，确保下次不会因为"看起来已应用"而跳过。

**dispwin 的发现与显示编号**：`FindDispwin` 不查 `PATH`、不查当前目录，而是走与色度计工具共享的 `ArgyllPathFinder.FindArgyllToolPath("dispwin.exe")`——只接受同时包含 `spotread.exe` 与 `dispwin.exe` 的完整 bin 目录，避免被同名程序劫持（`DispwinRunner.cs:221-234`）。显示编号也不能假设 `OutputId + 1`：dispwin 按自己的 GDI 枚举编号，多适配器系统上会错位。`GetDisplayIndexMap` 启动 `dispwin -?`（退出码 1、输出在 stderr），用正则 `^\s*(\d+)\s*=\s*'([^,']+),` 解析形如 `1 = 'DISPLAY1, at 0, 0, width 2560, height 1440 (Primary Display)'` 的行，按 GDI 名匹配；5s 超时则杀进程，解析失败也**缓存空 map** 以免每次 apply 重查，由 `InvalidateAppliedState()` 清掉重试（`DispwinRunner.cs:159-213`）。`RunDispwin` 并发读 stdout/stderr（顺序 `ReadToEnd` 会在另一根管道写满时死锁）、5s 硬超时、`Kill(entireProcessTree: true)`，超时/取消都返回 false 而不是抛（`DispwinRunner.cs:523-586`）。

`NativeGammaRamp` 的核心不是 P/Invoke 本身，而是 **GDI 的 gamma 校验包络**（`src/Gloam.Core/NativeGammaRamp.cs:16-27,59-105`）：

- Windows（win32k，不是驱动）在未设置 ICM `GdiIcmGammaRange` 覆盖时，会校验 ramp 每一项的**高字节**：`|(ramp[i] >> 8) − i| ≤ 128`。任一项越界，**整个调用被拒绝、什么都不生效**。源码注释记录了实测结论：单调性不是必要条件，边界是逐项、以索引为锚的。
- 这就是夜间模式在 SDR 上曾经"静默无效"的原因：2700K 把蓝通道压到约 0.21 线性，gamma 编码后顶端只到满量程的 0.489，比 index 255 允许的 0.496 差一点，于是整条 ramp 被拒。HDR 路径不会撞上，因为 PQ LUT 在高光段接近恒等（headroom 直通）。
- 默认策略是**夹到包络内**：`BuildRampChannel(lut, applyGdiEnvelope: !IsGammaRangeUnlocked())`，把每项 `Math.Clamp(word, GdiEnvelopeLower(i), GdiEnvelopeUpper(i))`。理由是"应用最接近的可接受 ramp"好过"整条被拒、屏幕完全不变"，代价是极端通道的末端会被掰弯（`NativeGammaRamp.cs:67-75`）。
- 只有注册表 `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM\GdiIcmGammaRange = 0x100` 才解锁完整范围；进程内缓存（改值要重新登录才生效），探测失败按"锁定"处理。每个显示器**只在第一次真的被夹时**记一条日志（`ConcurrentDictionary EnvelopeLoggedByDevice`），因为淡入每秒会 apply 多次，不能刷屏。
- LUT → ramp 是线性插值重采样（任意长度 ≥ 2 → 固定 256 项 16 位），与 dispwin 加载 1024 点 `.cal` 时做的事一致；`TryRead` 读回硬件 ramp；`TryClear` 写恒等 ramp `i * 257`（0..65535 的 256 等步长）。

#### 7.2.6 另一条腿：WCS / MHC2 profile

GPU ramp 是"每帧都要活的修正"，MHC2 是"装一次、由合成器执行的面板修正"。两者不是替代关系：`MonitorProfileData.Mhc2ProfileName` 存在意味着 Windows 已经在合成器做完整的色域+色调修正，因此 GPU ramp **不能再叠一条色调曲线**（会双重 gamma），只应在其上叠夜间暖色（`src/Gloam.Core/SettingsManager.cs:89-95`）。

MHC2 文件的生成/安装分两层：

- **`ProfileTemplatePatching.PatchProfile(templatePath, outputPath, newLut)`**（`src/Gloam.Core/ProfileTemplatePatching.cs`）：读 ICC v4 模板（128 字节头 + 标签表），按 12 字节条目找签名 `'mhc2'`（`0x6D686332`）的私有标签；标签内用"找 1024 计数（大小端都试）+ 前 64 项里至少 58 项非降"的双重启发式定位 LUT 起点（`FindLutStart`/`LooksLikeLut`，`ProfileTemplatePatching.cs:87-121`）；把 1024 个 `[0,1]` 值写成 16.16 定点，`1.0` 饱和到 `0xFFFF`（`65536` 会溢出）；最后**清零 84-99 字节的 profile ID**，否则做 MD5 校验的消费者会拒绝被改过内容的 profile。文件头有 `MaxTagCount = 4096` 和边界检查，防止恶意/损坏 profile 把读取带出文件。
- **`ProfileManager.ApplyProfile(monitor, mode, whiteLevel)`**（`src/Gloam.Core/ProfileManager.cs:21-87`）：生成 LUT → 文件名 `HDRGamma_{SHA256(MonitorDevicePath)[:8]}_{mode}_{whiteLevel:F0}.icm` → 补丁写到 `%TEMP%` → `Wcs.InstallColorProfile` → `Wcs.AssociateColorProfileWithDevice` → `WcsSetDefaultColorProfile(CURRENT_USER, CPT_ICC, CPST_PERCEPTUAL)` → finally 必删临时文件。

**一个可核对的事实**：`ProfileManager` 在 DI 里注册了（`src/Gloam/Shell/App.xaml.cs:34`，模板路径 `AppDomain.CurrentDomain.BaseDirectory + "srgb_to_gamma2p2_100_mhc2.icm"`），但全仓 `*.cs` 的 `\.ApplyProfile(` 与 `new ProfileManager(` 检索只命中这一处 DI 注册——`ProfileManager.ApplyProfile` **没有调用点**。`TrayViewModel` 里有一个同名但无关的私有方法 `ApplyProfile(MonitorInfo, GammaMode)`，只是 `RequestApply` 的包装（`TrayViewModel.cs:707-713`），不要混淆。现役安装链路是 `CalibrationProfileInstaller.Install(...)`：它按 SDR 白点就近选模板 `srgb_to_gamma2p2_{100|200|300|400}_mhc2.icm`（`FindTemplate`，`src/Gloam.Core/Calibration/CalibrationProfileInstaller.cs:767-791`），SDR 用 `AssociateColorProfileWithDevice + WcsSetDefaultColorProfile`，HDR/Advanced Color 用 `ColorProfileAddDisplayAssociation(..., associateAsAdvancedColor: true)` + `ColorProfileSetDisplayDefaultAssociation(..., CPST_EXTENDED_DISPLAY_COLOR_MODE, ...)`。**推断**：`ProfileManager` 是早期"每换一次 gamma 就装一个 profile"的遗留路径，已被校准安装器取代；细节在校准章。

#### 7.2.7 恢复触发：谁把 ramp 修回来

| 触发 | 入口 | 行为 |
|---|---|---|
| 启动 | `TrayViewModel` 构造末尾 `ApplyAll()`（`src/Gloam/Features/Tray/TrayViewModel.cs:167-168`） | 按持久化 profile 逐台 apply |
| 显示配置变化 | `MainWindow.WndProc` 的 `WM_DISPLAYCHANGE (0x007E)` → `HandleDisplayChange()`（`src/Gloam/Shell/MainWindow.xaml.cs:75-84`） | 1500ms settle 去抖后重放 |
| 从睡眠恢复 | `WM_POWERBROADCAST + PBT_APMRESUMEAUTOMATIC (0x0012)` → `HandleResume()`（同文件 85-91） | 3000ms settle 去抖后重放 |
| 外部改写 ramp（全屏游戏、驱动重置） | `GammaApplyService` 的 `System.Timers.Timer(10000)`（`src/Gloam.Core/GammaApplyService.cs:100-106`） | 每 10s `DispwinRunner.VerifyAndRestoreRamps()` |
| 用户 panic | Win+Shift+F4 热键 → `TrayViewModel.PanicAll()`（`TrayViewModel.cs:490,501-507,770-799`） | 见下 |

显示事件重放不是"直接 ApplyAll"，中间有四个必要步骤（`TrayViewModel.HandleDisplayEventAsync`，`TrayViewModel.cs:595-636`）：

1. **去抖**：`_displayEventSeq` 自增 + `Task.Delay(settleDelayMs)`，醒来时序号不是最新就放弃。注释说明原因：HDR 模式切换期间 Windows 会连发 WM_DISPLAYCHANGE，每次事件都排一轮 `RefreshMonitors+ApplyAll` 会看到闪烁（`TrayViewModel.cs:595-599`）。
2. `_applyService.InvalidateAppliedState()`：清空 `_lastApplied` / `_deviceNameToPath` / `_unverifiableDisplays` / dispwin 显示编号缓存，否则"LUT 相同就跳"的去重会让重放变成空操作（`DispwinRunner.cs:39-51`）。
3. `RefreshMonitors()` → `_profileModeService.ReconcileModeChanges()`（必须在 ApplyAll 之前，apply 路径要读当前 MHC2 profile）→ `_appDetectionService.Refresh()`（HMONITOR 与输出事实可能已变，Gameplay Lock 要跟着刷新）→ `_nightModeService.UpdateSettings(_settingsManager.NightMode)`（睡眠跨过调度触发点后，缓存的 kelvin 可能是几小时前的，必须强制立刻重算）。
4. 最后 `ApplyAll()`。

**启动时的时间线**（从进程启动到第一次 ramp 写）同样有顺序契约（`src/Gloam/Shell/App.xaml.cs:85-159`）：

1. `AppPaths.MigrateLegacyData()`——必须在 `Log.Initialize()` 之前，否则新目录已存在、迁移失败。
2. `Log.Initialize()`——之后所有日志才可能落盘。
3. `ResourceExtractor.ExtractIcmProfiles()`——把内嵌 ICC 模板解压到程序目录（只读时落 `AppPaths.DataDir`）。
4. `ConfigureServices()`（DI 容器）——`SettingsManager` 的构造函数即 `Load()`，此时 settings.json 已解析/迁移/夹取完毕。
5. `AdvancedColorProfileMigrationService.Run()`——**在托盘与 apply 路径之前**跑：修复旧 HDR characterization，并验证 Windows 真的在查包含该 profile 的那条关联列表，保证"第一帧"的设置与合成器修正一致（`App.xaml.cs:151-155`）。
6. `MainWindow`（隐藏的消息窗口）→ `TrayViewModel` 构造：`RefreshMonitors()` → `NightModeService.Start()` / `AppDetectionService.Start()` → `ApplyAll()` → 注册热键 → 更新检查（`TrayViewModel.cs:162-177`）。

任何一步抛异常都会走 `Log.Error("CRITICAL STARTUP ERROR")` + `Shutdown(-1)`——启动路径没有"部分可用"模式（`App.xaml.cs:163-169`）。

**10s ramp guard** 的实现（`DispwinRunner.VerifyAndRestoreRamps`，`DispwinRunner.cs:53-145`）值得单独看，因为它处理了三种反直觉情况：

- **不可校验的显示器**：有的驱动对写入做变换，读回永远不等于写入值。第一次发现后标记 `_unverifiableDisplays`、记一条 error、以后完全跳过——否则会每 10s 恢复一次、闪一次。
- **读不出来就当匹配**：`NativeGammaRamp.TryRead` 失败时 `RampMatchesApplied` 返回 true，"绝不和看不见的显示器对抗"。
- **容差**：比较时允许 `RampToleranceCounts = 512`（约满量程 0.8%）。10-bit 硬件会量化 ramp 项，而真实的外部覆写（identity 重置、其他夜间软件）偏差远大于此。
- **TOCTOU**：读到快照后、写回前，coalescer 可能已经换上新 LUT 并写进硬件。恢复后要再查 `_lastApplied` 是否已变；变了就以新值为准（且再查一次 bypass），避免用旧 ramp 覆盖新 ramp 造成下一 tick 又"自愈"一次闪烁（`DispwinRunner.cs:98-113`）。

**panic** 是产品级的"放我出去"按钮：取消待定前台策略、`NightModeManuallyDisabled = true`、清空应用排除与游戏会话、关闭或紧急挂起游戏模式、`ClearAll` 把所有显示器 ramp 恢复恒等、关闭 GameLab 窗口，最后弹一条非模态 toast 说明当前状态（`TrayViewModel.cs:770-799`）。`ClearGamma` 先走 `NativeGammaRamp.TryClear`，失败才 `dispwin -c`（`DispwinRunner.cs:475-490`）。另外每次 `ApplyAll` 都会调 `EnsureWindowsNightLightDisabled()`（60s 节流、可被 `AllowWindowsNightLight` 关掉），因为 Windows Night Light 会和 Gloam 在同一管线里叠加第二层暖色（`TrayViewModel.cs:732-768`）。

#### 7.2.8 校准期 bypass 的双点检查（真实是四点）

校准要测"面板原始响应"，因此测量期间必须把 Gloam 的所有修正撤掉；但滑块/夜间模式/resume 重放随时可能把修正写回来。`CalibrationStateManager` 用**静态旁路注册表 + 同一个 key 推导函数**解决（`src/Gloam.Core/Calibration/CalibrationStateManager.cs`）：

- `BypassKey(monitor)`：优先 `MonitorDevicePath`，其次 `DeviceName`，最后 `"unknown"`。注释明确：注册与所有查询**必须**用同一个 helper 推导 key，否则 `MonitorDevicePath` 为空的显示器会注册在一个 key、查询在另一个 key，旁路守卫静默失效（`CalibrationStateManager.cs:53-67`）。
- `EnterBypassMode`：**先注册、后清 ramp**。反过来的话，一个并发的 live apply 可以落在"清 ramp"与"注册"之间，把修正写到 identity 上（`CalibrationStateManager.cs:120-125`）。注册后 `_dispwinRunner.ClearGamma(monitor)`；清失败则回滚注册并抛。
- `RestorePreviousState`：逐个恢复保存的 (mode, settings)，原本就是默认态的显示器**也要**显式清成 identity（闭环可能留了最后一个候选修正），然后恢复夜间模式、注销旁路。

查询点（"双点"实际是四点串联）：

1. `GammaApplyService.RequestApply` 入口（`GammaApplyService.cs:336-340`）。
2. coalescer 的 work 回调里，`DispwinRunner.ApplyGamma` 之前（`GammaApplyService.cs:73-81`）。
3. `DispwinRunner.ApplyGamma` 内、`SetDeviceGammaRamp` 之前，仅当 `skipIfBypassed: true`（`DispwinRunner.cs:345-355`）。注释说明：这是为了关掉"调用方检查"与"系统调用"之间的窗口。
4. ramp guard 每 tick 也对 `_deviceNameToPath` → `BypassKey` 查一次，避免把校准中的 identity 又"恢复"回旧 ramp（`DispwinRunner.cs:80-87`）。`MelanopicMonitorService` 也会跳过旁路中的显示器（`src/Gloam.Core/MelanopicMonitorService.cs:152`）。

校准自己的写（`ApplyCorrectionLut`）与退出时的恢复（`RestorePreviousState` 调 `ApplyGamma` 用默认 `skipIfBypassed: false`）不会被跳过——这是"旁路只挡 live 路径"的设计（`DispwinRunner.cs:349-350`）。

### 7.3 设置与基础设施

#### 7.3.1 `SettingsManager`：门面、锁与两条"拒绝写"规则

`SettingsManager` 是 Core 里最大的文件（~1.4k 行），但职责单一：**把 `settings.json` 包成强类型属性 + 变更方法**。文件位置 `AppPaths.DataDir/settings.json`（`src/Gloam.Core/SettingsManager.cs:363-365`），当前 schema `CurrentSchemaVersion = 4`，单文件上限 `MaxSettingsFileBytes = 16 MiB`（`SettingsManager.cs:359-360`）。

并发模型写在字段注释里（`SettingsManager.cs:367-376`），值得原样记住：

- `_dataLock` 保护 `_data` 与所有组合读；UI 线程、夜间模式定时器的线程池线程（经 `Dispatcher.Invoke` → `RequestApply`）、后台校准路径都会访问。
- `_saveLock` 串行化写文件；两个锁**分开**，是为了让慢磁盘 I/O 不阻塞 UI/设置读取。
- `_dataVersion` 单调递增；`Save()` 在锁内取 json+版本快照，落到 `_saveLock` 后再对比版本，不一致就重新序列化——防止两个重叠 `Save()` 里旧快照覆盖新状态（`SettingsManager.cs:608-628`）。
- **返回可变对象的读接口一律给副本**（`GetMonitorProfile`、`ExcludedApps` 返回 `Clone()`），否则调用方可以在锁外改共享状态（`SettingsManager.cs:373-374`）。

`Save()` 有两条"拒绝写"规则，都是数据安全边界（`SettingsManager.cs:578-635`）：

1. **NewerSchema 保护**：`SettingsFileFromNewerVersion` 为真时直接返回 false 并记日志。旧版本二进制绝不能覆盖新版本写的文件。
2. **解析失败保护**：`LoadFailedPreservingFile && _dataVersion == _dataVersionAtLoad` 时拒绝写。文件解析失败后内存里是默认值，此时保存等于用默认值覆盖用户文件；只有某项设置真的变了（版本前进）才允许落盘。

门面覆盖面（按方法名前缀归类）：显示器 profile（`Get/SetProfileForMonitor`、`Get/SetMonitorProfile`）、夜间模式、MHC2 记账（`SetMhc2Calibration`、`ForgetMhc2Profile`、`SetActiveMhc2Profile`、`SetMhc2ModeSlot`、`RecordDisplacedAdvancedColorProfile`、`TryReplaceMhc2Calibration`、`HasMhc2Calibration`）、应用排除、窗口矩形、UI 折叠状态、游戏 profile、语言/主题/提醒、备份（`TryCreateBackup`）。校准 profile 的**大对象**不放在 settings.json：它们在 `DataDir/CalibrationProfiles/<guid>.json`，ID 必须是 GUID 才允许读写删（`SettingsManager.cs:1215-1234`），列表按 `CalibratedAt` 倒序。

**对外事件**（四个，订阅者都在应用层）：

- `NightModeChanged`：`SetNightMode` 在 `Save()` 之后、`_dataLock` **之外**触发；注释写明原因——handlers 可能回调 settings，锁内触发会死锁（`SettingsManager.cs:741-751`）。
- `GamerProfileUsed` / `GamerSettingsChanged` / `MonitorProfileChanged`：分别对应使用记录、游戏模式开关与 profile 写入、显示器 profile 变更；三者都包在 try/catch 里，订阅者异常不外溢（`SettingsManager.cs:1139,1151,1157`）。`GamerModeCoordinator.OnMonitorProfileChanged` 靠最后一条在会话期间立即重评估。

事件是**在调用者线程上同步 `Invoke`** 的，没有 Dispatcher 包装；因此订阅者里的 UI 操作必须自己回投（`TrayViewModel` 统一走 `OnUiThread`）。

#### 7.3.2 `SettingsPersistence`：原子替换与格式探测

`SettingsPersistence` 是 `internal static`，是所有 settings I/O 的唯一实现（`src/Gloam.Core/SettingsPersistence.cs`）。`Load` 返回 `LoadResult(Data, FileExists, ParseFailed, NewerSchema, MigratedLegacy)` 五元组，把"文件出了什么事"显式交给上层决策。流程（`SettingsPersistence.cs:19-96`）：

1. 文件不存在 → 返回默认 `SettingsData`。
2. 先卡 `FileInfo.Length > maximumBytes`，再用 `TextFileStore.ReadBounded` 带上字节/字符双上限读取。
3. **先读 `SchemaVersion` 再反序列化**：字段形状可能变，注释写明"即使 best-effort 加载失败，也绝不能让未来的文件变成可写"（`SettingsPersistence.cs:41-49`）；比较用 `OrdinalIgnoreCase`，所以手写的 `schemaVersion` 也算。
4. **旧版 `ExcludedApps` 就地转换**：早期是字符串数组，现在必须变成 `{AppName, FullDisable}` 对象。注释明确不能"整份 DTO 重建"，那会静默丢掉所有其他设置（`SettingsPersistence.cs:51-67`）。
5. 反序列化用 `PropertyNameCaseInsensitive` + `TolerantJsonStringEnumConverter`；随后 `SettingsMigration.Apply` → `SettingsNormalization.Validate`。
6. 解析异常：把原文件复制成 `settings.json.bak-{ticks}` 再回退默认值，标 `ParseFailed = true`——"保留原文件"是这里的第一原则（`SettingsPersistence.cs:75-81,127-131`）。
7. 写：`Serialize`（缩进 + 宽容 enum）→ `TextFileStore.WriteAtomic`。
8. 备份：`TryCreateBackup` 把 label 过滤成字母数字/`-_.`，文件名 `settings.json.{label}-{yyyyMMdd-HHmmss}.bak`，`overwrite: false`。

#### 7.3.3 `SettingsNormalization` / `SettingsMigration`：把不可信输入夹回域内

两个类都在 `src/Gloam.Core/SettingsNormalization.cs`，都是 `internal static`，**无文件系统副作用**——加载、导入、测试共用同一套策略（文件头注释）。

- `SettingsMigration.Apply` 只有一个迁移：schema ≤ 3 的 Night Ops profile 里 `NightOpsMelanopicCeiling == 10.0`（旧版强制 10 mel-lx 上限）改成 `0.0`（新版改为 opt-in），显式选择过的值保留（`SettingsNormalization.cs:7-25`）。
- `GamerProfiles`：先 `Sanitized()`，丢掉空 `AppName`，用 `GamerExecutableSafety.IsSafeProfileTarget` 拒绝不安全目标（防把 `explorer.exe` 之类系统进程当游戏），同名（忽略大小写）后者覆盖前者。
- `ExcludedApps`：`AppExclusionRule.NormalizeAppName` 取 basename、补 `.exe`；重复项合并 `FullDisable |= rule.FullDisable`（`SettingsNormalization.cs:59-85`）。
- `Validate`：给缺失集合补默认值；逐台显示器把 brightness/temperature/offset/tint/gains/offsets 夹到与 `CalibrationSettings.Sanitized()` 相同的范围；非法 GUID 的 `CalibrationProfileId` 置空并关掉 `UseCalibrationForGamma`；夜间模式经纬度/色温/淡入/算法/感知强度/日程点逐项夹取；无效窗口矩形（非有限、宽 < 320 或高 < 240）删除；UI 折叠 key 为空或 > 80 字符删除（`SettingsNormalization.cs:87-167`）。

注意夜间模式算法这里有一个兼容动作：`Algorithm == BlueReduction`（已废弃）或枚举值非法时统一回落到 `Perceptual`（`SettingsNormalization.cs:132-134`）。

#### 7.3.4 `CalibrationSettings`：运行时的调整载体（不是持久化 DTO）

`CalibrationSettings`（`src/Gloam.Core/CalibrationSettings.cs`）是**每次 apply 现算的运行时对象**，持久化格式是 `SettingsManager.MonitorProfileData`。两者通过 `MonitorProfileData.ToCalibrationSettings()` / `FromCalibrationSettings()` 转换，字段一一对应。

- 常量：`MinimumTemperatureScale = (1900 − 6500) / 70 ≈ −65.7`、`MaximumTemperatureScale = 50`（`CalibrationSettings.cs:12-13`）。滑块只到 ±50，但内部允许更宽的范围，因为夜间日程与每显示器 offset 会叠加，中途裁掉会破坏合成（`CalibrationSettings.cs:31-38` 注释）。
- 运行时字段：`MeasuredCorrectionLut`（3D LUT 引用，不深拷贝）、`CalibrationProfileId`、`NightMelanopicCoefficients`（由 apply 服务填入，LUT 生成不做文件 I/O）、`NightLuminanceCeiling`（HDR headroom，运行时计算，不持久化）、`ShadowDetailStrength/Pivot`（游戏 profile 提供，也不入 settings）。
- `HasAdjustments` 是"要不要走校准分支"的总开关：任一字段偏离中性（容差 0.001-0.01）或带 3D LUT 就为真（`CalibrationSettings.cs:145-157`）。
- `Sanitized()` 返回新对象并夹取值域：brightness `0..100`、gains `0.5..1.5`、offsets `±0.5`、`PerceptualStrength 0..1`、`NightLuminanceCeiling 1..4`、`ShadowDetailStrength 0..1`、`ShadowDetailPivot 0.02..0.25`（`CalibrationSettings.cs:201-227`）。`LutGenerator.GenerateLut` 第一步就调它（`LutGenerator.cs:103`），因此用户输入到不了数学函数。
- `GetHashCode` 是为缓存设计的：各标量**按分辨率取整**（温度 ×100、tint ×10、offsets ×1000）避免拖动时碎片化；`MeasuredCorrectionLut` 用 `RuntimeHelpers.GetHashCode` 的**实例身份**——同样标量但重新生成的 LUT 必须 miss，不能命中旧曲线（`CalibrationSettings.cs:235-277`）。

**持久化与运行时的字段缺口**（`MonitorProfileData.ToCalibrationSettings()`，`SettingsManager.cs:149-163`）：只映射 12 个字段——brightness、useLinearBrightness、temperature、temperatureOffset、tint、RGB gains/offsets、`NightModeCcssPath`。**不映射** `CalibrationProfileId`、`UseCalibrationForGamma`、`Mhc2ProfileName`/`Mhc2SdrProfileName`/`Mhc2HdrProfileName`、`Previous*`、`MeterCorrectionPath`、`Calib*`。这解释了一个容易误解的现象：**live apply 路径永远拿不到 `CalibrationProfileId`**，因此 `RequestApply` 只能调 profile-less 的 `ApplyGamma` 重载（与 `GammaApplyService.cs:379-385` 的注释一致）；日常输出的"实测校准"实际由合成器侧的 MHC2 profile 承担，VCGT 校正曲线只在校准预览/闭环里由调用方显式传入。读这段代码时不要把"settings 里有 CalibrationProfileId"误当成"运行时 apply 会用它"。

#### 7.3.5 `AppPaths`：磁盘路径的唯一权威

`src/Gloam.Core/AppPaths.cs` 的类注释是一条纪律：**应用拥有的每一个磁盘路径都必须经这里**，文件夹名只存在一处。

- `DataDir = %LocalAppData%\Gloam`（settings.json、app.log、CalibrationProfiles、reports、i18n、下载的 Argyll、解压的 ICM 模板）；`RoamingDataDir = %AppData%\Gloam`（漫游数据）。
- `LegacyDataFolderName = "HDRGammaController"` 是 rebrand 前的**磁盘身份**，必须保留字面量，否则老用户的设置与 profile 全丢（`AppPaths.cs:22-27`）。
- `MigrateLegacyData()` 在**任何代码碰新目录之前**执行（`App.Startup` 里排在 `Log.Initialize()` 之前，`src/Gloam/Shell/App.xaml.cs:85-94`），因为日志一创建新目录，`Directory.Move` 就会因目标存在而失败。移动失败时退化成递归复制并**保留旧目录**，不半移动；结果以消息列表返回，由调用方在日志可用后补记（`AppPaths.cs:56-100`）。
- `UseDataDirectoriesForCurrentProcess(dataDir, roamingDataDir)` 只覆盖当前进程，供启动冒烟测试/隔离测试使用，`Path.GetFullPath` 归一化（`AppPaths.cs:42-54`）。

#### 7.3.6 `Log` 与 `LogFileRotator`：唯一的持久诊断通道

GUI 是 WinExe，控制台输出会被 Windows 丢弃，所以文件 sink 是托盘应用**唯一**的持久诊断通道（`src/Gloam.Core/Log.cs:7-13`）。

- `Log.Initialize()` 在 `App.Startup` 最先调用之一：建目录 → 先 `RotateIfNeeded` → 设 `_filePath`；任何异常都静默降级为 console-only（`Log.cs:31-47`）。
- 每条日志格式 `yyyy-MM-dd HH:mm:ss.fff [LEVEL] message`，先 `Console.WriteLine`，再在 `lock (Sync)` 里 rotate + `File.AppendAllText`。日志失败被吞掉——"logging must never take the app down"（`Log.cs:74-87`）。
- 阈值：`MaxBytes = 5_000_000`、`MaxArchives = 3`（`Log.cs:21-22`）。`LogFileRotator.RotateIfNeeded` 是共享实现：超限时把 `path` → `path.1`、`.1` → `.2`、`.2` → `.3`，最老的删掉（`src/Gloam.Core/LogFileRotator.cs:28-36`）。
- `DebugRateLimited(key, message, interval)`：同一 key 在 interval 内只写一次，key 表满 256 条整体清空。用于绘制/进程竞态这类"既不能永久隐藏、也不能刷爆日志"的事件（`Log.cs:53-72`）。

#### 7.3.7 `TextFileStore`：有界读 + 原子写

`internal static TextFileStore`（`src/Gloam.Core/TextFileStore.cs`）被 settings 与校准 profile 共用：

- `ReadBounded(path, maximumBytes, maximumCharacters)`：`FileShare.Read` 打开，先查 `stream.Length`，再边读边累计字符数，超限抛 `InvalidDataException`。两道限制是"字节上限"与"字符上限"，防止多字节 UTF-8 或异常文件绕过单一检查。
- `WriteAtomic(path, text, maximumBytes, maximumCharacters)`：先校验尺寸，建目录，写 `path + ".{Guid:N}.tmp"`，再 `File.Move(temporary, path, overwrite: true)`；finally 删临时文件。**只有 `File.Move` 这一步是替换动作**，进程中途崩掉也不会留下半个 settings.json。清理失败只记日志、不掩盖原始写错误（`TextFileStore.cs:28-50`）。

#### 7.3.8 `DiagnosticsBundle`：可分享的文本化现场

`DiagnosticsBundle.Create(outputDirectory, monitors, settingsManager, includeCalibrationReports = false)` 生成 `Gloam-Diagnostics-{yyyyMMdd-HHmmss}.zip`（重名时追加 `-02…`，最多 999 次后加 GUID），返回完整路径（`src/Gloam.Core/DiagnosticsBundle.cs:38-80`）。包内固定内容：

| 条目 | 内容 |
|---|---|
| `manifest.json` | 应用产品/版本、`AppContext.BaseDirectory`、`AppPaths.DataDir`、OS/Framework/架构、显示器列表（`BuildManifest`，`DiagnosticsBundle.cs:108-136`） |
| `settings.sanitized.json` / `update-state.sanitized.json` | 脱敏后的设置与更新状态 |
| `logs/*.log*` | `DataDir` 下所有 `*.log*`（含轮转档） |
| `calibration-profiles-summary.json` | profile ID/名称/显示器哈希/目标/gamma/黑位/峰值/测量 gamma/有无 tone curve（`DiagnosticsBundle.cs:187-218`） |
| `reports/…` | 仅 `includeCalibrationReports: true` 时包含保存的校准报告与原始测量 CSV |
| `THIRD_PARTY_NOTICES.txt` | 许可证文本 |

脱敏是硬要求：`SanitizeText` 把 `%USERPROFILE%`/`%LOCALAPPDATA%`/`%APPDATA%`/`%TEMP%`/用户名替换成占位符，并把显示器标识（`\\?\DISPLAY#…`、`MONITOR\…`）替换成 `monitor-{hash}`（`DiagnosticsBundle.cs:82-106`）。**默认不含原始 ICC / 修正文件**，这是"诊断包可以贴到 issue 里"的前提（类注释 `19-23`）。

#### 7.3.9 `WindowsNightLight`：关掉会叠加的第二层暖色

Windows Night Light 通过同一条显示管线施加自己的暖色变换；两者同时开，输出是两次颜色变换的乘积，校准与夜间模式的色度学都会被破坏，**与 Gloam 自己的夜间模式是否开启无关**（`src/Gloam.Core/WindowsNightLight.cs:7-12`）。所以 `TryDisplay` 的检测是常开的、60s 节流的（§7.2.7）。

实现全部基于 CloudStore 里未文档化的 blob，并且刻意保守：

- `Detect()` → `CalibrationInstallPreflight.DetectNightLightActive()`；判定纯函数 `IsNightLightActiveBlob` 只认 `data[18] == 0x15`（ON）/`0x13`（OFF），其他值返回 null = 无法确定（`src/Gloam.Core/Calibration/CalibrationInstallPreflight.cs:350-374`）。格式假设在注释里标明来自逆向（Win10 1903 – Win11 23H2 实测）。
- `BuildDisabledBlob`：只在"确信 ON 布局"（长度 ≥ 25、`data[23]==0x10 && data[24]==0x00`）时构造 OFF blob——复制掉那 2 字节额外字段、`result[18]=0x13`，并**自增 10..14 的 varint 时间戳**让 shell 认为记录更新、重新广播给 DWM（`WindowsNightLight.cs:24-53`）。
- `TryDisable`：写注册表 `HKCU\Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\default$windows.data.bluelightreduction.bluelightreductionstate\windows.data.bluelightreduction.bluelightreductionstate` 的 `Data` 值（键路径常量在 `CalibrationInstallPreflight.cs:341-344`），写完**重新读取验证**，只有"确信 ON→确信 OFF"才返回 true；任何意外都是 no-op 失败（`WindowsNightLight.cs:59-83`）。

**磁盘布局**（本章涉及部分）：`%LocalAppData%\Gloam\` 下：`settings.json`（唯一设置文件，schema 4）、`app.log` + `.1…3`（5 MB 轮转）、`CalibrationProfiles\<guid>.json`（校准 profile 大对象）、`reports\`（校准报告 JSON/CSV，诊断包可选携带）、`i18n\`（外部语言目录，覆盖内嵌目录，`App.xaml.cs:69-83`）；程序目录只读时 `ResourceExtractor` 把 ICM 模板解压到同一目录兜底（`CalibrationProfileInstaller.cs:788-790` 注释）。`update-state.json` 由 `UpdateService` 维护、`DiagnosticsBundle` 只读脱敏后打包；本章不展开。

### 7.4 CLI 与 DebugMonitors

#### 7.4.1 `Gloam.Cli`：无 GUI 的颜色管线诊断台

`src/Gloam.Cli/Program.cs`（单文件 875 行）是一个**不启动托盘、不碰显示器状态**的开发工具。它引用 `Gloam.Core`，但 grep 结果里没有 `MonitorManager` / `DispwinRunner` / `NativeGammaRamp` / `Gdi32` / `Wcs` / `Log.Initialize` / `AppPaths` 的任何调用——CLI 只做纯计算与文本输出。命令分发（`Program.cs:30-40`）：

| 子命令 | 签名 | 作用 |
|---|---|---|
| `lut` | `<gamma> <white> [options]` | 生成一组 LUT，打印采样表 + ASCII 图，可写 CSV/JSON |
| `sweep` | `<gamma> <white>` | 扫亮度（100/75/50/25）等场景矩阵，每个场景一行 sparkline + 低/中/高采样值 |
| `compare` | `<gamma> <white> <A> <B>` | 两个场景（`b=50,t=-20` 简写）并排对比，打印 A/B/Δ 三条 sparkline 与中点值 |
| `check-lut` | `<gamma> <white> [options]` | 校验 LUT 不变量；失败时**退出码 2** |
| `night` | `<kelvin> [options]` | 打印某色温下四种算法的 RGB 乘数、通道削减比例、相对 melanopic/luminance |
| `golden-ingest` | `<fixtureDir> [options]` | 创建/刷新金标准回归夹具（复制测量 CSV + 生成 manifest） |
| *（legacy）* | `Gloam.Cli 2.2 200 [out.csv]` | 旧形态，`RunLegacy` 直接按位置参数转成 `LutOptions`（`Program.cs:97-113`） |

`gamma` 取值 `2.2 | 2.4 | default`（`ParseGamma`，也接受 `srgb`/`windows` 作 default 别名）。`LutOptions`/`NightOptions` 是私有嵌套类（`Program.cs:846-865`），命令行解析全部手写（`ParseCalOptions` / `ParseNightOptions`），未知选项直接抛 `ArgumentException` 而不是静默忽略。

内建帮助就是选项清单（`PrintHelp`，`Program.cs:51-93`），原文摘录：

```
Usage: cli lut <gamma> <white> [options] | cli sweep <gamma> <white> | cli compare <gamma> <white> <A> <B>
       cli check-lut <gamma> <white> [options] | cli night <kelvin> [options] | cli golden-ingest <fixtureDir> [options]
Options (lut/check-lut): --brightness N | --tint T | --strength N | --linear-dim | --csv PATH | --json PATH|-
                         --temp-k K | --algorithm NAME (perceptual|ultra|accurate|classic) | --ccss PATH
                         --sdr | --no-chart
Scenario shorthand (compare): 'b=50,t=-20' means brightness 50, temp -20
```

异常处理有一个开发便利：`HDRCLI_TRACE=1` 时把完整堆栈打到 stderr，否则只打印 `error: {message}` 并以 1 退出（`Program.cs:42-48`）——CI 日志默认干净，本地排障时再打开。

#### 7.4.2 输出格式：三套并存，各有用途

- **采样表**：`lut` 在 `SamplePoints`（0/64/128/256/384/448/512/576/640/768/896/1023）打印 `index / input_signal / input_nits / out_*`，点密度刻意压在白点边界（200 nits 对应约 index 520）附近（`Program.cs:539-555`）。
- **CSV**：表头固定 `index,input_signal,input_nits,out_r,out_g,out_b,out_grey`，数值用 `G8` + `InvariantCulture`（`WriteCsv`，`Program.cs:557-569`）。可直接进 Excel/pandas。
- **JSON**：`WriteJson` 用 camelCase + 缩进；路径 `-` 表示写 stdout（`Program.cs:577-591`），所以 CI 可以直接 `--json -` 抓 JSON。`lut` 的 JSON 与 `check-lut`/`night` 的 report 结构不同，report 里都带 `command` 字段便于区分（`Program.cs:224-234,268-289`）。
- **ASCII chart**：`Sparkline` 用 Unicode 半块 `▁▂▃▄▅▆▇█` 把 1024 点按 64 列取均值；`SparklineSigned` 用 `. - + = # ▼ ▲` 表示 B 相对 A 的符号与量级（`Program.cs:492-535`）。这是给人快速看"曲线形状对不对"的，不参与断言。

`night` 子命令的乘数来源按算法分派（`GetNightMultipliers`，`Program.cs:792-803`）：`Perceptual → ColorAdjustments.GetPerceptualMultipliers(kelvin, strength, basis)`、`UltraNight → GetUltraNightMultipliers(kelvin, coefficients, basis)`、`Standard → GetStandardMultipliers(kelvin)`、`AccurateCIE1931 → GetAccurateMultipliers(kelvin, basis)`。`kelvin` 先过 `NightModeSettings.ClampKelvin`、强度过 `ClampPerceptualStrength`——CLI 与运行时调用同一套夹取函数，所以相同输入下 CLI 打印的乘数就是运行时实际会用的乘数。melanopic 估计只在给了 `--ccss` 时出现：没有系数就返回 null 并明确打印 "unavailable"（`Program.cs:302-305`）。

#### 7.4.3 `check-lut`：CI 为什么能跑、断言了什么

`check-lut` 对 R/G/B/Grey 四个通道各跑 `AnalyzeChannel`（`Program.cs:607-657`），失败项聚合成 `failures` 列表：

1. 每个值必须**有限**（`double.IsFinite`）。
2. 每个值必须落在 `[-1e-9, 1 + 1e-9]`——即 LUT 是归一化信号，不是 nits。
3. **单调性**：相邻下降超过 `1e-7` 即失败（相等允许）。这一条直接锁住"LUT 不能把暗部掰回去"。
4. 空通道失败。

失败时进程退出码 **2**（不是 1；1 留给未捕获异常，`Program.cs:254`），并把 `passed/failures/channels` 写成 JSON。CI 里的用法是（`.github/workflows/build.yml:49-53`）：

```
dotnet run --project src/Gloam.Cli/Gloam.Cli.csproj --configuration Release --no-restore -- \
  check-lut 2.2 200 --algorithm perceptual --temp-k 2700 --json artifacts/cli-lut-check.json
```

能在 CI 跑的原因有三个，都可核对：workflow 的 `runs-on: windows-latest`（CLI 的 TFM 是 `net10.0-windows`，虽然它不做 Windows API 调用，但 TFM 需要 Windows 运行时支持，见 `src/Gloam.Cli/Gloam.Cli.csproj`）；CLI 只依赖 `Gloam.Core` 的纯计算路径（`LutGenerator`/`TransferFunctions`/`ColorAdjustments`）；它不写设置、不碰硬件，所以不需要交互会话。`sweep`/`compare`/`lut` 没有断言、供人看；`check-lut` 是唯一有退出码语义的"闸门"。

#### 7.4.4 `golden-ingest`：金标准夹具的唯一入口

`golden-ingest <fixtureDir> [--native <csv> --verification <csv> --target <name> --panel <label> [--hdr] [--sdr-white N] [--instrument <model>] [--meter-correction]]`（`Program.cs:318-375`）：

- **创建模式**（给了 `--native/--verification`）：复制两份测量 CSV 进夹具目录（固定名 `native-measurements.csv` / `verification-measurements.csv`），写 `manifest.json`（panel/target/HDR 标志/SDR 白点/仪器/是否有仪器校正）。
- **刷新模式**（无 flags）：读已有 `manifest.json`，重算基线。
- 设计意图写在注释里：**重新生成基线是接受数值管线变更的"正规通道"**——diff 会出现在 git review 里，而不是让测试偷偷改写自己的期望（`Program.cs:312-317`）。贡献流程见 `docs/golden-fixture-contribution.md`。

#### 7.4.5 `Gloam.DebugMonitors`：最小可运行的显示器探针

`src/Gloam.DebugMonitors/Program.cs` 只有 46 行：`new MonitorManager().EnumerateMonitors()` → 每台打印 `DeviceName`/`FriendlyName`/`HDR` 状态行 → 再 `JsonSerializer.Serialize(monitor, WriteIndented)` 全量 dump（含 LUID、bounds、HDR nits、DisplayConfig ID、EDID 色度）→ 非重定向时 `Console.ReadKey()` 停住。它是排查"枚举结果不对"时的第一现场：DXGI 看到的、GDI 补出来的、DisplayConfig 补出来的字段在同一个 JSON 里并排。异常也打印堆栈而不是崩溃（`Program.cs:12-37`）。

### 7.5 不变量、线程模型与风险

#### 7.5.1 改动前必须成立的十条不变量

1. **LUT 契约**：1024 点、值域 `[0,1]`、单调不降；`LutGenerator` 返回的数组是**共享只读**，任何调用方写它都会污染缓存（`LutGenerator.cs:26-28`）。`check-lut` 是这条不变量的自动化守卫。
2. **GDI 包络**：每项高字节与索引偏差 ≤ 128，否则整条 ramp 被系统拒绝；除非 `GdiIcmGammaRange = 0x100`（需重新登录生效）（`NativeGammaRamp.cs:16-27`）。
3. **写节流与去重**：每显示器同时最多一个 apply，写入间隔 ≥ 250ms，相同 LUT 不重写。三者缺一都会让用户看到闪烁或让合成器卡顿（`GammaApplyService.cs:94-98`、`DispwinRunner.cs:335-343`）。
4. **旁路时序**：注册 bypass 必须在清 ramp **之前**；注销在恢复之后；注册 key 与查询 key 必须来自同一个 `BypassKey`（`CalibrationStateManager.cs:53-67,120-125`）。
5. **设置宁可不写，不可写坏**：NewerSchema 拒绝写、解析失败且无变更拒绝写、写盘必须是 `.tmp` + `File.Move` 原子替换（`SettingsManager.cs:578-635`、`TextFileStore.cs:28-50`）。
6. **ramp guard 不与自己看不见的显示器对抗**：读不出当匹配、驱动变换过写后标记不可校验并永久跳过（`DispwinRunner.cs:58-66,115-119,129-134`）。
7. **启动顺序**：`AppPaths.MigrateLegacyData()` 必须在任何代码创建 `%LocalAppData%\Gloam` 之前（实际是 `Log.Initialize()` 之前），否则迁移永远失败（`App.xaml.cs:85-94`）。
8. **基底一致性**：调色在**线性光**里做，编码/解码必须成对；SDR 用 sRGB 基底推导色温乘数，HDR 用 Rec.2020 基底——对角色温是基底相关的，混用会落到 Planckian 轨迹外、偏暖不足（`LutGenerator.cs:182-195,269-274`）。
9. **失败不升级**：apply/clear/ramp guard/snapshot/dose 的异常都在边界处被捕获记日志，不向 UI 抛出（`GammaApplyService.cs:89-92,100-105,670-681`）。
10. **持久化只跟随真实变更**：显式预览（`manualCalibration`/`nightKelvinOverride`）不写 settings，滑块拖动不会产生磁盘写（`GammaApplyService.cs:432-440`）。

#### 7.5.2 线程模型

| 执行体 | 做什么 | 同步手段 |
|---|---|---|
| UI（Dispatcher）线程 | `TrayViewModel`/dashboard、`RequestApply` 的策略解析、`SettingsManager` 变更、`ApplyAll` 的遍历 | `_policyLock`、`_dataLock`；UI 状态只允许在此线程（`OnUiThread` 回投） |
| 线程池（`Task.Run`） | `LatestValueCoalescer` 的 runner：LUT 生成 + GDI 写入（每显示器 key 一个） | 每 key `SemaphoreSlim` + `slot` 锁；`_lifetimeLock` 守生命周期 |
| `System.Timers.Timer` | ramp guard 每 10s 一次 `VerifyAndRestoreRamps`；`NightModeService` 1s 定时器推进淡入，`BlendChanged` 再投回 Dispatcher | 各自 try/catch；guard 内读 `_lastApplied` 无锁（ConcurrentDictionary） |
| WinEvent 回调线程 | `AppDetectionService` 的 `EVENT_SYSTEM_FOREGROUND`（`WINEVENT_OUTOFCONTEXT`）→ 游戏/排除策略 | `GamerModeCoordinator._evaluationLock`，再调 `UpdateActiveGamerSessions` 的 `_policyLock` |
| 前景任务线程池 | `MarkGamerProfileUsed` 的持久化 | `SettingsManager._gamerWriteLock` |
| 校准流程线程（Calibration 章） | `EnterBypassMode`/`ApplyCorrectionLut`/`RestorePreviousState` | 静态 `ActiveBypassLock`（所有实例共享） |

三个容易踩的点：

- **策略在调用线程同步解析，硬件在工作线程异步执行**。这是刻意的：`RequestApply` 必须读"请求这一刻"的夜间 kelvin、游戏会话、排除集合，而合流后的执行只负责写；两段之间靠 §7.2.8 的旁路复查和 `skipIfBypassed` 兜住状态漂移（`GammaApplyService.cs:342-344` 注释）。
- **快照发布是事件去重的**。`PublishSnapshot` 按 `HMonitor` 存 hash key，key 由有效 kelvin、算法、量化到 0.001 的 RGB 增益、亮度、HDR、SDR 白点、CCSS 路径组成，时间戳**不参与**——因此 `StateApplied` 是"状态变更信号"而不是"每次 ramp 写"（`GammaApplyService.cs:467-519`）。melanopic dashboard 与剂量存储靠这个信号工作，写频率降两个数量级。
- **HMONITOR 是易失身份**。显示配置变化后句柄值会 stale，`TrayViewModel` 用 `MonitorBounds` 矩形回匹配找回"焦点在哪台"（`TrayViewModel.cs:575-592`）；`_lastApplied` 又用 GDI 设备名做 key（不随 `HMonitor` 变）。跨事件传递 `MonitorInfo` 前先 `RefreshMonitors`。

| 锁 | 保护对象 | 持锁期间是否做 I/O |
|---|---|---|
| `SettingsManager._dataLock` | `_data` 与所有组合读；锁内只做序列化，写盘在锁外的 `_saveLock` | 否 |
| `SettingsManager._saveLock` | 串行化 `settings.json` 写入 | 是（`WriteAtomic`，刻意如此） |
| `SettingsManager._gamerWriteLock` | 游戏模式开关/profile 写入/`MarkGamerProfileUsed` | 否（只改内存后交给 `Save`） |
| `GammaApplyService._policyLock` / `_snapshotLock` / `_doseLogLock` | 策略状态（blocked set + gamer sessions）、快照去重 key、剂量日志去重 key | 否 |
| `DispwinRunner._displayIndexLock` | `_displayIndexMap` | **是**：持锁启动 `dispwin -?` 并等 5s，是唯一"持业务锁跑外部进程"的点（`DispwinRunner.cs:161-212`） |
| `CalibrationStateManager.ActiveBypassLock`（static） | 跨实例的 bypass 注册表 | 否 |
| `LatestValueCoalescer._lifetimeLock` + per-slot 锁 | slot 字典与生命周期；work 回调在锁外执行 | 否 |
| `Log.Sync` | 文件轮转 + 追加 | 是（`File.AppendAllText`，量小且串行） |
| 无锁（`ConcurrentDictionary`） | `LutGenerator._lutCache`、`DispwinRunner._lastApplied`/`_deviceNameToPath`/`_unverifiableDisplays` | — |

#### 7.5.3 风险与硬边界

1. **GDI 包络是物理上限**。SDR 上"2700K + 高感知强度"这类极端修正无法完整表达，只能夹到最接近的可接受 ramp；日志会提示用户去设 `GdiIcmGammaRange`。这不是 bug，是 Windows 的安全设计。
2. **双兜底依赖 Argyll**。原生 `SetDeviceGammaRamp` 失败后若 `dispwin.exe` 不存在，则本次完全没有修正，只弹通知与日志（`DispwinRunner.EnsureConfigured`，`DispwinRunner.cs:244-259`）。CLI 与校准的"下载 Argyll"链路是这条兜底的前提。
3. **MHC2 重复施加色调曲线**。`Mhc2ProfileName` 非空时必须只叠夜间暖色；当前 `RequestApply` 的 live 路径一直调 profile-less 的 `ApplyGamma` 重载，注释专门说明了为什么不强制切 `WindowsDefault`（`GammaApplyService.cs:379-385`）。任何未来把校准 profile 传进 live 路径的改动都会造成双重 gamma。
4. **未文档化 API 的面积不小**：CloudStore Night Light blob（纯逆向）、ICC `'mhc2'` 私有标签（靠启发式定位 LUT）、`ColorProfileAddDisplayAssociation`（Win10 20348+）、`DISPLAYCONFIG_*` 结构。这些都有"解析不确定就放弃"的保护，但升级 Windows 时是首要回归点。
5. **枚举的 O(n) 与 COM 手工封送**：`MonitorManager` 每次 `RefreshMonitors` 都重建 DXGI 工厂并逐输出 QI；枚举失败只记日志、返回已收集到的部分，UI 会显示少一台显示器而不是崩溃（`MonitorManager.cs:23-27,56-66`）。**推断**：多适配器 + 频繁 HDR 切换场景下，这段是抖动/延迟的主要来源之一。
6. **设置写是同步的**。所有 `Set*` 方法在调用线程上直接 `Save()`；文件小（KB 级）时无感，但 `Save()` 内含 serialize + 原子替换，`16 MiB` 上限只是防御值。**推断**：拖动滑块本身不触发保存，所以主路径不受影响，但批量导入/迁移时应避免在 UI 线程密集调用。
7. **时间相关的行为未验证**：本报告是静态阅读，未在真实显示器/驱动上运行。`RampToleranceCounts = 512`、`GdiIcmGammaRange` 的实际效果、ACM 与 HDR 的区分、Night Light blob 在特定 Windows build 上的字节布局，都需要硬件/环境复核；文中凡标「推断」处同理。
8. **遗留路径的认知成本**：`ProfileManager` 已注册但无调用点（§7.2.6），`SettingsNormalization` 里还保留 `BlueReduction` 的兼容回落。读到它们时先确认没有调用方，再判断是否死代码。
9. **HDR/SDR 不跨模式**：`monitor.IsHdrActive` 决定 LUT 走哪条数学分支（PQ wire vs gamma 编码 wire），校准安装器也拒绝跨模式安装（`CalibrationProfileInstaller.cs:225-234`）。任何"用 SDR 校准去修 HDR"的捷径都会毁图像。
10. **诊断包不泄露原始素材**：`DiagnosticsBundle.Create` 默认排除原始 ICC/修正文件，路径与显示器 ID 必须脱敏（`DiagnosticsBundle.cs:19-23,82-106`）。新增诊断内容时先过 `SanitizeText`/`HashId`。

#### 7.5.4 静态阅读未覆盖的行为

本章全部结论来自源码、注释、CI 配置与既有文档；**未执行 `dotnet build`/`dotnet test`，未运行程序，未连接显示器或色度计**。因此下面这些只能算"代码声称的行为"，需要环境复核：

- GDI 包络的数值（±128 高字节、`0.496` 顶端）来自源码注释里记录的 Windows 11 22621 实测，不是本次复现。
- `GdiIcmGammaRange = 0x100` 解锁"完整范围"的结论同样来自注释与 f.lux/Argyll 的公开文档，且需要重新登录才生效，无法在不改注册表的前提下验证。
- Night Light CloudStore blob 的字节布局是纯逆向结论，源码注释只承诺"在 Win10 1903 – Win11 23H2 上观测过"；其他 build 的行为未验证。
- ICC `'mhc2'` 标签内的 LUT 定位依赖"1024 计数 + 前 64 项至少 58 项非降"的启发式；非微软模板是否命中（或误命中）未验证。
- dispwin 显示编号映射依赖 `dispwin -?` 的文本格式，换 Argyll 版本可能变化；代码有 `OutputId + 1` 兜底，但兜底本身在多适配器下不保证正确（注释自述）。
- "旁路四点检查"的时序正确性靠代码顺序保证；真实并发（滑块拖动 + 校准同时进行 + display-change 重放三者叠加）未做压力测试。
- 10-bit 驱动的 ramp 量化幅度、`RampToleranceCounts = 512` 的实际余量，需要真实面板验证。

### 7.6 文件 → 职责表

**Interop（`src/Gloam.Interop/`）**

| 文件 | 职责 | 关键入口 / 被谁调用 |
|---|---|---|
| `Gdi32.cs` | GDI gamma ramp P/Invoke：`GammaRamp`(256×3 ushort)、`CreateDC/DeleteDC/Set/GetDeviceGammaRamp` | `NativeGammaRamp` |
| `Dxgi.cs` | DXGI COM 手工声明：工厂/适配器/输出、`LUID`/`DXGI_*_DESC1`、HDR 色彩空间常量 | `MonitorManager.EnumerateMonitors` |
| `DisplayConfig.cs` | `QueryDisplayConfig` 最小封装：GDI 名→(LUID,source)、Advanced Color 状态、SDR 白点 nits | `MonitorManager.EnrichWithDisplayConfig`、校准安装侧 |
| `Wcs.cs` | `mscms.dll`：ICC 安装/关联、Advanced Color 关联、颜色存储目录、`LocalFree` | `ProfileManager`、`CalibrationProfileInstaller`、`AdvancedColorProfileAssociation` |
| `User32.cs` | 显示设备枚举、热键、前景窗口/WinEvent、监视器矩形、`User32Safe` 错误包装 | `MonitorManager`、`AppDetectionService`、`HotkeyManager`、`TrayViewModel` |

**Core 根目录（`src/Gloam.Core/`）**

| 文件 | 职责 | 关键入口 |
|---|---|---|
| `GammaMode.cs` | 三值枚举：`Gamma22`/`Gamma24`/`WindowsDefault` | `DispwinRunner.GammaModeToValue`、`LutGenerator` |
| `TransferFunctions.cs` | PQ（ST 2084）、sRGB、BT.1886 的 EOTF/OETF 及输入夹取 | `LutGenerator`、`Lut3D`/校准链路 |
| `LutGenerator.cs` | 1024 点 LUT 生成（HDR/SDR 分支、headroom 淡出、校准 LUT 三重载）+ 有界缓存 | `DispwinRunner.ApplyGamma`、CLI、`Calibration/ClosedLoopCorrector` |
| `NativeGammaRamp.cs` | LUT→256 项 ramp 重采样、GDI 包络夹取与解锁探测、`TryApply/TryRead/TryClear` | `DispwinRunner`、ramp guard |
| `DispwinRunner.cs` | 下发与自愈：去重、原生优先/dispwin 兜底、`.cal` 生成、ramp guard、显示编号映射 | `GammaApplyService`、`CalibrationStateManager` |
| `GammaApplyService.cs` | apply 策略唯一所有者：模式/夜间/排除/游戏会话解析、合流提交、10s guard、快照发布、panic clear | `TrayViewModel`、`GamerModeCoordinator`、`MelanopicMonitorService` |
| `LatestValueCoalescer.cs` | 通用每 key 最新值合流器：单飞、可取消、250ms 下限、两阶段 dispose | `GammaApplyService` |
| `MonitorInfo.cs` | 单台显示器运行时模型（GDI 名、持久化路径、HDR 事实、SDR 白点、LUID、EDID 色度、`Sanitize*`） | `MonitorManager`、全应用 |
| `MonitorManager.cs` | DXGI→GDI(EDID)→DisplayConfig 三段富化的显示器枚举 | `TrayViewModel.RefreshMonitors`、dashboard、校准窗口、DebugMonitors |
| `DxgiColorSpaceInfo.cs` | `DXGI_COLOR_SPACE_TYPE` → 是否 HDR / 名称 / bpc 文案 | `MonitorManager` |
| `ProfileManager.cs` | 旧 MHC2 链路：LUT→模板补丁→安装/关联/设默认（**无调用点**，§7.2.6） | DI 注册 `App.xaml.cs:34` |
| `ProfileTemplatePatching.cs` | ICC `'mhc2'` 标签定位与 1024×16.16 定点 LUT 写入、profile ID 清零 | `ProfileManager`、`CalibrationProfileInstaller` |
| `CalibrationSettings.cs` | apply 的运行时调整载体：字段、`HasAdjustments`、`Sanitized()`、缓存用 `GetHashCode` | `LutGenerator`、`GammaApplyService` |
| `SettingsManager.cs` | `settings.json` 强类型门面 + 显示器/夜间/游戏/MHC2/校准 profile 记账 + 三把锁 + 两条拒写规则 | 全应用（DI singleton） |
| `SettingsPersistence.cs` | `settings.json` 全部 I/O：大小上限、NewerSchema 探测、legacy 转换、原子写、备份 | `SettingsManager` |
| `SettingsNormalization.cs` | `SettingsMigration`（schema 4）+ `SettingsNormalization`（去重/安全过滤/夹取，无 FS 副作用） | `SettingsPersistence.Load` |
| `AppPaths.cs` | 数据目录唯一权威、`HDRGammaController` 一次性迁移、测试用目录覆盖 | `Log`、`SettingsManager`、`DiagnosticsBundle`、`App.Startup` |
| `Log.cs` | console + 5 MB/3 归档文件 sink、`DebugRateLimited` | 全应用 |
| `LogFileRotator.cs` | 共享按尺寸轮转（`path`→`path.1`…） | `Log` |
| `TextFileStore.cs` | 有界读、临时文件 + 原子替换 | `SettingsPersistence`、校准 profile |
| `DiagnosticsBundle.cs` | 文本化支持包（manifest/脱敏设置/日志/profile 摘要/可选报告），用户路径与显示器 ID 脱敏 | `TrayViewModel.ExportDiagnostics` |
| `WindowsNightLight.cs` | 检测并关闭 Windows Night Light（CloudStore blob 纯函数变换 + 写后校验） | `TrayViewModel.EnsureWindowsNightLightDisabled` |

**工具程序**

| 文件 | 职责 | 关键入口 |
|---|---|---|
| `src/Gloam.Cli/Program.cs` | 无 GUI 颜色管线诊断：`lut/sweep/compare/check-lut/night/golden-ingest` + legacy 位置参数；CSV/JSON/ASCII 输出；CI 闸门 | `Main`（`Program.cs:20`） |
| `src/Gloam.DebugMonitors/Program.cs` | 显示器枚举探针：文本摘要 + `MonitorInfo` 全量 JSON | `Main`（`Program.cs:9`） |

**不在本章但被反复引用**：`NightModeService`（调度与 kelvin 源）、`GamerModeCoordinator`（前景与锁定策略）、`AppDetectionService`（WinEvent 前景流）、`ColorAdjustments`（色温/调暗数学）、`Calibration/*`（闭环、ICC 安装、bypass 状态机）——它们各自有独立章节，本章只给出接缝。

**与相邻章节的接缝（本章承诺的契约）**

- **校准章**：`CalibrationStateManager` 是 bypass 的唯一权威（静态注册表 + 统一 `BypassKey`）；`DispwinRunner.ApplyCorrectionLut` 是闭环写候选修正的唯一入口；`LutGenerator.GenerateCalibratedLut(CalibrationTarget, ...)` 是唯一保证"校正器/验证器/安装 LUT 同一条目标 EOTF"的入口；`ProfileTemplatePatching` 是 MHC2 字节级补丁的唯一实现。
- **夜间模式章**：`GammaApplyService.RequestApply(monitor, mode, manualCalibration?, nightKelvinOverride?, nightModeSettingsOverride?)` 是唯一 apply 入口；`NightModeService.CurrentNightKelvin` 与 `PauseUntil` 由它读写；mired 合成与 CCSS 系数注入只在 `ApplyNightModeToCalibration` 与 `CcssMelanopicEstimator.TryLoad` 两处。
- **游戏模式章**：`UpdateActiveGamerSessions` 负责原子替换、单显示器单会话 owner、会话 kelvin 锁定；`ResolveGamerKelvin`/`BuildNightOpsSettings` 定义 Night Ops；`LatestValueCoalescer.Cancel` 是"暂停即边界"的实现。
- **UI 章**：`SettingsManager` 的读接口返回副本（不得保存后改）；`StateApplied` 是唯一"有效状态变更"信号（不是 ramp 写信号）；`DispwinUnavailable` 是唯一"需要安装 Argyll"信号；`WindowBoundsData` 用于窗口位置记忆。
- **测试/CI 章**：LUT 不变量由 `Gloam.Cli check-lut` 守（退出码 2）；金标准夹具由 `golden-ingest` 维护；`Gloam.Interop` 的 TFM 允许跨平台**编译**（`EnableWindowsTargeting`），但任何调用都要求 Windows。

**依赖方向规则**（改代码时的硬约束）：`Gloam.Interop` 不引用任何其他 Gloam 项目；`Gloam.Core` 只引用 `Gloam.Interop`；`Gloam.Cli`/`Gloam.DebugMonitors` 引用 Core（DebugMonitors 另显式引用 Interop）；`src/Gloam`（WPF 壳）引用 Core + Localization + WPF 包。反向依赖（Core 引用 UI）在仓库中不存在，也不应引入——Core 的 UI 交互全部通过事件/返回值交给壳层（`DispwinUnavailable`、`StateApplied`、`NightModeChanged`、`GamerSessionsChanged`、`PolicyChanged`）。本章涉及文件里最大的四个是 `SettingsManager`（1413 行）、`LutGenerator`（688）、`GammaApplyService`（686）、`DispwinRunner`（620），恰好对应"设置 → 生成 → 策略 → 下发"，按 §7.2 → §7.3 读比硬啃 `SettingsManager` 省时间。

---

## 8 夜间模式与 Game Lab 的色彩科学

> 覆盖 `src/Gloam.Core` 两条链路：**夜间模式**（`NightModeService` → `ColorAdjustments` → `LutGenerator` → `GammaApplyService`）
> 与 **Game Lab**（`GamerMode`/`GamerModeCoordinator`/`GameDiscoveryService` → `GammaApplyService` 的会话契约）。
> **阅读方式：静态阅读**——未执行 build/test、未运行应用、未接真实显示器或色度计；公式为源码逐字摘录，引用格式 `path:line`，
> 推断处标注「推断」。贯穿全节的事实：**Core 从不直接写硬件**，只产出「目标 Kelvin + 每通道乘子 + 1D LUT」这套描述，
> 由 `GammaApplyService` 合并节流后交给 `DispwinRunner`/GDI——这是本节到处出现「量化」「节拍」「每 250 ms 一次写」的根因。

---

### 8.1 夜间模式调度：从墙上时钟到目标 Kelvin

这一层不碰颜色，只回答「此刻应该用多少 K」。

#### 8.1.1 设置模型：`NightModeSettings`（`NightModeService.cs:11-216`）

| 常量 | 值 | 含义 |
|---|---|---|
| `MinKelvin` / `MaxKelvin` | 1900 / 6500 | 白点可及范围（6500 是恒等点） |
| `DefaultNightKelvin` | 2700 | 暖白炽灯观感 |
| `MaxFadeMinutes` | 120 | 单次渐变上限 |
| `MaxSunOffsetMinutes` | 120.0 | 日出/日落触发点最大偏移 |

主量纲 `TemperatureKelvin` 之外还有一个**遗留线性标尺** `Temperature`（同一份额度）：

```
get => (TemperatureKelvin - 6500) / 70.0;
set => TemperatureKelvin = ClampKelvin((int)Math.Round(6500 + value * 70));
```

即 −50↔3000 K、0↔6500 K、+50↔10000 K（每单位 70 K）。夜间模式实际会跑到 1900 K（≈−65.7），所以
`ClampKelvin` 只认 1900–6500，而 `ColorAdjustments.TemperatureScaleToKelvin` 允许 1000–10000（§8.2）。
「同一标尺、两套钳位」是后面许多细节的根因。

其余字段：`ManualOverrideEnabled` 忽略日程、立即套用 `GetManualOverrideKelvin()`（= 日程里最暖的点，
即 `TargetKelvin < MaxKelvin` 的最小值，否则退回 `TemperatureKelvin`），但 `Enabled=false` 仍最高优先强制白天；
`PreserveLuminance`（常量 Y，排除 UltraNight，§8.2.7）；`MelanopicEdiCeiling`（mel-lux，0 = 关闭，
`ClampMelanopicCeiling` 钳 0.5–1000）；`Schedule` 为空时 `EnsureSchedule` 从 `StartTime`/`EndTime`（自动模式为
Sunset/Sunrise）迁移出两点：日出侧到 6500 K、日落侧到夜间 Kelvin。`ConvertSimpleScheduleToSunTriggers()` 按
`TargetKelvin` 而非列表顺序判定谁属于日出（白天点 ≥6000 K 且暖点 <6000 K），修掉旧 auto-detect 把首行分配给
Sunset 的 bug。

#### 8.1.2 日程点与 NOAA 太阳位置

`GetTimeOfDay(lat, lon)`（`NightModeSchedulePoint.cs:25-49`）统一三种触发：`FixedTime` → `NormalizeTimeOfDay(Time)`；
`Sunrise`/`Sunset` → `CalculateTodayDetailed` 取对应时刻加 `OffsetMinutes`（钳 ±120 min）再归一化；若
`HasValidTimes == false`（极昼/极夜）**不用** `(0,0)`、`(0,24h)` 哨兵，而回退 **07:00 / 19:00**——哨兵会让渐变逻辑
在本地午夜排一次「渐变到零」的假转换。`SunResult` 因此保留旧哨兵值以兼容老调用者，新调用者必须读
`IsPolarNight` / `IsMidnightSun`（`SunCalculator.cs:18-34`）。

`SunCalculator`（197 行，纯静态 NOAA 实现）先把输入 `ClampFinite`（纬 ±90、经 ±180、UTC 偏移 ±14 h），核心步骤逐字：

```
JD = floor(365.25*(year+4716)) + floor(30.6001*(month+1)) + day + B - 1524.5
JC = (JD - 2451545.0) / 36525.0
L0 = Mod360(280.46646 + JC*(36000.76983 + 0.0003032*JC))
M  = Mod360(357.52911 + JC*(35999.05029 - 0.0001537*JC))
e  = 0.016708634 - JC*(0.000042037 + 0.0000001267*JC)
C  = sin(M)*(1.914602 - JC*(0.004817 + 0.000014*JC))
   + sin(2M)*(0.019993 - 0.000101*JC) + sin(3M)*0.000289
y      = tan(obliqCorr/2)^2
eqTime = 4*RadToDeg*( y*sin(2*L0) - 2e*sin(M) + 4e*y*sin(M)cos(2*L0)
                      - 0.5*y*y*sin(4*L0) - 1.25*e*e*sin(2*M) )
cosHA  = cos(90.833°) / (cos(lat)cos(decl)) - tan(lat)tan(decl)
solarNoon = 720 - 4*longitude - eqTime + utcOffset*60        (分钟)
sunrise   = Mod1440(solarNoon - 4*HA);   sunset = Mod1440(solarNoon + 4*HA)
```

天顶角 **90.833°** 是折射修正后的标准值；`cosHA > 1` → 极夜，`cosHA < -1` → 极昼（返回带标志的 `SunResult`）。
`CalculateTodayDetailed` 用 `TimeZoneInfo.Local.GetUtcOffset(now)` 取当前偏移，每次调用都重算今天——这正是服务
要缓存「已解析日程」的原因。

#### 8.1.3 `NightModeService`：one-shot 定时器 + 状态锁 + 事件

服务本体（`NightModeService.cs:221-741`）：一个 `System.Timers.Timer`（`AutoReset = false`）、一把 `_stateLock`、
一个 `BlendChanged` 事件。每次 `Elapsed` 后由 `ScheduleNextTickLocked()` 重设间隔：

- 手动暂停窗口内：间隔 = 距 `_pauseUntil` 的剩余毫秒，夹 `[MaxFadeTickMs=500, IdleTickMs=60000]`；
- 未启用或手动覆盖：`Stop()`（不空转）；
- 渐变窗口内：`_fadeTickMs`（JND 节拍，§8.3.5）；
- 空闲：距下一个日程触发的毫秒，同样夹 `[500, 60000]`——**睡到下一个触发点**，不轮询。

节拍常量（`NightModeService.cs:381-397`）：`MinFadeTickMs = 1000.0 / 60.0`（≈16.7 ms）、`MaxFadeTickMs = 500`、
`IdleTickMs = 60000`、`HardwareWriteFloorMs = 250.0`（与 apply 侧合并下限对齐）。

**事件语义**：`BlendChanged` 是 `Action<double>`，夜间生效为 `1.0`、强制回白天为 `0`；注释要求「每次有效状态变化恰好发一次」，
因为多发一次就是一次多余的 dispwin 调用（用户看到闪烁）。渐变中每次整数 Kelvin 变化都会发一次，所以 `blend` 实际是**二值开关**。

**状态边界**：`IsNightModeActive => _currentNightKelvin < 6450`（:248-251），apply 侧复用该阈值；`Enabled = false` →
`ForceDayModeLocked()` 拉回 6500 并返回 `0`；`_pauseUntil` 未过期同样强制白天（校准旁路 `CalibrationStateManager.cs:118,283`、
「暂停 1h/4h/至今晨」`DashboardViewModel.cs:266-268`）；`ManualOverrideEnabled` → 用手动 Kelvin，不排日程、不设渐变窗口。

**并发与刷新**（:227-233、:349-364、:689-726）：所有可变状态全在 `_stateLock` 内读写，`BlendChanged` 一律锁外触发以避免重入死锁，
`UpdateSettings` 先 `CloneSettings` 再替换（防日程编辑器在定时器枚举时改 `List`）。`Refresh()` 会丢弃已解析日程并复算，注释点名用于
恢复与 `TimeChanged`；但实际接线是 `App.OnTimeChanged`（`src/Gloam/Shell/App.xaml.cs:117,230-246`）调
`nightMode.UpdateSettings(settings.NightMode)`，靠克隆对象使缓存键失效（§8.1.4）。**`src/` 全库未见 `Refresh()` 调用点。**

#### 8.1.4 折叠成整数 Kelvin，以及 mired 渐变

`CalculateCurrentKelvinLocked()`（:486-605）：先把日程解析为绝对 `TimeSpan` 并排序（缓存键 = 设置对象引用 + 日历日，
:611-614）→ 取最后一个 `Time <= now` 的点为「当前上下文」；清晨 `currentIndex == -1` 时用**昨天最后一个点**
（`-24h`），单点日程用 `-48h` 兜底；**每个点定义的是「到它自己目标」的过渡**——从上一个点的 `TargetKelvin` 渐变到
本点目标，时长用本点的 `FadeMinutes`；`timeSinceTrigger < fadeMinutes` 时按进度插值，否则返回 `endKelvin`（已到位）。

mired（1e6/K）插值是这里第一个色彩学决定：CCT 是倒数量，等 Kelvin 步长在暖端感知上大得多，线性 Kelvin lerp 会把
感知变化堆到冷半段：

```
startMired = 1e6 / startKelvin;
endMired   = 1e6 / endKelvin;
mired      = startMired + (endMired - startMired) * progress;
return mired > 0.0 ? (int)Math.Round(1e6 / mired) : endKelvin;
```

（`InterpolateKelvinInMired`，:638-648；非有限 progress → 1.0，任一 Kelvin ≤0 → 返回 `endKelvin`。）返回值是 `int`，
且只在整数 Kelvin 变化时才更新状态并发事件，因此量化台阶是 **1 K**。

渐变节拍由 mired 距离反解（`CalculateFadeTickMilliseconds`，:650-668）：

```
distanceMired        = |1e6/endKelvin - 1e6/startKelvin|
millisecondsPerMired = fadeMinutes * 60_000.0 / distanceMired
tickMs = clamp(millisecondsPerMired * maxStepMired, MinFadeTickMs, MaxFadeTickMs)
```

`maxStepMired` 的历史契约是 `0.05`（≈4500 K 附近 1 K 的步长），3.5 之后它只是**回退值**：实时上限来自
`JndPacedFade.ComputeMaxStepMired`（§8.3.5）。当「日程时长」与「JND 不可感知」冲突时**日程赢**，且每次渐变窗口
只记一条日志：`ratePerMs = distanceMired / (fadeMinutes * 60_000)`，若 `ratePerMs * 250.0 > maxStepMired` 则记录
"the schedule wins and steps may be perceptibly visible"。

#### 8.1.5 算法枚举：五种白点意图

`NightModeAlgorithm`（41 行）：

| 值 | 意图 | 实现 |
|---|---|---|
| `Standard` | Tanner Helland 近似，照片级暖调 | 码值域归一 + sRGB EOTF 转线性后取比值；<2800 K 可选二次加速（§8.2.2） |
| `AccurateCIE1931` | CIE 1931 物理准确，模黑体 | CCT → 黑体色度 → XYZ → 线性比值，归一化到最亮通道（§8.2.3） |
| `BlueReduction` | 专打 460–480 nm，牺牲色准 | 线性削减：绿 −30%、蓝 −90%、红保 1.0（§8.2.4） |
| `Perceptual`（默认） | 不完整色适应软化的色度学白点 | CAT16 锥空间 D 混合 + 同路径归一（§8.2.5） |
| `UltraNight` | 最大生理防护（琥珀/红），非色准 | Accurate 核 + 整体压暗 + 蓝地板 + 光谱绿削减（§8.2.6） |

一处必须点明的**可达性差异**：`SettingsNormalization.cs:132-134` 在加载设置时把 `BlueReduction` 与非法枚举值
强制改写为 `Perceptual`。枚举与算法实现仍在、仍可被 `ColorAdjustments` 调度，但正常设置路径下用户拿不到它
（除非有别的写入路径绕过归一化）。这是「枚举值存活 ≠ 功能可达」的实例，列入 §8.6 风险表。

---

### 8.2 色彩科学：从白点到逐通道乘子

`src/Gloam.Core/ColorAdjustments.cs`（821 行）是本仓库最集中的颜色数学文件。它只做一件事：给定目标 Kelvin
（以及算法、强度、线基），算出**线性光域的三个逐通道乘子** `(R,G,B)`；编码/解码在 `LutGenerator`（§8.2.9）。

#### 8.2.1 前置约束：`NightBasis` 与 1D LUT 的表达力

`NightBasis`（`ColorAdjustments.cs:14-21`）只有两个值：`Srgb`（SDR 线信号 / Rec.709 基色）与 `Rec2020`（HDR10 的
PQ + Rec.2020 容器）。存在这个枚举的原因是**对角白点缩放在基色下不是不变量**：同一组通道比值在 sRGB 基色上命中
某个色度，在 Rec.2020 编码上会落到另一个色度，所以乘子必须在**它将要缩放的那条信号的基色**里推导。

更硬的约束来自硬件形状：显示校正是 **1D 逐通道 gamma ramp**，只能表达「wire RGB 基里的对角缩放」。注释
（`ColorAdjustments.cs:243-256`）直接给出推论：LMS 锥空间的 CAT 在这里**不可表达**，所以 `Accurate`/`Perceptual`
都是「显示 RGB 基里的 von Kries 式对角」，而非真正的锥空间变换。

RGB↔XYZ 矩阵在 `ColorMath` 静态构造里由基色 + 共享 D65 白点**以全 double 精度现算**，正逆矩阵互为精确逆
（`Calibration/ColorMath.cs:294-336`）。CAT16 正向矩阵是硬编码发表值：

```csharp
public static readonly double[,] Cat16Matrix = {
    {  0.401288, 0.650173, -0.051461 },
    { -0.250268, 1.204414,  0.045854 },
    { -0.002079, 0.048952,  0.953127 }
};
```

逆矩阵由 `Invert3x3(Cat16Matrix)` 现算而非录四舍五入的发表逆值，以保证双精度往返（`ColorMath.cs:216-232`）。

#### 8.2.2 `Standard`：Helland 近似 + 线性域比值

`GetStandardMultipliers`（`ColorAdjustments.cs:159-203`）：取 6500 K 参考 RGB（同一 Helland 公式）与目标 Kelvin RGB，
各自除以自己的最大通道，**再转到线性光取比值**：`r = SrgbEotf(target.r/maxVal) / SrgbEotf(ref6500.r/refMax)`
（`LinearCodeRatio`，:205-210，分母 <1e-9 返回 1.0）。注释解释为何不能直接在码值域取比值：那会在线性 LUT 管线里
**欠校正**冷暖移。Helland 原始公式逐字：

```
temp = kelvin / 100.0
r: temp <= 66 ? 255 : 329.698727446 * pow(temp - 60, -0.1332047592)
g: temp <= 66 ? 99.4708025861 * ln(temp) - 161.1195681661
              : 288.1221695283 * pow(temp - 60, -0.0755148492)
b: temp >= 66 ? 255 : (temp <= 19 ? 0 : 138.5177312231 * ln(temp - 10) - 305.0447927307)
```

（`GetRawKelvinRGB_Helland`，:212-230，每通道夹 0–255。）可选 `UseUltraWarmMode` 只在 `kelvin < 2800` 生效，
给低色温一段二次加速，因为 Helland 在极暖端会饱和：

```
factor = clamp((2800.0 - kelvin) / (2800.0 - 1900.0), 0.0, 1.0)
accel  = factor * factor
g *= (1.0 - accel * 0.25)
b *= (1.0 - accel * 0.5)
```

#### 8.2.3 `AccurateCIE1931`：黑体白点 → 线性 RGB → 同路径归一

三步：(1) `ColorMath.CctToChromaticity(kelvin)` 取黑体色度——实现**不是**注释说的「Kang 近似」，而是启动时按
CIE 1931 2° 配色函数 + 普朗克定律构建的 1000–25000 K **黑体轨迹表**（~1% 对数步长，查询时按 log-CCT 线性插值），
`KangChromaticity` 的 2002 三次拟合只作为表构建失败的回退（`ColorMath.cs:671-715, 795-856`），**注释与实现措辞不一致**；
(2) `xy.ToXyz(1.0)` 后按线基转线性 RGB，负值（出基色）只截到 0、**高通道不截**——截了会在形成白平衡比值之前改变色度
（`ColorAdjustments.cs:304-316`）；(3) `NormalizedRatioMultipliers`：目标与参考各自归一化到自身最大值再逐通道取比值：

```
r = (target.r / targetMax) / (reference.r / refMax)
```

（:273-288；结果夹 [0, 1.5]，非有限值回 1.0。）「同路径归一化」是承重不变量：目标与参考走**完全相同的管线与线基**，
所以 6500 K 逐位相同、比值恰好 (1,1,1)；换算法、切 SDR/HDR 都不许让中性点漂移。

代价是**亮度不受控**：归一化到最亮通道（而非保 Y）意味着白点越暖、绿蓝被削、输出亮度越低。注释明说这对夜间是
**期望行为**——没有通道被推过 1.0（不会削顶），更暗的暖白进一步降低褪黑素剂量。

#### 8.2.4 `BlueReduction`：纯信号域启发式

`GetBlueReductionMultipliers`（`ColorAdjustments.cs:322-339`）没有色度目标，只是线性映射（且**不受线基影响**）：

```
factor = clamp((6500.0 - kelvin) / (6500.0 - 1900.0), 0.0, 1.0)
r = 1.0;  g = 1.0 - factor * 0.3;  b = 1.0 - factor * 0.9
```

#### 8.2.5 `Perceptual`（默认）：CAT16 锥空间里的不完整色适应

`GetPerceptualMultipliers`（:362-388）与 `ColorMath.Cat16Adapt`（`ColorMath.cs:257-288`）。不完整适应不采取
「在 RGB 里 lerp 最终乘子」，而是**把 D 解释为照明体混合**：有效采用白在 CAT16 锐化锥空间取目标白与源白的 D 混合，

```
adoptedWhite_c = D · destWhite_c + (1 − D) · sourceWhite_c
scale_c        = adoptedWhite_c / sourceWhite_c
```

于是每通道增益恰是 `D · (destWhite_c / sourceWhite_c) + (1 − D)`，即「完整 CAT16 von Kries 因子与 1 的 D 插值」——
注释指出这正是 CAM16 对其适应度因子 `D_c = D·Y_w/RGB_wc + 1 − D` 的同一约定，在两白点等亮度（均 Y = 1）、只动色度
的夜间场景下成立。D = 1 复现完整 CAT16（源白精确映射到目标白），D = 0 是恒等。调用：

```csharp
var sourceWhite = ColorMath.CctToChromaticity(6500).ToXyz(1.0);
var targetWhite = ColorMath.CctToChromaticity(kelvin).ToXyz(1.0);
var adaptedTarget    = ColorMath.Cat16Adapt(sourceWhite, sourceWhite, targetWhite, strength);
var adaptedReference = ColorMath.Cat16Adapt(sourceWhite, sourceWhite, sourceWhite, strength);
return NormalizedRatioMultipliers(XyzToUsableLinearRgb(adaptedTarget, basis),
                                  XyzToUsableLinearRgb(adaptedReference, basis));
```

D = 1 时等于 `Accurate`，D = 0 时是 (1,1,1)，最亮通道恒为 1.0（不削顶、不带色偏）；`DefaultPerceptualStrength = 0.8`
（:134）。注释还记录了历史：这替换了早先的显示 RGB 软化 `1 + D·(m − 1)`，后者是不完整 von Kries 而非锐化锥空间。

#### 8.2.6 `UltraNight`：以 Accurate 为核的琥珀模式

`GetUltraNightMultipliers`（:397-434）骨架为「真黑体琥珀 + 整体压暗 + 光谱驱动的绿削减」：

```
factor = clamp((6500.0 - kelvin) / (6500.0 - 2000.0), 0.0, 1.0)
(ar, ag, ab) = GetAccurateMultipliers(kelvin, basis)    // 真黑体轨迹上的核
dim = 1.0 - 0.30 * factor                                // 暖端 → 0.70
r = ar * dim;  g = ag * dim;  b = ab * dim               // 注释：ar == 1.0
b = Math.Max(BlueFloor, b)                               // BlueFloor = 0.10
g = Math.Max(g, b)                                       // 绝不让绿低于蓝（否则洋红）
```

两个非显然的工程细节：**蓝地板 0.10**——某通道被驱到 0 会让它的 gamma ramp 把白映射成黑，Windows 的
`SetDeviceGammaRamp` 会拒绝整条 ramp（表现是「什么都没应用」），0.10 是驱动能接受的最暖 Accurate 设置，仍砍掉约 90% 蓝；
**光谱驱动的绿削减**（`ApplySpectralGreenReduction`，:436-457）只在本机有 CCSS 系数时启用：

```
penalty = clamp((greenRatio / redRatio - 1.0) / 4.0, 0.0, 1.0)      // ratio = Mel/Lum（§8.2.8）
readableGreenFloor = Math.Max(blue * 1.8, 0.12)
targetGreen = Math.Min(planckianGreen, readableGreenFloor)
strictness  = factor * penalty
g = planckianGreen + (targetGreen - planckianGreen) * strictness
```

若这块屏的绿「单位亮度携带的褪黑素剂量」明显高于红，就往更深的红/琥珀压，但保留可读绿地板，避免文字变成刺眼的洋红红。

#### 8.2.7 常量 Y：在余量内补回暖移丢掉的亮度

`PreserveNightLuminance` 用线基 Y 行点积定义白亮度：

```
WhiteLuminance(m, basis) = m[1,0]*m.R + m[1,1]*m.G + m[1,2]*m.B
s = Math.Min(1.0 / y, ceiling / (dimmedWhiteFraction * maxChannel))
```

（`ColorAdjustments.cs:752-764, 786-807`。两张矩阵都是白归一化的，所以这个「wire Y」就是 CIE Y，没有第二个色度量可选。）
理想重缩放是 `1/Y(m)`，被余量上限钳住，`ceiling` 再夹到 [1,4]，`s <= 1` 时原样返回——**部分保持是优雅降级，不是错误**。
SDR 满亮度时 `ceiling = maxChannel = 1`，故 `s = 1`，注释诚实写明它「在 SDR 上是惰性的」；真正生效需要 **HDR 余量**
（`ceiling = min(2.0, HdrPeakNits/SdrWhiteLevel)`，`GammaApplyService.cs:415-420`）或**调暗后的 SDR**。`UltraNight` 被排除：
它的压暗是刻意的剂量削减，补回来等于反转模式目的。

#### 8.2.8 逐通道微调与合成顺序

- **Tint**（±50）：`t = tint/50`；偏绿时 `r,b = 1 − |t|·0.08`、`g = 1 + |t|·0.10`；偏洋红时 `r,b = 1 + t·0.08`、
  `g = 1 − t·0.12`（:467-491）。
- **白点微调**：`TemperatureOffset` 与手动 `Temperature` 必须先在 **mired 空间**合成再转 Kelvin，否则「夜间暖移 + 用户
  白点微调」会在暖端过冲：

```
referenceMired = 1e6 / 6500.0
miredA = 1e6 / (6500.0 + scaleA * 70.0)
miredB = 1e6 / (6500.0 + scaleB * 70.0)
composedMired  = miredA + (miredB - referenceMired)
composedKelvin = 1e6 / composedMired
return (composedKelvin - 6500.0) / 70.0
```

（`ComposeTemperatureScaleMired`，:669-693；单侧为 0 时短路直接相加，保证单一调整逐位不变。）
`EffectiveTemperatureScale = Compose(Temperature, TemperatureOffset)`（:695-696）。
- **合成顺序**（`ApplyUserAdjustmentsLinear`，:545-650）固定六步：`暗部 toe → Dimming → Temperature → Tint → RGB Gains →
  RGB Offsets → 钳位`；钳位上界在常量 Y 生效时抬到 `max(1.0, NightLuminanceCeiling)`，否则 1.0。
- **Dimming**（保留暗部的光度域压缩）：`gammaBoost = 1.0 + (1.0 - brightness) * 0.3`，
  `output = pow(value, 1.0 / gammaBoost) * brightness`；绝对 nits 版把 `value` 换成 `nits/sdrWhiteLevel` 再乘回 `white`
  （`ApplyDimming` / `ApplyDimmingNits`，:38-96）。

#### 8.2.9 `NightMelanopicCoefficients` 与 `LutGenerator` 的输入结构

`NightMelanopicCoefficients.cs`（40 行）是六个数 + 来源名的不可变容器（三原色的褪黑素响应与亮度响应），派生三个
`MelanopicPerLuminance` 比值，守卫为 `(IsFinite(n) && IsFinite(d) && d > 1e-12) ? n / d : 0.0`；注释说明归一化服务于
**比值**而非绝对 mel-EDI。唯一下游消费者是 `UltraNight` 的绿削减。

`LutGenerator.cs`（688 行）产出 **1024 点 1D LUT**（R/G/B + Grey），签名 `GenerateLut(GammaMode, sdrWhiteLevel,
CalibrationSettings, bool isHdr)`。缓存键是逐字段完整的 `CalibrationCacheKey`（:34-63），含亮度、温度、tint、增益、偏移、
算法、`PerceptualStrength`、`PreserveNightLuminance`、`NightLuminanceCeiling`、`ShadowDetailStrength/Pivot`、
`NightModeCcssPath`、六个褪黑素系数、`ProfileId`、`MeasuredCorrectionLut`；上限 100，越界整体清空（:65-69）。
注释有硬契约：**返回数组只读**，缓存直接共享引用以免滑块拖动时每次 32 KB 分配。

**SDR 路径**（:157-198）：按目标 gamma 解码（`Gamma24 → 2.4`，其余 2.2）→ `ApplyUserAdjustmentsLinear(..., Srgb)` →
按显示原生 ~2.2 重编码 `pow(x, 1.0/2.2)`。注释点明 gamma 模式控制的是**信号解码**而非显示：SDR 内容按 2.2 母版，
故 `Gamma22`/`WindowsDefault` 净效果是直通，`Gamma24` 才真的压暗阴影。**HDR 路径**（:200-318）：PQ EOTF 解码 →
`SrgbInverseEotf` 回到信号域 → 施加 gamma → 折算 nits → 归一化后 `ApplyUserAdjustmentsLinear(..., Rec2020)`
（注释：sRGB 推导的比值放进 Rec.2020 容器会落在黑体轨迹之外并**偏欠暖**）→ PQ 重编码；`linear > sdrWhiteLevel` 的
高光段用 smoothstep 把残差修正淡到零以保留 HDR 创作意图，常量 Y 生效时先把 `[sdrWhite, 10000]` 重映射到
`[boostAnchor, 10000]`（:214-228, 327-336）。

---

### 8.3 生理量与剂量：从光谱到「今晚累积了多少」

#### 8.3.1 褪黑素剂量的三个数

`MelanopicCalculator.Compute`（`Calibration/MelanopicState.cs:53-97`）是纯函数：输入 CCSS 通道光谱 + 线性增益 +
白亮度 + 视立体角，输出 `MelanopicReading`。核心：

```
stateSpd[i]    = max(0,gR)·R[i] + max(0,gG)·G[i] + max(0,gB)·B[i]
baselineSpd[i] = R[i] + G[i] + B[i]                    // 6500 K 恒等白
yRel           = Photopic(stateSpd) / Photopic(baselineSpd)
screenNits     = whiteLuminanceNits · yRel
eV             = screenNits · Ω_eff                      // 角膜照度，E_v ≈ L·Ω
melEdi         = eV · melDER_state                       // mel-lux
reduction      = 1 − (yRel · melDER_state) / melDER_baseline
```

分工注释很直白：**亮度压暗不属于 `linearGains`**（它缩放幅度而非光谱形状），必须经 `whiteLuminanceNits` 传入。
`Ω_eff` 默认 `0.20 sr`（约 27″ 16:9 面板在 60 cm、投影后），注释强调 `melEdi` 是**唯一**依赖该假设的数，
`reduction` 是无几何依赖的主指标（Ω、亮度、白点全部约掉）。无 CCSS 时 `GenericPrimaries` 合成高斯原色：
380–935 nm 步长 5 nm（81 点），窄/宽色域两组峰值（红 615/630、绿 545/530、蓝 455/455），`σ = FWHM/2.3548`。

#### 8.3.2 `MelanopicMonitorService`：为什么必须有专用线程

`MelanopicMonitorService.cs`（294 行）的注释是全仓库最直白的一段性能实话：apply 处理器**绝不能**在调用线程上算数学、
做 I/O 或发事件——渐变期间那个线程是 **UI 线程**，「每 tick 一次褪黑素数学 + 一次 JSONL 追加」曾把整个系统卡死。
因此 `OnStateApplied` 只把最新快照塞进 `_pending`（按设备路径去重）并 `_wake.Set()`；单个后台线程（`"MelanopicWorker"`、
`IsBackground = true`、`Priority = BelowNormal`）唤醒后整批取走、逐条 `Evaluate`。

**有界性是「每显示器一条最新值」而非定长队列**：同一显示器在处理前只保留最后快照，慢消费者只丢中间值、不积压。采样由状态变化
+ `KeepaliveMinutes = 5.0` 保活定时器驱动（把最后快照时间戳换成现在再入队，让剂量积分在屏幕静止时继续推进）；**写盘节流
`MinAppendIntervalSeconds = 60.0`**：面板数字即时更新，剂量曲线每显示器每分钟最多一行。校准旁路期间跳过。关闭同步：置
`_disposed`、退订事件、停表、`_wake.Set()`、`_worker.Join(2s)`；**Join 超时则不 Dispose 那个 wait handle**（后台线程可能仍在
读文件，注释宁可留给进程回收）。光谱来源带出处：Gloam 自抓 CCSS（文件头 `ORIGINATOR` 含 `Gloam`）> 同型号社区 CCSS
（`DbMatchedSameModel`）> 通用原色（`GenericOrOtherPanel`，`HasSpectra = false`）。

#### 8.3.3 `MelanopicDoseStore`：JSONL，不做数据库

`MelanopicDoseStore.cs`（163 行）：每天一个 `%LocalAppData%\Gloam\melanopic\yyyy-MM-dd.jsonl`，一行一个 `MelanopicDoseSample`
（时间戳、设备路径、mel-EDI、扩展不确定度、reduction、Kelvin、是否有光谱）。写入走进程内锁；读取容忍**崩溃撕裂的尾行**；
保留期默认 90 天。累积剂量用梯形积分、按显示器分组求和（多显示器求和被注释标注为「上界估计」）：

```
hours = (t[i] - t[i-1]).TotalHours
if (hours <= 0 || hours > 6) continue          // 断档 = 熄屏/睡眠，不跨缺口积分
dose += 0.5 * (max(0, edi[i-1]) + max(0, edi[i])) * hours
```

#### 8.3.4 `CircadianDoseGovernor`：剂量上限反过来改白点与亮度

`CircadianDoseGovernor.cs`（215 行）解反问题：日程给出的 `(kelvin, brightness)` 可能超上限，要找**最不显眼**的合规点。
硬约束是**永不变亮、永不变冷**。搜索空间：Kelvin 从日程值向下到 1900（步长 50），亮度向下到
`MinBrightnessPercent = 10.0`。固定 Kelvin 时 mel-EDI 与白亮度成线性，故最小压暗是**闭式解**：

```
probeEdi          = ReadingAt(kelvin, scheduledBrightness).MelanopicEdiLux
neededBrightness  = scheduledBrightness * ceiling / probeEdi
feasible          = neededBrightness >= MinBrightnessPercent
brightness        = min(scheduledBrightness, max(neededBrightness, MinBrightnessPercent))
edi               = probeEdi * brightness / scheduledBrightness
```

代价函数是 CAM16-UCS 的 ΔE′，观视条件锚在**日程状态**（`DisplayConditions`：`L_A = Y_white/5`、`Y_b = 20`、dim surround，
`Calibration/Cam16Ucs.cs:33-45`），所有候选共用同一条件保证可比。选择规则：先按 `CeilingMet` 优先，合规者取最小 ΔE′，
都不可达时取**残余剂量最小**者并标 `CeilingMet = false`。一条必须记录的**单调性守卫**：候选搜索用 25 K / 2% 的桶缓存
（`KelvinCacheBucket = 25`），但「把搜索锚点向上取整」绝不能变亮或变冷，因此缓存解一旦出现
`Kelvin > requestedKelvin || Brightness > requestedBrightness || !Adjusted` 就**放弃缓存、按请求值重算**（:112-119）。
缓存上限 512，键含光谱 + 系数的 SHA256 指纹。

接线（`GammaApplyService.ApplyDoseCeiling`，:532-584）：每显示器独立求解（剂量取决于面板光谱、白点、亮度）；解出的
Kelvin 已含该显示器的温度微调（喂进去的是合成后的有效 Kelvin），故把 `TemperatureOffset` 折成 0，只写 `Temperature`
与 `Brightness`；日志「每个不同解每显示器只记一次」，因为渐变期会以高达 4 Hz 重解。

#### 8.3.5 `JndPacedFade`：把「不可感知」变成可算的步长上限

`JndPacedFade.cs`（138 行）用 ΔE ITP（BT.2124，1 单位 ≈ 1 JND）替换固定 0.05 mired 启发式。常量：`TargetStepItp = 0.5`
（每次硬件写半个 JND）、`ReferenceWhiteNits = 100.0`、`ProbeMired = 0.5`（有限差分探针）、护栏
`[MinStepMired = 0.01, MaxStepMired = 2.0]`、回退 `FallbackStepMired = 0.05`。

```
mired      = 1e6 / clamp(currentKelvin, 1000, 10000)
kelvinLow  = round(1e6 / (mired + 0.5))     // 更暖的探针
kelvinHigh = round(1e6 / (mired - 0.5))     // 更冷的探针
g    = ΔE_ITP(lowWhite, highWhite) / |1e6/kelvinLow - 1e6/kelvinHigh|
step = clamp(0.5 / g, 0.01, 2.0)
```

两侧白点由 `AdaptedWhiteXyz` 生成：对给定 Kelvin 调 `GetTemperatureMultipliers(..., NightBasis.Srgb)`，常量 Y 生效时再
`RescaleToConstantLuminance(..., 2.0, 1.0)`（名义 HDR 余量），然后 `LinearSrgbToXyz` × 100 nits。**因为走的就是真实算法
路径，`UltraNight` 的刻意压暗与常量 Y 的平坦亮度都会被自动定价。** 开启常量 Y 时代码会**两种变体都算一遍取较大者**
——一条全局渐变同时驱动多条显示路径，而 SDR 路径无法保持常量 Y，所以要向「更可见」的一侧对齐。注释里的诚实说明值得
保留：渐变中相邻适应白之间的瞬时 ΔE ITP 是**保守上界**（缓慢漂移时色适应会让变化更不易察觉），0.5 JND 是「每次硬件写」
的预算，参考白固定 100 cd/m²（JND 在 50 nits 以上近似 Weber 平坦）。

> 端到端量化链：渐变在 mired 域连续 → 取整到 1 K → JND 每写 ≤0.5 ΔE ITP → 定时器间隔 16.7–500 ms → 硬件合并器
> 每显示器 ≥250 ms（实际 ≤4 次写/秒）；日程时长与 JND 冲突时日程优先、只记一条日志。
> **平滑度由 mired + JND 决定，吞吐由 250 ms 决定，两者互不越权。**

---

### 8.4 Game Lab 核心：按游戏覆盖画面意图

#### 8.4.1 Profile 模型与「失败关闭」边界

`GamerProfileRule`（`GamerMode.cs:128-302`）是每个可执行文件一条的持久化规则，字段分四组：身份（`AppName`、
`ExecutablePath`、`DisplayName`；有 path 时**名字与路径必须同时匹配**，防 `game.exe` 撞名）、画面意图
（`PictureIntent`、`OverrideGamma`、`GammaMode`、`ShadowDetailStrength/Pivot`）、输出范围（`DisplayScope` +
`MonitorDevicePath`，`SpecificDisplay` 缺路径时回落 `WindowDisplays`）、夜间与 HDR（`NightPolicy`、
`NightOpsKelvin=3400`、`NightOpsStrength=0.55`、`NightOpsMelanopicCeiling`、`GameplayLock=true`、`HdrExpectation`、
`HdrCapability`、`PaperWhiteNits/PeakNits/BlackLevelNits`，0 = 未知）。`Sanitized()` 是唯一入口守卫（`ShadowDetailPivot`
钳 [0.02,0.25]、`NightOpsKelvin` 钳 1900–6500 等）；`SemanticallyEquals` 把「是否同一份配置」定义成逐字段比较
（可排除 `LastUsedUtc`），这是 §8.5 会话保留的基础。

`GamerExecutableSafety`（:60-121）是**失败关闭**边界：精确 exe 黑名单（浏览器、外壳、`gloam.exe`、Steam/Epic 启动器、
VR 运行时、反作弊服务等 40+ 项）、exe 名片段（`anticheat`、`launcher`、`overlay`、`installer`、`webview`…）、
精确标题与标题片段（`SteamVR`、`dedicated server`、`modding tools`…）。`IsSafeProfileTarget` 是唯一判据，被发现、
会话、协调器三处共同调用——**旧设置文件里手写的启动器名字也拿不到显示所有权**。

#### 8.4.2 五个画面意图预设

`GamerPresetCatalog.Apply`（:318-365）先统一置 `OverrideGamma = true`、`GameplayLock = true`、`HdrExpectation = Automatic`，
再按意图覆盖：

| 意图 | `OverrideGamma` | `GammaMode` | 暗部 toe | `NightPolicy` | 其它 |
|---|---|---|---|---|---|
| `Reference` | **false** | `Gamma22` | 0.0 | `ForceDaylight` | 保持面板校准，不加暗部提升 |
| `CompetitiveClarity`（默认） | true | `Gamma22` | 0.55 @ 0.10 | `FollowSchedule` | 跟随日程，晚间仍会暖 |
| `CinematicHdr` | true | `WindowsDefault` | 0.0 | `ForceDaylight` | `RequireHdr`；PQ 已是 HDR 传递函数，再叠 SDR 2.4 会整形真实 HDR 暗部/中间调 |
| `NightOps` | true | `Gamma22` | 0.45 @ 0.10 | `NightOps`（3400 K / 0.55） | `NightOpsMelanopicCeiling = 0`：硬上限会同时大改亮度与白点，必须用户显式选择 |
| `Custom` | 不改动 | — | — | — | 早退，保留用户值 |

预设只覆盖「渲染意图 + 启动策略」；**实测面板校正始终留在显示器校准档案，由正常 apply 管线先合成**。

#### 8.4.3 暗部可见度曲线：黑锚定、C¹ 连续、pivot 之上恒等

`GamerShadowVisibility`（`GamerMode.cs:373-396`）——本节第二条必须逐字复述的公式：

```csharp
private const double MaximumToeAmount = 1.5;

public static double Apply(double linear, double strength, double pivot = 0.10)
{
    linear   = IsFinite(linear)   ? Clamp(linear, 0.0, 1.0) : 0.0;
    strength = IsFinite(strength) ? Clamp(strength, 0.0, 1.0) : 0.0;
    pivot    = IsFinite(pivot)    ? Clamp(pivot, 0.02, 0.25) : 0.10;
    if (strength <= 0.0 || linear <= 0.0 || linear >= pivot) return linear;

    double u = linear / pivot;
    double shoulder = 1.0 - u;
    double lifted = linear + MaximumToeAmount * strength * linear * shoulder * shoulder;
    return Math.Min(lifted, pivot);
}
```

形状（可逐条验证）：黑锚定在 0；`linear = pivot` 时 `shoulder = 0`，故 `lifted = pivot` 且一阶导为 1——**C¹ 连续**，
pivot 以上逐位不变；`Math.Min(..., pivot)` 保证不越过接头。单调性由解析最小斜率给出：

```
MinimumSlope(strength) = 1.0 - MaximumToeAmount * strength / 3.0
```

strength ∈ [0,1] 时最小 0.5 > 0，故对任意允许设置都单调；它**在线性光里求值**。应用位置是
`ApplyUserAdjustmentsLinear` 第 1 步（`ColorAdjustments.cs:558-575`）：先按线基 Y 行权重算 `shadowLuminance`
（Rec.2020 用 `0.2627/0.6780/0.0593`，sRGB 用 `0.2126/0.7152/0.0722`），提升后得 `requestedGain = lifted/shadow`，
再被 `headroomGain = 1/maxChannel` 钳住，最后 **RGB 同乘一个增益**——暗部彩色细节留在同一色相射线上，且任何通道都不削顶。

#### 8.4.4 `GamerModeCoordinator`：焦点事件 → 会话

`GamerModeCoordinator.cs`（372 行）是 Game Lab 的**唯一决策者**，托盘与仪表盘都是它的视图。

- **焦点合并 + 稳定延迟**：`ObserveForeground` 先归一化应用名、忽略 `Gloam.exe` 自身，再算 settle delay：候选是游戏 →
  **350 ms**；已有会话仍在（切出/切回）→ **900 ms**；其它 → **250 ms**（:101-124）。延迟经 `LatestValueCoalescer` 的
  等待句柄实现，即后续焦点事件会取消前一个待定评估。
- **2 秒运行策略校验**：`_runningAppTimer` 固定 2 s 周期调 `RefreshRunningApps()`，**仅当**存在
  `Enabled && ApplyWhileRunning` 的配置时才重评估（:72-73, 147-151）。该语义是「不必等到前台也能接管」，
  故用 `Process.GetProcessesByName` 抓运行实例的主模块路径与窗口矩形，再走与焦点相同的匹配函数（:153-181）。
- **匹配与范围**：`MatchesForeground` 先验安全边界，再要求 `AppName` 忽略大小写相等；带 `ExecutablePath` 时双方路径
  都要存在且全路径相等。`TargetsMonitor`：`AllDisplays` → 全部；`SpecificDisplay` → 设备路径相等；默认按窗口矩形与
  显示器矩形**相交**，无矩形且只有一块屏 → 该屏（:312-357）。
- **应用排除优先**：被排除应用若 `FullDisable` 屏蔽所有显示器，否则只屏蔽与窗口相交的那些；屏蔽集合经
  `UpdateBlockedMonitors` 交给 apply（被屏蔽显示器强制 6500 K）。
- **单拥有者与紧急暂停**：一次评估最多产生一个 exe 的多条 assignment（每显示器一条，取 `GroupBy` 最后一项）；
  `EmergencySuspend()` 只做 `Interlocked.Exchange(ref _emergencySuspended, 1)` + 立即重评估——**纯内存**，
  设置目录不可写也能生效，保证 panic 热键后保存过的配置不会自我重新武装。
- **副作用最小化**：仅 `gamerChanged && profile != null && assignments.Count > 0` 时
  `Task.Run(() => MarkGamerProfileUsed(...))`（只为「最近游戏」排序）。

#### 8.4.5 `GameDiscoveryService`：读清单，不爬盘

`GameDiscoveryService.cs`（697 行）不做全盘 exe 扫描，只读启动器自己维护的小清单，且从不启动、打补丁或注入。四个来源：
**Steam**（注册表 `SOFTWARE\Valve\Steam` 三个 hive/view → `libraryfolders.vdf` 的 `"path"` → 各库 `appmanifest_*.acf` 的
`name`/`installdir`）、**Epic**（`%ProgramData%\Epic\...\Manifests\*.item` 的 `DisplayName`/`InstallLocation`/`LaunchExecutable`）、
**GOG**（注册表 `SOFTWARE\GOG.com\Games\<id>`，HKLM/HKCU × 64/32）、**Xbox**（各固定盘 `XboxGames\<title>\Content\MicrosoftGame.Config`
中含 `Executable` 属性的节点）。**exe 评分**（:360-381）是这里最实用的经验规则：

```
score = 100 - depth * 15
if (fileKey == installKey) score += 160
if (fileKey == gameKey)    score += 160
if (installKey.Length >= 4 && (contains)) score += 70
if (gameKey.Length    >= 4 && (contains)) score += 70
if (目录含 binaries/shipping/win64/win_x64/win_x86) score += 35
score += min(fileSize / 20 MB, 40)          接受条件：score >= 90 且未命中安全黑名单
```

BFS 边界：深度 ≤4、每层子目录 ≤80、候选 exe ≤500（:315-358）。**HDR 能力探测**（:439-544）只在启动器目录附近的**有界文件集**
里找强 token（`bUseHDRDisplayOutput`、`r.HDR.EnableHDROutput`、`EnableHDR`、`HDR10`、`scRGB`…）：≤48 目录、每目录 ≤40 文件、
总数 ≤120、单文件 ≤2 MB，跳过 readme/license/changelog；命中返回 `Detected + "token in 文件名"` 作证据，否则 `Unknown`——注释明确拒绝
从渲染 API 或商店分类去猜。缓存是**进程级静态**（关掉再开 Game Lab 不丢）：每来源一条、5 分钟生命周期、键为「路径 + 文件长度 +
最后写入时间」指纹，且命中还要求列表中每个 exe **仍然存在**（:552-587）。最终 `Normalize`：安全过滤 → 按 exe 路径去重 → 按显示名排序。

#### 8.4.6 前台窗口与 DXGI 信号

`AppDetectionService.cs`（115 行）用 `SetWinEventHook(EVENT_SYSTEM_FOREGROUND, ..., WINEVENT_OUTOFCONTEXT)` 监听前台切换；
对 `hwnd` 取 `GetWindowThreadProcessId`，**`threadId == 0` 或 `pid == 0` 直接返回**（不要复用未定义 pid 或报告陈旧应用）；
进程名统一为 `p.ProcessName.ToLowerInvariant() + ".exe"`，可执行路径取不到则为 `null`，`GetWindowRect` 失败则传 `null` 矩形；
`Start()` 里还会主动 `CheckForeground()` 一次做初始同步。`DxgiColorSpaceInfo.cs`（59 行）是 `DXGI_COLOR_SPACE_TYPE` 解码表：
`IsHdr` 认定 **12/13/14/16/18/19**（RGB full G2084 P2020、YCbCr studio G2084、RGB studio G2084、YCbCr studio G2084
top-left、GHLG 两种）；`DecodeBitsPerColor` 把 0–5 映射为 Unspecified/6/8/10/12/16 bpc。注释点明实际路径可能是 RGB、
studio RGB 或 YCbCr（取决于 GPU、传输、TV 模式与驱动），所以 HDR 判定必须看**信号**而非「HDR 开关」。

---

### 8.5 与输出管线的接口：会话回执与「语义相同不重置」

`GammaApplyService.cs`（686 行）是 Game Lab 与夜间模式唯一的会合点。输入 `GamerSessionAssignment(MonitorInfo Monitor,
GamerProfileRule Profile)`（`GamerMode.cs:556`），输出只读的 `GamerSessionSnapshot`（:539-553）。`UpdateActiveGamerSessions`
（`GammaApplyService.cs:134-212`）的契约：①过滤空 assignment、未启用配置与**不安全目标**；②**单拥有者**——多 exe 竞争时
只留第一个、其余记日志丢弃，每显示器一条（`GroupBy(HMonitor).Select(g => g.Last())`）；③**会话保留**——若已有会话且
`previous.Profile.SemanticallyEquals(profile, includeRecency: false)`、`SignalKey`、设备路径三者都相同，则**原样保留**
`LockedKelvin` 与 `StartedUtc`（只刷新 `Monitor` 引用），这就是「语义相同不重置会话」，焦点钩子的重复投递无法在玩家
脚下移动 Gameplay Lock；④否则新建会话，此刻 `LockedKelvin = _nightModeService.CurrentNightKelvin`（**捕获色温**）、
`StartedUtc = UtcNow`，并重跑 `GamerSignalDiagnostics.Evaluate`；⑤`SignalKey` 是 `HashCode` 组合：`IsHdrActive`、
`DxgiColorSpace`、`BitsPerColor`、整数化 SDR 白点与 HDR 峰值、设备路径、档案的 `GammaMode`/`CalibrationProfileId`/
`Mhc2ProfileName`、校准状态指纹（:276-293）——信号或校准一变即重建会话并重新捕获色温，因为回执里的诊断已过期；
⑥集合真变了才触发 `GamerSessionsChanged`，返回值告诉协调器是否需要发 `PolicyChanged`。

会话内色温解析（`ResolveGamerKelvin`，:600-615）：

```
baseline = profile.GameplayLock ? clamp(session.LockedKelvin) : clamp(scheduledKelvin)
ForceDaylight => 6500
NightOps      => Math.Min(baseline, profile.NightOpsKelvin)    // 永不变冷
FollowSchedule=> baseline
```

`NightOps` 用 `Math.Min` 的理由写在注释里：白天启动一个 Night Ops 游戏，不应把用户更严格的深夜状态变亮/变冷。两个覆盖
优先级：`NightModeManuallyDisabled`（全局热键）**最后**把 Kelvin 强行改回 6500，能压过任何游戏配置；设置窗口的显式预览
（`nightKelvinOverride`/`manualCalibration`）临时把 `gamerSession = null`，让控件不被前台游戏「冻结」（:353-375）。
`NightOps` 还会用 `BuildNightOpsSettings` 合成「算法固定 `Perceptual`、强度 = `NightOpsStrength`、`PreserveLuminance = false`」
的临时夜间设置（:617-653）。回执字段来源（`CreateGamerSessionSnapshot`，:236-257）：设备路径/友好名/`HMonitor` 来自
`MonitorInfo`；应用名/显示名/意图/`GameplayLock`/`NightPolicy`/暗部参数来自 profile；`EffectiveGamma` 在 `OverrideGamma`
时取 profile 的 `GammaMode`，否则取显示器档案的 `GammaMode`，再退到 `MonitorInfo.CurrentGamma`；`LockedKelvin` 由
`ResolveGamerKelvin` 现算；`Diagnostics` 是会话建立时冻结的那份列表。

**诊断码**（`GamerSignalDiagnostics.Evaluate`，`GamerMode.cs:433-513`）每条都是可验证事实而非猜测：
`HDR_REQUIRED_INACTIVE`(Critical)、`SDR_REQUIRED_HDR_ACTIVE`(Warning)、`HDR_COLOR_SPACE_MISMATCH`(Critical)、
`HDR_LOW_BIT_DEPTH`(Warning)、`HDR_PEAK_UNKNOWN`(Information)、`HDR_HEADROOM_LOW`(Warning，SDR 白点 > 面板峰值 80%)、
`HDR_GAMMA_OVERRIDE`(Information)、`DISPLAY_UNMEASURED`/`DISPLAY_CALIBRATION_INACTIVE`/`DISPLAY_CALIBRATION_UNVERIFIED`、
`GAMEPLAY_LOCK_OFF`。校准状态区分「有存档」「Windows 已确认关联」「无法验证」三态——文件名存在本身**从不**算作已生效。

---

### 8.6 不变量、风险与文件职责表

**承重不变量**（改动等于改产品语义）：①**6500 K 恒等**——目标与参考走同一管线与线基，必须逐位返回 (1,1,1)；
②**白天判据唯一**——`< 6450` 在服务与 apply 两侧复用；③**事件不重复**——`BlendChanged` 每次有效变化只发一次；
④**锁外发事件**——`_stateLock` 内绝不触发 `BlendChanged`；⑤**单 exe 所有权 + 安全黑名单**不可绕过；⑥**语义相同不重置会话**
（`SemanticallyEquals` + `SignalKey` 同时相同才保留 `LockedKelvin`/`StartedUtc`）；⑦**日程承诺**——渐变时长优先于 JND，
被打破只记日志、绝不悄悄拉长；⑧**剂量治理永不变亮/变冷**，缓存锚点向上取整也不允许。

**风险与静态发现**（供维护核对）：`BlueReduction` 在设置加载时被改写为 `Perceptual`（`SettingsNormalization.cs:132-134`），
枚举与实现仍在但正常路径不可达；`GetAccurateMultipliers` 注释称走「Kang 近似」，实现实际走黑体轨迹表、Kang 只是回退
（`ColorMath.cs:671-715`）；`NightModeService.Refresh()` 注释指向恢复/时钟变化，但 `src/` 无调用点（`TimeChanged` 实际走
`UpdateSettings`，靠克隆对象使缓存键失效）；`NightModeSettings`(1900–6500) 与 `ColorAdjustments`(1000–10000) 钳位不同，
跨层只能靠调用方自觉、无编译期保证；常量 Y 在 SDR 满亮度下数学上恒为 1（诚实但无效果），HDR 峰值未上报（0）时回落 1.0；
`MelanopicMonitorService` 的「有界」是「每显示器一条最新值」，慢消费者**丢中间样本**，剂量曲线是采样曲线而非逐帧积分；
全部剂量结论依赖 CCSS 光谱与 `Ω_eff = 0.20 sr` 假设，无 CCSS 时只能保证**比值**有意义。

| 文件（`src/Gloam.Core/`） | 行数 | 职责 |
|---|---|---|
| `NightModeService.cs` | 741 | 设置模型 + one-shot 定时器调度 + mired 渐变 + `BlendChanged` |
| `NightModeSchedulePoint.cs` | 51 | 固定时间/日出/日落三触发，极昼极夜回退 07:00/19:00 |
| `NightModeAlgorithm.cs` | 41 | 五种白点算法枚举与意图文档 |
| `SunCalculator.cs` | 197 | NOAA 太阳位置：儒略日、均时差、90.833° 天顶角、极昼极夜标志 |
| `ColorAdjustments.cs` | 821 | 全部白点/暗部/dimming 数学与 `NightBasis` 线基规则 |
| `NightMelanopicCoefficients.cs` | 40 | 三原色褪黑素/亮度系数容器与安全比值 |
| `Calibration/ColorMath.cs` | 1256 | CAT16（逆矩阵现算）、RGB↔XYZ、黑体轨迹、CCT/Duv、CAM16-UCS |
| `JndPacedFade.cs` | 138 | ΔE ITP 节拍：每写 ≤0.5 JND 的 mired 步长 |
| `CircadianDoseGovernor.cs` | 215 | mel-EDI 上限 → 最不显眼的 (K, 亮度) 闭式解 + 缓存 |
| `MelanopicMonitorService.cs` | 294 | 专用线程评估、5 min 保活、60 s 写盘节流、关闭同步 |
| `MelanopicDoseStore.cs` | 163 | 每日 JSONL 剂量日志、梯形积分、90 天保留 |
| `LutGenerator.cs` | 688 | 1024 点 1D LUT：SDR 直通/2.4 与 HDR PQ 两条路径 |
| `GamerMode.cs` | 557 | Profile 模型、五预设、暗部 toe、安全黑名单、诊断码、快照记录 |
| `GamerModeCoordinator.cs` | 372 | 焦点合并、2 s 运行校验、显示器范围、单拥有者、紧急暂停 |
| `GameDiscoveryService.cs` | 697 | Steam/Epic/GOG/Xbox 清单、exe 评分、HDR token 探测、5 min 缓存 |
| `AppDetectionService.cs` | 115 | `EVENT_SYSTEM_FOREGROUND` 钩子与前台窗口矩形 |
| `DxgiColorSpaceInfo.cs` | 59 | DXGI 色彩空间/位深解码与 HDR 信号判定 |
| `GammaApplyService.cs` | 686 | 会话契约、策略解析、250 ms 合并、10 s ramp 守卫、剂量接线 |

---

## 9 校准子系统（一）：领域核心

本章覆盖 `src/Gloam.Core/Calibration/` 下的全部 57 个 `.cs` 文件——校准子系统的领域核心：补丁生成、测量协议、色度学与建模、MHC2 profile 合成与安装、验证与不确定度。补丁窗口、向导步骤、进度/报告界面属于 WPF 层（`src/Gloam`），不在本章范围；凡涉及 UI 的地方只标注交接点。

### 9.1 职责与规模

校准是这个仓库里唯一"硬件在环"的子系统：它必须把一个外接色度计（或光谱仪）实际读到的光，变成一个 Windows 会真正执行的色彩修正。因此它的代码量远超其他模块，而且大到不能靠"读一遍就会"的方式理解。

实测规模（本章所有数字均由仓库当次快照统计得出）：

| 指标 | 数值 |
| --- | --- |
| 文件数 | 57 个 `.cs` |
| 总行数 | 24,945 行（非空 21,955 行） |
| `src/Gloam.Core` 总计 | 93 个 `.cs`，35,871 行 |
| 占 `Gloam.Core` 比例 | 文件 ~61%，行数 ~70% |
| WPF 应用层 `src/Gloam` | 68 个 `.cs`，22,235 行 |

单文件行数前 12（说明重心在哪）：

```
CalibrationOrchestrator.cs     1681   Lut3DGenerator.cs        1487
SpotreadSession.cs             1325   ColorMath.cs             1256
ColorimeterService.cs           997   PatchSetGenerator.cs      940
CalibrationReport.cs            890   CalibrationProfileInstaller.cs 875
CalibrationProfile.cs           688   Mhc2ProfileBuilder.cs     688
CalibrationTarget.cs            594   ColorTypes.cs             564
```

规模为什么这么大，而不是被拆成几个包：校准的每一层都直接面对物理世界，而物理世界不接受抽象。

1. **测量不可重来**：一次 Standard 校准 ~15 分钟、Full ~60 分钟（`PatchSetGenerator.GetEstimatedTime`）。任何"先跑一遍看看、不行再来"的设计都会让用户等待翻倍，所以判据、回退、重试都必须内建在流程里，而不是放在调用者手里。
2. **失败模式是硬件级的**：USB HID 被别的程序占住、探头的环境光挡板没转开、OLED 的 ABL 在整屏白之后几秒内还在衰减、OS 报的 SDR 白点比真实值偏 7%、Windows 25H2 把 Advanced Color profile 写进注册表却让 `ColorProfileGetDisplayList` 返回 0 条——这些都不是"异常"，而是必须在代码里被命名、被检测、被解释的具体现象。每一处这样的注释背后都是一次现场失败。
3. **公式必须自己写且有据可查**：PQ（ST.2084）、BT.1886、Bradford/CAT16、CAM16-UCS、Ohno 的 CCT/Duv、ΔE2000、ΔE ITP（BT.2124）、Oklab 色域压缩。仓库里没有第三方色彩库依赖，这些全部是本目录（以及 `src/Gloam.Core/TransferFunctions.cs`）里的静态类，且每个都注明出处。
4. **Windows 侧的 profile 语义是"隐式契约"**：MHC2 标签的二进制布局、矩阵的 XYZ 域包裹、ICC 特征标签的 D50 PCS 约定、Advanced Color 关联的作用域——这些没有可调用的托管 API，只能靠按字节读写 `.icm` 并逐一验证。

### 9.2 端到端管线

一次校准从目标选择走到"Windows 已经在应用这个修正"，跨越四个阶段：**采集 → 归一化/校验 → 建模 → 合成/安装/验证**。下图是真实类型名与真实调用方向的浓缩（`【…】` 标出条件/可选路径，其余是主路径）：

```
CalibrationSetup（WPF 向导状态：target / preset / displayType / 校正文件）
   │
   ▼
CalibrationOrchestrator.StartCalibrationAsync
   ├─ PatchSetGenerator.GeneratePatchSet(target, preset) ── 补丁序列（含 DriftCheck 锚点）
   │     └─ preset=Adaptive 时只给 SEED，后续轮次由 AdaptivePatchPlanner 追加
   ├─ ColorimeterService.BeginMeasurementSessionAsync(hdrMode)
   │     └─ SpotreadSession.StartAsync ── spotread.exe（ARGYLL_NOT_INTERACTIVE=1）
   │
   ├─ 逐补丁循环
   │     DisplayPatchRequested  ─▶ （WPF 全屏补丁窗，FP16 scRGB 或 sRGB）
   │     ComputeSettleDelayMs / SettleHighAplAsync / NeedsMultiRead / MedianMeasurement
   │     ColorimeterService.MeasureAsync ─▶ SpotreadSession.MeasureAsync ─▶ CieXyz
   │
   ├─【Adaptive】RunAdaptiveRoundsAsync：Lut3DGenerator.ComputeModelResiduals
   │              ─▶ AdaptivePatchPlanner.PlanNextBatch ─▶ 再测一轮
   ├─ DriftCompensator.Compensate(measurements) ── 把整轮归一化到 t0
   ├─【可选】RunClosedLoopAsync：ClosedLoopCorrector ─▶ Apply(VCGT) ─▶ 复测灰阶
   ▼
CalibrationResult { Measurements, CorrectedMeasurements, FinalCorrection, ... }

CalibrationResult.Measurements
   ├─ CalibrationMeasurementValidator.ValidateForProfile ── 硬性物理门槛（失败即拒绝建 profile）
   ├─ Lut3DGenerator.Generate ─▶ DisplayCharacterization + Lut3D         （SDR 模型）
   │     或 Lut3DGenerator.BuildCharacterizationOnly
   │        + HdrMhc2LutBuilder.Build ─▶ 三个 PQ 域 LUT                   （HDR 模型）
   ├─ CalibrationProfileInstaller.Install
   │     ├─ BuildGamutMatrixPlan（Mhc2ProfileBuilder.BuildGamutMatrix + GamutReachability 闸门）
   │     ├─ Mhc2ProfileBuilder.Build ─▶ 改写模板 .icm 的 MHC2 标签字节
   │     └─ AdvancedColorProfileAssociation.TryActivateInstalled ─▶ WCS 关联 + 回读校验
   └─ CalibrationVerifier.ComputeMetrics / TrustCheck.Compute
         ─▶ CalibrationReport（报告）+ TrustCheckHistory（长期趋势）
```

关键的分层纪律：**编排层（Orchestrator）不碰色彩数学，色彩数学不碰 Windows API，Windows API 不碰测量协议**。`Gloam.Core/Calibration` 中所有类型都是无 UI、可单测的；连 `SpectralCaptureService`、`MeterOffsetCaptureService`、`HdrRefinementLoop` 这种"明显需要硬件"的类，也是把"显示这个补丁""读一次"注入成委托，从而能在没有探头的情况下被测。

#### 9.2.1 CalibrationOrchestrator：状态机、事件与失败恢复

`CalibrationOrchestrator`（1681 行）是采集阶段的唯一权威。它持有 `ColorimeterService`、`CalibrationTarget`、`PatchSetGenerator.CalibrationPreset`，通过 6 个事件与 UI 通信：`DisplayPatchRequested`、`ProgressChanged`、`StateChanged`、`MeasurementTaken`、`ErrorOccurred`、`CalibrationCompleted`，另加一个 `PhaseChanged`（闭环/自适应阶段的短文本标签）。

状态机为 `CalibrationState`：`Idle → Initializing → Running ⇄ Paused → Completed | Cancelled | Failed`。`IsRunning` 把 `Running` 与 `Paused` 都算作运行中，从而 `StartCalibrationAsync` 在暂停期间重复调用会抛异常而不是并行开跑。两个关键实现细节：

- **锁与事件顺序**：`SetState` 在 `_stateLock` 内改状态、出锁后触发 `StateChanged`；`SetStateLocked` 专供 `Pause/Resume/Cancel` 使用，且注释明确"事件在持锁状态下触发，处理器必须轻量且不得回调编排器"。
- **成功与取消的原子判定**：`Cancel()` 可能在任意线程把状态改成 `Cancelled`。因此结束阶段在 `_stateLock` 内同时读取消标志与状态，再决定写 `Completed` 还是 `Cancelled`。注释写明了这么做的原因——否则两者之间的 `Cancel()` 会被覆盖，一次被取消的运行会以 `Success` 上报并装上一个坏 profile。另外，若 `Pause()` 恰好落在收尾的 `await` 期间，状态是 `Paused` 也必须完成（测量已经做完），否则编排器会永久卡在 `Paused` + `IsRunning=true`，把下一次运行挡死。

取消只在一个地方"软"处理：逐补丁循环在每个补丁边界 `ThrowIfCancellationRequested()`，收到 `OperationCanceledException` 后转 `Cancelled` 并返回已测到的部分 `CalibrationResult`。`UsbDriverException` 是唯一被单独 `catch` 后重新抛出的异常类型——因为它需要用户动手（装驱动/关掉占用设备的程序），UI 有专门的引导流程，吞掉它等于剥夺用户唯一的修复入口。

#### 9.2.2 补丁序列：PatchSetGenerator

`ColorPatch`（`MeasurementResult.cs`）是贯穿全流程的数据单元：`Name`、`DisplayRgb`（`LinearRgb`，信号域而非光域）、`Category`（`PatchCategory`）、`Index`、`IsCritical`、`TargetXyz`/`TargetLab`，以及 `Nits`——`Nits` 非空表示这是 HDR **线缆梯**（wire ladder）补丁，渲染走 FP16 scRGB，其 PQ 线缆位置是精确的，不受 OS 的 SDR 白点映射影响。

`PatchSetGenerator` 用一组可组合的构造器拼出各预设：`AddGrayscaleRamp`、`AddPrimariesAndSecondaries`、`AddPrimariesAtSaturation`、`AddSingleChannelRamps`、`AddGrid`、`AddNearNeutrals`、`AddSkinTones`、`AddShadowDetail`、`AddHighlightDetail`。

| 预设 | 名义补丁数 | 预估耗时 | 组成与取舍 |
| --- | --- | --- | --- |
| `Quick` | ~50 | 3 min | 黑/白 + 11 点灰阶 + 原色/间色 + 3×3×3 网格 + 近中性。**不插漂移锚点**：短跑窗口内漂移小，锚点开销占预算比例过高 |
| `Standard` | ~200 | 15 min | 默认路径，含漂移锚点 |
| `Thorough` | ~500 | 35 min | 增加单通道 R/G/B 斜坡（E4）与更大网格 |
| `Full` | ~1000 | 60 min | 网格最密，含肤色/阴影/高光细节集 |
| `GrayscaleOnly` | 21 | 2 min | 只测中性轴 |
| `Custom` | 调用方给 | — | `GenerateCustomSet(target, gridSize, grayscalePoints)`，显式不插锚点（预算由调用方掌控） |
| `Adaptive` | 预算 120 | ≤360 s（上界） | `GeneratePatchSet` 只返回 SEED，其余轮次在运行中产生 |

三个必须按顺序发生的收尾动作（`FinalizePatchSet`）：**去重 → 排序 → 插锚点 → 重编号**。

1. `Snap8Bit` 把信号量化到 8 bit 网格（真实显示链路的输入精度），`DeduplicateBySnappedSignal` 再按量化后的 `(R,G,B)` 去重并保留首次出现——稠密斜坡与网格节点在量化后会塌到同一个点，重复输入只浪费测量时间。
2. `OptimizePatchOrder` 从黑色起做贪心最近邻，距离键是 `0.2126·R + 0.7152·G + 0.0722·B`（**gamma 编码信号的 Rec.709 加权和**，不是真实线性亮度）；换类别时 +0.01 的轻微惩罚，避免同类补丁被拆散。注释明确了它的代价：步长最小化会产生准单调的亮度斜坡，把面板漂移（预热、ABL）系统性地混叠进 tone curve，这是漂移混叠的最坏情形；完全随机会解耦漂移但让每一步都变成大跳变，成倍增加 settle 时间并折磨 OLED 的下降沿。选择是"保留步长最小化，然后**直接测量漂移**"。
3. `InsertDriftAnchors` 在排序**之后**插入锚点，且此后绝不重排——锚点的价值就是它在时间轴上的固定周期位置。规则：起始先放一个白（t0 参考，后续所有比值以它为分母），之后每 25 个普通补丁复测一次白（`DriftWhiteIntervalPatches`）、每 50 个复测一次黑（`DriftBlackIntervalPatches`），结尾补一个白锚点以覆盖尾部。这组白/黑重测既喂给 `DriftCompensator` 拟合漂移曲线，也喂给校验器"重复白"闸门。
4. 最后 `Reindex` 让 `Index` 忠实反映最终测量顺序（锚点插入前不编号）。

> 未验证：`PatchCategory` 的完整枚举在 `MeasurementResult.cs` 中（含 `Grayscale`、`Primary`、`Secondary`、`Saturated`、`MemoryColor`、`SkinTone`、`DriftCheck`、`General`）。本章按使用点记录到 `DriftCheck`/`Grayscale`/`Primary`/`Saturated`/`MemoryColor`/`SkinTone`/`General` 七项，未逐行核对枚举声明是否还有第八项。

#### 9.2.3 自适应放置：AdaptivePatchPlanner

`Adaptive` 预设把"测多少、测哪里"从静态表变成闭环决策。它不是"随便撒点"，而是**用模型自身的不确定度选下一个测点**，并把整件事做成可解释的两项打分：

- **残差项（residual term）**：候选点所在流形上的残差，用反距离加权（IDW，软化距离 `IdwDistanceEpsilon = 0.02`）插值，并按各自的精度目标归一化，使 tone 与 color 两类误差共享同一尺度（1.0 = 恰好在目标上）。
- **空隙项（gap term）**：候选点到同流形上最近已测点的距离 × `GapTermWeight = 0.5`。这一项保证早期轮次先补真正的覆盖空洞，而不是只追当前最坏残差。

`SignalManifold` 把"距离"限定在有意义的维度上：`Gray`（R=G=B，距离为 |Δlevel|）、`RedRamp`/`GreenRamp`/`BlueRamp`（单通道，距离为 |Δchannel|）、`Cube`（信号 RGB 欧氏距离）。**跨流形的距离是 `+∞`**——灰轴的残差对红通道斜坡没有任何信息量。选点后还要做批次内**最小间隔抑制**：按分数降序访问，若候选与已入选者在同流形上近于 `MinPickSeparation`（1D 为 `4/255`，Cube 为 `0.05`）就跳过。注释诚实地说这只是"给间距设下限"，不是真正的 farthest-point / OFPS 最大化最小距离优化器——对这个阶段够用，且不引入随机性（同输入必同输出）。

全部可调面就是一组命名常量：`DefaultBatchSize = 12`、`DefaultPatchBudget = 120`、`ToneTargetDeltaLstar = 1.5`、`ColorTargetDeltaE = 0.8`、`PlateauMinImprovementFraction = 0.10`、`PlateauPatienceRounds = 2`、`RobustResidualPercentile = 0.90`、`DuplicateExclusionDistance = 0.5/255`、`UnmeasuredManifoldGap = 1.0`。

编排侧（`RunAdaptiveRoundsAsync`）每轮：用已有测量拟合模型并算残差（`Lut3DGenerator.ComputeModelResiduals`，`lutSize: 17`）→ `AdaptivePatchPlanner.EvaluateStopping` 决定停不停 → `PlanNextBatch` 出一批 → `PatchSetGenerator.BuildAdaptiveRoundSequence` 把这批补丁与继续原有节奏的漂移锚点合并 → 测量 → 记录 `AdaptiveRoundsCompleted`。三个工程决定值得记住：

- **停止判据用稳健分位数而非最坏点**：`MaxNormalizedResidual` 用于展示，真正决策的是 90 分位（`RobustNormalizedResidual`）。否则"解决当前最差点、立刻暴露次差点"会被误读为收敛；再叠加 `PlateauPatienceRounds = 2` 的耐心，单轮噪声不足以终止一条仍在改善的运行。
- **进度条不倒退**：追加轮次会改变 `TotalPatches`，进度百分比与 ETA 会往回跳。`_adaptiveProgressTotal = 已排队补丁 + 剩余普通补丁预算` 冻结一个投影上界作为分母。
- **未达标是"降级"而非"成功"**：预算耗尽、平台期、候选耗尽而精度目标未到，都会置 `AdaptiveAccuracyTargetsMet = false`，并在 `CalibrationResult` 上给出 `AdaptiveDegraded` + 原因文本。profile 仍然可用，但不允许被当成无保留的成功上报。

残差本身分两类（`ResidualKind`）：`Tone` 用**留一法**（逐点拿出、用其余点重拟合、预测被拿出点）的 **|ΔL\*|**——因为普通拟合残差在被拟合点上恒接近 0，只有 LOO 才是诚实的预测误差，而 |ΔL\*| 按可见度给暗部加权且近黑不发散；`Color` 则是重投影到当前前向模型后，在**匹配亮度下只比色度**的 ΔE2000，避免与 tone 项重复计算，也避开 CIELAB 近黑 a\*/b\* 放大。低于 `ColorResidualMinNormLuminance = 0.05`（约 5% 白，L\*≈27）的补丁不产生 color 残差——这是个被明确记录的盲区：该区间色度计的色度噪声会凭空造出比真实偏色更大的假 ΔE。

#### 9.2.4 每补丁的时序：自适应 settle 与多读中位数

单个补丁的测量不是"切换→等待→读一次"。`MeasurePatchAsync` 的流程是：**自适应 settle → 首次读 → 判断是否需要多读 → 噪声门 → 高 APL 稳定化 → 喂噪声模型 → 逐分量取中位数**。

**（a）自适应 settle（m2，`ComputeSettleDelayMs`）。** 固定 settle 对大亮度跳变欠等待（OLED/VA 像素下降沿、背光调制）、对小跳变过等待。这里按"与上一个补丁之间的信号亮度差"缩放：

```
key   = 0.2126·R + 0.7152·G + 0.0722·B        （非线缆补丁）
key   = clamp(Nits / 1000, 0, 1)               （线缆补丁，梯子顶点约 1000 nits）
delta = |key - prev|                            （首个补丁前 prev 假定 1.0 = 亮屏）
settle = SettleBaseMs + delta · SettleScaleFullSwingMs
若 prev - key > 0.5（大幅下降）: settle = max(settle, LargeFallSettleFloorMs)
settle = min(max(settle, _settleTimeMs), SettleMaxMs)
若预测落在本轮已证明"噪声大"的亮度十倍程: settle = min(settle·2, SettleMaxMs·2)
```

默认 `SettleBaseMs = 200`、`SettleScaleFullSwingMs = 600`、`LargeFallSettleFloorMs = 500`、`SettleMaxMs = 1200`（1200 ms 与 PQ 验证扫描的首补丁 settle 一致，是全仓库用过的最长 settle）。"大幅下降加地板"的理由写在注释里：OLED/VA 面板向暗部的衰减远慢于向亮部上升，测太早会把残余辉光读进暗补丁。

**（b）多读中位数（M8，`NeedsMultiRead` / `MedianMeasurement`）。** 近黑补丁（信号 ≤ `LowSignalThreshold = 0.10`）受探头噪声支配；白与 100% 原色锚定白点、峰值亮度和色域矩阵，一个离群值会污染整个 profile——这两类取 `MultiReadCount = 3` 次读数的**逐分量中位数**（中位数完全拒绝单次毛刺，均值不会）。中间调保持单读以节省时间预算；HDR 线缆补丁也保持单读（PQ 扫描有自己的单调性校验和紧张的时间预算）。噪声门：三次读数的 Y 极差超过 `max(均值·0.05, 0.02 cd/m²)` 时再补一次读（上限 `MaxReadsPerPatch = 4`），让单个离群点无法"扳倒"中位数。时间戳取**中间那次读数**，使漂移拟合看到读数的时序中心。

**（c）高 APL 稳定化（`SettleHighAplAsync` / `IsDrifting`）。** 这是全套逻辑里最"硬件现场"的一段。OLED 的自动亮度限制器（ABL）在长时间暗画面后放开余量，于是接下来第一个全屏白读到的值远高于面板稳态，并在数秒内衰减。注释记下了真实硬件证据：MAG 271QP X28 上，蓝斜坡后的白锚点在 1.2 s 内读到 265.7 → 263.4 → 260.9 cd/m²，而同一轮其他三个白都在 242–243；这个衰减脉冲的中位数不是测量值而是瞬态采样，直接触发校验器 8% 重复白闸门、整轮失败。解法不是拉长固定 settle（瞬态比任何合理上限都长，且其他每个补丁都要陪跑），而是：**读、检测单调趋势、继续读到相邻读数一致为止**。

`IsDrifting` 的三个条件缺一不可，每条都为了排除一种误判：

1. 极差超过均值的 `SettledRelativeSpread = 0.003`（0.3%）——否则稳定读数里的普通噪声会被当成瞬态。注释给出硬件标定：稳定白脉冲跨度约 0.14%，限幅器瞬态约 1.8%。
2. 每一步同号（`Math.Sign(step)` 一致）——探头噪声不会持续单向。
3. 步长量级可比：`smallestStep >= largestStep · MinDriftStepRatio`（0.25）。失败案例里 -2.35、-2.42 的比值是 0.97；单次毛刺后接着正常读数是 -49、-1，比值 0.02，会被这一条挡住，从而保住多读中位数的毛刺抑制能力。

满足条件就每 `SettleReadDelayMs = 400` ms 再读一次，最多 `MaxSettleReads = 24` 次（约 25 秒/补丁的硬上限）。只看最后 `MultiReadCount` 次读数的滑窗（`TailWindow`），记录该窗口的中位数；始终不收敛的补丁写入 `UnsettledPatches` 并记日志——报告可以据此说"这块面板没能维持稳定输出"，而不是悄悄把瞬态当结果。

**（d）逐亮度十倍程噪声模型（1.4，`LuminanceNoiseModel`）。** 上面 (a)(b)(c) 的阈值都是先验的；这一层把它变成**运行中学习**的：每次多读脉冲把观测到的相对极差（极差/均值）记入其绝对亮度所在十倍程（`<0.1`、`0.1–1`、`1–10`、`>10 cd/m²`，`BinCount = 4`）的 EWMA（`EwmaWeight = 0.3`）。某十倍程的相对极差超过 `NoisyRelativeSpread = 0.02` 判为噪声大，低于 `QuietRelativeSpread = 0.005` 才回落（滞回，避免单阈值抖动），于是：后续**单读**补丁若首次读数落在噪声十倍程，自动升级为三次中位数，且 settle 加倍（有 `NoisySettleMultiplier = 2` 上限）。暗场 VA/OLED 运行只会在真正嘈杂的亮度段变慢。

**（e）重试与暂停。** `MeasurePatchWithRetryAsync` 只在**读失败**时重试（`maxRetries` 构造参数，默认 3）：无效读数等一个 `_settleTimeMs` 后重试，抛异常则等 `_settleTimeMs·2`，`UsbDriverException` 永不重试（没有用户操作不可能恢复）。另一个容易漏掉的细节：若用户在某个补丁读数**在途**时按下暂停，这次读数被视为可疑（探头或面板可能被碰过，且暂停/恢复倒计时期间已经播过采集音），代码把 `_currentPatchIndex--` 并 `continue`——丢弃并重测同一个补丁。

#### 9.2.5 测量协议：ColorimeterService 与 SpotreadSession

**`ColorimeterService`** 是对 ArgyllCMS `spotread.exe` 的托管封装，也是 UI 侧唯一认识的测量入口。它负责：定位 `spotread`（经 `ArgyllPathFinder`）、初始化时先跑 `OemCorrectionImporter.EnsureImportedAsync`（把机器上已装的 X-Rite/Calibrite 软件里的 i1Display EDR 校正转成 Argyll 能用的形式）、用 `spotread -?` 输出探测设备、维护持久会话、把型号/序列号/`-y` 显示类型表暴露为 `ColorimeterInfo`。

关键设计点：

- **一次运行一个 spotread 进程**。`BeginMeasurementSessionAsync` 打开会话，`EndMeasurementSessionAsync` 在 `finally` 里关闭（无论循环如何结束）。注释说明了为什么不再用"每补丁一次 `spotread -O`"：那种模式对 USB 重枚举、连接失败的 exit-code-0 静默、以及"连上了但读不到"都无法区分，表现为"走到补丁阶段就不读数"。
- **模式切换会重开会话**：`_sessionHdrMode != hdrMode` 时先关再开。注意 `-H` 是光谱仪的高分辨率光谱模式，**不是** HDR 开关；这里重开的理由是 SDR/HDR 阶段之间 OS 显示模式翻转，跨这次切换重新初始化仪器是刻意的。
- **会话被超时"毒化"后必须重启**：若某次测量超时，超时的那次触发仍排在 spotread 的输入队列里；复用进程会让那个迟到的读数配给**下一个**补丁——整轮之后每个读数都错位一格而完全无声。`SpotreadSession` 因此把 `IsPoisoned` 置真并杀掉进程，`TakeMeasurementAsync` 在下一次测量前强制重启会话。一次超时的代价是一次会话重启，不是整轮报废。
- **无会话时的瞬时路径**：广告/工具类的一次性测量调用打开临时会话（`await using`），行为正确但每次付出进程启动代价。
- **显示类型 `-y` 选择器不是固定字母**：Argyll 按仪器 + 已装校正在校准时构建该表，只有装了匹配校正才存在技术字母。`SpotreadDisplayTypeTable.Resolve` 使用从 `spotread -?` 解析出的真实表；仪器未枚举时用基础选择器，并在 spotread 打印 `Failed to locate display type matching '...'` 时捕获 `DisplayTypeRejectedException`，用它输出里的用法表重试一次，并把该表回灌给 `ColorimeterInfo`（`AdoptDisplayTypeTable`）。
- **`MeasureAsync` 的契约**：普通失败返回 `IsValid = false` + `ErrorMessage`（不抛），`UsbDriverException` 抛出。这让编排层能对"读数无效"重试、对"设备不可用"升级给用户。

**`SpotreadSession`**（1325 行，`internal`）是与进程对话的状态机，实现的是 DisplayCAL 同款模式：交互式启动 + `ARGYLL_NOT_INTERACTIVE=1` 环境变量让它按行读取输入，等就绪提示，然后每次测量写入 `" \r\n"`（空格 = "take reading"；必须是 CRLF，这是 spotread 脚本模式在 Windows 上的怪癖），从 stdout 里正则抓 `XYZ:` 行。

它的复杂度几乎全部来自"如何判断 spotread 在说什么"，且每条判据都附有版本/现场证据：

| 关注点 | 实现 |
| --- | --- |
| 启动就绪 | 片段匹配而非整行匹配（`"any other key to take"`、`"to take a reading"`、`"to commence"`、`"hit esc or q"`、`"place instrument"`、`"instrument is ready"`），因为 Argyll 版本间措辞不同 |
| 校准提示 vs 就绪 | 光谱仪读发光目标前要先跑白/暗参考校准，其提示（"…calibration"、"reference tile"、"white reference"）也含 "place instrument"，在光谱模式下必须**不**当成就绪，否则会在校准中途触发读数 |
| 硬错误 | 特定短语（`"failed to open"`、`"not connected"`、`"no instrument found"`…）；通用 `"error: "` / `"error - "` 只在**行首**才算致命——旧实现全文匹配，结果被仪器信息/校正描述里出现 "error" 字样的正常输出打断 |
| 探头位置错误 | i1 DisplayPro 环境光挡板未转开时 spotread 立刻打印 `"sensor being in the wrong position"` 一类文本；不识别它会白等三次 30 s 超时，把"立刻可修"变成"神秘失败"（2026-07-09 现场案例） |
| 超时 | 单次测量 `MeasureTimeout = 30 s`（internal settable 供测试），连接阶段 15 s；超时即毒化会话 + 杀进程 |
| 光谱模式 | `-s -H`；解析 `Spectrum from A to B nm in N steps`（结尾名词 steps/bands/samples 可选，因为各版本不同）；波段数 3..10000、`end > start` 做合理性拦截 |
| 残留进程 | 启动前 `KillStraySpotread`：上次运行崩溃后被留下的 spotread 会继续持有探头的 USB 句柄，导致下一次连不上；同一时刻只允许一个 spotread 与探头通信 |

进程参数由 `BuildSpotreadArguments` 集中构造：`-v`（verbose）、`-N`（仅色度计，跳过上电暗校准）、`-c <index>`、`-e`；非光谱模式追加 `-y <selector>`；光谱模式追加 `-s -H`；若有 `.ccss`/`.ccmx` 校正文件则 `-X <path>`——传入前必须先过 `CgatsValidator.IsValidFile`（防御纵深：路径可能被手工编辑或来自陈旧数据库）。

#### 9.2.6 物理门槛：CalibrationMeasurementValidator

这是"坏数据不得变成已安装 profile"的第一道（也是最后一道）硬闸门。`ValidateForProfile(measurements, target, hdrMode)` 返回 `Result(IsValid, Error)`，被 `Lut3DGenerator.BuildCharacterizationOnly` 与 `CalibrationProfileInstaller.Install` 两处**独立**调用——建模前拒绝一次，安装前再拒绝一次（后者可能面对的是从磁盘/CLI 重放的测量集）。按执行顺序，闸门有：

| 闸门 | 判据 | 拦住的真实故障 |
| --- | --- | --- |
| 样本量 | 有效"精度测量"（`IsValid` 且 `Patch.Nits is null`）< 10 | 提前中止/大量重试失败 |
| 数值合法 | XYZ 非有限（NaN/Inf）、XYZ 为负 | 解析失败、探头返回垃圾 |
| 全黑 | 峰值 `Y < 1.0 cd/m²` | 探头被遮挡 / 环境光挡板未转开 |
| 无动态范围 | `peak <= floor·1.5` | 读数陈旧或被缓存复用 |
| 锚点 | 缺白或缺黑（`FindPatch` 容差 0.02） | 补丁窗没显示/没被测量 |
| 白比黑亮 | `white.Y <= black.Y + max(1.0, peak·0.05)` | 补丁窗被别的窗口盖住 |
| HDR 线缆梯 | 请求 `Nits` 必须有限非负；有效去重梯级 ≥ 5；最大有效梯级 ≥ 100 nits；若请求过 ≥200 nits，则有效覆盖 ≥ 请求峰值的 80% | FP16 渲染器失败、高亮度段丢失 |
| 线缆梯动态范围 | 梯子峰值 `< 1.0` 或 `peak <= floor + max(1.0, peak·0.05)` | HDR 渲染路径未生效 |
| 线缆梯单调 | 相邻梯级不得反向超过 `max(1.0, peak·0.05)` | 测量噪声造成的微小反转之外的真实异常 |
| 重复白漂移 | 多个白锚点：`(maxY-minY)/minY > 0.08` 失败；chromaticity 距离 `> 0.01` 失败 | 面板未预热、动态亮度 |
| 重复黑漂移 | 多个黑锚点极差 `> max(0.5, peak·0.01)` | 环境光漏入、局部调光状态跳变 |
| 灰阶数量 | `PatchCategory.Grayscale` 有效点 < 5 | 斜坡中断 |
| 灰阶单调 | 后点 `Y + max(0.25, peak·0.02)` 不得低于前点 | 动态对比度/自适应亮度/VRR 黑帧插入 |
| 原色可信 | 原色 `Y <= black.Y + 0.5` 失败；`xy` 必须满足 `x>0, y>0, x≤0.8, y≤0.9, x+y≤1.1` | 贴错补丁、探头指向错误 |

错误信息不是终点：`BuildRecoveryText` 按错误文本分类给出**可执行**的修复建议（重跑、检查贴面、关掉动态亮度、换校正文件、确认 HDR 全程开启……）。这是"失败可解释"原则的落地——同一段文本同时进日志和 UI。

#### 9.2.7 漂移归一化与闭环细化

**`DriftCompensator`（M7）** 把"慢速乘性亮度漂移"从数据里除掉。模型：`输出(t) = 稳定显示 × f(t)`，`f` 只作用于亮度；每个漂移白锚点给出一个样本 `f(t_i) = Y_i / Y_0`（对**第一个**白锚点取比值，它是在预热/ABL 发展之前测的 t0 基准），`f` 在样本间分段线性插值，然后每个测量的 XYZ **整体除以** `f(timestamp)`，把整轮归一化回 t0 状态。除以同一因子保持色度完全不变——这是刻意的：色度漂移不被掩盖，仍由校验器看见并裁决。

安全阀：漂移超过 `MaxCorrectableDriftFraction = 0.08` 时**不补偿**。注释的理由很硬：漂移这么大的运行本身就是坏掉的测量设置（没预热、动态亮度在动），抹平它等于用坏数据静默地装一个 profile；保持原始数据让校验器的重复白闸门（同样是 8%）带着可执行信息把这一轮否掉。黑锚点是加性域且噪声支配，无法做乘性拟合，这里只做分析（报告最大偏差），由校验器的重复黑闸门负责。

编排层的收尾细节：`DriftCompensator` 重建 `MeasurementResult` 实例并**不携带**多读元数据，因此 `StartCalibrationAsync` 在替换测量列表时逐项把 `ReadingCount`/`ReadingSpreadY` 搬回去——否则不确定度预算（9.5）会失去逐点重复性项。

**闭环（`ClosedLoopCorrector` + `RunClosedLoopAsync`）** 是经典硬件校准流程：测原生灰阶 → 建修正 → 加载 → 复测 → 收敛或轮次耗尽。产物是三条 1024 点 **signal→signal** LUT，可直接作为 GPU gamma ramp 加载。

- `BuildInitialCorrection(nativeMeasurements)` / `RefineCorrection(afterMeasurements, correction)`（带 `damping = 0.5` 默认阻尼）、`GrayscaleResidualDeltaE(measurements)` 打分。
- `ClosedLoopConfig`：`Corrector`、`Apply`（把候选 LUT 加载到被测显示器的委托）、`MaxRefinementRounds`（默认 1 = 只做一次 apply+verify）、`TargetDeltaE`（默认 1.0）、可选 `VerificationPatches`（默认取补丁序列里的灰阶子集）。
- **保留最优**：`RunClosedLoopAsync` 记录 `bestCorrection/bestResidual/bestAfter`，循环结束时重新加载**最优**那轮而不是最后尝试的那轮。注释原话是"refinement 绝不可能产出比初始更差的结果"。
- **域契约警告**（写在类注释里，是这套代码里最容易踩的坑）：VCGT 数组是 signal→signal，其**输出**是被送进原生面板的（gamma 编码）信号，而面板的光响应是测量得到的 tone curve；任何"多给 x% 光"的意图都必须穿过那条曲线。直接把 VCGT 值相乘会引入约 `gain^(1/γ)⁻¹` 量级的误差——在典型面板上相当于 ~2.2 倍过校正。配套的 `DecomposeCorrection` / `RecomposeChannel` / `MakeEffectiveTarget` 就是为"分解成共享中性 tone + 每通道增益，再合成回去"服务的。
- 闭环与安装的交接：`CalibrationResult.FinalCorrection` + `NativeResidualDeltaE`/`CorrectedResidualDeltaE`/`RefinementRounds`。若安装路径拿到的是"中性 LUT"（三通道相同），`CalibrationProfileInstaller` 会用特征化的逐通道曲线补上灰阶跟踪差量 `f_c⁻¹ ∘ f_neutral`（见 9.2.9）。

#### 9.2.8 建模与 LUT：从测量到“应写入什么”

**SDR 路径：`Lut3DGenerator`。** 构造即校验（少于 10 个测量点直接拒绝），`Generate()` 的第一步是 `BuildCharacterizationOnly()`——先用校验器把坏测量集挡住，因为"探头连上了但从未真正读屏"会产出一个垃圾 LUT，而应用会把它当作成功校准上报。

特征化（`DisplayCharacterization`）从测量里抽出：黑/白点（`BlackLevel`、`PeakLuminance`）、三个原色色度、白点色度、中性 tone curve（灰阶测量，输出为中性补丁的总亮度 Y，归一化到 `(Y - Y_black)/(Y_white - Y_black)`）、**逐通道 tone curve**（E4：单通道 R/G/B 斜坡给出该通道真实 EOTF，归一化到它自己的满驱动发射，因此满驱动处必然收敛到 1.0、不会重新平衡白点——那是矩阵的职责）、测得原色构造的 RGB→XYZ 矩阵、平均 gamma。

生成 LUT 时对每个网格点（`Lut3DGenerator` 构造参数默认 `lutSize = 17` 并被夹到 9–65；`Lut3D` 本身接受 2–256，注释里点名常用 17³/33³/65³）：

```
targetXyz = target.LinearRgbToXyz(target.ApplyEotf(inputRgb))        ← 目标空间"应该"产生什么
adaptedXyz = 若目标白点 ≠ 显示白点: ColorMath.ChromaticAdaptation(targetXyz, 目标白, 显示白)
linearRgb  = Inverse(characterization.RgbToXyzMatrix) · adaptedXyz
signalRgb  = (RedToneCurve⁻¹, GreenToneCurve⁻¹, BlueToneCurve⁻¹)(linearRgb)
output     = CompressToGamut(signalRgb)
```

色域映射不是逐通道裁剪，而是 **Oklab 中保持色相的两步压缩**：先把 Oklab 明度 L clamp 进 `[0, L(white)]`（明度不可表示时，任何降色度的操作都救不回来，只能 clamp），再固定 L 与色相 `h = atan2(b, a)`，对色度 C 做 40 次二分，取"线性 sRGB 像仍落在 [0,1]³"的最大 C。注释给出了理由：逐通道裁剪会带来色相偏移，信号域（Rec.709 亮度锚定）压缩的色相稳定性只是近似。在色域内的输入**原样返回**（bit-exact），所以灰阶与合法颜色零改动；NaN→0、±Inf→±65504 先做净化，保证输出永远有限且在色域内。

**HDR 路径：并非另一个 3D LUT。** Windows 的 MHC2 只能表达"一个 3×3 矩阵 + 三条一维 LUT"，所以 HDR 校准走 `Lut3DGenerator.BuildCharacterizationOnly(hdrMode: true)` 只做特征化，真正的修正是 `HdrMhc2LutBuilder.Build` 合成的三条 **PQ 域** 一维 LUT。其核心事实：HDR 下线缆格式是 BT.2100 PQ，MHC2 LUT 作用在 **PQ 信号**（0..1 ↔ ST.2084 的 0..10000 nits）上，而不是 SDR 的 gamma 编码信号；目标是让内容信号 `v` 产生 `ST2084(v)` nits 的实测亮度。

- 首选数据源：HDR 线缆梯（`ColorPatch.Nits` + FP16 scRGB 渲染）。其线缆位置是精确的 `PQ⁻¹(nits)`，不依赖 OS 的 SDR 白点映射（参考系统上该映射实测偏 ~7%），且能覆盖到远高于 SDR 白点的区间；因此可以把实测响应的逆写进整段可达范围，只在面板可达峰值**以上**回归恒等（面板做不到的光，LUT 不能凭空造）。
- 回退数据源：SDR 窗口灰阶。它假定 Windows 以 `PQ(sdrWhiteNits · srgbEotf(v))` 把 SDR 内容映上线缆——这是**假设**，且覆盖止于 SDR 白点，所以修正被限在实测范围下半段、在 50%–80% 之间渐回恒等（knee-safe）。
- `Result` 同时带回 `MeasuredBlackNits`/`MeasuredPeakNits`（供 MHC2 头与 `lumi` 标签用实测值，而不是 DXGI 广告值）、`WireExact`（数据源是否线缆精确）、`MatrixNeutralScale`（见下）。
- **矩阵尺度是 LUT 输入域的一部分（M5）**：DWM 在应用这三条 LUT **之前**先应用（被统一缩放过的）色域矩阵，所以 LUT 实际看到的线缆位置是"矩阵缩放后的编码"，而不是实测时的原始内容位置。`Build` 因此把 `matrixNeutralScale` 合成进反演——否则矩阵+LUT 合成链的 PQ 跟踪会整体偏。`Refine`/`RebaseNeutralScale` 会在矩阵变化后拒绝复用旧 LUT（尺度不匹配时必须从测量重建）。
- LUT 本身是**中性**的（三通道相同）：白点修正完全交给（统一缩放、绝对）色域矩阵；在这里放逐通道增益会把矩阵已经摆好的饱和色重新染色。

HDR 之上的三段闭环细化各自职责分明：`HdrColorMatrixRefiner`（拟合 3×3 XYZ 残差 F，把"透过已安装矩阵+LUT 测到"的色误差折回 MHC2 矩阵）、`HdrMhc2LutBuilder.Refine`（在 PQ 域逐点细化 LUT，`RefineClampMin = 0.7`/`RefineClampMax = 1.4` 限幅，`RefineConvergedError = 0.01` 时拒绝再折腾）、`HdrJointRefinement`（矩阵与 tone LUT 作为**一个原子候选**联合求解，因为 MHC2 表达不了任意亮度相关的色域旋转——这是受限分块求解，不是无约束 3D 拟合），`HdrRefinementLoop` 用注入的"测量/安装"委托迭代，保留平均绝对亮度误差最低那一轮（首轮阻尼 1.0、后续 0.5，最多 3 轮）。

#### 9.2.9 合成与安装：Mhc2ProfileBuilder + CalibrationProfileInstaller

`CalibrationProfileInstaller.Install` 是"测量结果变成 Windows 生效修正"的唯一入口，签名收拢了全部参数：`monitor`、`characterization`、`target`、三条 tone LUT、`whiteLevel`、`hdrMode`、`measurements`、`profileNameOverride`、`hdrLutsOverride`、`xyzCorrectionOverride`。

**安装前的四道闸门**（顺序即代码顺序）：

1. 显示器必须有 `MonitorDevicePath`，否则无法做关联。
2. **模式必须与测量时一致**：`monitor.IsHdrActive != hdrMode` 直接拒绝。注释点明这是硬安全闸——LUT 的域完全不同（SDR 是 gamma 编码信号，HDR 是 PQ 线缆信号），跨模式安装会毁掉画面（历史上"硬编码 SDR"的 bug 就是这样把 HDR 桌面洗白的）。
3. HDR 安装必须带 `measurements`（要现建 PQ 域 LUT）。
4. 若给了 `measurements`，再跑一次 `CalibrationMeasurementValidator.ValidateForProfile`。

**色域矩阵计划（`BuildGamutMatrixPlan`）**：`M = D⁻¹ · T`，其中 `D` 是特征化测得的 RGB→XYZ、`T` 是目标 RGB→XYZ；`WhitePointOnly` 时把 `T` 的基色替换成面板实测基色（只修白点与 tone，不做色域重映射——对基色已达标或处理过于非线性的面板是正确选择，注释举了 QD-OLED 在 HDR 下全色域校正会过拟合：基色 1.49 → 3.34 ΔE）。带 `xyzCorrectionOverride` 时 `M' = D⁻¹ · F⁻¹ · T`（预补偿已安装链路上测到的残差）。然后过 `GamutReachability` 闸门：`PrimaryDriveExtent` 求每个基色所需驱动范围，必须落在 `[MinReachablePrimaryDrive = -0.05, MaxReachablePrimaryDrive = 1.3]` 内，否则**抛异常**并给出可执行建议（"换一个面板能到的目标，SDR 通常是 sRGB Gamma 2.2 或 Rec.709"）。负驱动同理：目标基色落在显示色域之外会要求"减去光"，矩阵只能裁到黑并让该色相掉色/偏色。

不可达时的解决方式是**统一缩放**而不是逐通道增益：`UniformScale(maxTargetDrive) = maxTargetDrive > 1 ? 1/maxTargetDrive : 1`，`ScaleMatrix` 等比缩放整个矩阵。注释记录了反例：逐通道对角增益会把矩阵刚摆好的每个非中性色重新染色，第一次 HDR 扫描把已验基色 ΔE 从 1.39 推到 2.46；统一缩放精确保持所有色度、只付出峰值亮度的代价。

**写 profile（`Mhc2ProfileBuilder.Build`）** 是字节级操作：以 MHC2 模板 `.icm` 为基础——`FindTemplate(whiteLevel)` 在 `{100, 200, 300, 400}`（`TemplateWhiteLevels`）中取与当前 SDR 白点最近的一档，拼成 `srgb_to_gamma2p2_{nearest}_mhc2.icm`，再按 `AppContext.BaseDirectory` 向上的 8 级目录 + `AppPaths.DataDir` 依次找（同一份模板既由 `ResourceExtractor` 放到可执行文件旁，也存在于仓库根，便于开发运行）——定位 `MHC2` 标签并原地改写。标签布局（源码注释里给出，是从随附模板逆推并对照 MHC2Gen 产物验证的）：

```
+0   'MHC2'                      类型签名
+4   uint32  reserved (0)
+8   uint32  lutCount (1024)
+12  s15Fixed16 minLuminance
+16  s15Fixed16 maxLuminance
+20  uint32  matrixOffset        （相对标签起点）
+24/28/32  uint32 lut0/1/2Offset （红/绿/蓝）
matrix: 3 行 × 4 列 s15Fixed16，output = M·[r,g,b,1]（无元素头）
每条 LUT: 'sf32'(4) + reserved(4) + lutCount × s15Fixed16（signal→signal，[0,1]）
```

写完后还会：`PatchCharacterizationTags` 重写 `wtpt/chad/rXYZ/gXYZ/bXYZ/rTRC/gTRC/bTRC/chrm`，让 ICC 感知的应用看到真实显示器而不是模板里合成的 sRGB/G2.2（ICC v4 约定：`wtpt` = PCS D50，`chad` = Bradford(实际白 → D50)，`r/g/bXYZ` = 经 `chad` 适配的基色）。注意 HDR 时普通 ICC TRC **刻意保持 sRGB**：在 Advanced Color 下应用与 SDR 内容都面向这个呈现编码，PQ 只属于 MHC2 线缆域载荷——把目标的 PQ TRC 暴露在这里会让 Photoshop 把 sRGB 图先色彩管理成 PQ、再由 DWM 管理一次（两种 HDR 显示模式下都表现为画布洗白）。

Windows 只在"矩阵被夹在固定 sRGB↔XYZ 转换之间"的域里应用 MHC2 矩阵，所以 `ToMhc2MatrixDomain(scaledMatrix)` 先把矩阵包裹进该域。最后**把 profile ID（字节 84–99）清零**：正文改过了，存档的 MD5 已过期，必须让系统重算。

**安装顺序与回滚**：`TryInstallVerified` 先把文件装进系统色彩存储，再 `AdvancedColorProfileAssociation.VerifyInstalledProfile` 回读校验；校验不通过就立刻卸载新装文件（"两个不同 profile 不会被静默激活"）→ SDR 额外走 `AssociateLegacyDefault`（WCS 关联，应用读的是这张旧列表）→ 两种模式都要 `AdvancedColorProfileAssociation.TryActivateInstalled`（Windows 只从 Advanced Color 列表应用 MHC2）。任一步失败即 `UndoFailedInstall`：解除 legacy 关联、从存储删除新装文件，**不留半装状态**。成功后 `ForeignPriorDefault` 记录被顶掉的第三方 Advanced Color 默认 profile，供回滚/提示。SDR 装完若 `DetectSdrAutoColorManagement` 为 false，返回 `Warning`：profile 装上了，但"自动管理应用颜色"关着时 Windows 不会应用它。

`AdvancedColorProfileAssociation` 的文档里有一条必须记住的实测结论：**列表成员关系不是有效判据**（Windows 11 25H2 build 26200.8973）。`ColorProfileGetDisplayList` 对两个作用域都返回 S_OK + 0 条，即使关联已经生效（profile 在注册表 `ICMProfileAC`，`ColorProfileGetDisplayDefault` 读得回来，列表仍为空）。要求"出现在列表里"曾让每次 HDR 安装都验证失败并回滚一个完全正常的关联。现在的判据是"Windows 实际查询的作用域里的**默认关联**"：如果我们的 profile 就是那个默认，它按定义已被关联；列表只用于诊断，永不用来判定失败。`ActivationReceipt` 记录 `Identity / PerUserWasEnabled / PriorSelectedScope / PriorCurrentUserDefault / PriorCurrentUserProfiles / ActivatedProfile / AddedAssociation`，使 `TryRollback` 只撤掉本次新增的关联。

相邻的两个服务分担"事后修复"：`AdvancedColorProfileMigrationService` 在启动时保守修复旧版本创建的 HDR profile（只有出现在该显示器设置里、带 Gloam 显示器名前缀、且 MHC2 载荷有效才允许改写；安全 profile 还会与 Windows 当前默认比对，把卡在失效 per-user 列表里的关联恢复而不必重校），`CalibrationInstallPreflight.BuildMessages` 则负责"测量到安装之间世界变了"的检查：显示器身份变了（ERROR）、HDR 模式变了（ERROR）、SDR 白点变了 `max(10, 5%)`（WARN+重新测量更准）、Windows 默认 profile 被别人换了（WARN）、以及组合 tone 路径提示。

#### 9.2.10 验证：CalibrationVerifier / VerificationPatchSets / TrustCheck

安装完成不等于校准成立。验证的定义是**重新测**：`CalibrationVerifier` 用同一套指标在自己的补丁扫描上打分，而这个扫描是**透过已生效修正**测的（Windows 会把已安装的 MHC2 应用到屏幕上的所有内容，包括补丁窗）。类注释一句话概括了立场："the honest 'after' — re-measured reality, not a model prediction"。

- `LinearizePatchSignal(target, signal)` 是"SDR 内容补丁 → 线性"的**唯一权威规则**：PQ 目标下补丁仍是 SDR 内容（Windows 用 sRGB 曲线按 SDR 白点缩放渲染），所以按 `ColorMath.SrgbEotf` 评分；对补丁信号做 PQ 解码等于拿一条管线里没人应用的曲线去比。
- ΔE 度量：ΔE2000（`CieLab.DeltaE2000`）用于 SDR；ΔE ITP 用于 HDR 彩色刺激，按 ITU-R BT.2124：`XYZ(绝对 nits) → LMS（BT.2124 串扰矩阵）→ PQ 编码 → ICtCp`，然后 `ΔE_ITP = 720·√(ΔI² + ΔT² + ΔP²)`，其中 `T = 0.5·Ct`。非物理输入返回 **NaN 而不是 0**——0 会被读成"完美匹配"并通过 `IsFinite` 过滤，悄悄把平均值拖向 0。
- `VerificationPatchSets.Detailed` 是可选的高密度验证集：细灰阶、逐原色饱和度斜坡、ColorChecker classic 记忆色，用来给出 ΔE 分布、逐补丁误差与**分类别**汇总（`VerificationAnalysis` + `CategoryBreakdown`）；`ColoredHdrVerificationSet` 提供 HDR 容器域彩色刺激，其中 `BuildForMatrixRefinement` 带中性锚点供矩阵细化使用。另含 `ProfileActivationCheck`/`ProfileActivationStatus`，把"profile 到底有没有生效"也做成可断言的结果。
- `TrustCheck` 是 6 补丁 ~20 秒的抽查（白/40% 灰/黑 + R/G/B）：黑补丁**只按亮度**评级（黑的 ΔE 在计量上没有意义：探头噪声底支配、Lab 在零附近发散），其余走与完整验证相同的 `ComputeMetrics` 路径，因此数字可直接与报告对比。结果追加进 `TrustCheckHistory`（每显示器一份、带 schema 版本、未知字段容忍），`DriftPredictor` 再在其上做趋势与阈值穿越预测，但设了诚实门：少于 3 点或跨度不足 7 天不预测；斜率统计上不显著时只报"稳定"，绝不编一个穿越日期。
- `CalibrationReport` 是给 UI/导出的聚合体：识别信息（`Id`、时间戳、`MonitorDevicePath`、`Target`、探头型号、软件版本）、原始测量、`DeltaEStatistics`、`GrayscaleTrackingPoint`、`GammaResponsePoint`、`RgbBalancePoint`、`ChromaticityPoint`、`ColorTemperaturePoint`、`QualityAssessment`/`CalibrationGrade`，以及结构化的 `CalibrationIssue`（`IssueSeverity` × `IssueCategory`）。

### 9.3 色度学核心

这一层没有 I/O、没有时钟、没有 Windows API：全部是可单测的静态数学。公式一律照抄源码（含源码注释里的出处），不引入外部定义。

**（1）值类型（`ColorTypes.cs`）。** `readonly struct Chromaticity(x, y)`（含 `D65 = (0.3127, 0.3290)`、`D50 = (0.34567, 0.35850)`、`IlluminantA = (0.44758, 0.40745)`、`Rec709Red/Green/Blue = (0.64,0.33)/(0.30,0.60)/(0.15,0.06)`、`Rec2020* = (0.708,0.292)/(0.170,0.797)/(0.131,0.046)`、`P3* = (0.680,0.320)/(0.265,0.690)/(0.150,0.060)`）、`CieXyz`（Y 单位 cd/m²）、`CieLab`（含 `DeltaE2000`、`DeltaE76`）、`LinearRgb`（含 `IsInGamut` 判 [0,1]³）。`Chromaticity.ToXyz(Y)` 按 `X = x/y·Y`、`Z = (1−x−y)/y·Y` 展开——这也是所有参考白（`ColorMath.D65White` 等）**在静态构造期推导**而非写成四舍五入字面量的原因：推导值与 RGB↔XYZ 矩阵、与 `StandardTargets` 的每个白点共用同一个源，`D65White` 因此是 (0.950455…, 1.0, 1.089058…)。

**（2）XYZ ↔ Lab（`ColorMath.XyzToLab` / `LabToXyz`）。**

```
f(t) = t^(1/3)            t > ε
     = (κ·t + 16)/116     否则
ε = (6/29)³ = 216/24389 ≈ 0.008856451679
κ = (29/6)³ = 24389/27  ≈ 903.2962962
L* = 116·f(Y/Yn) − 16
a* = 500·(f(X/Xn) − f(Y/Yn))
b* = 200·(f(Y/Yn) − f(Z/Zn))
```

逆函数 `f⁻¹(t) = t³ (t³ > ε)，否则 (116t − 16)/κ`。所有入口都做 `SafeFinite`/`SafeReferenceWhite` 净化：NaN/Inf 或零参考白不得传播成"看起来合理"的颜色差。

**（3）色适应。** 两种变换并存，都作用在锥响应域：

- **Bradford**（`ChromaticAdaptation`）：`M_B = [[0.8951, 0.2664, −0.1614], [−0.7502, 1.7135, 0.0367], [0.0389, −0.0685, 1.0296]]`，源/目标白各映到锥响应，取每通道比例 `scale_i = dst_i/src_i`，输入 XYZ 同样映到锥响应、乘比例、再用 `Invert3x3(M_B)` 转回。`AdaptD50ToD65`/`AdaptD65ToD50` 是它的两个便利封装。
- **CAT16**（`Cat16Adapt`，Li et al. 2017 / CIE 248:2022）：`M16 = [[0.401288, 0.650173, −0.051461], [−0.250268, 1.204414, 0.045854], [−0.002079, 0.048952, 0.953127]]`；逆矩阵**由 `Invert3x3` 现算**而不是硬编码发布的四舍五入值，保证正逆对在 double 精度下往返。支持不完备适应度 `D ∈ [0,1]`，实现方式是"照明体混合"：`adoptedWhite_c = D·destWhite_c + (1−D)·sourceWhite_c`，于是每通道增益为 `D·(destWhite_c/sourceWhite_c) + (1−D)`；`D = 1` 复现完整 CAT16（源白精确映到目标白），`D = 0` 是恒等。注释说明了选这个约定的理由：它与 CAM16 的适应度因子 `D_c = D·Yw/RGB_wc + 1 − D` 同构，而夜间模式正是"两个白同亮度、只有色度移动"。

**（4）RGB ↔ XYZ 矩阵。** `SrgbToXyzMatrix`、`Rec2020ToXyzMatrix`、`P3D65ToXyzMatrix` 及其逆全部在 `static ColorMath()` 里由 `CalculateRgbToXyzMatrix(primaries, D65)` **推导**（`S = [Xr Xg Xb; Yr Yg Yb; Zr Zg Zb]`、按白点求通道标量），而不是保存 7 位小数字面量——这样正逆矩阵互为精确逆、且与 `D65White` 共享同一白点。与旧字面量的差 < 1e-4。

**（5）传递函数。** sRGB 与 Rec.2020 在 `ColorMath`，PQ/BT.1886 在 `src/Gloam.Core/TransferFunctions.cs`，HLG/BT.1886 的目标适配在 `CalibrationTarget`。

```
sRGB OETF: S = 12.92·L                      L ≤ 0.0031308
             1.055·L^(1/2.4) − 0.055       否则
sRGB EOTF: L = S/12.92                      S ≤ 0.04045
             ((S + 0.055)/1.055)^2.4        否则
Rec.2020 OETF: α = 1.09929682680944, β = 0.018053968510807
             S = 4.5·L                      L < β
                 α·L^0.45 − (α−1)           否则（EOTF 为其逆，阈值 4.5β）
纯 gamma:    Encode = L^(1/γ)，Decode = S^γ；γ 越界（非 [1,4]）时回落 2.2
```

ST.2084 PQ（`TransferFunctions.PqEotf` / `PqInverseEotf`），常量按源码写出：

```
m1 = 2610/4096/4 = 0.1593017578125      m2 = 2523/4096·128 = 78.84375
c1 = 3424/4096 = 0.8359375              c2 = 2413/4096·32 = 18.8515625
c3 = 2392/4096·32 = 18.6875

EOTF  ：L = ( max(N^(1/m2) − c1, 0) / (c2 − c3·N^(1/m2)) )^(1/m1) · 10000   [nits]
逆EOTF：N = ( (c1 + c2·L^m1) / (1 + c3·L^m1) )^m2                             L = nits/10000
```

HLG 在 `CalibrationTarget` 内实现（`HlgA = 0.17883277`、`HlgB = 0.28466892`、`HlgC = 0.55991073`，BT.2100-2）：先按系统 gamma `γ = 1.2 + 0.42·log10(L_W/1000)` 处理 OOTF，再做 OETF/OETF⁻¹ 的分段（`√(3E)` / `a·ln(12E − b) + c`，分界 `1/12`）。注释明确指出旧实现只返回场景线性、漏掉了定义上属于 HLG EOTF 的 OOTF。`Bt1886Eotf(signal, white, black)` 在有非零黑时用 BT.1886 的幂律形式，否则退化为 `γ = 2.4`：`ApplyBt1886Eotf` 返回 `…/white`（归一化），`ApplyBt1886InverseEotf` 走逆。

**（6）CCT / Duv（`ChromaticityToCct` / `CalculateDuv`）。** Ohno (2014) 的三角+抛物线解法：先把 `(x,y)` 转 CIE 1960 UCS `(u,v)`，在一张由 CIE 1931 2° 观察者生成的 Planckian 轨迹表上找最近点，再用抛物线细化。CCT 与 Duv 因此**互相自洽**（同一张表、同一坐标系），而不是两条独立公式。可解范围 `1667 K – 25000 K`；色度不合格时 CCT 回落 6500 K、Duv 回落 0，而不是抛异常——报告里的 CCT 是诊断量，不该让一次坏读数终止整轮。

**（7）色域覆盖率（`GamutCoverage` / `GamutCoverageUv`）。** `area(measured ∩ sRGB) / area(sRGB)`：在 xy（或 u'v'）平面把两个三角多边形规范化绕向后做 Sutherland–Hodgman 裁剪，再算有符号面积比。返回值可 > 1（超色域）。

**（8）CAM16-UCS（`Cam16Ucs`）。** 只实现正向 XYZ → J′a′b′（求解器从不需要逆）。默认观测条件为"夜间暗环境桌面"：`L_A = Y_white/5`（gray-world 约定）、`Y_b = 20`、`Surround.Dim`（`F=0.9, c=0.59, N_c=0.9`；Average 为 `1.0/0.69/1.0`，Dark 为 `0.8/0.525/0.8`）。核心步骤（源码逐一对应）：

```
D   = clamp( F·(1 − (1/3.6)·e^((−L_A − 42)/92)), 0, 1 )
k   = 1/(5·L_A + 1)；  F_L = k⁴·L_A + 0.1·(1−k⁴)²·∛(5·L_A)
n   = Y_b/100；  z = 1.48 + √n；  N_bb = N_cb = 0.725·(1/n)^0.2
锥响应：rgb = M16·x，rgb_w = M16·x_w（x 已缩放到 Y_white = 100 尺度）
适应增益：D_RGB_i = D·(100/rgb_w_i) + 1 − D
压缩：p = (F_L·|R_c|/100)^0.42，R_a = 400p/(p + 27.13) + 0.1（保号）
对立维：a = R_a − 12·G_a/11 + B_a/11，b = (R_a + G_a − 2·B_a)/9，h = atan2(b,a)
无色响应：A = (2R_a + G_a + B_a/20 − 0.305)·N_bb
J = 100·(A/A_w)^(c·z)；  e_t = 0.25·(cos(h′ + 2) + 3.8)
t = (50000/13)·N_c·N_cb·e_t·√(a²+b²) / (R_a + G_a + 21B_a/20)
C = t^0.9·√(J/100)·(1.64 − 0.29^n)^0.73；  M = C·F_L^0.25
J′ = 1.7J/(1 + 0.007J)；  M′ = ln(1 + 0.0228M)/0.0228
ΔE′ = √(ΔJ′² + Δa′² + Δb′²)      （≈1 单位 ≈ 该条件下的 1 个 JND）
```

它存在的理由写在注释里：`DisplayMatchSolver`（多显示器白点匹配）要在"候选状态亮度也不同"的条件下挑**视觉上最难察觉**的操作点，ΔE ITP 不建模观测条件相关的表观，CAM16-UCS 建模。

**（9）目标与显示类型。** `CalibrationTarget` 描述"理想显示"：三个基色、白点、`TransferFunctionType`（`Linear/Srgb/Gamma/Bt1886/Rec2020/Pq/Hlg`）、可选 `Gamma`、`PeakLuminance`、`BlackLevel`、`ReferenceWhite`、`IsHdr`（Pq 或 Hlg）、以及 `WhitePointOnly`。`StandardTargets.All` = `SrgbGamma22`、`Rec709PureGamma22`、`Rec709Gamma24`、`P3D65Gamma22`、`P3D65Gamma26`、`Rec2020Gamma24`、`Rec709Pq`；`SrgbPiecewise` 刻意不在 `All` 里。两条被注释反复强调的选择：

- "Gamma 2.2" 指**纯幂律 2.2**，不是 sRGB 分段曲线。sRGB 的分段（带线性趾）是**编码**函数，消费级显示器解码 sRGB 内容时用的是普通 2.2 幂律；拿纯 2.2 去对分段曲线评级，会在 10% 信号以下永久带一个 ~1.5–2 ΔE2000 的暗部惩罚。
- HDR 桌面目标用 `Rec709Pq`（sRGB/Rec.709 **内容**色域 + PQ tone）。在 HDR 下 Windows 已经把应用内容（sRGB）色彩管理到线缆上，所以透过该管线测到的基色 ≈ sRGB；修正的任务是残余色域/白点误差与面板 PQ 跟踪，而非扩展到容器色域（那正是宽色域目标被裁的原因）。

`DisplayType`（`LcdLed/Oled/LcdWideGamut/LcdCcfl/Crt/Plasma/Projector`）是"测量配置"而非数学：`DisplayTypeExtensions.IsRefreshTechnology` 标出 CRT/等离子/DLP（Argyll 按刷新方式测），OLED 与所有 LCD 非刷新；具体 `spotread -y` 选择器由 `SpotreadDisplayTypeTable` 按仪器解析（见 9.2.5 与 9.4）。`GamutReachability` 的两条常数闸门见 9.2.9。`ToneMappingAnalyzer` 是纯分析：在"密集近峰值梯子 + APL 窗口扫描"上找首次跌破 95% 跟踪的点作为真实 tone-mapping 膝点（`KneeTrackingThreshold = 0.95`，`KneeObserved` 区分"观测到离轨"与"只是下界"），并给出 HGIG 峰值、`MaxCLL`/`MaxFALL` 建议值。`HdrWirePatchSet` 的梯级是 `{0, 2, 4, 8, 16, 32, 64, 100, 150, 220, 320, 450, 650, 1000}` nits（对数间隔、上限 1000，因为以上属于面板自身 rolloff/ABL 的领域，LUT 不该去对抗），并按 DXGI 声明峰值的 0.9 倍设顶、必要时补一个顶部 rung。`DisplayCalibrationProfile` 是持久化的"这份校准是什么"（`Mode`：`Reference`/`Adaptive`；`TargetColorSpace` 含 `Native`），与 `CalibrationProfile`（9.5）分工不同：前者是运行时配置文件，后者是版本化历史。

### 9.4 ArgyllCMS 与外部依赖

**定位（`ArgyllPathFinder`）。** 单一入口 `FindArgyllBinPath()`，四级优先：应用自带/缓存的 Argyll（`argyll_cache`，通常最新）→ 标准安装路径 → `LocalAppData` 下的版本化目录 → DisplayCAL 捆绑的 Argyll（最后手段，可能很旧）。注释明确：**刻意不搜索 `PATH`**——校准会执行 `spotread.exe`，把发现范围限制在应用管理与已知安装位置是防"二进制植入"（binary planting）的措施。`FindArgyllToolPath(executableName)` 额外要求传入的是纯文件名（含目录分隔符直接抛 `ArgumentException`），避免把共享 finder 变成"任意进程启动器"。

**下载与安装（`ArgyllDownloader`）。** 版本集中在三行常量里：`ArgyllVersionNumber = "3.5.0"`、`ArgyllVersion = "Argyll_V3.5.0"`、URL `https://www.argyllcms.com/Argyll_V3.5.0_win64_exe.zip`。选 3.5.0 的理由写在注释里：3.3.0 在打不开色度计 HID 句柄时会静默 exit 0（正是 `SpotreadSession` 要绕开的"silent HID fail"），3.5.0 还带 i1d3 测量修复。完整性链条：

- 固定 SHA-256：`ArgyllArchiveSha256 = "C80F78BA30E715079A00A14C2E7C9F533C58E6C8FDBC27F2D32B3D7813F9D132"`。下载到临时文件后先 `SHA256.HashDataAsync` 比对，**不匹配则在解压/安装任何可执行文件之前**抛 `InvalidDataException`。
- 大小与条目上限：`MaxDownloadBytes = 128 MiB`、`MaxExtractedBytes = 512 MiB`、`MaxArchiveEntries = 10_000`。
- **Zip-slip 防护**：每个条目路径取 `Path.GetFullPath(Combine(stagingDir, entryPath))` 后必须仍在暂存目录内（`..\..\evil.exe`、绝对路径一律拒绝）。
- **替换语义**：先解压到 `LocalArgyllDir + ".staging-<guid>"`，再把旧目录 `Directory.Move` 成 backup、把 staging 改成正式目录名。旧版本在新版本就位前一直保留，所以取消或损坏不会让校准失去工具。
- 版本判定用数值比较而非字典序（否则 `"3.10.0"` 会排在 `"3.5.0"` 之下，压掉一次真实更新）；`version.txt` 作标记文件；`MinimumVersion = "3.0.0"`。

**一次性外部进程（`BoundedProcess`）。** `RunAsync(psi, timeout, ct)` 统一 `UseShellExecute=false/CreateNoWindow=true` + 双流重定向，超时或取消时 `Kill(entireProcessTree: true)` 后抛 `OperationCanceledException`；`BoundedProcessResult.Combined = stdout + "\n" + stderr`（Argyll 输出解析器预期的形状）。使用者：`spotread -?` 探测、`oeminst` 导入。

**`DispwinRunner` 在校准中的角色。** 它不在本目录（`src/Gloam.Core/DispwinRunner.cs`），但校准离不开它：`CalibrationStateManager` 用它 `ClearGamma`（进入 bypass）与 `ApplyCorrectionLut`（闭环把候选修正加载到被测显示器）。两处关键行为：（a）同一显示器的相同 LUT 重复应用会被跳过——每次 `dispwin` 调用都会重写 GPU ramp，用户能看见闪烁；（b）`VerifyAndRestoreRamps` 在恢复已保存 ramp 前先查 `CalibrationStateManager.IsDeviceInBypass(devicePath)`，命中就跳过——否则会在测量进行中把保存的 ramp 重新压上去，毁掉本次读数（见 9.6 的 bypass 不变量）。

**仪器与校正文件生态。** 这一组类构成"让三滤色色度计读得准"的完整链路：

| 类型 | 作用 |
| --- | --- |
| `SpotreadDisplayTypeTable`（`SpotreadDisplayTypes.cs`） | 解析 `spotread -?` 用法输出里的 `-y` 表（形如 `n|l  i1D3: Non-Refresh display [Default,CB1]`）；`ForInstrument` 按型号/描述符作用域化，`Resolve` 给出选择器与理由，`HasInstrumentRows` 判断表是否真的来自枚举到的仪器，`Describe` 供日志 |
| `CcssWriter` | 把实测 R/G/B/W 发射光谱写成 CCSS（CGATS 文本）：`SAMPLE_ID` + `SPEC_nnn` 的 `DATA_FORMAT`，四条数据行顺序固定 **white, red, green, blue**，白样本积分归一化到 Y = 100 |
| `CcmxWriter` | 由同一组 W/R/G/B 补丁在"参考光谱仪 vs 日常色度计"下的两组读数解 3×3 矩阵并写 CCMX；**Argyll 约定：存储矩阵把仪器（色度计）XYZ 映到参考（光谱仪）XYZ**，按行主序 |
| `CgatsText` | 两个 writer 共享的 CGATS 文本原语（引号/转义规则、`ctime` 格式）——注释说得好：转义规则的两份拷贝就是多出来的一份 |
| `CgatsValidator` | 不解析完整 CGATS 语法，只校验**结构骨架**：首行非空行必须是 `CCMX`/`CCSS` 关键字，然后 `NUMBER_OF_FIELDS n`、`BEGIN_DATA_FORMAT`…`END_DATA_FORMAT`、`NUMBER_OF_SETS n`（正整数）、`BEGIN_DATA`…`END_DATA`。用于在把文件交给外部进程前拒绝截断/畸形/恶意内容 |
| `CcssDatabaseClient` | DisplayCAL 社区校正库客户端（`https://colorimetercorrections.displaycal.net/`，API 与 DisplayCAL 相同：`?get=1&type=ccss&display=<pattern>&json=1`，每条 JSON 的 `cgats` 字段是完整文件）。响应上限 32 MiB、查询串上限 200 字符、本地结果上限 10 000；UA 固定 `Gloam/1.0`（服务端对通用/机器人 UA 返回 403）；`MergePreferSaved` 让本地已存副本优先 |
| `CcssMelanopicEstimator` / `MelanopicCalculator`（`MelanopicState.cs`） | 从 CCSS 光谱估相对 RGB melanopic/photopic 系数；`MelanopicCalculator` 计算 mel-EDI 与"相对 6500K 的降低百分比"（比值法除 CCSS 本身外不依赖任何假设；绝对 mel-EDI 需要假设观测立体角，默认 `DefaultViewingSolidAngleSr = 0.20` sr，并作为主导不确定度项显式上报而非隐藏真相） |
| `SpectralCaptureService` | 四补丁（白/R/G/B）光谱采集：每补丁多次读、逐波长取中位数、白样本归一化到 Y = 100，产出可直接喂 `CcssWriter` 的 `SpectralSample`。"显示补丁""读一次"以委托注入，故完全无硬件可测 |
| `MeterOffsetCaptureService` | 双仪器 CCMX 采集：phase 1 用仪器 A 测 W/R/G/B，提示换探头，phase 2 用仪器 B 测**同一组**补丁；每补丁多次 XYZ 逐分量取中位数。稳定性护栏：phase 内开头测白作 White 数据点、结尾复测白，若白亮度漂移超过 `MaxWhiteDriftFraction` 就拒绝出矩阵——否则会把面板漂移当成"仪器差异"烤进矩阵 |
| `OemCorrectionImporter` | 把已安装 X-Rite/Calibrite 软件里的 i1Display `.edr` 通过 Argyll 的 `oeminst` 转成 Argyll 校正，使 `-y` 表出现对应技术行。幂等：首次运行后、或无相关软件时是 no-op |
| `UsbDriverHelper` | 定位/启动 Argyll USB 驱动安装器（与卸载器），并用 `IsDriverError` 把 spotread 的错误文本分类成"可能是驱动/占用问题"，供 `ColorimeterService` 转成 `UsbDriverException` 触发 UI 引导 |

### 9.5 状态与持久化

**（1）测量期间的显示状态（`CalibrationStateManager`）。** 校准要测的是"面板本身"，所以所有应用层修正（gamma ramp、夜间模式）必须临时撤掉，测完再恢复。

- `EnterBypassMode(monitor, currentMode, currentSettings)` 保存 `SavedMonitorState`（设备路径、`MonitorInfo`、`GammaMode`、`CalibrationSettings`、`WasActive`），把夜间模式 `PauseUntil(now + 2h)`（宽松上限，之后会显式恢复），然后 **先注册 bypass、再清 ramp**。顺序是刻意的：泄漏窗口期越长，后台正在跑的一次 apply 就越可能在"清 ramp"和"注册 bypass"之间把修正压回去。
- **key 规则只有一处**：`BypassKey(monitor)` = 非空 `MonitorDevicePath` → 否则非空 GDI `DeviceName` → 否则 `"unknown"`。注册与所有查询（`IsDeviceInBypass(MonitorInfo)`、ramp 守卫、apply 路径守卫）都必须经这个 helper——否则设备路径为空时会出现"注册在一个 key、查询在另一个 key"，守卫静默失效。
- 静态注册表 `ActiveBypassPaths`（`StringComparer.OrdinalIgnoreCase` + 独立锁）让 UI 层（仪表盘卡片）也能知道"这台显示器正在校准中"，而不必显示它已保存的 profile 值装作生效。
- `RestorePreviousState`：`WasActive` 为真则重新 `ApplyGamma(…)`，否则 `ClearGamma` 强制恒等——因为闭环或一次崩溃的运行可能把最后一个候选 ramp 留在屏幕上，跳过恢复会把那条 ramp 永久留下。失败只记录不抛（恢复是尽力而为，但每个显示器都尝试）。`ExitBypassMode` 只清状态不恢复。

**（2）校准记录（`CalibrationProfile`）。** 带 `Guid Id` 的版本化记录：`MonitorDevicePath`/`MonitorName`/`MonitorSerial`、目标、特征化、逐版本快照（`CalibrationProfileSnapshot` 也带 `Id`/`ProfileId`），`AdvanceVersion()` 把当前状态推进历史链。落盘走 `TextFileStore.WriteAtomic`（**写临时文件再改名**：写到一半崩溃不会毁掉已有 profile），文件名 `{安全化的显示器名}_{Id:N}.json`，并有 `MaxFileBytes`/`MaxFileCharacters` 上限防御异常大文件。`CalibrationProfileManager` 负责目录枚举与装载；`CalibrationProfileData` 是内部序列化 DTO，与对外模型解耦。

**（3）运行期配置（`DisplayCalibrationProfile`）。** 与上者的区别：这是"当前生效的校准是什么"的运行期对象（`Version`、`Id`（GUID 字符串）、`Name`、`MonitorDevicePath`、`CalibratedAt`、`ColorimeterModel`、`Mode`、`TargetColorSpace`、测量值等），带 `MaxProfileFileBytes = 16 MiB`、`MaxProfileCharacters = 8_000_000` 的读取上限。

**（4）测量数据交换（`MeasurementCsvExporter` / `MeasurementCsvImporter`）。** 导出是"支持友好"的单表 CSV，列固定为：

```
report_id, phase, sequence_index, measurement_id, timestamp_utc,
patch_index, patch_name, patch_category,
display_r, display_g, display_b, patch_nits,
target_x, target_y, target_z,
measured_x, measured_y_nits, measured_z,
measured_xy_x, measured_xy_y, cct_k, duv,
integration_time_ms, is_valid, error_message
```

它刻意**不含** spotread 的进程输出（支持包要小而可预测）。导入器按**表头名**而非列位置解析，因此新版本多加的列不会破坏旧文件读取；未知列忽略；派生列（`measured_xy_*`、`cct_k`、`duv`）不读回——它们由 `MeasurementResult` 属性按需从 XYZ 重算，避免"文件里的派生值和公式算出的值不一致"这种双真相。

**（5）金标基线（`GoldenSampleBaseline`）。** 一段录下来的真实面板测量集经 `cli golden-ingest` 回放进在线管线，得到的数值被提交为基线 JSON：特征化（`BlackLevelNits`、`PeakLuminanceNits`、`MeasuredGamma`、六个基色/白点坐标、`HasPerChannelToneCurves`）、中性 tone 曲线在 `0.00, 0.05, …, 1.00` 的 21 个采样、验证指标（平均/最大/中位 ΔE、灰阶与基色 ΔE、平均 ΔE ITP）、扩展不确定度 `U95`，以及 HDR 线缆 LUT 的构建结果（`HdrMeasuredBlackNits`/`PeakNits`/`WireExact` + 17 点 LUT 采样）。纪律写在注释里：**基线只能由 CLI 重新生成，绝不由测试自己写**——这样每一处被接受的数值变化都出现在 git review 里。

**（6）长期趋势（`TrustCheckHistory` / `DriftPredictor`）。** 每显示器一份历史文件（带 schema 版本、容忍未知字段），`Append`/`Load`/`BuildCsv`，并给出漂移判读：`DeltaEDriftFloor = 1.0`、`DuvDriftFloor = 0.003`、`AnalyzeDrift` 返回 `DriftVerdict`、`ShouldRemind(lastCheckUtc, now, intervalDays)`。`DriftPredictor` 在其上做趋势外推（见 9.2.10 的诚实门）。

**（7）不确定度预算（`UncertaintyBudget`，1.3）。** 把"这个 ΔE 数字有多可信"变成可计算的三个标准不确定度分量，**按平方和开根（RSS）合成**，再乘覆盖因子：

- 重复性 `RepeatabilityStdU`：来自逐补丁多读脉冲的 Y 极差。`RangeToSigma(range, readCount)` 把极差换算成 σ，含中位数的效率因子 `MedianEfficiencyFactor = 1.2533`（中位数比均值低效，`MedianStandardErrorFactor(n)`）；对平均 ΔE 类指标按 `√(Σuᵢ²)/N` 组合（逐点噪声独立）。
- 仪器/校正 `InstrumentStdU`：按 `InstrumentClass` 给定——`ColorimeterWithCorrectionStdU = 0.5`、`ColorimeterGenericStdU = 1.5`、`SpectrometerStdU = 0.3`（ΔE 单位），`ClassifyInstrument(model, hasSpectralCorrection)` 分类。
- 漂移残差 `DriftStdU`：由 `PeakWhiteDriftFraction` 与"是否做过补偿"推得（`DriftResidualFraction = 0.25` 的残差比例假设）；亮度→ΔE 的换算用 `LStarPerRelativeLuminance = 22.0`。
- 合成：`CombinedStdU = Rss(三term)`，`ExpandedU = CoverageFactorK · CombinedStdU`（**k = 2**，约 95%），并给出每补丁的 `LuminanceUncertainty`。`Result.Describe()` 直接产出可放进 tooltip/日志的一句话：`± U ΔE (k=2, ~95%) — std-u terms combined in quadrature: repeatability …, instrument/correction …, drift residual …`。
- 光谱/褪黑素侧另有一组相对不确定度：`MelDerRelStdU_UserPanel = 0.05`、`_DbSameModel = 0.10`、`_Generic = 0.25`，加上观测几何 `ViewingGeometryRelStdU = 0.35`（几何是主导项，这也是为什么它被当作设置项而非隐藏常数）。

### 9.6 不变量、线程模型与风险

**（1）"测出来的数字不是预测的"。** 这是整个子系统的第一原则，落地为一组互相加固的机制：

- 验证必须**重新测**：`CalibrationVerifier` 的 after 值来自透过已安装修正的补丁扫描（"re-measured reality, not a model prediction"），而不是模型重投影。
- 模型残差必须**留一**：自适应放置用 LOO 误差，因为拟合内残差在被拟合点上恒接近 0。
- 非物理必须**NaN**：`DeltaEItp` 对非物理 XYZ 返回 NaN 而非 0，聚合按 `IsFinite` 过滤；0 会被读成完美匹配并通过有限性检查。
- 不收敛必须**留痕**：`UnsettledPatches` 记录没能稳定输出的补丁；自适应未达标置 `AdaptiveDegraded`；漂移超 8% 不补偿而是让校验器拒绝。
- 黑补丁不评 ΔE：`TrustCheck` 只按亮度报告黑——在黑的噪声底上报 ΔE 是在制造数字。

**（2）金标夹具是唯一的数值回归防线。** 硬件不在 CI 里，所以"公式/建模改动是否悄悄改变了结果"只能靠录制的真实测量集回放 + 提交的基线 + 显式容差来守。配套纪律：基线只能由 `cli golden-ingest` 生成；CSV 导入按表头名（文件格式可演进而回放仍成立）；导入的派生列一律重算。

**（3）bypass 期间禁止写 ramp。** 一旦某显示器进入 bypass，任何其他组件（后台 apply、夜间模式、ramp 恢复、`VerifyAndRestoreRamps`）都不得写入该显示器的 gamma ramp；唯一允许写的是闭环显式加载候选 LUT（`CalibrationStateManager.ApplyCorrectionLut`，且仅在 `_wasBypassActive` 时生效）。实现上是"静态注册表 + 两处守卫 + 注册先于清 ramp"，注释明确记录了反例（守卫缺失时后台 apply 会在测量中途压回校正）。这条不变量跨越 `CalibrationStateManager`、`DispwinRunner` 与应用层，是本文档里最需要保持的跨模块契约。

**（4）标识符必须是 GUID。** `CalibrationProfile.Id`、`CalibrationProfileSnapshot.Id`/`ProfileId`、`MeasurementResult.Id`、`CalibrationReport.Id` 都是 `Guid`；`DisplayCalibrationProfile.Id` 是 `Guid.NewGuid().ToString()`；文件命名用 `Id:N`。原因有三：显示器名字不是唯一键（同名双屏）、时间戳会重复（批量重放）、而序列化格式要求稳定标识。**另一个不同层面的 ID 也要注意**：写 MHC2 标签后必须把 ICC 头部 profile ID（字节 84–99）清零——正文变了、存档 MD5 已过期，留着旧 ID 会让系统/校验工具认为 profile 未变或损坏。

**（5）线程模型。**

- 采集是**单线程异步循环**：一个 patch 一个 patch 前进（`for` 循环 + `await`），没有并行测量——探头是独占资源，且 `SpotreadSession` 用 `_pendingMeasurement` 硬性拒绝"同一会话内并发测量"。
- 状态变更走 `_stateLock`；`SetState` 出锁后触发事件，`SetStateLocked` 明确要求处理器轻量且不得回调编排器。跨线程的 `Cancel()` 与收尾的状态判定必须在同一把锁内完成（见 9.2.1）。
- `ColorimeterService.InitializeAsync` 用"双检 + `_isInitializing` 标志 + 最多 60 s 轮询"实现线程安全的单次初始化，且不在 `await` 期间持锁。
- 静态 bypass 注册表、`TrustCheckHistory.Append` 等共享状态各自加锁；`DriftCompensator`/`Lut3DGenerator`/`ColorMath` 全部无状态，可并发调用。
- LUT 生成是**同步 CPU 循环**（17³ = 4913 点，65³ = 274 625 点），用 `Action<double> progress` 回报 0–100，不支持取消——这是刻意的：生成到一半放弃没有意义，代价也不高。

**（6）已知风险与脆弱点。**

- **Windows 行为随版本漂移**：25H2 上 `ColorProfileGetDisplayList` 返回空列表却关联生效（9.2.9），这类"文档与实际相反"的行为只能靠实测注释与回读校验兜住；新的 Windows 版本仍可能再次改变语义。
- **Argyll 输出措辞跨版本变化**：就绪提示、光谱头、错误前缀都用"片段匹配 + 版本容忍"处理；新增措辞会导致超时而非误判，但会表现为"连不上"。
- **硬件瞬态**：ABL/热漂移/局部调光属于物理世界，代码只能检测、记录、拒绝，不能修正；`SettleHighAplAsync` 的 24 次上限意味着极慢瞬态仍会被记为 unsettled。
- **`CalibrationProfileInstaller.ClassifyHdrFromName` 从 profile 名字反推目标**：这是给"只有名字可用"的场景留的路径，名字被用户改过就会失配。
- **HLG 未端到端验证**：`TransferFunctionType.Hlg`、`ApplyHlgEotf/Oetf`、`StandardTargets.Rec2020Hlg` 都存在，但 `StandardTargets.All` 不含它，安装/验证路径对 HLG 的实测证据在本次阅读范围内**没有找到**（见 9.7 的未验证项汇总）。
- **模型型功能的天花板**：`DisplayMatchSolver` 直接在存储的特征化上求解，其注释自陈"结果只和每块面板的校准一样好，残余误差与仪器间异谱同色不可见"。
- **文本持久化的边界**：`TextFileStore` 原子写 + 大小上限能防损坏与畸形文件，但不提供跨版本迁移保证（`Version`/`SchemaVersion` 字段只是迁移的抓手）。

### 9.7 文件→职责表（57 个文件）

行数为该文件当前行数（含空行），用于定位复杂度分布。

| 文件 | 行数 | 职责 |
| --- | --- | --- |
| `AdaptivePatchPlanner.cs` | 447 | 自适应选点：`SignalManifold`/`ModelResidual`、残差+空隙两项打分、批次内最小间隔抑制、停止与平台期判据 |
| `AdvancedColorProfileAssociation.cs` | 498 | WCS Advanced Color 关联：current-user 作用域、默认关联回读校验、`ActivationReceipt` 回滚、平台接口（测试可替身） |
| `AdvancedColorProfileMigrationService.cs` | 305 | 启动时保守修复旧版创建的 HDR profile（前缀 + MHC2 载荷 + 显示器设置三重条件） |
| `ArgyllDownloader.cs` | 376 | Argyll 3.5.0 下载：HTTPS、固定 SHA-256、大小/条目上限、zip-slip 防护、staging 替换 |
| `ArgyllPathFinder.cs` | 322 | 四级优先定位 Argyll bin（**不搜 PATH**）、纯文件名工具查找、有效性校验 |
| `BoundedProcess.cs` | 74 | 一次性外部进程运行器：双流捕获、硬超时、杀整个进程树 |
| `CalibrationInstallPreflight.cs` | 427 | 安装前"世界变了"检查、第三方默认 profile 评估、ACM/NightLight 探测、MHC2 标签存在性 |
| `CalibrationMeasurementValidator.cs` | 282 | 物理合理性闸门集合（样本量/数值/锚点/漂移/单调/色度）+ 可执行恢复建议 |
| `CalibrationOrchestrator.cs` | 1681 | 采集编排：状态机与事件、逐补丁时序、自适应轮次、漂移、闭环、结果聚合 |
| `CalibrationProfile.cs` | 688 | 版本化校准记录（`Guid`、快照链、原子落盘）+ `CalibrationProfileManager` |
| `CalibrationProfileInstaller.cs` | 875 | 色域矩阵计划、tone LUT 分派、MHC2 安装与关联、失败回滚、`Disable`/`Reenable`、名称编解码 |
| `CalibrationReport.cs` | 890 | 报告聚合模型：ΔE 统计、灰阶跟踪、gamma、RGB 平衡、色度、CCT、质量评级、问题列表 |
| `CalibrationStateManager.cs` | 287 | bypass 静态注册表与状态保存/恢复、`BypassKey` 规则、夜间模式暂停 |
| `CalibrationTarget.cs` | 594 | 目标定义与 `ApplyOetf`/`ApplyEotf`（含 HLG 系统 gamma、BT.1886、PQ 适配）+ `StandardTargets` |
| `CalibrationVerifier.cs` | 383 | 安装后验证指标、补丁线性化唯一规则、ΔE ITP、彩色 HDR 刺激评级 |
| `Cam16Ucs.cs` | 161 | CAM16 正向模型 → J′a′b′ 与 ΔE′（多屏匹配的感知度量） |
| `CcmxWriter.cs` | 386 | 解/写 CCMX 3×3（色度计 XYZ → 参考 XYZ），CGATS 行主序 |
| `CcssDatabaseClient.cs` | 440 | DisplayCAL 校正库检索/保存/去重，本地已存副本优先合并 |
| `CcssMelanopicEstimator.cs` | 494 | 从 CCSS 光谱估 melanopic/photopic 系数，光谱解析与绝对计算 |
| `CcssWriter.cs` | 205 | 写 CCSS：`SAMPLE_ID` + `SPEC_nnn`，四行固定 W/R/G/B，白归一到 Y = 100 |
| `CgatsText.cs` | 36 | CGATS 文本原语：关键字、引号/转义、`ctime` 格式 |
| `CgatsValidator.cs` | 333 | 校正文件结构骨架校验（截断/畸形/恶意内容在上进程前被拒） |
| `ClosedLoopCorrector.cs` | 430 | VCGT signal→signal 修正的构建/细化/分解/合成与残差打分 |
| `ColorimeterService.cs` | 997 | spotread 封装：初始化、设备探测、持久会话、`-y` 表维护、光谱会话、异常分类 |
| `ColorMath.cs` | 1256 | XYZ↔Lab、Bradford、CAT16、RGB↔XYZ 矩阵推导、sRGB/Rec.2020 传递函数、Oklab、CCT/Duv、色域覆盖 |
| `ColorTypes.cs` | 564 | 值类型 `Chromaticity`/`CieXyz`/`CieLab`/`LinearRgb` + 标准白点与基色常量 |
| `DisplayCalibrationProfile.cs` | 445 | 运行期校准配置对象（`Mode`、`TargetColorSpace`、测量摘要、读取上限） |
| `DisplayMatchSolver.cs` | 166 | 多显示器共同白点 minimax 求解 + 每面板增益/亮度建议（模型型，限制自陈） |
| `DisplayType.cs` | 86 | 显示技术枚举与 `IsRefreshTechnology` 判定 |
| `DriftCompensator.cs` | 208 | 乘性漂移拟合与归一化（>8% 拒绝补偿），黑锚点仅分析与报告 |
| `DriftPredictor.cs` | 148 | 趋势 OLS 与诚实门（≥3 点、≥7 天、斜率显著才给预测） |
| `GamutReachability.cs` | 112 | 基色驱动范围闸门 `[-0.05, 1.3]`，拒绝面板物理上达不到的目标 |
| `GoldenSampleBaseline.cs` | 189 | 金标基线 JSON：特征化、验证指标、U95、HDR LUT 采样 |
| `HdrColorMatrixRefiner.cs` | 389 | 闭合彩色细化：拟合 3×3 残差 F、保留中性亮度、`HdrColorMatrixLoop` 封装 |
| `HdrJointRefinement.cs` | 330 | 矩阵 + PQ tone LUT 作为原子候选的联合求解与 keep-best |
| `HdrMhc2LutBuilder.cs` | 521 | HDR PQ 域 LUT 构建/细化/尺度重定基/平均绝对亮度误差 |
| `HdrRefinementLoop.cs` | 225 | refine → install → re-measure 迭代（阻尼、收敛、保留最优） |
| `HdrWirePatchSet.cs` | 55 | HDR 线缆梯补丁（对数间隔 0–1000 nits，按声明峰值 0.9 设顶） |
| `Lut3D.cs` | 546 | 3D LUT 容器（尺寸 2–256，常用 17/33/65）+ 四面体插值 |
| `Lut3DGenerator.cs` | 1487 | 特征化、逆向显示模型、Oklab 保色相色域压缩、LUT 生成、模型残差、`ToneCurve`（PCHIP）与指标 |
| `MeasurementCsvExporter.cs` | 138 | 25 列测量 CSV 导出（不含进程输出） |
| `MeasurementCsvImporter.cs` | 308 | 按表头名回读 CSV 为 `MeasurementResult`，派生列重算 |
| `MeasurementResult.cs` | 230 | 测量结果、`ColorPatch`、`PatchCategory`、派生量（Lab/CCT/Duv/ΔE2000）、多读元数据 |
| `MelanopicState.cs` | 136 | melanopic-EDI 计算（默认观测立体角 0.20 sr，几何作不确定度项） |
| `MeterOffsetCaptureService.cs` | 189 | 双仪器 CCMX 采集：换探头两阶段、逐分量中位数、白漂移护栏 |
| `Mhc2ProfileBuilder.cs` | 688 | 字节级改写 MHC2 标签、特征化 ICC 标签、矩阵 XYZ 域包裹、profile ID 清零、旧 profile 修复 |
| `OemCorrectionImporter.cs` | 289 | 已装 OEM 软件 EDR → Argyll 校正（`oeminst`），幂等 |
| `PatchSetGenerator.cs` | 940 | 预设补丁集、8 bit 量化去重、最小 settle 排序、漂移锚点、重编号、自适应候选池 |
| `SpectralCaptureService.cs` | 162 | 四补丁光谱采集（逐波长中位数、白归一） |
| `SpotreadDisplayTypes.cs` | 465 | 解析 `spotread -?` 的 `-y` 表、按仪器作用域化、给出选择器与选择理由 |
| `SpotreadSession.cs` | 1325 | spotread 长会话协议状态机：就绪/校准提示/致命错误/探头位置/超时毒化/光谱解析/残留进程清理 |
| `ToneMappingAnalyzer.cs` | 185 | tone-mapping 膝点、APL 扫描、HGIG 峰值、MaxCLL/MaxFALL 建议 |
| `TrustCheck.cs` | 119 | 6 补丁快检评级（黑只按亮度，其余复用统一指标路径） |
| `TrustCheckHistory.cs` | 214 | 每显示器趋势历史（schema 版本、未知字段容忍）、漂移判读、CSV |
| `UncertaintyBudget.cs` | 554 | 十倍程噪声模型（EWMA + 滞回）+ 三项不确定度 RSS + k=2 扩展 + melanopic 相对不确定度 |
| `UsbDriverHelper.cs` | 177 | USB 驱动安装器/卸载器定位与启动、驱动错误文本分类 |
| `VerificationPatchSets.cs` | 488 | 详细验证集、彩色 HDR 刺激与容器参考、分类汇总、profile 生效检查 |

**本章未核实的点（供后续修订）**：`PatchCategory` 的完整成员未逐行核对（本章按使用点记录到 7 项）；HLG 目标虽有类型、常量与 `StandardTargets.Rec2020Hlg`，但不在默认流转中，安装/验证路径的端到端证据未找到；`srgb_to_gamma2p2_sdr.icm` 与 `srgb_to_gamma2p2_unspecified.icm` 被 `ResourceExtractor` 发布、也被发布校验脚本检查，但 `FindTemplate` 只按 `{100,200,300,400}` 选 `_mhc2` 变体，这两个模板的消费方不在本章阅读范围内；`src/Gloam`（WPF）侧的交接点（补丁窗渲染、向导步骤、报告界面）按任务划分未阅读，本章只描述核心侧契约。

---

## 10 校准子系统（二）：UI、报告与状态

> **范围**：`src/Gloam/Features/Calibration` 全部文件。校准的领域算法（状态机、色度学、profile 构建、验证数学）在 §9；本节只回答四件事：**谁把领域动作串起来、谁在屏幕上画色块、谁独占探针、谁把结果写成可回放的报告**。
>
> **规模（实测）**：目录下 37 个文件（29 个 `.cs` + 8 个 `.xaml`），合计 14,390 行；其中 10 个文件超过 400 行，最大的 5 个是 `CalibrationReportWindow.xaml.cs` 2,993 / `CalibrationWindow.xaml.cs` 2,118 / `CalibrationSetupViewModel.cs` 1,182 / `CalibrationReportWindow.xaml` 635 / `CalibrationWindow.xaml` 533。任务书写"42 个文件"与目录实际条目不符（无子目录），本文以 37 为准。
>
> 依赖方向：`Gloam`（UI）→ `Gloam.Core` / `Gloam.Interop` / `Gloam.Localization.Wpf`。UI 层不实现色彩数学，只做**编排、呈现、所有权、持久化交接**；唯一被承认的例外是 `HdrPatchRenderer`（见 §10.2）。

### 10.1 流程编排：从托盘点击到报告窗口

#### 10.1.1 入口与单探针守卫

托盘菜单"Calibrate Display"落到 `TrayViewModel.OpenCalibration`（`src/Gloam/Features/Tray/TrayViewModel.cs:887`），它只做一件事：`_calibrationLaunchCoordinator.Open()`。

`CalibrationLaunchCoordinator.Open`（`CalibrationLaunchCoordinator.cs:39-112`，114 行）是这个流程的唯一入口，职责有三条：

1. **禁止并行流程**。它先遍历 `Application.Current.Windows`，只要存在 `CalibrationSetupWindow`、`CalibrationWindow`、或 `CalibrationReportWindow && report.IsVerifyRunning`，就把该窗口 `Activate()` 后直接返回（`:44-55`）。原因写在代码注释里：**机器上只有一个探针**。同一守卫在 `TrayViewModel.OpenTrustCheck` 与报告窗口的 `EnterProbeBusy` 里各重复了一次（见 §10.6）。
2. **模态跑 setup，并接收探针所有权**。`setupWindow.ShowDialog()` 之后从 `setupWindow.ColorimeterService` 取走已初始化的 `ColorimeterService`；`DialogResult != true` 或任何一项选择为空时 `colorimeter?.Dispose()` 后返回（`:62-70`）。
3. **窗口链与所有权转移**。构造 `CalibrationWindow`（注入 `colorimeter`、`SelectedTarget`、`SelectedPreset`、`new CalibrationStateManager(_dispwin, _nightMode)`、`monitor`、上次的 `GammaMode`/`CalibrationSettings`、`SettingsManager`），`WindowBoundsPersistence.CopyBounds` 继承 setup 的窗口位置与尺寸，再 `ApplyUiState(calibrationUiState)` 恢复上次运行的运行参数。

返回/关闭的所有权规则（`CalibrationLaunchCoordinator.cs:91-110`）：

| 用户动作 | 窗口事件 | 探针归宿 |
|---|---|---|
| 点 Back（`CalibrationWindow.BackRequested`） | 先 `CaptureUiState()`，再 `Closed` | `BeginInvoke` 重新 `Open(devicePath, uiState, colorimeter)` —— **同一个实例交回 setup**，避免重新枚举/重新初始化 USB |
| 正常关闭、取消、校准失败 | `Closed` | `colorimeter.Dispose()` |
| setup 被取消 | `ShowDialog() != true` | `colorimeter?.Dispose()` |

`_calibrationClosed()` 无论哪条路径都会调用，用于托盘刷新。`CalibrationWindow.CalibrationCompleted` 成功时额外 `_refreshMonitors()`。

#### 10.1.2 Setup：`CalibrationSetupWindow` + `CalibrationSetupViewModel`

窗口本身只有 121 行 code-behind，注释明确写了"view-only concerns live here"：窗口 chrome、主题切换、`WindowBoundsPersistence.Attach(this, settingsManager, "CalibrationSetup")`、子对话框宿主、`DialogResult` 与 `CloseRequested`。**所有决策都在 1,182 行的 `CalibrationSetupViewModel` 里**，窗口只把 `SelectedTarget` / `SelectedPreset` / `SelectedMonitor` / `SelectedDisplayType` / `ColorimeterService` 暴露给协调者。

ViewModel 的七个职责区（按 `#region` 与实测方法名）：

| 职责 | 关键成员 | 行为要点 |
|---|---|---|
| 显示器与面板类型 | `DetectPanelType(MonitorInfo)`、`IsOled`/`IsLcdWideGamut`/`IsLcdCcfl`、`DetectedDisplayType` | 由 EDID/实测原色三角面积推断面板技术，作为 `spotread -y` 提示；用户仍可改 |
| 目标（target） | `TargetOption`、`UpdateTargetAvailability`、`ResolveTargetSelection`、`SelectTarget`、`SelectSavedTarget` | HDR 开启时只允许 HDR 目标，SDR 时可选择但给警告；`WhitePointOnly` 是独立开关 |
| 预设（preset） | `IsPresetAdaptive/Quick/Standard/Thorough`、`PatchCountFor`、`FormatEstimatedTime`、`PresetDetail` | 标签里的色块数与预计时长来自 `PatchSetGenerator.GetApproximatePatchCount` / `GetEstimatedTime`，不是硬编码 |
| 机内修正文件 | `SelectedCorrection`、`PopulateCorrectionFiles`、`SelectCorrectionPath`、`ValidateCorrectionChoice` | 只接受 `.ccss`/`.ccmx`，且先过 `CgatsValidator.Validate(File.ReadAllText(path), ext)`；`MeterUsesGenericCalibration` 会单独警告 |
| 预检 | `RefreshPreflight`、`BuildPreflightMessages`、`AddHdrLuminanceWarnings`、`PreflightItems` | 环境探针 `CalibrationInstallPreflight.DetectNightLightActive()` / `DetectSdrAutoColorManagement(...)` 返回 `bool?`，**未知（null）不阻塞不警告**；HDR 亮度元数据 5 条检查（peak 缺失 / min≥peak / full-frame > peak / 目标 peak 超面板 / 参考白接近面板 peak） |
| 能否开始 | `CanStart`、`StartBlockReason`、`CriticalStartError` | 只有特定 ERROR 才阻塞（选择显示器 / 目标 / 需要 HDR / SDR 目标在 HDR 下 / correction 相关），其余 WARN 放行 |
| 探针生命周期 | `InitializeColorimeterAsync`、`RefreshColorimeterAsync`、`StopDetection`、`StatusText`/`StatusDetailText` | 窗口 `Loaded` 触发 `OnLoadedAsync`，`Closed` 触发 `StopDetection()`；`ArgyllDownloadDialog` 由 `OfferArgyllDownload` 回调弹出，下载成功后重试初始化 |

子对话框都从 setup 打开：`ArgyllDownloadDialog`（下载/更新 ArgyllCMS）、`CcssBrowserWindow`（DisplayCAL 校正库 + 自制 `.ccss`/`.ccmx`）、`ProfileManagerWindow`（该显示器已安装 profile 的激活/停用/删除）、`ReportHistoryWindow`（历史报告浏览）。

#### 10.1.3 运行窗口：`CalibrationWindow` + `CalibrationViewModel`

`CalibrationWindow.xaml.cs` 2,118 行 / 94 KB，是校准期间唯一的可见窗口。它有**四个互斥视图状态**（都由 `CalibrationViewModel` 的布尔属性驱动）：setup 说明页（`IsSetupVisible`）、定位页（`IsPositioningVisible`）、测量页（`IsMeasurementVisible`）、完成页（`IsCompletionVisible`），外加暂停遮罩（`IsPauseOverlayVisible`）与窗口化提示条（`ShowWindowedModeBanner`）。

关键机制：

- **运行参数是可回传的状态**。`public sealed record UiState(bool IsFullScreenMode, bool AlwaysOnTop, bool SoundOnCompletion, bool SoundOnCapture, bool RunDetailedVerify, int PatchSize, double PatchOffsetX, double PatchOffsetY)`（`CalibrationWindow.xaml.cs:195-203`）+ `CaptureUiState()` / `ApplyUiState(UiState?)`。Back 时由协调者带着它回到 setup，再进新窗口时恢复，所以"调整一次 patch 大小/偏移"不会因为返回上一页而丢失。`PatchSize` 在 `ApplyUiState` 里被夹到 `[120, 2000]`。
- **全屏是"按目标显示器的物理像素"移动窗口**。`ApplyDisplayMode()`（`:612-671`）在 `IsFullScreenMode` 时用 `TryGetTargetMonitorPixelBounds`（目标 `HMONITOR` → DXGI 桌面 bounds → 窗口当前所在显示器，三级回退）+ `MoveWindowToPixelRect` 走原生 `SetWindowPos`；注释解释了为什么不能用 WPF 的 `Left/Width`：那是 DIP，非 100% 缩放下会算出错误的物理尺寸，甚至把窗口放到**另一台**显示器上（探针就会测错屏）。窗口化模式下 `CenterOnTargetMonitor(600, 500)`，同样以目标显示器为中心。
- **色块可以被用户微调**。测量页支持拖动（`PatchDrag_MouseDown/Move/Up`）与方向键微调（`TryNudgePatch`，步长 5 px / Shift 25 px），偏移量写回 `_patchOffsetX/_patchOffsetY` 并同步到 `MeasurementPatchTransform`。定位页的微调委托给共享控件 `ProbePlacement`（`ProbePlacementControl`）。
- **键盘契约**：`Escape` 在运行中 = 暂停；暂停中 = 取消；未运行 = 关窗。`Space` 在暂停中 = 恢复。方向键优先给色块微调。
- **关闭是恢复契约的收口**。`Window_Closing`（`:333-381`）依次：确认"校准进行中是否取消"→ `_cancellationTokenSource?.Cancel()` → `_inputLock.Release()`（系统级键盘钩子绝不允许随窗口泄漏）→ `DisposeHdrWireRenderer()` → `_stateManager.RestorePreviousState()`（成功校准后也要做：闭环把候选校正留在 ramp 上，托盘会再按 MHC2 组合态重设）→ `RestoreProfileDisabledForMeasurement()`（自己与"外来"被禁用的 profile 都恢复）→ 恢复屏保 → 退订 `ColorimeterService.StatusChanged`。
- **崩溃恢复**：测量前会临时禁用本机已有的（含非 Gloam 的）校准 profile，为了让探针测到原生面板。若进程中途死亡，内存里的恢复清单就没了，于是 `PersistForeignRestoreIntent` / `ClearForeignRestoreIntent` / `RecoverStaleForeignRestoreIntent`（`:468-585`）把一个"待恢复"意图写到磁盘上，下一次 `Window_Loaded` 先执行恢复再开始测量。
- **视图与 UI 状态本身在 `CalibrationViewModel`**（222 行）：`CalibrationViewModel.cs` 的注释写明测量编排留在窗口里（因为它与窗口尺寸、HDR 渲染器、模态对话框纠缠），视图模型只提供 XAML 绑定的一切。这也是 §10.3 讨论报告窗口体积时的同一条原则。

#### 10.1.4 三个协调者：谁的代码算"流程"

| 协调者 | 行数 | 持有什么 | 不做/不持有 |
|---|---:|---|---|
| `CalibrationSessionCoordinator` | 180 | `CalibrationOrchestrator` 一个实例；6 个 orchestrator 事件的转发；`ClosedLoopConfig` 与 HDR wire patch 的装配 | 不碰 UI；`RunAsync` 返回 `Result(Calibration, Lut, Characterization, Metrics)` |
| `VerificationCoordinator` | 70 | 一次验证扫描的**时序**：逐 patch `ReportProgress` → `ShowPatch` → 延时 → `MeasureAsync` →（可选）`MeasurementCaptured` | 不知道窗口、探针、图表；渲染与测量都以 `Action`/`Func` 注入 |
| `HdrRefinementCoordinator` | 152 | 关节式 HDR 精修的 UI 侧：矩阵 neutral-scale 解析、原子安装、被取代 profile 清理、`RecordInstalled` 回写 | 不做数学（在 `HdrJointRefinement`），不画界面 |

`CalibrationSessionCoordinator.RunAsync` 的顺序是校准产物的定义：先 `orchestrator.StartCalibrationAsync`；成功后 `new Lut3DGenerator(target, measurements, lutSize: 33)`；HDR 走 `BuildCharacterizationOnly(hdrMode: true)`（不出 3D LUT，PQ tone LUT 稍后由 `HdrMhc2LutBuilder` 从 wire ladder 生成），SDR 走 `Generate(progress)`；两条路最后都 `CalculateMetrics()`。`ConfigureClosedLoop()` 只在 SDR、有 `StateManager`/`Monitor`、且 `RefinementRounds > 0` 时装配 `ClosedLoopCorrector` + `stateManager.ApplyCorrectionLut` + `TargetDeltaE = 1.0`。`ConfigureHdrPatches()` 在 HDR 下把 `HdrWirePatchSet.Build(monitor.HdrPeakNits)` 挂到 `orchestrator.AdditionalPatches`。

`VerificationCoordinator` 的延时是硬编码的 1,200 ms（首个 patch，等面板从界面切到满屏色块）与 500 ms（其余）；它在**最后一个 patch 测量返回后**再检查一次取消（`:53-55`），因为驱动可能在取消后仍把读数交回来——这种读数不能当作"验证成功"发布。测试 `VerificationCoordinatorTests` 正是钉这三条：顺序、取消在呈现前生效、取消在最后一 patch 期间不能发布成功。

#### 10.1.5 `ProbeOperationScope`：所有"把探针放上屏幕"的操作共用的事务

`ProbeOperationScope`（208 行）是第二次校准之后新增的抽象，注释写着"construction is transactional; a failed startup runs the same cleanup path as a completed or cancelled sweep"。它一次持有五样东西，并在 `DisposeAsync` 里按相反顺序释放：

1. 取消源 `_cancellation`（链接调用方 token）；
2. `PatchDisplayWindow`（完整色块窗口，含 `AbortRequested` → `Cancel`、`Closed` → `Cancel`）；
3. **校正绕过** `options.StateManager.EnterBypassMode(monitor, previousGammaMode, previousSettings)`（`EnterBypass` 为真时；失败且有 `ContinueIfBypassFails` 时只记日志继续）；
4. **仪器会话**：`SessionKind.Measurement` → `BeginMeasurementSessionAsync(hdrMode, Token)`；`SessionKind.Spectral` → `BeginSpectralSessionAsync(Token)`；
5. 忙标志：`EnterBusy` / `ExitBusy` 回调，让宿主窗口禁用一组按钮。

启动顺序固定为：`ConfigurePatchWindow` → `EnterBusy` →（可选）bypass → `PatchWindow.Show()` →（可选）`WaitForPlacementAsync` → `PlacementCommitted(offsetX, offsetY)` → `StartSessionAsync()`。`EndSessionAsync()` 是给"双仪器校正采集"用的提前交还 USB 的钩子（保留已定位的色块窗口）。

**这就是"探针所有权"在代码里的形状**：一个探针 = 一个打开着的 spotread 会话 = 一个 `ProbeOperationScope`；任何拿探针的操作都必须经它，从而保证异常路径也恢复 ramp / 关闭窗口 / 结束会话 / 解除按钮禁用。

#### 10.1.6 `CalibrationWindow` 的视图状态机

窗口用三个布尔量（`_isCalibrationRunning`、`_isPaused`、`_isCancelled`）驱动所有守卫，用 `CalibrationViewModel` 的四个 `Is*Visible` 驱动画面。转换关系如下（方法名均可 grep）：

| 当前 | 触发 | 转换动作 |
|---|---|---|
| setup 说明页 | `Start_Click` / `BeginMeasurement_ClickAsync` | 构造 `CalibrationSessionCoordinator.Config`（Colorimeter/Target/Preset/HdrMode/Monitor/StateManager/RefinementRounds），接 6 个事件转发，切定位页；`_isCalibrationRunning = true` 前先 `_inputLock.Engage()` |
| 定位页 | 共享定位面的 `BeginRequested`（Enter/Space/双击）或"开始测量" | 落定 `_patchSize` / `_patchOffsetX/Y`，`UpdatePatchSize()`，切测量页；`PatchDisplayWindow`/`CalibrationWindow` 的定位与测量用同一 patch 尺寸 |
| 测量页 | orchestrator 的 `ProgressChanged` / `StateChanged` / `PhaseChanged` | 更新进度条、Current/Next 文案、暂停遮罩；`StateChanged(Running)` 重新 `Engage()` 输入锁，`StateChanged(Paused)` 立即 `Release()` |
| 测量页 | `RunCalibrationAsync` 返回成功 | 清 `_isCalibrationRunning` → `ShowCompletion(true, detail)` → **立即** `ViewReport_Click`（先清标志再关窗，否则 `Window_Closing` 会弹"校准进行中，确定取消？"把流程卡成 report→prompt 循环——注释记录过这个 bug） |
| 任意 | `ReturnToSetup()`（取消/失败） | 恢复 ramp 与被禁用 profile、清所有视图布尔、恢复窗口 chrome 并 `CenterOnTargetMonitor(600, 700)`（**回到被校准的那台显示器**，不是主屏） |
| 任意 | `Back_Click` | 触发 `BackRequested` 并 `Close()`，所有权交回 `CalibrationLaunchCoordinator` |

`MeasurementForegroundGuard` 的判定 lambda 是 `() => _isCalibrationRunning && !_isPaused && !_isCancelled`——暂停/取消期间不再抢前台，这是"暂停时把桌面还给用户"的同一条原则。

### 10.2 色块显示：让探针看到什么

#### 10.2.1 两条渲染路径

| 路径 | 触发条件 | 实现 | 可见精度 |
|---|---|---|---|
| WPF 8-bit SDR | `ColorPatch.Nits == null`（**包括 HDR 模式下的普通灰度/原色色块**） | 窗口里一个 `Border.Background = new SolidColorBrush(Color.FromRgb(b,b,b))`；`CalibrationWindow` 用 `ColorPatchBorder`，`PatchDisplayWindow` 用 `_patch` | 8-bit sRGB surface，`v → byte` |
| FP16 scRGB 覆盖层 | `ColorPatch.Nits != null`（HDR wire ladder） | `HdrPatchRenderer`：独立的 Win32 弹窗 + D3D11 swapchain | 绝对亮度（scRGB = nits / 80） |

8-bit 路径有两个必须保持一致的细节：

- **生成时就吸附**：`PatchSetGenerator.Snap8Bit(v) = round(clamp(v,0,1)·255)/255`（M9）。注释给出的理由：SDR 色块窗口最终只能发出 n/255，所以在生成阶段就把模型输入坐标变成"物理上真的会显示的值"，而不是事后拿光去拟合一个差半个 8-bit 步长的坐标。
- **渲染用 `Math.Round` 而不是截断**：`PatchDisplayWindow.ToPatchByte(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255)`。注释点名了截断的偏差：每个码值最多低 1/255，在 5% 灰处约 6% 相对误差；校准与验证必须为同一 patch 渲染**完全相同的码值**，否则 before/after 比较会漂。

#### 10.2.2 `HdrPatchRenderer`：住在 UI 工程的 Core 职责

这是全仓库唯一被文档承认的分层例外（`docs/project-structure.md:95-96`："Domain code does not live in the UI project. `HdrPatchRenderer` is the current exception and is a candidate to move into `Gloam.Core`"）。

它为什么存在（文件头注释即设计文档）：WPF/GDI 窗口渲染进 8-bit sRGB surface，其中**最亮的可表示像素就是 SDR 白** —— 合成器把 (255,255,255) 映射到 SDR 内容亮度，再亮的值无法编码。而 HDR 校准需要发出高于 SDR 白的绝对亮度。scRGB FP16 中 1.0 = 80 nits 且允许 >1.0，1000 nits 就是浮点 (12.5, 12.5, 12.5)；合成器（DWM）自己把它转成 PQ/BT.2020 上线，走的正是真实 HDR 内容的同一条路径。

实现要点（均为可 grep 的常量与调用）：

- **手写 COM vtable 互操作**，无外部包：`D3D11CreateDevice` → `Dxgi.CreateDXGIFactory1` → `QueryInterface(IDXGIFactory2)` → `CreateSwapChainForHwnd` → `QueryInterface(IDXGISwapChain3)` → `CheckColorSpaceSupport` / `SetColorSpace1(DXGI_COLOR_SPACE_RGB_FULL_G10_NONE_P709)`。槽位号是文件里最"承重"的常量：`Slot_Factory2_CreateSwapChainForHwnd = 15`、`Slot_SwapChain_Present = 8`、`Slot_SwapChain_GetBuffer = 9`、`Slot_SwapChain3_CheckColorSpaceSupport = 37`、`Slot_SwapChain3_SetColorSpace1 = 38`、`Slot_Device_CreateRenderTargetView = 9`、`Slot_Context_ClearRenderTargetView = 50`。
- **swapchain**：`DXGI_FORMAT_R16G16B16A16_FLOAT` + `DXGI_SWAP_EFFECT_FLIP_DISCARD`（注释：FP16 强制 flip model），2 个 buffer，`SetColorSpace1` 失败即抛"scRGB 不可用"。
- **一个颜色一次 Present**：`PresentNits(r,g,b)` = `GetBuffer(0)` → `CreateRenderTargetView` → `ClearRenderTargetView(nits/80, nits/80, nits/80, 1)` → `Present(1,0)`；flip-model 的呈现会留在屏幕上，因此不需要渲染循环。
- **窗口**：`CreateWindowEx(WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, WS_POPUP, "HdrPatchRendererWindow", ...)` + `WindowTheme.ExcludeFromPeek(_hwnd)`（否则鼠标蹭任务栏触发 Aero Peek，探针会读到玻璃而非色块）+ `ScRgbSupported` 暴露给调用方判断"这条路径是否真的可用"。
- **生命周期由 `CalibrationWindow` 管**：`DisplayPatch(ColorPatch)` 遇到 `patch.Nits` 时，按 `ColorPatchBorder.PointToScreen(...)` 算出的物理像素矩形**懒创建**渲染器（`CalibrationWindow.xaml.cs:1809-1832`），把下面的 WPF 色块刷黑（防止边缘漏出旧色污染），再 `PresentNits(nits)`；关窗时 `DisposeHdrWireRenderer()` 释放 swapchain / device / HWND。

**搬迁到 `Gloam.Core` 的障碍**（静态阅读可见）：它依赖 `Gloam.Interop.Dxgi`、UI 基础设施里的 `WindowTheme.ExcludeFromPeek`、以及本地化 `Loc.Get`。搬迁需要先把"从任务栏预览里排除窗口"抽成 Core 可用的原语，否则会形成 Core→UI 的反向引用。

#### 10.2.3 `PatchDisplayWindow`：验证/复测专用的色块窗口

431 行，注释写明它"styled to MATCH the main calibration measurement screen"——因为 Windows 在合成器处应用已安装的 MHC2 profile，**透过这个窗口看到的就已经是校正后的输出**，所以验证必须用与校准相同的暗背景、色块尺寸与偏移、底部进度条。

- **像素级定位**：`SourceInitialized` 里用原生 `SetWindowPos(hwnd, HWND_TOPMOST, b.Left, b.Top, w, h, SWP_NOACTIVATE)` 按 `monitor.MonitorBounds` 摆放；之前先用 `Left/Top = b.Left/b.Top`、`Width/Height = 200` 做"粗定位"，目的是让窗口**先在正确的显示器上创建**，从而拿到正确的 DPI 上下文。文件注释记录了失败模式：把 DXGI 物理像素矩形塞进 WPF 的 DIP `Left/Width`，在混合 DPI 下窗口（进而色块）物理尺寸会变，甚至落到另一台显示器。
- **与校准一致的码值**：`SetColor` 走同一个 `ToPatchByte`（见 §10.2.1）。
- **进度/控制 API**：`SetProgress(current,total,patchName)` 与带 `nextPatchName`/`phase` 的重载（Current/Next 行只在扫描报告 next 时显示）；`EnableSweepControls(cancelRequested)` 显示 Cancel（先弹确认框）与 Mute；`Escape` → `AbortRequested`；定位阶段的 `Enter`/`Space`/双击 → `ContinueRequested`。
- **抗干扰**：`Topmost`、`ExcludeFromPeek`、`MeasurementForegroundGuard.Attach(this, () => IsLoaded && _placement.Visibility != Visible)`、定位期间释放 `MeasurementInputLock`、`OnStateChanged` 里把最小化强制恢复成 `Normal`、鼠标指针 `Cursors.None` **只作用于色块本身**（注释记过一次回归：误加到整窗导致按钮点不到）。
- **`GetPatchPixelRect()`**：给验证阶段的 FP16 wire 渲染器提供覆盖矩形（与 `HdrPatchRenderer` 同一套物理像素约定）。

#### 10.2.4 干扰防护两件套

- `MeasurementForegroundGuard`（118 行）：探针在读数时把测量窗口重新拉回前台。色度计读的是**物理像素**，任何盖在色块上的东西都会被当成色块测掉，而且不会报错——只会安静地污染一个读数，然后流进 profile。`CalibrationWindow` 在 `finally` 里记录 `guard.Recoveries`：发生过几次遮盖恢复，就说明有几次读数可能在遮盖瞬间被采到（日志原话："readings taken at those moments may be…"）。
- `MeasurementInputLock`（151 行）：读书期间屏蔽会画在色块上的 shell 快捷键（Alt+Tab 最严重，任务切换器绘制在普通 topmost 窗口之上，事后重新置顶救不回已经被读走的像素）。`Engage()` / `Release()`；暂停时释放（注释：暂停期间扣着 Alt+Tab 无法辩护），定位结束时释放，关窗 `Closed` 里兜底释放（"a system-wide keyboard hook must never outlive the window that installed it"）。

#### 10.2.5 `ProbePlacementControl`

159 行 code-behind + 112 行 XAML 的共享定位面：目标视觉、拖动/方向键微调、说明文字，以及 `BeginRequested` / `SecondaryRequested` 两个事件。它被 `CalibrationWindow` 的定位页与 `PatchDisplayWindow` 的 `ShowPlacementPrompt` 同时使用，目的写在注释里——让这些流程**不会各自演化出不同的定位体验**。

### 10.3 报告层：`CalibrationReportWindow` 与它的支撑件

#### 10.3.1 它不是一个"显示页"

构造参数已经说明问题：`CalibrationReportWindow(CalibrationProfile profile, CalibrationMetrics? metrics, DisplayCharacterization? characterization, Lut3D? correctionLut, IReadOnlyList<MeasurementResult>? measurements, double? peakWhiteDriftFraction, bool driftCompensationApplied)`。它同时持有一份 profile（可持久化）、一份度量、一份表征、一份 3D LUT、原始测量、以及运行时的漂移观察（**必须在 orchestrator 归一化之前捕获**，否则不确定度的 drift 项会变成 0）。

它有两种模式：

| 模式 | 判定 | 能力 |
|---|---|---|
| live（刚校准完） | `metrics != null \|\| measurements != null` | 可以 Apply（安装 MHC2）、Verify（重测）、HDR refine、白点工具、导出/打印、写快照 |
| historical（从历史报告打开） | `_isHistorical = metrics == null && measurements == null` | 只读：从 `profile.ReportSummary` 恢复数字，图表换成说明文字，所有需要探针或重装 profile 的按钮禁用 |

`SetApplyContext(ApplyContext)` 的入参是一个 18 字段的 record（`Monitor`、`Target`、三通道 LUT、`WhiteLevel`、`OnInstalled` 回调、`Colorimeter`、`HdrMode`、`StateManager`、`SettingsManager`、`PreviousGammaMode`、`PreviousSettings`、`PatchSize`、`PatchOffsetX/Y`、`CaptureSounds`、`MeasurementDefaultProfile`）。它是"报告窗口能执行 apply/verify"的全部前提；没有它时按钮永远不可用。

#### 10.3.2 它实际做的事（按方法簇归类，共 78 个方法声明）

| 簇 | 代表方法 | 说明 |
|---|---|---|
| 装配与呈现 | `PopulateReport`、`PopulateDisplayCharacteristics`、`PopulatePrimariesComparison`、`PopulateRecommendations`、`SetGradeDisplay`、`GetSummaryText`、`ApplyHistoricalPresentation`、`UpdateStatus`、`RefreshMeasurementValidation` | 把度量/表征/评级翻译成 `CalibrationReportViewModel` 的字符串与画刷；`C` 级以下会附带具体建议 |
| 测量不确定度 | `ComputeUncertainty`、`RefreshNativeUncertainty` | 用 `LuminanceNoiseModel.FromMeasurements(_measurements)` + `_peakWhiteDriftFraction` + `UncertaintyBudget.ClassifyInstrument(model, hasCorrection)` 组 `Context`，调用 `CalibrationVerifier.ComputeMetrics(..., out uncertainty)`；异常只记日志（"report continues without it"） |
| 图表 | `RenderCharts()` / `RenderCharts(canvas×4, palette)`、`RenderDetailedCharts`、`OnThemeChanged` | 4 张主图（tone / gamma / RGB balance / gamut）+ 2 张 detailed 图（dE 分布、逐 patch）；主题切换时重画（图是命令式绘制，不是 XAML 绑定） |
| Apply / Verify | `ApplyButton_ClickAsync`、`ApplyAndVerifyAsync`、`RunVerificationAsync`、`ResolveCurrentMonitor`、`ResolveLiveGammaMode`、`EffectiveTarget` | 一次按钮三态：Apply → Disable → Re-enable；安装前跑 `CalibrationInstallPreflight.BuildMessages`，ERROR 阻断、WARN 要用户确认 |
| HDR 专项 | `ValidateHdrRendererAsync`、`RunPqTrackingSweepAsync`、`RunColoredHdrSweepAsync`、`MeasurePqRungsAsync`、`ReanchorWhiteAsync`、`RefineHdrJointAsync`、`CharacterizeHdrAsync`、`MeasureColoredStimuliAsync` | 每条都是一次完整的"拿探针→放色块→测量→装 profile（部分）→写报告"流水线 |
| 白点工具 | `RunVisualWhiteTrimAsync` / `RunVisualWhiteTrimCoreAsync`、`InstallTrimPreviewAsync`、`TrimPreviewName` | 目视白点微调：每次 nudge **重建并重装** profile（A/B 两个固定文件名强制合成器重载），把用户的眼睛结果烘进 profile 而不是留在 GPU ramp |
| 持久化 / 导出 | `PersistReportSummary`、`ExportButton_Click`（`.cube`）、`ExportReportButton_Click`（打印） | 见 §10.3.4；打印失败时解包 `RuntimeWrappedException` 并把内层异常完整记日志 |
| 探针互斥 | `EnterProbeBusy` / `ExitProbeBusy`、`ShowOperationNotice`、`RememberProbePlacement`、`IsVerifyRunning` | 一个手工布尔门；`ShowOperationNotice` 是为了让完成提示**在 topmost 色块窗口关闭之后**才弹（单显示器下否则会被挡住） |

#### 10.3.3 为什么 `CalibrationReportWindow.xaml.cs` 是 157 KB / 2,993 行

先给度量：156,869 字节、2,993 行、约 78 个方法声明、30 个以上私有字段声明。它**不是**"报告页 + 几个按钮"，而是**校准结束后所有需要独占探针、或需要重装 profile 的操作的前端宿主**。体积来源可以逐条指认：

1. **六条各自完整的异步流水线**。`RunVerificationAsync`（Apply 后自动跑）、`RefineHdrJointAsync`、`CharacterizeHdrAsync`、`ReanchorWhiteAsync`、`ValidateHdrRendererAsync`、`RunVisualWhiteTrimCoreAsync`。每条都要自己处理：拿探针（`ProbeOperationScope` 或 `EnterProbeBusy`）、显示命令、进度文案、取消（`_verifyCts`）、异常（解包/模态/日志）、结果写回、按钮复位、完成提示。没有"操作"抽象，只有六份手写实现。
2. **一次 Verify 实际是三次扫描**。`RunVerificationAsync` 先跑标准 14 patch 或 `VerificationPatchSets.Detailed`（39 patch）；HDR 下再跑 PQ tracking sweep（`RunPqTrackingSweepAsync`，按 rung 过滤到 LUT 修正区间）与彩色 HDR sweep（`RunColoredHdrSweepAsync`，R/G/B/C/M/Y 容器参考 + ΔE ITP）。三组读数合并进 `_verifyMeasurements` 持久化，但**汇总文本分成三段**（`VerifyDetailText` / `PqTrackingDetailText` / `ColoredHdrDetailText`），各有可见性开关与 HTML/文本拼接。
3. **live / historical 双模式贯穿每个方法**。`_isHistorical` 决定：图表能不能画、`ComputeUncertainty` 是否早退、`PersistReportSummary` 写不写、Apply/Verify 是否可用。历史模式还要从 `profile.ReportSummary` 反向恢复准确率表、detailed 列表、等级与摘要（`ApplyHistoricalPresentation`）。
4. **状态字段密度**。`_profileApplied`、`_profileEnabled`、`_installedProfileName`、`_installedHdrLuts`、`_installedHdrXyzCorrection`、`_latestVerificationMetrics`、`_detailedPatchResults`、`_reportSavePath`、`_lastReportSnapshotSaved`、`_toneMapping`、`_operationNotice`、`_trimmedTargetWhite`、`_trimBusy`、`_trimPendingNudge`、`_trimNameToggle`、`_acmWarningShown`、`_measurementInstallBlocked`、`_probeBusy`……这些不是"界面开关"，而是**跨操作的业务状态**：Apply 改了 profile，Verify 要读它，Refine 要覆盖它，Disable/Enable 要绕过它。
5. **数值口径的拼装**。不确定度要变成 `"0.94 ± 3.00"` 这种"值 + 灰尾"（`CalibrationReportViewModel` 专门切分，测试已钉）、要进 tooltip（`uncertainty.Describe()`）、还要追加进持久化摘要；verify 之后等级与摘要要**切换到 after 口径**（`Vm.GradeScopeText = "after correction"`）；detailed 结果在标准 re-verify 时必须退役（否则显示与数字不对应）。
6. **图表与打印共用一套画布能力但参数不同**：屏幕上是深色 `OnScreenPalette`，打印是 `CalibrationCharts.ChartPalette.Light` + 离屏 canvas + 打印尺寸；主题切换要重画，窗口 `SizeChanged` 也要重画。

**一句话结论**：157 KB 不是"报告内容多"，而是**一个窗口承担了 6 条独占探针的校准后工作流 + 3 类验证扫描 + 2 种运行模式 + 1 份打印排版 + 1 份快照持久化**；每加一个"后校准能力"就加一条手写流水线，体积因此线性增长，而不是因为视图字段多（视图状态已经在 407 行的 `CalibrationReportViewModel` 里）。

#### 10.3.4 支撑件：ViewModel / 打印 / 图表 / 快照 / 历史 / 声音

| 文件 | 行数 | 职责与关键点 |
|---|---:|---|
| `CalibrationReportViewModel.cs` | 407 | 只放 XAML 绑定的显示状态；`DeltaEBrush` 按阈值给绿/琥珀/红画刷（对 `NaN`/非有限值有兜底，测试钉住）；`AvgDeltaEText` 的"值 + ± 不确定度尾巴"被切成两段渲染 |
| `ReportPrintBuilder.cs` | 528 | 打印专用浅色 `FlowDocument`：白页、近黑字、`#0078D4` 强调色，内容取**同一批 view-model 字符串**；`CreatePrintCanvas` / `RenderPrintCanvas` 把图表按打印尺寸重画成 `ImageSource`（绝不截屏深色 UI）；`DetailedPrintSection` 承载 dE 分布/逐 patch/worst-best/category breakdown；分页交给 `FlowDocument` 的 paginator，窗口只负责设 `PageWidth/PageHeight/PagePadding` 与**单列** `ColumnWidth`（不设会变成报纸分栏） |
| `CalibrationCharts.cs` | 452 | 零依赖的 WPF `Canvas` 绘图：`DrawLineChart`、`DrawGamutDiagram`、`DrawDeltaEHistogram`、`DrawPerPatchDeltaE`；`Series`/`Pads`/`ChartPalette`（Light/Dark）。坐标轴按 canvas 实际尺寸算，所以必须在 `Loaded` / `SizeChanged` 之后调用；非有限点被跳过（测试：`DrawLineChart_IgnoresNonFiniteSeriesPoints` 等 4 条） |
| `ReportSnapshotBuilder.cs` | 118 | 把"这次显示的所有数字"序列化成 `CalibrationReportSummary` 并写盘：native 与 after 的 avg/max/gray/primary、`GradeScopeLabel`、`SummaryText`、detailed patch（截断到 `VerificationPatchSets.DetailedPatchCount = 39`）、直方图、四类 breakdown、三段 HDR 文本、`ToneMappingCharacterization`；随后 `profile.SaveToFile(path)` + 把 native/verification 两组测量用 `MeasurementCsvExporter` 写到同目录 `measurements/` 子目录。**所有后校准操作都调它**，所以"报告里的数"和"历史报告里的数"是同一份来源 |
| `ReportHistoryWindow.cs` | 216 | 历史报告浏览器：列出 `CalibrationProfile.GetReportsDirectory()` 下的 profile JSON，选中后 `new CalibrationReportWindow(row.Profile).Show()` —— 没有 metrics/measurements，于是自动进入只读历史模式；`Selection_ControlsAvailabilityOfReportActions` 测试钉住"没选中时动作不可用" |
| `CalibrationSounds.cs` | 101 | 运行时合成 16-bit/44.1 kHz WAV：采集提示音、完成和弦、失败低音。无内置音频资源，也不依赖 Windows 声音方案（注释：`SystemSounds.*` 在方案未分配声音时是静音的，这正是"完成音没响"的原因）。`CalibrationSounds.Muted` 由色块窗口与校准窗口的静音按钮共享 |
| `CalibrationViewModel.cs` | 222 | `CalibrationWindow` 的显示状态（视图互斥布尔、patch 文案、进度、按钮态、暂停/恢复倒计时）。与报告 VM 同一条原则：**编排留在窗口，状态放 VM** |

### 10.4 其余窗口：各自被谁触发、拿不拿探针

| 窗口 | 文件/行数 | 触发点 | 是否占探针 | 职责要点 |
|---|---|---:|---|---|
| `TrustCheckWindow` | 348 + 76 | 托盘菜单（`TrayViewModel.OpenTrustCheck`，与校准共用"只有一个探针"守卫） | **是** | 6 patch（白/40% 灰/黑/R/G/B）、约 20 秒的"信任检查"：`new ColorimeterService(argyllBin)` + `ProbeOperationScope` 自己开一次会话；`TrustCheck.Compute(results, target, context)` 后用 `TrustCheckHistory.Append(entry)` 落 JSONL；随后 `AnalyzeDrift(Load(devicePath))`，只有同时超过 `combinedU95` 与实用下限（`DeltaEDriftFloor` / `DuvDriftFloor`）才 `_toastService.Show(..., ToastKind.Warning)`；趋势页还会调 `DriftPredictor.Predict(history, DateTime.UtcNow)` 给出"何时会越过重校阈值"；可把历史导出为 CSV（`TrustCheckHistory.BuildCsv`） |
| `DisplayMatchWindow` | 273 | 托盘菜单（`TrayViewModel.OpenMatchDisplays`） | 否（**模型**） | 多显示器匹配：用已存表征喂 `DisplayMatchSolver.Solve`，求各面板都可达、最坏情况感知扰动最小的公共白点，然后把结果写成分通道 gain + 亮度上限；窗口文案明确"only one calibrated display found — calibrate a second"这类前置条件。不测量，所以不与探针守卫冲突 |
| `ProfileManagerWindow` | 321 | setup 的"Manage profiles"按钮 | 否 | 列出本显示器已安装的 Gloam profile，支持激活（回退到旧校正）、停用（保留文件）、删除（需确认；删除是不可撤销的） |
| `CcssBrowserWindow` | 528 + 110 | setup 的"Find correction…"；以及 `SettingsWindow` 的同一入口 | **是**（仅两条生成路径） | DisplayCAL 社区校正库浏览器：按显示名搜索、选中即下载/保存到 corrections 目录。两条自制路径都在本窗口内起一个会话："From spectrometer…" → `SpectralCaptureService`（W/R/G/B 四 patch 光谱，逐 bin 取中位数，白点归一化到 Y=100）；"Correction matrix…" → `MeterOffsetCaptureService`（两台仪器先后测同一组 patch，换探针时 `EndSessionAsync` 提前交还 USB） |
| `WhiteTrimWindow` | 156 | 报告窗口的白点工具（`RunVisualWhiteTrimAsync` → `new WhiteTrimWindow`） | 否（自己） | 可拖动的中灰场 + 方向键微调；**每次 nudge 由报告窗口重建并重装 profile**（`InstallTrimPreviewAsync`，A/B 两个固定文件名强制合成器重载），用户看到的是整屏实时变化 |
| `WhiteToolsDialog` | 148 | 报告窗口的"White tools"按钮 | 否 | 动作选择器，按能力决定哪些动作可点：重锚白（漂移修复）、目视白点微调（同色异谱修复）、验证 HDR renderer |
| `ArgyllDownloadDialog` | 154 + 110 | setup 初始化探针失败/版本过旧时由 `OfferArgyllDownload` 弹出 | 否 | 下载 ArgyllCMS 并显示进度；`DownloadSucceeded` 返回给 VM 触发重新初始化 |
| `DriverInstallDialog` | 121 + 134 | `CalibrationWindow` 捕获 `UsbDriverException` 时（每个会话只提示一次，`_driverInstallAttempted`） | 否 | 引导安装/卸载 ArgyllCMS USB 驱动（`UsbDriverHelper`）；安装失败后再次失败则给出"拔插设备/关闭其它校准软件"的收尾提示 |
| `ReportHistoryWindow` | 216 | setup 的"Past reports"按钮 | 否 | 历史报告列表 + 删除 + 打开（只读历史模式，见 §10.3.4） |
| `ProbePlacementControl` | 159 + 112 | 被 `CalibrationWindow`（定位页）与 `PatchDisplayWindow`（`ShowPlacementPrompt`）复用 | 否 | 共享定位面，见 §10.2.5 |

### 10.5 状态与持久化交接：UI 层到底写了什么

校准 UI 是"写状态"最集中的地方，四类写入必须分清，因为它们的所有权与失败语义完全不同。

#### 10.5.1 显示校正状态：进入/退出 bypass

`CalibrationStateManager`（Core，287 行）在 UI 层的使用点只有三处：

1. `CalibrationWindow` 在测量开始时进入 bypass（并置 `_bypassApplied`），**成功校准后也要**在关闭时 `RestorePreviousState()`。原因写在 `Window_Closing` 注释里：闭环会把候选校正留在 GPU ramp 上，托盘 ramp guard 会持续重申它；必须先恢复用户真实的校正状态，再由托盘在窗口 `Closed` 后按新的 MHC2 组合态重设。
2. `ProbeOperationScope` 在每次验证/复测扫描时 `EnterBypassMode(...)`，`DisposeAsync` 里 `RestorePreviousState()`。这就是"验证测的是 profile 本身，不是 GPU ramp 上的历史残留"。
3. 报告窗口通过 `ApplyContext.StateManager` 间接使用（由 `ProbeOperationScope` 传入）。

#### 10.5.2 "测量时临时禁用 profile"及其崩溃恢复

为了让探针测到**原生面板**，`CalibrationWindow` 在测量前禁用当前生效的校准 profile：

- 自己的 profile：`_disabledProfileForMeasurement` + `_disabledProfileForMeasurementHdrMode`，退出时 `CalibrationProfileInstaller.Reenable(...)` 并 `SettingsManager.SetMhc2Calibration(...)` 回写；
- **外来**（非 Gloam）profile：`_foreignProfilesDisabledForMeasurement` 列表，退出时逐个 `CalibrationProfileInstaller.RestoreDefaultProfile(...)`；
- 崩溃恢复：因为进程死掉后内存清单消失，`PersistForeignRestoreIntent` / `ClearForeignRestoreIntent` / `RecoverStaleForeignRestoreIntent`（`CalibrationWindow.xaml.cs:468-585`）把"禁用了什么"写到磁盘，下一个 `Window_Loaded` 先恢复。`RestoreProfileDisabledForMeasurement()` 是所有这些路径的幂等收口，被取消/失败/关窗/驱动对话框共用。

#### 10.5.3 安装结果回写：`SettingsManager` 与 profile 关联

Apply 成功后，报告窗口通过 `ApplyContext.OnInstalled(profileName, previousDefaultProfile)` 回到 `CalibrationWindow` 注册的回调（`CalibrationWindow.xaml.cs:1764-1783`）：

1. 取 `SettingsManager.GetMonitorProfile(devicePath)?.Mhc2ProfileName`，若与本次不同则 `CalibrationProfileInstaller.Disable(monitor, previous)` —— 注释记录了真实故障："repeated applies stack associations in the color store (the report loop left three in one session)"；
2. `SettingsManager.SetMhc2Calibration(devicePath, profileName, previousDefaultProfile, measuredInHdr, builtForHdr)`，让实时 apply 路径知道"现在有 MHC2 校准"，从而把夜间模式/伽马**组合**在它之上而不是二次应用；
3. 清空 `_disabledProfileForMeasurement`；
4. 报告窗口另外调用 `SettingsManager.RecordDisplacedAdvancedColorProfile(devicePath, result.DisplacedAdvancedColorProfile)`，记下被本次安装挤掉的 Advanced Color 默认 profile（回滚线索）。

#### 10.5.4 报告与测量数据的落盘

| 产物 | 写入者 | 位置/内容 |
|---|---|---|
| 报告快照 JSON | `ReportSnapshotBuilder.Save`（由报告窗口 `PersistReportSummary` 调用，每次 Apply/Verify/Refine 后重写同一路径） | `CalibrationProfile.GetReportsDirectory()`；文件名 `{MonitorName}_{yyyyMMdd_HHmmss}_{ProfileId:N}.json`；内含 `CalibrationReportSummary`（native/after 数字、详细 patch、直方图、breakdown、三段 HDR 文本、tone mapping） |
| 测量 CSV | 同上（`SaveMeasurements`） | 报告同目录 `measurements/` 子目录，`{basename}_native-measurements.csv` 与 `..._verification-measurements.csv`；列见 `MeasurementCsvExporter`（含 `patch_nits`、`reading` 元数据、`is_valid`） |
| 信任检查历史 | `TrustCheckHistory.Append`（TrustCheckWindow 调用） | `reports/trend/{sanitized_device}_{sha256}.jsonl`，append-only，坏行跳过 |
| 窗口位置/尺寸 | `WindowBoundsPersistence.Attach(this, settingsManager, "<key>")` | 键名：`"CalibrationSetup"`、`"CalibrationReport"`（`CalibrationWindow` 的键在构造处）；`CopyBounds` 把 setup 的 bounds 复制给校准窗口，Back/返回时再由 `UiState` 延续运行参数 |

**这条边界的意义**：报告层不持有也不重算模型。它写下去的每个精度数字都来自 `CalibrationVerifier.ComputeMetrics`（穿过已安装 profile 的实测），而"这次校准用了什么参数"来自 `CalibrationProfile`；两者在快照里合并，所以历史报告可复现"当时的结论 + 当时的原始读数"。

### 10.6 UI 层不变量与风险

#### 10.6.1 不变量（改动前必读）

| # | 不变量 | 强制方式（代码位置） | 破坏后的表现 |
|---|---|---|---|
| 1 | **一次只能有一个流程独占探针** | `CalibrationLaunchCoordinator.Open` 扫窗口；`TrayViewModel.OpenTrustCheck` 同一守卫；报告窗口 `IsVerifyRunning` 参与判定 | 两个 spotread 抢 HID → 共享冲突（err 32），或读数被记到错误的面板上 |
| 2 | **任何拿探针的操作都必须经 `ProbeOperationScope`**（或等价的 enter/exit 配对） | 构造事务化：失败走与完成相同的清理路径（`ProbeOperationScope.StartAsync` catch → `DisposeAsync` → throw） | ramp/会话/色块窗口/按钮态泄漏，下一次测量被上一次的残留污染 |
| 3 | **测量期间画面像素不可变** | `MeasurementForegroundGuard`（重新拉前台）+ `MeasurementInputLock`（屏蔽 Alt+Tab 等）+ `Topmost` + 取消最小化 + `ExcludeFromPeek` | **不会报错**：探针把桌面/切换器当色块读进去，污染一个读数并影响 profile |
| 4 | **窗口关闭 = 状态收口** | `CalibrationWindow.Window_Closing` 的固定顺序；`PatchDisplayWindow.Closed` 释放键盘钩子；`ProbeOperationScope.DisposeAsync` | GPU ramp 卡在闭环候选校正、profile 被永久禁用、系统级键盘钩子泄漏 |
| 5 | **"after" 只能是实测** | 报告窗口的 after 数字全部来自 `RunVerificationAsync` 的扫描（穿过已安装 profile）；模型预测只用于诊断文本 | 报告把预测当结果，用户看到"变好了"而屏幕没变 |
| 6 | **历史报告只读** | `_isHistorical` 早退（无图表/无 apply/无 verify），数字来自 `profile.ReportSummary` | 历史页面触发重装 profile 或空指针（无 measurements） |
| 7 | **一次校准只弹一个报告窗口** | `CalibrationWindow._reportOpened` 守卫（第二次点击直接 `Close()`）；注释：每个报告都会自动 apply+verify | 重复安装 profile、重复跑扫描（实测曾一次留下 3 条关联） |
| 8 | **打印是独立浅色排版** | `ReportPrintBuilder.Build` + `ChartPalette.Light` 重画，绝不截屏深色 UI | 打印出黑底/不可读，或图表分辨率随窗口尺寸漂移 |
| 9 | **视图状态在 VM，编排在窗口** | `CalibrationViewModel` / `CalibrationReportViewModel` 注释与用法 | code-behind 直接摸控件，绑定与命令式渲染两套真相 |
| 10 | **同一 patch 在校准与验证渲染相同码值** | `PatchSetGenerator.Snap8Bit`（生成期）+ `PatchDisplayWindow.ToPatchByte`（`Math.Round`） | before/after 比较出现系统性偏移 |

#### 10.6.2 风险与观察

| 风险 | 证据 | 影响/建议 |
|---|---|---|
| `HdrPatchRenderer` 是 Core 职责却住在 UI 工程 | 296 行，依赖 `Gloam.Interop.Dxgi`、`WindowTheme.ExcludeFromPeek`、`Loc` | 已登记为例外（`docs/project-structure.md`）；搬迁需先把"从任务栏预览排除窗口"抽成 Core 原语，否则 Core→UI 反向依赖 |
| 报告窗口是 2,993 行的"操作宿主" | 6 条 probe 流水线 + 3 类扫描 + 2 模式；`_probeBusy` 是手工布尔门 | 新增后校准操作必须记得 `EnterProbeBusy`/`ExitProbeBusy`（无编译期强制）；建议按"操作"抽协作者而非按行数切 |
| 两套 profile 概念在 UI 层交叉 | `CalibrationProfile`（报告/MHC2 元数据，§10.5.3 写入）vs `DisplayCalibrationProfile`（GPU ramp 模式，`DispwinRunner`/`SettingsManager`/`LutGenerator` 使用） | 读代码时极易混淆"哪个 profile 被装了"；报告窗口只碰前者，设置路径只碰后者 |
| 自动 apply+verify 依赖固定延时 | `AutoApplyOnLoadAsync` 里 `await Task.Delay(600)`（等托盘窗口关闭后的 re-apply 落地） | 慢机器上仍是竞态假设；属于"已知时序耦合"而非确定性保证 |
| topmost 色块窗口与模态框的顺序脆弱 | `_operationNotice` + `ShowOperationNotice` 专门为此存在；Verify 出错时先 `probe.DisposeAsync()` 再弹框 | 新增操作若忘记这条，错误对话框会被色块窗口盖住（单显示器） |
| 硬编码英文仍存在 | 审计测试是 `Hardcoded_user_visible_strings_do_not_increase`（基线"不增加"），不是零 | 报告窗口里可见 `"Verification cancelled. Use Re-verify to run it again."` 之类；新文本应走 `Loc` |
| 物理像素定位逻辑重复 | `CalibrationWindow.MoveWindowToPixelRect`、`PatchDisplayWindow` 的 `SetWindowPos`、`HdrPatchRenderer` 的 `CreateWindowEx` 各写一套 | DPI/多屏回归面分散；改动需同时验证三条路径 |
| 未验证项 | 本机无法运行 WPF/HDR/探针 | 多显示器 HDR 下的 `HdrPatchRenderer` 覆盖、Aero Peek 排除的实际效果、打印分页在真实打印机上的表现、OLED QD-OLED 的 wire ladder 行为——均需真机确认 |

### 10.7 文件 → 职责表（全部 37 个文件）

| 文件 | 行数 | 职责 |
|---|---:|---|
| `CalibrationSetupWindow.xaml` | 294 | setup 表单布局（显示器/目标/预设/显示类型/校正文件/预检/开始） |
| `CalibrationSetupWindow.xaml.cs` | 121 | setup 窗口 chrome、子对话框宿主、`DialogResult`、结果属性转发 |
| `CalibrationSetupViewModel.cs` | 1,182 | setup 全部决策：目标可用性、面板类型检测、预设与预计时长、校正文件校验、预检、`CanStart`、探针初始化 |
| `CalibrationWindow.xaml` | 533 | 校准三视图（说明/定位/测量）+ 完成页 + 暂停遮罩 |
| `CalibrationWindow.xaml.cs` | 2,118 | 校准运行宿主：视图切换、全屏/窗口化、patch 拖动/微调、orchestrator 事件 marshal、闭环/HDR 渲染器、关闭恢复契约 |
| `CalibrationViewModel.cs` | 222 | 校准窗口的显示状态与按钮/进度绑定 |
| `CalibrationLaunchCoordinator.cs` | 114 | setup→calibration→back 窗口链；单探针守卫；`ColorimeterService` 所有权转移 |
| `CalibrationSessionCoordinator.cs` | 180 | 一次校准会话：orchestrator 构造/事件转发、闭环装配、HDR wire patch 追加、产物生成（LUT/表征/度量） |
| `VerificationCoordinator.cs` | 70 | 验证扫描的纯粹时序（呈现→延时→测量→捕获），取消不发布 |
| `HdrRefinementCoordinator.cs` | 152 | 关节式 HDR 精修的 UI 侧：矩阵 scale 解析、原子安装、旧 profile 清理、回写 |
| `ProbeOperationScope.cs` | 208 | 探针操作事务：取消源、色块窗口、bypass、仪器会话、忙标志的成对获取/释放 |
| `PatchDisplayWindow.xaml.cs` | 431 | 验证/复测色块窗口：像素级定位、8-bit 码值、进度、Cancel/Mute、定位面、键盘契约 |
| `HdrPatchRenderer.cs` | 296 | FP16 scRGB 真 HDR 色块发射器（Win32 弹窗 + D3D11 flip swapchain），UI 工程的已登记例外 |
| `ProbePlacementControl.xaml` | 112 | 共享定位面的视觉（目标框、说明、按钮） |
| `ProbePlacementControl.xaml.cs` | 159 | 共享定位面行为：拖动/方向键微调、`BeginRequested`/`SecondaryRequested` |
| `MeasurementForegroundGuard.cs` | 118 | 读数期间把测量面拉回前台，统计 `Recoveries` |
| `MeasurementInputLock.cs` | 151 | 读数期间屏蔽会覆盖色块的 shell 快捷键（系统级键盘钩子，必须随窗口释放） |
| `CalibrationReportWindow.xaml` | 635 | 报告布局：摘要/精度表/图表四联/详细验证/白点工具/状态条 |
| `CalibrationReportWindow.xaml.cs` | 2,993 | 后校准操作宿主：装配呈现、不确定度、图表、Apply/Verify、HDR 专项、白点工具、打印/导出、快照、历史模式、探针互斥 |
| `CalibrationReportViewModel.cs` | 407 | 报告窗口全部绑定状态与画刷；不确定性"值+尾巴"切分 |
| `ReportPrintBuilder.cs` | 528 | 打印专用浅色 `FlowDocument` + 离屏图表渲染 + 详细验证章节 |
| `CalibrationCharts.cs` | 452 | 零依赖 Canvas 图表：线图、CIE xy 色域图、dE 直方图、逐 patch dE |
| `ReportSnapshotBuilder.cs` | 118 | 报告快照序列化（`CalibrationReportSummary`）+ native/verification 测量 CSV 落盘 |
| `ReportHistoryWindow.cs` | 216 | 历史报告浏览器：列表、删除、以只读历史模式打开 |
| `TrustCheckWindow.xaml` | 76 | 信任检查窗口布局（显示器选择、状态、趋势图） |
| `TrustCheckWindow.cs` | 348 | 6 patch 快速检查：自建 ColorimeterService 会话、评分、追加趋势、漂移告警（U95 + 下限）、`DriftPredictor` 预测、CSV 导出 |
| `ProfileManagerWindow.cs` | 321 | 本显示器已安装 profile 的激活/停用/删除 |
| `CcssBrowserWindow.xaml` | 110 | 校正库浏览器布局（搜索、列表、自制入口） |
| `CcssBrowserWindow.cs` | 528 | DisplayCAL 校正库搜索/下载；`SpectralCaptureService`（.ccss）与 `MeterOffsetCaptureService`（.ccmx）两条自制路径 |
| `DisplayMatchWindow.cs` | 273 | 多显示器匹配（模型解算 `DisplayMatchSolver`，写 gain/亮度上限，不测量） |
| `WhiteTrimWindow.cs` | 156 | 目视白点微调的拖动中灰场（每次 nudge 由报告窗口重装 profile） |
| `WhiteToolsDialog.cs` | 148 | 白点工具动作选择器（按能力禁用） |
| `ArgyllDownloadDialog.xaml` | 110 | Argyll 下载对话框布局 |
| `ArgyllDownloadDialog.xaml.cs` | 154 | 下载进度与结果（`DownloadSucceeded` 回传） |
| `DriverInstallDialog.xaml` | 134 | USB 驱动安装引导布局 |
| `DriverInstallDialog.xaml.cs` | 121 | 引导安装/卸载 ArgyllCMS USB 驱动 |
| `CalibrationSounds.cs` | 101 | 运行时合成的采集/完成/失败音；共享 `Muted` 开关 |

> 校准 UI 的回归测试主要在 `tests/Gloam.Tests`：`CalibrationSetupPreflightTests`（预检与预设标签一致性）、`VerificationCoordinatorTests`（时序/取消）、`ReportPrintPaginationTests`（打印每页可排版）、`CalibrationChartsTests`（非法数据不画崩）、`CalibrationReportViewModelTests`（画刷与不确定性尾巴）、`ReportHistoryWindowTests`（动作可用性）。它们钉的是**UI 契约**；数值正确性由 §9 的 golden fixtures 钉住。

### 10.8 UI 线程、异步与取消约定

这一节是本 feature 最容易踩坑的部分，因为 UI 必须同时驱动"单线程的 WPF"与"多线程的测量/仪器"。约定如下：

| 约定 | 代码依据 | 为什么必须这样 |
|---|---|---|
| **所有窗口代码在 STA UI 线程** | 各窗口均为 WPF `Window`；`CalibrationWindow` 的 6 个 orchestrator 事件处理器全部 `Dispatcher.Invoke(...)`（整个 feature 共 21 处 `Dispatcher.Invoke/BeginInvoke`） | orchestrator 的事件在异步测量上下文的续体上触发（通常是 threadpool），直接摸控件会抛线程所有权异常 |
| **异步的统一入口与兜底** | `SafeAsync.FireAndForget(task, "<op>", onError?)` 用于 `Loaded`、点击与键盘触发；按钮 handler 内部另有 try/catch 把错误写进状态条 | 全局崩溃对话框会打断整条校准流水线；安装/预检/模态框的异常"属于状态条"（`ApplyButton_ClickAsync` 注释原话） |
| **取消是分层的，不是一刀切** | 校准运行：`CalibrationWindow._cancellationTokenSource`；单次扫描：报告窗口 `_verifyCts`（`PreviewKeyDown` Escape 与 `Closing` 都 `Cancel()`）；探针操作：`ProbeOperationScope._cancellation` | 取消必须能到达"正在 `await` 探针读数"的那一层，否则关窗只是关掉界面、spotread 还在读 |
| **取消后不发布结果** | `VerificationCoordinator` 在最后一次测量返回后再检查一次 token；`RunVerificationAsync` 的 `catch (OperationCanceledException)` 只写状态文案 | 驱动可能在取消后才交回读数（注释："do not publish it as successful verification"） |
| **teardown 先于模态** | Verify 失败时先 `await probe.DisposeAsync()`，再 `ConfirmDialog.Info(...)`；窗口已关闭时只记日志 | 单显示器下 topmost 色块窗口会盖住对话框，用户在"看不见错误"的状态下重试 |
| **探针操作不并行，靠 UI 顺序 + 布尔门** | `_probeBusy` / `IsVerifyRunning`（报告窗口）、`CalibrationLaunchCoordinator` 的窗口扫描、`TrayViewModel.OpenTrustCheck` 的同一守卫 | 机器上只有一个探针；并行 = 共享 HID 冲突（`err 32`）或读数串台 |
| **图表在布局之后画** | `Loaded += (_, _) => RenderCharts(); SizeChanged += (_, _) => RenderCharts();` | `CalibrationCharts` 用 canvas 的 `ActualWidth/Height` 算坐标，布局前尺寸为 0 |
| **声音/提示不阻塞测量** | `CalibrationSounds` 运行时合成 WAV 后播放；`ShowOperationNotice` 在 `await` 返回后才弹 | 采集音必须紧跟读数，不能拖慢 patch 循环；完成提示必须等色块窗口关闭 |

把这八条放在一起看，就能解释为什么"UI 层"在这个仓库里占 14,390 行：它真正承担的是一套**围绕单一硬件资源（探针）的并发编排**，而 WPF 只是它的呈现端。

---

## 11 WPF UI（一）：组合根、托盘、Dashboard、基础设施与主题

> **覆盖范围**：`src/Gloam/Shell`（进程入口、`App`、`MainWindow`）、`src/Gloam/Features/Tray`、`src/Gloam/Features/Dashboard`、`src/Gloam/Infrastructure/{Dialogs,Platform,Theming}`、`src/Gloam/Controls`、`src/Gloam/Behaviors`、`src/Gloam/Themes`、`src/Gloam/Fonts`、`src/Gloam/Resources/i18n`（只记存在与角色）。
>
> **阅读方式**：纯静态阅读，未启动程序、未执行 `dotnet build/test`、未使用 git。凡涉及运行时行为（焦点、动画、DPI、Win32 消息实际到达顺序）的结论都标注「静态可证」或「未验证」。行号引用格式为 `path:line`。
>
> **边界**：Calibration 系列窗口、GameLab/NightMode/Settings 三个功能窗口的内部实现、`Gloam.Localization` / `Gloam.Localization.Wpf` 两个项目的解析算法，均由其他章节负责；本章只描述它们在 UI 外壳中的**接入点**与**宿主关系**。
>
> **一句话定位**：Gloam 的 UI 是一棵「隐藏 WPF 窗口作消息泵 + 托盘菜单 + 一个 Dashboard 主窗 + 若干代码构建的临时对话框/浮层」的树。DI 组合根在 `App.ConfigureServices`，所有命令由 ViewModel 暴露为 `ICommand`，窗口代码隐藏层只保留无法 MVVM 化的视图关注点。

### 11.1 组合根与进程启动

#### 11.1.1 Program.cs：Velopack 钩子、单实例、`--smoke`

`src/Gloam/Shell/Program.cs` 是真正的 `Main`，由 `src/Gloam/Gloam.csproj` 的 `<StartupObject>Gloam.Shell.Program</StartupObject>` 指定，而不是 `App.xaml` 生成的入口。原因写在文件头注释里：Velopack 的 install/update/uninstall 钩子必须在 WPF 起来**之前**处理。

启动顺序（`Program.cs:23-61`）：

1. **Velopack 钩子**（`Program.cs:31-35`）：`VelopackApp.Build().OnAfterInstallFastCallback(...).OnBeforeUninstallFastCallback(...).OnFirstRun(...).Run()`。正常启动时是 no-op；`OnAfterInstallFastCallback` 与 `OnFirstRun` 都调用 `ApplyFreshInstallStartupDefault()`（新装默认写入 HKCU Run），`OnBeforeUninstallFastCallback` 调用 `RemoveStartupRegistration()`。整段包在 try/catch 中，因为此时 `Log` 可能尚未 `Initialize`，钩子失败不能阻断启动（`Program.cs:37-42`）。
2. **`--smoke` 启动冒烟**（`Program.cs:44-45`，实现 `Program.cs:91-136`）：要求同时提供 `--data-dir <dir>`；用 `AppPaths.UseDataDirectoriesForCurrentProcess(dataDir, dataDir/Roaming)` 重定向数据目录，`Log.Initialize(<dataDir>/app.log)`，`ResourceExtractor.ExtractIcmProfiles()`，再 `App.ConfigureServices()` 并 resolve `SettingsManager`、`NightModeService`、`ProfileManager`、`ILocalizer`，检查 `localizer.MissingKeys` 非 null、日志文件确实创建。成功返回 0，失败返回 2。这把「服务装配 + 本地化 catalog 解析 + ICM 落地」纳入可脚本验证的启动契约（`Program.cs:114-124` 注释）。
3. **单实例互斥体**（`Program.cs:51-56`）：`new Mutex(initiallyOwned: true, name: "Local\\Gloam-SingleInstance", out createdNew)`；`createdNew == false` 时记日志并 `return 0`。互斥体保存在 `static Mutex? _singleInstanceMutex` 字段，防止 GC 提前回收导致句柄释放。`Local\` 前缀把作用域限定在当前登录会话，符合「每用户托盘应用」的语义。注释给出的三条冲突理由是：两套托盘会争抢 GPU gamma ramp、互相覆盖 `settings.json`、Velopack 更新重启与「开机启动」竞态。
4. **创建并运行 `App`**（`Program.cs:58-60`）：`new App()` → `InitializeComponent()` → `app.Run()`。

#### 11.1.2 App.xaml / App.xaml.cs：DI 注册表与启动顺序

`src/Gloam/Shell/App.xaml` 只有 32 行，但定下三条全局约束：

- `ShutdownMode="OnExplicitShutdown"`（`App.xaml:4`）：窗口全关不等于进程退出，退出只由托盘「Exit」或显式 `Application.Current.Shutdown()` 触发——这也是隐藏 `MainWindow` 可行的前提。
- `DispatcherUnhandledException="App_DispatcherUnhandledException"`（`App.xaml:5`）：全局 UI 线程异常兜底。
- `Application.Resources` 先 merge `pack://application:,,,/Gloam;component/Themes/Tokens.xaml`（`App.xaml:10`），再定义托盘菜单专用的 `LightMenu*` / `DarkMenu*` / 动态 `Menu*` 画刷（`App.xaml:14-29`）。

**DI 注册表**（`App.xaml.cs:23-63`，全部 Singleton）：

| 注册项 | 构造依赖 / 备注 | 角色 |
| --- | --- | --- |
| `MonitorManager` | — | 显示器枚举与 `MonitorInfo` 快照 |
| `SettingsManager` | — | `settings.json`、每显示器 profile、窗口 bounds、UI 展开状态、语言/主题持久化 |
| `AdvancedColorProfileMigrationService` | — | 旧 HDR profile 表征修复，启动时主动 `Run()` |
| `DispwinRunner` | 自检 dispwin.exe | 原生 gamma 失败时的 ArgyllCMS 回退 |
| `NightModeService` | 由 `SettingsManager.NightMode` 构造 | 夜间暖色调度、`BlendChanged` 事件源 |
| `ProfileManager` | 基目录下 `srgb_to_gamma2p2_100_mhc2.icm` | MHC2/ICM profile 安装 |
| `GammaApplyService` | `DispwinRunner` + `SettingsManager` + `NightModeService` | 应用管线唯一入口 |
| `MelanopicMonitorService` | `GammaApplyService` | 监听 apply 快照，供 Dashboard 昼夜卡片 |
| `AppDetectionService` | — | 前台窗口 hook，Gameplay Lock 输入 |
| `UpdateService` | — | Velopack 封装（见 11.4） |
| `IToastService` → `ToastService` | — | 主题化浮层通知 |
| `TrayViewModel` | 由容器注册，但**运行时实例不从这里取** | 托盘 VM；真正的实例由 `MainWindow` 用 `ActivatorUtilities.CreateInstance` 创建（`App.xaml.cs:47-52` 明确标注） |
| `ILocalizer` | `BuildLocalizer()` | 进程级本地化器 |
| `LocalizedStrings` | `ILocalizer` | XAML `{loc:Tr}` 的 `Default` 宿主 |
| `LanguageService` | `ILocalizer` + 写回 `SettingsManager.SetLanguage` | 运行时切语言 |

`BuildLocalizer()`（`App.xaml.cs:69-83`）用 `CompositeCatalogSource` 叠两层：`EmbeddedCatalogSource(typeof(App).Assembly, "Gloam.Resources.i18n.")` 为基线，`DirectoryCatalogSource(%LOCALAPPDATA%\Gloam\i18n)` 作为可热修的外部覆盖层；缺目录或文件损坏都只跳过、不致命。

**`OnStartup` 的顺序是负载相关（load-bearing）的**（`App.xaml.cs:85-170`），逐步理由如下：

1. `AppPaths.MigrateLegacyData()`（`:90`）：在 `Log.Initialize` 之前跑。旧品牌目录 `%LocalAppData%\HDRGammaController`（含 roaming profile 目录）要迁移成 Gloam，若先初始化日志会先创建新目录，导致目录移动失败。
2. `Log.Initialize()`（`:94`）：WinExe 的控制台输出会被丢弃，文件 sink 必须最先可用。
3. `ResourceExtractor.ExtractIcmProfiles()`（`:106`）：在 apply 之前把 6 个 ICM 模板落到可写目录（见 11.4）。
4. 订阅系统事件（`:114-124`）：`SystemEvents.UserPreferenceChanged`、`SystemEvents.TimeChanged`、`BrutalistTheme.Changed`，并在 `Exit` 里对称退订。
5. `ConfigureServices()` 并发布 `App.Services`（`:127-128`）。
6. 主题初始化（`:132-135`）：取 `SettingsManager`，设 `BrutalistTheme.Persist = dark => settings.SetDarkTheme(dark)`，再 `BrutalistTheme.Initialize(settings.DarkTheme ?? ThemeDetector.IsDarkMode())`，随后 `ApplyTrayMenuTheme()`。用户手动选择优先于 OS 设置。
7. 本地化必须在**第一个 XAML 被解析之前**发布（`:137-142`）：`LocalizedStrings.Default = ...` 后 `LanguageService.ApplyStartup(settings.Language)`；因为 `{loc:Tr}` 绑定通过 `LocalizedStrings.Default` 解析，且 culture font policy 会在窗口加载时改写 `BodyFont`/`DisplayFont` 两个 token。
8. 启动项自愈（`:144-149`）：`StartupManager.EnableByDefaultForFreshInstall(settings, updateService.IsInstalled)` 只对已安装版本、首次运行生效；`StartupManager.RepairIfStale(updateService.IsInstalled)` 修掉指向旧二进制（如 pre-Velopack 安装）的 HKCU Run 值。两步都带 `isInstalled` 门，dev/portable 运行不会偷走注册。
9. `AdvancedColorProfileMigrationService.Run()`（`:155`）：必须在托盘/apply 路径之前，让 settings 与合成器校正从第一帧起一致。
10. `new MainWindow()`（`:158`）：构造函数内部完成托盘 VM 与热键绑定（见 11.1.3）。
11. `WarnIfLegacyInstallPresent(settings)`（`:161`，实现 `:182-210`）：若 `C:\Program Files\HDR-Gamma-Adjust\Gloam.exe` 仍存在，用 toast 警告一次（`settings.LegacyInstallWarningShown` 记忆）。不尝试删除——那需要提权。
12. 任一步抛出：`Log.Error("CRITICAL STARTUP ERROR: ...")` 后 `Shutdown(-1)`（`:163-169`）。

**启动之后的两条系统事件路径**：

- 时钟/时区变化（`OnTimeChanged`，`App.xaml.cs:230-246`）：`SystemEvents` 在广播线程触发，必须先 `Dispatcher.BeginInvoke` 回 UI 线程；然后 `nightMode.UpdateSettings(settings.NightMode)` 强制按新的 "now" 重排夜间模式定时器。若有效 kelvin 变化，`BlendChanged` → `TrayViewModel.ApplyAll`，与 resume 路径一致。
- OS 明暗切换（`OnUserPreferenceChanged` → `RefreshThemeFromSystem`，`App.xaml.cs:248-286`）：`UserPreferenceCategory.General/Color` 时重估；`DarkTheme == null`（跟随系统）且 OS 值真的变了才 `BrutalistTheme.Initialize(osDark)`；有手动覆盖时只把托盘菜单画刷重新钉到当前 token。`MainWindow` 的 `WM_SETTINGCHANGE` 也调用同一方法，因为 `SystemEvents.UserPreferenceChanged` 对 `ImmersiveColorSet` 经常不触发（`:258-265` 注释）。

**托盘菜单画刷**（`ApplyTrayMenuTheme`，`App.xaml.cs:288-296`）把 `MenuBackground/MenuForeground/MenuBorder/MenuHighlight` 直接指向 `ThemeBg/ThemeText/ThemeBorder/ThemeHover`——`App.Resources` 里这 4 个 key 是动态入口，ContextMenu 模板通过 `DynamicResource` 消费。

**崩溃对话框 once-per-session**（`App_DispatcherUnhandledException`，`App.xaml.cs:300-335`）：`_crashDialogShown` 保证每会话最多弹一次，避免「弹窗→异常→再弹窗」的死循环；首次弹 `CrashDialog.Show(owner: MainWindow, ...)`，用户选 Exit 时让异常继续传播（`e.Handled = exit != true`），选 Continue 或对话框自身失败则吞掉并记日志。`OnExit`（`:212-219`）只做 `_serviceProvider?.Dispose()`，容器拥有的夜间模式定时器、ramp guard、前台 hook 在这里释放；`TrayViewModel.Dispose` 的重复调用由各服务自己的幂等 `Dispose` 兜住。

#### 11.1.3 MainWindow：隐藏的消息泵

`src/Gloam/Shell/MainWindow.xaml` 是一个 `Height="0" Width="0"`、`WindowStyle="None"`、`ShowInTaskbar="False"`、`Visibility="Hidden"` 的窗口（`MainWindow.xaml:1-9`）。它唯一的内容是 `tb:TaskbarIcon x:Name="MyNotifyIcon"`（Hardcodet.NotifyIcon.Wpf，`MenuActivation="RightClick"`，`IconSource="/app.ico"`），其 `ContextMenu` 绑定 `TrayItems`（`MainWindow.xaml:116-130`）。

同一文件里定义了托盘菜单的完整模板体系：

- `ThemedContextMenu`：方角、无图标列、`ThemeAccent` 高亮字的 Brutalist 菜单容器（`:13-32`）。
- `SimpleMenuItemTemplate`：无子菜单项；`IsHighlighted` 时底色变 `ThemeAccent`、文字变 `ThemeOnAccent`（`:35-49`）。
- `SubMenuItemTemplate`：带右侧三角和 `PART_Popup` 的子菜单项（`:52-89`）。
- `ThemedMenuItem` / `ThemedSubMenuItem`：把 `Header`、`Command`、`StaysOpenOnClick`、`SubItems` 绑定到数据项（`:91-112`）。`StaysOpenOnClick` 让 gamma 模式切换（MonitorViewModel 的子项）不必每次重开菜单。

`MainWindow.xaml.cs` 的构造顺序（`MainWindow.xaml.cs:19-71`）：

1. `InitializeComponent()`，并把 `MyNotifyIcon.Icon` 从 `Environment.ProcessPath` 提取的关联图标设置上去（失败只记日志）。
2. **提前强制创建 HWND**：`new WindowInteropHelper(this).EnsureHandle()`（`:33-34`）。因为 `HotkeyManager` 需要真实句柄注册热键，而窗口永不显示，不能等 `Loaded`。
3. `_hotkeyManager = new HotkeyManager(helper.Handle)`（`:38`）。
4. **`ActivatorUtilities.CreateInstance<TrayViewModel>(App.Services, _hotkeyManager)`**（`:44`）：这是组合根里唯一的「例外」。`HotkeyManager` 绑定窗口句柄、不能进容器；`TrayViewModel` 构造函数的 `hotkeyManager` 是可选参数，若直接从容器 resolve 会得到一个没有热键的实例（`App.xaml.cs:47-52` 有同样说明）。
5. 把 `TrayViewModel.NotificationRequested` 桥到 `IToastService`，并 `MyNotifyIcon.DataContext = _trayViewModel`（`:49-58`）。
6. 订阅 `Application.Current.Exit += Cleanup`（`:65-66`）：窗口从不显示，`OnClosed` 不是可靠拆除时机；`Exit` 才是托盘「Exit」命令触发的路径。`OnClosed` 也调用 `Cleanup`，靠 `_cleanedUp` 幂等（`:110-128`）。
7. `HwndSource.FromHwnd(helper.Handle)!.AddHook(WndProc)`（`:69`）。

`WndProc` 处理三条消息（`MainWindow.xaml.cs:73-108`）：

| 消息 | 常量 | 动作 |
| --- | --- | --- |
| `WM_DISPLAYCHANGE` | `0x007E` | `TrayViewModel.HandleDisplayChange()` |
| `WM_POWERBROADCAST` + `PBT_APMRESUMEAUTOMATIC` | `0x0218` / `0x0012` | `TrayViewModel.HandleResume()` |
| `WM_SETTINGCHANGE` | `0x001A` | 仅当 `lParam` 解出的宽字符串等于 `"ImmersiveColorSet"` 时调用 `(Application.Current as App)?.RefreshThemeFromSystem()` |

`Cleanup()`（`:122-128`）先 `_trayViewModel?.Dispose()`（停夜间/ramp guard 定时器、解前台 hook、顺带应用 pending update），再 `_hotkeyManager?.Dispose()`（注销全部热键）。

### 11.2 托盘

#### 11.2.1 TrayViewModel：托盘的全部行为

`src/Gloam/Features/Tray/TrayViewModel.cs` 共 1025 行 / 46KB，是 UI 外壳里职责最杂的类：它同时是托盘菜单数据源、热键分发器、更新协调器、显示器状态缓存和「panic 恢复」入口。构造函数有 13 个参数（`TrayViewModel.cs:71-84`），其中 `toastService`、`melanopicService`、`hotkeyManager`、`profileModeService`、`localizer` 是可选的——注释说明这是为单元测试保留的无容器构造路径（`:86-88`）。

**命令表**（10 个 `ICommand` 属性，全部在构造函数内用 `RelayCommand` 实例化，`TrayViewModel.cs:58-67` / `:151-160`）：

| 命令 | 实现 | 行为 |
| --- | --- | --- |
| `ExitCommand` | `Application.Current.Shutdown()` | 走 `OnExplicitShutdown` 的显式退出；`Cleanup` 在 `Application.Exit` 上跑 |
| `RefreshCommand` | `RefreshMonitors` | 重建 `TrayItems` |
| `StartupCommand` | `ToggleStartup` | 翻转 HKCU Run 并原地改 checkmark（不重建菜单，`:807-813`） |
| `DashboardCommand` | `OpenDashboard` | 复用已存在的 `DashboardWindow`（`:824-849`） |
| `CalibrateCommand` | `CalibrationLaunchCoordinator.Open()` | 校准流程入口，含「单探针独占」守卫 |
| `TrustCheckCommand` | `OpenTrustCheck` | 复用/激活已开的 `TrustCheckWindow`，与校准互斥（`:241-260`） |
| `MatchDisplaysCommand` | `new DisplayMatchWindow(...)` | 多显示器匹配 |
| `ExportDiagnosticsCommand` | `ExportDiagnostics` | `ConfirmDialog` 问是否含报告 → `DiagnosticsBundle().Create(...)` → toast 显示路径（`:889-927`） |
| `GameLabCommand` | `ToggleGameLabWindow` | 已开则按「有焦点就关、无焦点就激活」处理，避免误毁编辑（`:851-884`） |
| `ToggleGamerModeCommand` | `ToggleGamerMode` | `GamerModeCoordinator.TrySetEnabled`，失败弹 Error toast（`:687-702`） |

**`TrayItems` 的装配顺序**（`RefreshMonitors`，`TrayViewModel.cs:953-1023`）是理解托盘菜单的唯一权威来源：

1. `ActionViewModel(TrayToolTipText, null, staysOpenOnClick: true)` —— 顶部版本/提示行。
2. 若枚举不到显示器：一条 `tray.noMonitors`（点击即 `Refresh`）；否则依次加入 `tray.openDashboard`、`tray.calibrate`、`tray.trustCheck`、`tray.matchDisplays`、`tray.gameLab`、`tray.gamerMode.pause/resume`（可点击但菜单不关），随后分隔线 `───────────`。
3. 每个 `MonitorInfo` 变一个 `MonitorViewModel`（`:984-1014`）；先按 `SettingsManager.GetProfileForMonitor(devicePath)` 恢复持久化的 gamma，没有则回退到内存缓存 `_savedConfigs`，然后写回缓存。
4. 底部固定 4 项：启动开关、`tray.exportDiagnostics`、`tray.refresh`、`tray.exit`。

菜单是**每次刷新整体重建**的（`TrayItems.Clear()`），除了两处刻意原地修改：语言切换由 `_loc.CultureChanged → OnUiThread(RefreshMonitors)` 触发整树重建（`:680-681`），而启动/GameMode 的标签只在原 item 的 `Header` 上改（`_startupItem` / `_gameModeItem`，`:801-813`），因为重建会把菜单项从仍打开的下拉菜单底下抽走。

**夜间模式与 Windows Night Light**：`NightModeService.BlendChanged` 在任意线程触发，经 `OnUiThread` 切回 UI 线程再 `ApplyAll()`（`:140-145`）；`SettingsManager.NightModeChanged` 直接喂回 `nightMode.UpdateSettings`（`:146`）。Win+Shift+N 的切换**不持久化**，只翻转 `GammaApplyService.NightModeManuallyDisabled`，避免「夜间模式被永久关掉」的脚枪（`:509-520` 注释）。`ApplyAll()` 每次都会先跑 `EnsureWindowsNightLightDisabled()`（`:741-768`）：读注册表检测 Windows Night Light，60 秒节流，若开启就关掉并只 toast 一次；`settings.AllowWindowsNightLight == true` 是显式退出口。

**更新流程**（`CheckForUpdatesAsync`，`TrayViewModel.cs:274-346`；更新服务细节见 11.4）：

- `_updateCheckTimer` 每 `UpdateService.SuccessfulCheckInterval`（15 分钟）触发一次，`SafeAsync.FireAndForget` 包裹（`:179-184`）。
- 非 Velopack 安装（F5 / portable）直接返回；先 `TrySchedulePendingUpdateOnExit()` 把上次下载但没应用的包重新排到退出时应用，再查 feed；若 feed 上有比 pending 更新的版本，继续下载覆盖。
- 下载是**完全静默**的，没有确认框（`:324-325` 注释）；下载完成后 `ApplyUpdatesOnExit(info)` 排队，并 `NotifyUpdateReady` 提供「Restart now」/「Exit now」两种动作 toast（`:374-410`）。
- 通知有版本级去重（`ShouldNotifyUpdateAvailable/Ready/Updated` + `Mark*`），连续失败 3 次且距上次失败通知满 24 小时才提示「会继续重试」（`:412-421`）。

**热键分发**（`RegisterHotkeys` / `OnHotkeyPressed`，`TrayViewModel.cs:473-542`）：

| 组合键 | id 变量 | 动作 |
| --- | --- | --- |
| Win+Shift+F1 | `_hotkeyActions[id]` | 对光标所在显示器应用 Gamma 2.2 |
| Win+Shift+F2 | `_hotkeyActions[id]` | 应用 Gamma 2.4 |
| Win+Shift+F3 | `_hotkeyActions[id]` | 应用 Windows Default |
| Win+Shift+F4 | `_panicId` | `PanicAll()` |
| Win+Shift+N | `_nightModeToggleId` | 翻转 `NightModeManuallyDisabled` + 重 apply + toast |
| Win+Shift+G | `_gamerControlsId` | `ToggleGameLabWindow()`；注册失败只记日志（`:496-498`） |

F1–F3 的「目标显示器」由 `GetFocusedMonitor()`（`:544-593`）决定：优先 `GetCursorPos` + `MonitorFromPoint`，退而求其次用前台窗口；匹配时先比 `HMONITOR`，失配再按 `MonitorBounds` 四边比（HDR 模式切换后 `HMONITOR` 会失效，注释 `:562-565`）。匹配不到时弹 warning toast，而非静默失败（`:536-540`）。

**显示器/唤醒事件去抖**：`HandleDisplayChange`（1500ms）与 `HandleResume`（3000ms）都走 `HandleDisplayEventAsync`（`:601-636`）。实现是「自增序号 + `Task.Delay(settle)` + 序号过期即放弃」：Windows 在 HDR 切换时会连发 `WM_DISPLAYCHANGE`，不去抖会叠加多次 apply 造成闪烁（`:595-599`）。settle 之后的固定管线顺序为 `InvalidateAppliedState` → `RefreshMonitors` → `AdvancedColorProfileMigrationService.ReconcileModeChanges` → `AppDetectionService.Refresh` → `NightModeService.UpdateSettings` → `ApplyAll`；注释逐条说明了为什么顺序不能换。

**PanicAll（Win+Shift+F4 安全阀）**（`:770-799`）：取消 pending foreground 策略、强制 `NightModeManuallyDisabled = true`、清空 blocked monitors 与 gamer session、停用或紧急挂起 `GamerModeCoordinator`、`ApplyService.ClearAll(所有显示器)`、关闭所有 `GameLabWindow`，最后用 warning toast 确认。这里刻意不再用 `MessageBox`——原实现从热键 hook 线程弹窗，可能出现在别的窗口后面或错误的显示器上（`:792-794`）。

**其他职责**：`CheckTrustCheckReminder`（24h 定时器 + `TrustCheckHistory`，`:199-239`）、`ExportDiagnostics`（`:889-927`）、显示器 profile 持久化 `OnMonitorProfileChanged`（`:815-822`）。`Dispose`（`:929-951`）先补一次 `ApplyUpdatesOnExit`，再停 timer、退订事件、按依赖顺序 dispose coordinator / apply service / night mode / app detection，最后 `GC.SuppressFinalize`。

#### 11.2.2 MonitorViewModel 与 ActionViewModel

`ActionViewModel`（`src/Gloam/Features/Tray/ActionViewModel.cs`，41 行）是托盘菜单的通用数据项：`Header`、`Command`、`IsSeparator`、`StaysOpenOnClick`、`SubItems`（普通项恒为 null）。两个构造函数分别表示「真实菜单项」与「分隔线」（`ActionViewModel.cs:24-40`）。它靠 `ObservableObject.SetProperty` 支持原地改 `Header`。

`MonitorViewModel`（`src/Gloam/Features/Tray/MonitorViewModel.cs`，152 行）是每个显示器的子菜单：

- `Header => "{index}: {FriendlyName} ({HDR|SDR})"`（`:39`）。
- `Command => null`（父项无动作，只有 `SubItems`）。
- HDR 激活时子项为 Gamma 2.2 / Gamma 2.4 / Windows Default（带 `✓ ` 前缀，`GammaLabel`，`:61-62`）+ 分隔线 + Settings；SDR 时只显示 `tray.sdr.display` 信息 + Settings（`:64-94`）。
- 三个 gamma 子项都用 `RelayCommand` 并 `staysOpenOnClick: true`，方便 A/B 对比；应用成功后只调 `UpdateCheckmarks()` 原地改标签，**不重建 `SubItems`**，因为重建会把菜单项从仍打开的菜单里抽走（`:96-106`）。
- 实际应用通过父级回调 `OnApplyWithCalibration` 委托给 `TrayViewModel.RequestApply`，确保夜间模式被尊重（`:128-150`）；失败时用 `ConfirmDialog.Info`，owner 取「活动可见窗口，否则 `MainWindow`」。
- `OpenSettings` 构造 `SettingsWindow(monitor, allMonitors, settings, callback)` 并 `ShowDialog`（`:108-126`）。

#### 11.2.3 HotkeyManager

`src/Gloam/Features/Tray/HotkeyManager.cs`（83 行）是 `RegisterHotKey` 的薄封装：`Register(Key, ModifierKeys)` 自增 id，把 WPF `ModifierKeys` 映射为 Win32 的 `MOD_ALT/CONTROL/SHIFT/WIN` 位，注册失败返回 0；`HotkeyPressed` 事件在 `WM_HOTKEY (0x0312)` 的 hook 里按 id 派发（`:59-69`）；`Dispose` 注销全部 id 并 `RemoveHook`。它用 `WeakReference<HwndSource>` 持有 hook 宿主，注释明确「借用，不 Dispose」——`HwndSource` 属于 `MainWindow`（`:12-14`）。

#### 11.2.4 ToastService / ToastViewModel / ToastWindow

三件套把「Core 想通知用户」与「WPF 怎么画」解耦：

- `IToastService`（`src/Gloam/Features/Tray/ToastService.cs:15-25`）：`Show(title, message, kind)`、带动作按钮的重载、`ShowAsync`。接口注释明确 Core 通过 `TrayViewModel.NotificationRequested` 表达意图，由 app 层桥接，Core 不依赖 WPF。
- `ToastService`（`:33-153`）是单例且**只保留一个 `_current` 窗口**：新 toast 会先请旧的 `RequestDismiss()`，再替换，避免夜间淡入 tick 之类的密集事件刷屏。`ComputeDuration`（`:125-131`）按 `1.5 + 字符数*0.045` 秒估算阅读时间，夹在 2.5–12 秒；带动作的 toast 不短于 8 秒。`Show` 一律 `Dispatcher.BeginInvoke` 回 UI 线程，失败只记日志，绝不向调用方抛。
- `ToastViewModel`（`src/Gloam/Features/Tray/ToastViewModel.cs`，99 行）只有展示状态：`Title/Message/Kind/ActionLabel/HasAction/ActionCommand`，`DismissRequested` 事件 + `InvokeActionCommand`（先执行 action 再 dismiss，`:88-97`）。`ToastKind` 四档 Info/Success/Warning/Error 决定视图中 4px accent stripe 的颜色。
- `ToastWindow`（`src/Gloam/Features/Tray/ToastWindow.xaml` + `.xaml.cs`）：`WindowStyle=None`、`AllowsTransparency`、`Topmost`、`ShowActivated=False`、`SizeToContent=WidthAndHeight`、`MaxWidth=380`；XAML 用 `Overlays.xaml` 的 `OverlayCard/OverlayTitle/OverlayMessage` 与 `Brutalist.xaml` 的 `BrutBtnSecondary`，accent stripe 的颜色通过 `DataTrigger` 绑定 `Kind`（`ToastWindow.xaml:55-72`），注释特别强调默认值必须写在 `Style` 里而不是 inline，否则 trigger 永远不生效。代码隐藏只负责视图生命周期：`ContentRendered` 后定位到光标所在显示器工作区右下角（物理像素→DIP 用 `CompositionTarget.TransformToDevice` 换算，`ToastWindow.xaml.cs:89-135`）、自动关闭定时器、150ms 淡出后 `Close()`。

### 11.3 Dashboard

Dashboard 是 Gloam 唯一常驻的可见窗口，也是本章体量最大的部分：`DashboardViewModel.cs` 56KB / 1294 行，`DashboardWindow.xaml` 95KB / 1089 行，`DashboardWindow.xaml.cs` 20KB / 414 行。它的结构是「顶部标题栏 + 显示器卡片区（`ItemsControl`）+ 三个可折叠区块（Game Lab / Night Mode / Circadian）」。

#### 11.3.1 DashboardViewModel：分区与命令

**19 个命令**（`DashboardViewModel.cs:89-107` 声明，`:261-282` 实例化）：

| 分组 | 命令 |
| --- | --- |
| 基础 | `RefreshCommand`（`Refresh()`）、`ConfigureMonitorCommand`（`RelayCommand<DashboardItem>` → 触发 `ConfigureRequested`，由窗口打开 Settings）、`CycleNightModeCommand`（`NightModeModeIndex = (index+1)%3`） |
| 夜间暂停 | `Pause1hCommand`、`Pause4hCommand`、`PauseUntilMorningCommand`（各自 `NightModeService.PauseUntil(...)`） |
| 自动禁用应用 | `AddExcludedAppCommand`、`RemoveExcludedAppCommand`、`SaveExcludedAppsCommand` |
| Game Lab | `AddGamerProfileCommand`、`RemoveGamerProfileCommand`、`SaveGamerProfilesCommand`、`ToggleGamerModeCommand`、`ScanForGamesCommand`（`AsyncRelayCommand`）、`AddDiscoveredGamesCommand`、`ClearGamerProfilesCommand`、`ToggleShowAllGamerProfilesCommand`、`DismissGameDiscoveryCommand`、`ApplyGamerLibraryPresetCommand` |

**可折叠分区**由 `SettingsManager.GetUiSectionExpanded(key, default)` 持久化展开状态，key 分别是 `Dashboard.GameLab`、`Dashboard.NightModeControls`、`Dashboard.Circadian`（`:183-207`），另外还有 `IsCircadianAdvancedExpanded`、`IsAddRunningGameExpanded`、`IsGameProfileAdvancedExpanded` 控制二级折叠。`SetSectionExpanded`（`:1124-1129`）统一写回。

**数据集合**：`Items` 是 `ObservableCollection<object>`（`:86`），元素只有两种——每个显示器一个 `DashboardItem`，外加一个长生命周期的 `AppExclusionItem`。`GamerMode` 是独立的 `GamerModeItem`（42KB，GameLab 章节负责，本章只记宿主关系）。`AvailableNightModeAlgorithms` 是计算属性，直接返回 `NightModeAlgorithmOption.DefaultOptions`（Perceptual / UltraNight / AccurateCIE1931 / Standard，`:109-112`），注释说明不能缓存，因为选项标签是本地化文本。

**刷新管线** `Refresh(bool reEnumerate = true)`（`:528-604`）是 Dashboard 的主循环：

1. 若当前不是自己正在保存夜间设置（`_savingNightMode`），比较 `SettingsManager.NightMode` 与编辑器持有的克隆；不同则替换 `_editingNightMode` 并 `EditingNightModeReplaced?.Invoke()`，让窗口用新实例重新 `ScheduleEditor.Initialize`（`:531-546`）。这是「别把旧克隆的编辑当成新快照保存掉」的关键。
2. `OnNightModeModeChanged()` + `RefreshNightRenderingBindings()` 重算渲染相关绑定。
3. `reEnumerate` 时重新枚举显示器并写 `_cachedMonitors`，否则复用缓存（blend tick 不重扫进程，避免清掉用户正在输入的 ComboBox 文本，`:599-601` 注释）。
4. 每个显示器构造 `DashboardItem`：`BadgeText` = HDR/SDR 与对应画刷、当前 gamma、亮度、有效色温文本（`ColorAdjustments.ComposeTemperatureScaleMired` 串联用户/偏移/夜间三段，`:674-688`）、`IsCalibrating = CalibrationStateManager.IsDeviceInBypass(m)`。
5. `SyncDashboardItems`（`:611-652`）**复用已有卡片对象**（按 `MonitorDevicePath` 或 `DeviceName+OutputId` 匹配，`SameMonitor`），只把新值 `UpdateFrom` 进去，再把 `AppExclusionItem` 固定在最后。注释解释了原因：WPF 会保留该 item 的容器，因此夜间 blend 刷新时自动禁用卡片的输入焦点/弹出层不会被打断。
6. `reEnumerate` 时异步 `LoadRunningAppsAsync(_appExclusionItem)`。

**定时器与节流**（构造函数 `:284-346`）：

| 机制 | 间隔 | 作用 |
| --- | --- | --- |
| `_gamerAutoSaveTimer` | 180ms（debounce） | 合并 Game Lab 滑杆/预设的连续改动，只写一次 settings |
| `_runningAppRefreshTimer` | 2.5s | 刷新「正在运行的游戏」候选（Game Lab 两处界面共用） |
| `_bypassPollTimer` | 2s | 校准 bypass 没有事件，靠轮询让 Dashboard 感知开始/结束 |
| `ThrottledRefresh` | 500ms 节流 | `NightModeService.BlendChanged` 触发的兼容刷新（`:325-331`, `:499-527`） |
| `GamerSessionsChanged` | 事件 | 经 `Dispatcher.BeginInvoke` 更新 `GamerMode.SetSessions`（`:312-321`） |

**本地化**：所有依赖 `_loc.Get(...)` 的属性都是「读取时解析」的普通 getter；`_loc.CultureChanged += OnLanguageChanged`（`:238`），后者对空属性名 `OnPropertyChanged(string.Empty)`，一次性刷新全部绑定（`:1269-1270`）。注释举了「英文窗口里出现中文」的实际 bug 作为动机。

#### 11.3.2 DashboardWindow.xaml：结构性布局

窗口内容在最外层 `Border`（`ThemeBg` + `ThemeWindowFrame`，`DashboardWindow.xaml:245`）内，分成两行：48px 自绘标题栏 + 内容区。

**标题栏**（`:252-282`）从左到右：`BrandMark`（20×20，`GapBrush=ThemeSurface`）、`GLOAM` 字标、分隔线、`WindowSectionTitle`（`{loc:Tr chrome.section.dashboard}`）、`AppVersionLabel`（绑定 `AppVersion`）；右侧是 `LanguageToggleButton`、`ThemeToggleButton`（◐/◑，`ThemeToggle_Click`）、关闭按钮（✕，`Close_Click`）。整条栏 `MouseLeftButtonDown="TitleBar_MouseDown"` 调 `DragMove()`。

**内容区**（`:284-1086`）行定义为 Auto / * / Auto：

1. `DashboardHeader`（`:292-323`）：左侧 `Kicker`（`dashboard.displays`）+ 26px ExtraBold 标题（`dashboard.activeMonitors`）；右侧 Night profile 下拉（`ItemsSource=AvailableNightModeAlgorithms`、`SelectedValue=SelectedNightModeAlgorithm`，宽 210）、`Refresh` 按钮、`NightModeModeLabel` 循环按钮。
2. `ScrollViewer` → `DashboardContentStack`（`:325-1084`）：
   - `ItemsControl x:Name="DashboardItems"`，`ItemsPanel=WrapPanel`（`:327-333`）。两个 `DataTemplate` 定义在 `Window.Resources`：`DashboardItem` 显示器卡片与 `AppExclusionItem` 自动禁用卡片。
   - `GamerModeExpander`（`:337-983`，`IsExpanded=IsGameLabSectionExpanded`）：Game Lab 的全部 UI 都在这里——全局开关状态、`GamerAppPicker`、`ScanForGames`、发现结果列表、`LibraryPresetPicker`、游戏库列表（`DashboardGameProfileList`）、每个 profile 的编辑控件、Night behavior / HDR support / signal check / target displays 等高级面板，以及 `ApplyWhileRunningBox`。
   - Night Mode Controls（`:985-1002`，`IsExpanded=IsNightModeControlsExpanded`）：宿主 `local:NightModeScheduleControl x:Name="ScheduleEditor"`；调度控件本体由 NightMode 章节负责。
   - `MelanopicExpander`（`:1005-1083`，`IsExpanded=IsCircadianSectionExpanded`）：主读数 `ExposureBig`（mel-lx）、建议文本 `ExposureRecommendation`、今晚累计剂量 `DoseBig`（mel-lx·h）、目标 `DoseGoal`；二级 `MelanopicAdvancedExpander` 内是 `MelanopicSummary`、`MelanopicProvenance`、`DoseChartsPanel`、`DoseSummary`。

**显示器卡片模板**（`:33-130`）：`MonitorCard` 样式 + 头部（`FriendlyName`、HDR/SDR `BadgeText`/`BadgeColor`）、校准中 `dashboard.calibrating` 徽标与琥珀色描边（`IsCalibrating` trigger）、三项统计（GammaMode / Brightness / ColorTemp，校准 bypass 时整体淡出）、“Configure”按钮（`Command=ConfigureMonitorCommand`，`CommandParameter` 为 item）。

**自动禁用卡片模板**（`:133-241`）：标题、可编辑 `ComboBox x:Name="ExcludedAppPicker"`（`ItemsSource=RunningApps`、`Text=NewAppText` two-way，placeholder 用 `DataTrigger` 在文本为空时显示）、`AddExcludedAppButton`、`ListBox` 规则列表（每条含 `AppName`、`FullDisable` 复选框、删除按钮）。按钮的命令通过 `RelativeSource AncestorType=Window` 找到窗口 DataContext，这是 DataTemplate 内访问根 VM 的固定写法。

**`DashboardWindow.xaml` 的 31 个 `x:Name`**（按出现顺序，是改 XAML 时最容易被代码隐藏文件引用的清单）：

| x:Name | 行 | 所属区域 |
| --- | --- | --- |
| `WindowSectionTitle` / `AppVersionLabel` / `ThemeToggleButton` | 263 / 265 / 274 | 自绘标题栏 |
| `DashboardHeader` | 292 | 内容区顶栏 |
| `DashboardContentStack` / `DashboardItems` | 326 / 327 | 滚动区与显示器卡片列表 |
| `GamerModeExpander` | 337 | Game Lab 折叠区 |
| `GamerAppPicker` / `AddGamerProfileButton` | 536 / 563 | 「添加正在运行的游戏」 |
| `LibraryPresetPicker` / `ApplyLibraryPresetButton` | 584 / 603 | 整库预设 |
| `DashboardGameLibraryPane` / `DashboardProfileCount` / `DashboardShowAllProfilesButton` / `DashboardGameProfileList` | 638 / 665 / 668 / 677 | 游戏库列表 |
| `GameRow` / `LookCard` | 697 / 798 | 单个游戏行 / 外观编辑卡片 |
| `ApplyWhileRunningBox` | 917 | 高级：非焦点也应用 |
| `ScheduleEditor` | 999 | 夜间模式调度控件宿主 |
| `MelanopicExpander` | 1005 | 昼夜（melanopic）折叠区 |
| `ExposureBig` / `ExposureRecommendation` | 1026 / 1033 | 当前屏幕 melanopic 光 + 建议 |
| `DoseBig` / `DoseGoal` | 1040 / 1047 | 今晚累计剂量 / 目标 |
| `MelanopicAdvancedExpander` | 1055 | 高级信息二级折叠 |
| `MelanopicSummary` / `MelanopicProvenance` | 1066 / 1070 | 每显示器摘要 / 光谱来源 |
| `DoseChartsPanel` / `DoseSummary` | 1074 / 1076 | 剂量曲线容器 / 汇总文案 |
| `ExcludedAppPicker` / `AddExcludedAppButton` | 160 / 188 | 自动禁用卡片（在 DataTemplate 内） |

这些名字中只有 11 个真正被 `DashboardWindow.xaml.cs` 引用：`ThemeToggleButton`、`ScheduleEditor`、`ExposureBig`、`ExposureRecommendation`、`DoseBig`、`DoseGoal`、`MelanopicAdvancedExpander`、`MelanopicSummary`、`MelanopicProvenance`、`DoseChartsPanel`、`DoseSummary`。其余 20 个是 XAML 内部的命名节点（AutomationId/FindName 备查），重命名时只需改 XAML 与绑定；但 `WindowSectionTitle`、`AppVersionLabel` 这类当前只由绑定驱动的名字，也应保持唯一，避免后续代码隐藏加入引用时与模板内同名元素冲突。

#### 11.3.3 DashboardWindow.xaml.cs：视图关注点

代码隐藏严格只做视图/导航/绘制（文件头注释 `DashboardWindow.xaml.cs:9-14`）：

- 构造顺序（`:24-96`）：`InitializeComponent()` → `WindowBoundsPersistence.Attach(this, settingsManager, Loc.Get("chrome.section.dashboard"))` → new VM、挂 `ConfigureRequested`、`DataContext` → melanopic 服务事件 + 500ms 合并定时器 → `ScheduleEditor.Initialize(_viewModel.EditingNightMode)` 并接 `ScheduleChanged` / `PreviewTemperatureRequested` / `NightRenderingEdited` / `EditingNightModeReplaced` → `Closed` 里统一退订并 `_viewModel.Dispose()`。
- `ThemeToggle_Click`（`:101-105`）：`BrutalistTheme.Toggle()` + 同步 glyph。
- `OnConfigureRequested`（`:107-126`）：`new SettingsWindow(item.Model, snapshot, settings, ApplyFromSettings){Owner=this}` → `ShowDialog` → `_viewModel.Refresh()`。
- 昼夜卡片（`:138-412`）：`ExposureBig` 取所有显示器 melanopic EDI 之和；相位按本地时钟划分 Daytime 06–18 / Evening 18–23 / Night 23–06；目标常量 `DaytimeMelEdiTarget=250`、`EveningMelEdiCeiling=10`、`NightMelEdiCeiling=1`（Brown 2022 / CIE TN 015:2023 口径，`:140-146`）；今晚剂量 = 18:00 起 `MelanopicDoseStore.IntegrateDose`，预算 = 10 mel-lx × 已过小时数；图表每 15 秒或展开时重建，每个显示器一张独立 y 轴图（`CalibrationCharts.DrawLineChart`，`:312-412`）。

#### 11.3.4 DashboardItem / AppExclusionItem / DisplayIdentify

- `DashboardItem`（`src/Gloam/Features/Dashboard/DashboardItem.cs`，82 行）：纯展示模型，8 个 `SetProperty` 属性（`Model/FriendlyName/BadgeText/BadgeColor/CurrentGamma/CurrentBrightness/CurrentTemperatureText/IsCalibrating`）+ `UpdateFrom`（`:70-80`），配合 `SyncDashboardItems` 做原地刷新。
- `AppExclusionItem`（`AppExclusionItem.cs`，22 行）：`ObservableCollection<AppExclusionRule> ExcludedApps`、`ObservableCollection<string> RunningApps`、`NewAppText`。注释解释占位文本不能作为绑定值，否则会被当作可执行文件名持久化。
- `DisplayIdentify`（`DisplayIdentify.cs`，172 行）：静态工具，`Flash(MonitorInfo, index, duration?)` 在指定显示器上盖一个 `Topmost`、`ShowActivated=false`、`IsHitTestVisible=false` 的透明窗口，左上角是巨大编号 + 显示器名 + 「这是将被测量的显示器」提示，2.8 秒后 450ms 淡出并 `Close`。窗口按物理像素 bounds 摆放（注释承认高 DPI 下不是精确换算，但足够落在正确显示器，`:41-46`）；颜色全部从 `Application.Current.Resources` 的主题 token 取，取不到用硬编码 fallback（`:165-170`）。

### 11.4 基础设施

#### 11.4.1 对话框：BusyDialog / ConfirmDialog / CrashDialog

`src/Gloam/Infrastructure/Dialogs` 下三个对话框共同的设计约束是：**不要用系统 `MessageBox`**。原因有三：stock chrome 与 Brutalist 主题不搭；在 `Topmost` 全屏校准窗口之上 `MessageBox` 可能出现在窗口后面、用户完全看不到反馈；以及无法统一本地化。三个对话框都是 `WindowStyle=None` + `AllowsTransparency` + `SizeToContent=WidthAndHeight` + `WindowStartupLocation=CenterOwner`，颜色通过 `SetResourceReference` 引用 `Theme*` token。

| 对话框 | 构造方式 | 行为要点 |
| --- | --- | --- |
| `BusyDialog`（`BusyDialog.cs`，112 行） | `BusyDialog.Open(owner, message)` 非模态 `Show()`，`Dismiss()` 关闭 | 深色等待框 + 260px 轨道上循环滑动的 70px accent runner（`DoubleAnimation`，`:74-84`）；`Closing` 中 `e.Cancel = !_allowClose`，用户不能 Alt+F4；`Topmost = owner?.Topmost == true`，避免被全屏校准窗口压住（`:98-100`） |
| `ConfirmDialog`（`ConfirmDialog.cs`，172 行） | `Confirm(...)` / `ConfirmKeys(...)` / `Info(...)`，模态 `ShowDialog()` | 深色 Yes/No；`ConfirmKeys` 接受 catalog key 而非字面文本，避免每个调用点各自成为翻译点（`:116-120` 注释）；`Info` 是单 OK 按钮的信息框，`MaxWidth=640` 容纳多行报告；构造末尾 merge `pack://application:,,,/Gloam;component/Themes/DarkControls.xaml` 拿到按钮模板（`:106-110`） |
| `CrashDialog`（`CrashDialog.xaml` + `.cs`） | `CrashDialog.Show(owner, title, message, details)` | MVVM：`CrashDialogViewModel` 暴露 `Title/Message/Details/HasDetails/AreDetailsVisible` + 4 个命令（Continue/Exit/ToggleDetails/CopyDetails，`CrashDialogViewModel.cs:58-80`）；窗口把 `ContinueRequested/ExitRequested` 映射为 `DialogResult=false/true` + `Close`，`CopyDetailsRequested` 用 `Dispatcher.BeginInvoke` 写剪贴板（剪贴板要求 STA）；`Show` 自身失败时回落 `MessageBox` 保证仍有反馈（`CrashDialog.xaml.cs:71-86`） |

#### 11.4.2 UpdateService：Velopack 静默更新

`src/Gloam/Infrastructure/Platform/UpdateService.cs`（707 行）封装 Velopack 的 `UpdateManager`，feed 固定为 `https://github.com/halideworks/gloam`（`:35`），要求包 id 为 `GloamApp`（`:36`）。安装布局是 per-user `%LocalAppData%\GloamApp\current`，而应用数据目录是 `%LocalAppData%\Gloam`，注释特意说明两者分离是为了「卸载后 settings/logs/reports 仍在」（`:17-23`）。

**状态与节流常量**（`:26-29`）：成功检查间隔 15 分钟、失败重试 30 分钟、失败通知间隔 24 小时、连续失败阈值 3 次。

**两条应用路径**：

- `ApplyUpdatesOnExit(info)` → `WaitExitThenApplyUpdates(asset, silent: true, restart: false)`（`:226-246`）：后台下载完成后静默排队，进程正常退出时由 Velopack 落地，下次启动即是新版本。`TrayViewModel.Dispose` 会再补一次，因为调度可能失败。
- `ApplyUpdatesAndRestart(info)` → `_manager.ApplyUpdatesAndRestart(info)`（`:208-219`）：会终止当前进程，所以只允许由「Restart now」toast 动作显式触发；调用方必须先保存状态。

**安全校验** `IsAcceptableRemoteUpdate`（`:364-389`）在每次 check 和 download 前都跑：目标版本必须严格更新（`IsNewerThanCurrent`，且 `isDowngrade` 直接拒绝）、`PackageId` 必须等于 `GloamApp`、包大小 > 0、`SHA256` 必须是 64 位十六进制。任一不满足记 `Log.Error` 并返回 false——这是「更新 feed 被投毒/误发」的最后一道闸。

**pending 更新自愈** `TrySchedulePendingUpdateOnExit`（`:109-136`）：读取 `_manager.UpdatePendingRestart`；若它不比当前版本新，删除暂存的 `.nupkg`（避免 100+MB 垃圾常驻）并返回 false；否则重新排到退出时应用。

**通知去重**：`ShouldNotifyUpdateAvailable/Ready/Updated` 各自保存「上次已通知版本」，版本相同不再提示；`Mark*` 系列写回状态。**持续失败**只在 `ConsecutiveFailures >= 3` 且距上次失败通知 ≥ 24h 时提示一次（`:248-269`）。`UpdateState`（`:657-...`）把安装版本、最后一次检查/成功/错误、目标包元数据、各通知版本、连续失败数序列化到磁盘，并作为 `StateSnapshot` 供诊断包使用；**节流跳过（throttle skip）单独记 `LastSkipReason`**，不覆盖真正的 `LastResult`（`:149-156` 注释）。

`IUpdateManagerAdapter`（`:600-...`）是给单元测试用的接缝：`UpdateService` 有两个 internal 构造函数，接受 adapter 和可注入时钟（`:49-55`）。

#### 11.4.3 StartupManager：HKCU Run 与自愈

`StartupManager.cs`（196 行）管理 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 下的 `Gloam` 值：

- `IsStartupEnabled` 读写属性（`:22-41`），写入时用 `TrySetStartupEnabled`，值形如 `"<exe path>"`（带引号）。
- 旧品牌值 `HDRGammaController` 在每次读写前由 `MigrateLegacyValue()` 迁移：删除旧值，若 `Gloam` 不存在则用当前 exe 重新注册（`:161-185`）。注释强调这个字面量是已发布版本写下的注册表身份，不能跟着改名。
- `EnableByDefaultForFreshInstall(settings, isInstalled)`（`:72-84`）：只对「已安装 + settings 文件不是既有的 + 未打过默认标记」的首次运行注册，并 `MarkStartupDefaultApplied()`。
- `RepairIfStale(isInstalled)`（`:94-117`）：已安装且已注册时，比较注册路径与当前 exe，不一致就重写；`IsRegisteredPathStale` / `ExtractExePath` 是纯函数，便于测试引号与尾随参数（`:125-154`）。
- exe 路径优先 `Environment.ProcessPath`（单文件发布下也指向真实宿主），再退 `MainModule.FileName`（`:187-194`）。

#### 11.4.4 ResourceExtractor：ICM 模板落地

`ResourceExtractor.cs`（163 行）把 6 个嵌入的 `srgb_to_gamma2p2_*.icm` 模板释放到磁盘：

- 目标目录优先应用目录；先做一次写探针 `IsWritable`，Program Files 下非提权进程不可写时回退到 `AppPaths.DataDir`（`:36-44`）。注释给出真实事故：镜像部署清空了 app 目录里先前解压的模板，导致校准 apply 报 “No MHC2 template found”。
- 每个文件按资源名后缀匹配（兼容 `_icm` 命名变体，`:104-113`），读入内存做 SHA-256 与现有文件比对，仅缺失或哈希不同时才写；返回更新数量（供日志与 `--smoke` 断言）。
- 同一批 `.icm` 在 csproj 里既 `EmbeddedResource` 又 `None CopyToOutputDirectory=PreserveNewest`，是「运行时可提取 + 发布产物自带」的双保险（`Gloam.csproj` 两个 ItemGroup）。

#### 11.4.5 WindowBoundsPersistence：窗口位置恢复

`WindowBoundsPersistence.cs`（121 行）用 `Attach(window, settingsManager, key, shouldSave?)` 挂两个事件：`SourceInitialized` 恢复、`Closing` 保存（`:11-21`）。恢复逻辑（`Apply`，`:33-75`）：

- 非法/非有限/非正的尺寸直接放弃；
- 宽高夹在 `max(MinWidth, 480) × max(MinHeight, 360)` 与虚拟桌面之间；
- 若窗口大部分在屏幕外（可见边缘 < 80px，`RecoveryVisibleEdge`），居中到虚拟桌面；否则把左上角夹回可见范围；
- 保存时 `WindowState != Normal` 用 `RestoreBounds`，否则用实际 `Left/Top/ActualWidth/ActualHeight`（`:77-94`）。

`CopyBounds` 提供「子窗口沿用父窗口位置」的能力（`:23-24`）。

#### 11.4.6 SafeAsync：async void 的兜底

`SafeAsync.cs`（78 行）只有一个 `FireAndForget(Func<Task> work, string context, Action<Exception>? onError = null)`。设计论证在类注释里：`async void` 里逃逸的异常会在 dispatcher 上重抛成进程崩溃；改成 `void Handler() => SafeAsync.FireAndForget(() => HandlerAsync(), nameof(Handler))` 后，`async Task` 方法不会同步抛出，所有失败都走返回的 Task，被 `await` 在 try 里接住。`OperationCanceledException` 被视为正常结果（测量/下载里用户取消很常见），其他异常记 `Log.Error`；`onError` 只给有安全展示面的调用方，且它自身的异常也被吞掉——注释明确「这不是错误报告路径，而是防崩溃 backstop」（`:24-31`）。

### 11.5 主题与视效

#### 11.5.1 资源字典的分层

主题由 4 个 `ResourceDictionary` 组成，合并关系是「全局一层 + 窗口按需一层」：

```
App.xaml（全局，始终合并）
 └─ Tokens.xaml ……………………………… 3 个 FontFamily + 12 个 Theme* token + 9 个旧别名

DashboardWindow / SettingsWindow / CalibrationWindow（XAML merge）
 └─ Brutalist.xaml ………………………… 10 个命名 Style + 一批隐式控件 Style

ToastWindow / CrashDialog（XAML merge）
 ├─ Brutalist.xaml
 └─ Overlays.xaml ………………………… 覆层专用 4 个 Style（OverlayCard/Title/Message/Detail）

ConfirmDialog / DisplayMatchWindow / ProfileManagerWindow / ReportHistoryWindow /
WhiteToolsDialog / WhiteTrimWindow / CcssBrowserWindow / TrustCheckWindow（代码或 XAML merge）
 └─ DarkControls.xaml …………………… Utility* 画刷 + 深色工具控件模板
```

实测的合并关系（`grep 'ResourceDictionary Source='`）：`DashboardWindow.xaml`、`SettingsWindow.xaml`、`CalibrationWindow.xaml` 只 merge `Brutalist.xaml`；`ToastWindow.xaml` 与 `CrashDialog.xaml` merge `Brutalist.xaml` + `Overlays.xaml`；`DarkControls.xaml` 的消费者是 `ConfirmDialog`、`DisplayMatchWindow`、`ProfileManagerWindow`、`ReportHistoryWindow`、`WhiteToolsDialog`、`WhiteTrimWindow`、`CcssBrowserWindow`、`TrustCheckWindow`（多数在代码里 `Resources.MergedDictionaries.Add`）。

- `src/Gloam/Themes/Tokens.xaml`（45 行，24 个 key）：3 个 `FontFamily`（`DisplayFont`/`BodyFont`/`MonoFont`）+ 12 个 `Theme*` 画刷 + 9 个 legacy alias（`AccentBrush`、`CardBackground`、`TextDim`、`InputBackground`、`DarkAccent`…）。文件头注释说明它是**被动**的：只有 `{DynamicResource Theme*}` 的消费者才受影响；默认值即深色主题。
- `src/Gloam/Themes/Brutalist.xaml`（514 行）：命名样式 `MonitorCard`、`Kicker`、`FieldLabel`、`MonoValue`、`BrutBtnSecondary`、`BrutBtnPrimary`、`BrutLanguageButton`、`BrutIconButton`、`ModernToggle`、`ScrollBarThumb`；另有 `Button`、`ContextMenu`、`MenuItem`、`Separator`、`ProgressBar`、`CheckBox`、`RadioButton`、`TextBox`、`ComboBox`/`ComboBoxItem`、`ScrollBar` 的隐式模板。方角、1px 边框、accent 填充是这套样式的统一语言。
- `src/Gloam/Themes/DarkControls.xaml`（165 行）：7 个 `Utility*`/`Dark*` 画刷 + `Button`、`GridViewColumnHeader`、`Thumb`、`ListViewItem`、`ScrollBar` 的隐式模板，供代码构建的工具窗口使用。
- `src/Gloam/Themes/Overlays.xaml`（57 行）：`OverlayCard`、`OverlayTitle`、`OverlayMessage`、`OverlayDetail` 四个样式，头注释写明由 `ToastWindow` 与 `CrashDialog` 共用，颜色全部走 `DynamicResource Theme*`，因此跟随明暗切换。

#### 11.5.2 BrutalistTheme：运行时换肤

`src/Gloam/Infrastructure/Theming/BrutalistTheme.cs`（79 行）是主题的唯一写入口：

- `IsDark`（默认 `true`）、`Changed` 事件、`Persist` 回调（由 `App.OnStartup` 设为 `settings.SetDarkTheme`）。
- `Initialize(dark)` 只应用不持久化；`Toggle()` 应用并 `Persist?.Invoke(IsDark)`。
- `Apply()` 把 12 个 token 的画刷**整体替换**进 `Application.Resources`，再把 9 个 legacy alias 指向同一个新画刷，最后触发 `Changed`。注释强调必须「替换（swap）而不是原地修改（mutate）」：sealed style 会冻结它引用的画刷，改颜色会抛异常（`:9-11`）。
- `Color(key)` 给代码绘制的画布（如调度图、剂量图）按 key 取当前颜色。

token 的双色值定义在 `Palette`（`:26-40`）：

| Token | Dark | Light | 用途 |
| --- | --- | --- | --- |
| `ThemeBg` | `#0E1116` | `#F7F8FA` | 窗口底色 |
| `ThemeSurface` | `#171C23` | `#FFFFFF` | 卡片/标题栏表面 |
| `ThemeHover` | `#222A34` | `#EEF1F4` | hover 背景 |
| `ThemeText` / `ThemeTextDim` | `#F4F7FA` / `#A8B0BC` | `#15191F` / `#64707D` | 主/次文字 |
| `ThemeBorder` / `ThemeWindowFrame` | `#465567` / `#364252` | `#B9C4D0` | 控件边框 / 窗口外框 |
| `ThemeAccent` / `ThemeOnAccent` | `#E35F52` / `#FFFFFF` | `#CF4A40` / `#FFFFFF` | 信号红 / 反色文字 |
| `ThemeMeter` / `ThemeTrack` | `#82B7F2` / `#29313B` | `#327CC7` / `#DFE5EC` | 仪表 / 轨道 |
| `ThemeAmber` | `#D89A2B` | `#B87512` | 警告、校准中 |

`Changed` 的另一个消费者是 `App.ApplyTrayMenuTheme`：托盘 ContextMenu 不在任何窗口的可视树里，`DynamicResource` 够不到窗口级字典，所以由 app 级事件把 `Menu*` 画刷重新指向 token（见 11.1.2）。

#### 11.5.3 BrutalistChrome：代码构建窗口的统一外壳

`BrutalistChrome.cs`（142 行）给**没有 XAML 标题栏**的窗口套一层一致的 chrome：`Apply(win, title, content)` 把 `WindowStyle=None`、透明背景、`ResizeMode=CanResizeWithGrip`，顶部插入 44px 高 header（`BrandMark` + `GLOAM` + 大写 title + ◐/◑ 主题切换 + ✕ 关闭），下方放内容，外面再包 1px `ThemeWindowFrame` 边框。所有画刷用 `SetResourceReference` 绑定 token，切换主题自动重解析；切换 glyph 与 `ThemeChanged` 事件同步，并在 `Closed` 退订防止静态事件泄漏窗口（`:118-124`）。一个细节：切换/关闭 glyph 的 `MouseLeftButtonDown` 必须 `e.Handled = true`，否则事件冒泡到 header 的 `DragMove()`，而 `DragMove` 会捕获鼠标并吞掉 `MouseUp`，glyph 就永远点不动（`:88-102` 注释）。

#### 11.5.4 OS 主题探测与原生标题栏

- `ThemeDetector.cs`（39 行）：读 `HKCU\...\Themes\Personalize\AppsUseLightTheme`，`0 = dark`，读取失败默认 `false`（light）——注释说明这是「无法检测时的保守选择」。
- `DarkTitleBar.cs`（29 行）：`DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE=20)`，只用于仍保留系统 chrome 的窗口。
- `WindowTheme.cs`（83 行）提供更完整的能力：`UseDarkTitleBar` 先试属性 20，失败回落 19（Win10 1809–1903）；`ExcludeFromPeek` 设 `DWMWA_DISALLOW_PEEK(11)` + `DWMWA_EXCLUDED_FROM_PEEK(12)`，注释给出硬理由——校准 patch 窗口若在 Aero Peek 中变透明，探针会读到桌面而不是色块，测量直接作废（`:56-61`）。两个方法都同时支持已有 HWND 与 `SourceInitialized` 延迟应用两种调用时机。

#### 11.5.5 绝对 pack URI 规则

代码里所有跨程序集/运行时的资源引用都写**绝对 pack URI**：

```
pack://application:,,,/Gloam;component/Themes/Tokens.xaml
pack://application:,,,/Gloam;component/Themes/Brutalist.xaml
pack://application:,,,/Gloam;component/Fonts/#Plus Jakarta Sans
```

规则来自 `Gloam.csproj`：`AssemblyName`/`RootNamespace` 被显式钉成 `Gloam`，注释说明 `/Gloam;component/` 是**运行时身份**，项目文件改名时不能让它们漂移。绝对 URI 在两类场景是必需的：一是 `ConfirmDialog`/`ToastWindow` 这类**代码构建或运行时才 merge** 的窗口（相对 URI 没有基准）；二是字体 `FontFamily` 的 pack URI 必须带 `;component` 才能定位到资源程序集。`App.xaml` 里 merge `Tokens.xaml` 也用了绝对形式（`App.xaml:10`）。

#### 11.5.6 字体族

三套 OFL 字体以 `Resource` 形式打进程序集（`Gloam.csproj` 的 `<Resource Include="Fonts\*.ttf" />`），按**排版族名**引用，而不是文件名：

| Token | 族名 | 文件 | 用途 |
| --- | --- | --- | --- |
| `DisplayFont` | Plus Jakarta Sans | `PlusJakartaSans-{Regular,Medium,Bold,ExtraBold}.ttf` | 标题、大数字、Kicker、按钮 |
| `BodyFont` | Space Grotesk | `SpaceGrotesk-{Regular,Medium,Bold}.ttf` | 正文 |
| `MonoFont` | Cascadia Mono | `CascadiaMono-{Regular,Bold}.ttf` | 路径/版本/异常详情/图表轴标签 |

`Tokens.xaml:12-15` 的注释说明 mono 会回落到系统常驻的 Consolas；`CultureFontPolicy` 会在语言切换时改写 `BodyFont`/`DisplayFont` 两个 token（见 11.5.7）。

#### 11.5.7 本地化接入点与字体策略

本地化项目本体由其他章节负责，这里只记录 UI 外壳的接线：

- 目录 `src/Gloam/Resources/i18n/` 内置两份 catalog：`en-US.json`（约 59KB）与 `zh-Hans.json`（约 58KB）；csproj 以 `EmbeddedResource Include="Resources\i18n\*.json"` 打进程序集，资源名前缀为 `Gloam.Resources.i18n.`。
- 运行时可被 `%LOCALAPPDATA%\Gloam\i18n` 覆盖（`App.BuildLocalizer`，`:69-83`），不需要重新发版。
- `LocalizedStrings.Default` 必须在任何 XAML 解析前发布（`App.xaml.cs:137-142`），因为 `{loc:Tr}` 通过它解析；`TrExtension`（`Gloam.Localization.Wpf/TrExtension.cs`）是 XAML 端的 markup extension，`Loc.Get/Format` 是代码端的静态入口。
- `CultureFontPolicy`（`Gloam.Localization.Wpf/CultureFontPolicy.cs`）在需要 CJK 字体的 culture 下，把 `BodyFont`/`DisplayFont` 换成 `Microsoft YaHei UI`/`Noto Sans CJK SC` 等系统族，非 CJK 时恢复 `Tokens.xaml` 首次发布的字体快照——因为 XAML 用 `DynamicResource` 消费这两个 key，已打开的窗口也会重新解析。这是 `App.OnStartup` 必须在 `MainWindow` 之前做语言发布的原因之一。
- `LanguageService`（`Gloam.Localization.Wpf/LanguageService.cs`）通过 `SettingsManager.SetLanguage` 持久化，并在 `LanguageChanged` 后让 `LanguageToggleButton`、`DashboardViewModel`、`TrayViewModel` 等分别刷新。

#### 11.5.8 Controls 与 Behaviors

- `Controls/BrandMark.xaml(.cs)`（21 + 20 行）：品牌标——`ThemeAccent` 圆盘 + 一条用 `GapBrush` 描的 S 形切口。`GapBrush` 是依赖属性，调用方要把它设成「标背后那块表面的颜色」，切口才像透明；标题栏场景传 `ThemeSurface`。图标与 `app.ico`、站点 favicon 同源。
- `Controls/LanguageToggleButton.cs`（143 行）：标题栏语言按钮，显示的是「点一下会切到哪种语言」（英文界面显示 `ZH`），而不是当前语言；`Load` 时从 `App.Services` 取 `LanguageService`（XAML 无法构造注入），取不到则回落到 `LocalizedStrings.Default.Localizer`，若可用语言少于 2 个就把自己 `Collapsed`（`:38-61`）。
- `Behaviors/SliderBehaviors.cs`（270 行）包含两个 attached behavior：`SliderDetent`（`DetentValue`/`SnapThreshold`/`IsEnabled`，拖拽中越过阈值立即吸附，鼠标释放/失去捕获时再补一次 `SnapIfNeeded`）与 `ClickToEdit`（点击 TextBlock 弹出 TextBox 直接输入数值）。它们服务于校准/设置里的数值微调。

### 11.6 MVVM 约定与数据流

#### 11.6.1 CommunityToolkit.Mvvm 的实际用法

`Gloam.csproj` 引用 `CommunityToolkit.Mvvm`，但**没有使用源生成器**：在整个 `src/Gloam` 中检索 `[ObservableProperty]` 与 `[RelayCommand]` 均为 0 处，`new RelayCommand` / `new AsyncRelayCommand` 在 UI 程序集共 50 行（含校准等功能）。落地约定是：

- ViewModel 继承 `ObservableObject`，属性手写 `private` 字段 + `SetProperty(ref _field, value)`，或包一层计算属性（如 `Header => $"{...}"`）。
- 命令声明为 `public ICommand XxxCommand { get; }`，在构造函数里 `new RelayCommand(...)` / `new RelayCommand<T>(...)` / `new AsyncRelayCommand(...)` 一次性赋值，不做 `CanExecute` 的频繁变更，因此无需 `NotifyCanExecuteChanged`。
- 派生通知用 `OnPropertyChanged(string.Empty)` 触发「全部刷新」（Dashboard 的语言切换，`DashboardViewModel.cs:1269-1270`），或用显式 `RaiseGamerModeStateProperties()`（`:1223-...`）。
- `RelayCommand<T>` 的参数直接绑定 `CommandParameter="{Binding}"`（自动禁用卡片、Game Lab 列表项都是这个模式）。

本章范围内可点数的命令共 **38 个**：`TrayViewModel` 10、`MonitorViewModel` 4、`DashboardViewModel` 19、`ToastViewModel` 1、`CrashDialogViewModel` 4。`ActionViewModel` 不生产命令，只承载命令引用。

#### 11.6.2 Dispatcher 与线程约定

Gloam 的 UI 线程纪律集中体现在四类写法上：

| 场景 | 约定写法 | 出处 |
| --- | --- | --- |
| 后台服务事件要改 UI | `Dispatcher.BeginInvoke`，**绝不** `Invoke` | `TrayViewModel.OnUiThread`（`:447-471`）注释：模态拖拽循环（`DragMove`）不泵 dispatcher，同步 `Invoke` 会卡死后台线程；关停阶段的 `Invoke` 会抛异常拖垮进程 |
| 高密度状态更新 | `DispatcherTimer` 合并（500ms / 180ms / 2.5s / 2s） | DashboardViewModel 构造函数 |
| `async void` 事件处理器 | `SafeAsync.FireAndForget(() => HandlerAsync(), nameof(Handler))` | `SafeAsync.cs:44-76` |
| 需要闭包到 UI 线程的一次性更新 | `BeginInvoke(() => ...)`，先判 `HasShutdownStarted/Finished` | `TrayViewModel.cs:456-471` |

`SystemEvents` 回调（时区/主题变化）同样遵守：`OnTimeChanged` 先 `Dispatcher.BeginInvoke` 再碰 `NightModeService`（`App.xaml.cs:230-246`）；`RefreshThemeFromSystem` 用 `Dispatcher.Invoke` 是因为它从 `WM_SETTINGCHANGE` 的 hook 直接调用，本身已在 UI 线程，且需要同步生效。

#### 11.6.3 代码隐藏的边界

视图层 CS 文件只允许出现以下五类代码，超出即视为把业务逻辑漏进了 View：

1. 窗口 chrome：`DragMove()`、`Close()`、主题 glyph（`DashboardWindow.xaml.cs:101-136`）。
2. 视图生命周期：订阅/退订服务事件、停定时器、`Closed` 时 `Dispose` VM（`:82-91`）。
3. 无法 MVVM 化的控件桥接：`ScheduleEditor.Initialize/ ScheduleChanged/ PreviewTemperatureRequested`（`:66-80`）。
4. 画布绘制：`CalibrationCharts.DrawLineChart` 的调用与布局后重绘（`:312-412`，用 `DispatcherPriority.Loaded` 等布局完成）。
5. 导航：`new SettingsWindow(...){Owner=this}.ShowDialog()`（`:107-126`）。

反例是 `TrayViewModel`/`MonitorViewModel` 直接 `new` 窗口（`OpenDashboard`、`OpenSettings`），这在「托盘是导航根」的架构里是刻意例外：托盘 VM 事实上承担了 shell 的导航职责，所以 `App.ConfigureServices` 才没有给窗口注册工厂。

#### 11.6.4 View → ViewModel → Core 数据流

```
[Win32 消息]  WM_DISPLAYCHANGE / PBT_APMRESUMEAUTOMATIC / WM_HOTKEY / WM_SETTINGCHANGE
      │
      ▼
MainWindow.WndProc ──► HotkeyManager.HotkeyPressed ──► TrayViewModel.OnHotkeyPressed
      │                                             ├─► ApplyProfile → RequestApply
      │                                             └─► PanicAll / ToggleGameLabWindow
      ▼
TrayViewModel.HandleDisplayChange/HandleResume ──► (去抖) ──► ApplyAll
      │                                                        │
      ▼                                                        ▼
RefreshMonitors ──► TrayItems ──► TaskbarIcon.ContextMenu    GammaApplyService
      │                │  (绑定)                                  │
      │                └── MonitorViewModel ──► 子菜单            ▼
      │                                                NightModeService / ProfileManager
      ▼                                                        │
DashboardWindow ◄── DashboardViewModel.Refresh ◄── MonitorManager / SettingsManager
      │  DataContext 绑定                                                        │
      └── Items(DashboardItem / AppExclusionItem) ◄── 枚举 + settings 快照      ▼
                                                                    GPU gamma ramp / MHC2
通知：任意 Core/VM ──► IToastService.Show ──► ToastWindow（单例覆盖）
主题：BrutalistTheme.Apply ──► Application.Resources[Theme*] ──► 所有 DynamicResource
本地化：ILocalizer.CultureChanged ──► LocalizedStrings.Default / 各 VM 刷新 ──► XAML 重新求值
```

要点：**硬件写入只发生在 `GammaApplyService`**，UI 层任何「应用 gamma / 亮度 / 夜间模式」的动作最终都汇聚为 `RequestApply` 或 `ApplyAll`；UI 只读 `SettingsManager` 快照与 `MonitorManager` 枚举结果，不直接写注册表/ramp（启动项、Night Light 是明确例外，分别由 `StartupManager`、`WindowsNightLight` 封装）。

### 11.7 不变量、风险与文件职责表

#### 11.7.1 不变量（改代码时必须保持）

1. **Velopack 钩子先于 WPF**：`Program.Main` 里 `VelopackApp.Run()` 必须是第一段逻辑；`StartupObject` 必须继续指向 `Gloam.Shell.Program`，不能退回到 `App.xaml` 生成的 `Main`。
2. **单实例互斥体不能被回收**：`_singleInstanceMutex` 必须是 static 且活到进程结束，否则句柄释放后第二个实例可以起来抢 gamma ramp。
3. **`LocalizedStrings.Default` 先于第一个 XAML**：语言发布与 `CultureFontPolicy` 必须在 `new MainWindow()` 之前完成；否则窗口会出现原始 key 或错误字体。
4. **主题是「替换画刷」不是「改颜色」**：`BrutalistTheme.Apply` 必须新建 `SolidColorBrush`；原地改 `Color` 会在 frozen style 上抛异常。
5. **托盘菜单的原地更新**：菜单打开期间不能 `TrayItems.Clear()`（语言切换除外，它会重建整棵树并另开菜单）；启动/GameMode/gamma 勾选只能改 `Header`。
6. **跨线程只 `BeginInvoke`**：后台服务/定时器/hook 线程更新 UI 一律排队，不阻塞；`Application.Current` 或 dispatcher 已关停时静默 no-op。
7. **`Cleanup` 幂等**：`MainWindow.Cleanup` 会被 `OnClosed` 与 `Application.Exit` 各调一次，`_cleanedUp` 必须挡住第二次；被 dispose 的服务自身也要幂等。
8. **崩溃对话框每会话一次**：`_crashDialogShown` 是防弹窗循环的唯一屏障。
9. **`isInstalled` 门**：`UpdateService` 的下载/应用、`StartupManager` 的默认注册与自愈，都只能在 Velopack 安装态执行；dev/portable 不得改写用户的启动项或触发更新。
10. **绝对 pack URI**：任何运行时 merge 的字典与 `FontFamily` 都必须用 `pack://application:,,,/Gloam;component/...`；`AssemblyName`/`RootNamespace` 必须固定为 `Gloam`。
11. **窗口 bounds 必须可恢复**：恢复时非法值丢弃、越界回中，避免把窗口恢复到已拔掉的显示器上。
12. **单 toast 覆盖**：`ToastService` 只保留一个窗口；不要改成堆叠，否则夜间 fade tick 会刷屏。

#### 11.7.2 风险与异味

- **体量集中**：`DashboardWindow.xaml` 95KB、`DashboardViewModel.cs` 1294 行、`TrayViewModel.cs` 1025 行，且 Dashboard 的 Game Lab 全部 UI 内嵌在同一个 XAML（`:337-983`）。改动易冲突，阅读成本高；拆分（子 UserControl + 子 VM）是明显但未做的重构。
- **VM 直接 new 窗口**：`TrayViewModel`/`MonitorViewModel` 里存在 `new DashboardWindow/GameLabWindow/SettingsWindow`，使 VM 依赖 WPF 窗口类型，单元测试无法覆盖导航分支；只有构造参数做了可选化（`localizer` 等）来保住一部分可测性。
- **视图层承担绘制与文案**：昼夜卡片的相位判断、剂量积分、建议文案、图表绘制都在 `DashboardWindow.xaml.cs`（`:138-412`），这部分既不是纯 View 也不在 VM，属于事实上的第三层。
- **XAML 触发器与默认值**：`ToastWindow` 的 accent stripe 依赖「默认值写在 Style 而非 inline」这一 WPF 优先级规则（`:51-54` 注释），改动样式时容易再次踩坑。
- **`WM_SETTINGCHANGE` 与 `SystemEvents` 双通道**：`App.RefreshThemeFromSystem` 会从两个入口被调，依赖 `BrutalistTheme.IsDark` 比较来避免无谓重建；任何一侧去掉都会导致托盘菜单主题不跟随。
- **静态事件订阅泄漏**：`BrutalistTheme.Changed`（BrutalistChrome、App）、`BlendChanged`、`GamerSessionsChanged`、`ForegroundAppChanged` 都要求窗口/VM 在 `Closed`/`Dispose` 里退订；`DashboardWindow` 的每一处订阅都有对应退订，但这是需要逐处复核的模式。
- **未验证项**：本章所有「运行时」结论（消息实际到达顺序、DPI 换算、菜单在打开状态下的重建行为、焦点/动画、Velopack 静默应用是否在退出时真正落地）均为静态阅读推断，未经运行验证；仓库中是否存在覆盖这些路径的测试也没有在本任务中核对（明确禁止 build/test/git）。

#### 11.7.3 文件 → 职责表

| 文件（相对 `src/Gloam/`） | 行数/大小 | 职责 |
| --- | --- | --- |
| `Shell/Program.cs` | 149 行 | 进程入口：Velopack 钩子、`--smoke`、单实例互斥、启动 App |
| `Shell/App.xaml` | 32 行 | `OnExplicitShutdown`、全局异常、Tokens 合并、托盘菜单画刷 key |
| `Shell/App.xaml.cs` | 337 行 | 组合根、启动顺序、主题/语言发布、启动项自愈、系统事件、崩溃对话框 |
| `Shell/MainWindow.xaml` | 132 行 | 隐藏窗口 + TaskbarIcon + 托盘菜单模板 |
| `Shell/MainWindow.xaml.cs` | 130 行 | 句柄/图标、HotkeyManager、TrayViewModel 构造、WndProc 三消息、幂等 Cleanup |
| `Features/Tray/TrayViewModel.cs` | 1025 行 | 托盘菜单、10 命令、热键分发、更新协调、显示器/夜间模式状态、诊断导出 |
| `Features/Tray/MonitorViewModel.cs` | 152 行 | 单显示器子菜单：HDR/SDR 子项、勾选、Settings 导航 |
| `Features/Tray/ActionViewModel.cs` | 41 行 | 托盘菜单通用项（含分隔线与 `StaysOpenOnClick`） |
| `Features/Tray/HotkeyManager.cs` | 83 行 | `RegisterHotKey` 封装、WM_HOTKEY 派发、注销 |
| `Features/Tray/ToastService.cs` | 154 行 | `IToastService` 实现、单窗口覆盖、阅读时长、Dispatcher 编组 |
| `Features/Tray/ToastViewModel.cs` | 99 行 | Toast 展示状态 + `ToastKind` + 动作命令 |
| `Features/Tray/ToastWindow.xaml(.cs)` | 100 + 137 行 | 覆层窗口：accent stripe、淡入淡出、右下角定位（DIP 换算） |
| `Features/Dashboard/DashboardViewModel.cs` | 1294 行 | 19 命令、刷新管线、分区折叠持久化、Game Lab/夜间/节律绑定、节流定时器 |
| `Features/Dashboard/DashboardWindow.xaml` | 1089 行 | 自绘标题栏、显示器/自动禁用卡片模板、Game Lab/Night Mode/Circadian 三个 Expander |
| `Features/Dashboard/DashboardWindow.xaml.cs` | 414 行 | 窗口 chrome、Settings 导航、ScheduleEditor 桥接、昼夜卡片读数与剂量图 |
| `Features/Dashboard/DashboardItem.cs` | 82 行 | 显示器卡片展示模型 + `UpdateFrom` |
| `Features/Dashboard/AppExclusionItem.cs` | 22 行 | 自动禁用卡片数据（规则、运行中应用、输入文本） |
| `Features/Dashboard/DisplayIdentify.cs` | 172 行 | 「这是哪台显示器」全屏编号浮层 |
| `Infrastructure/Dialogs/BusyDialog.cs` | 112 行 | 不可关闭的等待框 + 滑动进度条 |
| `Infrastructure/Dialogs/ConfirmDialog.cs` | 172 行 | 深色 Yes/No 与 Info 对话框、`ConfirmKeys` 本地化重载 |
| `Infrastructure/Dialogs/CrashDialog.xaml(.cs) + CrashDialogViewModel.cs` | 89 行 + 4.6KB + 82 行 | 崩溃对话框视图、MVVM 映射（剪贴板/MessageBox 回落）、状态与 4 命令 |
| `Infrastructure/Platform/UpdateService.cs` | 707 行 | Velopack feed、检查/下载/排队、安全校验、通知去重、状态快照 |
| `Infrastructure/Platform/StartupManager.cs` | 196 行 | HKCU Run 读写、旧名迁移、新装默认、路径自愈 |
| `Infrastructure/Platform/ResourceExtractor.cs` | 163 行 | 6 个 ICM 模板提取、哈希比对、可写目录回退 |
| `Infrastructure/Platform/WindowBoundsPersistence.cs` | 121 行 | 窗口位置恢复/保存/越界回中 |
| `Infrastructure/Platform/SafeAsync.cs` | 78 行 | `async void` 事件处理器的异常兜底 |
| `Infrastructure/Theming/BrutalistTheme.cs` | 79 行 | 12 token × 双色 + alias + 运行时换肤唯一入口 |
| `Infrastructure/Theming/BrutalistChrome.cs` | 142 行 | 代码构建窗口的统一标题栏/边框 |
| `Infrastructure/Theming/{ThemeDetector,DarkTitleBar,WindowTheme}.cs` | 39 + 29 + 83 行 | OS 明暗探测、原生标题栏深色（属性 20→19 回落）、Aero Peek 排除 |
| `Themes/Tokens.xaml` | 45 行 | 字体 + Theme token + 旧别名（全局） |
| `Themes/Brutalist.xaml` | 514 行 | 命名样式 + 隐式控件模板 |
| `Themes/{DarkControls,Overlays}.xaml` | 165 + 57 行 | 工具窗口深色控件模板 / Toast·Crash 覆层样式 |
| `Controls/{BrandMark.xaml(.cs),LanguageToggleButton.cs}` | 21 + 25 + 143 行 | 品牌标（圆盘 + S 切口，`GapBrush`）/ 标题栏语言切换（显示目标语言） |
| `Behaviors/SliderBehaviors.cs` | 270 行 | 滑杆 detent 吸附 + 点击编辑 |
| `Resources/i18n/{en-US,zh-Hans}.json` | 约 59KB + 58KB | 英文 / 简体中文 catalog（嵌入基线） |
| `Fonts/*.ttf` | 9 个文件 | Plus Jakarta Sans / Space Grotesk / Cascadia Mono（OFL） |

#### 11.7.4 本章计数小结

- **命令**：38 个（Tray 10 + Monitor 4 + Dashboard 19 + Toast 1 + CrashDialog 4）。
- **窗口/对话框表面**：6 个 `Window` 派生类（`MainWindow`、`DashboardWindow`、`ToastWindow`、`BusyDialog`、`ConfirmDialog`、`CrashDialog`）+ 1 个匿名浮层（`DisplayIdentify` 临时 `Window`）= 7 个可显示表面；其中只有 `DashboardWindow` 是常规可见窗口，`MainWindow` 永不显示。
- **热键**：6 组（Win+Shift+F1/F2/F3/F4/N/G）。
- **资源字典**：4 个；资源 key 24（Tokens）+ 10 命名样式（Brutalist）+ 7（DarkControls）+ 4（Overlays）。
- **本地化 catalog**：2 份内嵌 + 1 个外部覆盖目录。

---

## 12 WPF UI（二）：Game Lab、夜间模式、设置与本地化

本章覆盖 `src/Gloam/Features/GameLab`、`Features/NightMode`、`Features/Settings` 三个功能垂直切片，以及两个本地化项目 `src/Gloam.Localization`（引擎，平台中立）与 `src/Gloam.Localization.Wpf`（WPF 适配层）。Shell/Tray/Dashboard/Infrastructure 见第 11 章；Calibration 见第 13 章。

范围：本切片共 10 个 C#/XAML 文件（GameLab 4、NightMode 3、Settings 3）+ 本地化 19 个文件（引擎 13 含 csproj、适配 6 含 csproj）+ 2 份目录 JSON。行数：`GamerModeItem.cs` 999、`GameLabWindow.xaml` 772、`NightModeScheduleControl.xaml.cs` 744、`SettingsWindow.xaml` 473、`NightModeScheduleViewModel.cs` 466、`SettingsViewModel.cs` 461、`NightModeScheduleControl.xaml` 272、`GameLibraryCoordinator.cs` 174、`GameLabWindow.xaml.cs` 81。

### 12.1 Game Lab：一个 ViewModel，两个界面

#### 12.1.1 两个入口、一个 ViewModel

Game Lab 没有自己的 ViewModel。`GameLabWindow.xaml.cs:33-35` 与 `DashboardWindow.xaml.cs:37-39` 都直接 `new DashboardViewModel(...)`：

```csharp
// src/Gloam/Features/GameLab/GameLabWindow.xaml.cs
_viewModel = new DashboardViewModel(
    monitorManager, settingsManager, nightModeService, updateService,
    applyCallback, gamerApplyService, gamerModeCoordinator);
```

所以同一个 `DashboardViewModel`（含其 `GamerModeItem` 子对象）会以两种视图形态出现：

| 形态 | 宿主 | 说明 |
| --- | --- | --- |
| 嵌入式 | `DashboardWindow.xaml:337-983` 的 `Expander x:Name="GamerModeExpander"` | 与显示器卡片、夜间模式、melanopic 卡片共用一栏滚动 |
| 紧凑独立窗口 | `GameLabWindow.xaml`（`WindowStyle=None`、`AllowsTransparency=True`、`Topmost=False`、1040×660，`MinWidth=940 MinHeight=640`） | `Win+Shift+G` 打开；无窗口级 `ScrollViewer`，库与扫描列表各自虚拟化（`VirtualizingPanel.IsVirtualizing="True"` + `VirtualizationMode="Recycling"`） |

`GameLabWindow` 构造参数（`GameLabWindow.xaml.cs:20-28`）：`MonitorManager`、`SettingsManager`、`NightModeService`、`UpdateService`、`ApplyCalibrationRequest`、可选 `GammaApplyService`、可选 `suggestedGameApp`、可选 `GamerModeCoordinator`。窗口生命周期：

- `WindowBoundsPersistence.Attach(this, settingsManager, "GameLabCompact")`（:31）——键是字面量，不随语言变化；
- `_viewModel.SuggestGamerApp(suggestedGameApp)`（:36）把托盘记录的上一个前台外部进程预填进"添加游戏"输入框；
- `Activated += (_, _) => _viewModel.RefreshGamerChoices();`（:40）每次获得焦点都重扫运行中进程；
- `Loaded += (_, _) => ShowGamerControls();`（:41）在 `DispatcherPriority.Loaded` 上把焦点给 `GamerAppPicker` 或 `GameProfileSearchBox`；
- `Closed += (_, _) => _viewModel.Dispose();`（:42）——窗口拥有这个 ViewModel，必须由它释放；
- `PreviewKeyDown`：`Esc` 关闭窗口（:61-66）；
- 没有 `LanguageToggleButton`，只有主题切换按钮（:112-114），语言入口在 Dashboard/Settings 上。

#### 12.1.2 `GameLabWindow.xaml` 的五个区段

| 行段 | 区段 | 关键内容 |
| --- | --- | --- |
| 84-120 | 标题栏（46px 高） | `BrandMark` + `GLOAM` + `{loc:Tr dashboard.gameLab.title}` + `{Binding AppVersion}` + 硬编码热键提示 `"Win + Shift + G"` + 主题切换 `◐/◑` + 关闭 |
| 124-198 | 状态带 | 左：`GamerModeStateTitle`/`GamerModeStateDetails`；中：`GamerMode.ActiveSessionSummary`/`ActiveSessionDetails`（当前游戏回执）；右：`GameLabLibraryPresetPicker` + 应用按钮 + `ToggleGamerModeCommand`（`AutomationId=ToggleGamerMode`） |
| 201-642 | `LibraryWorkspace`（库 + 单个编辑器） | 左 258px 列：`GameProfileSearchBox`、`GameProfileList`（`ItemContainerStyle={StaticResource GameLibraryRow}`）、`GamerAppPicker`（可编辑 ComboBox）+ `AddGamerProfileButton`、Find Games（`ScanForGamesCommand`）+ `GamerMode.DiscoveryStatus`。右列：空态提示或 `ContentControl Content="{Binding GamerMode.SelectedProfile}"` 的编辑器模板 |
| 645-759 | `GameScanPanel`（扫描结果审阅） | `GamerMode.HasDiscoveryResults` 为真时**替换** `LibraryWorkspace`（两者双向 `DataTrigger` 控制 `Visibility`），而不是把窗口撑大；`DiscoveredGamesList` + `AddDiscoveredGamesCommand`（按钮文字来自 `GamerMode.AddDiscoveredButtonLabel`）+ "Back to library" |
| 761-768 | 页脚 | `GamerMode.EditorStatus` + 硬编码 Esc/安全模式提示 |

标题栏/状态带的 `AppVersion` 来自 `DashboardViewModel.AppVersion => _updateService.DisplayVersion`（`DashboardViewModel.cs:84`）。

编辑器模板（`GameLabWindow.xaml:392-639`，`ContentControl.ContentTemplate`）自上而下：游戏标题 + `AppName` + `ExecutableIdentityLabel` + `HdrSupportLabel`、`gamelab.auto.apply` 勾选（`Enabled`）、移除按钮、"choose a look" 四宫格（`AvailableQuickLooks`，`SelectedValue={Binding PictureIntent}`）、`IntentDescription`、`ShadowDetailPercent` 滑杆 + `ShadowDetailPlainLabel`、`GameplayLock`（"keep this look steady while I play"）、`IsNightOps` 为真时显示 `NightOpsKelvin` / `NightOpsStrengthPercent` 双滑杆，最后是 `Expander` "advanced signal & display controls"（展开状态绑定 `DataContext.IsGameProfileAdvancedExpanded`，见 `DashboardViewModel.cs:213-217`）。

`AdvancedControlsGrid`（:531-634）是 8 列 × 5 行的手工网格，命名控件与职责：

| 控件 | 绑定 |
| --- | --- |
| `AdvancedProfileNameBox` | `DisplayName` |
| `AdvancedHdrSupportBox` | `HdrCapability`（`AvailableHdrCapabilities`） |
| `AdvancedExpectedSignalBox` | `HdrExpectation`（`AvailableHdrExpectations`） |
| `AdvancedTargetDisplaysBox` | `DisplayScope`（`AvailableDisplayScopes`） |
| 第 1 行整宽 ComboBox | `MonitorDevicePath`，仅 `IsSpecificDisplay` 时可见，项源是窗口级 `GamerMode.AvailableDisplays` |
| `AdvancedNightBehaviorBox` | `NightPolicy`（`AvailableNightPolicies`） |
| `AdvancedGammaModeBox` + `gamelab.override` 勾选 | `GammaMode`，`IsEnabled={Binding OverrideGamma}` |
| `AdvancedPaperWhiteBox` / `AdvancedPeakNitsBox` | `PaperWhiteNits` / `PeakNits`（`StringFormat=F0`） |
| `AdvancedMelanopicCeilingBox` | `NightOpsMelanopicCeiling`，`IsEnabled={Binding IsNightOpsMelanopicCeilingEnabled}` |
| `ApplyWhileRunningBox` | `ApplyWhileRunning` |

窗口内定义了本地样式 `GameLibraryRow`（:25-51）与 `QuickLookRow`（:53-78）：选中项用 `ThemeAccent` 描边 + 左侧 3px 加粗、`ThemeHover` 悬停；这是"选中 = 左侧刻度"的布鲁塔利特视觉语言，模板里没有圆角。

#### 12.1.3 `GamerModeItem`：42KB 的 item VM 拆成四层

`src/Gloam/Features/GameLab/GamerModeItem.cs` 一个文件装了四个类型：

1. `GamerModeItem`（:15-488，`sealed ObservableObject`）——长生命周期的 Game Lab 状态容器；
2. 三个值类型：`GamerDisplayOption`（:490）、`GamerChoice<T>`（:495）、`GamerLibraryPresetOption`（:503）；
3. `DiscoveredGameItem`（:511-539）——扫描结果的勾选行；
4. `GamerProfileEditorItem`（:542-998，`ObservableObject, IDisposable`）——单条 `GamerProfileRule` 的可编辑包装。

集合（:18-23）：`Profiles`、`RunningApps`、`AvailableDisplays`、`DiscoveredGames`、`FilteredProfiles`、`FilteredDiscoveredGames`；另有 `_runningAppPaths` 字典（:24-25）保存 appName→可执行路径。`RecentProfileLimit = 5`（:17）。

`GamerModeItem` 的构造（:42-72）第一次触到本地化：占位文案（`gamelab.waitingForGame`、`gamelab.switchToGame`、`gamelab.editorStatus`、`gamelab.discoveryNote`）是**字段**，所以语言切换时必须重写字段再 `OnPropertyChanged(string.Empty)`：

```csharp
loc.CultureChanged += (_, _) =>
{
    if (!HasActiveSession) { _activeSessionSummary = loc.Get("gamelab.waitingForGame"); ... }
    _editorStatus = loc.Get("gamelab.editorStatus");
    _discoveryStatus = loc.Get("gamelab.discoveryNote");
    OnPropertyChanged(string.Empty);
};
```

同一文件里 `Loc.Get(...)` 与 `LocalizedStrings.Default.Localizer.Get(...)` 两种写法混用（如 `:76-77` 与 `:84-85`），语义相同（`Loc.Instance` 就是 `LocalizedStrings.Default.Localizer`，见 `Loc.cs:19`）。

计算属性（:74-119）全部是"读时求值"，因为标签本身来自目录：`HasProfiles`、`ProfileCountText`（单/复数走 `gamelab.gameCount.one` / `.other`）、`HasDiscoveryResults`、`FilteredProfileCountText`、`ShowAllProfilesLabel`、`AvailableLibraryPresets`（四条：准确/竞技/夜间/影院校准 HDR）、`SelectedDiscoveryCount`、`AddDiscoveredButtonLabel`、`ClearProfilesLabel`。

会话回执 `SetSessions`（:243-278）把 Core 的 `GamerSessionSnapshot` 列表压成两行文本：第一行 `$"{title} is active"` / `$"{title} is active on {sessions.Count} displays"`（**硬编码英文**），第二行按显示器拼 `意图 · gamma · 阴影 · 夜间策略` 与诊断等级（`CHECK`/`WARN`/`INFO`，:468-473）。

发现结果 `SetDiscoveryResults`（:280-301）先保存当前勾选的 `ExecutablePath` 集合，重建列表时恢复，避免一次重扫清掉用户的选择。两处破坏性操作做二段确认：`ArmLargeAddConfirmation`（>20 个才允许"确认添加 N 个"，:303-317）与 `ToggleClearProfilesConfirmation`（清空库，:319-337）；两者的按钮文字都是计算属性。

`RefreshProfileFilter`（:372-416）是这个小节最值得记的一段：三种模式（有搜索词→按 `GameTitle`/`AppName` 过滤；`_showAllProfiles`→全量按标题排序；否则→最近使用的前 5），并且**永远把当前选中项塞回可见列表首位**，否则"添加成功但编辑器看起来没变"。

`GamerProfileEditorItem`（:542-998）的要点：

- 构造时 `profile.Sanitized()`（:549），`ToRule()` 再 `Sanitized()` 一次（:968）——UI 永远不直接持有脏规则；
- 订阅 `CultureChanged` 并在切换时**重设六个枚举值**（:559-588）：选项列表每次读都重建，`ItemsSource` 替换瞬间 ComboBox 的 TwoWay 绑定会把 null 写回 ViewModel，所以必须显式重新赋值；
- 七个 `Available*` 计算列表（PictureIntents、QuickLooks、NightPolicies、HdrExpectations、HdrCapabilities、DisplayScopes、GammaModes），全部 `Loc.Get` 组装，不缓存（:596-641）；
- 数值属性自己 clamp：`ShadowDetailPercent` 0-100 ↔ `ShadowDetailStrength` 0-1（:847-860）、`NightOpsKelvin` 1900-6500（:887-900）、`NightOpsStrengthPercent` 默认 0.55（:902-912）、`PaperWhiteNits` 0-1000、`PeakNits` 0-10000（即 10000 nits，:956-966）；
- `PictureIntent` 的 setter 调 `GamerPresetCatalog.Apply(_profile, value)` 后 `RaiseAllRenderingProperties()`（:738-747、:970-990）——预设会一次改很多字段，必须整体通知，否则界面只更新一半；
- `EffectSummary`（:698-714）把当前动作压成 `gamma · shadows · night` 一行；`Dispose` 只解绑 `CultureChanged`（:590），由 `GamerModeItem.OnProfilesChanged` 在移除时调用（:350）。

#### 12.1.4 `GameLibraryCoordinator`：后台进程枚举与库扫描的生命周期

`src/Gloam/Features/GameLab/GameLibraryCoordinator.cs`（174 行，`internal sealed ... IDisposable`）是"耗时的、可被打断的、结果可能过期"的那一半。它被 `DashboardViewModel` 持有（`DashboardViewModel.cs:79`：`private readonly GameLibraryCoordinator _gameLibrary = new();`）。

```csharp
internal sealed record RunningAppScan(
    IReadOnlyList<string> AppNames,
    IReadOnlyDictionary<string, string> ExecutablePaths);
```

两条任务线各有独立的世代计数：

| 方法 | 线程模型 | 过期保护 |
| --- | --- | --- |
| `RefreshRunningAppsAsync()`（:44-52） | `Task.Run(ScanRunningApps)` | `_runningAppGeneration` 递增；返回前比对代际，过期返回 `null` |
| `ScanLibrariesAsync(progress)`（:54-97） | `Task.Run(() => _scan(token, guardedProgress), token)` | 先 `CancelLibraryScan()` 取消上一次，再新建 `CancellationTokenSource`；`guardedProgress` 同时校验 `_disposed`、`token.IsCancellationRequested`、世代 |
| `CancelLibraryScan()`（:99-109） | 同步，持 `_discoveryLock` | 递增世代 + `Cancel()`，与异步宿主的 `finally` 里的 `Dispose` 串行化 |

`ScanRunningApps`（:111-161）只保留**有可见顶层窗口**的进程：`process.MainWindowHandle == IntPtr.Zero || string.IsNullOrEmpty(process.MainWindowTitle)` 直接跳过，`appName = process.ProcessName.ToLowerInvariant() + ".exe"`，路径优先取 `process.MainModule?.FileName`（受保护进程会抛，走 `Log.DebugRateLimited` 降噪，冷却 5 分钟）。结果按名字排序后返回。

真正的库扫描实现是 Core 的 `GameDiscoveryService.Scan`（:24、:35），协调器只负责取消、代际与线程——这是本仓库"UI 只做生命周期、领域逻辑在 Core"的一个正例。

#### 12.1.5 与 Core 的调用方向

Game Lab 的 UI 层不直接写 GPU，也不自己判断前台窗口。所有跨层调用都是单向的（UI → Core）：

```
DashboardViewModel / GamerModeItem
        │  读/写
        ├─► SettingsManager.TrySetGamerProfiles / GamerProfiles / GamerSettingsChanged / GamerProfileUsed
        ├─► GammaApplyService.ActiveGamerSessions / GamerSessionsChanged   （只读快照）
        ├─► GamerModeCoordinator.TrySetEnabled / TrySetProfiles / LastExternalForegroundApp / PolicyChanged
        ├─► GameDiscoveryService.Scan / DetectHdrCapability
        ├─► GamerPresetCatalog.Create / Apply
        ├─► GamerExecutableSafety.IsSafeProfileTarget / RejectionReason
        └─► AppExclusionRule.NormalizeAppName
```

`GamerModeCoordinator` 由 Tray 层构造并注入（`TrayViewModel.cs:111-118`），Game Lab 只是它的消费者：`IsGamerModeEnabled` 的 getter 优先读协调器（`DashboardViewModel.cs:153-169`）。协调器缺失时（单测、无容器构造）回退到 `SettingsManager.TrySetGamerModeEnabled`——这套"可选协作者 + 回退"写法在本切片出现三次（另两处是 `GammaApplyService?` 与 `ILocalizer?`）。

自动保存路径：任何 profile 编辑（`OnGamerProfileEdited`，:1147-1152）→ `QueueGamerProfileSave()` 启动 180ms `DispatcherTimer`（:287-295）→ `SaveGamerProfiles()`（:1170-1191）。写库时若有协调器就走 `TrySetProfiles`，否则直接 `SettingsManager.TrySetGamerProfiles`；结果立刻回显到 `GamerMode.EditorStatus`（`ShowSaved`/`ShowSaveFailed`）。`_suppressGamerProfileSave` 用来在批量操作（扫描回填、库预设、清空）期间抑制这条链。

### 12.2 夜间模式：画布式时间线编辑器

#### 12.2.1 它不是一个用户控件，而是一个"半个 View"

`Features/NightMode` 只有三个文件，但职责划分很反常，值得记录：

| 文件 | 行数 | 职责 |
| --- | --- | --- |
| `NightModeScheduleControl.xaml` | 272 | 布局：摘要/定位行、180px 画布容器、工具栏、渲染选项、DataGrid 行编辑 |
| `NightModeScheduleControl.xaml.cs` | 744 | 画布绘制、命中测试、拖拽、缩放、地理定位请求、节流 |
| `NightModeScheduleViewModel.cs` | 466 | 行集合、位置文本、渲染参数、拖拽浮层状态（**不含**画布几何） |

类注释明确承认这个分工（`NightModeScheduleViewModel.cs:12-17`）："The canvas drawing, timeline drag handling and geolocation detect flow stay in the control (they are entangled with hit testing and canvas geometry)"。这就是本章"本地化不变量"一节的例外来源——文件里仍有硬编码英文。

控件对外只有三个门面（`NightModeScheduleControl.xaml.cs:16-19`）：

```csharp
public NightModeScheduleViewModel Vm { get; } = new NightModeScheduleViewModel();
public event Action? ScheduleChanged;                        // 控件 → 窗口：该保存了
public event Func<int?, Task>? PreviewTemperatureRequested;  // 控件 → 窗口：实时预览这个色温
public void Initialize(NightModeSettings settings);
public void SyncRenderingSettings();
```

`Initialize` 由宿主传入**同一个** `NightModeSettings` 实例（`DashboardWindow.xaml.cs:66`：`ScheduleEditor.Initialize(_viewModel.EditingNightMode)`），控件与 ViewModel 都是就地改写它，再由 `ScheduleChanged` 回到 `DashboardViewModel.SaveEditedNightMode()` 落盘。预览走 `PreviewTemperatureRequested`，`DashboardWindow` 接到后调 `DashboardViewModel.PreviewTemperatureAsync(kelvin)`（:482-497），后者在 `Task.Run` 里对每台显示器套一次 gamma——所以拖动时不会卡 UI 线程。

#### 12.2.2 XAML 的五行布局

`Grid.RowDefinitions`（`NightModeScheduleControl.xaml:20-26`）：摘要+定位（Auto）、图表（固定 180）、工具栏（Auto）、渲染算法（Auto）、列表（Auto）。关键点：

- **图表是双层 Canvas**：`GridCanvas`（`IsHitTestVisible="False"`，只画网格）+ `CurveCanvas`（`ClipToBounds`、`Background="Transparent"`、接收三个鼠标事件，:57-62）。透明背景是必须的，否则没有命中区域。
- `DragOverlay`（:65-75）是画布内的浮层 Border，`IsHitTestVisible="False"`，显示 `OverlayTimeText`/`OverlayTempText`，显隐由 `Vm.IsDragOverlayVisible` 驱动。
- 工具栏（:80-98）：Add point / Reset default / `UseUltraWarmMode` 勾选（`IsEnabled={Binding IsUltraWarmApplicable}`）；右侧文案里 `" - Enhanced warmth below 2800K"` 是硬编码。
- 渲染算法（:100-161）：`AvailableAlgorithms` ComboBox（`DisplayMemberPath="Label"`、`SelectedValuePath="Value"`）、`AlgorithmDescription`、仅在 `IsPerceptualSelected` 时显示的强度滑杆（`PerceptualStrengthPercent`）、仅在 `IsPreserveLuminanceApplicable` 时显示的 `PreserveLuminance`、以及 `DoseCeilingEnabled` + `DoseCeilingText`（mel-lx）。
- 列表（:164-271）是 `DataGrid`，五列：`TriggerType`（编辑态是 `ComboBox ItemsSource={RelativeSource AncestorType=UserControl, Path=Vm.TriggerTypes}`）、`DisplayTime`、`TargetKelvin`、`FadeMinutes`、删除按钮。`DataGridCell` 被重写模板成"无边框、选中即 `ThemeAccent` 实心"，因为内置 `DataGrid` 皮肤和布鲁塔利特语言不兼容。

#### 12.2.3 画布几何：4:00 起点、24 小时、温度对数轴

常量在控件里：`GraphStartHour = 4`（:48）——时间线从凌晨 4 点起算，这样"日落→日出"整段落在画布中段，跨午夜不需要折返。坐标换算四个纯函数（:357-387）：`TempToY/TimeToX/XToTime/YToTemp`，其中 `TimeToX` 以 `GraphStartMins` 为原点、按 1440 分钟归一化。

绘制分两步，各自节流：

- `DrawGrid()`（:161-214）：清空 `GridCanvas`，每 4 小时一条竖线，午夜线用 `ThemeText`@0.45 强调，标签用 `ThemeTextDim`@0.30；尺子颜色每次绘制时从 `BrutalistTheme.Color(key)` 现取，所以 `BrutalistTheme.Changed` 必须触发重绘（:60-71，重绘前 `CurveCanvas.Children.Clear()`，因为节点/now-line 的 brush 也需要重建）。
- `DrawCurve()`（:215-356）：画曲线 + 可拖节点 + "now" 标记；`DrawThrottleMs = 16`（约 60fps），拖动时用 `_drawPending` 合并重入。

#### 12.2.4 交互：拖拽即预览，预览要节流

鼠标路径（`NightModeScheduleControl.xaml.cs:389-441`、`:442-576`）：`Node_MouseDown` 记录 `_dragItem` 与偏移 → `GraphCanvas_MouseMove` 命中拖动时把 `SchedulePointViewModel.Model.Time/TargetKelvin` 直接改掉（不经过属性 setter，所以之后要 `RefreshDisplay()` 手动通知，:410-414）→ `UpdateOverlay` 跟随鼠标 → 每 `PreviewThrottleMs = 250`（4fps，:43）发一次 `StartPreview(kelvin)`；`MouseUp` 收尾并 `Vm.RefreshPoints()`。

两处易错点已经在代码里解决：

- `StartPreview`（:28-35）用 `SafeAsync.FireAndForget` 包住 `PreviewTemperatureRequested`，而不是 `_ = handler.Invoke(...)`；后者会把故障 Task 静默丢弃，"预览不再生效"将既无日志也无症状。
- `_isPreviewRunning`（:45）防止预览叠加：上一次硬件调用未回到前不再发新的。

双击画布空白处添加点（`GraphCanvas_MouseDown`），`AddPoint_Click`（:584-601）与 `Reset_Click`（:603-610）；上限 12 个点（:586 显式判断），超限时用**原生 `MessageBox.Show`**（:588）配 `nightmode.maximum.of.12.schedule.points.allowed` / `nightmode.limit.reached` 两个目录键——这一点与仓库"禁止原生 MessageBox、统一走 `Infrastructure/Dialogs`"的约定不一致（见 12.8 R13）。删除行反而正确：`ShowDeleteConfirmation`（:627 起）自己搭了一个带主题的 `Window`，文案 `nightmode.deletePointConfirm` 带 `{time}` `{kelvin}` 两个占位符。

#### 12.2.5 地理定位：唯一一处 UI 直连网络的代码

`Detect_ClickAsync`（:107-152）用 `HttpClient`（`Timeout=5s`）请求 `http://ip-api.com/json/?fields=lat,lon`，再用正则 `"lat":([\d.-]+)` / `"lon":([\d.-]+)` 抽值。两个刻意的细节：

- 解析固定用 `CultureInfo.InvariantCulture`——JSON 是 invariant 格式，逗号小数区域会把 `40.67` 读成 `4067`；
- 拿到坐标后调 `_settings.ConvertSimpleScheduleToSunTriggers()`（:136），把"固定时间点"整表转换成"日出/日落 ± 偏移"，再 `RefreshPoints()` + 重绘。

这是本切片唯一的出网代码，也在 UI 项目里——和"领域逻辑不进 UI"的规则相悖，但没有更好的归宿（它服务于编辑器交互）。

#### 12.2.6 `NightModeScheduleViewModel`：设置的就地编辑代理

它不持有 `NightModeService`，只持有一个 `NightModeSettings` 实例（:20），由 `Initialize` 注入。写回完全依赖两个事件：

```csharp
public event Action? SettingsEdited;   // 渲染参数变了 → 控件 → ScheduleChanged → DashboardViewModel.SaveEditedNightMode
public event Action? PointEdited;      // 某个时间点变了 → 控件重绘曲线
```

属性都是"改字段 + 写 `_settings` + 触发 `SettingsEdited`"的三段式：`UseUltraWarmMode`（:49-62）、`Algorithm`（:69-86）、`PreserveLuminance`（:91-104）、`PerceptualStrengthPercent`（0-100 ↔ 0-1，:157-171）、`DoseCeilingEnabled`/`DoseCeilingText`（:112-151，写 `MelanopicEdiCeiling`，0 表示关闭，经 `NightModeSettings.ClampMelanopicCeiling`）。

三条互斥可见性规则是**计算**出来的，不是三份状态：`IsUltraWarmApplicable => _algorithm == NightModeAlgorithm.Standard`（:89）、`IsPreserveLuminanceApplicable => _algorithm != NightModeAlgorithm.UltraNight`（:110）、`IsPerceptualSelected`（:154）。

`Initialize`（:200-217）除注入实例外还做三件事：回填经纬度文本、`SyncRenderingSettings()`（把持久化值读进字段并批量 `OnPropertyChanged`，**不**触发保存）、`settings.EnsureSchedule(Latitude, Longitude)` 后 `RefreshPoints()`。

`RefreshPoints()`（:276-287）按 `p.GetTimeOfDay(Latitude, Longitude)` 解析出的实际时刻排序——日照触发点的时间依赖纬度与日期，排序必须在解析后做。

`CommitLocation()`（:247-259）由 XAML `LostFocus` 调用；`TryParseCoordinate`（:269-273）先 invariant 后当前文化，且 `NumberStyles.Float` **故意排除** `AllowThousands`——否则 invariant 分支会把 `"40,67"` 读成 4067。

`SchedulePointViewModel`（:296-423）是行 VM：`DisplayTime` 的 get 端按触发类型返回 `"23:00"` 或 `"+45m"`，set 端用 `ParseTimeInput` 接受 `"23:00"/"2300"/"23"/"9:30"/"930"/"9"` 六种写法（:357-392）；`SuppressNotifications` 供拖动期间抑制反复保存。

`NightModeAlgorithmOption`（:429-465）是四个算法的选择项；`DefaultOptions` 是**每次访问重建**的静态属性（不是静态字段），理由写在注释里：缓存会把标签冻结在首次访问时的语言。`ToString()` 覆写成返回 `Label`，因为主题里的 ComboBox 模板用 `ToString()` 渲染折叠态。

### 12.3 设置：每显示器配置的唯一写入口

#### 12.3.1 三个调用方

`SettingsWindow` 没有 DI 注册，全部由调用方 `new` 出来再 `ShowDialog()`：

| 调用方 | 位置 | 传入的 apply 回调 |
| --- | --- | --- |
| 托盘每显示器子菜单 | `MonitorViewModel.OpenSettings()`（`MonitorViewModel.cs:108-126`） | `OnApplyWithCalibration` → 托盘集中式 `RequestApply`（会叠加夜间模式） |
| Dashboard 卡片 "Configure" | `DashboardWindow.OnConfigureRequested`（`DashboardWindow.xaml.cs:107-126`） | `_viewModel.ApplyFromSettings` |
| 单显示器重载 | `SettingsWindow(MonitorInfo, SettingsManager, ...)`（:55-61） | 同上，`allMonitors` 只有一个元素 |

`SettingsWindow.xaml.cs:26` 的 `WindowBoundsPersistence.Attach(this, settingsManager, "MonitorSettings")` 用字面量键——与 `DashboardWindow` 的本地化键形成对照（见 12.7 R2）。

#### 12.3.2 `SettingsWindow.xaml` 的分区

44px 自绘标题栏 + 三段 Grid（监视器选择器 / `ScrollViewer` 内容 / 页脚）；`ScrollViewer` 滚动条被隐藏（`VerticalScrollBarVisibility="Hidden"`，:201），长语言下的裁剪由布局测试兜底。

| 行段 | 卡片 | 控件与绑定 |
| --- | --- | --- |
| 208-218 | Interface language | `Languages` / `SelectedLanguage`（`DisplayMemberPath="DisplayName"`）；全窗口唯一"非显示器属性"的设置 |
| 221-230 | Gamma | ComboBox `SelectedIndex={Binding GammaModeIndex}`，三个 `ComboBoxItem` 用 `{loc:Tr settings.gamma.*}` |
| 233-257 | Brightness | `BrightnessSlider`(10-100, `TickFrequency=1`) → `Brightness`；`ModernToggle` 复选框 `EnhanceShadows`（底层是 `UseLinearBrightness` 取反）；100/75/50 快捷档走 `SetBrightnessCommand` + `CommandParameter` |
| 260-309 | Color temp / Tint | `TemperatureSlider`、`TintSlider`（-50..50），带 `behaviors:SliderDetent`（`DetentValue=0`、`SnapThreshold=2`）与 `behaviors:ClickToEdit.TargetSlider` 把读数变成可点输入框 |
| 312-369 | Night override / Ultra Night | `TempOffsetSlider`(-20..20)；`NightSpectrumLabel` + `FindNightSpectrum_Click` 打开 `CcssBrowserWindow`；`ClearNightSpectrumCommand`，可见性绑 `HasNightSpectrum` |
| 372-433 | RGB fine tuning（默认折叠 `Expander`） | `Red/Green/BlueGain` 0.5-1.5、`TickFrequency=0.01`、detent 1.0、`SnapThreshold=0.03`；`ResetRgbCommand` |
| 442-469 | 页脚 | `IsLivePreview` 勾选；Apply（仅 `IsLivePreview=False` 可见）、Compare（按住）、Reset all、Save & Close |

四个滑杆样式在窗口资源里手写（`ModernSlider`/`CenteredSlider`/`RgbSlider`），控件模板依赖 `PART_Track` 命名约定（:32、:59、:86）。

#### 12.3.3 `SettingsViewModel`：pending changes + 实时预览 + 关闭回滚

```csharp
private readonly Dictionary<string, MonitorProfileData> _pendingChanges = new();  // :40 每显示器一份待提交配置
private MonitorProfileData _profile;        // 当前编辑的显示器
private MonitorProfileData? _savedProfile;  // Compare 用
private bool _livePreviewApplied;           // 关闭时是否要回滚
private bool _savedAndClosed;               // SaveAndClose 已执行
```

- `Monitors` 是 `ObservableCollection<MonitorChoice>`；`MonitorChoice` 含 `Model` 与 `Label = $"{FriendlyName} (HDR|SDR)"` 并覆写 `ToString()`（同 ComboBox 折叠态原因）。切换 `SelectedMonitor` 先 `SaveCurrentToPending()` 再 `LoadMonitorProfile()`（:142-157）。
- 滑杆 setter 一律"取整/取两位 + `SchedulePreview()`"；`SchedulePreview`（:358-379）复用单个 `DispatcherTimer` 合并 100ms（`PreviewDebounceMs`），`_isLoading` 期间静默。
- `ApplyCurrent()`（:381-385）是唯一把值送到 GPU 的地方：`_applyCallback(_currentMonitor, _profile.GammaMode, _profile.ToCalibrationSettings(), null)`——第四个参数 `nightKelvinOverride = null` 表示"不覆盖夜间模式"。
- `CompareStart/CompareEnd`（:388-397）：按下用 `_savedProfile` 覆盖、抬起回到编辑值——按住对比，没有撤销栈。
- `SaveAndClose`（:408-432）：`SaveCurrentToPending()` → 逐条 `SettingsManager.SetMonitorProfile(key, value)` → 逐条 `_applyCallback` → `CloseRequested`。
- `Cleanup()`（:442-459）由窗口 `Closed` 调用：若关窗时既没保存又已预览过，就把每个被改动显示器的**已持久化**配置重新应用一次，否则最后一次预览会留在 GPU ramp 上。这是"用 X 关闭设置窗"的正确语义，也是最容易漏掉的一环。

窗口侧只剩三件事（`SettingsWindow.xaml.cs`）：`LanguageService` 解析、Compare 手势、`.ccss` 浏览。

```csharp
// :31-42 —— XAML 无法注入，只能从组合根取；取不到就退化构造一个不落盘的实例
LanguageService? languageService = App.Services?.GetService(typeof(LanguageService)) as LanguageService;
if (languageService is null)
    languageService = new LanguageService(LocalizedStrings.Default.Localizer, _ => { },
        followSystemLabelKey: "settings.language.followSystem");
```

`FindNightSpectrum_Click`（:88-102）打开 `CcssBrowserWindow`（`typeFilter: "ccss"`，`introText` 为硬编码英文），选中后 `_viewModel.SelectNightSpectrumPath(path)`；后者校验后缀并调用 `CgatsValidator.IsValidFile(path, "ccss")`（`SettingsViewModel.cs:305-321`），不合法只记日志并忽略。

### 12.4 `Gloam.Localization`：平台中立的本地化引擎

`Gloam.Localization.csproj` 的注释把边界写死了："Deliberately platform-neutral: no WPF, no System.Windows, no registry"（`TargetFramework` 为 `net10.0`，无 `UseWPF`）。所以这个库能被 UI、`Gloam.Cli` 和测试同时消费。

| 文件 | 行数 | 职责 |
| --- | --- | --- |
| `ILocalizer.cs` | 75 | 只读翻译门面：`Culture`、`AvailableCultures`、`Get/Format/Plural/TryGet`、`SetCulture`、`CultureChanged`、`MissingKeys`；文件里还定义了 `CultureChangedEventArgs(Previous, Current)` |
| `Localizer.cs` | 224 | 唯一实现：有序目录链查找、模板渲染、缺键计数、文化切换 |
| `StringCatalog.cs` | 111 | 不可变目录（`StringCatalog`）、条目（`CatalogEntry`）、`PluralCategory` 枚举 |
| `CatalogSources.cs` | 266 | `ICatalogSource` 与 `EmbeddedCatalogSource`/`DirectoryCatalogSource`/`InMemoryCatalogSource`/`CompositeCatalogSource`，加合并结果 `StringCatalogSet` |
| `CatalogJson.cs` | 211 | JSON 线格式读写 + `CatalogFormatException` |
| `CultureChain.cs` | 99 | 标签宽松解析 `TryParse`、回退链 `Build`、`RequiresCjkFonts`、`SourceCulture = en-US` |
| `PluralRules.cs` | 94 | CLDR 基数规则 `Select` + `RequiredCategories` |
| `MessageTemplate.cs` | 142 | `{name}` 占位符渲染与提取、`{{`/`}}` 转义 |
| `CatalogValidator.cs` | 154 | 跨语言校验（5 类问题） |
| `MissingKeyReport.cs` | 49 | 线程安全缺键计数（默认上限 500） |
| `SourceAudit.cs` | 294 | 源码审计：找出硬编码 UI 文本与被引用的键 |
| `LocalizationOptions.cs` | 41 | `SourceCulture`、`ExternalCatalogDirectory`、`MissingKeyBehavior`、`MaxTrackedMissingKeys` |

#### 12.4.1 一次 `Get` 的真实路径

```
Localizer.Get(key)
  └─ TryResolve(key, out entry)                 // Localizer.cs:158-173
       chain = Volatile.Read(ref _chain)       // 切换语言时整条链换引用，读端不加锁
       for i in chain: chain[i].TryGet(key)    // 命中即返回 CatalogEntry
  ├─ 命中 → entry.SelectTemplate(null)          // 复数条目回退到 Other
  └─ 未命中 → MissingKeys.Record(key)
              → MissingKeyBehavior == ReturnEmpty ? "" : key   // 默认返回 key 本身
```

链的构造在 `StringCatalogSet.Resolve`（`CatalogSources.cs:238-265`），顺序是：文化链（`zh-CN → zh-Hans → zh`）、再补任何"同语言"的目录、最后 `SourceCulture`。这就是 Windows 报 `zh-CN` 而只发 `zh-Hans` 仍出中文的原因。

并发模型（`Localizer.cs:9-12`）：目录链是单一引用，`SetCulture` 先在外层 `_catalogs.Resolve` 构建新链，再在 `_chainLock` 内同时 `Volatile.Write` 文化与链引用——"读端永不加锁，且不会看到新文化配旧链"。`SetCulture` 成功后 `MissingKeys.Clear()`：缺键是"每种文化各自的信号"。

`Plural(key, count, args)` 与 `Format(key, args)` 的区别只在于复数类别从哪来：`Format` 从参数表里找名为 `count` 的项（`ExtractCount`，:203-223），`Plural` 用显式传入的 `count`——这样调用方可以对一个不参与插值的数量做复数选择。

#### 12.4.2 目录模型：不可变 + 后源覆盖

`StringCatalog` 持 `ImmutableDictionary<string, CatalogEntry>`，构造后不可变——"切换语言时换引用，已经拿到旧目录的读者继续读一致数据"（`StringCatalog.cs:79-83`）。`CatalogEntry` 是"单模板"或"复数表单"的二选一（`SelectTemplate`，:61-72）：缺具体类别时回退 `Other`，再退第一个表单，符合 CLDR 只给 `other` 也算合法的约定。

`StringCatalogSet.Load`（`CatalogSources.cs:176-222`）按来源顺序合并，**后源逐键覆盖前源**，因此 `CompositeCatalogSource(embedded, directory)` 就实现了"内嵌基线 + 磁盘覆盖"。合并时 `Dictionary` 用 `StringComparer.OrdinalIgnoreCase` 存文化名、用 `Ordinal` 存键——键是精确匹配的。

`EmbeddedCatalogSource` 扫的是资源名前缀 + `.json` 后缀；应用侧的实例化在 `App.BuildLocalizer()`（`App.xaml.cs:69-83`），前缀写死为 `Gloam.Resources.i18n.`，对应 csproj 的 `<EmbeddedResource Include="Resources\i18n\*.json" />`（`Gloam.csproj:91`）。

`DirectoryCatalogSource`（:72-126）的容错语义值得单独记：目录不存在 → 返回空列表（该目录本来就是可选的）；单个文件 `CatalogFormatException` → 收集进 `Errors` 后跳过，绝不抛出；`IOException`/`UnauthorizedAccessException` 在枚举阶段被吞掉并记一条错误。这就是"用户可控输入不得让启动失败"的落点。

#### 12.4.3 JSON 线格式（`CatalogJson`）

顶层只有两个必需字段：`culture`（字符串，经 `CultureChain.TryParse` 解析，失败即 `CatalogFormatException`）与 `strings`（对象）。值可以是字符串，也可以是复数对象（键必须是 `zero/one/two/few/many/other` 之一，否则抛）。解析选项刻意宽松：`AllowTrailingCommas = true`、`CommentHandling = Skip`、`MaxDepth = 32`；写入时 `JavaScriptEncoder.Create(UnicodeRanges.All)`，避免中文被转成 `\uXXXX` 让 diff 不可读（:46-51）。`Serialize` 还会写 `$comment` 说明占位符语法，输出按 `Ordinal` 排序，便于评审。

#### 12.4.4 复数、模板与校验

`PluralRules.Select`（:19-72）是手写表，不是 CLDR 数据文件：`zh/ja/ko/vi/th/id/ms` 恒为 `Other`；`fr/pt` 的 0/1 算 `One`；`ru/uk/pl` 实现 one/few/many 并带 11-14 例外；`ar` 实现全部六类；其余走英文规则。`RequiredCategories` 给校验器用，不列出的语言按 `{One, Other}` 要求。

`MessageTemplate` 只支持一种语法：`{identifier}`（首字符非数字，仅字母/数字/下划线，长度 ≤64）+ `{{`/`}}` 转义。刻意不用 ICU MessageFormat（:9-15）。未解析的占位符**原样保留**并回传 `unresolved`——"界面显示 `{deviceName}` 是明显 bug，会被修；静默丢掉则读起来像设计如此"。

`CatalogValidator.Validate(catalogs, sourceCulture)`（:44-74）对每个非源语言目录报五类问题：`MissingKey`、`OrphanTranslation`、`PlaceholderMismatch`（源有翻译无、翻译有源无，双向都报）、`MissingPluralForm`（源是复数而译文是单串，或缺该语言的必需类别）、`EmptyValue`。输出按 `Culture → Kind → Key` 稳定排序，方便 CI diff。

`MissingKeyReport`（49 行）是 `ConcurrentDictionary<string,int>` + 上限：已有键在达到上限后仍继续计数，只有**新**键被丢弃——"失控循环不能变成无界内存增长"。

### 12.5 `Gloam.Localization.Wpf`：为什么适配层必须极薄

csproj 注释同样把边界写死："The adapter is deliberately tiny: it turns the platform-neutral localizer into something XAML can bind to. Anything that is not WPF plumbing belongs in Gloam.Localization."（`net10.0-windows` + `UseWPF`）。五个源文件：

| 文件 | 行数 | 职责 |
| --- | --- | --- |
| `LocalizedStrings.cs` | 90 | 绑定源：索引器 `this[string key]` + `PropertyChanged("Item[]")` + 静态 `Default` + 启动前的 `NullLocalizer` |
| `TrExtension.cs` | 42 | XAML markup extension `{loc:Tr some.key}`，`ProvideValue` 返回 `Binding` 而非字符串 |
| `Loc.cs` | 27 | 静态门面 `Loc.Get/Format` → `LocalizedStrings.Default.Localizer`，给拿不到注入的窗口/静态帮助类用 |
| `LanguageService.cs` | 202 | 语言选择器 + 切换 + 持久化回调 + `LanguageOption` |
| `CultureFontPolicy.cs` | 86 | CJK 文化的字体令牌替换 |

#### 12.5.1 `{loc:Tr}` 为什么必须返回 `Binding`

```csharp
// TrExtension.cs:34-40
var binding = new Binding($"[{Key}]") { Source = LocalizedStrings.Default, Mode = BindingMode.OneWay };
return binding.ProvideValue(serviceProvider);
```

如果 `ProvideValue` 直接返回解析好的字符串，文本就在加载时被冻结——每次切换语言都要重建窗口，正是 `.resx` 方案被否掉的第二个理由（`docs/localization.md:10-22`）。返回 `Binding` 后，一次语言切换只要 `LocalizedStrings.Refresh()` 抛 `PropertyChanged("Item[]")`，所有绑定元素就地重解析，窗口保持打开、滚动位置与模态状态不丢。

`LocalizedStrings` 在构造时订阅 `_localizer.CultureChanged`（:30），因此 `Default` 一旦在启动时被赋成真实实例（`App.xaml.cs:140`），后续切换自动生效。启动前 `Default` 是 `new LocalizedStrings(NullLocalizer.Instance)`（:23），`NullLocalizer.Get(key) => key`——设计期预览和"本地化初始化失败"两种情况下都显示能自我说明的键名，而不是空白。

#### 12.5.2 `LanguageService`：决定"当前是哪种语言"

构造参数刻意只要两样：`ILocalizer` 和一个 `Action<string?> persist` 回调（:56）。持久化是回调而不是依赖——设置存储属于 `Gloam.Core`，适配层不该知道它；应用侧传的是 `tag => settings.SetLanguage(tag)`（`App.xaml.cs:58-60`），`null` 表示"跟随 Windows"。

- `Options`（:70）：`Rebuild()` 把"跟随 Windows"放第一位，其余按 `AvailableCultures` 的原生名（`NativeName`）列出；`ShortLabel` 是两字母大写码，给标题栏语言按钮用。
- `ApplyStartup(savedTag)`（:78-89）：解析目标文化 → `Apply` → 用 localizer **实际**服务的文化回填选中项（`zh-CN` 请求可能落到 `zh-Hans` 目录）。若用户选的是"跟随系统"，保持该条目而不是反推成具体语言。
- `NextOption()`/`Toggle()`（:95-121）：在具体语言之间循环而非二选一，加第三种目录时不用改代码；单语言发行时返回 `null`，标题栏按钮自行隐藏。
- `Select(option)`（:127-141）：应用 + 把 `option` 本身记为选中（不从解析结果反推，否则"跟随 Windows"会在系统语言恰好命中目录时被静默钉死）+ `_persist` + `LanguageChanged`。
- `Apply(culture)`（:157-167）：`CultureInfo.CurrentUICulture`、`DefaultThreadCurrentUICulture`、`_localizer.SetCulture`、`CultureFontPolicy.Apply` 四件一起做——数字与日期的格式化跟 `CurrentCulture`/`UICulture` 走，这三处不同步就会出现"中文界面英文日期"。

#### 12.5.3 `CultureFontPolicy`：CJK 需要有字的字体

内嵌的三套字体（Plus Jakarta Sans / Space Grotesk / Cascadia Mono，见 `Themes/Tokens.xaml:13-15`）只有拉丁字形。WPF 会逐字回退到系统字体——不会出现豆腐块，但字重与度量混杂，评审时看不出来、用户一眼看出。所以 `Apply(culture)`（:43-70）在 `CultureChain.RequiresCjkFonts`（`zh/ja/ko`）为真时把 `BodyFont`/`DisplayFont` 两个令牌换成 `"Microsoft YaHei UI, Microsoft YaHei, Segoe UI, Noto Sans CJK SC"`（显示字体多一档 `Segoe UI Semibold`），否则恢复。

两个实现细节：一是首次覆盖前**快照**原值（`_originalBody`/`_originalDisplay`），所以类里不硬编码品牌字体，`Tokens.xaml` 仍是唯一来源；二是写回的是 `Application.Current.Resources` 的令牌条目，UI 用 `{DynamicResource BodyFont}` 消费（`docs/localization.md:204-210`），已打开的窗口也会跟着换。`Application.Current` 为空（单测、CLI）时直接 no-op。

### 12.6 目录文件与外部覆盖目录

两份目录：`src/Gloam/Resources/i18n/en-US.json`（59,456 字节）与 `zh-Hans.json`（44,389 字节）。按"缩进 4 空格 + 引号键"统计，**两份各 790 个字符串叶子键**。结构统一为三段：

```json
{
  "$comment": "Source catalog. Every other language is validated against these keys and placeholders. ...",
  "culture": "en-US",
  "strings": {
    "settings.language.heading": "Interface language",
    "tray.tooltip": "Gloam {version}",
    "gamelab.gameCount.other": "{count} games"
  }
}
```

键的命名呈现**两种风格并存**：一种是把英文整句压成点分小写（`tray.so.gloam.has.turned.it.off.and.manages.display.colour`、`dashboard.every.game.found.by.the.scanner.is.already.in.your`），另一种是 `tray.openDashboard`、`dashboard.gameLab.title`、`tray.lastCheckDays` 这样的 camelCase 段。前者可读性差、重命名风险高（见 12.8 R5）。`zh-Hans.json` 的 `culture` 是 `zh-Hans`，因此 Windows 报 `zh-CN` 时靠文化链命中。

外部覆盖目录：

```csharp
// App.xaml.cs:69-83
var options = new LocalizationOptions
{
    ExternalCatalogDirectory = System.IO.Path.Combine(AppPaths.DataDir, "i18n"),   // %LOCALAPPDATA%\Gloam\i18n
};
var sources = new CompositeCatalogSource(
    new EmbeddedCatalogSource(typeof(App).Assembly, "Gloam.Resources.i18n."),
    new DirectoryCatalogSource(options.ExternalCatalogDirectory));
return Localizer.Load(options, sources);
```

即"内嵌基线 + 磁盘逐键覆盖"：把改好的 `zh-Hans.json` 丢进 `%LOCALAPPDATA%\Gloam\i18n`，不重新打包也能修正译文或新增语言；坏文件被跳过并记入 `DirectoryCatalogSource.Errors` / `StringCatalogSet.LoadErrors`，不影响启动。注意两点：目录**只扫描顶层** `*.json`（`SearchOption.TopDirectoryOnly`，`CatalogSources.cs:101`）；加载只发生在 `App.ConfigureServices` 这一次（`ILocalizer` 是单例工厂），运行期改文件不会热加载，需重启——文档里"hot-loaded from disk"（`docs/localization.md:27`）指的是"不必重编译"，不是"不必重启"。

### 12.7 本地化不变量：代码与 CI 实际强制的东西

这些不是建议，是 `tests/Gloam.Tests` 里会红的行为。

#### 12.7.1 四条审计（`LocalizationAuditTests.cs`）

| 测试 | 检查内容 | 失败意味着 |
| --- | --- | --- |
| `Every_key_referenced_from_xaml_exists_in_the_source_catalog`（:67） | 扫 `src/Gloam/**/*.xaml` 的 `{loc:Tr key}`，键必须在 `en-US` 目录里 | 界面会把 `old.key` 原样显示给用户 |
| `Every_catalog_key_is_referenced_somewhere_in_source`（:146） | 每个目录键必须在 XAML 或任意 `src/` 源文件（含点分字面量）里出现 | 死键，翻译者仍在翻 |
| `Every_translation_differs_from_the_source_unless_it_cannot_be_translated`（:112） | 非源目录的每个非复数条目不得与英文完全相同，除非登记在 `UntranslatableByDesign`（10 个键：品牌名、`Gamma 2.2`/`Gamma 2.4`、`ArgyllCMS`、单位符号等） | 复制粘贴式"翻译" |
| `Hardcoded_user_visible_strings_do_not_increase`（:177） | 按文件统计硬编码候选串，与 `tests/Gloam.Tests/Fixtures/localization/hardcoded-baseline.json` 逐文件比较，**只许降不许升** | 改动的代码新增了绕过目录的 UI 文本 |

基线的当前数字（对 fixture 求和）：`54` 个文件、`492` 条候选串。其中 `Features/Calibration/` 占 `28` 个文件 `407` 条（`CalibrationReportWindow.xaml.cs` 一家 220 条）；本切片（GameLab + NightMode + Settings + 相关基础件）占 `26` 个文件 `85` 条，分例如下：

| 文件 | 候选串 | 典型来源 |
| --- | --- | --- |
| `Features/NightMode/NightModeScheduleControl.xaml` | 3 | "Drag points to adjust. Double-click to add."、`" - Enhanced warmth below 2800K"` |
| `Features/NightMode/NightModeScheduleControl.xaml.cs` | 6 | 日志、`Lat`/`Lon` 之外的调试文本 |
| `Features/NightMode/NightModeScheduleViewModel.cs` | 1 | 拖拽浮层默认值 |
| `Features/GameLab/GameLabWindow.xaml` | 4 | `"Win + Shift + G"`、`"Scanning launcher libraries…"`、`"% preservation"`、`"GAMMA"` |
| `Features/GameLab/GameLabWindow.xaml.cs` | 4 | 焦点注释外的提示文本 |
| `Features/GameLab/GamerModeItem.cs` | 3 | `"{title} is active"` / `"is active on {count} displays"` |
| `Features/Settings/SettingsWindow.xaml` | 2 | "Close" 提示等 |
| `Features/Settings/SettingsWindow.xaml.cs` | 2 | `CcssBrowserWindow` 的 `introText` |

基线只有在**迁移提交**后、且人工复核 diff 时才重新生成：

```powershell
$env:GLOAM_WRITE_LOCALIZATION_BASELINE = "1"; dotnet test; Remove-Item Env:GLOAM_WRITE_LOCALIZATION_BASELINE
```

#### 12.7.2 三类文本与 `CultureChanged` 协议

这是本仓库最容易踩、文档写得最细的一条（`docs/localization.md:164-195`）：

| 文本存放位置 | 语言切换后的行为 | 正确写法 |
| --- | --- | --- |
| XAML `{loc:Tr key}` | 自动更新 | 无需处理 |
| ViewModel getter 里的 `Loc.Get` | **不会**更新，除非 VM 重新通知 | 构造时 `_loc.CultureChanged += (_, _) => OnPropertyChanged(string.Empty);`，`Dispose` 里退订 |
| 字段或被 `static readonly` 缓存的值 | **不会**更新，除非重建 | 改成计算属性（每次读时组装），或在 `CultureChanged` 里重写字段再整体通知 |

本切片里两种正确写法都有实例：`GamerModeItem` 用"重写字段 + `OnPropertyChanged(string.Empty)`"（`GamerModeItem.cs:55-68`）、`GamerProfileEditorItem` 用"重设六个枚举值 + 整体通知"（:559-588`），而 `NightModeAlgorithmOption.DefaultOptions`、`GamerProfileEditorItem.Available*` 选择"每次访问重建"。反向的例子是 `GamerModeItem.SetSessions` 里的 `"{title} is active"`——那句是硬编码，语言切换后不会变，也不进目录。

#### 12.7.3 单行文本不得被裁切

`LayoutOverflowTests.No_visible_text_is_clipped_on_a_main_surface`（`LayoutOverflowTests.cs:41`）对 `UiFixtures.MainSurfaces` 三个主界面（dashboard / gamelab / settings，`UiFixtures.cs:85-91`）在 `zh-Hans`、`en-US`、`qps-ploc` 三种语言下测量：对每个可见的单行 `TextBlock`，用元素自身的字体设置排版，比较"需要的宽度"与"分到的宽度"，容差 1.5 DIP。折叠面板也算（先展开）。

`qps-ploc` 由 `PseudoLocalizer`（88 行）提供：给字母加变音符号、尾部加 `·` 把长度撑到约 +40%、首尾加方括号，让截断与省略一眼可见。"中文通常比英文短，所以中文截图无法证明布局能扛翻译"——这条测试就是为这句话存在的。

#### 12.7.4 其他约定

- **占位符一律 `{name}`，禁止 `{0}`**：词序随语言变化，位置参数会逼翻译者改写语法（`MessageTemplate.cs:9-15`）。
- **字体令牌用 `{DynamicResource BodyFont}`，不用 `StaticResource`**：否则 CJK 字体替换不会作用到已加载的界面。
- **`CatalogJson` 的宽松/严格分界**：对人友好的（注释、尾逗号）宽松，对静默失败（未知复数值、缺 culture）严格。
- **`SourceAudit` 的启发式是保守的**：日志调用整段跳过（含跨行括号配平）、`nameof(...)`、注释行都排除；`#RRGGBB`、`ThemeAccent`、`display-identify-close`、`srgb_to_gamma2p2_sdr.icm`、纯小写单词、点分标识符都判为"非散文"。这些过滤器每一条都在注释里对应一次"基线被灌水"的历史。

### 12.8 风险（均可在源码中复核）

| # | 风险 | 证据 |
| --- | --- | --- |
| R1 | **同时存在两个 `DashboardViewModel`**：Dashboard 与 Game Lab 各 new 一个，都订阅 `GamerSettingsChanged`/`GamerProfileUsed`，各有 180ms 自动保存 timer 与 2.5s 进程刷新 timer。进程枚举翻倍；两个窗口同开时任一保存都会让另一方重建 `Profiles`，而 `SaveGamerProfiles` 是整表 `TrySetProfiles`，后写覆盖先写。 | `DashboardWindow.xaml.cs:37`、`GameLabWindow.xaml.cs:33`、`DashboardViewModel.cs:287-310`、`:1170-1191` |
| R2 | 语言入口三界面不一致：Dashboard 有 `LanguageToggleButton`、Settings 有下拉，Game Lab 只有主题切换。 | `DashboardWindow.xaml:272`、`SettingsWindow.xaml:214`、`GameLabWindow.xaml:112` |
| R3 | 中文界面仍有运行时拼接的英文（不属基线统计范围）：`"{title} is active"`、`"shadows +… below … linear"`、`" - Enhanced warmth below 2800K"`、`CcssBrowserWindow.introText`。 | `GamerModeItem.cs:257-268`、`NightModeScheduleControl.xaml:36,95`、`SettingsWindow.xaml.cs:95` |
| R4 | `LanguageService.Apply` **没有**设置 `CultureInfo.CurrentCulture`，只设了 `CurrentUICulture`/`DefaultThreadCurrentUICulture`；而 `docs/localization.md:217-219` 声称会一并设置。实际影响：`DateTime.Now.ToString("h:mm:ss tt")` 等格式跟系统区域而非界面语言。 | `LanguageService.cs:157-167`、`GamerModeItem.cs:238` |
| R5 | 键命名两套风格（点分小写整句 vs camelCase），790 键中大量是"整句压成键"；改文案即改键名，必须同步 XAML/代码/`zh-Hans.json`/硬编码基线四处，否则三条审计各自报错。 | `en-US.json` 全文，例 `tray.so.gloam.has.turned.it.off.and.manages.display.colour` |
| R6 | 文档漂移：文档称"696 keys in both catalogs"，实测两份各 790 键。 | `docs/localization.md:140` vs 两份 JSON |
| R7 | 两处"收集了没人看"的诊断：`StringCatalogSet.LoadErrors` 与 `DirectoryCatalogSource.Errors` 在 `src/` 内无消费者，坏覆盖文件被静默跳过；`MissingKeys` 只在 `--smoke` 里做非空断言，未进 `DiagnosticsBundle`。 | `CatalogSources.cs:170`、`:85`、`Program.cs:120` |
| R8 | `LocalizationOptions.ExternalCatalogDirectory` 是死配置：引擎从不读取，真正生效的是 `App.BuildLocalizer` 直接传给 `DirectoryCatalogSource` 的路径。 | `LocalizationOptions.cs:31`（grep 仅一处） |
| R9 | 外部目录不热加载：`AddSingleton<ILocalizer>(_ => BuildLocalizer())` 只执行一次，无 `FileSystemWatcher`；改译文须重启。 | `App.xaml.cs:56,69-83` |
| R10 | 自动定位走明文 `http://ip-api.com`，失败仅 `Log.Error`，离线时按钮"点了没反应"。 | `NightModeScheduleControl.xaml.cs:107-152` |
| R11 | `GamerProfileEditorItem` 的语言切换重设列表是手工维护的六个枚举；新增绑定枚举的 ComboBox 忘记登记就会重现"标签变了但选中值被清空"。无编译期保护。 | `GamerModeItem.cs:559-588` |
| R12 | `SettingsWindow` 用服务定位器取 `LanguageService`；取不到时构造一个 `persist: _ => { }` 的临时实例，语言选择界面有效但不落盘，且失败是静默的。 | `SettingsWindow.xaml.cs:31-42` |
| R13 | 日程编辑器加点的"已达 12 点上限"提示用原生 `MessageBox.Show`，绕过了仓库自有的主题化对话框（`Infrastructure/Dialogs/ConfirmDialog`），在自绘标题栏窗口上会露出系统 chrome。 | `NightModeScheduleControl.xaml.cs:588` |

### 12.9 文件 → 职责

| 文件 | 职责 |
| --- | --- |
| `src/Gloam/Features/GameLab/GameLabWindow.xaml` | 紧凑独立 Game Lab（1040×660，自绘标题栏）：状态带 / 库列表 / 单条 profile 编辑器 / 扫描审阅面板 |
| `src/Gloam/Features/GameLab/GameLabWindow.xaml.cs` | 只做窗口生命周期：bounds 持久化、`DashboardViewModel` 装配、`Esc` 关闭、焦点落位、主题切换 |
| `src/Gloam/Features/GameLab/GamerModeItem.cs` | 四个类型：`GamerModeItem`（状态容器）、`DiscoveredGameItem`（扫描行）、`GamerProfileEditorItem`（规则编辑器）、三个 `record` 选项类型 |
| `src/Gloam/Features/GameLab/GameLibraryCoordinator.cs` | 后台进程枚举与启动器库扫描的取消/代际/线程边界，`RunningAppScan` 结果类型 |
| `src/Gloam/Features/NightMode/NightModeScheduleControl.xaml` | 五行布局：摘要+经纬度、180px 双画布图表、工具栏、渲染选项、`DataGrid` 行编辑 |
| `src/Gloam/Features/NightMode/NightModeScheduleControl.xaml.cs` | 画布绘制（网格/曲线/节点/now 线）、拖拽命中与浮层、ip-api 定位、250ms 预览与 16ms 重绘节流 |
| `src/Gloam/Features/NightMode/NightModeScheduleViewModel.cs` | 行集合与排序、经纬度文本、渲染参数、拖拽浮层状态；`SchedulePointViewModel` 与 `NightModeAlgorithmOption` 也在本文件 |
| `src/Gloam/Features/Settings/SettingsWindow.xaml` | 七张设置卡片（语言/Gamma/亮度/色温与色调/夜间覆盖与 Ultra Night/RGB）+ 页脚动作区 |
| `src/Gloam/Features/Settings/SettingsWindow.xaml.cs` | Compare 按住手势、`LanguageService` 的服务定位、`.ccss` 浏览对话框接线 |
| `src/Gloam/Features/Settings/SettingsViewModel.cs` | 每显示器待提交配置（`_pendingChanges`）、100ms 防抖实时预览、Save & Close 批量落盘、关闭时回滚 |
| `src/Gloam.Localization/ILocalizer.cs` | 翻译门面接口 + `CultureChangedEventArgs` |
| `src/Gloam.Localization/Localizer.cs` | 目录链查找、`{name}` 渲染、缺键计数、文化切换（无锁读 + 换引用） |
| `src/Gloam.Localization/StringCatalog.cs` | 不可变目录/条目、`PluralCategory`、复数模板回退 |
| `src/Gloam.Localization/CatalogSources.cs` | 四种 `ICatalogSource` + `StringCatalogSet`（后源逐键覆盖、文化的同语言兜底、`LoadErrors`） |
| `src/Gloam.Localization/CatalogJson.cs` | JSON 线格式读写、`CatalogFormatException`、宽松解析与排序输出 |
| `src/Gloam.Localization/CultureChain.cs` | 标签宽松解析、回退链构造、`RequiresCjkFonts`、`SourceCulture = en-US` |
| `src/Gloam.Localization/PluralRules.cs` | 手写 CLDR 基数规则（zh/ja/ko、fr/pt、ru/uk/pl、ar、默认英文）+ `RequiredCategories` |
| `src/Gloam.Localization/MessageTemplate.cs` | `{name}` 占位符提取/渲染、`{{`/`}}` 转义、未解析占位符保留 |
| `src/Gloam.Localization/CatalogValidator.cs` | 五项跨语言校验（缺键、孤儿译文、占位符不匹配、缺复数形、空值），稳定排序 |
| `src/Gloam.Localization/MissingKeyReport.cs` | 并发缺键计数 + 上限（默认 500）+ `Clear()` |
| `src/Gloam.Localization/SourceAudit.cs` | 源码审计：抽取 `{loc:Tr}` 键、按启发式找硬编码 UI 文本（日志整段跳过） |
| `src/Gloam.Localization/LocalizationOptions.cs` | `SourceCulture`、`ExternalCatalogDirectory`（未被消费）、`MissingKeyBehavior`、`MaxTrackedMissingKeys` |
| `src/Gloam.Localization.Wpf/LocalizedStrings.cs` | 绑定源：`this[key]` 索引器 + `PropertyChanged("Item[]")` + `Default` + `NullLocalizer` |
| `src/Gloam.Localization.Wpf/TrExtension.cs` | `{loc:Tr key}` markup extension，返回 `Binding` 以支持就地换语言 |
| `src/Gloam.Localization.Wpf/Loc.cs` | 静态门面 `Loc.Get/Format`，转发到 `LocalizedStrings.Default.Localizer` |
| `src/Gloam.Localization.Wpf/LanguageService.cs` | 语言列表/选中/切换/持久化回调、`ApplyStartup`、`NextOption`/`Toggle`、`CultureFontPolicy` 调用 |
| `src/Gloam.Localization.Wpf/CultureFontPolicy.cs` | CJK 文化下替换 `BodyFont`/`DisplayFont` 令牌并可在之后恢复原值 |
| `src/Gloam/Resources/i18n/en-US.json` | 源目录（790 键），所有其他语言的校验基准 |
| `src/Gloam/Resources/i18n/zh-Hans.json` | 简体中文目录（790 键），与源目录键集一致 |

### 12.10 未验证项

- 本章结论来自**静态阅读**：未 `dotnet build`、未运行测试、未启动应用（任务禁止）。因此"审计会红""布局测试通过"这类表述是"测试代码会这样检查"，不是"本次运行通过"。
- 未逐条核对 790 个中文条目的译文质量，只做了键数量与占位符/复数结构的抽查。
- `GamerModeCoordinator`、`GammaApplyService`、`GameDiscoveryService`、`GamerPresetCatalog` 的内部实现属 Core，本章只按调用签名记录方向，未展开。
- `qps-ploc` 文化是否会被 `CultureChain.TryParse` 命中未验证：伪本地化由测试内的 `PseudoLocalizer` 包装真实 localizer，不依赖真实 `qps-ploc` 目录。
- 未验证 `ip-api.com` 的实际可达性与返回格式（含 `fields=lat,lon` 的响应布局假设）。

---

## 13 测试、构建、CI/CD、工具链与文档


> 本篇覆盖 `src/` 之外的全部工程化资产：`tests/Gloam.Tests`（105 个 `.cs`）、
> 根级构建配置、`package.ps1` 与 `scripts/`、`.github/`、`docs/`、`site/`、`deploy/`、
> `ai/`、`.simplify/`。
>
> **阅读方式：静态阅读。** 全程未执行 `dotnet build` / `dotnet test` / `package.ps1` /
> `vpk`，也未运行任何 `.github` 工作流；所有结论来自源码、脚本与配置的字面内容。
> 凡属下推而非可直接观察的，均标注「推断」。行号引用格式为 `path:line`。
>
> **文件计数口径。** `tests/Gloam.Tests` 根目录下恰有 **105 个 `.cs` 文件**（文件系统实测，
> 排除 `bin/`、`obj/`）。任务书里的「106」按如下口径成立：105 个 `.cs` + `Gloam.Tests.csproj`
> = 106 个参与测试项目的文件。105 个 `.cs` 在 §13.1.7 的 12 张表里逐一列出，无遗漏。

---

### 13.1 测试套件架构

#### 13.1.1 项目设置：`tests/Gloam.Tests/Gloam.Tests.csproj`

54 行，SDK 风格项目，默认 glob 收全部 `.cs`。

| 设置 | 值 | 理由（源码注释/可验证效果） |
|---|---|---|
| `TargetFramework` | `net10.0-windows` | 与全部 `src/` 项目一致；测试要 `UseWPF` |
| `RollForward` | `LatestMajor` | 运行时可上浮，不被 10.0 运行时钉死 |
| `UseWPF` | `true` | 注释明说：打印分页回归测试要驱动 `FlowDocument` 的 PTS 分页器（`Gloam.Tests.csproj:9-11`） |
| `IsPackable` | `false` | 非发布物 |
| `Nullable` / `ImplicitUsings` | `enable` | 与 `Directory.Build.props` 同值（此处显式重述） |

包引用 4 个，全部由中央包管理供版本（见 §13.2.2）：

- `Microsoft.NET.Test.Sdk`
- `xunit`
- `xunit.runner.visualstudio`（`PrivateAssets=all`）
- `coverlet.collector`（`PrivateAssets=all`）

项目引用 3 个：`Gloam.Core`、`Gloam.Localization`、`Gloam`（UI）。经传递依赖，测试同样能用到
`Gloam.Interop` 与 `Gloam.Localization.Wpf`。

两个非显然的 `ItemGroup`：

1. **全局 using 镜像 UI 程序集策略**（`Gloam.Tests.csproj:33-46`）——把
   `Gloam.Features.*`、`Gloam.Infrastructure.*`、`Gloam.Controls` 注册为项目级 `Using`，
   测试文件因此无需逐个 `using`。注释写明这是「镜像 UI 程序集的模块可见性策略」。
2. **Golden fixture 进输出目录**（`Gloam.Tests.csproj:48-52`）：

   ```xml
   <None Include="Fixtures\**\*" CopyToOutputDirectory="PreserveNewest" />
   ```

   测试用 `AppContext.BaseDirectory/Fixtures/Golden` 定位，而不是回推仓库根。

四个 `src/` 项目都声明了 `<InternalsVisibleTo Include="Gloam.Tests" />`
（`src/Gloam/Gloam.csproj:109`、`src/Gloam.Core/Gloam.Core.csproj:14`、
`src/Gloam.Localization/Gloam.Localization.csproj:12`、
`src/Gloam.Localization.Wpf/Gloam.Localization.Wpf.csproj:18`），因此测试可以直测 internal 类型。

#### 13.1.2 进程级引导：`AssemblyInfo.cs`

67 行，做了两件事，都是「让整个测试进程可预测」。

**其一，关掉并行**（`AssemblyInfo.cs:7`）：

```csharp
[assembly: CollectionBehavior(DisableTestParallelization = true)]
```

理由在 `docs/project-structure.md:105-110` 与 `TestLocalization.cs:48-56`：应用有进程级单例——
唯一的 WPF `Application`、`LocalizedStrings.Default`、静态 `AppPaths`、主题资源字典。并行跑
两个改这些状态的测试类只会制造 flaky，不会更快。

**其二，`[ModuleInitializer]` 在第一个测试之前做三件事**（`AssemblyInfo.cs:9-20`）：

1. `NormalizeWindir()`——某些隔离测试运行器只提供 `SystemRoot`，而 WPF 原生主题加载器直接读
   `WINDIR`；缺它时窗口还没创建就失败（`AssemblyInfo.cs:22-35`）。
2. `RedirectAppDataAwayFromTheRealUser()`——把 `AppPaths.DataDir` / `RoamingDataDir` 指到
   `%TEMP%\GloamTests\<pid>-<guid>`。注释记录了一次真实事故：`new SettingsManager()` 加一次写入
   就落到开发者**真实**的 `%LOCALAPPDATA%\Gloam\settings.json`，会静默毁掉真实设置、显示器配置与
   校准关联（`AssemblyInfo.cs:37-72`）。此处的设计选择值得记下：不是「每个测试自己 opt-in」，
   而是把默认反转；测试自己的临时覆盖仍然有效，因为其间保存/恢复的原值已经是临时根。
   目录**故意不删**——xUnit 没有可靠执行的 assembly-teardown 钩子，泄漏一个临时目录比删掉一个
   可能有测试仍在写的路径便宜。
3. `TestLocalization.UseEnglish()`——见 §13.1.5。

#### 13.1.3 WPF 宿主：`WpfTestHost.cs`

120 行，解决两个硬约束。

- **一个进程只能有一个 `System.Windows.Application`**。两个测试类各自 `new Application()`，第二个
  必抛「cannot create more than one」。因此宿主在一条**单独 STA 线程**上创建 dispatcher、
  `Application`（`ShutdownMode.OnExplicitShutdown`）并把 `pack://application:,,,/Gloam;component/Themes/Tokens.xaml`
  合并进 `Application.Resources`（`WpfTestHost.cs:96-113`），此后所有 UI 测试被 marshal 到这条线程。
- **嵌套调用不能死锁**。`Run(Action)` 先 `dispatcher.CheckAccess()`，已在 UI 线程则直接执行，
  否则 `dispatcher.Invoke` 并捕获异常跨线程重抛（`WpfTestHost.cs:26-52`）。

`PumpUntil(Func<bool>, TimeSpan)`（`WpfTestHost.cs:54-68`）等的是**断言所关心的条件**，预算只用来
封顶失败时间；注释解释了为什么不写死 sleep：固定时长的测试在空闲机器上过、在负载机器上挂。
`Pump(TimeSpan)` 用 `DispatcherFrame` + `DispatcherTimer(DispatcherPriority.Background)` 泵消息。

#### 13.1.4 界面夹具：`UiFixtures.cs`

105 行，构造三个「用户不打开模态框就能到达的主界面」：
`BuildDashboard()`（1080×760）、`BuildGameLab()`（1080×720）、`BuildSettings()`。
`MainSurfaces` 把它们暴露成 `(Name, Factory)` 列表，供布局门禁与截图共用。

三个刻意的替身（`UiFixtures.cs:93-104`）：`NoUpdateManager : IUpdateManagerAdapter`——返回
`IsPortable=true`、无待装更新；真实 `MonitorManager` 与空 apply 回调。注释写明这些「不影响布局」。
窗口一律 `Left=Top=-20000`、`ShowInTaskbar=false`，在屏幕外渲染。

为什么共享：手册里的截图与布局门禁测的必须是同一个窗口，否则「文档展示的」和「门禁量的」
会漂移（`UiFixtures.cs:10-18`）。`BuildGameLab()` 还塞了一条样例 profile，否则编辑面板全空，
读者分不清是布局坏了还是夹具缺内容。

#### 13.1.5 本地化测试基建

| 文件 | 作用 | 关键点 |
|---|---|---|
| `TestLocalization.cs` | 给测试进程装配与运行时相同的 localizer | `UseEnglish()` 用内嵌目录 `Gloam.Resources.i18n.` + `en-US` 源语言（`TestLocalization.cs:22-31`）；否则 `LocalizedStrings.Default` 是 null object，所有文案解析成 key，断言失败的原因与行为无关 |
| 同上 | `[ResetsLocalization]` 属性 | `BeforeAfterTestAttribute.After` 里重新 `UseEnglish()`，因为默认值是进程级，一个测试换掉它会静默改变后续所有测试看到的语言（`TestLocalization.cs:34-46`） |
| 同上 | `UiGlobalState` 集合 | `[CollectionDefinition(Name, DisableParallelization = true)]`（`TestLocalization.cs:48-60`）；5 个测试类加入：`UiSnapshotTests`、`LayoutOverflowTests`、`LanguageSwitchBehaviourTests`、`LocalizationWpfTests`、`DashboardAppPickerWpfTests` |
| `ShippedCatalogs.cs` | 按**应用启动时的同一路径**加载真正随包发布的目录 | `ResourcePrefix = "Gloam.Resources.i18n."`，从 `typeof(Gloam.Shell.App).Assembly` 读（`ShippedCatalogs.cs:11-16`）。测试因此验证的是发布物里的 catalog，不是测试自带的副本 |
| `PseudoLocalizer.cs` | 伪语言 `qps-ploc` | 给每个 ASCII 字母换成带音标字符、尾部补 `·` 到 **+40%**、整体加 `[` `]` 便于看出截断（`PseudoLocalizer.cs:23,58-87`）。注释交代动机：中文通常比英文**短**，中文截图证明不了布局健壮（`PseudoLocalizer.cs:12-20`） |

#### 13.1.6 规模统计（实测）

| 指标 | 数值 |
|---|---|
| `.cs` 文件 | 105 |
| 总行数 | 28,646 |
| `[Fact]` 方法 | 1,125 |
| `[Theory]` 方法 | 91 |
| 测试方法合计 | 1,216 |
| `[InlineData]` 行 | 332 |
| `[Fact]`/`[Theory]` 合计 | 1,216 |

> 注：`ai/memory-bank/tasks/gloam-i18n-tasklist.md:88` 记「全量测试绿（当前 1498）」，那是
> **执行用例数**（Theory 展开后）的历史快照，无法在本机静态复核，也不作为本文的数值依据。

#### 13.1.7 12 个主题簇（105 文件全部入账）

分簇原则：一个文件只进一个簇，按「它钉住的领域」而非「它所在的项目」归类。
「F/T」= `[Fact]`/`[Theory]` 数，最后一列是行数。

##### 簇 0 · 测试基建（7 文件，692 行，1 F / 0 T）

| 文件 | F/T | 行 | 作用 |
|---|---|---|---|
| `AssemblyInfo.cs` | 0/0 | 67 | 关并行 + 进程级引导（§13.1.2） |
| `WpfTestHost.cs` | 0/0 | 106 | 唯一 `Application` + STA dispatcher + 泵（§13.1.3） |
| `UiFixtures.cs` | 0/0 | 94 | 三个主界面的构造夹具（§13.1.4） |
| `PseudoLocalizer.cs` | 0/0 | 71 | `qps-ploc` 伪语言（§13.1.5） |
| `TestLocalization.cs` | 0/0 | 55 | 英文 catalog + 重置属性 + 串行集合定义 |
| `ShippedCatalogs.cs` | 0/0 | 13 | 从发布程序集加载 catalog |
| `DocumentationFigures.cs` | 1/0 | 191 | **不是测试**：`GLOAM_FIGURES=1` 时合成对比图（§13.1.11） |

##### 簇 1 · Gamma / LUT / 色彩数学（10 文件，4,468 行，259 F / 15 T）

钉的是「从传输函数到 GPU 1D/3D LUT 再到线性光调整」这条链，全部纯 CPU。

| 文件 | F/T | 行 | 钉住的行为 |
|---|---|---|---|
| `TransferFunctionTests.cs` | 8/4 | 137 | ST.2084 PQ 参考点与往返、sRGB 分段、BT.1886（含非零黑位）曲线 |
| `ToneCurvePchipTests.cs` | 4/0 | 113 | Fritsch–Carlson PCHIP 单调三次拟合替换旧拟合后的形状与单调性 |
| `LutGeneratorTests.cs` | 23/2 | 774→378 | 缓存键不别名（温度/强度/白点）、Windows Default 恒等、Gamma 2.4 单调与肩部混合、SDR/HDR 分支、`CanUseCalibratedLut` 拒绝损坏 profile |
| `Lut3DTests.cs` | 69/2 | 1,164 | 3D LUT 三线性插值、tone curve、全量边界 |
| `LutBinaryValidationTests.cs` | 3/0 | 47 | 截断的大 LUT 在分配颜色表之前被拒；二进制往返保真 |
| `NativeGammaRampTests.cs` | 11/0 | 174 | `BuildRampChannel` 与 LUT 采样点一致、GDI 包络上下界、夜间模式蓝通道入包络后仍单调 |
| `ColorAdjustmentsTests.cs` | 53/0 | 604 | 亮度/色温/色调/感知强度/Ultra Night 的乘数，线性光域顺序，校准 LUT 先于用户调整 |
| `ColorMathTests.cs` | 69/7 | 883 | ΔE2000、色域、白点、色度学核心函数 |
| `GamutCoverageTests.cs` | 13/0 | 147 | `ColorMath.GamutCoverage` 的 Sutherland–Hodgman 三角裁剪 |
| `Cam16UcsTests.cs` | 6/0 | 104 | CAM16-UCS 正/逆模型，锚定 Li et al. 2017 标准算例 |

##### 簇 2 · 校准数学与测量（13 文件，5,774 行，215 F / 5 T）

钉的是「测出来的数字」：指标、不确定度、MHC2 字节、仪表偏移、ICC/CMM 行为。

| 文件 | F/T | 行 | 钉住的行为 |
|---|---|---|---|
| `CalibrationMetricsTests.cs` | 31/2 | 883 | 报告头条 ΔE（`Lut3DGenerator.CalculateMetrics`）的回归覆盖 |
| `VerificationAnalysisTests.cs` | 29/0 | 428 | 直方图分桶边界、worst/best patch、分类汇总、PQ 跟踪行剔除 |
| `UncertaintyBudgetTests.cs` | 26/1 | 394 | RSS 合成、包含因子 2、多读中位数标准误小样本表、仪表分类、噪声模型 EWMA、漂移残差 |
| `Mhc2ProfileBuilderTests.cs` | 19/1 | 820 | 逐字节校验 MHC2 写入：色域矩阵 + 三条 1024 点 TRC |
| `CcmxMeterOffsetTests.cs` | 21/0 | 473 | 双仪表 CCMX 工作流：加权最小二乘矩阵、泄漏过滤、原色选择 |
| `CgatsValidatorTests.cs` | 24/0 | 293 | CGATS `.ccmx/.ccss` 校验：头部关键字、集合数、数据块平衡、NUL/超大/非有限数拒绝；数据库去重 |
| `SpectralCcssTests.cs` | 13/1 | 322 | 光谱采集到 CCSS 生成、melanopic 系数推断 |
| `DriftCompensatorTests.cs` | 5/0 | 123 | 5% 线性漂移补偿到 <1% 残差、保色度、过度漂移不补偿交给校验器 |
| `ClosedLoopCorrectorTests.cs` | 9/0 | 349 | 闭环校正在**模拟**显示器上收敛（不使用真实面板） |
| `PerChannelGrayTrackingTests.cs` | 9/0 | 318 | E4 逐通道灰阶跟踪：单通道 ramp 合成 |
| `DisplayMatchSolverTests.cs` | 12/0 | 215 | 多显示器匹配（路线图 4.5）：minimax 解把各面板拉到同一目标 |
| `TrustCheckTests.cs` | 16/0 | 242 | 6 patch 分级数学与「黑是黑」诚实规则 |
| `IccColorManagementRegressionTests.cs` | 1/0 | 168 | 真实 Windows CMM（`mscms.dll`）代理 Photoshop：HDR profile 保 sRGB 中灰，旧 PQ TRC 会偏亮 >0.08 信号（§13.1.10） |

##### 簇 3 · 校准流程（21 文件，4,908 行，200 F / 15 T）

钉的是「向导怎么走、什么时候拒绝开始、量完怎么装、装完怎么切」。

| 文件 | F/T | 行 | 钉住的行为 |
|---|---|---|---|
| `CalibrationSetupPreflightTests.cs` | 19/0 | 367 | 19 条预检规则（HDR 状态与目标不匹配、OLED 无校正、缺失/非法校正文件、Night Light 干扰、参考白接近峰值…） |
| `CalibrationInstallPreflightTests.cs` | 17/0 | 272 | 安装前预检在其余条件不变时只对单一变量反应 |
| `ForeignCorrectionPreflightTests.cs` | 17/0 | 212 | 测量前「外来校正」的纯决策逻辑 |
| `CalibrationTargetTests.cs` | 11/0 | 175 | 标准目标集合只暴露生产用 HDR Desktop PQ；HLG/BT.1886/sRGB 曲线参数；非有限输入回退 |
| `CalibrationOrchestratorAveragingTests.cs` | 8/0 | 264 | 假色度计脚本化回读下的多次平均与编排 |
| `AdaptiveOrchestratorTests.cs` | 9/1 | 373 | 自适应模式编排（路线图 1.1）的端到端合成数据测试 |
| `AdaptivePatchPlannerTests.cs` | 12/0 | 325 | 自适应打点位置：评分、覆盖、去重 |
| `PatchSetGeneratorTests.cs` | 5/4 | 191 | 预设打点全部吸附 8-bit 网格、无重复三元组、长预设插漂移锚、短预设不插 |
| `SpotreadSessionTests.cs` | 37/0 | 429 | **假进程 harness**：进程内钩子替换真实 `spotread` 调用 |
| `SpotreadDisplayTypeTableTests.cs` | 19/5 | 319 | Issue #7：`-y` 选择子按仪表×安装状态生成 |
| `ColorimeterDetectionTests.cs` | 3/0 | 37 | 解析 spotread 设备列表文本（裸串口不算仪表） |
| `ArgyllPathFinderTests.cs` | 1/1 | 52 | 合法 Argyll bin 目录必须同时有 `spotread.exe` 与 `dispwin.exe`；拒绝带参数的路径 |
| `OemCorrectionImporterTests.cs` | 8/0 | 130 | 厂商 EDR 递归查找、已存在 CCSS 时跳过、解析输出计数 |
| `CalibrationDetectionTests.cs` | 4/0 | 94 | 检测失败可重试、关闭设置页取消检测、并发检测复用一个仪表操作 |
| `CalibrationReportViewModelTests.cs` | 4/1 | 56 | 非有限 ΔE 用默认画刷；带不确定度尾巴的文本拆分 |
| `ReportSnapshotBuilderTests.cs` | 1/0 | 15 | 同名显示器同一时刻的报告路径唯一 |
| `ProfileModeSwitchTests.cs` | 8/2 | 248 | SDR/HDR 双 profile 槽位的切换、迁移、外来 profile 恢复与重试 |
| `AdvancedColorProfileAssociationTests.cs` | 9/0 | 277 | 已关联 profile 的重复激活必须成功 |
| `AdvancedColorProfileMigrationTests.cs` | 2/0 | 173 | 历史 Gloam profile 修复 + 设置更新 + 激活验证副本 |
| `VerificationCoordinatorTests.cs` | 2/1 | 101 | 验证流程的呈现延迟/测量顺序、取消语义 |
| `CalibrationChartsTests.cs` | 4/0 | 125 | 图表绘制忽略非有限点/非法三角/负计数（WPF 绘制类型） |

##### 簇 4 · 设置、持久化与序列化（10 文件，2,159 行，80 F / 14 T）

| 文件 | F/T | 行 | 钉住的行为 |
|---|---|---|---|
| `SettingsValidationTests.cs` | 29/7 | 630 | 设置校验与安全输入处理 |
| `SettingsResilienceTests.cs` | 10/2 | 232 | 未知枚举容忍、永不崩、迁移 |
| `FilePersistenceTests.cs` | 3/3 | 82 | 失败替换不留临时文件、超大报告解析前拒绝、BOM、写出上限 |
| `ColorSerializationTests.cs` | 4/0 | 21 | 色度/XYZ/Lab/LinearRgb 往返 |
| `MeasurementCsvExporterTests.cs` | 6/0 | 139 | CSV 列序、HDR PQ 跟踪行、非有限值留空 |
| `MeasurementCsvImporterTests.cs` | 12/0 | 220 | 引号/逗号/换行、未知列忽略、缺列报列名、超大文件拒绝、导出→导入逐字段往返 |
| `CalibrationProfilePersistenceTests.cs` | 4/0 | 178 | 完整 target 持久化、旧 target 名解析、非有限指标与越界色度清洗 |
| `DisplayCalibrationProfileTests.cs` | 9/0 | 256 | profile → 表征的非法值回退、损坏 TRC 标记不可用、超大文件拒绝 |
| `MonitorInfoTests.cs` | 0/2 | 28 | `SdrWhiteLevel` / nits 清洗到有限合理值 |
| `LogFileRotatorTests.cs` | 3/0 | 80 | 归档轮转与最旧删除、彩色仪日志路径跟随数据目录 |

##### 簇 5 · 夜间模式与昼夜节律（10 文件，2,260 行，121 F / 11 T）

| 文件 | F/T | 行 | 钉住的行为 |
|---|---|---|---|
| `NightModeAlgorithmTests.cs` | 10/2 | 204 | 线基正确性、算法选项顺序、偏移合成 |
| `NightModeServiceTests.cs` | 20/1 | 290 | 淡入节奏按 mired 距离而非 Kelvin；手动覆盖/暂停/过期恢复；太阳位置转换 |
| `LuminancePreservingNightModeTests.cs` | 18/2 | 256 | 恒定 Y 重缩放补偿（路线图 3.3） |
| `CircadianDoseGovernorTests.cs` | 17/2 | 269 | 剂量上限是硬约束、逐级退让 |
| `MelanopicCalculatorTests.cs` | 19/0 | 272 | 光谱提取、mel-EDI、仪表/光谱路径 |
| `JndPacedFadeTests.cs` | 9/1 | 174 | 每步实际硬件变化不超过 1 JND（路线图 3.5） |
| `SunCalculatorTests.cs` | 14/0 | 172 | 极昼/极夜/日界线/负数经度/非有限输入 |
| `WindowsNightLightTests.cs` | 4/1 | 84 | Night Light 注册表 blob 的纯变换 |
| `NightModeScheduleViewModelTests.cs` | 4/1 | 97 | 时间表编辑的钳制与归一化 |
| `HistoryBoundaryTests.cs` | 6/1 | 102 | UTC 查询落到前一本地日历日、剂量文件跨文化可读、长显示器 id 不碰撞、按显示器过滤 |

##### 簇 6 · Gamer Mode（4 文件，1,340 行，43 F / 4 T）

| 文件 | F/T | 行 | 钉住的行为 |
|---|---|---|---|
| `GamerModeTests.cs` | 27/2 | 650 | 五种意图（Competitive/Reference/Cinematic HDR/Night Ops/自定义）、阴影可见性曲线锚点与单调、信号诊断、profile 深清洗、Gameplay Lock、窗口显示范围 |
| `GameDiscoveryTests.cs` | 9/0 | 227 | Steam manifest/额外库、Epic 启动可执行、bootstrapper 换成 shipping exe、取消语义 |
| `RunningProfileTests.cs` | 6/1 | 268 | 后台启动检测、前台优先级、暂停清除、原生进程快照 |
| `GameLibraryCoordinatorTests.cs` | 1/1 | 67 | 扫描取消/替换时旧结果丢弃 |

##### 簇 7 · HDR（9 文件，2,544 行，84 F / 10 T）

| 文件 | F/T | 行 | 钉住的行为 |
|---|---|---|---|
| `HdrMhc2LutBuilderTests.cs` | 16/2 | 420 | HDR MHC2 LUT 活在 PQ 线信号域；模拟面板下的线阶梯重建 |
| `HdrMhc2LutRefineTests.cs` | 10/0 | 259 | 对 HDR PQ tone LUT 的闭环 refine |
| `HdrRefinementLoopTests.cs` | 12/0 | 375 | 迭代闭环的 keep-best/damped、停止原因、原子安装 |
| `HdrJointRefinementTests.cs` | 7/0 | 229 | 矩阵 + tone 双轴联合精修；`IsBetter` 拒绝单轴实质性回退；取消后恢复 |
| `HdrColorMatrixRefinerTests.cs` | 19/0 | 332 | 亮度加权矩阵拟合恢复注入的色偏（路线图 2.2） |
| `ColoredHdrVerificationTests.cs` | 14/1 | 226 | Rec.2020 容器彩色刺激集下的彩色 HDR 验证数学 |
| `HdrHighlightTests.cs` | 0/4 | 99 | 满亮度/降亮/仅阴影调整都不压缩未测量的 HDR 高光 |
| `HighAplSettleTests.cs` | 5/1 | 227 | OLED 亮度限制器瞬态的回归（两次真实运行失败后加入） |
| `DxgiColorSpaceInfoTests.cs` | 1/2 | 40 | DXGI 色彩空间 → HDR 判定、位深人类可读标签 |

##### 簇 8 · 本地化（5 文件，1,081 行，45 F / 2 T）

| 文件 | F/T | 行 | 钉住的行为 |
|---|---|---|---|
| `LocalizationCoreTests.cs` | 31/2 | 393 | 文化解析与回退链、模板渲染、复数、校验器 |
| `LocalizationWpfTests.cs` | 7/0 | 131 | `{loc:Tr}` 标记扩展、绑定源、字体策略 |
| `LanguageSwitchBehaviourTests.cs` | 2/0 | 119 | 已构造的 VM 与已打开窗口在切换语言后的表现 |
| `LocalizationAuditTests.cs` | 4/0 | 205 | 三条发布门禁 + 一条基线棘轮（§13.1.10） |
| `LocalizationDumpTests.cs` | 1/0 | 56 | 导出剩余硬编码字符串清单（`GLOAM_DUMP_LOCALIZATION`） |

##### 簇 9 · UI / 布局 / WPF（9 文件，1,611 行，24 F / 1 T）

| 文件 | F/T | 行 | 钉住的行为 |
|---|---|---|---|
| `LayoutOverflowTests.cs` | 0/1 | 128 | 三种语言下主界面无单行文本被裁：按元素自身字体量 `FormattedText.WidthIncludingTrailingWhitespace` 与可用宽度比（理论数据 `zh-Hans`/`en-US`/`qps-ploc`） |
| `ReportPrintPaginationTests.cs` | 3/0 | 217 | 报告导出崩溃的回归：`FlowDocument` 分页器 + XPS 写出，逐页物化（§13.1.10） |
| `DashboardViewModelTests.cs` | 14/0 | 278 | 「明天 7 点」边界、有效色温文本、运行中应用保留选择、路径归一化、更新命令表面 |
| `DashboardAppPickerWpfTests.cs` | 1/0 | 415 | 应用选择器跨实时刷新保持打开、Add 按钮持久化输入（真窗口） |
| `ReportHistoryWindowTests.cs` | 1/0 | 53 | 选中项决定报告动作可用性 |
| `UiSnapshotTests.cs` | 1/0 | 166 | `GLOAM_CAPTURE_UI=1` 时渲染真窗口到 PNG（§13.1.11） |
| `ProbePlacementControlTests.cs` | 1/0 | 40 | 配置 + 微调共享同一放置状态 |
| `ProbePlacementDragTests.cs` | 2/0 | 114 | 放置面拖拽探针目标、Begin/Back 按钮 |
| `UtilityWindowXamlTests.cs` | 1/0 | 31 | `CcssBrowser` 在共享 chrome 内用 XAML body |

##### 簇 10 · 更新、诊断与平台管线（5 文件，1,400 行，52 F / 6 T）

| 文件 | F/T | 行 | 钉住的行为 |
|---|---|---|---|
| `UpdateServiceTests.cs` | 23/1 | 409 | 同版本/降级/包 id 不符/缺 SHA256 一律忽略；节流与失败退避；退出时调度；通知去重；诊断状态文件；暂存包清理 |
| `DiagnosticsBundleTests.cs` | 15/0 | 363 | 路径与用户名脱敏、显示器标识脱敏、CSV 字段转义、manifest 拓扑、唯一包名 |
| `StartupManagerTests.cs` | 1/5 | 66 | Run 键修复**决策逻辑**（注册表读写本身不测） |
| `ServiceDisposalTests.cs` | 1/0 | 52 | 定时器类服务对托盘/容器关闭幂等 |
| `LatestValueCoalescerTests.cs` | 12/0 | 308 | 单次执行、末值胜出、并发不丢更新、同键取消、`Dispose` 后拒绝 |

##### 簇 11 · Golden 回放（2 文件，409 行，1 F / 8 T）

| 文件 | F/T | 行 | 钉住的行为 |
|---|---|---|---|
| `GoldenReplayTests.cs` | 1/5 | 166 | Tier A：录制数据回放，逐数字对基线（§13.1.9） |
| `GoldenClosedLoopModelTests.cs` | 0/3 | 197 | Tier B：拟合面板模型，断言闭环不变量（§13.1.9） |

##### 分簇汇总

| 簇 | 文件 | F | T | 方法 | 行 |
|---|---:|---:|---:|---:|---:|
| 0 基建 | 7 | 1 | 0 | 1 | 692 |
| 1 Gamma/LUT/色彩 | 10 | 259 | 15 | 274 | 4,468 |
| 2 校准数学 | 13 | 215 | 5 | 220 | 5,774 |
| 3 校准流程 | 21 | 200 | 15 | 215 | 4,908 |
| 4 设置/序列化 | 10 | 80 | 14 | 94 | 2,159 |
| 5 夜间模式 | 10 | 121 | 11 | 132 | 2,260 |
| 6 Gamer Mode | 4 | 43 | 4 | 47 | 1,340 |
| 7 HDR | 9 | 84 | 10 | 94 | 2,544 |
| 8 本地化 | 5 | 45 | 2 | 47 | 1,081 |
| 9 UI/WPF | 9 | 24 | 1 | 25 | 1,611 |
| 10 更新/诊断 | 5 | 52 | 6 | 58 | 1,400 |
| 11 Golden | 2 | 1 | 8 | 9 | 409 |
| **合计** | **105** | **1,125** | **91** | **1,216** | **28,646** |

#### 13.1.8 硬件依赖：谁需要真显示器 / 色度计

这是本文最需要说清楚的一节，因为「测试要硬件」是最常见的误解。

**结论一：没有任何测试被标记跳过。** 全库 grep `Skip =`、`Trait(`、`SkippableFact`、
`RequiresDisplay`、`RequiresHardware` 均为 0 命中。所有 1,216 个方法在 `dotnet test` 下都会执行。

**结论二：没有任何测试访问物理显示器或物理色度计。** 证据：

- 全库唯一的 `DllImport` 在 `IccColorManagementRegressionTests.cs:164-185`，载入的是 `mscms.dll`
  （Windows 色彩管理系统），不是显示驱动或 HID 仪表。
- 仪表路径全部走替身：`SpotreadSessionTests` 明确是「fake-process harness」；
  `CalibrationOrchestratorAveragingTests` 用脚本化回读的假色度计；
  `ClosedLoopCorrectorTests` 用模拟显示器；`ColorimeterDetectionTests` 只解析文本。
- 显示与信号状态走纯数据：`DxgiColorSpaceInfoTests` 解码结构体，`NativeGammaRampTests` 只算数组，
  `MonitorInfoTests` 只做数值清洗，`StartupManagerTests` 只做决策（注释说明注册表 I/O 不测）。
- 真实面板只以**录制文件**形式进入测试：`tests/Gloam.Tests/Fixtures/Golden/*`（§13.1.9）。

**结论三：一部分测试需要 Windows 桌面会话，不是需要显示器。** 13 个文件 `using System.Windows`
（簇 0 的 4 个、簇 8 的 `LocalizationWpfTests`/`LanguageSwitchBehaviourTests`、簇 9 的 9 个、簇 3 的
`CalibrationChartsTests`）。它们创建真窗口、在屏幕外布局并用 `RenderTargetBitmap` 栅格化；
`ReportPrintPaginationTests` 还驱动 PTS 分页器与 XPS 写入器。`CONTRIBUTING.md:33-35` 的说法是
「需要桌面会话，不能无头运行」——这是仓库自己的判断，本文照录。

**结论四：硬件在发布流程里，不在测试里。** `RELEASE.md:86-90` 把「色度计复验」列为**手动**发布
检查项，且只在变更影响测量行为（校准数学、LUT 生成、漂移处理、精修）时执行，并明确要求
「跳过就在发布说明里写跳过，不要含糊地不打勾」。这是测试套件无法替代的那部分验证。

**结论五：少数测试是宿主依赖型而非纯 CPU。** `IccColorManagementRegressionTests` 依赖
`mscms.dll` 与系统 `spool\drivers\color\sRGB Color Space Profile.icm`；找不到模板或系统 profile 时
直接 `return`（`IccColorManagementRegressionTests.cs:20-24,137-146`）——这是仓库里唯一的「软跳过」
写法，不通过 xUnit 的 Skip 机制，因此在测试报告里表现为通过。

#### 13.1.9 Golden fixture 机制（真实面板的回归钉）

**目录与文件。** `tests/Gloam.Tests/Fixtures/Golden/` 下两个面板，各 4 个文件，共 9 个夹具文件：

| 夹具 | 面板 | 目标 | HDR | 仪表 |
|---|---|---|---|---|
| `gigabyte-m27q-p` | Gigabyte M27Q P (IPS) | HDR Desktop PQ (sRGB gamut) | 是 | i1 DisplayPro，无校正 |
| `msi-mag271qpx28` | MSI MAG 271QPX QD-OLED | HDR Desktop PQ (sRGB gamut) | 是 | i1 DisplayPro，无校正 |

`manifest.json` 字段（`Fixtures/Golden/*/manifest.json`）：`SchemaVersion`、`PanelLabel`、
`TargetName`、`HdrMode`、`SdrWhiteNits`、`InstrumentModel`、`HasMeterCorrection`、`NativeCsv`、
`VerificationCsv`。`baseline.json` 是 CI 要守住的数字（例如 MSI 的 `PeakLuminanceNits=207.828644`、
`AverageDeltaE=0.9376444969783342`、`HdrMeasuredPeakNits=352.953158`、`HdrWireExact=true`、
17 个 `HdrLutSamples`）。

**发现规则 = 没有注册表。** `GoldenReplayTests.cs:41-53` 枚举 `Fixtures/Golden` 下的目录，凡是同时
含 `manifest.json` 与 `baseline.json` 的即为一个用例；`AtLeastOneGoldenFixtureExists`
（`:55-60`）先保证「装置没在空跑」。`GoldenClosedLoopModelTests.HdrFixtures()` 只取
`HdrMode=true` 的夹具。

**Tier A：开环回放（`GoldenReplayTests.cs`）。** 把两个 CSV 重新导入 → `GoldenSampleBaseline.Compute`
（`src/Gloam.Core/Calibration/GoldenSampleBaseline.cs:85`）→ 与 `baseline.json` 逐字段比。容差是显式
常量（`:23-28`）：ΔE2000 0.05、色度 0.0005、nits 相对 0.5%、γ 0.01、LUT 1e-4、tone 1e-4；
ITP 的 ΔE 放宽到 3×。四个 theory：`Characterization_MatchesBaseline`、`VerifyMetrics_MatchBaseline`、
`Uncertainty_MatchesBaseline`、`HdrLutBuild_MatchesBaseline`（SDR 夹具直接 `return`），外加
`RecordedMeasurements_AreInternallyConsistent`——录制本身损坏要在这一层响亮失败，而不是表现为
基线不符。

**Tier B：模型化闭环（`GoldenClosedLoopModelTests.cs`）。** 录制是开环的，无法回答「换一种校正
面板会怎样」，所以测试从录音拟合一个单调分段线性面板模型（`FittedHdrPanel`，`:44-83`），先跑
`FittedModel_ReproducesTheRecording_SelfCheck` 证明模型能复现训练数据（`:105-116`），再断言闭环
**不变量**：真实录音算出的 LUT 把拟合面板拉到绝对 PQ 的 5% 以内（`:118-139`）；注入 +6%→−3% 的
对数-nit 漂移后，`HdrRefinementLoop` 必须在 3 轮内收敛、`FinalAvgAbsError < 0.01`、且显示器最终
装的就是它报告的那份 LUT（keep-best，`:141-217`）。文件头明确写了「模型推断，不当作测量事实」。

**与 CLI 的闭环。** 基线由 `dotnet run --project src/Gloam.Cli -- golden-ingest <fixtureDir>` 生成
（`src/Gloam.Cli/Program.cs:37,318`；`docs/golden-fixture-contribution.md:72-103`）。同一命令对着
已有夹具目录不带参数运行即原地重算 `baseline.json`（`:160-170`），文档把这件事定义为「刻意行为，
diff 要在 review 里读」，并禁止「为了让红变绿而重算」。

**为什么只有两块面板。** `docs/golden-fixture-contribution.md:8-11` 与 `README.md:126-131` 说得很直：
两块中端 LCD 已钉住，OLED/QD-OLED/广色域/mini-LED 的代码路径**存在但没有夹具钉住**。贡献流程
（需要仪表、30 分钟预热、Thorough/Full 预设、两个 CSV、`manifest.json` 四个文件）写在
`docs/golden-fixture-contribution.md`，且 `.gitignore:53-58` 为
`tests/Gloam.Tests/Fixtures/**/*.csv` 开了 `*.csv` 的显式例外——真实录音必须进仓。

#### 13.1.10 已发布主张的回归钉

| 主张 | 钉住它的测试 | 机制 |
|---|---|---|
| 「校准数字来自真实面板，改了就会被发现」 | `GoldenReplayTests` + `GoldenClosedLoopModelTests` | 录制回放 + 模型不变量，容差显式（§13.1.9） |
| 「导出报告不会崩」 | `ReportPrintPaginationTests.cs:181-233` | 构造满内容 `FlowDocument`，设 Letter 页 816×1056、边距 48，`ComputePageCount` 后逐页 `GetPage`，可再走 XPS writer（`PrintDialog.PrintDocument` 驱动的同一引擎） |
| 「界面全部走 catalog，新增硬编码会被拦」 | `LocalizationAuditTests.cs` | 4 条：XAML 引用的 key 必须存在（`:66-87`）、catalog key 必须被引用（`:145-174`）、非复数译文不得与英文源逐字相同（白名单 `UntranslatableByDesign`，`:97-143`）、硬编码基线只降不升（`:176-222`，基线现为 54 文件 / 492 条） |
| 「HDR 下 ICC 色彩管理仍给 Photoshop 类应用正确中灰」 | `IccColorManagementRegressionTests.cs:17-48` | 用 `Mhc2ProfileBuilder.Build` 造身份矩阵 + 线性 LUT 的「安全」profile，再把 TRC 换成 PQ 造「旧式」profile，经真实 CMM 变换 50% 灰：安全分支落在 0.47–0.53，旧分支必须偏亮 >0.08 |
| 「中文界面不截断」 | `LayoutOverflowTests` + `PseudoLocalizer` | 三语言逐 `TextBlock` 量宽；伪语言 +40% 是真正的压力用例 |
| 「GDI 伽马斜坡不会把面板推到损坏区」 | `NativeGammaRampTests`（`GdiEnvelope_*`） | 包络上下界与实测验证规则绑定 |
| 「亮部不被阴影调整牺牲」 | `HdrHighlightTests` | 满亮度/降亮/仅阴影三条路径都断言高光不动 |

#### 13.1.11 文档图与截图：两个「不是测试的测试」

- `UiSnapshotTests.Capture_window_snapshots_for_documentation` 在 `GLOAM_CAPTURE_UI=1` 时才工作
  （`UiSnapshotTests.cs:37-41`），否则立即返回。它渲染 dashboard/settings/gamelab 的
  en-US、zh-Hans、qps-ploc 三语版本，每个窗口先出视口图、再展开所有 `Expander` 出 `-full`、
  必要时滚到底出 `-bottom`（`:105-152`）。注释解释为什么必须 `-full`：窗口定高内部滚动，
  只截视口会把折叠区域藏起来。
- `DocumentationFigures.Compose_comparison_figures` 需要 `GLOAM_FIGURES=1`，读取
  `GLOAM_SHOTS_DIR` 的截图，输出到 `GLOAM_FIGURES_DIR`，合成 `fig1-dashboard`、`fig2-gamelab`、
  `fig3-settings`、`fig4-pseudo` 四张对比图并画 callout 框（`DocumentationFigures.cs:40-96`）。
  注释写明目的是让文档图「从当前二进制重新生成，而不是手工拼」，否则「文档图会展示一个已经没人
  发布的版本」。
- 这两者的 `[Fact]` 计入 §13.1.6 的 1,216，但在 CI 里是**无条件通过的空操作**——这一点值得记住，
  因为它让「测试数量」并不能线性换算成「断言数量」。

#### 13.1.12 已知缺口

1. **CI 不收集覆盖率。** `coverlet.collector` 参与编译（`Gloam.Tests.csproj:21-24`），但
   `.github/workflows/build.yml:47` 只是 `dotnet test … --no-restore`，没有 `--collect`，
   也没有覆盖率门禁。包的存在只是给本地 `dotnet test --collect:"XPlat Code Coverage"` 备用。
2. **没有 skip/条件执行机制。** 所有测试无条件运行，意味着任何「只在本机成立」的假设都会成为
   CI 的硬依赖；`IccColorManagementRegressionTests` 的静默 `return` 是唯一例外，也因此它的失败
   不会被报告为跳过。
3. **Tier B 的结论是模型推断**，文件头已自陈；把它读成「面板实测」是误读。
4. **Golden 覆盖面窄。** 只有两块 HDR LCD；OLED 的 ABL/像素级行为仅由 `HighAplSettleTests`
   的合成瞬态覆盖。
5. **WPF 测试的宿主假设未在 CI 验证过**（本文未运行流水线）：仓库文档称需桌面会话，
   `windows-latest` 是否满足只能由一次真实运行回答。

---

### 13.2 构建系统

#### 13.2.1 `Directory.Build.props`（27 行）

| 属性 | 值 | 说明 |
|---|---|---|
| `ManagePackageVersionsCentrally` | `true` | 中央包管理，版本只在 `Directory.Packages.props` |
| `Nullable` / `ImplicitUsings` | `enable` | 全仓统一 |
| `EnableNETAnalyzers` | `true` | 分析器随 SDK |
| `AnalysisLevel` | `10.0-recommended` | **钉死而非 `latest-recommended`**：注释记录了一次 SDK 升级带来 48 条新诊断，而 `GloamStrictBuild` 视警告为错误，于是「没人改的代码红了」。升级这条值必须与修复同批提交（`:7-12`） |
| `AnalysisMode` | `Default` | — |
| `EnforceCodeStyleInBuild` | `true` | `.editorconfig` 的风格规则在 build 中生效 |
| `Deterministic` | `true` | 确定性编译 |
| `ContinuousIntegrationBuild` | `CI=true` 时为 `true` | 归一化路径，利于确定性与 SourceLink |
| `EnableWindowsTargeting` | 非 Windows 主机为 `true` | 允许非 Windows 机器还原/构建 Windows TFM |
| `KnownAnalyzerBacklog` | 14 个规则：`CA1305;CA1310;CA1401;CA1510;CA1513;CA1707;CA1711;CA1712;CA1716;CA1805;CA1822;CA1826;CA1838;CA1859;CA1861` | 已审阅的历史遗留，作为 `NoWarn` 加入——**但仅当 `GloamAnalyzerAudit != true`**。设 `GloamAnalyzerAudit=true` 可让这批重新暴露，用于清账 |
| `GloamStrictBuild` | 开关（默认关） | 为 `true` 时 `TreatWarningsAsErrors=true`，但 `WarningsNotAsErrors=NU1901;NU1902;NU1903;NU1904`（NuGet 漏洞告警不升级为错误） |

#### 13.2.2 `Directory.Packages.props`（13 行）

| 包 | 版本 | 使用者 |
|---|---|---|
| `CommunityToolkit.Mvvm` | 8.4.2 | `Gloam` |
| `Hardcodet.NotifyIcon.Wpf` | 1.1.0 | `Gloam` |
| `Microsoft.Extensions.DependencyInjection` | 8.0.1 | `Gloam` |
| `Velopack` | 1.2.0 | `Gloam`、`Gloam.Tests`（更新服务与替身） |
| `Microsoft.NET.Test.Sdk` | 18.6.0 | `Gloam.Tests` |
| `xunit` | 2.9.3 | `Gloam.Tests`（xUnit v2，非 v3） |
| `xunit.runner.visualstudio` | 3.1.5 | `Gloam.Tests` |
| `coverlet.collector` | 10.0.1 | `Gloam.Tests` |

#### 13.2.3 `global.json`（9 行）

```json
{ "sdk": { "version": "10.0.100", "rollForward": "latestFeature", "allowPrerelease": false } }
```

注释说明这是**下界而非钉版**：`latestFeature` 允许 10.0.x 内的特性带升级，但拒绝 SDK 11/12——
「未来安装的 SDK 11 或 12 会被拒绝而不是偷偷接管构建」。与 §13.2.1 的 `AnalysisLevel` 钉死是同一
防御目标的两端。

#### 13.2.4 `.editorconfig` 与 `.gitattributes`

`.editorconfig`（61 行）：`root = true`，全局 UTF-8 + LF + 末尾换行 + 去尾空格；C# 4 空格、
`System` 指令优先；少量风格建议（`var` 仅类型明显处、属性可用表达式体、方法不用）；显式诊断级别
`CA1416`/`CA2000`/`CA2201`/`CA2213` = warning，`CA1031`/`IDE0005` = silent；**测试目录单独放宽**：
`[tests/**/*.cs]` 里 `CA2000`（一次性的测试替身）与 `CA2016`（故意的取消令牌观察选择）设为 `none`
（`.editorconfig:47-53`）。注释明确禁止在全局按类别压分析器：「那会连新的安全/可靠性发现一起藏掉」。

`.gitattributes`（30 行）：`* text=auto eol=lf`；`*.ps1`/`*.cmd`/`*.bat` 强制 CRLF（cmd.exe 处理裸
LF 有问题）；ICC/图标/图片/字体/exe/dll 全部标 `binary`；`package-lock.json` 标
`-diff linguist-generated`、`site/assets/katex/**` 标 `-diff linguist-vendored`。动机写在头部注释：
`.editorconfig` 是编辑器约定，git 不读。

#### 13.2.5 解决方案与项目依赖图

`src/Gloam.sln` 含 8 个项目（7 个 `src/` + 测试）。依赖方向单调、无环：

```
Gloam.Interop (net10.0)          — 无项目引用
Gloam.Localization (net10.0)     — 无项目引用
        ↑
Gloam.Core (net10.0-windows)     → Gloam.Interop
Gloam.Localization.Wpf (net10.0-windows, UseWPF) → Gloam.Localization
        ↑
Gloam (WinExe, net10.0-windows, UseWPF)
    → Gloam.Core, Gloam.Interop, Gloam.Localization, Gloam.Localization.Wpf
Gloam.Cli (net10.0-windows)      → Gloam.Core
Gloam.DebugMonitors (net10.0-windows) → Gloam.Core, Gloam.Interop
Gloam.Tests (net10.0-windows, UseWPF)
    → Gloam.Core, Gloam.Localization, Gloam
```

> 口径差异：`docs/project-structure.md:3` 说「五个项目加测试」，实际是 **7 个 src 项目 + 测试**。
> 该文档的目录树（`:6-27`）也只列了 5 个。`Gloam.Cli` 与 `Gloam.DebugMonitors` 是文档未提的
> 两个开发者工具项目。这是文档与现实的漂移，按实测记录。

`src/Gloam/Gloam.csproj` 的其余关键点：`AssemblyName=Gloam`（`/Gloam;component/` pack URI 是运行时
身份）、`StartupObject=Gloam.Shell.Program`（Velopack 钩子必须在 WPF 启动前跑）、`Version=1.10.0`、
6 个 ICM 模板同时以 `EmbeddedResource` 与 `None CopyToOutputDirectory` 两种方式带上（注释解释：
发布目录被 `robocopy /MIR` 镜像时会删掉运行时解出的模板，导致「No MHC2 template found」）、
`Resources/i18n/*.json` 内嵌为基线 catalog、`PublishTrimmed=false`。

#### 13.2.6 构建与测试入口

| 目的 | 命令 | 出处 |
|---|---|---|
| 本地运行 | `dotnet run --project src/Gloam` | `README.md:142-146` |
| 跑测试 | `dotnet test src/Gloam.sln -c Release` | `CONTRIBUTING.md:29-31` |
| 严格构建 | `dotnet build src/Gloam.sln -c Release -p:GloamStrictBuild=true` | `CONTRIBUTING.md:39-41` |
| 无头 CLI 自检 | `dotnet run --project src/Gloam.Cli -- check-lut 2.2 200 --algorithm perceptual --temp-k 2700 --json …` | `README.md:156-159` |
| 打包 | `.\package.ps1 -Version X.Y.Z` | `README.md:150-152` |
| `.simplify` 检视 | `dotnet test src/Gloam.sln -c Release --nologo --logger "console;verbosity=normal"` | `.simplify/config.json:3` |

#### 13.2.7 一个必须说清的接线缺口

`GloamStrictBuild` 只出现在**文档与清单**里：`RELEASE.md:83`、`CONTRIBUTING.md:40`、
`docs/project-structure.md:73`、`ai/memory-bank/…:87`。`grep` 全仓（`.github/` 含）确认
**`build.yml` 没有任何一步传 `-p:GloamStrictBuild=true`**。因此 CI 的「Test」步骤是在非严格模式下
构建的（警告不致命），严格构建目前是 PR 模板与发布清单里的**人工承诺**，不是流水线门禁。
这是一条可复核的事实，也是本架构文档应当指出的一处流程与文档不一致。

---

### 13.3 `package.ps1` 与发布校验脚本

#### 13.3.1 `package.ps1`（170 行）

参数（`:2-15`）：`-Version`（空则读 `Gloam.csproj` 的 `<Version>`）、`-PublishOnly`（CI 用：只出
publish 目录，`vpk pack` 由工作流自己调，签名也归工作流）、`-PublishDir`（默认 `publish`）、
`-ReleaseDir`（默认 `Releases`）。

**步骤 1 · 供应链安全前置。** `Resolve-RepoChildPath`（`:28-58`）把每个可写路径解析成绝对路径，
拒绝仓库根、拒绝逃出仓库——因为脚本对 `publish`/Argyll 解压目录做**递归删除**。
`Download-Argyll`（`:71-96`）：只有缓存 zip 不存在时才下载
`https://www.argyllcms.com/Argyll_V3.5.0_win64_exe.zip`，**先校验 SHA-256**
`C80F78BA30E715079A00A14C2E7C9F533C58E6C8FDBC27F2D32B3D7813F9D132`（`:24,80-83`），
不匹配直接抛；校验通过后**从零重新解压**（先删解压目录），再确认 `bin\dispwin.exe` 与
`bin\spotread.exe` 存在。注释写的是「永远不要对未校验内容 `Expand-Archive`，永远不要信任已存在的
缓存——被投毒的缓存会被打进签名发布」。

**步骤 2 · publish。** `Remove-RepoChildDirectoryIfExists $PublishDir` 清空输出，然后：

```powershell
dotnet publish src\Gloam\Gloam.csproj -c Release -r win-x64 --self-contained true
    --output publish /p:Version=<v> /p:DebugType=None
```

注释交代为什么**不是** `PublishSingleFile`：Velopack 的 delta 与解压机制按单个文件工作，还要在应用
exe 旁放自己的 `Update.exe`（`:100-102`）。

**步骤 3 · 把文档与 Argyll 塞进 publish。** 复制 `README.md`、`LICENSE.txt`、
`THIRD_PARTY_NOTICES.txt`（`:125-127`），把整棵 `argyll_cache/Argyll_V3.5.0` 复制到
`publish\argyll_cache\Argyll_V3.5.0`（`:129-133`）。ICM 模板由 csproj 已在输出里。

**步骤 4 · 立即验证 publish 产物。** 调用 `scripts\Test-PublishOutput.ps1`（`:135`）。
若带 `-PublishOnly` 到此返回（`:137-140`）。

**步骤 5 · 确保 `vpk` 可用。** 不在 PATH 就 `dotnet tool install -g vpk` 并把
`%USERPROFILE%\.dotnet\tools` 加进 PATH（`:142-147`）。

**步骤 6 · `vpk pack`。**（`:157-167`）

```powershell
vpk pack --packId "GloamApp" --packVersion <v> --packDir publish --mainExe "Gloam.exe"
         --packTitle "Gloam" --packAuthors "David Torcivia"
         --outputDir Releases --icon src\Gloam\app.ico
```

`packId` 为什么是 `GloamApp` 而不是 `Gloam`，注释写得很具体：Velopack 拥有并会在卸载时整棵删除
`%LocalAppData%\<packId>`，而应用数据在 `%LocalAppData%\Gloam`（设置、日志、校准报告）；
用不同 id 让卸载永远删不到校准数据。用户看到的名字是 `--packTitle`。

**签名在哪里。** 本地运行的 `package.ps1` **不签名**（本地产物未签名）；签名只在 CI 的 tag 路径上，
由工作流调 `vpk pack --azureTrustedSignFile azure-sign.json` 完成（§13.4.1）。这一分工写在脚本注释与
`RELEASE.md` 里，读代码时不要误以为 `package.ps1` 会签名。

#### 13.3.2 `scripts/` 下的四个发布校验脚本

| 脚本 | 输入 | 断言 | 失败模式 |
|---|---|---|---|
| `Test-PublishOutput.ps1`（156 行） | `-PublishDir`、`-ArgyllVersion`（默认 `Argyll_V3.5.0`）、`-SkipLaunchSmoke` | 13 个必需文件存在且非空（3 个 dll/exe + 3 个文档 + 6 个 ICM + Argyll 的 `dispwin.exe`/`spotread.exe`）；`THIRD_PARTY_NOTICES.txt` 含 ArgyllCMS/AGPL/Velopack；≥6 个 `.icm`；Argyll 未被错误嵌套；**启动冒烟**：`Gloam.exe --smoke --data-dir <temp>` 15 秒内退出码 0、生成隔离 `app.log` 且含 `Launch smoke: completed.` | 收集全部失败后一次性抛（`:151-154`），不是遇错即停 |
| `Test-SignedReleaseArtifacts.ps1`（142 行） | `-ReleaseDir`、`-Version`、`-ExpectedPublisher`（默认 `David Torcivia`）、`-ExpectedPackId`（`GloamApp`）、`-ExpectedChannel`（`win`） | `GloamApp-win-Setup.exe` / `-Portable.zip` / `GloamApp-<ver>-full.nupkg` 各恰好一个；Setup 的 Authenticode 状态必须 `Valid` 且签名主体含期望发布者；解压 portable 与 nupkg，其中每个 `Gloam.exe` 都要有效签名；nuspec 的 `id`/`version` 匹配 | 缺件/多件/未签名/主体不符分别报错 |
| `Test-UpdateFeedConsistency.ps1`（51 行） | 无（默认期望 `https://github.com/halideworks/gloam`、`GloamApp`） | 读 4 个文件做**文本一致性**：`UpdateService.RepoUrl` 常量值、`RELEASE.md` 提到该 repo、`package.ps1` 用 `--packId "GloamApp"` 且 `-Version` 默认为空、`build.yml` 的 pack/download/upload 都指向 `${{ github.repository }}` | 见 §13.3.3 的路径问题 |
| `Test-VelopackInstalledUpdate.ps1`（95 行） | `-OlderSetupPath`（必填）、`-ExpectedVersion`（空则读 csproj）、`-TimeoutSeconds`（300）、`-IUnderstandThisModifiesLocalInstall`（必填闸门） | 静默安装旧版 → 启动 → 轮询 `%LOCALAPPDATA%\Gloam\update-state.json` 直到 `LastScheduledVersion`/`PendingRestartVersion`/`InstalledVersion` 命中目标版本 → 停进程等 Velopack 落 `packages\GloamApp-<v>-full.nupkg` → 再启一次确认 `InstalledVersion` | 无 `-IUnderstandThisModifiesLocalInstall` 时直接抛（`:14-16`），因为它真的会改写本机安装 |

#### 13.3.3 一个静态发现：`Test-UpdateFeedConsistency.ps1` 读的是旧路径

脚本第 16 行：`$updateService = Read-RepoFile "src/Gloam/Services/UpdateService.cs"`。
实际文件在 `src/Gloam/Infrastructure/Platform/UpdateService.cs`，`src/Gloam/Services/` 目录不存在
（`Test-Path` 实测 `False`）。由于脚本 `$ErrorActionPreference = "Stop"`，而 `Read-RepoFile` 直接
`Get-Content -Raw -LiteralPath`，**缺失文件会让这一步抛终止错误**——即 CI `validate` job 的
「Update feed consistency」步骤在当前代码状态下应当失败（推断，未执行流水线确认）。
`UpdateService.RepoUrl` 的常量值本身仍是期望值（`src/Gloam/Infrastructure/Platform/UpdateService.cs`
中 `private const string RepoUrl = "https://github.com/halideworks/gloam";`），说明脚本意图没错、
只是重构（更新服务移位到 `Infrastructure/Platform`）后没跟着改路径。
`RELEASE.md`、`package.ps1`、`build.yml` 三处文本检查未受影响。

---

### 13.4 CI/CD

#### 13.4.1 `.github/workflows/build.yml`（191 行）

**触发与权限。** `push.tags: v*`、`pull_request.branches: [main]`、`workflow_dispatch`
（`:6-12`）；顶层 `permissions.contents: read`。两个 job。

**`validate`（`windows-latest`，`contents: read`）** —— 6 步：

| # | 步骤 | 命令 |
|---|---|---|
| 1 | Checkout | `actions/checkout@v6` |
| 2 | Setup .NET | `actions/setup-dotnet@v5`，`dotnet-version: 10.0.x` |
| 3 | Restore | `dotnet restore src/Gloam.sln` |
| 4 | Test | `dotnet test src/Gloam.sln --configuration Release --no-restore` |
| 5 | Headless CLI LUT check | 建 `artifacts` 目录后 `dotnet run --project src/Gloam.Cli/Gloam.Cli.csproj --configuration Release --no-restore -- check-lut 2.2 200 --algorithm perceptual --temp-k 2700 --json artifacts/cli-lut-check.json` |
| 6 | Vulnerability scan | `dotnet list src/Gloam.sln package --vulnerable --include-transitive` |
| 7 | Update feed consistency | `./scripts/Test-UpdateFeedConsistency.ps1` |
| 8 | Upload validation artifacts | `if: always()`，`actions/upload-artifact@v7`，`name: Gloam_Validation`，`path: artifacts/*`，`if-no-files-found: ignore` |

（表中 1–8 为实际步骤数；「6 步」指后 5 条内容性门禁加 checkout/setup。）

**`build`（`needs: validate`）** —— 权限 `contents: write`（发 Release）+ `id-token: write`
（OIDC，无存储密钥）。`environment:` 用了表达式（`:80`）：

```yaml
environment: ${{ startsWith(github.ref, 'refs/tags/v') && 'release' || '' }}
```

注释解释：tag 构建落在 `release` 环境，OIDC 的 subject 恰好是
`repo:halideworks/gloam:environment:release`；Azure 联邦凭据**精确匹配**该 subject，无法像
`refs/tags/v*` 那样通配。非 tag 运行解析为空环境名。

步骤（`:82-191`）：Checkout → Setup .NET → **Derive version**（tag 则剥 `v` 并校验
`^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$`，否则用合成版本 `0.0.<run_number>`）→
**Publish + bundle assets**（`./package.ps1 -Version <v> -PublishOnly`）→ **Install Velopack CLI**
（`dotnet tool install -g vpk`）。之后按 tag / 非 tag 分叉：

*tag 路径（发布）*：`Azure login (OIDC)`（`azure/login@v3`，三个 `vars.AZURE_*`）→
`Write Azure signing metadata`（写 `azure-sign.json`，含 Endpoint/CodeSigningAccountName/
CertificateProfileName）→ `Download previous release (enables deltas)`
（`vpk download github --repoUrl … --token ${{ secrets.GITHUB_TOKEN }}`，`continue-on-error: true`，
首个 release 无物可下）→ `Pack (signed)`（`vpk pack … --azureTrustedSignFile azure-sign.json`）→
`Verify signed release artifacts`
（`./scripts/Test-SignedReleaseArtifacts.ps1 -ReleaseDir Releases -Version <v>`）→
`Upload to GitHub Release`（`vpk upload github --publish --releaseName "Gloam <v>" --tag <tag> --merge`）。

*非 tag 路径（PR/手动）*：`Pack (validation, unsigned)`（同样的 `vpk pack`，无签名）→
`Upload build artifacts`（`name: Gloam_Velopack`，`path: Releases/*`）。

需要预置的仓库配置在 `:17-26` 注释：6 个 **Variables**（不是 Secrets，因为 OIDC 无需存储签名密钥）
`AZURE_CLIENT_ID`、`AZURE_TENANT_ID`、`AZURE_SUBSCRIPTION_ID`、`AZURE_SIGN_ENDPOINT`、
`AZURE_SIGN_ACCOUNT`、`AZURE_SIGN_PROFILE`，以及 Entra 应用的「Trusted Signing Certificate Profile
Signer」角色与限定到 `refs/tags/v*`（注释口径）的联邦凭据。注意这里与 `RELEASE.md:40-47` 的表述
有细微不一致：workflow 注释说联邦凭据 scope 到 `ref:refs/tags/v*`，而 workflow 用 `release` 环境，
README 的 `environment:release` subject 才是实际匹配项；`RELEASE.md` 的描述与环境用法一致。
按静态阅读，`RELEASE.md` 的说法更符合 workflow 的实际行为，workflow 头部注释是旧描述。

#### 13.4.2 门禁链（一段话）

一次 push 到 `main` 的 PR 要过：`restore` → `dotnet test`（Release，1,216 个方法、含 WPF 与
本地化审计）→ 无头 CLI `check-lut` 产出 JSON → `dotnet list package --vulnerable`（传递依赖）→
`Test-UpdateFeedConsistency.ps1` 文本一致性 → 上传 `Gloam_Validation`；通过后 `build` job 再
`package.ps1 -PublishOnly`（内含 Argyll SHA-256 校验与 `Test-PublishOutput.ps1` 启动冒烟）→
`vpk pack`（PR 未签名、产物归档；tag 则 OIDC 登录 + Azure Trusted Signing 签名 →
`Test-SignedReleaseArtifacts.ps1` 验签 → `vpk upload github --publish`）。严格构建
（`GloamStrictBuild=true`）**不在链上**（§13.2.7），也没有覆盖率门禁（§13.1.12）。

#### 13.4.3 `.github/workflows/deploy-site.yml`（71 行）

- 触发：`push.branches:[main]` + `paths: ['site/**', '.github/workflows/deploy-site.yml']` +
  `workflow_dispatch`。注释：仓库同时放着应用，应用发布不该重部署网站。
- `concurrency: group: deploy-site, cancel-in-progress: true`。
- 单 job `deploy`（`ubuntu-latest`）：`actions/checkout@v4` → `cloudflare/wrangler-action@v3`
  （`command: pages deploy site --project-name=gloam --branch=main`，用
  `secrets.CLOUDFLARE_API_TOKEN` + `vars.CLOUDFLARE_ACCOUNT_ID`）。
- **部署后验证线上**（`:49-71`）：用 `curl -sI` 检查 9 条——首页 200、`/guides` 200、
  `/whitepaper` 200、`/guides.html` 301 到 `/guides`、`/whitepaper.md` 的
  `content-type: text/markdown`、CSP 头存在、HSTS 头存在、`www` 301 到 apex；任一失败即 `exit 1`。
  注释说明为什么值得：Markdown content-type 与 CSP 在渲染页面上看不见。

#### 13.4.4 `.github/` 模板

| 文件 | 内容 |
|---|---|
| `PULL_REQUEST_TEMPLATE.md`（26 行） | 4 条通用清单（测试过、严格构建无警告、未弱化/跳过/删除测试、`src/Gloam.Core` 改动必须有测试）+ 一段**仅在校准数学改动时**保留的清单（先加 pinning 测试、更新 whitepaper 并重新生成 md、golden 通过或基线被刻意重算并解释） |
| `ISSUE_TEMPLATE/bug_report.yml`（117 行） | 必填：发生了什么、Gloam 版本、Windows 版本（含 build）、GPU 与驱动版本、显示型号、HDR 状态（下拉）、安装方式（下拉）、**诊断包已附**（勾选，`required: true`）；选填：校准上下文（仪表/目标/预设/`.ccss|.ccmx`）、其他色彩工具与多显示器情况 |
| `ISSUE_TEMPLATE/feature_request.yml` | 想做什么、想让它做什么、试过什么、显示与仪表 |
| `ISSUE_TEMPLATE/config.yml`（11 行） | 关闭空白 issue，给 3 个 contact link：troubleshooting、贡献 golden fixture、私密报告安全问题 |

#### 13.4.5 `RELEASE.md`（105 行）与发布说明约定

流程（`:8-20`）：推 `vX.Y.Z` tag → `build.yml` 在 `release` 环境跑 → `package.ps1 -PublishOnly` →
OIDC 登录 + `vpk pack --azureTrustedSignFile` 签名安装包与所有 bundled exe →
`vpk upload github` 发版 → 产出 `GloamApp-win-Setup.exe`（自更新、per-user）与
`GloamApp-win-Portable.zip`。`UpdateService.RepoUrl` 与 CI 上传目标必须都是
`github.com/halideworks/gloam`。

一次性配置（`:22-47`）：GitHub 环境 `release`；6 个 Variables（表在 `:29-36`，三个 GUID
**故意不入库**）；Azure Trusted Signing 账户 `gloam-sign`（East US）+ 证书 profile
`gloam-public-trust`；Entra 应用 `gloam-github-signing` 持「Artifact Signing Certificate Profile
Signer」角色 + 联邦凭据（subject `repo:halideworks/gloam:environment:release`）。

发版步骤（`:49-67`）：main 绿 → 改 `Gloam.csproj` 的 `<Version>` 与 tag 对齐 → 写
`docs/release-notes/vX.Y.Z.md` → push main、tag、push tag → 观察 Actions → 用
`gh release edit vX.Y.Z --notes-file …` 补说明。`vpk upload github` 发布时 body 为空，所以说明
是单独一步；「v1.8.0 及以前的 release 因此完全没有说明」（`:71-73`）。

发布说明的写法约定（`:69-78`）：写给「决定要不要在意」的人，不写给读 diff 的人；**当改动影响
校准数学或测量输出时必须明说**——这是唯一一类用户可能需要重跑校准而不是静默更新的改动。

发版前清单（`:80-90`）：构建绿/全量测试过、严格构建干净、版本号与 tag 一致、发布说明已写、
**色度计复验**（仅当变更影响测量行为；跳过要写明）。

签名验证（`:92-105`）：Authenticode 有效 + 可信时间戳；安装无「Unknown Publisher」；旧版能检测、
下载并在重启时应用；本地端到端可用 `Test-VelopackInstalledUpdate.ps1`（§13.3.2）。另注：应用数据
`%LOCALAPPDATA%\Gloam` 与 Velopack 安装根 `%LOCALAPPDATA%\GloamApp` 分离，因此数据跨更新/卸载
存活。

---

### 13.5 工具脚本

#### 13.5.1 `scripts/render-comparison-images.py`（205 行）

把一张公有领域油画推过**真实 Windows-HDR 信号路径**两次，产出站点首页的对比对
（`site/assets/compare_windows_srgb.webp`、`compare_gloam_gamma2p2.webp`）。数学 1:1 移植自
`src/Gloam.Core/TransferFunctions.cs`、`LutGenerator.cs`、`Calibration/HdrMhc2LutBuilder.cs:104`
（文件头注释列出）。每像素每通道：`wire = PQ⁻¹(sdrWhite · srgbEotf(s))` → 量化到 10-bit 线 →
分支 `out = wire` 或 `out = lut1024[wire]`（GPU 式线性插值）→ `nits = PQ(out)` → 为普通 SDR
阅读器按 `(nits/sdrWhite) ** (1/VIEW_GAMMA)` 重编码。常量：`SDR_WHITE=200`
（`MonitorInfo.DefaultSdrWhiteLevel`）、`VIEW_GAMMA=2.2`、`LUT_N=1024`、`WIRE_BITS=10`、
`WEBP_QUALITY=92`、输出 1200×1500。

自检部分（`:169-205`）：打印 LUT 参考点与 `TransferFunctionTests` 对齐、打印 5%/10%/20%/40%/100%
信号处的发光比（站点引用 2.87/1.59/1.14/1.00）、打印 code-value 差值分布，并用 scipy 的
`gaussian_filter` 验证压缩后的 WebP 仍保留 8px 积分差值。这是「营销图不是手拼的」这一主张的
可执行证据。

#### 13.5.2 `scripts/build-whitepaper-md.py`（305 行）

把 `site/whitepaper.html` 的 `<main>` 转成 `site/whitepaper.md`（52,862 字节），供 agent/爬虫读。
设计要点：只认白皮书实际用到的标签词汇，遇到未知标签 `raise ValueError`（`:193-196`）——宁可构建
失败也不静默丢章节；`<sub>/<sup>` 转成 `_`/`^` 记号而不是拍平（注释：`m<sub>1</sub>` 拍平成
`m1` 会读成另一个变量）；公式取 `data-tex` 原文写成 ```math 代码块，跳过 KaTeX 渲染树与 MathML；
链接按 `#`/`.html`/`assets/` 三种情况改写成绝对 URL（`ORIGIN = "https://getgloam.org"`）；
输出带「生成文件，勿手改」头（`:293-299`）。

#### 13.5.3 `scripts/build-whitepaper-math.mjs`（66 行）与 `package.json`

`package.json`（8 行）只有 `devDependencies.katex: ^0.16.9`（`package-lock.json` 解析到 0.16.47），
`private: true`，说明字段写明「站点本身保持静态，除每页内联小脚本外没有运行时 JS」。

脚本把每个 `<div class="eq" data-tex="…">` 与 `<span class="m" data-tex="…">` 用 KaTeX
`renderToString`（`displayMode` 按元素类型、`throwOnError: true`、`strict: 'ignore'`、
`trust: false`、默认 `htmlAndMathml`）重新渲染并**原地写回** `whitepaper.html`
（291,984 字节）。idempotent：每次都从 `data-tex` 重建渲染 span。LaTeX 出错时打印
「LaTeX error in: …」、置 `process.exitCode = 1` 并保留原标记（`:29-42`），结尾再提示
「一部分表达式失败，页面部分未渲染」。这个脚本是 `site/` 唯一需要 npm 的环节。

#### 13.5.4 生成物与「忘记重新生成」的检测

`deploy/README.md:6-17` 明确：两个生成器都**手工跑、产物入库、不接 CI**；漏跑会表现为 diff 里的
过期输出，而不是坏部署。唯一的运行时验证是部署后 curl `content-type: text/markdown`
（`deploy-site.yml:67`）——因为 Markdown 类型是「生成 whitepaper.md 的全部理由」
（`site/_headers:25-30`）。

---

### 13.6 文档与站点

#### 13.6.1 `docs/*.md` 一览（各自 owning 什么）

| 文件 | 体量 | 拥有什么 |
|---|---|---|
| `project-structure.md` | 110 行 | 目录布局、feature-first 理由、命名空间与 global using 策略、工具链三条「为什么钉版本」、加 feature 的 4 步、测试并行策略 |
| `localization.md` | 222 行 | 本地化契约：为什么不用 `.resx`、两层架构（引擎 / WPF 适配）、解析与回退链、占位符与复数、加语言 4 步、热切换三态规则、字体策略、已知限制 |
| `golden-fixture-contribution.md` | 177 行 | 不用写代码就能贡献夹具的完整流程：需要什么、怎么跑校准、两个 CSV 在哪、`golden-ingest` 全部 flag、四文件结构、怎么自测、怎么提交、重算基线的红线、以及「夹具不能证明什么」 |
| `gamer-mode-design.md` | 120 行 | Game Lab 的边界声明（不注入 DLL、不 hook、不看游戏内存）、会话路径、五种意图、阴影可见性曲线、夜间策略、启动检查与回执、失败恢复、研究 backlog |
| `hdr-patch-renderer-design.md` | 80 行 | 「真 HDR 范围 patch 渲染器」设计（状态：已实现并集成，2026-07-04 更新） |
| `troubleshooting.md` | 约 200 行 | 每条都以「可执行动作」结尾的排障条目：SmartScreen、Windows 更新后 profile 失效、游戏重置 gamma、驱动更新后颜色不对、夜间模式无变化、校准起不来/找不到仪表、文件在哪、如何求助 |
| `app-cleanup-audit-2026-07.md` | 约 160 行 | 2026-07 清账审计：桌面 UI、校准生命周期、报告持久化、共享样式、迁移文案、构建警告、维护热点，以及本轮已完成项 |
| `codebase-hardening-audit-2026-07.md` | 约 280 行 | 2026-07 硬化审计；头部标注「历史文档，保持原样」，并说明命名早于 `HDRGammaController → Gloam` 改名 |
| `premiere-calibration-readiness-plan.md` | 约 180 行 | 12 个主题的工作计划：发布与测试门禁、显示身份与拓扑、HDR 状态建模、应用管线稳定性、测量有效性门禁、profile 生命周期、HDR 验证、色彩引擎、硬件兼容矩阵（明确排除在实现范围外）、UX 完整性、诊断与可支持性、分发/法务/安全 |
| `media/README.md` | 57 行 | 6 张 README 图的出处与重制方法；特别声明 hero 图是**渲染**而非拍照（因为截图无法显示「合成器送上线的信号 ≠ 截图记录」），以及隐私注意（夜间模式面板的经纬度已涂掉，重截要再涂） |
| `release-notes/v*.md` | 8 个版本 | 见 §13.6.2 |

#### 13.6.2 `docs/release-notes/`

`v1.8.1`、`v1.8.2`、`v1.8.3`、`v1.9.0`、`v1.9.1`、`v1.9.2`、`v1.9.3`、`v1.10.0`，共 8 个。
最新 `v1.10.0.md`（7 行）的写法是范例：先讲用户可见结果（「SDR 校准现在真的到达显示器」）、
再讲前提条件（「自动管理应用颜色」必须开）、再讲行为变化（SDR/HDR 校准分开保存与换入），
最后点名致谢 issue 报告者。`v1.9.3.md` 同样：三个用户可感知的点 + 一条致谢。

#### 13.6.3 `docs/media/`

6 张 PNG：`hero-comparison.png`（1.6 MB，渲染）、`dashboard.png`、`game-lab.png`、
`night-mode.png`、`tray-menu.png`、`calibration-report.png`，均按 `media/README.md` 的约定采集：
PNG、100% 显示缩放、只截窗口、暗色主题、真实数据、真实双显示器（MSI MAG271QPX28 / Gigabyte
M27Q-P，采集于 2026-07-28 的 v1.8.1 安装版）。README 里的图引用这些路径。

#### 13.6.4 `site/` 结构

静态站，无构建步骤；部署即上传目录。

| 路径 | 体量 | 内容 |
|---|---|---|
| `index.html` | 52 KB | 6 个 section：`#problem`（Windows lifts shadows）、`#how`（Pure math inside the HDR signal path）、`#features`、`#opensource`、`#download`、`#faq` |
| `guides.html` | 28 KB | 4 个 h2：Quick start、How Game Lab stays out of the game、Calibration best practices、Troubleshooting |
| `whitepaper.html` | 292 KB | 技术白皮书（KaTeX 静态排版；`data-tex` 为公式源） |
| `whitepaper.md` | 53 KB | 由 HTML 生成的 Markdown（头部「生成文件，勿手改」+ canonical 链接） |
| `style.css` | 33 KB | 站点样式；页面用 `style.css?v=<md5 前 8 位>` 取缓存 |
| `404.html`、`llms.txt`（2.8 KB）、`robots.txt`、`sitemap.xml`、`site.webmanifest` | — | `llms.txt` 是给 agent 的站点摘要（问题陈述、两套校正层、文档/下载链接、Notes for agents） |
| `_headers` | 33 行 | Cloudflare Pages 响应头：CSP（同源 + Google Fonts + 自托管分析 + Cloudflare Web Analytics 两对域名）、HSTS、`nosniff`、Referrer-Policy、Permissions-Policy；`/assets/*` 缓存 1 天 + SWR 7 天；`/whitepaper.md` → `text/markdown; charset=utf-8`；`/site.webmanifest` → `application/manifest+json` |
| `_redirects` | 17 行 | 三条 301：`/index.html→/`、`/guides.html→/guides`、`/whitepaper.html→/whitepaper`。注释明确：**www→apex 不在这里**（Pages 的 `_redirects` 只匹配路径，写全 URL 会被 wrangler 接受但永不触发），那是 Cloudflare 控制台的 zone 级 Redirect Rule |
| `assets/` | — | `katex/`（`katex.min.css` + 7 个 woff2，按页面实际加载的资源裁剪）、`compare_*.webp`、图标、`og.jpg`、`CREDITS.txt`（La Tour 画作 CC0 出处 + 渲染方法与压缩保真验证）、`screenshots/README.txt`（待拍截图清单） |

`deploy/caddy-site-block.txt`（108 行）是 Cloudflare Pages 之前的 erebus 方案，保留为回退。
两份配置的对应关系：CSP/HSTS/`nosniff`/Referrer-Policy/Permissions-Policy 两边同值；
`try_files {path} {path}.html` 与两条 `redir` 让 `/guides` 工作并把 `.html` 归一；
`handle_errors` 404 重写到 `/404.html`；`@revalidate`（`max-age=0, must-revalidate`）覆盖
HTML/`style.css`/`whitepaper.md`/`llms.txt`/`sitemap.xml`/`robots.txt` 与所有无点号路径。
两处注释是踩过的坑：地址必须写 `http://` 前缀以关掉该站点自动 HTTPS（隧道在边缘终止 TLS，
否则重定向回环）；`{re.htmlext.1}` 的捕获组下标不能省，否则每个 `.html` 都重定向到 `/`。

#### 13.6.5 `deploy/README.md`（273 行）

这是运维事实密度最高的文档之一：

- **Cloudflare Pages**：项目 `gloam`；preview 域名 `gloam-d4b.pages.dev`（裸 `gloam` 子域被占用）；
  `_headers`/`_redirects` 在部署时被消费且**不会被发布**；手工部署
  `npx wrangler pages deploy site --project-name=gloam --branch=main`。
- **`?v=` 缓存 bust**：`site/style.css?v=<md5sum 前 8 位>`；两个主机都对样式表
  `must-revalidate`，但换 URL 才是边缘立刻更新的保证。曾因漏换导致「新 HTML 配旧样式表，
  整站渲染成亮色主题」（`:69-83`）。
- **erebus 回退**：Caddy 在 Docker 容器 `caddy` 里，配置 `/nvme-mirror/apps/caddy/Caddyfile`，
  **该文件同时承载其它站点**，所以必须先 `caddy validate` 再 `caddy reload`；静态站根
  `/nvme-mirror/static/<name>` 只读挂到 `/srv/sites/<name>`。
- **Caddyfile bind-mount 陷阱**（`:170-197`）：单文件挂载绑定 inode，`mv` 会换 inode 而容器继续读
  旧文件；必须在原地写（`cat >` 或 `docker exec -i caddy sh -c 'cat > …'`），`docker restart caddy`
  会把所有站点一起短暂下线。
- **无扩展名 URL**（`:220-240`）：三处必须一致——Pages 的 `_redirects`、erebus 的
  `try_files`/`redir`、以及站内的链接/canonical/`og:url`/`sitemap.xml`/`llms.txt`。
- 还有：客户端 IP 在隧道下都记 `127.0.0.1`（需要 `trusted_proxies` + `CF-Connecting-IP`，
  会波及整份 Caddyfile，故有意不动）；KaTeX 字体子集是「加载页面读
  `performance.getEntriesByType('resource')`」得来的，缺字体只会静默降级。

`deploy/caddy-site-block.txt` 与 `site/_headers` 的注释都要求两边保持同步（Caddy 块是较老的一份）。

---

### 13.7 仓库约定与变更流

#### 13.7.1 文档风格

可观察到的约定：一篇文档一个关注点，标题即结论域；开头一段说明「这份文档拥有什么」；大量使用
**「为什么这样而不是那样」**的注释体（与代码注释同一风格，`CONTRIBUTING.md:78-80` 明写「注释解释
为什么、记录不变量，不描述下一行」）；历史文档加横幅声明
（`codebase-hardening-audit-2026-07.md` 的「Historical document, left as written」）；审计文档在
文件名里带 `-YYYY-MM`；`docs/` 内部用相对链接互引，`README.md` / `CONTRIBUTING.md` 是最外层入口。

#### 13.7.2 `.simplify/`

一个本地重构 harness 的工作目录，四个文件：

| 文件 | 内容 |
|---|---|
| `config.json`（36 行） | `stacks: ["csharp"]`；`test_command` / `collect_command`（`--list-tests`）/ `warn_command`（`--no-incremental`）；`golden_paths`（`Fixtures/Golden/**/*.json`、`**/*.csv`、`tests/**/*.verified.*`）；`source_globs`（`src`、`tests`、已不存在的 `gen-srgb-to-gamma-lut`、`site/**/*.js|ts`、`scripts/**/*.ps1`）；一长串 `exclude_globs`；超时 1800s / 900s |
| `ledger.jsonl` | 每次被接受的重构一行：时间、fingerprint、描述、lever（如 `extract-clone`）、`sloc_delta`、commit、`verify`（如 `isomorphic`）、caveats |
| `rejected.jsonl`（13.9 KB） | 被拒绝的候选 |
| `.gitignore` | `baseline*.json`、`survey*.json`、`jscpd/`、`snapshot*.json`、`trx/`、`expected*.json` |

`ledger.jsonl` 的价值在于它记录了等价性是怎么判定的。例如第一条：把 CCSS 解析前导抽成
`SelectPrimaries` 后，「harness 报 exit-code 保真，因为没有 dotnet 解析器；真实保真是逐 test-id 的
`.simplify/testids.txt` golden（1266 个 id + 结果），其哈希匹配。仓库里没有 `.ccss` golden 输出
文件，所以只有 in-suite 断言覆盖解析。」——这与 §13.1.12 的「Golden 覆盖面窄」相互印证。
`config.json` 的 `source_globs` 仍列着 `gen-srgb-to-gamma-lut/**`，该目录当前不存在：`.simplify`
配置也有轻微过期。

#### 13.7.3 `ai/memory-bank/`

唯一文件 `ai/memory-bank/tasks/gloam-i18n-tasklist.md`（112 行），中文任务清单：规格摘要、
14 个任务（5 个已完成 / 9 个待办，含验收标准与涉及文件）、质量要求、技术备注。
它是**过程档案**，不是架构文档，且与现状有已标注的时差：它记录「已交付
`v1.11.0-i18n.1`」等 tag、以及「全量测试 1498」，而 `src/Gloam/Gloam.csproj` 的当前版本是
`1.10.0`。文档本身也注明「版本节点均为附注标签，可回退」。读它时应把它当作某一时刻的工作计划，
而不是当前状态。

#### 13.7.4 `ROADMAP.md` 的排除

`.gitignore:60-63` 明确排除 `ROADMAP.md`，并给出理由：内部规划文档，含竞争定位、尚未验证的
结论与未发布计划，「不该是访客最先浏览到的东西」。因此架构文档不应引用它，仓库里也没有它。

#### 13.7.5 版本方案

- **单一真源**：`src/Gloam/Gloam.csproj` 的 `<Version>`，当前 `1.10.0`；同时派生出
  `AssemblyVersion` / `FileVersion`（`$(Version).0`）与 `InformationalVersion`（`$(Version)`）。
- **发布版本来自 tag**：CI 用 tag 名剥掉 `v` 作为 `--packVersion` 覆盖 csproj；本地不带参数的
  `package.ps1` 回退到 csproj 的值，因此两者保持一致时「本地打包与 tag 发布是同一版本」
  （`RELEASE.md:52-54`）。
- **tag 格式被校验**：`^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$`（`build.yml:98-100`），允许预发布后缀。
- **Velopack 比较的是安装包版本**，所以 `--packVersion` 必须单调（`UpdateServiceTests` 用同版本/
  降级/异常版本覆盖了这些拒绝路径）。
- **发布说明目录按 tag 命名**：`docs/release-notes/vX.Y.Z.md`，与物理 tag 一一对应；当前 8 个。

#### 13.7.6 一次改动从分支到发布

1. 分支上改代码 → 需要时先在 `src/Gloam.Core` 加 pinning 测试（`CONTRIBUTING.md:86-100`：
   校准数学类改动必须「先加捕获现状的测试，再改行为」，且要说明测了什么）。
2. UI 文案改动必须进 `src/Gloam/Resources/i18n/*.json`，否则 `LocalizationAuditTests` 的基线棘轮会红
   （`CONTRIBUTING.md:43-49`）。
3. 改白皮书则编辑 `site/whitepaper.html`，再依次跑
   `node scripts/build-whitepaper-math.mjs`、`python scripts/build-whitepaper-md.py`
   （`deploy/README.md:8-17`）。
4. 本地 `dotnet test src/Gloam.sln -c Release` + `dotnet build … -p:GloamStrictBuild=true` +
   PR 模板清单。
5. 开 PR → `build.yml` 的 `validate` 跑门禁链（§13.4.2）→ `build` 出未签名安装包作为产物验证。
6. 合并到 main → 若触及 `site/**`，`deploy-site.yml` 独立部署并 curl 验线上。
7. 发版：改 `<Version>`、写 `docs/release-notes/vX.Y.Z.md`、push main、`git tag vX.Y.Z`、
   `git push origin vX.Y.Z` → tag 路径的签名发布 → `gh release edit vX.Y.Z --notes-file …`。
8. 需要时按 `RELEASE.md:86-90` 做硬件（色度计）复验并在发布说明里记录。

---

### 13.8 无法验证 / 需要一次真实运行才能确认的清单

1. **CI 是否真的绿。** 未运行流水线。特别地：`Test-UpdateFeedConsistency.ps1` 的陈旧路径（§13.3.3）
   按静态推理会让 `validate` 在该步失败；`windows-latest` 能否跑 WPF 测试也只能由真实运行回答。
2. **`dotnet test` 的实际用例数。** 1,216 是方法数（`[Fact]`+`[Theory]`），Theory 展开后更多；
   `ai/memory-bank` 的 1498 无法复核。
3. **`vpk pack` 链路的端到端行为。** 未执行；`package.ps1:100-102`、`build.yml:143-170` 的注释与参数
   是唯一依据。`ai/memory-bank` 也把「发布链路验证 `vpk pack`」列为未完成项（Task 13）。
4. **签名产物。** `Test-SignedReleaseArtifacts.ps1` 需要已签名产物与 `Get-AuthenticodeSignature`；
   未运行。`ExpectedPublisher` 默认值是个人名 `David Torcivia`（`Test-SignedReleaseArtifacts.ps1:6`），
   与 `vpk pack --packAuthors` 一致，但实际证书主体取决于 Azure Trusted Signing 的验证身份。
5. **站点线上响应头与重定向。** `deploy-site.yml:49-71` 的 9 条 curl 断言未执行；`site/_headers` 与
   `deploy/caddy-site-block.txt` 的同步只是文本比对。
6. **两张 golden 基线是否仍与当前管线一致。** 未运行测试，只做了结构阅读；基线数值本身来自仓库
   提交的文件。
7. **`.simplify/ledger.jsonl` 提到的 `.simplify/testids.txt`。** 该文件不在仓库里（`.gitignore`
   未列出它，目录中也没有），无法核对「1266 个 test id + 结果」的哈希断言。
8. **`docs/codebase-hardening-audit-2026-07.md` 的具体发现条目。** 只读了头部与结构，未逐条读；
   安全相关结论应以该文档原文为准。

---

## 14 附录：索引、术语表、常用命令与未验证事项

### 14.1 按概念找代码（最快的入口索引）

| 我想改/理解…… | 从这里开始 | 备注 |
|---|---|---|
| Windows SDR-in-HDR 校正曲线本身 | `src/Gloam.Core/TransferFunctions.cs` | sRGB / gamma 2.2 / PQ 的数学定义 |
| 每显示器的 LUT 生成 | `src/Gloam.Core/LutGenerator.cs`、`ColorAdjustments.cs` | 单通道/三通道 LUT、亮度/色温/夜间叠加 |
| 真正写硬件的时刻 | `src/Gloam.Core/DispwinRunner.cs`（`ApplyGamma`）、`NativeGammaRamp.cs` | `SetDeviceGammaRamp` / MHC2 / dispwin 三条路 |
| "谁决定现在该用什么模式" | `src/Gloam.Core/GammaApplyService.cs`（`RequestApply`） | 策略解析：profile + 夜间 + 排除 + 游戏会话 |
| 写入合并与限速 | `src/Gloam.Core/LatestValueCoalescer.cs` | 每键单飞、250ms 下限 |
| 校正被踩掉后的自愈 | `GammaApplyService._rampGuard` → `DispwinRunner.VerifyAndRestoreRamps` | 10 秒回读 |
| 校准期间"摘掉校正" | `src/Gloam.Core/Calibration/CalibrationStateManager.cs` | `BypassKey` / `IsDeviceInBypass` |
| 夜间模式调度与太阳位置 | `NightModeService.cs`、`SunCalculator.cs`、`NightModeSchedulePoint.cs` | 固定时间或太阳触发 |
| 暖色偏移的色度学实现 | `ColorAdjustments.cs`、`Calibration/Cam16Ucs.cs` | CAT16、mired、亮度保持、Ultra Night |
| 褪黑素剂量与节律治理 | `MelanopicMonitorService.cs`、`MelanopicDoseStore.cs`、`CircadianDoseGovernor.cs` | EDI 上限反推亮度/白点 |
| Game Lab 的"画面意图" | `src/Gloam.Core/GamerMode.cs`、`GamerModeCoordinator.cs` | 预设表、暗部可见度曲线、Gameplay Lock |
| 游戏安装库发现 / 前台检测 | `GameDiscoveryService.cs`、`AppDetectionService.cs` | 进程钩子与窗口边界 |
| 校准编排（一轮完整校准） | `src/Gloam.Core/Calibration/CalibrationOrchestrator.cs` | 最大状态机（79KB） |
| 测量协议（色度计） | `Calibration/SpotreadSession.cs`、`ColorimeterService.cs` | ArgyllCMS `spotread` 的进程内协议 |
| 色彩数学（XYZ/Lab/CAM16/PQ） | `Calibration/ColorMath.cs`、`ColorTypes.cs`、`Cam16Ucs.cs` | 无分配 3×3 变换 |
| 闭环修正与漂移补偿 | `Calibration/ClosedLoopCorrector.cs`、`DriftCompensator.cs`、`DriftPredictor.cs` | 迭代逼近目标 |
| 3D LUT 与 HDR LUT | `Calibration/Lut3DGenerator.cs`、`Lut3D.cs`、`HdrMhc2LutBuilder.cs`、`HdrColorMatrixRefiner.cs` | 33³ 网格、FP16/сRGB 路径 |
| 生成并安装 Windows profile | `Calibration/Mhc2ProfileBuilder.cs`、`CalibrationProfileInstaller.cs`、`AdvancedColorProfileAssociation.cs` | MHC2 文件 + WCS 关联 |
| 校准的"证明" | `Calibration/CalibrationVerifier.cs`、`UncertaintyBudget.cs`、`CalibrationReport.cs` | 复测、误差预算、报告 |
| ArgyllCMS 下载/校验 | `Calibration/ArgyllDownloader.cs`、`ArgyllPathFinder.cs`、`BoundedProcess.cs` | 摘要、限长、原子替换 |
| 报告窗口（157KB 那个） | `src/Gloam/Features/Calibration/CalibrationReportWindow.xaml.cs` | 校准后所有独占探针操作的宿主 |
| 托盘菜单与热键 | `src/Gloam/Features/Tray/TrayViewModel.cs`、`HotkeyManager.cs` | 10 个命令、6 组热键 |
| Dashboard | `src/Gloam/Features/Dashboard/DashboardViewModel.cs` + `DashboardWindow.xaml` | 19 个命令、31 个 x:Name |
| Game Lab 窗口 | `src/Gloam/Features/GameLab/GameLabWindow.xaml`、`GamerModeItem.cs` | 五区段、字段 180ms 合并 |
| 夜间模式时间线编辑器 | `src/Gloam/Features/NightMode/NightModeScheduleControl.xaml.cs` | 双 Canvas、4:00 起点、对数温度轴 |
| 设置持久化 | `SettingsManager.cs` + `SettingsPersistence.cs` + `SettingsNormalization.cs` | 原子写、NewerSchema、归一化 |
| 启动项 / 更新 | `src/Gloam/Infrastructure/Platform/StartupManager.cs`、`UpdateService.cs` | HKCU Run、Velopack |
| UI 文本与翻译 | `src/Gloam/Resources/i18n/*.json` + `src/Gloam.Localization/` | 硬编码会被测试判失败 |
| 无头诊断 | `src/Gloam.Cli/Program.cs` | `lut`/`sweep`/`compare`/`check-lut`/`night`/`golden-ingest` |
| 金标回归 | `tests/Gloam.Tests/Fixtures/Golden/` + `GoldenReplayTests.cs` | 真实面板录制数据 |
| 打包与发布 | `package.ps1`、`.github/workflows/build.yml`、`RELEASE.md` | Velopack + 签名 |

### 14.2 术语表

| 术语 | 含义 | 在本仓库中的落点 |
|---|---|---|
| **SDR-in-HDR** | Windows 把 SDR 内容放进 HDR 信号时做的曲线转换，是"发灰"的根因 | `TransferFunctions`、`LutGenerator` |
| **MHC2** | Microsoft HDR Calibration 2 —— Windows 存放每显示器校准曲线的 profile 容器 | `Mhc2ProfileBuilder`、`*.icm` 模板 |
| **VCGT** | 显卡 gamma 查找表（Video Card Gamma Table），`SetDeviceGammaRamp` 写的东西 | `NativeGammaRamp`、`DispwinRunner` |
| **WCS** | Windows Color System，profile 关联与激活的 API | `Gloam.Interop/Wcs.cs`、`AdvancedColorProfileAssociation` |
| **PQ (ST.2084)** | HDR 电光转换函数；`scRGB` 是其线性浮点表示 | `TransferFunctions`、`HdrMhc2LutBuilder` |
| **CAT16 / CAM16** | 色适应变换（CAT16）与色貌模型（CAM16 / CAM16-UCS） | `ColorAdjustments`、`Cam16Ucs` |
| **CCT / mired** | 相关色温；mired = 10⁶/K，用于让暖色过渡在感知上均匀 | `ColorAdjustments`、`JndPacedFade` |
| **EDI / melanopic** | 褪黑素抑制等效照度，夜间模式的生理指标 | `MelanopicMonitorService`、`MelanopicDoseStore`、`CircadianDoseGovernor` |
| **bypass** | 校准时临时摘掉 Gloam 自己的校正，保证测量的是面板本身 | `CalibrationStateManager` |
| **ramp guard** | 10 秒定时回读并恢复被游戏/驱动踩掉的 gamma ramp | `GammaApplyService._rampGuard` |
| **coalescer** | 每显示器"最新值优先"的写入合并器，带速率下限 | `LatestValueCoalescer` |
| **Gameplay Lock** | 游戏会话期间冻结夜间模式派生的色温 | `GamerModeCoordinator`、`ActiveGamerSession.LockedKelvin` |
| **Golden fixture** | 真实面板录制测量数据，回放后逐数字比对，把色彩数学钉死 | `Fixtures/Golden/*`、`GoldenSampleBaseline` |
| **Velopack** | 安装/更新框架；安装钩子必须早于 WPF 启动 | `Program.Main`、`UpdateService`、`package.ps1` |
| **ccss / ccmx** | ArgyllCMS 的光谱校正文件格式（面板采样 / 色度计校正） | `CcssWriter`、`CcmxWriter`、`CcssDatabaseClient` |
| **CAM16-UCS** | 用于色差 ΔE 的感知均匀空间 | `Cam16Ucs`、`CalibrationReport` |
| **LUT 3D** | 三通道三维查找表，用于 HDR/广色域的非对角校正 | `Lut3D`、`Lut3DGenerator`、`HdrMhc2LutBuilder` |

### 14.3 常用命令

```powershell
# 开发运行（需要 .NET 10 SDK）
dotnet run --project src/Gloam

# 严格构建（CI 同款：警告即错误）
dotnet build src/Gloam.sln -c Release -p:GloamStrictBuild=true

# 测试（部分用例驱动真实 WPF/STA，需要桌面会话）
dotnet test src/Gloam.sln -c Release

# 启动冒烟（不建窗口，只验证组合根 + 日志 + 本地化）
dotnet run --project src/Gloam -- --smoke --data-dir C:\temp\gloam-smoke

# 无头色彩管线检查
dotnet run --project src/Gloam.Cli -- check-lut 2.2 200 --algorithm perceptual --temp-k 2700 --json -
dotnet run --project src/Gloam.Cli -- night 2200 --algorithm ultra --basis rec2020 --json -

# 本地打包（Velopack）
.\package.ps1 -Version X.Y.Z
```

### 14.4 本次通读的边界与未验证事项汇总

以下结论在文档各章已标注，这里汇总，便于后续有针对性地补验证：

1. **未运行任何程序**：CI 是否绿、`dotnet test` 的真实用例数（[Fact]+[Theory]=1,216 个方法是静态计数）、WPF 测试在 `windows-latest` 上是否可跑，均未实测。
2. **未执行打包链路**：`package.ps1` / `vpk pack` / 签名 / 更新 feed 一致性脚本都只做了静态阅读；`Test-UpdateFeedConsistency.ps1` 读的路径疑似陈旧（见 §13.3）。
3. **硬件相关全部未验证**：真实 MHC2 安装、Windows 高级颜色 profile 列表选择、GPU ramp 在驱动重置后的行为、FP16 scRGB 色块呈现、色度计 USB 恢复、Aero Peek 排除、真实打印机分页。审计报告把"一次 SDR + 一次 HDR 真机校准"列为发布前必须的人工门禁。
4. **金标夹具的数值未回放**：只做了结构与机制阅读。
5. **地理定位路径**：夜间模式"Detect"按钮直接请求 `http://ip-api.com/json/?fields=lat,lon`（明文 HTTP，`NightModeScheduleControl.xaml.cs:111`）。可达性与响应格式未验证；它与 README "No telemetry" 的措辞存在张力（属用户显式点击触发，但仍是外部网络请求且非 HTTPS），建议后续改为 HTTPS 或本地计算复选框。
6. **`.simplify/testids.txt`** 在仓库中不存在（配置与 ledger 提到它），无法核对「266 个 test id」的哈希断言。
7. **`ai/memory-bank/` 的任务清单**是历史规划，不是当前状态，不要当作 TODO 列表使用。
8. **本地化目录键数**：`en-US.json` / `zh-Hans.json` 实测各约 790 个字符串叶子键（递归计数口径），`docs/localization.md` 记录的 696 已漂移。
9. **本地化 R4/R7 发现**（见 §12）：`LanguageService.Apply` 只设 `CurrentUICulture` 不设 `CurrentCulture`；`StringCatalogSet.LoadErrors` / `DirectoryCatalogSource.Errors` 在 `src/` 内没有消费者，坏覆盖文件被静默跳过。

### 14.5 文档维护建议

- 本报告是**某一时刻的快照**。仓库以 `Version` 1.10.0 为基线，若结构变动（新增 feature、拆分巨型窗口、迁移 `HdrPatchRenderer`），优先更新 §3.2 依赖图、§5.5 DI 表与对应子系统的"文件→职责表"。
- 各章末尾的"风险"表是**可执行清单**，不是抱怨：每一条都对应一个具体文件与一个具体改法。
- 若要把本报告纳入仓库常规文档，建议与 `docs/project-structure.md` 去重：后者只保留"分层规则 + 加 feature 的步骤"，本报告的 §7–§13 承担逐文件展开。
- 报告中的"未验证"标注不要删。它们是下一轮真机验证的输入，也是防止把推断当结论的唯一防线。
