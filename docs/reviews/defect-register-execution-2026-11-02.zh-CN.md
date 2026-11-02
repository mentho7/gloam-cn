# Gloam 缺陷登记册执行报告

| 项 | 值 |
|---|---|
| 仓库 | `D:\WorkSpace\gloam` |
| 分支 | `codex/zh-terminology-review` |
| 依据 | `docs/reviews/defect-register-2026-11-02.zh-CN.md` |
| 执行日期 | 2026-11-02 |
| 执行范围 | 登记册「七、发版门禁」全部 7 项 + P1-7（发版前必修） |
| 不在范围 | P2-1~P2-12、P3-1~P3-8（登记册明确「可带已知问题发版」） |
| 执行方式 | **实机执行**：构建、全量测试、脚本、git 快照均已真实运行并留证 |

> 与登记册的关系：登记册是审查时的静态取证快照，本报告是它的执行结果。登记册原文未改动，
> 以免覆盖当时的取证事实。

## 一、执行结论

| 门禁项 | 缺陷 | 状态 | 证据 |
|---|---|---|---|
| 1 | P0-1 提权前未校验安装器 | **已修** | 代码 + 回归用例 4 项通过；手工 UI 验证未做（见 §四） |
| 2 | P0-2 发布快照不完整 | **已修** | `git status --porcelain` 为空；`git archive HEAD` 命中 3 个文件 |
| 3 | P1-1 版本号漂移 / P1-8 tag 不一致 | **已修** | `<Version>` = 1.13.0；脚本断言正反两向验证 |
| 4 | P1-2 严格构建 / P1-3 主干不跑 CI / P1-6 漏洞扫描不阻断 | **已修** | 严格构建 0 警告 0 错误；YAML 结构校验；扫描门禁本地演练 |
| 5 | P1-4 客户端验签 | **取证完成，结论为否；钉扎不可实现** | 见 §三，需负责人决策 |
| 6 | P1-5 无回滚 playbook | **已修** | `RELEASE.md`「Bad release playbook」 |
| 7 | 人工严格构建 + 全量测试 | **已执行** | 1540/1540 通过，耗时 1m02s（复现：`dotnet test src/Gloam.sln -c Release -p:GloamStrictBuild=true`；日志落在 `artifacts/`，该目录被 `.gitignore` 覆盖，不入库） |
| — | P1-7 枚举静默降级为空串 | **已修** | 兜底 key 入两份目录；新增 Theory 两文化断言 |

## 二、逐项变更

### P0-1 提权启动校验（`src/Gloam.Core/Calibration/UsbDriverHelper.cs`）

**根因复核**：`GetDriverInstallerPath()` 的候选来源确实包括应用自带的 `argyll_cache`、
`%LOCALAPPDATA%\Argyll\usb`、DisplayCAL 的 AppData 下载目录 —— 全部当前用户可写；
`LaunchDriverInstaller` / `...Async` 直接以 `Verb="runas"` 启动，下载期的 SHA-256 钉扎不覆盖这条路径。

**修复**：

1. 新增钉扎哈希 `TrustedInstallerSha256`。取值**不是凭空假设**：本地
   `argyll_cache_3.5.0.zip` 实测 SHA-256 = `C80F78BA…3F9D132`，与源码
   `ArgyllDownloader.ArgyllArchiveSha256` 逐字符一致；从该包内直接抽取
   `Argyll_V3.5.0/usb/ArgyllCMS_install_USB.exe` 计算得
   `D13D0A94748BD2559565C4DDB5BAE103EB60A0DA1E3E3F58482780900D7289AF`。
2. `OpenVerifiedInstaller` 以 `FileShare.Read` 打开文件（**拒绝他人写入与删除**），在同一句柄上
   算哈希、再启动。原登记册只要求「`Process.Start` 前校验」，但校验与使用之间存在替换窗口；
   持有拒绝写句柄把窗口关掉。
3. 失败即 fail closed：`return null` 且给出原因，不启动进程、不弹 UAC。
4. 返回类型由 `bool` 改为 `DriverInstallerLaunchOutcome`（5 值），因为「没跑起来」有三种完全不同的
   原因，需要三种提示。唯一调用方 `DriverInstallDialog` 原先**丢弃返回值并无条件
   `_driverInstalled = true`**，即进程根本没启动也会显示「驱动安装已完成」；现按结果分支，
   校验失败时显示「未通过校验」而不是「已完成」。
5. 新增本地化键 `calibration.driver.installer.integrity.failed[.detail]`（en-US / zh-Hans 各一条）。

**回归用例**（`tests/Gloam.Tests/UsbDriverInstallerIntegrityTests.cs`）：

- 非钉扎字节 → `IntegrityCheckFailed`（同步与异步各一条）
- `null` / 空串 / 不存在的路径 → `InstallerNotFound`（与「被拒」区分开）
- `IsTrustedInstaller` 对任意字节、`null`、空串、缺失路径均返回 false

正向用例（钉扎字节真的启动）**故意不写**：会真弹 UAC 并给测试机装驱动。

### P1-7 本地化降级（`src/Gloam/Features/NightMode/INightModeText.cs`）

三处 `switch` 合为一张键表 `Catalog()`，降级点从 3 个收敛为 1 个 `UnknownCatalog`，
兜底键 `night.profile.unknown` / `night.mode.unknown.description` / `night.mode.unknown.summary`
已入两份目录。原测试第 112 行断言的正是缺陷行为（`Assert.Equal(string.Empty, ...)`），已改为断言非空，
并新增 `Unknown_algorithm_falls_back_to_a_key_defined_in_the_catalog` 覆盖 en-US / zh-Hans 两文化。

### P1-1 / P1-8 版本治理

- `<Version>` 1.10.0 → **1.13.0**。
- `scripts/Test-UpdateFeedConsistency.ps1` 新增断言：`<Version>` 必须等于
  `docs/release-notes/v*.md` 中的最高版本。正反两向已验证（1.10.0 → 失败；1.13.0 → 通过）。
- **P1-8 取证结果**：`git ls-remote --tags origin` 远端最高为 `v1.10.0`，
  **不存在 `v1.13.0`**（本地也没有）。本地仅有未推送的 `v1.12.0` / `v1.12.1` /
  `v1.11.0-i18n.1`。故 1.13.0 同时大于「线上最高版」与「本地最高已打标版」。
  登记册所说「文档声称 v1.13.0 vs 实际 v1.12.1」的落差来源已查清：**远端从未收到 1.11 以上任何 tag**。

### P1-2 / P1-3 / P1-6 CI 门禁

| 改动 | 位置 |
|---|---|
| `env: GloamStrictBuild: 'true'`（validate job） | `.github/workflows/build.yml` |
| `on.push.branches: ["main"]` | 同上 |
| 漏洞扫描改为解析 `--format json` 并显式失败 | 同上 |

`--fail-on-vulnerable` 在本 SDK **不存在**（`dotnet list package --help` 无此开关），
登记册给的备选方案（解析输出判空后失败）已采用。本地演练：当前依赖树输出
`No known vulnerable packages.`，脚本正常退出。

### P1-5 / P1-4 文档

- `RELEASE.md` 新增「Bad release playbook」：禁止降号 / 禁止复用 tag、必须发 `X.Y.Z+1`、
  从坏 tag 拉 hotfix 分支、Portable zip 逃生路径、**并明确标注哪几步尚未演练**。
- `SECURITY.md` 新增「Known limitations」，见 §三。

## 三、P1-4 取证结论（需决策）

**取证方法**：检查本机 NuGet 缓存中 `Velopack 1.2.0` 的 `lib/net10.0/Velopack.dll` 与 `Velopack.xml`。

**证据**：

| 检索目标 | 结果 |
|---|---|
| `Ed25519` / `signature` / `signParams` / `publicKey` 相关符号 | **零命中**（XML 文档与 DLL 可打印字符串均无） |
| 校验和校验 | 命中：`Velopack.UpdateManager.VerifyPackageChecksumAsync`、`ChecksumFailedException`、`CalculateFileSHA1AndSHA256Async`、`SHA1`/`SHA256` 属性 |

**结论**：客户端**只做校验和比对**，比对基准是同一 feed 的 `releases.win.json`。
能改 feed 的人可以同时改校验和，因此校验和不构成真实性证明。
**Velopack 1.2.0 客户端不含任何非对称签名验证代码，也没有可供外部注入验签的钩子**——
这意味着登记册给出的「钉扎公钥（`--signParams`）」在该库版本上**无法在客户端兑现**。

**已落地的替代控制**（写入 `SECURITY.md`）：GitHub 账号 + `release` environment 为实际信任锚
（2FA / 分支保护 / required reviewer）、产物签名并在上传前校验
（`Test-SignedReleaseArtifacts.ps1`）、只由 CI 从 tag 出包。

**需决策**：接受该风险（已按此写入文档）／ 更换更新机制 ／ 升级到提供验签能力的 Velopack 版本。
在决策前，该项按登记册定义仍属「未定论 = 阻断」。

## 四、验证证据

| 证据类型 | 命令 | 结果 |
|---|---|---|
| 构建（基线） | `dotnet build src/Gloam.sln -c Release` | 成功，0 警告 0 错误 |
| 构建（严格） | `dotnet build src/Gloam.sln -c Release -p:GloamStrictBuild=true` | 成功，0 警告 0 错误 |
| 全量测试 | `dotnet test src/Gloam.sln -c Release -p:GloamStrictBuild=true` | **1540 / 1540 通过，0 失败**，耗时 1m02s |
| 针对性测试 | `--filter UsbDriverInstallerIntegrityTests\|NightModeLocalizationTests\|LocalizationAuditTests` | 20 / 20 通过 |
| 发布门禁脚本 | `scripts/Test-UpdateFeedConsistency.ps1` | `Update feed consistency passed.` |
| 漏洞扫描门禁 | 本地演练 build.yml 中的 pwsh 步骤 | `No known vulnerable packages.`（退出 0） |
| 快照完整性 | `git status --porcelain` / `git archive HEAD \| tar -t` | 0 行输出；命中 `README.zh-CN.md`、`docs/release-notes/v1.11.0-v1.13.0.zh-CN.md`、`docs/reviews/defect-register-2026-11-02.zh-CN.md` |

> 登记册原先记录「1137 `Fact` + 94 `Theory` 一条未跑」。本次首次全量执行，实际用例总数 1540，
> 全部通过。该「测试执行 = 零」的覆盖缺口就此关闭。

## 五、未验证项（必须明示）

| 项 | 为什么没验证 | 影响 |
|---|---|---|
| P0-1 验证方式 ③：篡改文件后点「安装驱动」，确认**不弹 UAC**且显示校验失败 | 需要真实 UAC 交互与可视界面，本环境无法自动断言 | 逻辑层已由单测覆盖（拒绝即不启动），但「UAC 确实没弹」未经人眼确认 |
| P0-1 正向路径：钉扎字节在**持有拒绝写句柄**期间能否被 `ShellExecute` 正常拉起 | 需要真实 UAC 交互；无法在不弹 UAC 的前提下验证 | 若某些环境下 `ShellExecute` 需要写权限（如清理 MOTW 流），会退化为提示「无法启动安装程序」。这是本次修复唯一的假定，上机验证请优先确认这一条 |
| P0-1 验证方式 ②：正确哈希夹具 → 返回 true | 需要把 480KB 官方 exe 作为夹具入库，或真跑安装 | 正向路径未自动化；哈希值本身已用可信包双向核对 |
| CI 三项（P1-2/3/6）在 GitHub Actions 上的实际行为 | 本地无 Actions runner | 仅验证了 YAML 结构与脚本逻辑；CI 首跑需人工确认绿灯 |
| 签名链、SmartScreen、实机 HDR、色度计标定 | 环境不具备 | 登记册 §九 已列为「本环境不可验」，本次未改变 |
| P1-5 回滚演练 | 会改动本机安装，需授权 | 已在 RELEASE.md 以表格如实标注「未演练」 |

## 六、给负责人的三件事

1. **P1-4 决策**：接受风险（文档已按此写），还是更换/升级更新机制。未决前该门禁项按登记册仍算阻断。
2. **CI 首跑确认**：三项门禁改动只做了本地校验，第一次 push 后需看 Actions 是绿灯，特别是
   `GloamStrictBuild: 'true'` 是否在 runner 上引入新的 SDK 差异告警。
3. **驱动安装手工验证**（P0-1 第 ③ 条）：找一台有 UAC 的机器，把 `%LOCALAPPDATA%\Argyll\usb\`
   下的 exe 换成任意文件，点「安装驱动」，确认不弹 UAC 且提示「未通过校验」。

## 变更记录

| 日期 | 变更 | 说明 |
|---|---|---|
| 2026-11-02 | 初始执行报告 | 覆盖登记册发版门禁 7 项 + P1-7；P2/P3 按登记册约定不在本次范围 |
