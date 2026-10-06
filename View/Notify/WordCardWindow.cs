using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
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

        /// <summary>
        /// onReplay 非 null 时，单词行右侧会出现播放图标，点击只重播发音，不关窗、不回传结果。
        /// </summary>
        public static void ShowCard(
            string word,
            string phonetic,
            string[] bodyLines,
            string statusLine,
            Action onReplay,
            params (string Text, int Result)[] buttons)
        {
            OnUi(() =>
            {
                var window = new WordCardWindow();
                window.Build(word, phonetic, bodyLines, statusLine, onReplay, buttons);
                window.ShowAsCurrent();
            });
        }

        private void Build(
            string word,
            string phonetic,
            string[] bodyLines,
            string statusLine,
            Action onReplay,
            (string Text, int Result)[] buttons)
        {
            TextBlock head = AddLine(word, NotifyTheme.WordSize, NotifyTheme.Foreground);
            if (!string.IsNullOrEmpty(phonetic))
            {
                head.Inlines.Add(new Run("  "));
                head.Inlines.Add(new Run(phonetic)
                {
                    FontSize = NotifyTheme.PhoneticSize,
                    Foreground = NotifyTheme.Muted
                });
            }
            if (onReplay != null)
                head.Inlines.Add(ReplayIcon(onReplay));

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

        /// <summary>播放图标。几何图形而非字符，避免用户把字体换成不含 ▶ 的字库后显示成方框。</summary>
        private static InlineUIContainer ReplayIcon(Action onReplay)
        {
            double size = NotifyTheme.ButtonSize * 0.85;
            var icon = new Path
            {
                Data = Geometry.Parse("M 0,0 L 8,5 L 0,10 Z"),
                Fill = NotifyTheme.ButtonForeground,
                Stretch = Stretch.Uniform,
                Width = size * 0.8,
                Height = size
            };
            var button = new Button
            {
                Content = icon,
                FontSize = NotifyTheme.ButtonSize,
                FontFamily = NotifyTheme.Font,
                Padding = new Thickness(7, 3, 7, 3),
                Background = NotifyTheme.ButtonBackground,
                BorderBrush = NotifyTheme.ButtonBorder,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = "播放发音"
            };
            button.Click += (s, e) => onReplay();
            return new InlineUIContainer(button) { BaselineAlignment = BaselineAlignment.Center };
        }
    }
}
