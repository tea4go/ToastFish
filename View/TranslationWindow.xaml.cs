using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ToastFish.Model.Ai;
using ToastFish.Model.Markdown;
using ToastFish.Model.Notify;
using ToastFish.Model.Speech;
using ToastFish.Model.SqliteControl;
using ToastFish.View.Notify;

namespace ToastFish.View
{
    /// <summary>
    /// 翻译窗口：上半部分是原文输入框，右侧竖排「翻译」「播放」「配置」三个按钮；
    /// 下半部分把译文按 Markdown 渲染出来（单词释义那套提示词返回的就是 Markdown），
    /// 中间的分隔线可拖动调整上下比例。整窗口随基准字号等比缩放，配色跟随明暗主题。
    /// </summary>
    public partial class TranslationWindow : Window
    {
        /// <summary>输入框打开时预填的默认文本。</summary>
        private const string DefaultInput =
            "Share Engine Windows SDK (documented version 1.0.0.300) for both sending and receiving  files. Personal, non-commercial project.";

        /// <summary>
        /// 译文区打开时预填的示例。挑一份把各级标题、代码块、行内代码、引用、表格、
        /// 链接都覆盖到的 Markdown，一开窗就能看到各部分各自的配色，不必先去翻一次翻译。
        /// </summary>
        private const string DefaultOutput = @"Markdown测试文档

# 一级标题

## 二级标题

### 三级标题

#### 四级标题

##### 五级标题

###### 六级标题

### 代码块

以下是Python代码

```python
@requires_authorization
def somefunc(param1='', param2=0):
    '''A docstring'''
    if param1 > param2: # interesting
        print 'Greater'
    return (param2 - param1 + 1) or None
class SomeClass:
    pass
>>> message = '''interpreter
... prompt'''
```

此代码只是测试使用。

### 标记

`标记`

### 引用块

> Markdown 是一种轻量级标记语言，它允许人们使用易读易写的纯文本格式编写文档，然后转换成格式丰富的 HTML 页面。

### 表格

| Item     | Value    | Qty |
| :-------- | --------: | :---: |
| Computer | 1600 USD | 5   |
| Phone    | 12 USD   | 12  |
| Pipe     | 1 USD    | 234 |

### 链接

[链接](http://www.example.com)";

        /// <summary>已经打开的窗口。同一时刻只留一个，重复点菜单只把它提到前面。</summary>
        private static TranslationWindow _open;

        /// <summary>
        /// 「播放」在本地词库里没查到词条时，占在译文位置的文案。查到了就放词条本身，
        /// 所以这句只在词库没收这个词（或选中的是一整句）时出现。
        /// 要说清楚是「本地词库没有」而不是「功能没反应」，否则用户只看到译文区没变化，
        /// 会以为播放坏了；顺带指出下一步该按哪个按钮。
        /// </summary>
        private const string PlayOnlyHint = "（六级完整词汇里没查到，只播放了发音；要看释义请点「翻译」）";

        /// <summary>一次翻译的历史。</summary>
        private class TranslationEntry
        {
            /// <summary>这次处理的文本：选中部分，或没选中时的整个输入框。做标签摘要用。</summary>
            public string Text;

            /// <summary>
            /// 记这条时输入框的完整内容。只查了其中一个词时，光记那个词会把上下文丢掉，
            /// 点回这条历史就再也找不回原来的整段原文了。
            /// </summary>
            public string Input;

            /// <summary>译文；只播放过的那条存一句说明。</summary>
            public string Output;
        }

        /// <summary>本次窗口打开期间的翻译历史，关窗即丢。</summary>
        private readonly List<TranslationEntry> _history = new List<TranslationEntry>();

        /// <summary>
        /// 本地词库连接。第一次查词时才建，之后整个窗口复用同一个连接——
        /// 每次播放都 new 一个会攒下一堆没人关的 SQLite 连接。
        /// </summary>
        private Select _dictionary;

        /// <summary>当前选中的历史下标，-1 表示还没有任何历史。</summary>
        private int _historyIndex = -1;

        /// <summary>流式翻译上一次重渲染译文的时刻，用来限流。</summary>
        private DateTime _lastStreamPaint = DateTime.MinValue;

        /// <summary>流式翻译两次重渲染之间至少隔这么久（毫秒）。每个 token 都重渲染会白白刷屏。</summary>
        private const int StreamPaintMs = 100;

        /// <summary>
        /// 最近一次选中是在哪个框里，true 表示原文框。两个框都留着选中时按它取：
        /// 选中不会因为焦点移走而消失，光按固定优先级取的话，原文框里一个早先选中的
        /// 单词会一直把译文区里刚选的那段顶掉。
        /// </summary>
        private bool _inputPickedLast = true;

        // 整窗口随基准字号等比缩放，基准 15 时比例为 1。
        private readonly double _scale = NotifyTheme.BaseSize / 15.0;

        private double S(double value)
        {
            return value * _scale;
        }

        public static void ShowWindow()
        {
            if (_open != null)
            {
                _open.Activate();
                return;
            }
            _open = new TranslationWindow();
            _open.Closed += (s, e) => _open = null;
            _open.Show();
        }

        private TranslationWindow()
        {
            InitializeComponent();
            NotifyTheme.Apply(this);

            // 窗口先按比例铺开，再夹到屏幕工作区内，字号调到 28 时也不会超出屏幕
            Rect work = SystemParameters.WorkArea;
            Width = Math.Min(S(560), work.Width * 0.92);
            Height = Math.Min(S(460), work.Height * 0.92);
            MinWidth = Math.Min(S(420), Width);
            MinHeight = Math.Min(S(320), Height);

            RootGrid.Margin = new Thickness(S(14));
            Background = NotifyTheme.Background;

            StyleBox(InputBox);
            StyleOutput();
            // 选中文本后焦点常会挪到「翻译」按钮或另一个框上，系统默认会把选中高亮藏掉
            InputBox.IsInactiveSelectionHighlightEnabled = true;
            // 在哪个框里选字，标记就挪到哪个框（点进去才能选，所以认键盘焦点足够）
            InputBox.GotKeyboardFocus += (s, e) => _inputPickedLast = true;
            OutputBox.GotKeyboardFocus += (s, e) => _inputPickedLast = false;
            StyleHistoryBar();
            InputBox.Text = DefaultInput;
            ShowOutput(DefaultOutput);
            RebuildTabs();

            ActionPanel.Margin = new Thickness(S(10), 0, 0, 0);
            StyleButton(TranslateButton);
            StyleButton(PlayButton);
            StyleButton(ConfigButton);
            TranslateButton.Click += Translate_Click;
            PlayButton.Click += Play_Click;
            ConfigButton.Click += Config_Click;

            // 上半部分 = 右侧三个按钮的总高（3×34 + 2×8）+ 标签栏一行（8 间距 + 26 高），
            // 输入框仍与第三个按钮等高，要更高就拖分隔线
            TopRow.Height = new GridLength(S(118 + 34));

            SplitLine.BorderBrush = NotifyTheme.Border;
            // 拖动区做高一点好抓，上下各留 8 让分隔线的间距和原来一致
            SplitGrip.Height = S(9);
            SplitGrip.Margin = new Thickness(0, S(8), 0, S(8));
        }

        /// <summary>输入框外观：主题底色、主题边框、主题字色。</summary>
        private void StyleBox(TextBox box)
        {
            box.Background = NotifyTheme.ButtonBackground;
            box.Foreground = NotifyTheme.Foreground;
            box.BorderBrush = NotifyTheme.Border;
            box.FontFamily = NotifyTheme.Font;
            box.FontSize = S(15);
            box.Padding = new Thickness(S(8));
        }

        /// <summary>译文区外观：跟输入框同底色同边框，内容每次翻译时换成新的 FlowDocument。</summary>
        private void StyleOutput()
        {
            OutputBox.Background = NotifyTheme.ButtonBackground;
            OutputBox.Foreground = NotifyTheme.Foreground;
            OutputBox.BorderBrush = NotifyTheme.Border;
        }

        /// <summary>
        /// 把文本当 Markdown 渲染进译文区。「翻译中…」「翻译失败：…」这类提示也走这里，
        /// 它们没有 Markdown 标记，渲染出来就是普通段落。
        /// </summary>
        private void ShowOutput(string text)
        {
            FlowDocument document;
            try
            {
                document = MarkdownRenderer.Render(text, S(15));
            }
            catch (Exception)
            {
                // 模型输出的 Markdown 千奇百怪，渲染失败就退回原样显示，别让整个窗口崩掉
                document = new FlowDocument(new Paragraph(new Run(text)));
            }
            Install(document);
        }

        /// <summary>
        /// 流式翻译每收到一段就调一次，把「目前已收到的全文」铺进译文区。
        /// 这一段是纯文本、不解析 Markdown：每收一段都解析一遍既费时，又会在半截语法上抖
        /// （表格没写完、代码围栏没闭合）。整段回来后再由 Translate_Click 用 ShowOutput
        /// 渲染成 Markdown。限流是因为整篇重铺文档比追加贵，100ms 一次已经够「实时」。
        /// </summary>
        private void ShowStreaming(string text)
        {
            DateTime now = DateTime.UtcNow;
            if ((now - _lastStreamPaint).TotalMilliseconds < StreamPaintMs)
                return;
            _lastStreamPaint = now;
            Install(MarkdownRenderer.RenderPlain(text, S(15)));
        }

        /// <summary>装进译文区。换文档会重建内部的 selection，这个开关得在装好文档之后再置一次才推得到它身上。</summary>
        private void Install(FlowDocument document)
        {
            document.PagePadding = new Thickness(S(8));
            OutputBox.Document = document;
            OutputBox.IsInactiveSelectionHighlightEnabled = true;
        }

        /// <summary>
        /// 右侧竖排按钮：等宽等高，复用卡片的按钮模板走主题色，
        /// 免得暗色主题下鼠标悬停被系统默认模板刷成浅蓝。
        /// </summary>
        private void StyleButton(Button button)
        {
            button.Width = S(84);
            button.Height = S(34);
            button.Margin = new Thickness(0, 0, 0, S(8));
            button.FontFamily = NotifyTheme.Font;
            button.FontSize = S(14);
            button.Background = NotifyTheme.ButtonBackground;
            button.Foreground = NotifyTheme.ButtonForeground;
            button.BorderBrush = NotifyTheme.ButtonBorder;
            button.Cursor = Cursors.Hand;
            button.Template = NotifyWindowBase.CreateButtonTemplate();
        }

        /// <summary>译文区里选中的文本，没选中时为空串。</summary>
        private string SelectedOutputText()
        {
            TextSelection selection = OutputBox.Selection;
            return selection == null ? "" : selection.Text;
        }

        /// <summary>两个框里任意一个有选中文本。有选中就按「查词」处理，用单词提示词。</summary>
        private bool HasSelectedText()
        {
            return InputBox.SelectionLength > 0 || !string.IsNullOrEmpty(SelectedOutputText());
        }

        /// <summary>
        /// 要处理的文本，按优先级取：原文框里选中的 &gt; 译文区里选中的 &gt; 整个原文框。
        /// 两个框都留着选中时，取最近一次选中的那个——选中不随失焦消失，
        /// 固定让原文框优先的话，译文区里刚选的一段永远轮不上。
        /// 翻译和播放共用这条规则。
        /// </summary>
        private string TextToProcess()
        {
            bool inputPicked = InputBox.SelectionLength > 0;
            string selectedOutput = SelectedOutputText();
            bool outputPicked = !string.IsNullOrEmpty(selectedOutput);

            if (inputPicked && outputPicked)
                return _inputPickedLast ? InputBox.SelectedText : selectedOutput;
            if (inputPicked)
                return InputBox.SelectedText;
            if (outputPicked)
                return selectedOutput;
            return InputBox.Text;
        }

        /// <summary>
        /// 把输入框里的原文交给 AI 翻译，译文写进下半部分的输出框。
        /// 有选中文本时只翻选中部分，并改用单词提示词（词典式释义），否则翻整框、用整句提示词。
        /// 译文是流式来的：模型吐一段就显示一段，不必等整段生成完。
        /// </summary>
        private async void Translate_Click(object sender, RoutedEventArgs e)
        {
            bool hasSelection = HasSelectedText();
            string text = TextToProcess();
            if (string.IsNullOrWhiteSpace(text))
                return;

            string prompt = hasSelection ? Select.AI_PROMPT_WORD : Select.AI_PROMPT_SENTENCE;
            TranslateButton.IsEnabled = false;
            ShowOutput("翻译中…");
            try
            {
                string result = await AiTranslator.TranslateAsync(text, prompt, ShowStreaming);
                ShowOutput(result);
                // 只有翻成功才记历史，「翻译中…」「翻译失败」都不算
                PushHistory(text, result);
            }
            catch (Exception ex)
            {
                // 详细日志由 AiTranslator 统一记录，这里只负责在界面上提示
                ShowOutput("翻译失败：" + ex.Message);
            }
            finally
            {
                TranslateButton.IsEnabled = true;
            }
        }

        /// <summary>打开设置窗口，直接落在「播放配置」页签。</summary>
        private void Config_Click(object sender, RoutedEventArgs e)
        {
            new SettingsWindow(SettingsWindow.PlaybackTab) { Owner = this }.ShowDialog();
        }

        /// <summary>
        /// 朗读原文，有选中文本时只读选中部分。空内容不发声。播放是阻塞的，放到后台线程。
        /// 同时去本地词库找词条：查得到就直接显示，省掉一次联网翻译。
        /// 查不到也要在译文区给个交代——只往历史里塞一条标签、译文区一动不动的话，
        /// 用户点完播放看不到任何反馈，会以为功能坏了。
        /// 播放也记一条历史：查词时经常先听发音再决定要不要翻，不记就找不回来了。
        /// </summary>
        private void Play_Click(object sender, RoutedEventArgs e)
        {
            string text = TextToProcess();
            if (string.IsNullOrWhiteSpace(text))
                return;

            string entry = LocalEntry(text);
            // 译文区现在摆的就是这条的释义（或译文），说明用户已经看过了，只是想再听一遍
            bool alreadyShowing = _historyIndex >= 0
                && _history[_historyIndex].Text == text
                && _history[_historyIndex].Output != PlayOnlyHint;

            // 反复按播放多半只是想多听几遍，已经在当前这条上就别再刷一个一样的标签
            if (_historyIndex < 0 || _history[_historyIndex].Text != text)
                PushHistory(text, entry ?? PlayOnlyHint);

            if (entry != null)
                ShowOutput(entry);
            else if (!alreadyShowing)
                ShowOutput(PlayOnlyHint);

            Task.Run(() => SpeechReader.Create(text).SpeakAsync(text));
        }

        /// <summary>
        /// 去六级完整词汇里查这个词，查到就返回渲染用的词条 Markdown，查不到返回 null。
        /// 词库读不出来时也返回 null：查词失败不该妨碍发声，退回「只播放」就是了。
        /// </summary>
        private string LocalEntry(string text)
        {
            try
            {
                if (_dictionary == null)
                    _dictionary = new Select();
                Word word = _dictionary.LookupCet6Word(text);
                return word == null ? null : DictionaryEntry(word);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 把词库里的一个词条拼成 Markdown：词形、音标、词性释义、例句、短语。
        /// 音标按「发音类型」设置取美音或英音，跟背诵卡片保持一致。
        /// 行尾两个空格是 Markdown 的硬换行，少了的话「译：」会被并到上一行去。
        /// </summary>
        private static string DictionaryEntry(Word word)
        {
            var entry = new StringBuilder();
            entry.Append("**").Append(word.headWord).Append("**");

            string phone = Select.ENG_TYPE == 1 ? word.usPhone : word.ukPhone;
            if (!string.IsNullOrWhiteSpace(phone))
                entry.Append("  /").Append(phone.Trim()).Append("/");

            if (!string.IsNullOrWhiteSpace(word.tranCN))
            {
                entry.Append("\n\n");
                if (!string.IsNullOrWhiteSpace(word.pos))
                    entry.Append(word.pos.Trim()).Append(". ");
                entry.Append(word.tranCN.Trim());
            }

            if (!string.IsNullOrWhiteSpace(word.sentence))
            {
                entry.Append("\n\n例：").Append(word.sentence.Trim());
                if (!string.IsNullOrWhiteSpace(word.sentenceCN))
                    entry.Append("  \n译：").Append(word.sentenceCN.Trim());
            }

            if (!string.IsNullOrWhiteSpace(word.phrase))
            {
                entry.Append("\n\n短语：").Append(word.phrase.Trim());
                if (!string.IsNullOrWhiteSpace(word.phraseCN))
                    entry.Append("  \n").Append(word.phraseCN.Trim());
            }

            return entry.ToString();
        }

        /// <summary>历史标签栏外观：两端箭头、中间折行的标签区。</summary>
        private void StyleHistoryBar()
        {
            HistoryBar.MinHeight = S(26);
            HistoryBar.Margin = new Thickness(0, S(8), 0, 0);

            PrevButton.Child = Chevron(false);
            NextButton.Child = Chevron(true);
            StyleNav(PrevButton);
            StyleNav(NextButton);
            PrevButton.MouseLeftButtonUp += (s, e) => StepHistory(-1);
            NextButton.MouseLeftButtonUp += (s, e) => StepHistory(1);

            TabsScroll.Background = Brushes.Transparent;
            TabsScroll.BorderThickness = new Thickness(0);
            TabsScroll.Padding = new Thickness(S(4), 0, S(4), 0);
            // 最多折两行（一行 26），再多就竖向滚动，免得把输入框挤没
            TabsScroll.MaxHeight = S(56);
        }

        /// <summary>左右箭头图标。用 Path 画 chevron，免得受用户自定义字体影响。</summary>
        private Path Chevron(bool forward)
        {
            return new Path
            {
                Data = Geometry.Parse(forward ? "M 0,0 L 4.5,5.5 L 0,11" : "M 4.5,0 L 0,5.5 L 4.5,11"),
                Stroke = NotifyTheme.Muted,
                StrokeThickness = 1.4,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Width = S(5),
                Height = S(12),
                Stretch = Stretch.Uniform
            };
        }

        private void StyleNav(Border button)
        {
            button.Padding = new Thickness(S(5), 0, S(5), 0);
            button.Background = Brushes.Transparent;
            // 撑满整行：标签折行变高时箭头跟着一起变高，和标签区等高
            button.VerticalAlignment = VerticalAlignment.Stretch;
        }

        /// <summary>到头时把箭头置灰并停掉点击。</summary>
        private void UpdateNavButtons()
        {
            SetNavEnabled(PrevButton, _historyIndex > 0);
            SetNavEnabled(NextButton, _historyIndex >= 0 && _historyIndex < _history.Count - 1);
        }

        private static void SetNavEnabled(Border button, bool enabled)
        {
            button.IsEnabled = enabled;
            button.Opacity = enabled ? 1 : 0.3;
            button.Cursor = enabled ? Cursors.Hand : Cursors.Arrow;
        }

        /// <summary>记一条历史并选中它。总是追加到末尾，右边已有的页签都留着。</summary>
        private void PushHistory(string text, string output)
        {
            _history.Add(new TranslationEntry
            {
                Text = text,
                Input = InputBox.Text,
                Output = output
            });
            _historyIndex = _history.Count - 1;
            RebuildTabs();
        }

        /// <summary>切到第 index 条历史：输入框恢复成当时的样子，译文重新渲染。</summary>
        private void SelectHistory(int index)
        {
            if (index < 0 || index >= _history.Count || index == _historyIndex)
                return;
            _historyIndex = index;
            ShowOutput(_history[index].Output);
            RestoreInput(_history[index]);
            RebuildTabs();
        }

        /// <summary>
        /// 输入框恢复成记这条时的样子：填回完整原文，并把当时处理的那段重新选中。
        /// 当时处理的是译文区里选中的一段时，那段不在原文里，就把输入框换成这段并全选——
        /// 选中即代表「按词处理」，再按翻译/播放才还针对它，而不是退化成整句。
        /// </summary>
        private void RestoreInput(TranslationEntry entry)
        {
            if (string.IsNullOrEmpty(entry.Text) || entry.Text == entry.Input)
            {
                InputBox.Text = entry.Input;
                InputBox.Select(InputBox.Text.Length, 0);
                return;
            }

            int at = entry.Input.IndexOf(entry.Text, StringComparison.Ordinal);
            if (at >= 0)
            {
                InputBox.Text = entry.Input;
                InputBox.Select(at, entry.Text.Length);
            }
            else
            {
                InputBox.Text = entry.Text;
                InputBox.SelectAll();
            }
        }

        /// <summary>在当前历史里前后移动。</summary>
        private void StepHistory(int delta)
        {
            SelectHistory(_historyIndex + delta);
        }

        /// <summary>删掉一条历史。删的是当前条就顺势显示相邻的一条，删光则保持画面不动。</summary>
        private void RemoveHistory(int index)
        {
            if (index < 0 || index >= _history.Count)
                return;

            bool removedCurrent = index == _historyIndex;
            _history.RemoveAt(index);
            if (_historyIndex > index)
                _historyIndex--;
            else if (_historyIndex == index)
                _historyIndex = Math.Min(_historyIndex, _history.Count - 1);
            RebuildTabs();

            if (removedCurrent && _historyIndex >= 0)
            {
                ShowOutput(_history[_historyIndex].Output);
                RestoreInput(_history[_historyIndex]);
            }
        }

        /// <summary>按当前历史重建标签。条数不多，整体重建比增量维护省事。</summary>
        private void RebuildTabs()
        {
            TabsPanel.Children.Clear();
            for (int i = 0; i < _history.Count; i++)
                TabsPanel.Children.Add(CreateTab(i));
            UpdateNavButtons();

            // 刚加进去的元素还没有布局位置，等布局完再滚进视野
            if (_historyIndex >= 0 && _historyIndex < TabsPanel.Children.Count)
            {
                FrameworkElement target = TabsPanel.Children[_historyIndex] as FrameworkElement;
                Dispatcher.BeginInvoke(new Action(() => target.BringIntoView()),
                    DispatcherPriority.Loaded);
            }
        }

        /// <summary>一个历史标签：原文摘要 + 右上角删除图标，选中的那个底色不同。</summary>
        private Border CreateTab(int index)
        {
            bool active = index == _historyIndex;

            var label = new TextBlock
            {
                Text = TabLabel(_history[index].Text),
                FontFamily = NotifyTheme.Font,
                FontSize = S(12),
                Foreground = active ? NotifyTheme.Foreground : NotifyTheme.Muted,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = S(110),
                // 右边给右上角的 × 让出位置，否则文字会拉伸到图标底下撞在一起
                Margin = new Thickness(0, 0, S(10), 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            var glyph = new Path
            {
                Data = Geometry.Parse("M 0,0 L 6,6 M 6,0 L 0,6"),
                Stroke = NotifyTheme.Muted,
                StrokeThickness = 1.2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Width = S(6),
                Height = S(6),
                Stretch = Stretch.Uniform
            };
            var close = new Border
            {
                Padding = new Thickness(S(2)),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                ToolTip = "删除这条记录",
                Child = glyph
            };
            close.MouseEnter += (s, e) => glyph.Stroke = NotifyTheme.Foreground;
            close.MouseLeave += (s, e) => glyph.Stroke = NotifyTheme.Muted;
            close.MouseLeftButtonUp += (s, e) =>
            {
                // 别让事件冒泡到标签上，否则删完又立刻切过去
                e.Handled = true;
                RemoveHistory(index);
            };

            var host = new Grid();
            host.Children.Add(label);
            host.Children.Add(close);

            var tab = new Border
            {
                Background = active ? NotifyTheme.ButtonBackground : Brushes.Transparent,
                BorderBrush = NotifyTheme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                // 右边留一点给 × 的可点区域，文字的让位交给 label 的 Margin
                Padding = new Thickness(S(7), S(2), S(4), S(2)),
                Margin = new Thickness(0, 0, S(4), S(4)),
                Cursor = Cursors.Hand,
                ToolTip = _history[index].Text,
                Child = host
            };
            tab.MouseLeftButtonUp += (s, e) =>
            {
                // 重听用记下来的文本，不靠原文框里的选中：从译文区选中的词原文框里根本没有，
                // 切回历史后不会有选中，按选中去播就会退化成整句。
                string text = _history[index].Text;
                SelectHistory(index);
                Task.Run(() => SpeechReader.Create(text).SpeakAsync(text));
            };
            return tab;
        }

        /// <summary>标签上显示的原文摘要：换行压成空格，过长交给 TextTrimming 省略。</summary>
        private static string TabLabel(string source)
        {
            return source.Replace('\r', ' ').Replace('\n', ' ').Trim();
        }
    }
}
