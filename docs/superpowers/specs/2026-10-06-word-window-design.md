# 用自绘窗口替换 Windows Toast 通知 — 设计文档

日期：2026-10-06
状态：待实现

## 背景

2026-10-06 排查「点开始不弹单词窗口」时确认了根因：本机 Windows 通知平台（WPN）的
`WpnUserService_b054f` 卡在 Stop Pending 死锁状态，导致**任何进程的任何 toast 调用都永久阻塞**
（用最小探针 `ToastContentBuilder().Show()` 复现：25 秒不返回；绕过 toolkit 直接用 WinRT
`ToastNotificationManager.Show()` 同样挂起）。强制重启该服务后恢复正常。

这件事暴露出一个结构性问题：ToastFish 的**核心功能（显示单词、接收答案）完全依赖 Windows
通知平台**，而该平台一旦异常，应用的核心流程就静默卡死——进程不崩、无日志、无提示。

此外，Toast 的字体由 Windows 渲染，应用无法控制（`Microsoft.Toolkit.Uwp.Notifications` 全库
0 个 Font 相关成员，`AdaptiveText` 只有 Text/Language/HintAlign/HintMaxLines/HintMinLines/
HintStyle/HintWrap）。这直接导致最初的需求「单词窗口的字体能不能设置」无法在 Toast 方案下满足。

## 目标

1. 把单词显示从 Toast 改为应用自绘的 WPF 窗口，**彻底移除对 Windows 通知平台的依赖**。
2. 字体（家族 + 字号）做成可配置项，在设置窗口里能改。
3. 顺带修掉 Toast 的一个体验缺陷：Toast 横幅几秒后消失，用户还没点就点不到了；自绘窗口可以一直显示到用户操作。

## 现状盘点

**Toast 的使用横跨 5 个文件，不只是 `PushWords.cs`。** 按 API 调用点统计：
`ToastContentBuilder` **32 处**、`ToastNotificationManagerCompat.OnActivated` **6 处**、
`ToastNotificationManagerCompat.History.Clear()` **19 处**。

分布：

| 文件 | 类 | ToastContentBuilder | OnActivated | History.Clear |
|---|---|---|---|---|
| `Model/PushControl/PushWords.cs` | `PushWords` | 13 | 4 | 8 |
| `Model/PushControl/PushJpWords.cs` | `PushJpWords` | 6 | 0 | 5 |
| `Model/PushControl/PushGoinWords.cs` | `PushGoinWords` | 12 | 2 | 5 |
| `Model/PushControl/PushCustomizeWords.cs` | `PushCustomizeWords` | 1 | 0 | 0 |
| `View/ToastFish.xaml.cs` | `ToastFish` | 0 | 0 | 1 |

类继承关系：`PushGoinWords : PushJpWords : PushWords`、`PushCustomizeWords : PushWords`。
子类复用基类的 `PushMessage` 与多数等待方法（`PushGoinWords` 另有自己的
`ProcessToastNotificationOrderGoin` / `...GoinQuestion`），但各自有独立的卡片与题目构造方法。

按用途归类：

| 用途 | 方法 | 位置（定义行） | 交互 |
|---|---|---|---|
| 单词卡片 | `PushWords.PushOneWordSM2` | 936 | 4 按钮：没有印象 / 记忆模糊 / 暂时记住 / 已经牢记 |
| 单词卡片 | `PushWords.PushOneWord` | 892 | 3 按钮：记住了！/ 暂时跳过.. / 发音 |
| 单词卡片 | `PushJpWords.PushOneWord(JpWord)` | 35 | 3 按钮：同上 |
| 单词卡片 | `PushGoinWords.PushGoinWord` | 274 | 2 按钮：记住了！/ 发音 |
| 单词卡片 | `PushCustomizeWords.PushOneWord` | 14 | 2 按钮：记住了！/ 暂时跳过.. |
| 选择题 | `PushWords.PushOneQuestion` | 1114 | 标题「选择题」+ 题目 + 4 选项 |
| 选择题 | `PushWords.PushOneTransQuestion` | 1172 | 标题「翻译」+ 中文 + 3 选项 |
| 选择题 | `PushJpWords.PushOneTransQuestion(JpWord,…)` | 63 | 标题「翻译」+ 中文 + 3 选项 |
| 选择题 | `PushGoinWords.PushOneGoinWordQuestion_1/2/3` | 296 / 371 / 446 | 标题「选择平假名/片假名」+ 3 选项 |
| 提示消息 | `PushWords.PushMessage` | 870 | 纯文本，无交互 |
| 提示消息 | 答错提示 | `PushWords.cs` 857 / 1043 / 1079，`PushJpWords.cs` 251 / 304，`PushGoinWords.cs` 188 / 264 | 纯文本，无交互 |
| 设置 | `PushWords.SetWordNumber` | 332 | 下拉框 + 确定 |
| 设置 | `PushWords.SetEngType` | 365 | 下拉框 + 确定 |

配套的等待机制（`ProcessToastNotificationRecitation` / `...RecitationSM2` / `...Question` /
`...SetNumber` / `ProcessToastNotificationOrderGoin` / `ProcessToastNotificationGoinQuestion`）
都是「订阅 `ToastNotificationManagerCompat.OnActivated` + `HotKeytObservable`，用
`TaskCompletionSource` 收口」。

**卡片内容有 4 种形态，数据结构完全不同**——这是窗口 API 必须接收现成文本、而不是接收
`Word` 对象的原因：

| 卡片 | 第一行 | 后续行 |
|---|---|---|
| 英语单词 | `headWord  (音标)` | `pos. 释义`、例句、例句译文、（SM2 才有）状态行 |
| 日语单词 | `headWord  (ひらがな)  重音：N` | `tranCN`、`pos` |
| 五十音 | `平假名：あ 片假名：ア` | `罗马音：a` |
| 自定义词库 | `firstLine` | `secondLine`、`thirdLine`、`fourthLine` |

**重要发现 1**：所谓「填空题」（`PushWaitFillQuestion`）实际调用的是 `PushOneQuestion`，
也就是**同样是 4 选 1**，只是选项文本不同。因此**不需要实现文本输入框**。

**重要发现 2**：`PushMessage(string Message, string Buttom = "")` 的 `Buttom` 参数在全库
**0 处**被传值（所有调用点都只传一个参数），其按钮分支是死代码。改造时该参数一并移除。

## 需求决策（已与用户确认）

| 决策点 | 选择 |
|---|---|
| 改动范围 | **全部 Toast 都换**，应用不再依赖通知平台 |
| 字体控制 | **做成可配置项**（托盘菜单 → 设置窗口） |
| 窗口位置 | **右下角** |
| 多显示器 | 出现在**鼠标所在屏幕**的右下角 |
| 卡片排版 | **紧凑单栏**（单词+音标 → 词性释义 → 例句 → 状态行 → 按钮行） |
| 主题 | **跟随系统**（可配置，默认跟随系统） |
| 窗口行为 | **不抢焦点** + **一直显示到用户点按钮** |
| 设置界面 | **合并成一个设置窗口**，三个分区 |
| 托盘菜单 | 「参数设置」子菜单下的**「单词个数」「英标类型」两项替换为一项「设置…」**，打开合并后的设置窗口；「自动播放」「自动日志」「重置进度」三项不动 |
| 窗口层架构 | **基类 + 子类** |

## 架构

### 文件结构

**新增：**

```
View/Notify/
  NotifyWindowBase.cs   通用外壳（基类）
  WordCardWindow.cs     单词卡片   ← 继承
  ChoiceWindow.cs       选择题     ← 继承
  MessageWindow.cs      提示消息   ← 继承
View/SettingsWindow.xaml(.cs)  设置窗口（独立，不继承——普通居中对话框，需要抢焦点）
Model/Notify/NotifyTheme.cs    字体/主题配置的读取与应用
```

四个 Notify 窗口**不用 XAML**：内容完全由代码根据传入的文本动态生成——基类用代码搭出
「圆角边框 + `StackPanel`」外壳，子类往 `Root` 里加内容。这样避开 WPF 的 XAML 继承
（子类 XAML 的根元素必须指向基类类型，会丢掉基类的视觉树）带来的额外复杂度。
设置窗口是常规对话框，仍用 XAML。

`ToastFish.csproj` 是旧式工程（显式 `<Compile Include>` / `<Page Include>`），新增的
`.cs` / `.xaml` 必须逐个登记进去，否则不会被编译。

**改动（移除全部 Toast 调用与 `using Microsoft.Toolkit.Uwp.Notifications;`）：**

| 文件 | 改动 |
|---|---|
| `Model/PushControl/PushWords.cs` | 13 处 `ToastContentBuilder` → 调用窗口；4 处 `OnActivated` 订阅 → 删除；8 处 `History.Clear()` → 删除 |
| `Model/PushControl/PushJpWords.cs` | 6 处 `ToastContentBuilder` → 调用窗口；5 处 `History.Clear()` → 删除 |
| `Model/PushControl/PushGoinWords.cs` | 12 处 `ToastContentBuilder` → 调用窗口；2 处 `OnActivated` 订阅 → 删除；5 处 `History.Clear()` → 删除 |
| `Model/PushControl/PushCustomizeWords.cs` | 1 处 `ToastContentBuilder` → 调用窗口 |
| `View/ToastFish.xaml.cs` | `ExitApp_Click` 里的 `History.Clear()` → 删除；移除 `using` |
| `ToastFish.csproj` | 确认全库无引用后，移除 `Microsoft.Toolkit.Uwp.Notifications` 的 `PackageReference`（第 223 行） |

### 为什么是「基类 + 子类」

四种窗口共用大量行为，且这些行为体量不小：

- 定位（多显示器 + 工作区计算）
- 不抢焦点（需要 Win32 `WS_EX_NOACTIVATE` 互操作）
- 置顶 / 不进任务栏 / 无边框 / 透明
- 主题与字体（读配置 + 按比例分配字号）
- 按钮行生成与结果回传（Tcs）
- 显示 / 关闭 / 等待

同时四种内容差异很大。塞进一个类（单窗口方案）会让该类迅速失控；复制到四个类（独立窗口方案）
会产生大量重复。因此把共用行为收敛到基类，内容各自独立。

### 组件职责

**`NotifyWindowBase`**（只做共用的事，不碰内容）

- **定位**：取鼠标所在屏幕的 `WorkingArea`，右下角内缩 16px
- **不抢焦点**：`ShowActivated=false` + `SetWindowLong(GWL_EXSTYLE, WS_EX_NOACTIVATE)`
- **窗口样式**：`Topmost=true`、`ShowInTaskbar=false`、`WindowStyle=None`、`AllowsTransparency=true`、
  `ResizeMode=NoResize`
- **主题 + 字体**：调用 `NotifyTheme.Apply(this)` 套用
- **按钮行**：`SetButtons(params (string 文本, int 结果值)[] buttons)`，生成按钮并绑定点击 →
  `_tcs.TrySetResult(结果值)`
- **等待**：`Task<int> WaitAsync()` 返回 `_tcs.Task`
- **关闭**：`CloseWindow()`；`Tcs` 未完成时先 `TrySetResult(默认值)` 再关，避免等待方永久挂起

**`WordCardWindow`**：`Show(string word, string phonetic, string[] bodyLines, string statusLine, (string,int)[] buttons)`
按紧凑单栏排版：第一行 `word`（大字）+ `phonetic`（小字，可空）同行 → `bodyLines` 逐行（正文）
→ `statusLine`（小字灰色，可空）→ 按钮行。

接收**现成文本**而不是 `Word` 对象，因为 4 种卡片的数据结构完全不同：

| 卡片 | `word` | `phonetic` | `bodyLines` | `statusLine` |
|---|---|---|---|---|
| 英语（`PushOneWord` / `PushOneWordSM2`） | `headWord` | 音标 | `pos. 释义`、例句、例句译文 | 仅 SM2 有 |
| 日语（`PushJpWords.PushOneWord`） | `headWord` | `ひらがな` + 重音 | `tranCN`、`pos` | 无 |
| 五十音（`PushGoinWord`） | `平假名：X 片假名：Y` | 无 | `罗马音：Z` | 无 |
| 自定义词库（`PushCustomizeWords.PushOneWord`） | `firstLine` | 无 | `secondLine`、`thirdLine`、`fourthLine` | 无 |

按钮数 2–4 由 `SetButtons` 决定。

**`ChoiceWindow`**：`Show(string title, string question, (string,int)[] options)`
标题 + 题目 + 3~4 个选项按钮。三种题目共用：

| 题目 | `title` | `question` | 选项数 |
|---|---|---|---|
| 选择题（`PushOneQuestion`） | `选择题` | 题目 | 4 |
| 翻译（`PushOneTransQuestion`） | `翻译` | 中文释义 | 3 |
| 平假名 / 片假名（`PushOneGoinWordQuestion_1/2/3`） | `选择平假名` / `选择片假名` | 罗马音或平假名 | 3 |

**`MessageWindow`**：`Show(string text, int autoCloseMs = 4000)`
纯文本，无按钮，定时器到点自动关闭。覆盖 `PushMessage` 与 7 处答错提示。

**`SettingsWindow`**：三个分区
- 单词数量（5/10/15/20）
- 发音类型（美国/英国）
- 字体与主题（字体家族下拉、基准字号、主题三选一）

底部「确定 / 取消」。确定时写回 `Global` 表（沿用 `Select.UpdateNumber` /
`UpdateGlobalConfig`），并且：**若有卡片窗口正在显示，立即重新套用字体与主题**
（再调一次 `NotifyTheme.Apply`），不必等下一张卡。

它是普通模态对话框，用 `ShowDialog()`，在托盘点击的 UI 线程上直接调用，
**不需要**像原来那样起后台线程（原 `SetNumber_Click` / `SetEngType_Click` 的
`new Thread(...)` 一并去掉）。

托盘菜单入口：把「参数设置」子菜单下的**「单词个数」「英标类型」两项替换为一项「设置…」**，
点击即 `new SettingsWindow().ShowDialog()`；同级的「自动播放」「自动日志」「重置进度」不动。

**`NotifyTheme`**：静态类
- `Load()`：从 `Select` 读 `fontFamily` / `fontSize` / `theme`
- `Apply(Window)`：设置 `FontFamily`，按固定比例把基准字号分配给单词/音标/释义/例句/状态/按钮
- `IsDark`：按 `theme` 判定（`theme==0` 时读系统「应用模式」注册表）
- 暴露浅色/深色两套画刷供子类绑定

### 数据流（线程模型）

**关键约束：WPF 窗口必须在 UI 线程创建，而背诵逻辑跑在后台线程**
（`View/ToastFish.xaml.cs` 的 `Begin_Click` 里 `thread = new Thread(new
ParameterizedThreadStart(PushWords.RecitationSM2))`）。

```
后台线程 RecitationSM2
  ├─ Application.Current.Dispatcher.Invoke(() => win.Show(...))   切到 UI 线程建窗并显示
  ├─ var answer = win.WaitAsync().Result                          后台线程等结果，不阻塞 UI
  │        ↑ UI 线程：用户点按钮 → Tcs.TrySetResult(n)
  └─ 拿到 answer，继续下一张卡
```

6 个等待方法中，前 5 个改为直接返回窗口的 `Task<int>`，设置相关的一处删除。
**全部 `ToastNotificationManagerCompat.OnActivated` 订阅一并删除**：

| 原方法 | 定义位置 | 改为 |
|---|---|---|
| `ProcessToastNotificationRecitation()` | `PushWords.cs` | 返回 `WordCardWindow.WaitAsync()` |
| `ProcessToastNotificationRecitationSM2()` | `PushWords.cs` | 返回 `WordCardWindow.WaitAsync()` |
| `ProcessToastNotificationQuestion()` | `PushWords.cs` | 返回 `ChoiceWindow.WaitAsync()` |
| `ProcessToastNotificationOrderGoin()` | `PushGoinWords.cs` | 返回 `WordCardWindow.WaitAsync()` |
| `ProcessToastNotificationGoinQuestion()` | `PushGoinWords.cs` | 返回 `ChoiceWindow.WaitAsync()` |
| `ProcessToastNotificationSetNumber()` | `PushWords.cs` | **删除**（连同 `SetWordNumber` / `SetEngType`），改由设置窗口承担 |

`PushMessage` 改为 `MessageWindow.Show(text)`（不需要等待，不返回 Task）。

**全局快捷键保持不变**：`Alt+1/2/3/4`、`` Alt+` ``、`Alt+Q` 仍经
`MainWindow.OnHotKeyHandler` → `PushWords.HotKeytObservable.raiseEvent(...)` 回传，
窗口的等待逻辑照旧订阅 `HotKeytObservable`。

### 配置

沿用 `Model/SqliteControl/Select.cs:130-136` 已有的 ALTER TABLE 模式，给 `Global` 表加三列：

| 列 | 类型 | 默认值 | 说明 |
|---|---|---|---|
| `fontFamily` | TEXT | `Microsoft YaHei UI` | 字体家族 |
| `fontSize` | INTEGER | `15` | 基准字号，范围 12–28 |
| `theme` | INTEGER | `0` | 0=跟随系统 1=浅色 2=深色 |

`LoadGlobalConfig` 读取，`UpdateGlobalConfig` 写回。

### 错误处理

- **窗口显示失败 → 捕获异常、写 `Debug.WriteLine`、按「暂时跳过」处理**，保证背诵线程绝不卡死。
  这是本次事故的直接教训：显示环节的任何阻塞/异常都会拖垮整个背诵流程，而进程不会崩、
  也没有任何提示。宁可跳过一张卡，也不能让线程挂死。
- 设置窗口输入非法（字号不是数字/越界）→ 保留原值，不报错、不弹提示。
- 提示消息窗口的定时器异常 → 忽略，不影响主流程。
- 用户直接用窗口的关闭按钮关掉卡片窗口 → 视同「暂时跳过」。

### 测试

项目没有测试框架，GUI 也无法自动化，采用**手动验证清单**：

1. 点开始 → 右下角出现卡片；**在记事本里打字不被打断**（不抢焦点）
2. 4 个按钮各点一次 → 状态正确推进
3. `Alt+1/2/3/4` 快捷键 → 与点按钮等效
4. `` Alt+` `` → 发音
5. 背完 → 选择题 / 填空题窗口 → 答对答错流程都正确
6. **四种卡片都试一遍**：英语（SM2 与非 SM2）、日语单词、五十音、自定义词库导入
7. **三种题目都试一遍**：选择题（4 选 1）、翻译（3 选 1）、平假名 / 片假名（3 选 1）
8. **四种入口都试一遍**：正常背诵、随机英语单词测试、随机日语单词测试、随机五十音测试
9. 答错时 → 提示消息窗口显示正确答案，随后流程继续（不卡死）
10. 设置窗口改字号 → 卡片**立刻**变大
11. 主题切浅色 / 深色 / 跟随系统 → 卡片跟着变
12. 提示消息窗口 4 秒自动消失
13. 多显示器：在副屏用鼠标时，卡片出现在副屏右下角
14. 托盘菜单（含新的「设置…」）、开机自启等原有功能不受影响
15. 托盘 → 退出，不报错（原来的 `History.Clear()` 已移除）

## 不在范围内

- 不改动背诵算法（SM2）、词库、日志导出、发音下载
- 不改动托盘菜单除「设置」入口以外的既有逻辑（自动播放 / 自动日志 / 重置进度 / 词库选择等保持原样）
- 不实现文本输入类题目（经核实填空题也是 4 选 1）
- 不保留 Toast 作为回退路径（用户明确要求全部替换）

## 遗留观察点（本次不处理）

- `Select.UpdateCount()`（`Model/SqliteControl/Select.cs:94`）里的
  `$"select * from Count where bookName = {TABLE_NAME}"` 没给 `TABLE_NAME` 加引号。
  已用 sqlite3 实测会抛 `no such column: CET4_1`（同文件第 89 / 107 行的写法是正确的）。
  调用点有 3 处且都不在 try/catch 内：`PushWords.cs:779`、`PushJpWords.cs:213`、
  `PushGoinWords.cs:128`。
- `.gitignore` 中的 `.github/` 会让以后新增的 workflow 文件被静默忽略。
