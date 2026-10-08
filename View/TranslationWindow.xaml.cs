using System;
using System.Globalization;
using System.Speech.Synthesis;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ToastFish.Model.Notify;
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

        /// <summary>朗读输入框里的原文。空内容不发声。</summary>
        private void Play_Click(object sender, RoutedEventArgs e)
        {
            string text = InputBox.Text;
            if (string.IsNullOrWhiteSpace(text))
                return;
            Task.Run(() => Speak(text));
        }

        /// <summary>
        /// 朗读一段文本。合成语音默认语速偏快，英文放慢两档更好跟读；
        /// 系统默认语音往往是中文，用它念英文会带中文腔调，所以英文要显式挑一个英文语音。
        /// 播放是阻塞的，调用方已经放到后台线程。
        /// </summary>
        private static void Speak(string text)
        {
            SpeechSynthesizer synth = new SpeechSynthesizer();
            if (IsMostlyLatin(text))
            {
                synth.Rate = -2;
                var english = synth.GetInstalledVoices(new CultureInfo("en-US"));
                if (english.Count > 0)
                    synth.SelectVoice(english[0].VoiceInfo.Name);
            }
            synth.SpeakAsync(text);
        }

        /// <summary>字母里拉丁字母占多数即认为是英文，数字标点不计入。</summary>
        private static bool IsMostlyLatin(string text)
        {
            int latin = 0, other = 0;
            foreach (char c in text)
            {
                if (!char.IsLetter(c))
                    continue;
                if (c < 128)
                    latin++;
                else
                    other++;
            }
            return latin > other;
        }
    }
}
