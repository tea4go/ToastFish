using ToastFish.Model.Notify;

namespace ToastFish.View.Notify
{
    /// <summary>
    /// 纯文本提示，无按钮，到点自动消失。用于 PushMessage 与答错提示。
    /// </summary>
    public class MessageWindow : NotifyWindowBase
    {
        public static void ShowMessage(string text, int autoCloseMs = 4000)
        {
            OnUi(() =>
            {
                var window = new MessageWindow();
                window.AddLine(text, NotifyTheme.MeaningSize, NotifyTheme.Foreground);
                window.ShowAsCurrent();
                window.StartAutoClose(autoCloseMs);
            });
        }

        /// <summary>
        /// 状态查询用的提示：不抢 Current（不打断正在进行的测试），8 秒后自动消失。
        /// 比默认 4 秒长一些，因为状态有多行文字要读。
        /// </summary>
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
    }
}
