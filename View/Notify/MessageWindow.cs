using ToastFish.Model.Notify;

namespace ToastFish.View.Notify
{
    /// <summary>
    /// 纯文本提示，无按钮，到点自动消失。用于 PushMessage 与答错提示。
    /// </summary>
    public class MessageWindow : NotifyWindowBase
    {
        private MessageWindow()
        {
            AutoClose = true;
        }

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
    }
}
