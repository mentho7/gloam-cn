# Gloam

[![English](https://img.shields.io/badge/lang-English-blue.svg)](README.md)
[![简体中文](https://img.shields.io/badge/lang-%E7%AE%80%E4%BD%93%E4%B8%AD%E6%96%87-blue.svg)](README.zh-CN.md)

修复 Windows HDR 模式下 SDR 内容发灰的问题。逐显示器伽马校正、感知式夜间模式，以及经色度计验证的校准。

[![Build](https://github.com/halideworks/gloam/actions/workflows/build.yml/badge.svg)](https://github.com/halideworks/gloam/actions/workflows/build.yml)
[![Latest release](https://img.shields.io/github/v/release/halideworks/gloam?sort=semver)](https://github.com/halideworks/gloam/releases/latest)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE.txt)

![烛光油画局部，同一画面并排显示两次。左图由 Windows 按 sRGB 曲线解码，暗部背景泛成一片平板的棕灰色雾状；右图为 Gloam 的伽马 2.2 校正后的效果，背景回到人物所处的深黑，烛焰亮度完全不变。](docs/media/hero-comparison.png)

<sub>渲染而非拍摄：源图被推过应用所实现的同一条信号链路（`TransferFunctions.cs`、`LutGenerator.cs`，以及 Windows SDR-in-HDR 的输出模型），一次不经 Gloam、一次经其伽马 2.2 LUT，再重新编码为普通 SDR 屏幕可看的图像。见 [`scripts/render-comparison-images.py`](scripts/render-comparison-images.py)。截图无法呈现这一点：问题本身就在于合成器送往面板的信号与截图捕获到的内容并不一致。</sub>

在 Windows 中开启 HDR 后，SDR 内容会变得平淡、发灰，因为 Windows 呈现它时所用的曲线，显示器本身并非为解码该曲线而设计。Gloam 为每台显示器把正确的曲线放回去。

## 快速开始

1. 下载 [`GloamApp-win-Setup.exe`](https://github.com/halideworks/gloam/releases/latest/download/GloamApp-win-Setup.exe) 并运行。按用户安装，不弹管理员提示。
2. 右键单击系统托盘中的 Gloam 图标。
3. 为需要修复的显示器选择 **伽马 2.2**。

修复到此为止。下面全部是可选的。

## 功能

- 用逐显示器的伽马 2.2、伽马 2.4 或 Windows 默认模式，修复 Windows SDR-in-HDR 的伽马失配。
- 在启动、显示器变更、系统恢复以及驱动或游戏重置 ramp 之后自动恢复校正。
- 提供基于 CAT16 色适应的感知式夜间模式，含固定时间或太阳位置调度、手动覆盖、感知均匀的 mired 空间渐变，以及把蓝光压制到最大的极致夜间配置。
- 提供按游戏的游戏实验室配置，含经校准的暗部可见度，以及「参考」「竞技清晰」「影院级 HDR」与「光谱夜间作战」等画面意图、多显示器窗口定位，以及游戏进行期间锁定调度输出的游戏锁定。
- 在每个配置的「高级」下提供可选项「应用运行期间即使没有焦点也生效」。为第二台显示器上的 MPV 之类应用选择「指定显示器」。获得焦点的配置优先，否则使用第一个匹配的运行中配置。后台应用的启动与退出约在三秒内被检测到。
- 启动时检查 Windows 侧可见的游戏信号链路，包括 SDR/HDR 期望、DXGI 色彩空间、输出位深、HDR 余量与实测校准状态。仪表盘持续给出当前意图与每一项检查结果的实时回执。
- 对选定应用在其位于前台时自动停用夜间模式，可作用于所有显示器，也可只作用于该应用窗口覆盖的显示器。伽马校正与已安装的校准配置文件保持生效。
- 通过 ArgyllCMS 支持 SDR 与 HDR 色度计校准，包括原生 Windows MHC2 配置文件的安装与验证。
- 按显示器记住校准设置，包括目标、预设、色度计校正文件、显示类型与窗口位置。
- 使用安装包安装后，在后台静默更新。
- 完全本地运行。无账户，无遥测。

## 安装

以下链接始终指向当前发布版本：

推荐方式：

- [`GloamApp-win-Setup.exe`](https://github.com/halideworks/gloam/releases/latest/download/GloamApp-win-Setup.exe)
- 按用户安装，不弹管理员提示。
- 自包含，无需另装运行时。
- 内置 ArgyllCMS。
- 退出或重启应用时静默自动更新。

便携版：

- [`GloamApp-win-Portable.zip`](https://github.com/halideworks/gloam/releases/latest/download/GloamApp-win-Portable.zip)
- 解压后运行 `Gloam.exe`。
- 不自动更新。替换为更新的 zip 包即可升级。

发布说明、校验和与旧版本见 https://github.com/halideworks/gloam/releases

若 SmartScreen 提示「Windows 已保护你的电脑」，点击 **更多信息** 并确认已验证的发布者。新签名的应用在积累到足够多安装量之前，仍可能显示信誉警告。

## 使用

![Gloam 的托盘菜单，显示逐显示器的伽马模式](docs/media/tray-menu.png)

1. 启动 Gloam，它会出现在系统托盘。
2. 右键单击托盘图标。
3. 为每台显示器选择伽马模式：
   - 伽马 2.2 适合一般 PC 使用。
   - 伽马 2.4 适合暗房视频或 BT.1886 式观看。
   - Windows 默认：绕过校正。
4. 若希望在登录时恢复校正，启用 **随 Windows 启动**。
5. 打开仪表盘查看显示器状态、夜间模式、校准与诊断。

出现异常？参见 [疑难解答](docs/troubleshooting.md)。

![Gloam 仪表盘：逐显示器状态、夜间模式与校准状态](docs/media/dashboard.png)

快捷键：

- `Win + Shift + F1`：对当前焦点显示器应用伽马 2.2。
- `Win + Shift + F2`：对当前焦点显示器应用伽马 2.4。
- `Win + Shift + F3`：Windows 默认。
- `Win + Shift + F4`：紧急模式（Panic），立即清除伽马 ramp。
- `Win + Shift + N`：切换夜间模式。
- `Win + Shift + G`：打开或关闭当前游戏的独立游戏实验室窗口。

## 夜间模式

![Gloam 的夜间模式时间线编辑器](docs/media/night-mode.png)

夜间模式按你设定的时间表为显示器加暖，可按固定时间，也可按你所在位置的太阳位置。偏移量以色适应的方式计算，而不是直接砍掉蓝通道，因此中性色保持中性、不会发浑；渐变在 mired 空间中匀速步进，使变化在感知上均匀，而不是一端快一端慢。

极致夜间把蓝光压制推到最大，并可在你提供面板光谱样本时使用该样本。

## 游戏实验室

![游戏实验室：逐游戏的画面意图与实时会话回执](docs/media/game-lab.png)

1. 在游戏中按 `Win + Shift + G` 打开紧凑的游戏实验室窗口。桌面端的配置仍可在仪表盘分区中完成。
2. 前台游戏的可执行文件会被预填。窗口打开期间，运行中游戏的候选项会持续刷新。
3. 添加游戏并选择画面意图。竞技清晰会提亮最深的暗部，同时保护中间调；影院级 HDR 保留 PQ 并检查 Windows 信号；夜间作战使用 Gloam 的光谱渲染器。
4. 编辑会自动保存并生效。切换 **配置已启用** 可将该配置与校准基线对比。
5. 保持 **游戏锁定** 启用，可在本次会话期间让调度得出的色温保持稳定。紧急模式与手动夜间模式快捷键仍然拥有最高优先级。
6. 当前会话回执会列出伽马、暗部处理、夜间策略、目标显示器，以及 Gloam 能够验证的信号检查结果。

实现与测量边界见 [游戏实验室设计](docs/gamer-mode-design.md)。

## 校准

![一份 Gloam 校准报告：实测前后对比、逐色块误差与不确定度预算](docs/media/calibration-report.png)

连接受支持的色度计，从托盘选择 **校准显示器**。

推荐流程：

1. 显示器预热至少 30 分钟。
2. 选择显示器、显示类型、目标与校准预设。
3. 对 QD-OLED、广色域 LCD 及其他非标准面板，使用匹配的 `.ccss` 或 `.ccmx` 校正文件。设置对话框可搜索 DisplayCAL 社区数据库并直接下载。
4. 运行校准。测量期间 Gloam 会绕过已有校正。
5. 查看报告。Gloam 会应用该配置文件，并自动经它重新测量一遍。

HDR 桌面校准请使用 **HDR 桌面 PQ（sRGB 色域）**。当面板实测已接近目标时，优先只校正白点而不做全色域校正：对一块本已准确的面板做校正，验证结果往往会变差，而不是变好。

报告中的每个数字都是经已安装配置文件实测得到的，不是预测值。方法本身，以及它不宣称的内容，写在[白皮书](https://getgloam.org/whitepaper)里。

### 硬件验证

Gloam 的校准模型由黄金夹具（golden fixture）固定：每次 CI 运行都会把真实记录的测量集重放进实时管线，因此任何会改变管线基于真实面板数据计算结果的建模改动都会让构建失败。

- **已由黄金夹具验证：** Gigabyte M27Q-P、MSI MAG271QPX28。
- **预期可用、正在征集验证数据：** OLED、QD-OLED、广色域 LCD、mini-LED。

第二份清单是诚实而非谦虚。代码对这些类别带有面板专用处理，但没有已记录的夹具将其固定。如果你拥有其中一类显示器以及受支持的色度计，[贡献一份黄金夹具](docs/golden-fixture-contribution.md)是你能发给我们最有价值的东西。

## 环境要求

- Windows 10 或 11。
- 主要的 SDR-in-HDR 伽马修复需要 HDR 显示器。
- 官方构建无需另装 .NET 运行时。
- 色度计可选。校准使用 ArgyllCMS，Gloam 可自动下载。

## 从源码构建

```powershell
git clone https://github.com/halideworks/gloam.git
cd gloam
dotnet run --project src/Gloam
```

本地打包：

```powershell
.\package.ps1 -Version X.Y.Z
```

无界面检查（headless）：

```powershell
dotnet run --project src/Gloam.Cli -- check-lut 2.2 200 --algorithm perceptual --temp-k 2700 --json artifacts\cli-lut-check.json
dotnet run --project src/Gloam.Cli -- night 2200 --algorithm ultra --basis rec2020 --json -
```

欢迎贡献。见 [CONTRIBUTING.md](CONTRIBUTING.md)。

## 文件位置

- 设置、日志、报告、校正文件：`%LOCALAPPDATA%\Gloam`
- Velopack 安装根目录：`%LOCALAPPDATA%\GloamApp`

报告问题时请从托盘使用 **导出诊断信息**。

## 许可证

Gloam 采用 MIT 许可证。

ArgyllCMS 为 AGPL v3。Gloam 以独立进程方式调用 ArgyllCMS 工具，不链接 AGPL 代码。见 `THIRD_PARTY_NOTICES.txt`。

## 致谢

Gloam 最初是 Dylan Raga 的 [win11hdr-srgb-to-gamma2.2-icm](https://github.com/dylanraga/win11hdr-srgb-to-gamma2.2-icm) 的衍生项目，并保留了他对 Windows SDR-in-HDR 下 sRGB 到伽马校正的识别——整个应用正是建立在这个洞察之上。Gloam 此后已长成一个不同的程序，但它始于此，而当初那步诊断才是最难的部分。

- [ArgyllCMS](https://www.argyllcms.com/)，作者 Graeme Gill：Gloam 每次校准所驱动的测量与特征化工具。
- [MHC2Gen](https://github.com/dantmnf/MHC2)，作者 dantmnf：Windows MHC2 配置文件生成的参考实现。

曾用名 HDR Gamma Controller。
