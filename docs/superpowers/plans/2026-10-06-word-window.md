# 用自绘窗口替换 Windows Toast 通知 — 实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 把 ToastFish 里全部 32 处 Windows Toast 通知换成应用自绘的 WPF 窗口，并把卡片字体（家族 + 字号）做成可配置项，从而彻底移除对 Windows 通知平台的依赖。

**架构：** 新增一个窗口基类 `NotifyWindowBase`（负责定位、不抢焦点、置顶、主题、按钮行、`TaskCompletionSource` 结果回传）与三个子类（`WordCardWindow` / `ChoiceWindow` / `MessageWindow`）。背诵逻辑跑在后台线程，窗口在 UI 线程创建，两者用 `Task<int>` 对接。原 `ProcessToastNotification*` 等待方法保留 `HotKeytObservable` 订阅、把结果转发给窗口，只把 `OnActivated` 订阅换成窗口的 `WaitAsync()`。

**技术栈：** C# 7.3 / .NET Framework 4.7.2 / WPF / Win32 互操作（`WS_EX_NOACTIVATE`）/ SQLite（`System.Data.SQLite` + Dapper）

**规格：** `docs/superpowers/specs/2026-10-06-word-window-design.md`

---

## 关于本计划的几点说明（执行前必读）

1. **项目没有测试框架，也没有测试工程。** 规格已确认采用「构建验证 + 手动验证清单」。本计划每个任务的验证 = `msbuild` 编译通过 + 指定的手动观察点。**不要**为了 TDD 而新建测试工程——那超出了本次范围。
2. **中途状态说明。** 任务 8 完成后英语流程即可用；但 `ProcessToastNotificationRecitation` / `...Question` 被 `PushJpWords`、`PushCustomizeWords` 复用，所以在任务 9–11 全部完成前，日语 / 五十音 / 自定义词库三条流程会处于「卡片是 Toast、等待是窗口」的不一致状态而失效。**任务 12 结束后整体才完全可用。** 每个任务后只要求「编译通过」，不要求全流程可用。
3. **不使用独立 worktree / 分支。** 本仓库历史提交全部直接落在 `main`，沿用该习惯。
4. **构建命令**（Git Bash 下路径含空格，必须整体加引号）：
   ```bash
   "/c/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Current/Bin/MSBuild.exe" \
     ToastFish.csproj -p:Configuration=Debug -nologo -v:m
   ```
   预期结尾：`0 个错误`（`0 Error(s)`）。
5. **不要**在任务 1–11 期间移除 `Microsoft.Toolkit.Uwp.Notifications` 的 `PackageReference`——移除会导致其它文件编译失败。移除动作放在任务 12。
6. **改代码时不要顺手格式化、不要动无关行。** 每个任务的「修改」步骤只替换计划中列出的代码块。

---

## 文件结构

### 新建

| 文件 | 职责 |
|---|---|
| `Model/Notify/NotifyTheme.cs` | 字体家族 / 基准字号 / 明暗主题的解析与调色板。静态类，无状态持久化。 |
| `View/Notify/NotifyWindowBase.cs` | 窗口外壳基类：定位（鼠标所在屏幕右下角）、`WS_EX_NOACTIVATE`、置顶 / 无边框 / 不进任务栏、按钮行、`WaitAsync` / `SetResult` / `Current`。 |
| `View/Notify/WordCardWindow.cs` | 单词卡片：`word + phonetic` → `bodyLines[]` → `statusLine` → 按钮行。服务英语 / 日语 / 五十音 / 自定义词库四种卡片。 |
| `View/Notify/ChoiceWindow.cs` | 选择题：`title + question` → 3~4 个选项按钮。服务选择题 / 翻译 / 平假名片假名三种题目。 |
| `View/Notify/MessageWindow.cs` | 提示消息：纯文本，无按钮，定时自动关闭。 |
| `View/SettingsWindow.xaml` + `.xaml.cs` | 设置对话框（XAML），三个分区：单词数量 / 发音类型 / 字体与主题。 |

### 修改

| 文件 | 改动 |
|---|---|
| `Model/SqliteControl/Select.cs` | `Global` 表加 3 列；`Global` 类加 3 个属性；`LoadGlobalConfig` / `UpdateGlobalConfig` 读写新列；加 3 个静态字段。 |
| `Model/PushControl/PushWords.cs` | 13 处 `ToastContentBuilder` → 窗口；4 处 `OnActivated` → 删除；8 处 `History.Clear()` → 删除；`PushMessage` 去掉 `Buttom` 参数。 |
| `Model/PushControl/PushJpWords.cs` | 6 处 `ToastContentBuilder` → 窗口；5 处 `History.Clear()` → 删除。 |
| `Model/PushControl/PushGoinWords.cs` | 12 处 `ToastContentBuilder` → 窗口；2 处 `OnActivated` → 删除；5 处 `History.Clear()` → 删除。 |
| `Model/PushControl/PushCustomizeWords.cs` | 1 处 `ToastContentBuilder` → 窗口。 |
| `View/ToastFish.xaml.cs` | `ExitApp_Click` 的 `History.Clear()` → 删除；移除 `using`；托盘菜单加「设置…」子项；构造函数加 `NotifyTheme.Load()`。 |
| `ToastFish.csproj` | 登记 6 个新文件；移除 toolkit 包引用。 |

---

## 任务 1：配置地基 —— `Global` 表加三列

**文件：**
- 修改：`Model/SqliteControl/Select.cs:19-22`（静态字段）、`:534-541`（`Global` 类）、`:133-137`（ALTER）、`:144`（读取）、`:150-154`（写回）

- [ ] **步骤 1：加静态字段**

在 `Model/SqliteControl/Select.cs` 第 22 行 `public static int AUTO_LOG  = 1;` 之后插入：

```csharp
        public static string FONT_FAMILY = "Microsoft YaHei UI";  // 卡片字体家族
        public static int FONT_SIZE = 15;  // 卡片基准字号，范围 12-28
        public static int THEME = 0;  // 0=跟随系统 1=浅色 2=深色
```

- [ ] **步骤 2：给 `Global` 类加属性**

把 `Global` 类（约第 534-541 行）改成：

```csharp
    [Serializable]
    public class Global
    {
        public string currentWordNumber { get; set; }
        public string currentBookName { get; set; }
        public int autoPlay { get; set; }
        public int EngType { get; set; }
        public int autoLog { get; set; }
        public string fontFamily { get; set; }
        public int fontSize { get; set; }
        public int theme { get; set; }
    }
```

- [ ] **步骤 3：`LoadGlobalConfig` 里加建列逻辑**

在 `LoadGlobalConfig` 中 `autoLog` 那段 `if` 块（约第 133-137 行）之后、`Global Temp = new Global();` 之前插入：

```csharp
            if (HeadTileList.Contains("fontFamily") == false)
            {
                Update.CommandText = $"ALTER TABLE Global ADD COLUMN fontFamily TEXT NOT NULL DEFAULT '{FONT_FAMILY}'";
                Update.ExecuteNonQuery();
            }
            if (HeadTileList.Contains("fontSize") == false)
            {
                Update.CommandText = $"ALTER TABLE Global ADD COLUMN fontSize INTEGER NOT NULL DEFAULT {FONT_SIZE}";
                Update.ExecuteNonQuery();
            }
            if (HeadTileList.Contains("theme") == false)
            {
                Update.CommandText = $"ALTER TABLE Global ADD COLUMN theme INTEGER NOT NULL DEFAULT {THEME}";
                Update.ExecuteNonQuery();
            }
```

- [ ] **步骤 4：`LoadGlobalConfig` 里加读取**

在 `AUTO_LOG = GlobalVariable[0].autoLog;`（约第 144 行）之后插入：

```csharp
            FONT_FAMILY = GlobalVariable[0].fontFamily;
            FONT_SIZE = GlobalVariable[0].fontSize;
            THEME = GlobalVariable[0].theme;
```

- [ ] **步骤 5：`UpdateGlobalConfig` 里加写回**

把 `UpdateGlobalConfig`（约第 147-156 行）的 SQL 改成：

```csharp
            Update.CommandText = $"UPDATE Global SET currentWordNumber ='{WORD_NUMBER}'" +
                $", currentBookName = '{TABLE_NAME}'" +
                $", autoPlay = '{AUTO_PLAY}'" +
                $", EngType = '{ENG_TYPE}' " +
                $", autoLog = '{AUTO_LOG}'" +
                $", fontFamily = '{FONT_FAMILY}'" +
                $", fontSize = '{FONT_SIZE}'" +
                $", theme = '{THEME}'";
```

- [ ] **步骤 6：编译验证**

运行：
```bash
"/c/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Current/Bin/MSBuild.exe" ToastFish.csproj -p:Configuration=Debug -nologo -v:m
```
预期：`0 个错误`

- [ ] **步骤 7：运行验证建列与默认值**

运行 Debug 版 exe（`bin/Debug/ToastFish.exe`），等托盘图标出现后，退出程序。然后：

```bash
/c/DevDisk/DevTools/AndroidSDK/platform-tools/sqlite3 bin/Debug/Resources/inami.db "select fontFamily, fontSize, theme from Global;"
```

预期输出一行：`Microsoft YaHei UI|15|0`

- [ ] **步骤 8：Commit**

```bash
git add Model/SqliteControl/Select.cs
git commit -m "feat(config): Global 表新增 fontFamily/fontSize/theme 三列"
```

---

## 任务 2：`NotifyTheme` —— 字体与主题解析

**文件：**
- 创建：`Model/Notify/NotifyTheme.cs`
- 修改：`ToastFish.csproj`（登记 `<Compile Include>`）

- [ ] **步骤 1：创建 `Model/Notify/NotifyTheme.cs`**

```csharp
using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using ToastFish.Model.SqliteControl;

namespace ToastFish.Model.Notify
{
    /// <summary>
    /// 卡片字体与明暗主题的唯一来源。启动时 Load() 一次，设置窗口改完再 Load() 一次。
    /// </summary>
    public static class NotifyTheme
    {
        // 相对基准字号的比例，基准 15 时对应 26 / 14 / 16 / 13 / 11 / 13
        private const double WordRatio = 1.75;
        private const double PhoneticRatio = 0.95;
        private const double MeaningRatio = 1.05;
        private const double SentenceRatio = 0.85;
        private const double StatusRatio = 0.75;
        private const double ButtonRatio = 0.85;

        public static bool IsDark { get; private set; }
        public static FontFamily Font { get; private set; }
        public static double WordSize { get; private set; }
        public static double PhoneticSize { get; private set; }
        public static double MeaningSize { get; private set; }
        public static double SentenceSize { get; private set; }
        public static double StatusSize { get; private set; }
        public static double ButtonSize { get; private set; }

        public static Brush Background { get; private set; }
        public static Brush Foreground { get; private set; }
        public static Brush Muted { get; private set; }
        public static Brush Border { get; private set; }
        public static Brush ButtonBackground { get; private set; }
        public static Brush ButtonBorder { get; private set; }
        public static Brush ButtonForeground { get; private set; }

        public static void Load()
        {
            int baseSize = Select.FONT_SIZE;
            if (baseSize < 12 || baseSize > 28)
                baseSize = 15;

            string family = Select.FONT_FAMILY;
            if (string.IsNullOrWhiteSpace(family))
                family = "Microsoft YaHei UI";

            Font = new FontFamily(family);
            WordSize = Math.Round(baseSize * WordRatio);
            PhoneticSize = Math.Round(baseSize * PhoneticRatio);
            MeaningSize = Math.Round(baseSize * MeaningRatio);
            SentenceSize = Math.Round(baseSize * SentenceRatio);
            StatusSize = Math.Round(baseSize * StatusRatio);
            ButtonSize = Math.Round(baseSize * ButtonRatio);

            IsDark = Select.THEME == 2 || (Select.THEME == 0 && SystemUsesDarkApps());

            if (IsDark)
            {
                Background = Brush("#23262B");
                Foreground = Brush("#F2F4F7");
                Muted = Brush("#757D88");
                Border = Brush("#34383F");
                ButtonBackground = Brush("#33383F");
                ButtonBorder = Brush("#434A53");
                ButtonForeground = Brush("#EAEEF3");
            }
            else
            {
                Background = Brush("#F7F7F8");
                Foreground = Brush("#14161A");
                Muted = Brush("#8B9199");
                Border = Brush("#E2E5EA");
                ButtonBackground = Brush("#E8EAEE");
                ButtonBorder = Brush("#D6D9DE");
                ButtonForeground = Brush("#22262C");
            }
        }

        public static void Apply(Window window)
        {
            window.FontFamily = Font;
        }

        private static Brush Brush(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        private static bool SystemUsesDarkApps()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key == null)
                        return false;
                    object value = key.GetValue("AppsUseLightTheme");
                    if (value == null)
                        return false;
                    return Convert.ToInt32(value) == 0;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
```

- [ ] **步骤 2：在 `ToastFish.csproj` 登记文件**

在 `<Compile Include="Model\StartWithWindows\StartWithWindows.cs" />`（第 153 行）之后插入：

```xml
    <Compile Include="Model\Notify\NotifyTheme.cs" />
```

- [ ] **步骤 3：编译验证**

运行任务 1 步骤 6 的 msbuild 命令。
预期：`0 个错误`

- [ ] **步骤 4：Commit**

```bash
git add Model/Notify/NotifyTheme.cs ToastFish.csproj
git commit -m "feat(notify): 新增 NotifyTheme，解析字体家族/基准字号/明暗主题"
```

---

## 任务 3：`NotifyWindowBase` —— 窗口外壳

**文件：**
- 创建：`View/Notify/NotifyWindowBase.cs`
- 修改：`ToastFish.csproj`

- [ ] **步骤 1：创建 `View/Notify/NotifyWindowBase.cs`**

```csharp
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ToastFish.Model.Notify;

namespace ToastFish.View.Notify
{
    /// <summary>
    /// 所有自绘通知窗口的外壳：定位、不抢焦点、置顶、主题、按钮行、结果回传。
    /// 子类只负责往 Root 里塞内容。
    /// </summary>
    public class NotifyWindowBase : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        /// <summary>当前正在显示的通知窗口。同一时刻只可能有一个。</summary>
        public static NotifyWindowBase Current { get; private set; }

        /// <summary>true 表示这个窗口会被后来的窗口顶掉（提示消息用）。</summary>
        protected bool AutoClose;

        /// <summary>窗口被异常关闭时回传给等待方的值。</summary>
        protected int DefaultResult = 1;

        protected readonly StackPanel Root = new StackPanel();
        private readonly Border _shell = new Border();
        private readonly TaskCompletionSource<int> _tcs = new TaskCompletionSource<int>();
        private DispatcherTimer _timer;

        public NotifyWindowBase()
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            AllowsTransparency = true;
            ShowActivated = false;
            SizeToContent = SizeToContent.WidthAndHeight;

            _shell.Padding = new Thickness(14, 12, 14, 12);
            _shell.CornerRadius = new CornerRadius(10);
            _shell.BorderThickness = new Thickness(1);
            _shell.Child = Root;
            Content = _shell;
        }

        /// <summary>后台线程等结果用。</summary>
        public Task<int> WaitAsync()
        {
            return _tcs.Task;
        }

        /// <summary>供快捷键回调用；与点按钮等效。</summary>
        public void SetResult(int value)
        {
            _tcs.TrySetResult(value);
        }

        /// <summary>把要在 UI 线程执行的建窗动作切过去。</summary>
        public static void OnUi(Action action)
        {
            Application app = Application.Current;
            if (app == null)
                return;
            if (app.Dispatcher.CheckAccess())
                action();
            else
                app.Dispatcher.Invoke(action);
        }

        /// <summary>套用主题、顶掉上一个窗口、显示并定位。</summary>
        protected void ShowAsCurrent()
        {
            NotifyTheme.Apply(this);
            _shell.Background = NotifyTheme.Background;
            _shell.BorderBrush = NotifyTheme.Border;

            if (AutoClose && Current != null && Current != this)
                Current.Close();

            if (Current != null && Current != this && Current.AutoClose)
                Current.Close();

            Current = this;
            Show();
        }

        /// <summary>autoCloseMs &gt; 0 时定时自动关闭。</summary>
        protected void StartAutoClose(int autoCloseMs)
        {
            if (autoCloseMs <= 0)
                return;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(autoCloseMs) };
            _timer.Tick += (s, e) => { _timer.Stop(); Close(); };
            _timer.Start();
        }

        /// <summary>生成按钮行，点击即回传结果并关闭。</summary>
        protected void SetButtons(params (string Text, int Result)[] buttons)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 12, 0, 0)
            };
            foreach ((string text, int result) in buttons)
            {
                var button = new Button
                {
                    Content = text,
                    FontSize = NotifyTheme.ButtonSize,
                    FontFamily = NotifyTheme.Font,
                    Padding = new Thickness(10, 6, 10, 6),
                    Margin = new Thickness(0, 0, 6, 0),
                    Background = NotifyTheme.ButtonBackground,
                    BorderBrush = NotifyTheme.ButtonBorder,
                    Foreground = NotifyTheme.ButtonForeground
                };
                int value = result;
                button.Click += (s, e) =>
                {
                    SetResult(value);
                    Close();
                };
                row.Children.Add(button);
            }
            Root.Children.Add(row);
        }

        protected TextBlock AddLine(string text, double fontSize, Brush foreground, double topMargin = 0)
        {
            var block = new TextBlock
            {
                Text = text,
                FontSize = fontSize,
                FontFamily = NotifyTheme.Font,
                Foreground = foreground,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 348,
                Margin = new Thickness(0, topMargin, 0, 0)
            };
            Root.Children.Add(block);
            return block;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            IntPtr handle = new WindowInteropHelper(this).Handle;
            int style = IntPtr.Size == 8
                ? GetWindowLongPtr64(handle, GWL_EXSTYLE).ToInt32()
                : GetWindowLong32(handle, GWL_EXSTYLE);
            SetWindowLongPtr(handle, GWL_EXSTYLE, new IntPtr(style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            MoveToCursorScreenCorner();
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_timer != null)
                _timer.Stop();
            _tcs.TrySetResult(DefaultResult);
            if (Current == this)
                Current = null;
            base.OnClosed(e);
        }

        private void MoveToCursorScreenCorner()
        {
            System.Drawing.Point cursor = System.Windows.Forms.Cursor.Position;
            System.Drawing.Rectangle area = System.Windows.Forms.Screen.FromPoint(cursor).WorkingArea;

            double scaleX = 1;
            double scaleY = 1;
            PresentationSource source = PresentationSource.FromVisual(this);
            if (source != null && source.CompositionTarget != null)
            {
                scaleX = source.CompositionTarget.TransformToDevice.M11;
                scaleY = source.CompositionTarget.TransformToDevice.M22;
            }

            Left = area.Right / scaleX - ActualWidth - 16;
            Top = area.Bottom / scaleY - ActualHeight - 16;
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            if (IntPtr.Size == 8)
                return SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
            return new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
        }
    }
}
```

- [ ] **步骤 2：在 `ToastFish.csproj` 登记文件**

在 `<Compile Include="Model\Notify\NotifyTheme.cs" />` 之后插入：

```xml
    <Compile Include="View\Notify\NotifyWindowBase.cs" />
```

- [ ] **步骤 3：编译验证**

运行任务 1 步骤 6 的 msbuild 命令。
预期：`0 个错误`

- [ ] **步骤 4：Commit**

```bash
git add View/Notify/NotifyWindowBase.cs ToastFish.csproj
git commit -m "feat(notify): 新增 NotifyWindowBase 窗口外壳（定位/不抢焦点/按钮行/Tcs）"
```

---

## 任务 4：`MessageWindow` —— 提示消息

**文件：**
- 创建：`View/Notify/MessageWindow.cs`
- 修改：`ToastFish.csproj`

- [ ] **步骤 1：创建 `View/Notify/MessageWindow.cs`**

```csharp
using ToastFish.Model.Notify;

namespace ToastFish.View.Notify
{
    /// <summary>
    /// 纯文本提示，无按钮，到点自动消失。用于 PushMessage 与答错提示。
    /// </summary>
    public class MessageWindow : NotifyWindowBase
    {
        private MessageWindow()
        {
            AutoClose = true;
        }

        public static void ShowMessage(string text, int autoCloseMs = 4000)
        {
            OnUi(() =>
            {
                var window = new MessageWindow();
                window.AddLine(text, NotifyTheme.MeaningSize, NotifyTheme.Foreground);
                window.ShowAsCurrent();
                window.StartAutoClose(autoCloseMs);
            });
        }
    }
}
```

- [ ] **步骤 2：在 `ToastFish.csproj` 登记文件**

在 `<Compile Include="View\Notify\NotifyWindowBase.cs" />` 之后插入：

```xml
    <Compile Include="View\Notify\MessageWindow.cs" />
```

- [ ] **步骤 3：编译验证**

运行任务 1 步骤 6 的 msbuild 命令。
预期：`0 个错误`

- [ ] **步骤 4：Commit**

```bash
git add View/Notify/MessageWindow.cs ToastFish.csproj
git commit -m "feat(notify): 新增 MessageWindow 提示消息窗口"
```

---

## 任务 5：`WordCardWindow` —— 单词卡片

**文件：**
- 创建：`View/Notify/WordCardWindow.cs`
- 修改：`ToastFish.csproj`

- [ ] **步骤 1：创建 `View/Notify/WordCardWindow.cs`**

```csharp
using ToastFish.Model.Notify;

namespace ToastFish.View.Notify
{
    /// <summary>
    /// 单词卡片。接收现成文本而非 Word 对象，因为英语 / 日语 / 五十音 / 自定义词库
    /// 四种卡片的数据结构完全不同。
    /// </summary>
    public class WordCardWindow : NotifyWindowBase
    {
        private WordCardWindow()
        {
        }

        public static void ShowCard(
            string word,
            string phonetic,
            string[] bodyLines,
            string statusLine,
            params (string Text, int Result)[] buttons)
        {
            OnUi(() =>
            {
                var window = new WordCardWindow();
                window.Build(word, phonetic, bodyLines, statusLine, buttons);
                window.ShowAsCurrent();
            });
        }

        private void Build(
            string word,
            string phonetic,
            string[] bodyLines,
            string statusLine,
            (string Text, int Result)[] buttons)
        {
            if (!string.IsNullOrEmpty(phonetic))
            {
                var head = AddLine(word, NotifyTheme.WordSize, NotifyTheme.Foreground);
                head.Inlines.Add(new System.Windows.Documents.Run("  "));
                head.Inlines.Add(new System.Windows.Documents.Run(phonetic)
                {
                    FontSize = NotifyTheme.PhoneticSize,
                    Foreground = NotifyTheme.Muted
                });
            }
            else
            {
                AddLine(word, NotifyTheme.WordSize, NotifyTheme.Foreground);
            }

            if (bodyLines != null)
            {
                foreach (string line in bodyLines)
                {
                    if (string.IsNullOrEmpty(line))
                        continue;
                    AddLine(line, NotifyTheme.SentenceSize, NotifyTheme.Foreground, 4);
                }
            }

            if (!string.IsNullOrEmpty(statusLine))
                AddLine(statusLine, NotifyTheme.StatusSize, NotifyTheme.Muted, 8);

            SetButtons(buttons);
        }
    }
}
```

- [ ] **步骤 2：在 `ToastFish.csproj` 登记文件**

在 `<Compile Include="View\Notify\MessageWindow.cs" />` 之后插入：

```xml
    <Compile Include="View\Notify\WordCardWindow.cs" />
```

- [ ] **步骤 3：编译验证**

运行任务 1 步骤 6 的 msbuild 命令。
预期：`0 个错误`

- [ ] **步骤 4：Commit**

```bash
git add View/Notify/WordCardWindow.cs ToastFish.csproj
git commit -m "feat(notify): 新增 WordCardWindow 单词卡片窗口"
```

---

## 任务 6：`ChoiceWindow` —— 选择题

**文件：**
- 创建：`View/Notify/ChoiceWindow.cs`
- 修改：`ToastFish.csproj`

- [ ] **步骤 1：创建 `View/Notify/ChoiceWindow.cs`**

```csharp
using ToastFish.Model.Notify;

namespace ToastFish.View.Notify
{
    /// <summary>
    /// 选择题。服务三种题目：选择题（4 选 1）、翻译（3 选 1）、平假名/片假名（3 选 1）。
    /// </summary>
    public class ChoiceWindow : NotifyWindowBase
    {
        private ChoiceWindow()
        {
            // 异常关闭时按「答错」处理，与原 OnActivated 解析失败时的 -1 语义一致
            DefaultResult = -1;
        }

        public static void ShowChoice(
            string title,
            string question,
            params (string Text, int Result)[] options)
        {
            OnUi(() =>
            {
                var window = new ChoiceWindow();
                window.Build(title, question, options);
                window.ShowAsCurrent();
            });
        }

        private void Build(string title, string question, (string Text, int Result)[] options)
        {
            AddLine(title, NotifyTheme.StatusSize, NotifyTheme.Muted);
            AddLine(question, NotifyTheme.MeaningSize, NotifyTheme.Foreground, 4);
            SetButtons(options);
        }
    }
}
```

- [ ] **步骤 2：在 `ToastFish.csproj` 登记文件**

在 `<Compile Include="View\Notify\WordCardWindow.cs" />` 之后插入：

```xml
    <Compile Include="View\Notify\ChoiceWindow.cs" />
```

- [ ] **步骤 3：编译验证**

运行任务 1 步骤 6 的 msbuild 命令。
预期：`0 个错误`

- [ ] **步骤 4：Commit**

```bash
git add View/Notify/ChoiceWindow.cs ToastFish.csproj
git commit -m "feat(notify): 新增 ChoiceWindow 选择题窗口"
```

---

## 任务 7：`SettingsWindow` + 托盘菜单入口

**文件：**
- 创建：`View/SettingsWindow.xaml`、`View/SettingsWindow.xaml.cs`
- 修改：`ToastFish.csproj`、`View/ToastFish.xaml.cs:157-173`（托盘菜单）、`:278-308`（子项挂载）、`:397-407`（两个点击处理）

- [ ] **步骤 1：创建 `View/SettingsWindow.xaml`**

```xml
<Window x:Class="ToastFish.View.SettingsWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="ToastFish 设置" Height="360" Width="380"
        WindowStartupLocation="CenterScreen" ResizeMode="NoResize">
    <Grid Margin="16">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>

        <StackPanel Grid.Row="0" Orientation="Horizontal" Margin="0,0,0,14">
            <TextBlock Text="单词数量" Width="90" VerticalAlignment="Center" />
            <ComboBox x:Name="NumberBox" Width="120">
                <ComboBoxItem Content="5" />
                <ComboBoxItem Content="10" />
                <ComboBoxItem Content="15" />
                <ComboBoxItem Content="20" />
            </ComboBox>
        </StackPanel>

        <StackPanel Grid.Row="1" Orientation="Horizontal" Margin="0,0,0,14">
            <TextBlock Text="发音类型" Width="90" VerticalAlignment="Center" />
            <ComboBox x:Name="EngTypeBox" Width="120">
                <ComboBoxItem Content="美国" />
                <ComboBoxItem Content="英国" />
            </ComboBox>
        </StackPanel>

        <StackPanel Grid.Row="2" Orientation="Horizontal" Margin="0,0,0,14">
            <TextBlock Text="主题" Width="90" VerticalAlignment="Center" />
            <ComboBox x:Name="ThemeBox" Width="120">
                <ComboBoxItem Content="跟随系统" />
                <ComboBoxItem Content="浅色" />
                <ComboBoxItem Content="深色" />
            </ComboBox>
        </StackPanel>

        <StackPanel Grid.Row="3">
            <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
                <TextBlock Text="字体" Width="90" VerticalAlignment="Center" />
                <ComboBox x:Name="FontBox" Width="200" />
            </StackPanel>
            <StackPanel Orientation="Horizontal">
                <TextBlock Text="基准字号" Width="90" VerticalAlignment="Center" />
                <TextBox x:Name="FontSizeBox" Width="60" />
                <TextBlock Text="（12 - 28）" Margin="8,0,0,0" VerticalAlignment="Center"
                           Foreground="Gray" />
            </StackPanel>
        </StackPanel>

        <StackPanel Grid.Row="4" Orientation="Horizontal" HorizontalAlignment="Right"
                    Margin="0,16,0,0">
            <Button Content="确定" Width="80" Margin="0,0,8,0" Click="Ok_Click" />
            <Button Content="取消" Width="80" IsCancel="True" />
        </StackPanel>
    </Grid>
</Window>
```

- [ ] **步骤 2：创建 `View/SettingsWindow.xaml.cs`**

```csharp
using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using ToastFish.Model.Notify;
using ToastFish.Model.SqliteControl;

namespace ToastFish.View
{
    public partial class SettingsWindow : Window
    {
        private static readonly string[] FontCandidates =
        {
            "Microsoft YaHei UI", "Microsoft YaHei", "SimSun", "SimHei",
            "Segoe UI", "Arial", "Consolas"
        };

        public SettingsWindow()
        {
            InitializeComponent();
            LoadCurrent();
        }

        private void LoadCurrent()
        {
            SelectNumber(Select.WORD_NUMBER);
            EngTypeBox.SelectedIndex = Select.ENG_TYPE == 1 ? 0 : 1;
            ThemeBox.SelectedIndex = Select.THEME < 0 || Select.THEME > 2 ? 0 : Select.THEME;

            var fonts = Fonts.SystemFontFamilies
                .Select(f => f.Source)
                .OrderBy(name => name)
                .ToList();
            if (!fonts.Contains(Select.FONT_FAMILY))
                fonts.Insert(0, Select.FONT_FAMILY);
            FontBox.ItemsSource = fonts;
            FontBox.SelectedItem = Select.FONT_FAMILY;

            FontSizeBox.Text = Select.FONT_SIZE.ToString();
        }

        private void SelectNumber(int number)
        {
            string target = number.ToString();
            foreach (object item in NumberBox.Items)
            {
                if (((System.Windows.Controls.ComboBoxItem)item).Content.ToString() == target)
                {
                    NumberBox.SelectedItem = item;
                    return;
                }
            }
            NumberBox.SelectedIndex = 1;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (NumberBox.SelectedItem != null)
                Select.WORD_NUMBER = int.Parse(
                    ((System.Windows.Controls.ComboBoxItem)NumberBox.SelectedItem).Content.ToString());

            Select.ENG_TYPE = EngTypeBox.SelectedIndex == 0 ? 1 : 2;
            Select.THEME = ThemeBox.SelectedIndex < 0 ? 0 : ThemeBox.SelectedIndex;

            if (FontBox.SelectedItem != null)
                Select.FONT_FAMILY = FontBox.SelectedItem.ToString();

            int size;
            if (int.TryParse(FontSizeBox.Text.Trim(), out size) && size >= 12 && size <= 28)
                Select.FONT_SIZE = size;

            new Select().UpdateGlobalConfig();

            NotifyTheme.Load();
            if (NotifyWindowBase.Current != null)
            {
                // 正在显示的卡片立刻跟着变大 / 换配色
                NotifyWindowBase.Current.Close();
            }

            DialogResult = true;
            Close();
        }
    }
}
```

> **说明：** `NotifyWindowBase.Current.Close()` 会让等待中的背诵线程收到 `DefaultResult`（卡片为「暂时跳过」），然后立刻弹出下一张卡——新卡用的是新字号。这样「改完字号立刻生效」不需要额外的重绘机制。

- [ ] **步骤 3：在 `ToastFish.csproj` 登记两个文件**

在 `<Compile Include="View\Notify\ChoiceWindow.cs" />` 之后插入：

```xml
    <Compile Include="View\SettingsWindow.xaml.cs">
      <DependentUpon>SettingsWindow.xaml</DependentUpon>
    </Compile>
```

在 `<Page Include="View\ToastFish.xaml">` 那一整块之后插入：

```xml
    <Page Include="View\SettingsWindow.xaml">
      <SubType>Designer</SubType>
      <Generator>MSBuild:Compile</Generator>
    </Page>
```

- [ ] **步骤 4：编译验证**

运行任务 1 步骤 6 的 msbuild 命令。
预期：`0 个错误`

- [ ] **步骤 5：把托盘菜单的「单词个数」「英标类型」换成「设置…」**

在 `View/ToastFish.xaml.cs` 中：

(a) 删除第 143-144 行的两个字段声明：

```csharp
        System.Windows.Forms.ToolStripMenuItem SetNumber = new System.Windows.Forms.ToolStripMenuItem();
        System.Windows.Forms.ToolStripMenuItem SetEngType = new System.Windows.Forms.ToolStripMenuItem();
```

改为一行：

```csharp
        System.Windows.Forms.ToolStripMenuItem OpenSettings = new System.Windows.Forms.ToolStripMenuItem();
```

(b) 把第 169-173 行：

```csharp
            SetNumber.Text = "单词个数";
            SetNumber.Click += new EventHandler(SetNumber_Click);

            SetEngType.Text = "英标类型";
            SetEngType.Click += new EventHandler(SetEngType_Click);
```

改为：

```csharp
            OpenSettings.Text = "设置…";
            OpenSettings.Click += new EventHandler(OpenSettings_Click);
```

(c) 把第 304-305 行：

```csharp
            ((ToolStripDropDownItem)Cms.Items[5]).DropDownItems.Add(SetNumber);
            ((ToolStripDropDownItem)Cms.Items[5]).DropDownItems.Add(SetEngType);
```

改为：

```csharp
            ((ToolStripDropDownItem)Cms.Items[5]).DropDownItems.Add(OpenSettings);
```

(d) 把第 397-407 行两个点击处理：

```csharp
        private void SetNumber_Click(object sender, EventArgs e)
        {
            Thread thread = new Thread(new ThreadStart(pushWords.SetWordNumber));
            thread.Start();
        }

        private void SetEngType_Click(object sender, EventArgs e)
        {
            Thread thread = new Thread(new ThreadStart(pushWords.SetEngType));
            thread.Start();
        }
```

改为：

```csharp
        private void OpenSettings_Click(object sender, EventArgs e)
        {
            new SettingsWindow().ShowDialog();
        }
```

- [ ] **步骤 6：构造函数里加载主题**

在 `View/ToastFish.xaml.cs` 的构造函数中，`Se.LoadGlobalConfig();` 那一行之后插入：

```csharp
            NotifyTheme.Load();
```

并在文件顶部加 `using ToastFish.Model.Notify;`。

- [ ] **步骤 7：编译验证**

运行任务 1 步骤 6 的 msbuild 命令。
预期：`0 个错误`。若报 `pushWords.SetWordNumber` 相关错误，说明步骤 5(d) 没删干净。

- [ ] **步骤 8：运行验证设置窗口**

启动 `bin/Debug/ToastFish.exe`，托盘右键 → 参数设置 → 设置…。
预期：弹出居中的设置对话框；四个下拉框与字号框都填了当前值；改字号为 20 点确定；再打开一次，字号仍是 20。

- [ ] **步骤 9：Commit**

```bash
git add View/SettingsWindow.xaml View/SettingsWindow.xaml.cs View/ToastFish.xaml.cs ToastFish.csproj
git commit -m "feat(settings): 新增合并设置窗口，托盘菜单改为「设置…」入口"
```

---

## 任务 8：接入英语背诵流程（`PushWords.cs`）

**文件：**
- 修改：`Model/PushControl/PushWords.cs`（多处）

- [ ] **步骤 1：移除 toolkit 的 using**

把第 7 行 `using Microsoft.Toolkit.Uwp.Notifications;` 替换为：

```csharp
using ToastFish.View.Notify;
```

- [ ] **步骤 2：改写 `ProcessToastNotificationRecitation`（约 105-160 行）**

整个方法替换为：

```csharp
        public async Task<int> ProcessToastNotificationRecitation()//CancellationToken cancellationToken
        {
            NotifyWindowBase window = NotifyWindowBase.Current;
            using (HotKeytObservable.Subscribe(events =>
            {
                Debug.WriteLine("HotKeytObservable.Subscribe:" + events);
                if (window == null)
                    return;
                switch (events)
                {
                    case "1": // succeed
                        window.SetResult(0);
                        break;
                    case "2"://fail
                        window.SetResult(1);
                        break;
                    case "3"://voice
                        window.SetResult(2);
                        break;
                    default:
                        break;
                }

            }))
            {
                return await window.WaitAsync();
            }
        }
```

- [ ] **步骤 3：改写 `ProcessToastNotificationRecitationSM2`（约 162-231 行）**

整个方法替换为：

```csharp
        public async Task<int> ProcessToastNotificationRecitationSM2()//CancellationToken cancellationToken
        {
            NotifyWindowBase window = NotifyWindowBase.Current;
            using (HotKeytObservable.Subscribe(events =>
            {
                Debug.WriteLine("HotKeytObservable.Subscribe:" + events);
                if (window == null)
                    return;
                switch (events)
                {
                    case "1": //again
                        window.SetResult(1);
                        break;
                    case "2"://hard
                        window.SetResult(2);
                        break;
                    case "3"://good
                        window.SetResult(3);
                        break;
                    case "4"://easy
                        window.SetResult(4);
                        break;
                    case "S"://voice
                        window.SetResult(0);
                        break;
                    default:
                        break;
                }

            }))
            {
                return await window.WaitAsync();
            }
        }
```

- [ ] **步骤 4：改写 `ProcessToastNotificationQuestion`（约 236-295 行）**

整个方法替换为：

```csharp
        public async Task<int> ProcessToastNotificationQuestion()
        {
            NotifyWindowBase window = NotifyWindowBase.Current;
            using (HotKeytObservable.Subscribe(events =>
            {
                Debug.WriteLine("HotKeytObservable.Subscribe:" + events);
                if (window == null)
                    return;
                int Ans = -1;
                switch (events)
                {
                    case "1":  // A
                        Ans = 0;
                        break;
                    case "2":  // B
                        Ans = 1;
                        break;
                    case "3":  // C
                        Ans = 2;
                        break;
                    case "4":  // D
                        Ans = 3;
                        break;
                    default:
                        break;
                }
                if (Ans == QUESTION_CURRENT_RIGHT_ANSWER)
                {
                    window.SetResult(1);
                }
                else
                {
                    window.SetResult(0);
                }

            }))
            {
                return await window.WaitAsync();
            }
        }
```

- [ ] **步骤 5：删除 `ProcessToastNotificationSetNumber`（约 297-327 行）与 `SetWordNumber`（约 329-363 行）**

把这两个方法（含各自的 `/// <summary>` 注释）整段删除。

- [ ] **步骤 6：删除 `SetEngType`（约 365-406 行）**

整段删除。

- [ ] **步骤 7：改写 `PushMessage`（约 867-886 行）**

整个方法替换为：

```csharp
        /// <summary>
        /// 推送一条通知
        /// </summary>
        public void PushMessage(string Message)
        {
            MessageWindow.ShowMessage(Message);
        }
```

- [ ] **步骤 8：改写 `PushOneWord`（约 892-934 行）**

把方法末尾的 `new ToastContentBuilder()...Show();`（915-933 行）替换为：

```csharp
            WordCardWindow.ShowCard(
                CurrentWord.headWord,
                Phoneme,
                new[] { CurrentWord.pos + ". " + CurrentWord.tranCN, SentenceTran },
                null,
                ("记住了！", 0),
                ("暂时跳过..", 1),
                ("发音", 2));
```

同时把方法开头的 `ToastNotificationManagerCompat.History.Clear();`（第 894 行）删除。

- [ ] **步骤 9：改写 `PushOneWordSM2`（约 936-1001 行）**

把方法末尾的 `new ToastContentBuilder()...Show();`（970-1000 行，含被注释掉的「发音」按钮）替换为：

```csharp
            WordCardWindow.ShowCard(
                CurrentWord.headWord,
                Phoneme,
                new[] { CurrentWord.pos + ". " + CurrentWord.tranCN, SentenceTran },
                HeadTile,
                ("没有印象", 1),
                ("记忆模糊", 2),
                ("暂时记住", 3),
                ("已经牢记", 4));
```

同时把方法开头的 `ToastNotificationManagerCompat.History.Clear();`（第 938 行）删除。

- [ ] **步骤 10：改写 `PushWaitAllQuestions` 里的两处答错提示与两处 `History.Clear`**

(a) 第 1022 行的 `ToastNotificationManagerCompat.History.Clear();` 删除。

(b) 第 1043-1045 行：

```csharp
                    new ToastContentBuilder()
                    .AddText("错误 正确答案：" + AnswerDict[QUESTION_CURRENT_RIGHT_ANSWER.ToString()] + '.' + CurrentWord.headWord)
                    .Show();
```

改为：

```csharp
                    MessageWindow.ShowMessage("错误 正确答案：" + AnswerDict[QUESTION_CURRENT_RIGHT_ANSWER.ToString()] + '.' + CurrentWord.headWord);
```

(c) 第 1063 行的 `ToastNotificationManagerCompat.History.Clear();` 删除。

(d) 第 1079-1082 行：

```csharp
                    new ToastContentBuilder()
                    .AddText("错误, 正确答案：" + AnswerDict[QUESTION_CURRENT_RIGHT_ANSWER.ToString()])
                    .AddText(CurrentWord.explain)
                    .Show();
```

改为：

```csharp
                    MessageWindow.ShowMessage("错误, 正确答案：" + AnswerDict[QUESTION_CURRENT_RIGHT_ANSWER.ToString()] + "\n" + CurrentWord.explain);
```

(e) 第 1088 行的 `ToastNotificationManagerCompat.History.Clear();` 删除。

- [ ] **步骤 11：改写 `PushOneQuestion`（约 1114-1147 行）**

把末尾的 `new ToastContentBuilder()...Show();`（1122-1145 行）替换为：

```csharp
            ChoiceWindow.ShowChoice(
                "选择题",
                Question,
                (A, 0),
                (B, 1),
                (C, 2),
                (D, 3));
```

- [ ] **步骤 12：改写 `PushOneTransQuestion`（约 1172-1245 行）**

整个方法替换为：

```csharp
        public void PushOneTransQuestion(Word CurrentWord, string B, string C)
        {
            string Question = CurrentWord.tranCN;
            string A = CurrentWord.headWord;

            Random Rd = new Random();
            int AnswerIndex = Rd.Next(3);
            QUESTION_CURRENT_RIGHT_ANSWER = AnswerIndex;

            string[] options = AnswerIndex == 0
                ? new[] { A, B, C }
                : AnswerIndex == 1
                    ? new[] { B, A, C }
                    : new[] { C, B, A };

            ChoiceWindow.ShowChoice(
                "翻译",
                Question,
                ("A." + options[0], 0),
                ("B." + options[1], 1),
                ("C." + options[2], 2));
        }
```

- [ ] **步骤 13：删除剩余的 `History.Clear()`**

`UnorderWord` 里的第 830、857 行（原行号，改动后会前移）两处
`ToastNotificationManagerCompat.History.Clear();` 删除。删除后运行：

```bash
grep -n "ToastNotificationManagerCompat\|ToastContentBuilder\|Microsoft.Toolkit" Model/PushControl/PushWords.cs
```

预期：**无输出**。

- [ ] **步骤 14：编译验证**

运行任务 1 步骤 6 的 msbuild 命令。
预期：`0 个错误`

- [ ] **步骤 15：运行验证英语流程**

启动 `bin/Debug/ToastFish.exe`，托盘右键 → 开始！。
预期：
- 右下角出现卡片，显示单词、音标、释义、例句、状态行与 4 个按钮
- 打开记事本打字，**输入焦点不被抢走**
- 依次点 4 个按钮，状态行随之推进
- `Alt+1/2/3/4` 与点按钮等效；`` Alt+` `` 触发发音
- 背完后出现选择题 / 填空题窗口；答错时出现「错误 正确答案：…」提示窗口，4 秒后消失

- [ ] **步骤 16：Commit**

```bash
git add Model/PushControl/PushWords.cs
git commit -m "feat(notify): 英语背诵流程改用自绘窗口，移除 Toast 依赖"
```

---

## 任务 9：接入日语单词流程（`PushJpWords.cs`）

**文件：**
- 修改：`Model/PushControl/PushJpWords.cs`

- [ ] **步骤 1：替换 using**

把第 7 行 `using Microsoft.Toolkit.Uwp.Notifications;` 替换为：

```csharp
using ToastFish.View.Notify;
```

- [ ] **步骤 2：改写 `PushOneWord`（35-61 行）**

整个方法替换为：

```csharp
        public void PushOneWord(JpWord CurrentWord)
        {
            string phonetic = CurrentWord.hiragana;
            if (CurrentWord.Phone != -1)
                phonetic += "  重音：" + CurrentWord.Phone;

            WordCardWindow.ShowCard(
                CurrentWord.headWord,
                phonetic,
                new[] { CurrentWord.tranCN, CurrentWord.pos },
                null,
                ("记住了！", 0),
                ("暂时跳过..", 1),
                ("发音", 2));
        }
```

- [ ] **步骤 3：改写 `PushOneTransQuestion`（63-136 行）**

整个方法替换为：

```csharp
        public void PushOneTransQuestion(JpWord CurrentWord, string B, string C)
        {
            string Question = CurrentWord.tranCN;
            string A = CurrentWord.headWord;

            Random Rd = new Random();
            int AnswerIndex = Rd.Next(3);
            QUESTION_CURRENT_RIGHT_ANSWER = AnswerIndex;

            string[] options = AnswerIndex == 0
                ? new[] { A, B, C }
                : AnswerIndex == 1
                    ? new[] { B, A, C }
                    : new[] { C, B, A };

            ChoiceWindow.ShowChoice(
                "翻译",
                Question,
                ("A." + options[0], 0),
                ("B." + options[1], 1),
                ("C." + options[2], 2));
        }
```

- [ ] **步骤 4：删除 `History.Clear()` 与答错 Toast**

在 `Recitation`（约 138-260 行）与 `UnorderWord`（约 262-312 行）中：

(a) 删除所有 5 处 `ToastNotificationManagerCompat.History.Clear();`（原第 37、224、258、277、310 行）。

(b) 两处答错提示（原第 251-253 行、304-306 行）：

```csharp
                    new ToastContentBuilder()
                    .AddText("错误 正确答案：" + pushJpWords.AnswerDict[pushJpWords.QUESTION_CURRENT_RIGHT_ANSWER.ToString()] + '.' + CurrentWord.headWord)
                    .Show();
```

都改为：

```csharp
                    MessageWindow.ShowMessage("错误 正确答案：" + pushJpWords.AnswerDict[pushJpWords.QUESTION_CURRENT_RIGHT_ANSWER.ToString()] + '.' + CurrentWord.headWord);
```

- [ ] **步骤 5：确认清干净**

```bash
grep -n "ToastNotificationManagerCompat\|ToastContentBuilder\|Microsoft.Toolkit" Model/PushControl/PushJpWords.cs
```

预期：**无输出**。

- [ ] **步骤 6：编译验证**

运行任务 1 步骤 6 的 msbuild 命令。
预期：`0 个错误`

- [ ] **步骤 7：Commit**

```bash
git add Model/PushControl/PushJpWords.cs
git commit -m "feat(notify): 日语单词流程改用自绘窗口"
```

---

## 任务 10：接入五十音流程（`PushGoinWords.cs`）

**文件：**
- 修改：`Model/PushControl/PushGoinWords.cs`

- [ ] **步骤 1：替换 using**

把第 7 行 `using Microsoft.Toolkit.Uwp.Notifications;` 替换为：

```csharp
using ToastFish.View.Notify;
```

- [ ] **步骤 2：改写 `ProcessToastNotificationOrderGoin`（20-53 行）**

整个方法替换为：

```csharp
        public Task<int> ProcessToastNotificationOrderGoin()
        {
            NotifyWindowBase window = NotifyWindowBase.Current;
            return window.WaitAsync();
        }
```

- [ ] **步骤 3：改写 `ProcessToastNotificationGoinQuestion`（55-81 行）**

整个方法替换为：

```csharp
        public async Task<int> ProcessToastNotificationGoinQuestion()
        {
            NotifyWindowBase window = NotifyWindowBase.Current;
            return await window.WaitAsync();
        }
```

> **注意：** 这个方法原本就**不订阅** `HotKeytObservable`（五十音测验没有快捷键），改写后保持这一现状，不要顺手加。

- [ ] **步骤 4：改写 `PushGoinWord`（274-294 行）**

整个方法替换为：

```csharp
        public void PushGoinWord(GoinWord CurrentWord)
        {
            WordCardWindow.ShowCard(
                "平假名：" + CurrentWord.hiragana + " 片假名：" + CurrentWord.katakana,
                null,
                new[] { "罗马音：" + CurrentWord.romaji },
                null,
                ("记住了！", 0),
                ("发音", 2));
        }
```

- [ ] **步骤 5：改写 `PushOneGoinWordQuestion_1`（296-369 行）**

整个方法替换为：

```csharp
        public void PushOneGoinWordQuestion_1(GoinWord CurrentWord, GoinWord B, GoinWord C)
        {
            string Question = CurrentWord.romaji;
            string A = CurrentWord.hiragana;

            Random Rd = new Random();
            int AnswerIndex = Rd.Next(3);
            QUESTION_CURRENT_RIGHT_ANSWER = AnswerIndex;

            string[] options = AnswerIndex == 0
                ? new[] { A, B.hiragana, C.hiragana }
                : AnswerIndex == 1
                    ? new[] { B.hiragana, A, C.hiragana }
                    : new[] { C.hiragana, B.hiragana, A };

            ChoiceWindow.ShowChoice(
                "选择平假名",
                Question,
                ("A." + options[0], 0),
                ("B." + options[1], 1),
                ("C." + options[2], 2));
        }
```

- [ ] **步骤 6：改写 `PushOneGoinWordQuestion_2`（371-444 行）**

整个方法替换为：

```csharp
        public void PushOneGoinWordQuestion_2(GoinWord CurrentWord, GoinWord B, GoinWord C)
        {
            string Question = CurrentWord.hiragana;
            string A = CurrentWord.katakana;

            Random Rd = new Random();
            int AnswerIndex = Rd.Next(3);
            QUESTION_CURRENT_RIGHT_ANSWER = AnswerIndex;

            string[] options = AnswerIndex == 0
                ? new[] { A, B.katakana, C.katakana }
                : AnswerIndex == 1
                    ? new[] { B.katakana, A, C.katakana }
                    : new[] { C.katakana, B.katakana, A };

            ChoiceWindow.ShowChoice(
                "选择片假名",
                Question,
                ("A." + options[0], 0),
                ("B." + options[1], 1),
                ("C." + options[2], 2));
        }
```

- [ ] **步骤 7：改写 `PushOneGoinWordQuestion_3`（446-519 行）**

整个方法替换为：

```csharp
        public void PushOneGoinWordQuestion_3(GoinWord CurrentWord, GoinWord B, GoinWord C)
        {
            string Question = CurrentWord.romaji;
            string A = CurrentWord.katakana;

            Random Rd = new Random();
            int AnswerIndex = Rd.Next(3);
            QUESTION_CURRENT_RIGHT_ANSWER = AnswerIndex;

            string[] options = AnswerIndex == 0
                ? new[] { A, B.katakana, C.katakana }
                : AnswerIndex == 1
                    ? new[] { B.katakana, A, C.katakana }
                    : new[] { C.katakana, B.katakana, A };

            ChoiceWindow.ShowChoice(
                "选择片假名",
                Question,
                ("A." + options[0], 0),
                ("B." + options[1], 1),
                ("C." + options[2], 2));
        }
```

- [ ] **步骤 8：删除 `History.Clear()` 与答错 Toast**

在 `OrderGoin`（约 83-196 行）与 `UnorderGoin`（约 198-272 行）中：

(a) 删除全部 5 处 `ToastNotificationManagerCompat.History.Clear();`（原第 144、194、219、270、276 行）。

(b) 两处答错提示（原第 188-190 行、264-266 行）：

```csharp
                    new ToastContentBuilder()
                    .AddText("错误 正确答案：" + pushGoinWords.AnswerDict[pushGoinWords.QUESTION_CURRENT_RIGHT_ANSWER.ToString()] + '.' + RightAnswer)
                    .Show();
```

都改为：

```csharp
                    MessageWindow.ShowMessage("错误 正确答案：" + pushGoinWords.AnswerDict[pushGoinWords.QUESTION_CURRENT_RIGHT_ANSWER.ToString()] + '.' + RightAnswer);
```

- [ ] **步骤 9：确认清干净**

```bash
grep -n "ToastNotificationManagerCompat\|ToastContentBuilder\|Microsoft.Toolkit" Model/PushControl/PushGoinWords.cs
```

预期：**无输出**。

- [ ] **步骤 10：编译验证**

运行任务 1 步骤 6 的 msbuild 命令。
预期：`0 个错误`

- [ ] **步骤 11：Commit**

```bash
git add Model/PushControl/PushGoinWords.cs
git commit -m "feat(notify): 五十音流程改用自绘窗口"
```

---

## 任务 11：接入自定义词库流程（`PushCustomizeWords.cs`）

**文件：**
- 修改：`Model/PushControl/PushCustomizeWords.cs`

- [ ] **步骤 1：替换 using**

把第 1 行 `using Microsoft.Toolkit.Uwp.Notifications;` 替换为：

```csharp
using ToastFish.View.Notify;
```

- [ ] **步骤 2：改写 `PushOneWord`（14-32 行）**

整个方法替换为：

```csharp
        public static void PushOneWord(CustomizeWord CurrentWord)
        {
            WordCardWindow.ShowCard(
                CurrentWord.firstLine,
                null,
                new[] { CurrentWord.secondLine, CurrentWord.thirdLine, CurrentWord.fourthLine },
                null,
                ("记住了！", 0),
                ("暂时跳过..", 1));
        }
```

- [ ] **步骤 3：确认清干净**

```bash
grep -rn "ToastNotificationManagerCompat\|ToastContentBuilder\|Microsoft.Toolkit" Model/PushControl/PushCustomizeWords.cs
```

预期：**无输出**。

- [ ] **步骤 4：编译验证**

运行任务 1 步骤 6 的 msbuild 命令。
预期：`0 个错误`

- [ ] **步骤 5：Commit**

```bash
git add Model/PushControl/PushCustomizeWords.cs
git commit -m "feat(notify): 自定义词库流程改用自绘窗口"
```

---

## 任务 12：清理 `ToastFish.xaml.cs` 并移除 toolkit 包引用

**文件：**
- 修改：`View/ToastFish.xaml.cs`、`ToastFish.csproj`

- [ ] **步骤 1：删除 `ExitApp_Click` 里的 `History.Clear()`**

在 `View/ToastFish.xaml.cs` 中，把 `ExitApp_Click`（约 722-726 行）：

```csharp
        private void ExitApp_Click(object sender, EventArgs e)
        {
            ToastNotificationManagerCompat.History.Clear();
            Environment.Exit(0);
        }
```

改为：

```csharp
        private void ExitApp_Click(object sender, EventArgs e)
        {
            Environment.Exit(0);
        }
```

- [ ] **步骤 2：移除 using**

删除 `View/ToastFish.xaml.cs` 第 1 行的 `using Microsoft.Toolkit.Uwp.Notifications;`。

- [ ] **步骤 3：全库确认无残留**

```bash
grep -rn "ToastContentBuilder\|ToastNotificationManagerCompat\|Microsoft.Toolkit" --include=*.cs .
```

预期：**无输出**（`README.md` 与 `docs/` 里的文字提及不算，那是文档）。

- [ ] **步骤 4：移除包引用**

删除 `ToastFish.csproj` 中（约第 223-225 行）：

```xml
    <PackageReference Include="Microsoft.Toolkit.Uwp.Notifications">
      <Version>7.0.0</Version>
    </PackageReference>
```

- [ ] **步骤 5：编译验证**

运行任务 1 步骤 6 的 msbuild 命令。
预期：`0 个错误`

- [ ] **步骤 6：Commit**

```bash
git add View/ToastFish.xaml.cs ToastFish.csproj
git commit -m "chore(notify): 移除 Microsoft.Toolkit.Uwp.Notifications 依赖"
```

---

## 任务 13：端到端手动验证

**文件：** 无（纯验证）

- [ ] **步骤 1：全新构建**

```bash
"/c/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Current/Bin/MSBuild.exe" \
  ToastFish.csproj -p:Configuration=Debug -t:Rebuild -nologo -v:m
```
预期：`0 个错误`

- [ ] **步骤 2：逐项跑验证清单**

启动 `bin/Debug/ToastFish.exe`，逐条确认（对应规格的「测试」章节）：

1. 托盘 → 开始！→ 右下角出现卡片；**在记事本里打字不被打断**
2. 4 个按钮各点一次 → 状态行正确推进
3. `Alt+1/2/3/4` → 与点按钮等效；`` Alt+` `` → 发音
4. 背完 → 选择题 / 填空题窗口 → 答对答错流程都正确
5. **四种卡片**：英语（SM2 与非 SM2）、日语单词、五十音、自定义词库导入
6. **三种题目**：选择题（4 选 1）、翻译（3 选 1）、平假名 / 片假名（3 选 1）
7. **四种入口**：正常背诵、随机英语单词测试、随机日语单词测试、随机五十音测试
8. 答错时 → 提示消息窗口显示正确答案，随后流程继续（不卡死）
9. 设置窗口改字号 → 卡片**立刻**变大
10. 主题切浅色 / 深色 / 跟随系统 → 卡片跟着变
11. 提示消息窗口 4 秒自动消失
12. 多显示器：在副屏用鼠标时，卡片出现在副屏右下角
13. 托盘菜单（含新的「设置…」）、开机自启等原有功能不受影响
14. 托盘 → 退出，不报错

- [ ] **步骤 3：记录结果**

任何一条不通过，就在对应任务里修，修完重新跑本任务。全通过后：

```bash
git status --short
```

预期：只有 `Resources/inami.db`、`View/ToastFish.xaml.cs` 之外的历史遗留改动（本次改动已全部 commit）。

---

## 自检记录

**1. 规格覆盖度**

| 规格章节 | 对应任务 |
|---|---|
| 目标 1：移除通知平台依赖 | 任务 8-12 |
| 目标 2：字体可配置 | 任务 1、2、7 |
| 目标 3：窗口一直显示到用户操作 | 任务 3（无自动关闭）、任务 4（提示消息除外） |
| 架构 / 文件结构 | 任务 2-7 |
| 组件职责 / 线程模型 | 任务 3、8 |
| 配置（3 列） | 任务 1 |
| 错误处理 | 任务 3（`DefaultResult` 兜底）、任务 5/6（`SetButtons` 必给结果） |
| 测试清单 15 条 | 任务 13 |
| 托盘菜单合并 | 任务 7 步骤 5 |

**已知偏差（需向用户说明）**

- 规格写的是 `ProcessToastNotification*` 「直接返回窗口的 `Task<int>`」，实际实现保留了
  `HotKeytObservable` 订阅（放在 `PushWords` 里而非窗口里），这样改动更小、且能原样保留
  五十音测验没有快捷键这一现状。对外行为不变。
- 规格的「用户点窗口关闭按钮 → 视同暂时跳过」在本实现里几乎不可达（窗口无边框、无关闭
  按钮、不抢焦点所以 `Alt+F4` 也到不了）。`DefaultResult` 仍作为异常兜底保留。

**2. 占位符扫描：** 无「待定 / TODO / 后续实现」；每个代码步骤都给了完整代码块。

**3. 类型一致性：** `NotifyWindowBase.Current` / `WaitAsync()` / `SetResult(int)` / `OnUi(Action)` /
`ShowAsCurrent()` / `AddLine(...)` / `SetButtons(...)` 在任务 3 定义，任务 4-6 使用；
`WordCardWindow.ShowCard(string, string, string[], string, params (string,int)[])` 在任务 5 定义，
任务 8/9/10/11 使用；`ChoiceWindow.ShowChoice(string, string, params (string,int)[])` 在任务 6 定义，
任务 8/9/10 使用；`MessageWindow.ShowMessage(string, int)` 在任务 4 定义，任务 8/9/10 使用。
`NotifyTheme` 的属性名在任务 2 定义，任务 3-6 使用，一致。
