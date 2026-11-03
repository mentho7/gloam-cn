# Gloam Windows 打包与 GitHub 首版发布指南

> **历史勘测，非当前发布手册。** 本文保留首次构建及后续补记，版本、分支、签名账户和本机日志路径不代表当前状态。日常操作以 [RELEASE.md](../RELEASE.md) 和 [维护规范](maintenance.md) 为准；v1.13.0 的实际未签名状态见 GitHub Release。下文旧示例不可直接作为新版本发布命令。

勘测日期：2026-11-01。本文对应当前本地工作区；上游发布说明见 [RELEASE.md](../RELEASE.md)。

## 1. 项目现状与发布决策

| 项目 | 勘测结果 |
| --- | --- |
| 桌面技术栈 | C# / WPF，`net10.0-windows` |
| 本机 SDK | .NET SDK 10.0.403，满足 `global.json` |
| 主程序 | `src/Gloam/Gloam.csproj` → `Gloam.exe` |
| 发布平台 | 现有脚本固定 Windows x64；未提供 ARM64 构建 |
| 项目默认版本 | `1.13.0`（`src/Gloam/Gloam.csproj` 的 `<Version>`）；发布版本仍以 `v*` 标签为准 |
| 当前分支 | `codex/zh-terminology-review` |
| 当前 origin | `https://github.com/mentho7/gloam-cn.git` |
| 安装器 | Velopack，包 ID `GloamApp`，显示名称 Gloam |
| 更新来源 | `src/Gloam/Infrastructure/Platform/UpdateService.cs` 中的 `RepoUrl`，必须指向本仓库 `github.com/mentho7/gloam-cn`（与 CI 上传目标一致） |
| CI | `.github/workflows/build.yml`，PR / 手动运行验证打包；推送 `v*` 标签会签名并公开发布 |
| 网页依赖 | 根目录 `package.json` 用于静态网站；生成桌面 exe 不需要 `npm install` |

本地存在未提交的校准流程、界面和翻译改动。本次构建会包含这些改动，不等同于某个已提交标签。正式发布前应完成审查和提交，并从干净工作区重建。

先确定发布归属：

- 如果你是 `halideworks/gloam` 的维护者，沿用该仓库和原有更新源，选择未使用且高于现有稳定版的版本。
- 如果这是你的衍生项目，先在自己的 GitHub 账户创建 Fork 或独立仓库，再改远程地址、更新源、下载链接及签名身份。不能假定继承了上游 Azure 权限。
- 新仓库可以从 `0.1.0` 或 `1.0.0` 开始，但继承的 Git 标签仍可能重名，先用 `git tag --list v1.0.0` 检查。若要继续原仓库的升级链，版本必须递增，不能回到 `1.0.0`。

## 2. exe 的三种含义

| 产物 | 用途 | 分发方式 |
| --- | --- | --- |
| publish 目录内的 `Gloam.exe` | 主程序启动入口 | 必须保留整个发布目录，不能只复制这个 exe |
| `GloamApp-win-Setup.exe` | 给普通用户安装，按用户安装、集成自动更新 | 推荐下载入口 |
| `GloamApp-win-Portable.zip` | 解压后运行 `Gloam.exe` | 保留全部文件；不自动更新 |

当前方案为 **self-contained、多文件**，用户无需另装 .NET。项目使用 WPF 并已关闭裁剪；打包脚本明确采用多文件供 Velopack 处理，不应随意改为单文件或 Native AOT。

安装目录为 `%LOCALAPPDATA%\GloamApp`；用户设置、日志和校准报告在 `%LOCALAPPDATA%\Gloam`。这两个路径刻意分开，禁止为了改名直接合并。

## 3. 本地构建流程

在 Windows x64 上安装 .NET 10 SDK，使用 PowerShell，从仓库根目录执行。首次构建需要联网访问 NuGet 和 ArgyllCMS。

```powershell
Set-Location D:\WorkSpace\gloam
dotnet --version
git status --short

dotnet restore src/Gloam.sln
dotnet build src/Gloam.sln -c Release -p:GloamStrictBuild=true --no-restore
dotnet test src/Gloam.sln -c Release --no-restore
./scripts/Test-UpdateFeedConsistency.ps1

# 用选定的纯数字正式版本替换此示例；不要直接复用已发布标签。
./package.ps1 -Version 1.13.0
```

上述 `1.13.0` 只是示例，不表示已保留或已发布。运行测试需要可运行 WPF 的 Windows 桌面环境。

`package.ps1` 实际依次完成：

1. 用 `dotnet publish -c Release -r win-x64 --self-contained true` 编译，传入 `/p:Version` 与 `/p:DebugType=None`。
2. 向 publish 复制 README、MIT 许可及第三方说明；项目文件同时复制 6 个 ICM 模板。
3. 下载 ArgyllCMS 3.5.0 win64 压缩包，校验固定 SHA-256 后解压并打包到程序目录。
4. 执行 `scripts/Test-PublishOutput.ps1`，检查必需文件并以 `--smoke --data-dir <临时目录>` 运行隔离启动检查。
5. 找到或安装固定版本 `vpk 1.2.161`，执行 `vpk pack` 生成安装器、便携包、完整更新包及更新索引。

默认输出为 `publish/` 和 `Releases/`，已被 Git 忽略。脚本会清理指定的 publish 目录，但不会清空 Releases，因此正式版本应使用专门的空输出目录，防止混入历史产物：

```powershell
./package.ps1 -Version 1.13.0 `
  -PublishDir artifacts/v1.13.0/publish `
  -ReleaseDir artifacts/v1.13.0/Releases
```

这会生成本地**未签名**安装包。未签名版不会出现经过认证的发布者；不能在 Release 文案中宣称已签名。

若只需要原始程序目录及依赖：

```powershell
./package.ps1 -Version 1.13.0 -PublishOnly `
  -PublishDir artifacts/v1.13.0/publish
```

### 工具版本与下载故障

当前项目使用 `Velopack` NuGet 1.2.0，CLI `vpk` 固定为 `1.2.161`：CI 安装时显式指定版本。为可重复构建，本地也用下面的命令安装同一固定版本（`package.ps1` 在缺工具时仍会以最新版安装，属待统一项）：

```powershell
dotnet tool install vpk --version 1.2.161 --tool-path artifacts/tools
$env:PATH = "$(Join-Path $PWD 'artifacts/tools');$env:PATH"
./package.ps1 -Version 1.13.0
```

`dotnet tool install` 仅用于首次安装；已有工具时应先查看版本，再决定是否更新。

ArgyllCMS 下载失败时，从 `package.ps1` 中同一官方 URL 获取 `argyll_cache_3.5.0.zip` 放在仓库根目录，重新执行脚本。脚本会重新校验；预期哈希为：

```text
C80F78BA30E715079A00A14C2E7C9F533C58E6C8FDBC27F2D32B3D7813F9D132
```

本次 `https://www.argyllcms.com/` 多次超时或连接中断；改用同一官方网站的不带 www 地址后成功，且哈希一致：

```powershell
Invoke-WebRequest -Uri https://argyllcms.com/Argyll_V3.5.0_win64_exe.zip `
  -OutFile argyll_cache_3.5.0.zip
Get-FileHash -LiteralPath argyll_cache_3.5.0.zip -Algorithm SHA256
```

不要跳过哈希校验。只有在缓存文件被确认损坏时才移走该缓存后重试；不要将未知来源的可执行文件替换进正式包。

## 4. 建立自己的 GitHub 仓库

推荐保留完整历史。GitHub 上创建 Fork 或空仓库；以下命令中的 `YOUR_ACCOUNT` 必须替换为你的账户，且只在 origin 确实仍为上游时执行重命名：

```powershell
git remote -v
git remote rename origin upstream
git remote add origin https://github.com/YOUR_ACCOUNT/gloam.git
git remote -v

# 审查并提交当前分支改动后，再推送当前开发分支。
git push -u origin codex/02-calibration-workflow
```

不要直接 `git push --mirror` 或 `git push --tags`：它们可能带入不准备维护的分支和全部旧版标签。先确认自己仓库的默认分支是 `main`；若空仓库尚无 main，可在确认发布基线后将相应提交推为 main，再用 PR 管理后续变更。

必须同步检查这些位置：

| 位置 | 必须决定的内容 |
| --- | --- |
| `UpdateService.cs` 的 `RepoUrl` | 自己的公开 GitHub 仓库；客户端当前不带访问令牌，不能直接拿私有仓库作为公共更新源 |
| `Test-UpdateFeedConsistency.ps1` 默认 `ExpectedRepoUrl` | 新的仓库地址，避免 CI 按上游地址校验 |
| `README.md`、`RELEASE.md`、`site/` | 下载链接、徽章、发布者及支持入口 |
| `package.ps1` 与 `build.yml` | 包作者、包 ID、签名方式，两处保持一致 |
| `Test-SignedReleaseArtifacts.ps1` | 默认发布者为上游的 `David Torcivia`；换证书后按实际证书 Subject 修改或明确传参 |
| `.github/workflows/deploy-site.yml` | 继承的网站部署；未准备部署自己的站点时，先禁用并审查 |

若要允许上游版与自己的衍生版并存，还需要单独设计安装包 ID、应用数据目录和单实例标识，不能只改 `packTitle`。本次未进行品牌迁移。

MIT 许可、上游归属和 `THIRD_PARTY_NOTICES.txt` 应保留。ArgyllCMS 包内的许可和第三方材料随包保留，发布时一并检查。

## 5. 日常 GitHub 项目管理

建议采用 `main` + 短期功能分支 + Pull Request，不必一开始维护多条长期发布分支。

- Issue 标签：`bug`、`enhancement`、`documentation`、`release`，再按需要补充 `calibration`、`ui`。
- Project 看板：待处理 → 进行中 → 待验证 → 完成；首版建立一个里程碑，关联必需 Issue 和 PR。
- PR：描述用户可见变化、测试结果及校准影响；沿用仓库现有模板。色彩计算变化要遵循 `CONTRIBUTING.md` 的测试及硬件验证要求。
- `main` ruleset：禁止强制推送和删除、要求 PR、要求构建检查通过。多人协作可要求 1 人审查；个人项目不要配置无人能完成的审查门槛。
- `v*` tag ruleset：限制修改和删除，保留维护者创建发布标签的权限。已发布版本通过递增版本修复。
- 二进制放 Release 附件，源码和流程文件放 Git；不要提交 publish、Releases、缓存、证书或访问令牌。

**现有 CI 的实际差异：** `build.yml` 仅对 main 的 PR、`v*` 标签和手动运行触发；普通 main push 不会运行。文档虽称 CI 使用严格构建，但当前 YAML 未传 `GloamStrictBuild=true`。依赖漏洞扫描目前是报告步骤，不能视为“发现漏洞必然阻止发布”的门禁。建议另开 PR 增加 main push 校验、严格构建参数、工具版本固定及明确的漏洞策略。

保护分支前先运行一次 PR，确认 GitHub 显示的实际检查名称，再选择 required checks，避免填写不存在的检查名称。

## 6. 第一版发布：两条可选路线

### 路线 A：先发布本地未签名版本

适合尚未拥有代码签名服务的新仓库。**当前继承的 tag 工作流强制 Azure 签名**，不能未经配置直接推标签或在网页创建标签，期待它自动发布成功。

1. 完成仓库迁移、更新源修改和版本确定。
2. 在自己的分支将 `build.yml` 调整为你实际使用的发布方式：保留验证，移除或显式禁用尚不可用的 tag 签名/上传路径。通过 PR 合并到 main。此指南没有替你禁用现有工作流。
3. 审查、提交并合并所有首版代码。在干净 main 上运行第 3 节的构建、测试与打包，使用空的版本输出目录。
4. 在 Windows 干净测试环境检查 Setup 安装、托盘启动、退出、便携包启动和卸载；校准路径按变更范围进行实际设备验证。
5. 写 `docs/release-notes/vX.Y.Z.md`：新增内容、支持平台、安装方式、未签名状态、已知问题、校准影响及上游来源。
6. 确认相关工作流已调整后，才创建并推送标签。以下示例版本仍需替换：

   ```powershell
   git switch main
   git pull --ff-only origin main
   git status --short
   git tag -a v1.13.0 -m "Gloam 1.13.0"
   git push origin v1.13.0
   ```

7. GitHub 仓库 → Releases → Draft a new release，选择刚推送的已有 tag，填入说明。
8. 上传该版本目录内的**全部 Velopack 产物**，包括 Setup.exe、Portable.zip、完整 `.nupkg` 和生成的更新索引/元数据（例如 `releases.win.json`、`RELEASES`、`assets.win.json`，以实际输出为准）。只上传 exe 会破坏自动更新链。
9. 增加 SHA-256 清单，检查附件后 Publish release。GitHub 自动附带的 Source code zip 不是用户可运行的程序。

生成附件校验清单的示例（路径按版本替换）：

```powershell
$releaseOutput = Resolve-Path artifacts/v1.13.0/Releases
Get-ChildItem -LiteralPath $releaseOutput -File |
  Where-Object Name -ne 'SHA256SUMS.txt' |
  Sort-Object Name |
  ForEach-Object {
    $artifactHash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$artifactHash  $($_.Name)"
  } | Set-Content -LiteralPath (Join-Path $releaseOutput 'SHA256SUMS.txt') -Encoding ascii
```

校验和用于检查下载完整性，不等同于发布者数字签名。若启用 GitHub immutable releases，应在草稿中上传并核对全部附件后再发布。

### 路线 B：沿用现有 CI，自动签名发布

适合拥有仓库维护权限和 Azure 签名服务的情况。先在 GitHub 仓库设置中创建 `release` Environment，并设置以下 Actions Variables：

```text
AZURE_CLIENT_ID
AZURE_TENANT_ID
AZURE_SUBSCRIPTION_ID
AZURE_SIGN_ENDPOINT
AZURE_SIGN_ACCOUNT
AZURE_SIGN_PROFILE
```

Azure 侧必须有可用的签名账号、已完成验证的证书配置文件、应用身份、签名角色和匹配的 OIDC 联合凭据。上游 `RELEASE.md` 的 “already done” 只描述上游，不能作为自己账户已配置好的证据。

工作流使用 `release` Environment。传统 OIDC subject 为 `repo:OWNER/REPO:environment:release`，不要配置成通配标签 subject。GitHub 当前文档还说明部分新建/迁移仓库使用包含 owner/repository ID 的 immutable subject，须按自己仓库的实际 OIDC 配置匹配，不能盲抄上游字符串。

发布步骤：

1. main 已包含全部已审查提交，测试与严格构建通过。
2. 将 `src/Gloam/Gloam.csproj` 的 `<Version>` 改成选定的 `X.Y.Z`，提交对应发布说明。
3. 从该 main 提交创建并推送 `vX.Y.Z` 标签。
4. Actions 依次执行 validate → publish/bundle → Azure login → 下载上一版 → 签名 pack → 签名验证 → `vpk upload github --publish`。
5. 首版没有上一版可下载属于预期；当前该下载步骤 `continue-on-error: true`，其他网络或认证故障也会被此配置忽略，需要查看日志。
6. CI 成功后检查 Release 和签名，并在 GitHub 网页补充发行说明；也可安装 GitHub CLI 后用 `gh release edit vX.Y.Z --notes-file docs/release-notes/vX.Y.Z.md`。

本机当前未发现 `gh`，网页操作即可完成。当前 CI 自动公开发布，不会停在 Draft。如希望草稿审阅，应先修改工作流，或为 `release` Environment 配置审核门槛。

## 7. 正式发布前必须明确的限制

- 当前 `AssemblyVersion` / `FileVersion` 由 `$(Version).0` 拼接。虽然 YAML 接受 `vX.Y.Z-prerelease`，项目还未完整处理这类版本，不能据此认为预发布可用。先用纯数字 `X.Y.Z`；预发布需要单独修正程序集数字版本、给上传加 `--pre` 并验证更新策略。
- 客户端 `GithubSource(... prerelease: false)` 默认仅查稳定发布。网页勾选 Pre-release 后，不应期待稳定客户端自动升级到该版。
- 同一包 ID 的衍生版如果仍使用上游更新地址，可能从你的构建自动更新回上游。正式分发前必须确认地址。
- 当前签名校验脚本要求有效 Authenticode 签名，并检查上游发布者姓名。没有签名服务时走路线 A，不能把签名验证失败描述为发布成功。
- GitHub 标签不等于二进制 Release；标签也不会自动证明产物来自该提交。干净工作区、固定版本和 CI 日志共同构成可追溯证据。
- 完整测试和 `--smoke` 不等于显示设备验证，也不等于安装/卸载和真实自动升级验证。

## 8. 本次勘测与验证记录

- 保留全部原有未提交改动；未推送代码、创建远端标签或发布 GitHub Release。
- 修复 `scripts/Test-UpdateFeedConsistency.ps1` 引用重构前 `src/Gloam/Services/UpdateService.cs` 的问题，改为当前实际路径；脚本运行通过。
- Release 测试：**1524 / 1524 通过**，报告位于 `artifacts/release-audit/tests/release-readiness.trx`。
- 严格构建：**0 警告、0 错误**，日志位于 `artifacts/release-audit/strict-build.log`。
- 本次采用项目当前默认版本 `1.10.0` 构建，仅供本地验证，不应直接当作新的正式 Release。
- CLI LUT 检查通过，报告位于 `artifacts/release-audit/cli-lut-check.json`。
- 使用固定的 vpk **1.2.0** 完成打包；发布目录文件检查及隔离启动检查通过，便携包解压后的 `current/` 应用目录也通过同一检查。
- Setup 的 Authenticode 状态为 **NotSigned**，符合本次本地未签名构建预期。
- 未执行真实安装、卸载、已有用户安装覆盖、自动升级或实际色度计测量。
- 打包日志：`artifacts/release-audit/package.log`。全部产物在 `artifacts/release-audit/Releases/`：

  | 文件 | 大小（字节） | 用途 |
  | --- | ---: | --- |
  | `GloamApp-win-Setup.exe` | 121286791 | 安装器，约 115.7 MiB |
  | `GloamApp-win-Portable.zip` | 116742024 | 便携版，约 111.3 MiB |
  | `GloamApp-1.10.0-full.nupkg` | 116781703 | 完整自动更新包 |
  | `releases.win.json` | 253 | 当前更新索引 |
  | `RELEASES` | 80 | 兼容索引 |
  | `assets.win.json` | 197 | 产物元数据 |
  | `SHA256SUMS.txt` | 生成的清单 | 上述六个文件的 SHA-256 |

本次原始程序目录为 `artifacts/release-audit/publish/`，便携包检查目录为 `artifacts/release-audit/portable-check/`。便携包根部的 `Gloam.exe` 是启动入口，实际应用文件在 `current/`，不要单独移动任一 exe。

## 9. 官方参考资料

- [GitHub：管理 Release](https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository)（草稿、附件及 immutable releases）
- [GitHub：创建分支和标签 rulesets](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/creating-rulesets-for-a-repository)
- [GitHub：OIDC subject 规则](https://docs.github.com/en/actions/reference/security/oidc)
- [Velopack：Windows CLI 参考](https://docs.velopack.io/reference/cli/content/vpk-windows)（`pack`、`upload github`、`--pre`、`--publish`）

外部文档核对日期为 2026-11-01；具体命令参数还应与实际安装的 vpk 版本对应。
