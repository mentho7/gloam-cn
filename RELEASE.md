# 发布手册

适用仓库：`github.com/mentho7/gloam-cn`。日常协作见 [维护规范](docs/maintenance.md)，发布历史见 [CHANGELOG.md](CHANGELOG.md)。

## 当前状态

2026-11-03 发布的 v1.13.0 在发布页明确标为**未签名**。仓库当前为私有，客户端更新服务不携带凭据，因此匿名下载和自动更新不能视为可用。现有 CI 设计为 Azure Trusted Signing 签名发布；配置存在不等于签名或升级验证通过。

[首次构建勘测](docs/windows-build-and-first-release.zh-CN.md) 仅供历史参考。签名与分发问题解决前，可以验证本地包，但不能把验证包称为正式签名发布。

## Release notes

发布准备 PR 同时更新项目 `<Version>`、`CHANGELOG.md` 和 `docs/release-notes/vX.Y.Z.zh-CN.md`（兼容既有 `vX.Y.Z.md`）。说明应包括：

- 用户可见变化与关联 PR / Issue；
- 升级步骤、兼容性、设置或数据迁移；
- 校准数学与实测输出是否变化；涉及变化时记录真实硬件和前后结果；
- 已知问题、验证命令和未执行的检查；
- 实际签名状态、安装与自动升级验证结果。

汇总文章如 `v1.11.0-v1.13.0.zh-CN.md` 不能替代单版本说明。版本号只接受无前导零的 `X.Y.Z`；预发布通道尚未实现，不推送 `vX.Y.Z-rc.1`。

## 一次性发布配置

1. 确定用户能够访问的更新源，使 `UpdateService.RepoUrl`、一致性检查、下载链接和 CI 上传地址一致。私有仓库须先解决分发设计，不能嵌入令牌。
2. 配置 `release` GitHub environment，限制可发布来源；有独立审查者时设置 required reviewers。分支和标签保护的可用性见维护规范。
3. 使用本维护者自己的 Azure Trusted Signing 账户、证书及身份验证。配置 Actions variables：`AZURE_CLIENT_ID`、`AZURE_TENANT_ID`、`AZURE_SUBSCRIPTION_ID`、`AZURE_SIGN_ENDPOINT`、`AZURE_SIGN_ACCOUNT`、`AZURE_SIGN_PROFILE`。不要沿用上游账户名称或声称继承了权限。
4. Azure 联合凭据 subject 对应 `repo:mentho7/gloam-cn:environment:release`，授予证书配置的签名角色。以实际 Azure 配置验证；不能用标签通配符替代 environment subject。
5. 核对 `scripts/Test-SignedReleaseArtifacts.ps1` 的预期发布者与实际证书主体。签名验证失败即停止发布，不能以跳过验证恢复流水线。

## 发布流程

1. 从最新 `origin/main` 建立发布准备 PR，确定尚未使用且高于已发布版本的版本号。合入所有范围内变更；检查 CI 的 `validate` 与 `build` 成功。
2. 在 Windows 桌面环境完成检查，保留结果和失败详情：

   ```powershell
   dotnet restore src/Gloam.sln
   dotnet build src/Gloam.sln -c Release -p:GloamStrictBuild=true --no-restore
   dotnet test src/Gloam.sln -c Release --no-restore
   ./scripts/Test-UpdateFeedConsistency.ps1
   ```

   校准路径改动必须补实机验证；打包或更新路径改动必须验证安装、卸载和旧版升级。未执行项写明原因，不能用单测替代这些结果。
3. 合并发布准备 PR，刷新远端；在干净工作区检查 `origin/main` 的提交和最终差异。用真实版本替换下面的占位符：

   ```powershell
   git fetch origin main --tags
   git status --short
   git log -1 origin/main
   # 确认当前 HEAD 是准备发布的 origin/main 提交后执行：
   ./scripts/Test-UpdateFeedConsistency.ps1 -ReleaseTag vX.Y.Z
   git tag -a vX.Y.Z -m "Gloam X.Y.Z"
   git push origin vX.Y.Z
   ```

   只推送本次标签，不使用 `--tags`、`--mirror` 或强推。推送标签会触发公开发布动作，不是预览。
4. `.github/workflows/build.yml` 先校验版本和说明，再测试、打包、签名并校验产物；`vpk upload github --publish` 会使 Release 生效。当前流程不提供等待人工确认的草稿阶段。
5. 发布成功后立即附上经过审查的说明（`--repo` 避免误操作上游）：

   ```powershell
   gh release edit vX.Y.Z --repo mentho7/gloam-cn --notes-file docs/release-notes/vX.Y.Z.zh-CN.md
   ```

6. 检查 Release 的标签提交、Setup、Portable、更新包与索引是否齐全，下载产物并核对有效签名及时间戳。生成并附上 SHA-256 校验清单，记录实际发布结果，再关闭 milestone。
7. 在测试机验证旧版升级。脚本会修改本机安装，仅在明确准备好测试环境时运行：

   ```powershell
   ./scripts/Test-VelopackInstalledUpdate.ps1 -OlderSetupPath ./older/GloamApp-win-Setup.exe -ExpectedVersion X.Y.Z -IUnderstandThisModifiesLocalInstall
   ```

签名和校验和不能代替功能验证。`%LOCALAPPDATA%\GloamApp` 是安装目录，`%LOCALAPPDATA%\Gloam` 是设置、日志及校准数据目录，必须保持分离。

## 本地验证包

`./package.ps1 -Version X.Y.Z` 生成未签名验证包，`-PublishOnly` 仅生成程序目录。使用独立输出目录避免混入旧产物；保留完整目录，不单独复制 `Gloam.exe`。本地验证包不上传覆盖已发布版本。

## 失败与紧急修复

- 尚未发布成功：保留失败日志、检查远端是否已有部分 Release / 资产。涉及代码修复时使用新版本；不得移动已有标签或盲目使用 `--merge` 覆盖已经分发的内容。
- 已分发坏版本：在发布说明标明影响范围与临时措施，立即准备更高的补丁版本。只改 Release 描述不能阻止客户端更新，也不能使已安装用户自动降级。
- 默认从最新 `main` 创建修复分支，经 PR 和相同门禁合入再发版；若 main 含不能交付的功能，从已发布标签建立临时维护分支，以 PR 审查修复，并将修复同步回 main，记录例外。
- 不删除、移动或复用已经发布的标签，不覆盖同版本二进制。更新客户端只向更高版本升级。旧便携包可作为临时使用路径，但数据兼容性必须先验证。
- 真实更新事故处置演练尚未在本次维护中执行；不要承诺降级安装或删除 Release 能安全回滚。
