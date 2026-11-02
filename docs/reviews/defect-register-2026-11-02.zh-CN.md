# Gloam 缺陷登记册（Defect Register）

| 项    | 值                                                                                                       |
| ---- | ------------------------------------------------------------------------------------------------------- |
| 仓库   | `D:\WorkSpace\gloam`                                                                                    |
| 分支   | `codex/zh-terminology-review`                                                                           |
| HEAD | `488120a`                                                                                               |
| 登记日期 | 2026-11-02                                                                                              |
| 审查方式 | **纯静态取证**（文件读取、`git status` / `git tag` / `git ls-files` / `grep`）                                      |
| 审查维度 | 架构与依赖 / 授权与安全 / 验收边界与质量 / CI-CD / 开源仓库标准                                                                |
| 执行状态 | **未执行任何构建与测试**：`dotnet build`、`dotnet test`、`package.ps1`、`scripts/Test-*.ps1`、实机 HDR 与标定、签名链验证均**零执行** |

> **使用前必读**：本文所有「验证方式」均为**计划**，不是结果。任何字段都不得被引用为「测试通过」。  
> 定级含义：P0 = 阻断发版，必须修；P1 = 发版前必修；P2 = 可带已知问题发版，须写入 release notes；P3 = 记录即可。



---

## 一、P0 — 阻断发版

### P0-1 提权启动的驱动安装器未做启动时完整性校验

| 字段       | 内容                                                                                                                                                                          |
| -------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 维度       | 安全（权限提升 / 任意代码执行）                                                                                                                                                           |
| 位置       | `src/Gloam.Core/Calibration/UsbDriverHelper.cs:90-96`（`LaunchDriverInstaller`）、`:127-133`（异步版）、`:17-48`（候选路径解析）                                                             |
| 触发条件     | 攻击者对 `ArgyllPathFinder.FindArgyllBinPath()` 解析目录、或 `%LOCALAPPDATA%\Argyll\usb\`、或 `AppPaths.DataDir` 任一处有写权限 → 投放名为 `ArgyllCMS_install_USB.exe` 的文件 → 用户点击驱动安装提示并在 UAC 点「是」 |
| 实际影响     | `UseShellExecute=true` + `Verb="runas"` 直接启动，构成**管理员权限任意代码执行（完整本地提权）**；提示语为常规的「安装色彩仪驱动」，社会工程可信度高                                                                            |
| 根因       | SHA-256 钉扎仅覆盖下载产物（`ArgyllDownloader.cs:47`、`:243-247`），**提权启动路径不重新校验**；且暂存→替换保留了长期可写目录，校验与使用之间无绑定                                                                           |
| 最小修复方向   | （fail closed）`Process.Start` 前对 `installerPath` 做 SHA-256 校验，不符即 `return false` 并返回明确错误；同步收紧候选来源，对**每一路**候选都先校验，或仅信任下载托管目录                                                  |
| 验证方式（计划） | ① 部分：向解析出的 usb 路径写入哈希不符的 exe，断言返回 `false` 且进程未启动；② 正确哈希夹具 → 返回 `true`；③ 手工：篡改文件后点击「安装驱动」，确认**不出现 UAC 弹窗**且显示校验失败提示                                                          |
| 定级说明     | 安全成员原定 P1，本登记册上调为 P0。理由：本轮唯一具备「权限提升 + 任意代码执行」的已验证缺陷，修复成本极低（复用现有哈希工具）。若负责人书面接受风险，可降级——不建议                                                                                    |

### P0-2 发布快照不完整：工作树脏 + 中文 README 死链

| 字段        | 内容                                                                                                                                                                                                            |
| --------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 维度        | 可维护性（发版治理 / 产物完整性）                                                                                                                                                                                            |
| 位置        | `README.md:3` 指向 `[简体中文](README.zh-CN.md)`；`git status` 显示该文件为 `??` 未跟踪                                                                                                                                       |
| 触发条件      | 在当前 HEAD（`488120a`）打 tag 并推送                                                                                                                                                                                  |
| 实际影响      | 未跟踪文件不进 tag 快照 → 首页语言切换链接 **404**；`README.md` 本身为 `M` 已修改未提交 → 发布内容 ≠ 本地所见                                                                                                                                    |
| 同类未跟踪/未提交 | `README.zh-CN.md`（??）、`docs/release-notes/v1.11.0-v1.13.0.zh-CN.md`（??，RELEASE.md 预发布清单要求其存在）、`docs/reviews/`（??）、`src/Gloam/Resources/i18n/zh-Hans.json`（M）、`tests/Gloam.Tests/LocalizationAuditTests.cs`（M） |
| 最小修复方向    | 提交上述文件；确属废弃则还原并删除。放行前 `git status --porcelain` 必须为空                                                                                                                                                           |
| 验证方式（计划）  | ① `git status --porcelain` 输出为空；② `git archive HEAD \| tar -t \| grep README.zh-CN.md` 命中；③ 网页点语言切换返回 200                                                                                                     |

---

## 二、P1 — 发版前必修

### P1-1 版本号漂移：`<Version>` 1.10.0 vs 已到 v1.13.0

| 字段       | 内容                                                                                                                                             |
| -------- | ---------------------------------------------------------------------------------------------------------------------------------------------- |
| 位置       | `src/Gloam/Gloam.csproj:23`；回落逻辑 `package.ps1:105-109`；要求 `RELEASE.md:54-56`                                                                   |
| 触发条件     | 执行无参 `.\package.ps1`（本地出包），或发版漏改                                                                                                               |
| 实际影响     | ① 产物显示 1.10.0，与 release notes 承诺的「可在托盘/Dashboard 看版本」冲突；② 用户报障无法定位版本；③ Velopack 按 packVersion 比较，存在误判空间。CI 用 `/p:Version=<tag>` 覆盖，**掩盖**了本地缺陷 |
| 最小修复方向   | 发版前提版本号；在 `scripts/Test-UpdateFeedConsistency.ps1` 增断言 `csproj Version == 最新 release-notes 版本`                                                 |
| 验证方式（计划） | `./package.ps1 -Version <目标版本> -PublishOnly` 后，产物文件属性「产品版本」= 目标版本                                                                              |
| 备注       | 架构成员定 P2、验收成员定 P0，本册裁决为 P1：CI 覆盖使对外产物版本正确，不构成对外错误；若依赖本地包对外分发，回升 P0                                                                             |

### P1-2 「警告即错误」在 CI 中不存在

| 字段       | 内容                                                                                                                             |
| -------- | ------------------------------------------------------------------------------------------------------------------------------ |
| 位置       | `.github/workflows/build.yml`（validate job）、`Directory.Build.props:28-31`（开关定义）、`:26`（`KnownAnalyzerBacklog` 永久 NoWarn 15 条规则） |
| 触发条件     | 任意提交引入 CA/CS 级警告                                                                                                               |
| 实际影响     | `TreatWarningsAsErrors` 仅在 `GloamStrictBuild=true` 时生效，而 workflow 从未设置该属性 → 文档宣称的「警告即红灯」在 CI 中**不存在**，规则回潮只能靠人工                |
| 最小修复方向   | validate job 增加 `env: { GloamStrictBuild: 'true' }`；backlog 收敛为按文件粒度 `NoWarn`，或在 workflow 保留 `GloamAnalyzerAudit` 审计任务         |
| 验证方式（计划） | 临时分支故意引入未使用变量 → 期望 CI 红灯；或本地 `dotnet build -p:GloamStrictBuild=true` 记录基线差异                                                    |

### P1-3 主干推送不触发任何 CI

| 字段       | 内容                                                                          |
| -------- | --------------------------------------------------------------------------- |
| 位置       | `.github/workflows/build.yml:9-15`                                          |
| 触发条件     | 直接 push / merge 到 `main`（非 PR 路径）                                           |
| 实际影响     | main 可能长期未验证；坏提交只在下一个 `v*` tag 时暴露——即**在发版时刻才失败**，此时 tag 已在远端               |
| 最小修复方向   | `on.push` 增加 `branches: [main]`（validate job 已存在，build job 依赖 validate，无需改） |
| 验证方式（计划） | Actions 触发列表出现 push(main)；对 main 推一个 docs-only 提交确认 validate 起跑             |

### P1-4 自动更新客户端验签无从取证（条件性阻断）

| 字段       | 内容                                                                                                                                                                                |
| -------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 位置       | `src/Gloam/Infrastructure/Platform/UpdateService.cs:35`（`RepoUrl`）、`:44-45`（`new GithubSource(RepoUrl, accessToken: null, prerelease: false)`）、`:189-196`（`DownloadUpdatesAsync`） |
| 触发条件     | GitHub 账号/仓库被入侵，或发布流程误操作，向 feed 推送恶意包                                                                                                                                             |
| 实际影响     | 若 Velopack 1.2.0 默认仅做同 feed 内自洽的校验和，则更新通道即**全部用户的远端代码执行通道**，且用户无从察觉。CI 侧已签名（`build.yml:159` 调 `Test-SignedReleaseArtifacts.ps1`、`:176`），但**服务端已签 ≠ 客户端已验**                        |
| 状态       | **需取证**。未反编译/未读 Velopack 包源码，无法断言其是否默认校验 Ed25519 与 `releases.win.json` 校验和                                                                                                        |
| 最小修复方向   | 显式传入钉扎公钥（`--signParams` 非对称签名），apply 前拒绝签名缺失/不符的包                                                                                                                                 |
| 验证方式（计划） | ① 本地密钥对签包 + 不匹配公钥启动 → 断言更新被拒；② 篡改包内文件但保留原 manifest → 断言被拒                                                                                                                         |

### P1-5 无自动更新回滚 playbook

| 字段       | 内容                                                                                                                                                     |
| -------- | ------------------------------------------------------------------------------------------------------------------------------------------------------ |
| 位置       | `RELEASE.md` 全文；`grep -i 'rollback\|回滚'` 零命中                                                                                                           |
| 触发条件     | 版本已 `vpk upload --publish` 后发现严重回归                                                                                                                     |
| 实际影响     | Velopack 单向升级：撤回 release 只影响新装用户，已装用户只能靠**更高版本号 hotfix** 脱身。当前无 playbook、无版本预留策略、无演练记录                                                                 |
| 最小修复方向   | RELEASE.md 增「Bad release playbook」：① 立即发 `X.Y.Z+1`，禁止降号；② 保留上一版 Setup 与 `scripts/Test-VelopackInstalledUpdate.ps1` 应急指引；③ release notes 提示可改回 Portable |
| 验证方式（计划） | 在测试机演练「旧版→新版→强制回退」（脚本存在但未执行，会改动本机安装，需授权）                                                                                                               |

### P1-6 漏洞扫描很可能不阻断

| 字段       | 内容                                                                                      |
| -------- | --------------------------------------------------------------------------------------- |
| 位置       | `.github/workflows/build.yml` 中 `dotnet list package --vulnerable --include-transitive` |
| 触发条件     | 依赖中出现已知漏洞                                                                               |
| 实际影响     | 未带 `--fail-on-vulnerable`，发现漏洞仍可能以 0 退出 → 门禁沦为日志                                        |
| 最小修复方向   | 加该开关，或解析输出判空后显式失败                                                                       |
| 验证方式（计划） | 临时引入已知脆弱包，确认步骤非 0 退出（依赖 SDK 行为，需先验证前提）                                                  |

### P1-7 本地化枚举静默降级为空串

| 字段       | 内容                                                                                                                          |
| -------- | --------------------------------------------------------------------------------------------------------------------------- |
| 位置       | `src/Gloam/Features/NightMode/INightModeText.cs:22,31,40`                                                                   |
| 触发条件     | 新增或持久化的 `NightModeAlgorithm` 值未被 switch 覆盖；或设置文件出现越界枚举                                                                      |
| 实际影响     | 设置界面标签、描述、摘要**三处空白**，无异常、无日志，用户看到空条目，且不可诊断                                                                                  |
| 最小修复方向   | `_ => string.Empty` 改为兜底 key（如 `night.profile.unknown`）或抛 `ArgumentOutOfRangeException`；三处 switch 合并为一个查表方法，把降级点收敛为 1 个     |
| 验证方式（计划） | `NightModeLocalizationTests` 增 `[Fact]`：以 `(NightModeAlgorithm)999` 取三者，断言均非空（或断言抛出指定异常）；并断言 `en-US`/`zh-Hans` 两份目录均含兜底 key |
| 备注       | 严重度取决于 NightMode 配置反序列化是否严格校验枚举——该路径未读；若严格校验则降 P2                                                                           |

### P1-8 目标版本 tag 与文档声称不一致

| 字段       | 内容                                                                            |
| -------- | ----------------------------------------------------------------------------- |
| 位置       | `docs/release-notes/v1.11.0-v1.13.0.zh-CN.md:414` vs `git tag` 实际最高 `v1.12.1` |
| 触发条件     | 按文档选择目标版本发版                                                                   |
| 实际影响     | 发版人无法确定目标版本；若该 tag 仅在远端未 fetch，则本地取证不完整                                       |
| 最小修复方向   | `git fetch --tags` 后确认，或改写文档措辞                                                |
| 验证方式（计划） | `git ls-remote --tags origin \| grep v1.13.0`                                 |
| 状态       | 缺少材料（不独立作为阻断，与 P1-1 合并构成版本治理风险）                                               |

---

## 三、P2 — 可带已知问题发版，须写入 release notes

### 3.1 结构类

| ID   | 缺陷                            | 位置                                                                                                                                                                                                                                                                                                                                                                                                        | 触发 / 影响                                      | 最小修复方向                                                                 |
| ---- | ----------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------- | ---------------------------------------------------------------------- |
| P2-1 | 13 个源文件 >1000 行               | 最大 `CalibrationReportWindow.xaml.cs` **2976**（XAML 643）、`CalibrationWindow.xaml.cs` 2124（XAML 539）、`CalibrationOrchestrator.cs` 1681、`Lut3DGenerator.cs` 1487、`SettingsManager.cs` 1413、`SpotreadSession.cs` 1325、`DashboardViewModel.cs` 1320、`ColorMath.cs` 1256、`CalibrationSetupViewModel.cs` 1182、`TrayViewModel.cs` 1025、`GamerModeItem.cs` 1004、测试 `Lut3DTests.cs` 1387 / `ColorMathTests.cs` 1074 | 改动碰撞率高；code-behind 逻辑不可单测                    | 把可测逻辑抽为 presenter/service，code-behind 只留控件装配；目标降 ≥50% 且行为不变（**不是挪代码**） |
| P2-2 | 本地化 **5 套访问面**                | `Loc.` 545 处 / `Tr` 扩展 377 处 / `LocalizedStrings` 33 / `ILocalizer` 36 / `INightModeText` 10                                                                                                                                                                                                                                                                                                              | 新人无法判断该用哪个；每加一处文案需选型，规则无法用 lint 固化           | 收敛到 2 个入口（`ILocalizer` + 1 个 XAML 扩展），其余标 `[Obsolete]` 后删除             |
| P2-3 | 目录 key 是英文句子 slug             | `INightModeText.cs:27-30`（`nightmode.warm.white.point.eased.toward.neutral.partial.chromatic`）                                                                                                                                                                                                                                                                                                            | 任何英文文案改写需同步改代码常量 + 2 份目录 + 审计基线，否则查不到 → 空白   | 改稳定语义 key 并按功能域分组，一次性迁移 + 审计比对；与 P2-2 合并解决                             |
| P2-4 | 校准功能 code-behind 与其余 MVVM 不一致 | `CalibrationReportWindow` / `CalibrationWindow` vs `DashboardViewModel`                                                                                                                                                                                                                                                                                                                                   | 逻辑须起窗口才能测；实现风格分裂                             | 抽出 presenter；与 P2-1 合并                                                 |
| P2-5 | `SettingsManager` 上帝对象        | `SettingsManager.cs` 1413 行                                                                                                                                                                                                                                                                                                                                                                               | 兼读写 / 迁移 / 校验 / 事件，改一处影响全局                   | 拆为 schema + repository + migration                                     |
| P2-6 | Core 分区不一致                    | 根目录平铺 **39** 文件 vs `Calibration/` 58 文件                                                                                                                                                                                                                                                                                                                                                                   | 新功能无处安放，倾向继续加根目录                             | 按能力（Color / Display / Persistence / Calibration）切 3–4 个命名空间            |
| P2-7 | 每个列表项各持一个语言订阅                 | `GamerModeItem.cs:18,56`；`DashboardViewModel.cs:214,235`；`NightModeScheduleViewModel.cs:23,35`                                                                                                                                                                                                                                                                                                            | N 个项 → 一次切换 N 次回调；项被重建未 Dispose 时 handler 累积 | 订阅上移 Feature VM 一处，刷新整集合。**设计风险**：仅确认构造点，未追踪释放点                        |
| P2-8 | UI 快照测试的 CI 稳定性               | `tests/Gloam.Tests/Gloam.Tests.csproj:4,30`（引用 WPF 主工程）；`UiSnapshotTests.cs`、`LayoutOverflowTests.cs`、`LocalizationDumpTests.cs`                                                                                                                                                                                                                                                                          | CI 字体/DPI 与开发机不同 → 假失败使「全绿」不可信；若被跳过则 UI 无覆盖  | 加 `[Trait("Category","UI")]`，CI 固定 DPI/字体后单跑                           |

### 3.2 安全 / 供应链类

| ID    | 缺陷                       | 位置                                               | 说明                                  | 最小修复方向                                                                            |
| ----- | ------------------------ | ------------------------------------------------ | ----------------------------------- | --------------------------------------------------------------------------------- |
| P2-9  | 候选安装器路径优先级把可写路径排在受保护路径之前 | `UsbDriverHelper.cs:20-24` → `:32-35` → `:40-50` | 产品逻辑合理（优先用用户自装 Argyll），但与提权结合即攻击面   | 并入 P0-1；若保留回退，每路须先过哈希校验                                                           |
| P2-10 | 无 NuGet 锁文件              | `Directory.Packages.props` 仅锁直接依赖                | 传递依赖随上游浮动，同一 commit 两次 restore 可能不同 | `RestorePackagesWithLockFile=true` + 提交 `packages.lock.json`；CI 用 `--locked-mode` |
| P2-11 | 无依赖更新自动化                 | 无 `.github/dependabot.yml`、无 renovate            | 与 SECURITY.md 的披露承诺不对称：安全更新无 PR 提醒  | 配置 dependabot（nuget + github-actions）                                             |
| P2-12 | 治理文件缺失                   | 根目录无 `CODE_OF_CONDUCT`、无 `CHANGELOG.md`          | 首次开源发布的标准缺口                         | 补两份文件，并在 README 链接                                                                |

---

## 四、P3 — 记录即可

| ID   | 内容                                                                                        | 处置                                      |
| ---- | ----------------------------------------------------------------------------------------- | --------------------------------------- |
| P3-1 | `.simplify/`、`docs/reviews/`、`ai/`、`src/Gloam.DebugMonitors` 是否随开源发布/进产物，`.gitignore` 未覆盖 | 明确范围：排除或声明                              |
| P3-2 | `docs/release-notes/` 缺 v1.11 / v1.12 / v1.12.1 / v1.13 分版本文件（由一份合并文档替代）                  | 可接受，文档自称可切分                             |
| P3-3 | 无独立 `examples/` 目录                                                                        | `Gloam.Cli` 可充当                         |
| P3-4 | `Gloam.Localization` 与 `Gloam.Localization.Wpf` 双工程可合并                                    | 少一个工程概念，非阻断                             |
| P3-5 | 可执行路径来自可变配置（`ColorimeterService.cs:184,804-825`、`DispwinRunner.cs:221`）                   | 单看不跨权限，与 P0-1 组合可放大；建议加「限定在白名单根之下」的后置校验 |
| P3-6 | 第三方响应按部分可信处理（`CcssDatabaseClient.cs:28-72`、`OemCorrectionImporter.cs:224-228`）            | schema 校验**未验证**                        |
| P3-7 | `Dispatcher.CurrentDispatcher` 在构造时捕获调用线程（`LanguageRefreshSubscription.cs:10`）            | 仅验证到代码形态，**未验证**运行时线程归属                 |
| P3-8 | `CalibrationProfileInstaller.cs`（875 行）把命名生成 / WCS 关联 / 安装 / 回滚 / 清理耦合一处                  | 命名是安全边界，建议与特权操作分离                       |

---

## 五、已检查且判定无问题（不重复排查）

| 检查项          | 结论与证据                                                                                                                                                     |
| ------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 项目图          | 8 工程引用**无环**：`Gloam→Core/Interop/Localization/Localization.Wpf`、`Core→Interop`、`Localization.Wpf→Localization`、`Cli→Core`、`Tests→Core/Localization/Gloam` |
| domain 泄漏 UI | `Core/Interop/Localization` 内 `using System.Windows` 计数 **0**                                                                                             |
| 下载链完整性       | HTTPS + SHA-256 钉扎（`ArgyllDownloader.cs:21-27,47`）；上限 128MB / 512MB / 1 万条目（`:31-33`）；条目大小与非负校验（`:277-284`）                                               |
| Zip-slip     | 已防护：`Path.GetFullPath` + 根前缀比较（`:261-275`），越界即抛并回滚                                                                                                        |
| 安装原子性        | staging → backup → move → 失败回滚（`:290-322`），取消/损坏不留残缺安装                                                                                                    |
| 参数注入         | 无。全部 `ArgumentList`（`OemCorrectionImporter.cs:224-228`、`DispwinRunner.cs:178,539`、`BoundedProcess.cs:38-50`）                                              |
| 硬编码密钥        | 未发现。`token` 命中全为 `CancellationToken` 假阳性；CI 仅 `secrets.GITHUB_TOKEN` + OIDC（`build.yml:119,137,166`）                                                      |
| ICC 命名穿越     | 已防护：`SanitizeProfileNameComponent` 替换 `GetInvalidFileNameChars()` 并截断 40 字符，`..\` 无法存活                                                                    |
| 配置/档案删除守卫    | `SettingsManager.TryGetCalibrationProfilePath` 强制 GUID `N`/`D` 格式，注释明确阻断 rooted path 与穿越                                                                  |
| 报告历史删除       | `FilePath` 来自 `Directory.GetFiles(GetReportsDirectory())`，非用户输入                                                                                           |
| PATH 兜底      | 无 `PATH` 环境变量查找，工具路径仅在显式目录下解析                                                                                                                             |
| 入库大文件        | `argyll_cache_3.5.0.zip`(35MB)、`artifacts/`、`bin/`、`obj/` 均被 `.gitignore` 覆盖；`git ls-files` 无 zip/exe/dll                                                 |
| 提权点数量        | 仅 2 处，均在 `UsbDriverHelper`                                                                                                                                |



---

## 六、开源仓库标准合规 checklist

| 检查项 | 状态 | 证据 |
|---|---|---|
| LICENSE | 符合 | `LICENSE.txt`（MIT，与 README badge 一致） |
| README（安装/快速开始/用法） | 部分符合 | `README.md` 齐全；中文版未跟踪（P0-2） |
| CHANGELOG / release notes | 部分符合 | `docs/release-notes/` 9 份；根目录无 `CHANGELOG.md`（P2-12） |
| 版本号与语义化版本 | 部分符合 | CI 强制 `vX.Y.Z`；`<Version>` 滞后（P1-1） |
| CONTRIBUTING | 符合 | `CONTRIBUTING.md` |
| CODE_OF_CONDUCT | **缺失** | 根目录无该文件（P2-12） |
| SECURITY.md 披露流程 | 符合 | `SECURITY.md`（2.9KB，含私有报告渠道） |
| Issue / PR 模板 | 符合 | `.github/ISSUE_TEMPLATE/` 3 个 yml + `PULL_REQUEST_TEMPLATE.md` |
| 依赖清单与锁文件 | 部分符合 | `Directory.Packages.props` 符合；无 `packages.lock.json`（P2-10） |
| .gitignore | 符合 | 覆盖 `bin/` `obj/` `/artifacts/` `argyll_cache_*` |
| 构建与打包产物说明 | 符合 | `RELEASE.md`、`package.ps1`、Velopack + Azure Trusted Signing |
| 目录结构约定 | 部分符合 | 有 `docs/project-structure.md`，但 Core 根 39 文件分区不一致（P2-6） |
| 示例代码 | 部分符合 | 有 `Gloam.Cli` 与 `check-lut` 示例；无 `examples/` |
| 测试说明 | 部分符合 | 有 `docs/golden-fixture-contribution.md`；README 未写「如何跑测试」 |
| 文档站 / docs | 符合 | `docs/` 19 项 + `site/` + `deploy-site.yml` |
| 依赖更新自动化 | **缺失** | 无 dependabot / renovate（P2-11） |
| 分支保护 / CODEOWNERS | **无法取证** | 仓库内无文件；平台侧设置本地不可见 |

---

## 七、发版门禁

**打 tag 前必须完成**
1. 修 P0-1（提权前哈希校验 + 收紧候选路径），回归用例入库
2. 清空工作树（P0-2），确认 `git archive` 含 `README.zh-CN.md` 与 release notes
3. 版本号对齐目标 tag 且大于线上最高版（P1-1），`git fetch --tags` 确认（P1-8）
4. CI：`GloamStrictBuild: 'true'`（P1-2）+ `push: [main]`（P1-3）+ `--fail-on-vulnerable`（P1-6）
5. **取证 P1-4**：确认客户端是否验签；否定则钉扎公钥——未定论前视为阻断
6. 补 Bad release playbook（P1-5）
7. 人工跑一次 `dotnet build -p:GloamStrictBuild=true` 与全量 `dotnet test`，留日志（CI 目前不强制这两步，不能靠 CI 背书）

**可带已知问题发版**：P2-1~P2-12、P3 全部
**不可带病发版**：P0-1、P0-2、P1-4（若取证为否）

---

## 八、分歧裁决记录

| 分歧点 | 各角色意见 | 本册裁决 | 可推翻裁决的新证据 |
|---|---|---|---|
| 版本号漂移级别 | 架构 P2 / 验收 P0 | P1 | 若依赖本地 `package.ps1` 出包对外分发 → 回升 P0 |
| 本地化枚举静默空串 | 架构 P1 | P1 | NightMode 配置严格校验枚举 → 降 P2 |
| 提权启动校验级别 | 安全 P1 | P0 | 负责人书面接受风险 → 可降级（不建议） |
| UI 测试引用 WPF 主工程 | 验收视为耦合症状 | P2 | 认定为必要设计 → 降 P3 |
| 「CI 很强」 | 安全：强的是构建/签名 | 采纳 | 客户端验签与提权启动校验不可由 CI 强度互推 |

---

## 九、覆盖与未覆盖

| 维度 | 覆盖程度 |
|---|---|
| 架构与依赖 | 高（项目图、命名空间、文件行数、分区均有实测计数） |
| 安全 | 高（下载链、zip-slip、参数注入、密钥、路径守卫、提权点）；低（`Gloam.Interop` 的 P/Invoke 面、注册表 ACL、managed DLL 植入面） |
| CI/CD | 高（触发、门禁、签名、密钥形态）；无法取证（分支保护、Actions 历史） |
| API / 接口契约 | 中低。本仓库为桌面应用，无 REST/GraphQL 面；实际接口是本地化目录 key 契约（P2-3）、进程边界（已用 `ArgumentList`）、`SettingsManager` 公开面（P2-5）。**未系统审计**：错误返回形状一致性、`any` / 裸 cast、静默 fallback 分布 |
| 代码质量五轴 | 中。正确性覆盖到 P1-7 与枚举持久化；**性能维度基本空白**（色彩算法/LUT 数值正确性、HDR 渲染管线、N+1、热路径未评估） |
| 测试执行 | **零**（1137 `Fact` + 94 `Theory` 一条未跑） |

**本环境不可验**：Win10/11 实机 HDR、色度计标定、签名证书链与 SmartScreen 声誉、Velopack 包内部结构、`package.ps1` 权限细节、517KB `ARCHITECTURE.md` 与 2976 行 code-behind 的逐行业务正确性。

---

## 十、变更记录

| 日期 | 变更 | 说明 |
|---|---|---|
| 2026-11-02 | 初始登记 | 基于三份独立静态审查（架构 / 安全 / 验收）合并去重并完成定级裁决 |
