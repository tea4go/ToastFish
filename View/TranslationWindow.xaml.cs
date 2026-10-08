using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
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
            InputBox.Text = DefaultInput;
            ShowOutput(DefaultOutput);

            ActionPanel.Margin = new Thickness(S(10), 0, 0, 0);
            StyleButton(TranslateButton);
            StyleButton(PlayButton);
            StyleButton(ConfigButton);
            TranslateButton.Click += Translate_Click;
            PlayButton.Click += Play_Click;
            ConfigButton.Click += Config_Click;

            // 上半部分只占右侧三个按钮的总高（3×34 + 2×8），要更高就拖分隔线
            TopRow.Height = new GridLength(S(118));

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
            document.PagePadding = new Thickness(S(8));
            OutputBox.Document = document;
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

        /// <summary>
        /// 输入框里选中了文本就只取选中部分，否则取整框。翻译和播放共用这条规则。
        /// </summary>
        private string TextToProcess()
        {
            return InputBox.SelectionLength > 0 ? InputBox.SelectedText : InputBox.Text;
        }

        /// <summary>
        /// 把输入框里的原文交给 AI 翻译，译文写进下半部分的输出框。
        /// 有选中文本时只翻选中部分，并改用单词提示词（词典式释义），否则翻整框、用整句提示词。
        /// </summary>
        private async void Translate_Click(object sender, RoutedEventArgs e)
        {
            bool hasSelection = InputBox.SelectionLength > 0;
            string text = TextToProcess();
            if (string.IsNullOrWhiteSpace(text))
                return;

            string prompt = hasSelection ? Select.AI_PROMPT_WORD : Select.AI_PROMPT_SENTENCE;
            TranslateButton.IsEnabled = false;
            ShowOutput("翻译中…");
            try
            {
                ShowOutput(await AiTranslator.TranslateAsync(text, prompt));
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

        /// <summary>朗读原文，有选中文本时只读选中部分。空内容不发声。播放是阻塞的，放到后台线程。</summary>
        private void Play_Click(object sender, RoutedEventArgs e)
        {
            string text = TextToProcess();
            if (string.IsNullOrWhiteSpace(text))
                return;
            Task.Run(() => SpeechReader.Create(text).SpeakAsync(text));
        }
    }
}
