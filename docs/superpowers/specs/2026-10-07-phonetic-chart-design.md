# 英语音标表（全视图窗口）设计

## 背景

ToastFish 目前只有背诵 / 测试卡片，没有任何地方能让用户系统地看一遍英语音标。
需求来自用户原话：

> 需要增加一个，英文音标的全视图界面，每个音标双击可以播放音标，点击可以显示包含音标的 5 个单词，每个单词又可以双击播放。

## 已确认的决策

| 项 | 决定 |
|---|---|
| 数据来源 | 音标表与例词全部内置，与用户当前词库无关 |
| 音标数量 | 44 个（20 元音 + 24 辅音，RP 标准）。不收 tr / dr / ts / dz |
| 音标发音 | 内置 mp3（`Resources\Phonetic\*.mp3`） |
| 音频出处 | 从 ipachart.com 抓取，ffmpeg 重编码 / 拼接 |
| 界面形式 | 独立窗口，左侧音标表、右侧例词 |
| 单击 / 双击 | 单击选中并展开例词；双击播放。用「幂等选中」消解冲突 |
| 例词显示 | 单词 + 音标 + 中文释义 |
| 主题 | 跟随 `NotifyTheme`（用户设置里的浅色 / 深色 / 跟随系统） |

## 架构

新增 4 个文件，改动 2 个文件。

```
Model/Phonetic/PhoneticSymbol.cs    音标与例词的数据类型
Model/Phonetic/PhoneticData.cs      44 条内置数据（静态）
View/PhoneticChartWindow.xaml       窗口布局
View/PhoneticChartWindow.xaml.cs    建表、选中、播放
Resources/Phonetic/*.mp3            44 个音标音频 + NOTICE.txt
```

### 数据层

`PhoneticExample`：`Word` / `Phonetic` / `Meaning`。
`PhoneticSymbol`：`Ipa` / `Audio`（`Resources\Phonetic` 下的文件名）/ `Examples`（5 个）。
`PhoneticData`：`Vowels`（20）+ `Consonants`（24）两个静态列表。

数据与用户词库完全无关，是静态常量。

### 界面

窗口 `720x620`，内容分两栏：左 `*`、间隔 20、右固定 `250`。

- 左栏：`ScrollViewer` 内一个 `StackPanel`，依次是「元音」标题、`WrapPanel`、「辅音」标题、`WrapPanel`。
  每个音标一个 `54x46` 的圆角 `Border`，里面是 `FontSize=20` 的音标符号。
- 右栏：标题（`/音标/  例词`）+ 一个 3 列的 `Grid`（单词 Auto / 音标 Auto / 释义 Star）。
  释义列吃剩余宽度并 `TextWrapping="Wrap"`，长释义在栏内折行。

右栏用固定宽度而不是 `Auto`：`Auto` 在例词为空时会塌成 0 宽，点一下再撑开，
左栏的方块会跟着重新换行 —— 布局跳动。固定 250 宽度稳定，且最长的一条释义也放得下。

窗口宽度 720 是按「44 个方块一屏放下」定的：左栏约 418px，一排放 6 个（每个 60px），
元音 4 行 + 辅音 4 行，加两个分组标题刚好填满，不需要滚动。

### 单击 / 双击

WPF 里双击必然先触发两次单击，所以不能把「单击」做成「展开 / 收起」的开关 ——
双击会先展开再收起。这里的做法是把单击定义成**幂等的选中**：再点同一个音标不取消。
于是双击 = 选中两次（状态不变）+ 播放，天然没有副作用。

用 `Border` + `MouseLeftButtonDown` 判 `e.ClickCount`，不能用 `Button`：
`Button` 自带的点击逻辑会和双击抢事件。

### 播放

- 音标：`Resources\Phonetic\<Audio>`，走既有的 `MUSIC`（MCI 封装）。
  它是阻塞调用，必须放后台线程。
- 例词：与背诵卡片同一套 —— 先取有道音频（`DownloadMp3.PlayMp3`），
  取不到再用 `SpeechSynthesizer` 兜底。有道接口不认 IPA 符号（返回 500），
  只认单词，所以例词走有道、音标走内置 mp3。

### 入口

托盘菜单新增顶层项「英语音标表」，插在「当前词库状态」和「设置」之间。
`PhoneticChartWindow.ShowChart()` 是静态入口，同一时刻只留一个窗口，
重复点只把已开的那个提到前面。

## 音频来源与授权

`Resources/Phonetic/NOTICE.txt` 记录了完整出处：

- 原始音频取自 https://www.ipachart.com/，该站点声明来自 Wikimedia Commons，
  贡献者含 Peter Isotalo、User:Denelson83、UCLA Phonetics Lab Archive 2003、
  User:Halibutt、User:Pmx、User:Octane。
- 授权是 free / copyleft（多为 CC BY-SA），本目录是衍生物，按原条款提供并保留署名。

处理方式：

- 全部重采样为 44100 Hz / 单声道 / 48 kbps mp3。
- 单元音直接转码；双元音由两个单元音片段以 60 ms 交叉淡入淡出拼接。
- 辅音的原始片段含两次发音，按第一个内部静音起点 + 80 ms 截断，只留第一段，
  使长度与元音（约 0.5–0.7s）接近。

## 验证

用反射探针（真实渲染，不 mock）验证：

1. 数据层：元音 20 / 辅音 24 / 合计 44；符号与音频名无重复；44 个 mp3 文件都在；
   每个音标都是 5 个非空例词。
2. 界面层：两个分组、方块数 20 / 24、例词区在音标表右侧。
3. 单击：展开 5 行 15 格；换音标内容跟着换；重复点同一个不清空。
4. 双击：第一次点击选中该音标，第二次（`ClickCount=2`）不改变已展开的例词。
5. 浅色 / 深色主题各跑一遍，并各出一张 2 倍截图确认可读。

音频的实际听感无法在探针里断言 —— 只验证到「解析到正确的文件且文件存在」。

## 不做的事

- 不做音标的分类筛选、搜索、播放全部。
- 不把 44 个方块做成可缩放 / 跟随字号设置：方块尺寸和音标字号是固定的，
  这是张固定版式的对照表，不是正文。
- 不缓存 / 预加载音频，双击时现播。
- 不处理窗口开着时用户切换主题的情况（与其它窗口一致，重开生效）。
