using System;
using System.Collections.Generic;
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
        /// onReplay 非 null 时，音标行可点击重播发音，不关窗、不回传结果。
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
                window.BuildCompact(word, phonetic, bodyLines, statusLine, onReplay, buttons);
                window.ShowAsCurrent();
            });
        }

        /// <summary>
        /// 大字分层：单词 / 音标 / 释义整块居中，下面用分隔线隔出例句段，
        /// 按钮排成两列，状态行放在最底部。
        /// 英语卡片专用，日语 / 五十音 / 自定义仍走 ShowCard。
        /// onSpeakSentence 非 null 时例句行可双击朗读。
        /// </summary>
        public static void ShowLayeredCard(
            string word,
            string phonetic,
            string[] bodyLines,
            string statusLine,
            Action onReplay,
            Action<string> onSpeakSentence,
            params (string Text, int Result)[] buttons)
        {
            OnUi(() =>
            {
                var window = new WordCardWindow();
                window.BuildLayered(word, phonetic, bodyLines, statusLine, onReplay, onSpeakSentence, buttons);
                window.ShowAsCurrent();
            });
        }

        private void BuildCompact(
            string word,
            string phonetic,
            string[] bodyLines,
            string statusLine,
            Action onReplay,
            (string Text, int Result)[] buttons)
        {
            var wordText = new TextBlock
            {
                FontSize = NotifyTheme.WordSize,
                FontFamily = NotifyTheme.Font,
                Foreground = NotifyTheme.Foreground,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = NotifyTheme.CardWidth,
                VerticalAlignment = VerticalAlignment.Bottom
            };
            wordText.Inlines.Add(new Run(word));
            MakeCopyable(wordText, word);

            if (string.IsNullOrEmpty(phonetic))
            {
                Root.Children.Add(wordText);
            }
            else
            {
                // 音标跟在单词后面同一行，按底部对齐，点它播放发音
                var head = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    MaxWidth = NotifyTheme.CardWidth
                };
                TextBlock phoneticText = PhoneticBlock(phonetic, onReplay);
                phoneticText.Margin = new Thickness(10, 0, 0, 0);
                phoneticText.VerticalAlignment = VerticalAlignment.Bottom;
                head.Children.Add(wordText);
                head.Children.Add(phoneticText);
                Root.Children.Add(head);
            }

            if (bodyLines != null)
            {
                foreach (string line in bodyLines)
                {
                    if (string.IsNullOrEmpty(line))
                        continue;
                    AddLine(line, NotifyTheme.SentenceSize, NotifyTheme.Foreground, 4, copyable: true);
                }
            }

            if (!string.IsNullOrEmpty(statusLine))
                AddLine(statusLine, NotifyTheme.StatusSize, NotifyTheme.Muted, 8);

            SetButtons(buttons);
        }

        /// <summary>大字分层布局。上半部分（单词 / 音标 / 释义）整块居中，例句段用分隔线隔开，按钮两列，状态行压在最底部。</summary>
        private void BuildLayered(
            string word,
            string phonetic,
            string[] bodyLines,
            string statusLine,
            Action onReplay,
            Action<string> onSpeakSentence,
            (string Text, int Result)[] buttons)
        {
            var wordText = new TextBlock
            {
                FontSize = NotifyTheme.LayeredWordSize,
                FontFamily = NotifyTheme.Font,
                Foreground = NotifyTheme.Foreground,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                MaxWidth = NotifyTheme.CardWidth,
                VerticalAlignment = VerticalAlignment.Center
            };
            wordText.Inlines.Add(new Run(word));
            MakeCopyable(wordText, word);
            Root.Children.Add(wordText);

            if (!string.IsNullOrEmpty(phonetic))
            {
                TextBlock phoneticLine = PhoneticBlock(phonetic, onReplay);
                phoneticLine.TextAlignment = TextAlignment.Center;
                phoneticLine.Margin = new Thickness(0, 4, 0, 0);
                Root.Children.Add(phoneticLine);
            }

            // 首行是释义，与单词、音标同属上半部分，一起居中，字号比例句大一号
            if (bodyLines != null && bodyLines.Length > 0 && !string.IsNullOrEmpty(bodyLines[0]))
            {
                TextBlock meaning = AddLine(bodyLines[0], NotifyTheme.MeaningSize, NotifyTheme.Foreground, 6, copyable: true);
                meaning.TextAlignment = TextAlignment.Center;
            }

            // 例句段整段可能为空（该词既无例句也无短语），这时一条分隔线都不留，免得两条贴在一起
            var sentences = new List<string>();
            if (bodyLines != null)
            {
                for (int i = 1; i < bodyLines.Length; i++)
                {
                    if (!string.IsNullOrEmpty(bodyLines[i]))
                        sentences.Add(bodyLines[i]);
                }
            }

            if (sentences.Count > 0)
            {
                AddSeparator();
                foreach (string line in sentences)
                {
                    TextBlock sentence = AddLine(line, NotifyTheme.SentenceSize, NotifyTheme.Foreground, 0, copyable: true);
                    sentence.TextAlignment = TextAlignment.Center;
                    MakeSpeakable(sentence, onSpeakSentence);
                }
                AddSeparator();
            }

            SetButtonsTwoColumns(buttons);

            if (!string.IsNullOrEmpty(statusLine))
            {
                TextBlock status = AddLine(statusLine, NotifyTheme.StatusSize, NotifyTheme.Muted, 10);
                status.TextAlignment = TextAlignment.Center;
            }
        }

        /// <summary>分层卡片里分隔各段的细横线，取卡片边框色。</summary>
        private void AddSeparator()
        {
            Root.Children.Add(new Rectangle
            {
                Height = 1,
                Width = NotifyTheme.CardWidth,
                Fill = NotifyTheme.Border,
                Margin = new Thickness(0, 10, 0, 10)
            });
        }

        /// <summary>音标文字块。onReplay 非 null 时点它播放发音。</summary>
        private static TextBlock PhoneticBlock(string phonetic, Action onReplay)
        {
            var block = new TextBlock
            {
                Text = phonetic,
                FontSize = NotifyTheme.PhoneticSize,
                FontFamily = NotifyTheme.Font,
                Foreground = NotifyTheme.Muted,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = NotifyTheme.CardWidth
            };
            if (onReplay != null)
            {
                block.Cursor = Cursors.Hand;
                block.ToolTip = "点击播放发音";
                // Transparent 参与命中测试而 null 不参与，设成 Transparent 后整行空白也能点中
                block.Background = Brushes.Transparent;
                block.MouseLeftButtonUp += (s, e) => onReplay();
            }
            return block;
        }
    }
}
