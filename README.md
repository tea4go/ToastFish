<p align="center">
  <img src="Resources/chika128.ico" width="128" height="128" alt="图标"/>
</p>

<div align="center">

# ToastFish

#### 这是一个利用 Windows 通知栏背单词的软件
#### 可以让你在上班、上课等恶劣环境下安全隐蔽地背单词

![License MIT](https://img.shields.io/badge/license-MIT-orange)
![GitHub release (latest by date)](https://img.shields.io/badge/release-v3.0-blue)
![GitHub issues](https://img.shields.io/github/issues/Uahh/ToastFish)
[![.NET Build & Test](https://github.com/Uahh/ToastFish/actions/workflows/dotnet-desktop.yml/badge.svg)](https://github.com/Uahh/ToastFish/actions/workflows/dotnet-desktop.yml)

</div>

## 项目简介

ToastFish 是一款运行在 Windows 平台的背单词工具。它不占用桌面、不弹独立窗口，而是把单词以**系统通知（Toast）**的形式推送到通知栏，你只需要在通知上点一下按钮，就能完成「认识 / 不认识 / 发音」等操作。

软件常驻系统托盘，支持全局快捷键触发，界面隐蔽，适合在上班、上课等场景下悄悄摸鱼背单词。

## 功能特性

- **通知栏背单词**：单词、音标、释义、例句以 Windows 通知形式推送，点击按钮即可交互。
- **多词库支持**：内置四六级、考研、雅思、托福、GRE、GMAT、SAT、专四专八等英语词库，以及日语词汇与五十音。
- **SM-2+ 记忆算法**：基于 SuperMemo SM-2 改进的间隔重复算法，根据「没有印象 / 记忆模糊 / 暂时记住 / 已经牢记」自动安排复习节奏。
- **语音发音**：支持美音 / 英音，优先在线下载发音，失败时回退到系统 TTS 合成；日语、五十音使用内置音频。
- **背完即测**：每轮背诵结束后自动进行「中译英选择」与「填空选择」测验，加深记忆。
- **随机测试**：随时对英语单词、日语单词、五十音进行随机抽测。
- **背诵记录**：每次背诵自动导出 xlsx 记录到 `Log` 目录。
- **导入单词**：可将背诵记录或自定义 Excel 导入，重新背诵。
- **自定义内容**：通过自定义 Excel 模板，让 ToastFish 推送任意内容。
- **托盘与热键**：常驻托盘，支持开机自启与全局快捷键。
- **进度管理**：显示当前词库进度，并支持一键重置学习进度。

## 技术栈

- **运行环境**：.NET Framework 4.7.2（WPF 桌面应用）
- **开发工具**：Visual Studio 2019（C#，LangVersion 7.3）

主要依赖库：

| 依赖 | 用途 |
| --- | --- |
| Microsoft.Toolkit.Uwp.Notifications | 构造与推送 Windows Toast 通知，接收通知按钮回调 |
| System.Data.SQLite + Dapper | 词库存储与轻量 ORM 查询 |
| NPOI | 背诵记录的 Excel（xlsx）导入 / 导出 |
| MvvmLight / Prism.Core / CommonServiceLocator | MVVM 框架与依赖定位 |
| MP3Sharp | MP3 发音播放 |
| System.Reactive | 全局热键事件流（Subject / Observable） |
| System.Speech | 系统 TTS 语音合成（发音回退方案） |

## 项目结构

```
ToastFish/
├── App.xaml(.cs)               # 应用入口
├── View/
│   └── ToastFish.xaml(.cs)     # 主窗口：隐藏窗口 + 托盘图标 + 右键菜单 + 全局热键
├── ViewModel/                  # MVVM 视图模型
├── Model/
│   ├── PushControl/            # 各词库的推送与交互逻辑
│   │   ├── PushWords.cs        # 英语单词（含 SM-2 背诵、测验）
│   │   ├── PushJpWords.cs      # 日语单词
│   │   ├── PushGoinWords.cs    # 五十音
│   │   └── PushCustomizeWords.cs # 自定义内容
│   ├── SM2plus/                # SM-2+ 间隔重复算法（Card / Parameters）
│   ├── SqliteControl/          # SQLite 数据库访问与数据模型（Word / JpWord / GoinWord 等）
│   ├── Mp3/                    # 发音下载（DownloadMp3）与播放（PlayMp3）
│   ├── Log/                    # 背诵记录 Excel 导入导出（CreateLog）
│   └── StartWithWindows/       # 开机启动与全局热键（HotKey）
└── Resources/                  # 词库 inami.db、发音 mp3、自定义模板、图标、使用说明
```

## 使用方法

### 基本流程
1. 选择词库：

![选择词库](https://github.com/Uahh/ToastFish/blob/main/Resources/Gif/选择词库.gif)

2. 设置背诵单词数量：

![设置词数](https://github.com/Uahh/ToastFish/blob/main/Resources/Gif/选择数量.gif)

3. 点击开始：

![设置词数](https://github.com/Uahh/ToastFish/blob/main/Resources/Gif/开始.gif)

4. 背完之后会有测试：

![设置词数](https://github.com/Uahh/ToastFish/blob/main/Resources/Gif/测试.gif)

### 支持的词库

| 分类 | 词库 |
| --- | --- |
| 英语 | 四级核心词汇、四级完整词汇、六级核心词汇、六级完整词汇、GMAT 词汇、GRE 词汇、IELTS 词汇、TOEFL 词汇、SAT 词汇、考研必考词汇、考研完整词汇、专四真题高频词、专四核心词汇、专八真题高频词、专八核心词汇 |
| 日语 | 标准日本语中级词汇 |
| 五十音 | 顺序五十音 |

### 快捷键

| 快捷键 | 功能 |
| --- | --- |
| `Alt` + `Q` | 开始内置单词学习 |
| `Alt` + `` ` ``（`~`） | 英语单词发音 |
| `Alt` + `1` ~ `4` | 对应点击通知上的按钮 1 ~ 4 |

### 背诵记录
每一次点击开始都会有记录，文件格式为 xlsx。位于安装目录的 Log 文件夹下。

### 导入单词
可以将背诵记录导入，重新背诵。

![设置词数](https://github.com/Uahh/ToastFish/blob/main/Resources/Gif/导入单词.gif)

### 自定义内容
可以通过自定义 Excel 内容来让 ToastFish 推送所需要的内容。
自定义 Excel 模板位于安装目录 /Resources/自定义模板.xslx

![设置词数](https://github.com/Uahh/ToastFish/blob/main/Resources/Gif/导入自定义单词.gif)

### 操作系统要求
Windows 10 及以上

### Q&A
Q: 每次通知停留时间太短了，如何设置停留时间？
A: 可以在系统设置 -> 轻松使用 -> 显示 -> 通知显示的时间 里设置停留时间

Q: 使用英语发音功能时会闪退？
A: 请在系统设置里下载英语语音包，重启软件即可解决。

Q: 有没有 Win7 或是 Mac 版本的开发计划？
A: 这个真没有，Win7 和 Mac 没有 Windows 10 的通知栏。

Q: 没有我想要背的单词怎么办？
A: 可以使用自定义功能自己构建单词列表，如果单词数量很多，可以联系作者帮忙添加。

Q: 遇到了其他困难或是想给软件提建议？
A: 可以提 Issue，将问题或建议提供给我。

Q: 软件收费吗？
A: 软件完全开源且免费。

## 下载与安装
1. 可以去网盘下载，下载双击安装 ToastFishSetup.exe 即可。
```bash
链接：https://pan.baidu.com/s/1VlnJSSbEgcNErV-gy3um6w
提取码：2173 
```
2. 也可以去项目 Tag 处下载 Release 版本，解压即可免安装运行。

## 编译源码
请在 cmd 中运行
```bash
git clone https://github.com/Uahh/ToastFish
```
项目使用 VS2019，.NET 环境为 4.7.2。

## 技术实现简述

- **通知推送**：通过 `Microsoft.Toolkit.Uwp.Notifications` 的 `ToastContentBuilder` 构造通知，按钮以 `action` 参数标记（如 `succeed` / `fail` / `voice` / `again` / `hard` / `good` / `easy`），应用订阅 `ToastNotificationManagerCompat.OnActivated` 接收回调。
- **交互模型**：每个通知的等待通过 `TaskCompletionSource` 转换为 `Task`，背诵主流程以 `await` 阻塞等待用户点击，从而实现「推一条、答一条」的节奏。
- **记忆算法**：`Model/SM2plus` 中实现了带学习 / 重学阶段的 SM-2 变体。卡片状态在 `New → Step1 → Step2 → Reviewed` 间迁移，复习间隔根据难度 `difficulty` 与上次评分动态调整，`percentOverdue` 用于挑选最该复习的卡片。
- **数据存储**：词库与学习进度存放在 `Resources/inami.db`（SQLite）。单词表按词库名区分，复习相关字段（`difficulty`、`daysBetweenReviews`、`lastScore`、`dateLastReviewed`）在首次使用时自动补列；全局配置（当前词库、单词数量、发音类型、自动发音、自动日志）存于 `Global` 表。
- **发音**：优先按美音 / 英音下载并播放 MP3，下载失败时回退到 `System.Speech.Synthesis.SpeechSynthesizer` 进行 TTS 朗读；五十音与日语使用内置音频文件。

## 许可证

本项目基于 [MIT License](LICENSE) 开源。

## 感谢

感谢 @itorr 为本软件提供的支持、建议和测试！
