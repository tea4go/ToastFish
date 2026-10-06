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
