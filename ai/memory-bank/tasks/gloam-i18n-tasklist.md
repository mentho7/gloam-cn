# Gloam 多语言（en-US / zh-Hans）开发任务清单

## 规格摘要

**需求来源**：用户要求"下一版本支持多语言切换，满足热切换，考虑字符数不一致的布局影响"，范围锁定英文 + 简体中文。

**已交付（v1.11.0-i18n.1，仓库标签）**
- 本地化内核：JSON 目录、`zh-CN → zh-Hans → zh → en-US` 回退链、CLDR 复数、`{name}` 命名占位符、缺失键记录、跨语言校验器
- WPF 适配层：`{loc:Tr key}` 标记扩展（返回 Binding，切换时抛 `PropertyChanged("Item[]")`）、`LanguageService`、CJK 字体策略
- 界面迁移：托盘 22 + 仪表盘 38 + 设置 37 + 游戏实验室 7 + 窗口框架 4 + 夜间配置 4 = **112 键**
- 治理：键存在性、键被引用、占位符一致性、复数完整性、硬编码基线棘轮（5 条会失败的测试）

**技术栈**：.NET 10（LTS）· WPF · MVVM · xunit · Velopack

**目标**：v1.12.0 达成"简体中文可作为主语言使用"，v1.13.0 完成全量覆盖。

---

## 开发任务

### [x] Task 1：托盘菜单迁移
**验收**：菜单项、启动勾选、游戏模式标签、悬浮提示、四条通知全部中文化；切换语言后菜单立即重建（不是等下次刷新）。
**文件**：`Features/Tray/TrayViewModel.cs`、`Resources/i18n/*.json`
**结果**：20 键，基线 59 → 39

### [x] Task 2：仪表盘首屏迁移
**验收**：不展开任何折叠面板即可看到的每个字都跟随语言；语言切换后无残留旧语言。
**文件**：`Features/Dashboard/DashboardWindow.xaml`、`DashboardViewModel.cs`、`GameLab/GamerModeItem.cs`
**结果**：38 键，`DashboardWindow.xaml` 76 → 56

### [x] Task 3：设置窗口迁移
**验收**：显示器、界面语言、伽马、亮度、色温/色调、夜间覆盖、Ultra Night、RGB 微调、底部按钮全部中文化；品牌名与 `Gamma 2.2/2.4` 保留原文。
**结果**：37 键，`SettingsWindow.xaml` 32 → 4

### [x] Task 4：热切换失效链修复（三态规则）
**验收**：XAML 绑定自动更新；VM getter 通过 `OnPropertyChanged(string.Empty)` 重读；字段在切换时先重写再通知；`static readonly` 列表改计算属性。
**结果**：三种变体各自修复，写入 `docs/localization.md`

### [x] Task 5：截图自动化
**验收**：一条命令产出两种语言的真实窗口渲染图；不止于首屏（折叠块展开、超屏内容滚动补帧）。
**结果**：`tests/Gloam.Tests/UiSnapshotTests.cs`，6 图 + 12 PNG

### [ ] Task 6：对话框改为按 key 取文案（P1，0.5 天）
**描述**：`ConfirmDialog` / `BusyDialog` 现在接收已解析的字面量，调用点必须自己查语言，迁移成本按调用点放大；`ConfirmDialog.cs:133` 还内置了硬编码 `"OK"`。
**验收**：
- 新增接收 `(string titleKey, string messageKey, ...)` 的重载，内部解析
- 保留字面量重载并标记 `[Obsolete]`，避免一次性改全部调用点
- 删除内置 `"OK"`，改目录键
- 调用点迁移后，`--smoke` 与全量测试仍绿

**文件**：`Infrastructure/Dialogs/ConfirmDialog.cs`、`BusyDialog.cs`

### [ ] Task 7：修掉断言英文界面文案的测试（P1，0.5 天）
**描述**：`CalibrationDetectionTests.cs:85` 断言 `"Detection timed out"`，迁移该文案时必然红，且它无法验证本地化正确性。
**验收**：改为断言目录键经解析后的值，或断言错误码而非显示文本；同类断言全库清零（可用 grep 门禁）。

### [ ] Task 8：仪表盘折叠面板迁移（P1，2 天）
**描述**：夜间模式控制、日光节律、Advanced 分组仍在折叠面板内，展开即为英文。
**验收**：展开后无英文残留（专有名词除外）；切换语言时展开状态与滚动位置不变。

### [ ] Task 9：Game Lab 独立窗口迁移（P1，2 天）
**描述**：`GameLabWindow.xaml`（51）+ `GamerModeItem`（92 中属于 UI 的部分）。
**验收**：扫描、添加、库观感、空状态、底部提示全部中文化；游戏标题等用户数据不翻译。

### [ ] Task 10：校准窗口与报表迁移（P2，4 天）
**描述**：`CalibrationReportWindow`（264 + 63）、`CalibrationSetupViewModel`（92）、`CalibrationWindow`（84）、`CcssBrowserWindow`（65）。体量最大，打开频率最低。
**验收**：向导、结果页、报表、CSS 浏览器全部中文化；**报表输出的术语需单独确认**（是否保持英文供支持团队阅读）。

### [ ] Task 11：伪语言 `qps-ploc` 与文本膨胀回归（P2，1 天）
**描述**：当前只有中英双语，中文普遍更短，布局风险被掩盖。伪语言把文本扩到 140% 并加标记，作为布局压测语言。
**验收**：伪语言目录可在 CI 生成、不入库；在伪语言下截图无截断；新增一条布局回归测试。

### [ ] Task 12：截图任务接入 CI（P3，1 天）
**验收**：每次改动 UI 相关文件后自动产出截图并作为构建产物归档；文档引用的图与代码同步。

### [ ] Task 13：发布链路验证 `vpk pack`（P3，0.5 天）
**描述**：唯一未验证环节。`dotnet publish --self-contained` 已验证，`vpk` 读同一份产物。
**验收**：在打包机上跑通 `vpk pack`，产出可安装包并完成一次升级验证。

### [ ] Task 14：外部语言包机制文档化（P3，0.5 天）
**验收**：`%LOCALAPPDATA%\Gloam\i18n` 覆盖机制有可操作说明 + 一份翻译指南（含术语表、占位符规则、长度约束）。

---

## 质量要求

- [x] 严格构建零警告（`-p:GloamStrictBuild=true`）
- [x] 全量测试绿（当前 1498）
- [ ] 新增界面文字必须走目录，否则基线棘轮失败
- [ ] 每批迁移后基线必须下降（只降不升）
- [ ] 不使用后台进程、不启动服务
- [ ] 图片素材仅来自 Unsplash / picsum

## 技术备注

**版本节点**（均为附注标签，可回退）
| 标签 | 内容 |
|---|---|
| `upstream-fork-point` | 上游最后提交 264b7e6 |
| `m1-ui-structure` | 结构重组完成，1449 测试绿 |
| `m2-i18n-core` | 内核就绪，尚未迁移字符串 |
| `m3-tray-menu` | 托盘首个中文界面 |
| `v1.11.0-i18n.1` | 当前候选版本 |
| `net11-snapshot` | .NET 11 尝试留档 |

**已知取舍**
- .NET 10 LTS 而非 11 STS：下一次强制迁移是 .NET 12
- `AnalysisLevel` 钉版本，不用 `latest-*`：升 SDK 不改变构建行为
- 中文作为源语言的反向校验暂缺：以英文为源、中文为译，单一方向

**时间预期**
剩余 9 项任务约 12.5 人日。按 v1.12.0 目标（Task 6–9）约 5 人日，v1.13.0（Task 10–11）约 5 人日，收尾（12–14）约 2 人日。
