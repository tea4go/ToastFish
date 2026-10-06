using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ToastFish.Model.Notify;
using ToastFish.Model.Log;

namespace ToastFish.View.Notify
{
    /// <summary>
    /// 所有自绘通知窗口的外壳：定位、不抢焦点、置顶、主题、按钮行、结果回传。
    /// 子类只负责往 Root 里塞内容。
    /// </summary>
    public class NotifyWindowBase : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        /// <summary>当前正在显示的通知窗口。同一时刻只可能有一个。</summary>
        public static NotifyWindowBase Current { get; private set; }

        /// <summary>窗口被异常关闭时回传给等待方的值。</summary>
        protected int DefaultResult = 1;

        protected readonly StackPanel Root = new StackPanel();
        private readonly Border _shell = new Border();
        private readonly TaskCompletionSource<int> _tcs = new TaskCompletionSource<int>();
        private DispatcherTimer _timer;
        private bool _shown;

        public NotifyWindowBase()
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            AllowsTransparency = true;
            ShowActivated = false;
            SizeToContent = SizeToContent.WidthAndHeight;

            _shell.Padding = new Thickness(14, 12, 14, 12);
            _shell.CornerRadius = new CornerRadius(10);
            _shell.BorderThickness = new Thickness(1);
            _shell.Child = Root;
            Content = _shell;
        }

        /// <summary>后台线程等结果用。</summary>
        public Task<int> WaitAsync()
        {
            return _tcs.Task;
        }

        /// <summary>供快捷键回调用；与点按钮等效。</summary>
        public void SetResult(int value)
        {
            _tcs.TrySetResult(value);
        }

        /// <summary>把要在 UI 线程执行的建窗动作切过去。</summary>
        public static void OnUi(Action action)
        {
            Application app = Application.Current;
            if (app == null)
                return;
            if (app.Dispatcher.CheckAccess())
                action();
            else
                app.Dispatcher.Invoke(action);
        }

        /// <summary>套用主题、顶掉上一个窗口、显示并定位。</summary>
        protected void ShowAsCurrent()
        {
            NotifyTheme.Apply(this);
            _shell.Background = NotifyTheme.Background;
            _shell.BorderBrush = NotifyTheme.Border;

            // 同一时刻只留一个窗口：新窗口直接把上一个顶掉
            if (Current != null && Current != this)
                Current.Close();

            Current = this;
            try
            {
                Show();
                _shown = true;
            }
            catch (Exception ex)
            {
                // 显示环节的任何异常都不能拖垮等待中的背诵线程，直接按默认结果放行
                Logger.Write("通知窗口显示失败：" + ex);
                if (Current == this)
                    Current = null;
                _tcs.TrySetResult(DefaultResult);
            }
        }

        /// <summary>autoCloseMs &gt; 0 时定时自动关闭。</summary>
        protected void StartAutoClose(int autoCloseMs)
        {
            if (autoCloseMs <= 0)
                return;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(autoCloseMs) };
            _timer.Tick += (s, e) =>
            {
                _timer.Stop();
                if (_shown)
                    Close();
            };
            _timer.Start();
        }

        /// <summary>生成按钮行，点击即回传结果并关闭。</summary>
        protected void SetButtons(params (string Text, int Result)[] buttons)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 12, 0, 0)
            };
            foreach ((string text, int result) in buttons)
            {
                Button button = CreateButton(text, result);
                button.Margin = new Thickness(0, 0, 6, 0);
                row.Children.Add(button);
            }
            Root.Children.Add(row);
        }

        /// <summary>每行两个的按钮网格，按钮等宽铺满各自格子。奇数个时最后一格留空。</summary>
        protected void SetButtonsTwoColumns(params (string Text, int Result)[] buttons)
        {
            var grid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            for (int row = 0; row < (buttons.Length + 1) / 2; row++)
                grid.RowDefinitions.Add(new RowDefinition());

            for (int i = 0; i < buttons.Length; i++)
            {
                Button button = CreateButton(buttons[i].Text, buttons[i].Result);
                button.Margin = new Thickness(
                    i % 2 == 0 ? 0 : 3,
                    i < 2 ? 0 : 6,
                    i % 2 == 0 ? 3 : 0,
                    0);
                Grid.SetRow(button, i / 2);
                Grid.SetColumn(button, i % 2);
                grid.Children.Add(button);
            }
            Root.Children.Add(grid);
        }

        private Button CreateButton(string text, int result)
        {
            var button = new Button
            {
                Content = text,
                FontSize = NotifyTheme.ButtonSize,
                FontFamily = NotifyTheme.Font,
                Padding = new Thickness(10, 6, 10, 6),
                Background = NotifyTheme.ButtonBackground,
                BorderBrush = NotifyTheme.ButtonBorder,
                Foreground = NotifyTheme.ButtonForeground
            };
            button.Click += (s, e) =>
            {
                SetResult(result);
                Close();
            };
            return button;
        }

        protected TextBlock AddLine(string text, double fontSize, Brush foreground, double topMargin = 0)
        {
            var block = new TextBlock
            {
                Text = text,
                FontSize = fontSize,
                FontFamily = NotifyTheme.Font,
                Foreground = foreground,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 348,
                Margin = new Thickness(0, topMargin, 0, 0)
            };
            Root.Children.Add(block);
            return block;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            IntPtr handle = new WindowInteropHelper(this).Handle;
            int style = IntPtr.Size == 8
                ? GetWindowLongPtr64(handle, GWL_EXSTYLE).ToInt32()
                : GetWindowLong32(handle, GWL_EXSTYLE);
            SetWindowLongPtr(handle, GWL_EXSTYLE, new IntPtr(style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            MoveToCursorScreenCorner();
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_timer != null)
                _timer.Stop();
            _tcs.TrySetResult(DefaultResult);
            if (Current == this)
                Current = null;
            base.OnClosed(e);
        }

        private void MoveToCursorScreenCorner()
        {
            System.Drawing.Point cursor = System.Windows.Forms.Cursor.Position;
            System.Drawing.Rectangle area = System.Windows.Forms.Screen.FromPoint(cursor).WorkingArea;

            double scaleX = 1;
            double scaleY = 1;
            PresentationSource source = PresentationSource.FromVisual(this);
            if (source != null && source.CompositionTarget != null)
            {
                scaleX = source.CompositionTarget.TransformToDevice.M11;
                scaleY = source.CompositionTarget.TransformToDevice.M22;
            }

            Left = area.Right / scaleX - ActualWidth - 16;
            Top = area.Bottom / scaleY - ActualHeight - 16;
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            if (IntPtr.Size == 8)
                return SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
            return new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
        }
    }
}
