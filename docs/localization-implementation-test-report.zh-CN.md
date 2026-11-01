# 多语言刷新与策略访问改造测试报告

日期：2026-11-01（Asia/Shanghai）

## 交付结论

已修复夜间模式英文界面残留中文算法名称和说明的源码缺陷，补充统一的语言刷新订阅组件，并将夜间模式文案访问收敛到强类型接口。中英文内容继续分别保存在 `en-US.json` 与 `zh-Hans.json`；没有复制中英文业务实现，也没有改变色彩计算。

最终严格构建通过：**0 警告、0 错误**。

最终完整测试通过：**1532 / 1532，失败 0，跳过 0**。测试运行约 1 分 9 秒。本次新增 8 个测试用例，全部包含在完整测试中。

## Git 版本管理

开发分支：`codex/localization-strategies`。

| 提交 | 内容 |
| --- | --- |
| `94206d7` | 保存开始工作前已有的校准流程、翻译资源和打包勘测改动，作为独立检查点 |
| `22ef47d` | 修复夜间模式翻译缓存，新增统一刷新订阅和基础回归测试 |
| `afec32f` | 引入强类型文案策略，接入 Dashboard，修复 Game Lab 预设缓存，扩充测试 |
| 本报告所在后续文档提交 | 架构说明与最终测试报告 |

最终完整测试所对应的代码提交为 `afec32f`。后续仅修改文档。所有提交均为本地提交，没有推送、合并 main 或发布 Release。

## 实现说明

### 1. 消除翻译结果缓存

原 `AvailableAlgorithms` 在 ViewModel 创建时捕获列表，列表中的 Label 又保存当时翻译，导致语言变化后仍显示旧文字。

现在列表及选项对象保持稳定，Label 在读取时通过文案策略查询当前语言。语言切换时通知选项的 Label 和 ViewModel 的 AlgorithmDescription，避免重建列表导致选中值改变或触发保存。

NightMode、Dashboard 和 Game Lab 相关 ComboBox 使用显式的 Label 数据模板，收起状态也能接收属性变化通知，不依赖一次性的 ToString 展示。

### 2. 统一刷新生命周期

新增 `Gloam.Localization.Wpf/LanguageRefreshSubscription.cs`，统一处理：

- 订阅 CultureChanged。
- 在创建订阅的 Dispatcher 线程上执行刷新；后台切换通过 Dispatcher 投递。
- Dispose 时退订。
- 已经投递但尚未执行的刷新，在 Dispose 后不再执行。

NightMode 控件在 Loaded 时恢复订阅并补刷，在 Unloaded 时释放订阅；Dashboard 和 Game Lab 在所属 ViewModel 释放时退订。

这个组件要求调用方管理生命周期，并不会自动扫描或刷新所有 ViewModel。

### 3. 强类型、策略式访问

新增 `INightModeText`，提供接受 `NightModeAlgorithm` 枚举的 Label、Description、Summary 方法。

`CatalogNightModeText` 是统一的 JSON 资源策略适配器：它集中维护枚举与资源键的映射，将实际语言选择交给已有 ILocalizer。NightModeScheduleViewModel 可注入替代策略，测试已经验证替代策略实际被调用。Dashboard 使用同一文案接口实现。

这样既保留语言文件隔离，也避免每种语言维护一份算法分支或 ViewModel。接口覆盖当前界面提供的四种算法；Core 中未在此选择器提供的 BlueReduction 没有被新增到界面，未知枚举值保持空文本回退。

### 4. 同类问题修复

Game Lab 的 AvailableLibraryPresets 也捕获了翻译后的 Label。现在预设保存资源键，Label 按当前语言查询，切换时刷新原对象。预设的 Intent、HdrCapableOnly 和选中对象保持稳定。原匿名语言订阅改为可释放的统一订阅。

## 测试环境与命令

- Windows x64。
- .NET SDK 10.0.403。
- 配置：Release。
- WPF 测试使用仓库已有的 STA Dispatcher 测试宿主。

```powershell
dotnet build src/Gloam.sln -c Release --no-restore -p:GloamStrictBuild=true

dotnet test src/Gloam.sln -c Release --no-build --no-restore `
  --logger "trx;LogFileName=localization-final.trx" `
  --results-directory artifacts/localization-tests
```

完整测试使用刚完成严格构建的程序集。无需重建的参数用于确保测试与该次构建一致。

## 新增回归测试明细

测试源文件：[NightModeLocalizationTests.cs](../tests/Gloam.Tests/NightModeLocalizationTests.cs)。

| 场景 | 验证点 | 结果 |
| --- | --- | --- |
| 同一绑定中文 → 英文 → 中文 → 英文 | Label 和说明均正确；列表和选项对象不更换；算法值不变；SettingsEdited 为 0 | 通过 |
| 真实 NightModeScheduleControl | 实际可视树中收起的 ComboBox 标签随语言变化；SelectedItem 不变；ScheduleChanged 为 0 | 通过 |
| 控件对应订阅卸载、恢复 | 释放后不刷新，恢复时补刷，后续语言事件仅产生一次刷新 | 通过 |
| 后台线程切换 | 回调执行在原 UI 线程，释放后已排队回调不执行 | 通过 |
| 英文策略资源覆盖 | 四种可选算法的名称、说明和摘要均非空，没有缺失资源键 | 通过 |
| 中文策略资源覆盖 | 同上 | 通过 |
| 注入替代强类型策略 | ViewModel 的选项标签及说明实际来自注入策略，并随算法选择变化 | 通过 |
| Game Lab 预设切换 | 同一预设对象的绑定标签更新，SelectedLibraryPreset 不变 | 通过 |

完整测试同时包含已有资源键审计、语言资源验证、UI、业务与校准回归测试。没有删除、跳过或放宽已有测试。

开发过程中严格检查曾发现 Dispose 的分析器要求，已补充 GC.SuppressFinalize；新增测试曾误将 Core 全部算法等同于界面可选算法，核对 BlueReduction 的现状后改为校验明确的四个可选项。以上中间失败均已解决，最终结果以完整 TRX 为准。

## 原始证据

- [完整测试 TRX](../artifacts/localization-tests/localization-final.trx)
- [完整测试日志](../artifacts/localization-full-test.log)
- [严格构建日志](../artifacts/localization-strict-final.log)
- [开发阶段定向测试日志](../artifacts/localization-focused.log)（早于最后两个新增用例，最终以完整测试为准）

`artifacts/` 受 Git 忽略，原始日志和 TRX 保存在本机；此报告和测试源码随 Git 提交。换机器后可使用上述命令重新生成。

## 验证边界

- 本次强类型访问首先落地在 NightMode 和 Dashboard 的夜间模式文案，其他模块仍保留现有 Loc.Get / ILocalizer API，没有宣称全项目每个字符串都已迁移成强类型接口。
- 已验证真实控件的收起状态及选项 Label 绑定；没有进行每个弹窗、每个下拉展开状态和所有业务流程的人工截图验收。
- 临时提示、动态操作结果、报告正文等其他文本路径仍需按各自业务状态进行迁移和验证；本次没有把整个应用重新创建来掩盖刷新问题。
- 没有测试显示硬件和色度计，也没有安装、卸载或发布新版本。
- 之前 `artifacts/release-audit/Releases/` 中的安装包仍是前一次构建，不包含此次修复。当前修复已编译到 `src/Gloam/bin/Release/net10.0-windows/`；若需要分发，应从本分支重新运行打包流程。

## 后续新增多语言内容的约定

1. 文案写入独立语言 JSON，业务状态保存枚举或 ID。
2. 固定 XAML 文本继续使用 `{loc:Tr key}`。
3. 复杂功能按领域扩展强类型文案接口，资源键集中于适配器。
4. 不在字段或静态列表中保存最终翻译字符串；可选项保留稳定身份。
5. 动态 getter 接入统一刷新订阅，明确其释放和重新加载行为。
6. 每次新增可切换界面，测试同一对象的双向切换与业务状态不变。
