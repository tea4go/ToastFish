using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
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

        /// <summary>被 ✕ 暂时收起的窗口。同一时刻只可能有一个，等用户从托盘恢复。</summary>
        public static NotifyWindowBase Paused { get; private set; }

        /// <summary>窗口被异常关闭时回传给等待方的值。</summary>
        protected int DefaultResult = 1;

        /// <summary>子类置 true 后窗口右上角出现 ✕。在子类构造函数里赋值，ShowAsCurrent 里建按钮。</summary>
        protected bool Closable;

        private const double CloseGlyphWidth = 18;   // 图标 8 + 内边距 5×2
        private const double CloseGap = 20;

        /// <summary>
        /// 顶行正文要给右上角 ✕ 让出的右侧空隙。✕ 叠在内容上，不预留的话长文本会钻到按钮底下。
        /// </summary>
        protected static readonly Thickness CloseGutter = new Thickness(0, 0, CloseGlyphWidth + CloseGap, 0);

        protected readonly StackPanel Root = new StackPanel();
        private readonly Border _shell = new Border();
        private readonly Grid _closeHost = new Grid();
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
            // 叠放而不是分两行：SizeToContent 下 star 行会被量成 0，且多一行会给每张卡片平白加高
            _closeHost.Children.Add(Root);
            _shell.Child = _closeHost;
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
            // 收起的窗口不接受作答：此时看不到选项，误按全局热键会把暂停的题答掉
            if (Paused == this)
                return;
            _tcs.TrySetResult(value);
        }

        /// <summary>暂时收起窗口。测试线程继续等在 WaitAsync 上，题目和进度都不丢。</summary>
        protected void Pause()
        {
            if (!_shown || Paused == this)
                return;
            Paused = this;
            Hide();
        }

        /// <summary>把暂时收起的窗口放回来。当前没有收起的窗口时返回 false。</summary>
        public static bool Resume()
        {
            NotifyWindowBase window = Paused;
            if (window == null)
                return false;
            // 必须先清 Paused：SetResult 的守卫认这个标记，留着会把恢复后的作答也吃掉
            Paused = null;
            OnUi(() =>
            {
                window.Show();
                // Show 不设 Current，不补上下一个窗口就不会把它顶掉
                Current = window;
            });
            return true;
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

            if (Closable)
                AddCloseButton();

            // 同一时刻只留一个窗口：新窗口直接把上一个顶掉。
            // 收起的窗口不顶掉——它还在等用户从托盘恢复
            if (Current != null && Current != this && Current != Paused)
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

        /// <summary>
        /// 只显示、不抢 Current。用于状态查询这类不该打断正在进行的测试的提示：
        /// 走 ShowAsCurrent 会把屏幕上的测试题顶掉，测试线程被推进到下一题。
        /// </summary>
        protected void ShowTransient()
        {
            NotifyTheme.Apply(this);
            _shell.Background = NotifyTheme.Background;
            _shell.BorderBrush = NotifyTheme.Border;
            try
            {
                Show();
                _shown = true;
            }
            catch (Exception ex)
            {
                Logger.Write("提示窗口显示失败：" + ex);
            }
        }

        /// <summary>
        /// 右上角的 ✕。用 Path 画交叉线而不是文字字形，免得受用户自定义字体影响。
        /// 点击区包一层透明 Border：Transparent 参与命中测试而 null 不参与（同 MakeCopyable）。
        /// </summary>
        private void AddCloseButton()
        {
            var glyph = new Path
            {
                Data = Geometry.Parse("M 0,0 L 8,8 M 8,0 L 0,8"),
                Stroke = NotifyTheme.Muted,
                StrokeThickness = 1.4,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            var hit = new Border
            {
                Padding = new Thickness(5),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = "暂时收起，稍后可从托盘「随机测试 → 恢复测试」找回",
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Child = glyph
            };
            hit.MouseLeftButtonUp += (s, e) => Pause();
            hit.MouseEnter += (s, e) => glyph.Stroke = NotifyTheme.Foreground;
            hit.MouseLeave += (s, e) => glyph.Stroke = NotifyTheme.Muted;
            _closeHost.Children.Add(hit);
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

        /// <summary>每行一个的按钮列。按钮等宽铺满卡片，长文本在按钮内部折行。</summary>
        protected void SetButtonsStacked(params (string Text, int Result)[] buttons)
        {
            var column = new StackPanel
            {
                Width = NotifyTheme.CardWidth,
                Margin = new Thickness(0, 12, 0, 0)
            };
            foreach ((string text, int result) in buttons)
            {
                Button button = CreateButton(text, result);
                TextBlock label = (TextBlock)button.Content;
                // 选项按行铺满卡片，文字靠左而不是居中
                label.TextAlignment = TextAlignment.Left;
                // 悬停放大只给测试选项：卡片里的两列按钮共享行高，放大一个会把同行的另一个一起撑高
                button.MouseEnter += (s, e) => label.FontSize = NotifyTheme.ButtonHoverSize;
                button.MouseLeave += (s, e) => label.FontSize = NotifyTheme.ButtonSize;
                button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                button.Margin = new Thickness(0, 0, 0, 6);
                column.Children.Add(button);
            }
            Root.Children.Add(column);
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
            var label = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                FontSize = NotifyTheme.ButtonSize,
                FontFamily = NotifyTheme.Font,
                Foreground = NotifyTheme.ButtonForeground
            };
            var button = new Button
            {
                Content = label,
                FontSize = NotifyTheme.ButtonSize,
                FontFamily = NotifyTheme.Font,
                Padding = new Thickness(10, 6, 10, 6),
                Background = NotifyTheme.ButtonBackground,
                BorderBrush = NotifyTheme.ButtonBorder,
                Cursor = Cursors.Hand,
                Template = CreateButtonTemplate()
            };
            // 悬停换文字色直接改 TextBlock：按钮自己的 Foreground 是局部值，模板触发器压不住；
            // 在模板里给 Border 设 TextElement.Foreground 也传不到里面的 TextBlock（实测无效）
            button.MouseEnter += (s, e) => label.Foreground = NotifyTheme.ButtonHoverForeground;
            button.MouseLeave += (s, e) => label.Foreground = NotifyTheme.ButtonForeground;
            button.Click += (s, e) =>
            {
                SetResult(result);
                Close();
            };
            return button;
        }

        /// <summary>
        /// 按钮模板。系统默认模板在鼠标悬停时把底色写死成浅蓝，暗色主题下配浅色文字
        /// 几乎看不清，所以自带一套走主题色的模板。悬停/按下的底色只在模板触发器里给，
        /// 光设 Button.Background 压不住默认模板里的触发器。
        /// </summary>
        internal static ControlTemplate CreateButtonTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border), "border");
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty,
                new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty,
                new TemplateBindingExtension(Control.VerticalContentAlignmentProperty));
            border.AppendChild(presenter);

            var template = new ControlTemplate(typeof(Button)) { VisualTree = border };

            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, NotifyTheme.ButtonHoverBackground, "border"));
            hover.Setters.Add(new Setter(Border.BorderBrushProperty, NotifyTheme.ButtonHoverBorder, "border"));
            template.Triggers.Add(hover);

            var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(Border.BackgroundProperty, NotifyTheme.ButtonPressedBackground, "border"));
            template.Triggers.Add(pressed);

            return template;
        }

        protected TextBlock AddLine(string text, double fontSize, Brush foreground, double topMargin = 0, bool copyable = false)
        {
            var block = new TextBlock
            {
                Text = text,
                FontSize = fontSize,
                FontFamily = NotifyTheme.Font,
                Foreground = foreground,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = NotifyTheme.CardWidth,
                Margin = new Thickness(0, topMargin, 0, 0)
            };
            if (copyable)
                MakeCopyable(block);
            Root.Children.Add(block);
            return block;
        }

        /// <summary>
        /// 点击该行即把文字复制到剪贴板，成功后被点的那行短暂变色。
        /// copyText 用于显示内容与实际要复制的内容不一致的情况（如单词行还带着音标）。
        /// </summary>
        protected static void MakeCopyable(TextBlock block, string copyText = null)
        {
            block.Cursor = Cursors.Hand;
            block.ToolTip = "点击复制";
            // Transparent 参与命中测试而 null 不参与，设成 Transparent 后整行空白也能点中
            block.Background = Brushes.Transparent;
            block.MouseLeftButtonUp += (s, e) => CopyText(block, copyText ?? block.Text);
        }

        /// <summary>
        /// 双击该行朗读它。朗读文本取第一个换行之前的部分——例句行是
        /// 「外文句子\n中文翻译」两段，只有前一段该读。
        /// 单击仍由 MakeCopyable 负责复制；双击时第一下会顺带复制一次，无碍。
        /// </summary>
        protected static void MakeSpeakable(TextBlock block, Action<string> onSpeak)
        {
            if (onSpeak == null)
                return;
            block.ToolTip = "单击复制，双击朗读";
            block.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount != 2)
                    return;
                string text = block.Text ?? "";
                int end = text.IndexOf('\n');
                onSpeak(end < 0 ? text : text.Substring(0, end));
            };
        }

        private static void CopyText(TextBlock block, string text)
        {
            if (string.IsNullOrEmpty(text))
                return;
            if (!TrySetClipboard(text))
                return;
            FlashCopied(block);
        }

        /// <summary>剪贴板常被其他程序短暂占用，失败时重试几次再放弃。</summary>
        private static bool TrySetClipboard(string text)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    // 用 WinForms 的 Win32 实现而非 System.Windows 的 OLE 实现：
                    // 后者在某些受限的启动上下文里会抛 CLIPBRD_E_CANT_OPEN，前者不会
                    System.Windows.Forms.Clipboard.SetText(text);
                    return true;
                }
                catch (ExternalException)
                {
                    Thread.Sleep(60);
                }
            }
            return false;
        }

        private static void FlashCopied(TextBlock block)
        {
            // 原色存进 Tag，连点时第二次不会把提示色当成原色存下来
            if (block.Tag == null)
                block.Tag = block.Foreground;
            block.Foreground = NotifyTheme.Copied;

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                block.Foreground = (Brush)block.Tag;
            };
            timer.Start();
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
            if (Paused == this)
                Paused = null;
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
