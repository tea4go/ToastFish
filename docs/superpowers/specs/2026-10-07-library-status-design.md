# 词库状态记录与查看 设计

## 背景与目标

现在每个词库只有「背诵进度」一个数字（`Count` 表的 `current / number`），测试成绩完全不记录，用户也看不到任何统计。目标：

1. 为每个内置词库记录**背诵**与**测试**的状态；
2. 能在托盘菜单里手动查看当前库的完整状态。

## 范围

**覆盖**：`Count` 表里已有的 18 个库 —— 16 个英语库 + `Goin`（五十音）+ `StdJp_Mid`（日语）。

**不含自定义词库**。理由：`Select.cs:227` 的 `SelectWordList()` 会把 `TABLE_NAME` 里带「自定义」的映射成 `GRE_2` 复用其词表，若给自定义单独建一行，进度和成绩会与 `GRE_2` 的数据混叠；且自定义是「导入文件 → 当场背完」的一次性流程，没有持久库身份。

## 记录的数据

每个库一行：

| 字段 | 类型 | 含义 |
|---|---|---|
| `bookName` | TEXT | 库名（已有，主键语义） |
| `number` | INTEGER | 总词数（已有） |
| `current` | INTEGER | 背诵进度（已有，不动） |
| `reciteCount` | INTEGER | 累计背诵次数 |
| `lastReciteTime` | TEXT | 最近一次背诵完成时间 |
| `testCount` | INTEGER | 累计测试次数 |
| `lastTestTime` | TEXT | 最近一次测试完成时间 |
| `lastTestCorrect` | INTEGER | 最近一次测试**首轮**答对题数 |
| `lastTestTotal` | INTEGER | 最近一次测试总题数 |

时间格式 `yyyy-MM-dd HH:mm`，与既有 `dateLastReviewed TEXT` 的风格一致。

## 存储方案

### 方案 A：给 `Count` 表加列（采用）

`Count` 表本来就是「每个库一行」，天然就是状态表。加 6 列，迁移沿用既有模式 —— `LoadGlobalConfig()`（`Select.cs:119-155`）里那套 `PRAGMA table_info` + `ALTER TABLE ADD COLUMN`。

- 优点：一张表一次查询拿到全部；复用既有迁移写法和 `BookCount` 类；改动面最小。
- 缺点：`Count` 表列数从 3 增到 9。

### 方案 B：新建 `LibraryStatus` 表

职责分离，`Count` 表不动。代价是多一张表、一套 CRUD、一套建表迁移，而且进度和状态分家，将来更容易不一致。

### 方案 C：塞进 `Global` 表

只能记「当前库」，切库即丢历史，不满足「对每个库」，否决。

## 迁移

新增 `Select.EnsureCountColumns()`，仿 `LoadGlobalConfig()` 的写法逐列判断：

```csharp
public void EnsureCountColumns()
{
    String cmdtext = "PRAGMA table_info(Count)";
    SQLiteCommand Update = DataBase.CreateCommand();
    Update.CommandText = cmdtext;
    var dr = Update.ExecuteReader();
    List<string> HeadTileList = new List<string>();
    while (dr.Read())
        HeadTileList.Add((string)dr.GetValue(1));
    dr.Close();

    if (HeadTileList.Contains("reciteCount") == false)
    {
        Update.CommandText = "ALTER TABLE Count ADD COLUMN reciteCount INTEGER NOT NULL DEFAULT 0";
        Update.ExecuteNonQuery();
    }
    // lastReciteTime / testCount / lastTestTime / lastTestCorrect / lastTestTotal 同理
}
```

在 `LoadGlobalConfig()` 末尾调用。`LoadGlobalConfig()` 是启动时必跑一次的既有迁移入口（`MainWindow` 构造函数里调），放这里最省事。

## 计数口径

### 背诵次数 / 测试次数

线程跑到**正常结束**（`PushMessage("结束了！恭喜！")`）时 +1。被「开始！」中止、中途关窗、异常退出都不算。

### 首轮正确率

测试是「答错不消题、循环到全对」，所以最终必然 100%，记「最终得分」没有意义。改记**首轮正确率**：该词第一次被抽到就答对的比例。

实现（三处测试循环形状完全一致，照同一套模板改）：

```csharp
int total = TestList.Count;          // 循环开始前
int correct = 0;
var wrongWords = new HashSet<X>();   // X 为 Word / JpWord / GoinWord

while (TestList.Count != 0)
{
    ...
    if (QUESTION_CURRENT_STATUS == 1)
    {
        // Add 返回 true 表示这个词之前没答错过，即首轮答对
        if (wrongWords.Add(CurrentWord))
            correct++;
        TestList.Remove(CurrentWord);
    }
    else if (QUESTION_CURRENT_STATUS == 0)
    {
        wrongWords.Add(CurrentWord);
        ...
    }
}
PushMessage("结束了！恭喜！");
new Select().RecordTest(correct, total);
```

## 接入点

| 流程 | 入口函数 | 结束处 | 动作 |
|---|---|---|---|
| 英语背诵 | `PushWords.RecitationSM2` | `PushWords.cs:457` | `RecordRecite()` |
| 日语背诵 | `PushJpWords.Recitation` | `PushJpWords.cs:211` | `RecordRecite()` |
| 五十音背诵 | `PushGoinWords.OrderGoin` | `PushGoinWords.cs:144` | `RecordRecite()` |
| 英语测试 | `PushWords.RunUnorderWord` | `PushWords.cs:668` | `RecordTest(correct, total)` |
| 日语测试 | `PushJpWords.UnorderWord` | `PushJpWords.cs:259` | `RecordTest(correct, total)` |
| 五十音测试 | `PushGoinWords.UnorderGoin` | `PushGoinWords.cs:216` | `RecordTest(correct, total)` |

测试成绩记到 `TABLE_NAME` 指向的库 —— 随机测试入口会先把 `TABLE_NAME` 设成被测的库（`RandomWordTest_Click` 设 `GRE_2`、`RandomJpWordTest_Click` 设 `StdJp_Mid`、`RandomGoinTest_Click` 设 `Goin`），所以归属天然正确。

### `PushWords.Recitation` / `PushJpWords.Recitation` 特殊处理

这两个函数都有导入模式（`ImportFlag`）。目前 `PushWords.Recitation`（`PushWords.cs:472`）只被 `ImportWords_Click` 走到（导入英语词表，`TABLE_NAME` 被设成 `GRE_2`），`PushJpWords.Recitation`（`PushJpWords.cs:94`）则两种模式都会走。

既有代码在导入模式下**刻意跳过**进度记录（`PushWords.cs:568-572` 的 `if (ImportFlag == false)` 守卫），因为导入的一批词不是「背这个库」。

所以计数也按同一守卫加：

```csharp
if (ImportFlag == false)
    Query.RecordRecite();
```

导入模式下不计数，与既有语义一致；将来若有人以非导入模式调它们，计数也不会漏。

`PushGoinWords.OrderGoin` 没有导入模式，直接无条件调用。

## 数据层新增方法（`Model/SqliteControl/Select.cs`）

```csharp
/// <summary>记一次背诵完成。</summary>
public void RecordRecite()
{
    SQLiteCommand Update = DataBase.CreateCommand();
    Update.CommandText = "UPDATE Count SET reciteCount = reciteCount + 1" +
        ", lastReciteTime = '" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "'" +
        " WHERE bookName = '" + TABLE_NAME + "'";
    Update.ExecuteNonQuery();
}

/// <summary>记一次测试完成。correct 为首轮答对题数，total 为总题数。</summary>
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
```

`BookCount` 类加对应的 6 个属性，`SelectStatus()` 复用现有的 `DataBase.Query<BookCount>(... where bookName = TABLE_NAME)`。

## 显示

### 托盘菜单

`View/ToastFish.xaml.cs` 新增菜单项 `LibraryStatus`，文本「当前词库状态」，放在「随机测试」子菜单之后（与测试相关的操作聚在一起）。

点击后读当前库那行，拼多行文本：

```
当前词库：四级词汇
背诵进度：530 / 1065
背诵：5 次，最近 2026-10-06 21:30
测试：3 次，最近 2026-10-07 09:12
最近测试：首轮 8/10（80%）
```

没有记录的部分显示「暂无」（如从未测试过则最后两行合并为「测试：暂无」）。库名用 `TablelDictionary`（`ToastFish.xaml.cs:40`）映射成中文名。

### 为什么不能直接用 `MessageWindow.ShowMessage`

`MessageWindow.ShowMessage` 走 `ShowAsCurrent()`，里面会把上一个窗口 `Close()` 掉（`NotifyWindowBase.cs:130`）。如果此时屏幕上正有一道测试题，题目窗口会被关掉、测试线程被推进 —— 查个状态把正在做的题弄没了，不可接受。

### 新增 `ShowTransient()`

在 `NotifyWindowBase` 加一个**不参与 `Current` 竞争**的显示入口：

```csharp
/// <summary>只显示、不抢 Current。用于状态查询这类不该打断测试的提示。</summary>
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

`MessageWindow` 加一个静态方法：

```csharp
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

8 秒后自动消失（状态有 5 行文字，比默认 4 秒长一些）。**不加 ✕** —— 基类的 ✕ 绑的是 `Pause()`，会给一个本就不参与 `Current` 的窗口置上 `Paused` 标记，污染收起/恢复状态。

## 不做的事

- 不给自定义词库记录状态（见「范围」）。
- 不做历史明细列表、不做趋势图、不做「历史最好成绩」。
- 不改测试「答错不消题、循环到全对」的既有行为。
- 不做「每次测试结束自动弹总结」。
- 不加设置窗口里的多库状态列表页。

## 已知边界

- 若用户当前库是「自定义」（导入后 `TABLE_NAME` 停在「自定义」），此时点随机测试，`SelectWordList()` 会把 `TABLE_NAME` 改成 `GRE_2`，成绩会记到 `GRE_2` 那一行。属既有映射逻辑的连带效果，本次不处理。
- 状态提示窗口与测试题窗口都会定位到「鼠标所在屏幕右下角」，同时显示时会短暂重叠，8 秒后状态窗口自行消失。

## 验证

1. **编译** Debug 与 Release，预期 EXIT=0。
2. **反射探针**（参考既有 `tf_pause_probe.cs` 的写法）：
   - 调 `Select.EnsureCountColumns()`，断言 `PRAGMA table_info(Count)` 里出现 6 个新列；连调两次不报错（幂等）。
   - 造一行测试数据，调 `RecordRecite()` / `RecordTest(8, 10)`，断言 `reciteCount` / `testCount` 自增、时间与 `lastTestCorrect/lastTestTotal` 正确落库。
   - 对 `Count` 表快照 → 跑一遍 `PushJpWords.UnorderWord`（全部答对、中间故意答错一次）→ 断言 `testCount +1`、`lastTestCorrect == 总题数 - 1`、`lastTestTotal == 总题数`。
   - 断言 `MessageWindow.ShowStatus(...)` 显示后 `NotifyWindowBase.Current` **未被改动**（不打断测试）。
3. **需要手动确认**（托盘菜单无法用探针驱动）：托盘右键 →「当前词库状态」→ 弹出多行状态文本；有测试题在屏幕上时点它，题目不被顶掉。
