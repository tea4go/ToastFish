using ToastFish.Model.Notify;

namespace ToastFish.View.Notify
{
    /// <summary>
    /// 选择题。服务三种题目：选择题（4 选 1）、翻译（3 选 1）、平假名/片假名（3 选 1）。
    /// </summary>
    public class ChoiceWindow : NotifyWindowBase
    {
        private ChoiceWindow()
        {
            // 异常关闭时按「答错」处理，与原 OnActivated 解析失败时的 -1 语义一致
            DefaultResult = -1;
            Closable = true;
        }

        public static void ShowChoice(
            string title,
            string question,
            params (string Text, int Result)[] options)
        {
            OnUi(() =>
            {
                var window = new ChoiceWindow();
                window.Build(title, question, options);
                window.ShowAsCurrent();
            });
        }

        private void Build(string title, string question, (string Text, int Result)[] options)
        {
            AddLine(title, NotifyTheme.StatusSize, NotifyTheme.Muted);
            AddLine(question, NotifyTheme.MeaningSize, NotifyTheme.Foreground, 4);
            SetButtonsStacked(options);
        }
    }
}
