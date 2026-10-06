# 词库状态记录与查看 实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 为 18 个内置词库记录背诵/测试的次数、时间与首轮正确率，并能在托盘菜单里手动查看当前库的完整状态。

**架构：** 状态直接落在既有的 `Count` 表（每库一行）上，加 6 列，迁移沿用 `LoadGlobalConfig()` 里那套 `PRAGMA table_info` + `ALTER TABLE ADD COLUMN`。背诵/测试线程跑到正常结束时各调一次记录方法。显示走托盘菜单，用一个不抢 `Current` 的提示窗口，避免打断正在进行的测试。

**技术栈：** C# 7.3 / .NET Framework 4.7.2 / WPF / WinForms 托盘 / SQLite + Dapper

**设计依据：** `docs/superpowers/specs/2026-10-07-library-status-design.md`

---

## 文件结构

| 文件 | 职责 | 变更 |
|---|---|---|
| `Model/SqliteControl/Select.cs` | 数据层：加列迁移、`RecordRecite` / `RecordTest` / `SelectStatus` | 修改 |
| `Model/PushControl/PushWords.cs` | 英语背诵（`RecitationSM2` / `Recitation`）与测试（`RunUnorderWord`）接入 | 修改 |
| `Model/PushControl/PushJpWords.cs` | 日语背诵（`Recitation`）与测试（`UnorderWord`）接入 | 修改 |
| `Model/PushControl/PushGoinWords.cs` | 五十音背诵（`OrderGoin`）与测试（`UnorderGoin`）接入 | 修改 |
| `View/Notify/NotifyWindowBase.cs` | 新增 `ShowTransient()`（不抢 `Current` 的显示入口） | 修改 |
| `View/Notify/MessageWindow.cs` | 新增 `ShowStatus()` | 修改 |
| `View/ToastFish.xaml.cs` | 托盘菜单「当前词库状态」+ 点击处理 | 修改 |
| `C:\Users\Admin\AppData\Local\Temp\tf_status_probe.cs` | 反射探针（临时，不进仓库） | 新建 |

**构建命令**（本仓库唯一验证手段，没有单元测试框架）：

```bash
cd /c/MyWork/GitCode/ToastFish && "/c/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Current/Bin/MSBuild.exe" ToastFish.csproj -p:Configuration=Debug -nologo -v:m
```

Release 必须换输出目录，否则会撞上用户正在运行的 `bin\Release\ToastFish.exe`（进程锁定文件）：

```bash
cd /c/MyWork/GitCode/ToastFish && "/c/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Current/Bin/MSBuild.exe" ToastFish.csproj -p:Configuration=Release -nologo -v:m -p:'OutDir=C:\Users\Admin\AppData\Local\Temp\tf_rel\'
```

---

## 任务 1：数据层 —— 加列迁移与记录/读取方法

**文件：**
- 修改：`Model/SqliteControl/Select.cs`

- [ ] **步骤 1：给 `BookCount` 加 6 个属性**

把 `Model/SqliteControl/Select.cs:538-543` 的 `BookCount` 类改成：

```csharp
    public class BookCount
    {
        public String bookName { get; set; }
        public int number { get; set; }
        public int current { get; set; }
        public int reciteCount { get; set; }
        public string lastReciteTime { get; set; }
        public int testCount { get; set; }
        public string lastTestTime { get; set; }
        public int lastTestCorrect { get; set; }
        public int lastTestTotal { get; set; }
    }
```

Dapper 按列名映射，`TEXT DEFAULT NULL` 的列映射到 `string` 时为 `null`。

- [ ] **步骤 2：加迁移方法 `EnsureCountColumns()`**

加在 `LoadGlobalConfig()`（`Select.cs:117`）**之前**：

```csharp
        /// <summary>
        /// 给 Count 表补上状态统计列。老库没有这些列，启动时补一次。重复调用无副作用。
        /// </summary>
        public void EnsureCountColumns()
        {
            SQLiteCommand Update = DataBase.CreateCommand();
            Update.CommandText = "PRAGMA table_info(Count)";
            var dr = Update.ExecuteReader();
            List<string> HeadTileList = new List<string>();
            while (dr.Read())
                HeadTileList.Add((string)dr.GetValue(1));
            dr.Close();

            AddCountColumn(HeadTileList, "reciteCount", "INTEGER NOT NULL DEFAULT 0");
            AddCountColumn(HeadTileList, "lastReciteTime", "TEXT DEFAULT NULL");
            AddCountColumn(HeadTileList, "testCount", "INTEGER NOT NULL DEFAULT 0");
            AddCountColumn(HeadTileList, "lastTestTime", "TEXT DEFAULT NULL");
            AddCountColumn(HeadTileList, "lastTestCorrect", "INTEGER NOT NULL DEFAULT 0");
            AddCountColumn(HeadTileList, "lastTestTotal", "INTEGER NOT NULL DEFAULT 0");
        }

        private void AddCountColumn(List<string> existing, string name, string definition)
        {
            if (existing.Contains(name))
                return;
            SQLiteCommand Update = DataBase.CreateCommand();
            Update.CommandText = "ALTER TABLE Count ADD COLUMN " + name + " " + definition;
            Update.ExecuteNonQuery();
        }
```

- [ ] **步骤 3：在 `LoadGlobalConfig()` 末尾调用它**

`Select.cs:165` 的 `THEME = GlobalVariable[0].theme;` 之后加一行：

```csharp
            EnsureCountColumns();
```

`LoadGlobalConfig()` 是启动时必跑一次的既有迁移入口（`MainWindow` 构造函数第 51 行调），放这里最省事。

- [ ] **步骤 4：加 `RecordRecite` / `RecordTest` / `SelectStatus`**

加在 `SelectCount()`（`Select.cs:201`）之后、`#endregion` 之前：

```csharp
        /// <summary>记一次背诵完成。计数与时间落到当前库那一行。</summary>
        public void RecordRecite()
        {
            SQLiteCommand Update = DataBase.CreateCommand();
            Update.CommandText = "UPDATE Count SET reciteCount = reciteCount + 1" +
                ", lastReciteTime = '" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "'" +
                " WHERE bookName = '" + TABLE_NAME + "'";
            Update.ExecuteNonQuery();
        }

        /// <summary>记一次测试完成。correct 是首轮答对题数，total 是总题数。</summary>
        public void RecordTest(int correct, int total)
        {
            SQLiteCommand Update = DataBase.CreateCommand();
            Update.CommandText = "UPDATE Count SET testCount = testCount + 1" +
                ", lastTestTime = '" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "'" +
                ", lastTestCorrect = " + correct +
                ", lastTestTotal = " + total +
                " WHERE bookName = '" + TABLE_NAME + "'";
            Update.ExecuteNonQuery();
        }

        /// <summary>读当前库的状态行。表里没有这个库时返回 null。</summary>
        public BookCount SelectStatus()
        {
            BookCount Temp = new BookCount();
            var rows = DataBase.Query<BookCount>($"select * from Count where bookName = '{TABLE_NAME}'", Temp).ToArray();
            return rows.Length == 0 ? null : rows[0];
        }
```

- [ ] **步骤 5：编译**

运行上面的 Debug 构建命令。预期：`EXIT=0`，无 error。

- [ ] **步骤 6：写探针验证数据层**

新建 `C:\Users\Admin\AppData\Local\Temp\tf_status_probe.cs`。反射加载真实程序集，调**真实**的迁移/记录方法（不是自己写一遍 SQL）：

```csharp
using System;
using System.Data.SQLite;
using System.Reflection;

/// <summary>验证 Count 表的加列迁移与记录/读取方法。</summary>
class StatusProbe
{
    static int fail;
    const string Exe = @"C:\MyWork\GitCode\ToastFish\bin\Debug\ToastFish.exe";
    const string Book = "VOA_1500";

    static void Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        Assembly asm = Assembly.LoadFrom(Exe);
        Type selectType = asm.GetType("ToastFish.Model.SqliteControl.Select", true);
        object sel = Activator.CreateInstance(selectType);
        SQLiteConnection db = (SQLiteConnection)selectType.GetField("DataBase").GetValue(sel);

        MethodInfo ensure = selectType.GetMethod("EnsureCountColumns");
        Ok("找到 EnsureCountColumns", ensure != null);
        if (ensure == null) { Report(); return; }

        selectType.GetField("TABLE_NAME").SetValue(null, Book);

        // 幂等：连调两次不抛异常
        ensure.Invoke(sel, null);
        ensure.Invoke(sel, null);

        foreach (string col in new[] { "reciteCount", "lastReciteTime", "testCount", "lastTestTime", "lastTestCorrect", "lastTestTotal" })
            Ok("列存在：" + col, HasColumn(db, col));

        selectType.GetMethod("RecordRecite").Invoke(sel, null);
        selectType.GetMethod("RecordTest").Invoke(sel, new object[] { 8, 10 });

        object status = selectType.GetMethod("SelectStatus").Invoke(sel, null);
        Type bt = status.GetType();
        Ok("reciteCount = 1", (int)bt.GetProperty("reciteCount").GetValue(status) == 1);
        Ok("testCount = 1", (int)bt.GetProperty("testCount").GetValue(status) == 1);
        Ok("lastTestCorrect = 8", (int)bt.GetProperty("lastTestCorrect").GetValue(status) == 8);
        Ok("lastTestTotal = 10", (int)bt.GetProperty("lastTestTotal").GetValue(status) == 10);
        Ok("lastReciteTime 非空", !string.IsNullOrEmpty((string)bt.GetProperty("lastReciteTime").GetValue(status)));
        Ok("lastTestTime 非空", !string.IsNullOrEmpty((string)bt.GetProperty("lastTestTime").GetValue(status)));

        // 还原，别把开发库弄脏
        Exec(db, "UPDATE Count SET reciteCount = 0, lastReciteTime = NULL, testCount = 0" +
                 ", lastTestTime = NULL, lastTestCorrect = 0, lastTestTotal = 0 WHERE bookName = '" + Book + "'");
        Report();
    }

    static void Exec(SQLiteConnection cn, string sql)
    {
        SQLiteCommand cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    static bool HasColumn(SQLiteConnection cn, string col)
    {
        SQLiteCommand cmd = cn.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(Count)";
        var dr = cmd.ExecuteReader();
        bool found = false;
        while (dr.Read())
            if ((string)dr.GetValue(1) == col) { found = true; break; }
        dr.Close();
        return found;
    }

    static void Ok(string what, bool pass)
    {
        if (!pass) fail++;
        Console.WriteLine("   " + (pass ? "OK  " : "FAIL") + " " + what);
    }

    static void Report()
    {
        Console.WriteLine();
        Console.WriteLine(fail == 0 ? "全部通过" : fail + " 项失败");
        Console.Out.Flush();
    }
}
```

> 这个探针**不建 WPF Application**，只反射 `Select` 这个纯数据类，所以不需要 PresentationCore 等 WPF 引用。但它必须能解析 `SQLite.Interop.dll`，因此 exe 要落在 `bin/Debug` 下运行。

- [ ] **步骤 7：编译并运行探针**

```bash
"/c/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe" -nologo -target:exe \
  -out:/c/MyWork/GitCode/ToastFish/bin/Debug/tf_status_probe.exe \
  -r:/c/MyWork/GitCode/ToastFish/bin/Debug/System.Data.SQLite.dll \
  /c/Users/Admin/AppData/Local/Temp/tf_status_probe.cs
cd /c/MyWork/GitCode/ToastFish/bin/Debug && ./tf_status_probe.exe
```

预期：全部 OK。**注意**：必须在 `bin/Debug` 下运行，`SQLite.Interop.dll` 按 exe 目录解析；放在别处会报 `DllNotFoundException`。

---

## 任务 2：背诵侧接入（4 处）

**文件：**
- 修改：`Model/PushControl/PushWords.cs`、`Model/PushControl/PushJpWords.cs`、`Model/PushControl/PushGoinWords.cs`

- [ ] **步骤 1：英语 SM2 背诵**

`PushWords.cs:457`，`pushWords.PushMessage("结束了！恭喜！");` 之后加一行：

```csharp
            pushWords.PushMessage("结束了！恭喜！");
            Query.RecordRecite();
```

（`Query` 是该函数里已有的 `Select` 实例，见 `PushWords.cs:455` 的 `Query.AllWordList`。）

- [ ] **步骤 2：英语导入式背诵**

`PushWords.cs:603`，同样位置，但要跟既有的 `ImportFlag` 守卫一致（`PushWords.cs:568` 那里就是这么写的）：

```csharp
            pushWords.PushMessage("结束了！恭喜！");
            if (ImportFlag == false)
                Query.RecordRecite();
```

- [ ] **步骤 3：日语背诵**

`PushJpWords.cs:211`：

```csharp
            pushJpWords.PushMessage("结束了！恭喜！");
            if (ImportFlag == false)
                Query.RecordRecite();
```

- [ ] **步骤 4：五十音背诵**

`PushGoinWords.cs:144`（没有导入模式，无条件调用）：

```csharp
            pushGoinWords.PushMessage("结束了！恭喜！");
            Query.RecordRecite();
```

- [ ] **步骤 5：编译**

运行 Debug 构建命令，预期 `EXIT=0`。

---

## 任务 3：测试侧接入（3 处，含首轮正确率计数）

三处测试循环形状完全一致，照同一套模板改。

**文件：**
- 修改：`Model/PushControl/PushWords.cs`、`Model/PushControl/PushJpWords.cs`、`Model/PushControl/PushGoinWords.cs`

- [ ] **步骤 1：英语测试 `RunUnorderWord`**

`PushWords.cs:619`。循环前（`Word CurrentWord = new Word();` 那一行前后）加三行：

```csharp
            int total = TestList.Count;
            int correct = 0;
            var wrongWords = new HashSet<Word>();
```

`PushWords.cs:655-666` 的两个分支改成：

```csharp
                if (QUESTION_CURRENT_STATUS == 1)
                {
                    // Add 返回 true 表示这个词之前没答错过，即首轮答对
                    if (wrongWords.Add(CurrentWord))
                        correct++;
                    TestList.Remove(CurrentWord);
                    Thread.Sleep(500);
                }
                else if (QUESTION_CURRENT_STATUS == 0)
                {
                    //CopyList.Remove(CurrentWord);
                    wrongWords.Add(CurrentWord);
                    string rightAnswer = en2cn ? CurrentWord.tranCN : CurrentWord.headWord;
                    MessageWindow.ShowMessage("错误\n正确答案：" + AnswerDict[QUESTION_CURRENT_RIGHT_ANSWER.ToString()] + "\n" + rightAnswer);
                    Thread.Sleep(3000);
                }
```

`PushWords.cs:668` 的 `PushMessage("结束了！恭喜！");` 之后加：

```csharp
            PushMessage("结束了！恭喜！");
            Query.RecordTest(correct, total);
```

`HashSet<Word>` 用的是引用相等（`Word` 没有重写 `Equals`/`GetHashCode`），而 `GetRandomWord(TestList)` 返回的就是列表里的同一个实例，所以 `Remove` / 集合判定都按实例生效。

- [ ] **步骤 2：日语测试 `UnorderWord`**

`PushJpWords.cs:214`。循环前（`JpWord CurrentWord = new JpWord();` 之后）加：

```csharp
            int total = TestList.Count;
            int correct = 0;
            var wrongWords = new HashSet<JpWord>();
```

`PushJpWords.cs:247-257` 的分支改成：

```csharp
                if (pushJpWords.QUESTION_CURRENT_STATUS == 1)
                {
                    if (wrongWords.Add(CurrentWord))
                        correct++;
                    TestList.Remove(CurrentWord);
                    Thread.Sleep(500);
                }
                else if (pushJpWords.QUESTION_CURRENT_STATUS == 0)
                {
                    //CopyList.Remove(CurrentWord);
                    wrongWords.Add(CurrentWord);
                    MessageWindow.ShowMessage("错误\n正确答案：" + pushJpWords.AnswerDict[pushJpWords.QUESTION_CURRENT_RIGHT_ANSWER.ToString()] + "\n" + CurrentWord.headWord);
                    Thread.Sleep(3000);
                }
```

`PushJpWords.cs:259` 之后加：

```csharp
            pushJpWords.PushMessage("结束了！恭喜！");
            Query.RecordTest(correct, total);
```

- [ ] **步骤 3：五十音测试 `UnorderGoin`**

`PushGoinWords.cs:147`。循环前（`GoinWord CurrentWord = new GoinWord();` 之后）加：

```csharp
            int total = TestList.Count;
            int correct = 0;
            var wrongWords = new HashSet<GoinWord>();
```

`PushGoinWords.cs:203-214` 的分支改成：

```csharp
                if (pushGoinWords.QUESTION_CURRENT_STATUS == 1)
                {
                    if (wrongWords.Add(CurrentWord))
                        correct++;
                    TestList.Remove(CurrentWord);
                    //PushWords.PushMessage("正确,太强了吧！");
                    //Thread.Sleep(3000);
                }
                else if (pushGoinWords.QUESTION_CURRENT_STATUS == 0)
                {
                    //CopyList.Remove(CurrentWord);
                    wrongWords.Add(CurrentWord);
                    MessageWindow.ShowMessage("错误\n正确答案：" + pushGoinWords.AnswerDict[pushGoinWords.QUESTION_CURRENT_RIGHT_ANSWER.ToString()] + "\n" + RightAnswer);
                    Thread.Sleep(3000);
                }
```

`PushGoinWords.cs:216` 之后加：

```csharp
            pushGoinWords.PushMessage("结束了！恭喜！");
            Query.RecordTest(correct, total);
```

- [ ] **步骤 4：确认 `total` 取值位置正确**

三处的 `int total = TestList.Count;` 都必须在 `while (TestList.Count != 0)` **之前**，且在五十音的裁剪循环（`PushGoinWords.cs:154-159`）**之后** —— 否则记到的是裁剪前的题数。

- [ ] **步骤 5：编译**

运行 Debug 构建命令，预期 `EXIT=0`。

---

## 任务 4：显示 —— 不抢 `Current` 的提示窗口 + 托盘菜单项

**文件：**
- 修改：`View/Notify/NotifyWindowBase.cs`、`View/Notify/MessageWindow.cs`、`View/ToastFish.xaml.cs`

- [ ] **步骤 1：给 `NotifyWindowBase` 加 `ShowTransient()`**

加在 `ShowAsCurrent()`（`NotifyWindowBase.cs:119`）**之后**：

```csharp
        /// <summary>
        /// 只显示、不抢 Current。用于状态查询这类不该打断正在进行的测试的提示：
        /// 走 ShowAsCurrent 会把屏幕上的测试题顶掉，测试线程被推进到下一题。
        /// </summary>
        protected void ShowTransient()
        {
            NotifyTheme.Apply(this);
            _shell.Background = NotifyTheme.Background;
            _shell.BorderBrush = NotifyTheme.Border;
            try
            {
                Show();
                _shown = true;
            }
            catch (Exception ex)
            {
                Logger.Write("提示窗口显示失败：" + ex);
            }
        }
```

**不要**给它加 ✕：基类的 ✕ 绑的是 `Pause()`，会给一个本就不参与 `Current` 的窗口置上 `Paused` 标记，污染收起/恢复状态。

- [ ] **步骤 2：给 `MessageWindow` 加 `ShowStatus()`**

`MessageWindow.cs` 里加在 `ShowMessage` 之后：

```csharp
        /// <summary>
        /// 状态查询用的提示：不抢 Current（不打断正在进行的测试），8 秒后自动消失。
        /// 比默认 4 秒长一些，因为状态有多行文字要读。
        /// </summary>
        public static void ShowStatus(string text, int autoCloseMs = 8000)
        {
            OnUi(() =>
            {
                var window = new MessageWindow();
                window.AddLine(text, NotifyTheme.MeaningSize, NotifyTheme.Foreground);
                window.ShowTransient();
                window.StartAutoClose(autoCloseMs);
            });
        }
```

- [ ] **步骤 3：托盘菜单加字段**

`View/ToastFish.xaml.cs:155`，`ResumeTest` 那一行之后：

```csharp
        System.Windows.Forms.ToolStripMenuItem LibraryStatus = new System.Windows.Forms.ToolStripMenuItem();
```

- [ ] **步骤 4：配置菜单项**

`ToastFish.xaml.cs:211`，`ResumeTest.Click += ...` 之后：

```csharp
            LibraryStatus.Text = "当前词库状态";
            LibraryStatus.Click += new EventHandler(LibraryStatus_Click);
```

- [ ] **步骤 5：挂到托盘主菜单**

`ToastFish.xaml.cs:289`，`Cms.Items.Add(RandomTest);` 之后：

```csharp
            Cms.Items.Add(RandomTest);
            Cms.Items.Add(LibraryStatus);
```

放在「随机测试」后面，与背诵/测试相关的操作聚在一起。

- [ ] **步骤 6：加点击处理**

放在 `ResumeTest_Click`（`ToastFish.xaml.cs:663` 附近）之后：

```csharp
        /// <summary>
        /// 把当前库的状态拼成多行文本弹出来。用 ShowStatus 而不是 ShowMessage：
        /// 前者不抢 Current，不会把屏幕上的测试题顶掉。
        /// </summary>
        private void LibraryStatus_Click(object sender, EventArgs e)
        {
            BookCount status = Se.SelectStatus();
            if (status == null)
                return;

            string name = TablelDictionary.ContainsKey(Select.TABLE_NAME)
                ? TablelDictionary[Select.TABLE_NAME]
                : Select.TABLE_NAME;

            string text = "当前词库：" + name
                + "\n背诵进度：" + status.current + " / " + status.number
                + "\n背诵：" + status.reciteCount + " 次" + RecentTime(status.lastReciteTime);

            if (status.testCount == 0)
            {
                text += "\n测试：暂无";
            }
            else
            {
                text += "\n测试：" + status.testCount + " 次" + RecentTime(status.lastTestTime)
                    + "\n最近测试：首轮 " + status.lastTestCorrect + "/" + status.lastTestTotal
                    + "（" + Percent(status.lastTestCorrect, status.lastTestTotal) + "）";
            }

            MessageWindow.ShowStatus(text);
        }

        private static string RecentTime(string time)
        {
            return string.IsNullOrEmpty(time) ? "" : "，最近 " + time;
        }

        private static string Percent(int correct, int total)
        {
            if (total <= 0)
                return "0%";
            return (int)Math.Round(correct * 100.0 / total) + "%";
        }
```

`BookCount` 来自已引入的 `using ToastFish.Model.SqliteControl;`（`ToastFish.xaml.cs:8`），`MessageWindow` 来自 `using ToastFish.View.Notify;`（第 15 行），`TablelDictionary` 是 `MainWindow` 的私有字段（第 34 行）。三者都已就位，不需要加 using。

- [ ] **步骤 7：编译**

运行 Debug 构建命令，预期 `EXIT=0`。

---

## 任务 5：端到端探针验证

**文件：**
- 新建：`C:\Users\Admin\AppData\Local\Temp\tf_status_flow.cs`（临时）

- [ ] **步骤 1：写端到端探针**

参照既有的 `tf_jpflow.cs`（真实跑日语背诵流程）和 `tf_pause_probe.cs` 的写法：`new Application()` + `ShutdownMode.OnExplicitShutdown` + `Assembly.LoadFrom(bin/Debug/ToastFish.exe)` + 先反射调 `NotifyTheme.Load()`，跑完把 `Count` 表那一行还原。

断言清单：

1. `EnsureCountColumns()` 连调两次不抛异常，`PRAGMA table_info(Count)` 里 6 个新列都在。
2. 调 `Select.RecordRecite()` → `reciteCount +1`、`lastReciteTime` 非空。
3. 调 `Select.RecordTest(8, 10)` → `testCount +1`、`lastTestCorrect = 8`、`lastTestTotal = 10`、`lastTestTime` 非空。
4. `Select.SelectStatus()` 返回的字段与上面一致。
5. 真实跑一遍 `PushJpWords.UnorderWord(3)`：第 1 张卡先答错（`SetResult(0)`）、再答对，其余直接答对 → 断言 `testCount +1`、`lastTestCorrect == 2`、`lastTestTotal == 3`。
6. `MessageWindow.ShowStatus("测试")` 之后 `NotifyWindowBase.Current` **没有变化**（证明没打断测试）。

驱动 UI 的循环要**只对 `ChoiceWindow` 作答** —— 答错时弹出的 `MessageWindow` 也是 `NotifyWindowBase` 子类，不过滤会把它也当成题目。探针里先声明这两个状态：

```csharp
        var answered = new HashSet<object>();   // 已经答过的窗口，避免重复作答
        bool firstWronged = false;              // 第一张卡是否已经故意答错过
```

循环体：

```csharp
        Window w = Current();
        if (w != null && w.IsVisible && w.GetType().Name == "ChoiceWindow" && answered.Add(w))
        {
            int value = 1;                      // 1 = 答对（见各测试循环里的 task.Result == 1 分支）
            if (!firstWronged)
            {
                firstWronged = true;
                value = 0;                      // 第一张卡故意答错，用来验证「首轮」口径
            }
            SetResult(w, value);
        }
```

- [ ] **步骤 2：编译并运行探针**

```bash
REF="/c/Program Files (x86)/Reference Assemblies/Microsoft/Framework/.NETFramework/v4.7.2"
"/c/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe" -nologo -target:exe -out:/c/MyWork/GitCode/ToastFish/bin/Debug/tf_status_flow.exe \
  -r:"$REF/PresentationCore.dll" -r:"$REF/PresentationFramework.dll" -r:"$REF/WindowsBase.dll" -r:"$REF/System.Xaml.dll" \
  /c/Users/Admin/AppData/Local/Temp/tf_status_flow.cs
cd /c/MyWork/GitCode/ToastFish/bin/Debug && ./tf_status_flow.exe
```

预期：全部 OK。**必须**在 `bin/Debug` 下运行。

- [ ] **步骤 3：还原开发库并清理探针产物**

探针会把 `bin/Debug/Resources/inami.db` 的 `Count` 行改掉（该文件被 gitignore，但仍要还原）。删掉 `bin/Debug/tf_status_probe.exe`、`bin/Debug/tf_status_flow.exe` 及对应的 `.pdb`。

- [ ] **步骤 4：Release 编译**

运行上面的 Release 构建命令，预期 `EXIT=0`。**不要**用默认 `bin\Release\` 输出目录 —— 用户可能正开着 ToastFish，会锁住 `bin\Release\ToastFish.exe`。

- [ ] **步骤 5：提交**

```bash
cd /c/MyWork/GitCode/ToastFish && git add Model/SqliteControl/Select.cs Model/PushControl/PushWords.cs Model/PushControl/PushJpWords.cs Model/PushControl/PushGoinWords.cs View/Notify/NotifyWindowBase.cs View/Notify/MessageWindow.cs View/ToastFish.xaml.cs && git commit -m "feat: 词库状态记录与查看"
```

---

## 需要用户手动确认的部分

托盘菜单无法用探针驱动，以下要人工点一遍：

1. 托盘右键 → 出现「当前词库状态」，位置在「随机测试」之后。
2. 点它 → 弹出多行状态文本，8 秒后消失。
3. **屏幕上有一道测试题时点它** → 题目不被顶掉，测试继续。
4. 背完一轮后点它 → 「背诵」次数 +1、时间更新。
5. 测完一轮后点它 → 「测试」次数 +1、首轮正确率符合实际（故意答错的题要算在分母里、不算分子）。

---

## 已知边界（本次不处理）

- 当前库是「自定义」时点随机测试，`SelectWordList()` 会把 `TABLE_NAME` 改成 `GRE_2`，成绩会记到 `GRE_2` 那一行。属既有映射逻辑的连带效果。
- 状态提示窗口与测试题窗口都会定位到「鼠标所在屏幕右下角」，同时显示时会短暂重叠。
