using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ToastFish.Model.Ai;
using ToastFish.Model.Notify;
using ToastFish.Model.Speech;
using ToastFish.Model.SqliteControl;
using ToastFish.View.Notify;

namespace ToastFish.View
{
    /// <summary>
    /// 翻译窗口：上半部分是原文输入框，右侧竖排「翻译」「播放」两个按钮；
    /// 下半部分显示译文。整窗口随基准字号等比缩放，配色跟随明暗主题。
    /// </summary>
    public partial class TranslationWindow : Window
    {
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
            StyleBox(OutputBox);

            ActionPanel.Margin = new Thickness(S(10), 0, 0, 0);
            StyleButton(TranslateButton);
            StyleButton(PlayButton);
            TranslateButton.Click += Translate_Click;
            PlayButton.Click += Play_Click;

            SplitLine.BorderBrush = NotifyTheme.Border;
            SplitLine.Margin = new Thickness(0, S(12), 0, S(12));
        }

        /// <summary>输入框与译文框统一外观：主题底色、主题边框、主题字色。</summary>
        private void StyleBox(TextBox box)
        {
            box.Background = NotifyTheme.ButtonBackground;
            box.Foreground = NotifyTheme.Foreground;
            box.BorderBrush = NotifyTheme.Border;
            box.FontFamily = NotifyTheme.Font;
            box.FontSize = S(15);
            box.Padding = new Thickness(S(8));
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
            OutputBox.Text = "翻译中…";
            try
            {
                OutputBox.Text = await AiTranslator.TranslateAsync(text, prompt);
            }
            catch (Exception ex)
            {
                // 详细日志由 AiTranslator 统一记录，这里只负责在界面上提示
                OutputBox.Text = "翻译失败：" + ex.Message;
            }
            finally
            {
                TranslateButton.IsEnabled = true;
            }
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
