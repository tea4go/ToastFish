using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using ToastFish.Model.SqliteControl;

namespace ToastFish.Model.Notify
{
    /// <summary>
    /// 卡片字体与明暗主题的唯一来源。启动时 Load() 一次，设置窗口改完再 Load() 一次。
    /// </summary>
    public static class NotifyTheme
    {
        // 相对基准字号的比例，基准 15 时对应 26 / 14 / 16 / 13 / 11 / 13
        private const double WordRatio = 1.75;
        private const double LayeredWordRatio = 2.2;
        private const double PhoneticRatio = 0.95;
        private const double MeaningRatio = 1.05;
        private const double SentenceRatio = 0.85;
        private const double StatusRatio = 0.75;
        private const double ButtonRatio = 0.85;

        // 卡片内容区宽度同样随字号缩放，否则放大字号后长单词会折行。
        // 基准 15 时正好是历史值 348（348 / 15）。
        private const double ContentWidthRatio = 23.2;

        public static bool IsDark { get; private set; }
        public static FontFamily Font { get; private set; }
        public static double WordSize { get; private set; }
        public static double LayeredWordSize { get; private set; }
        public static double PhoneticSize { get; private set; }
        public static double MeaningSize { get; private set; }
        public static double SentenceSize { get; private set; }
        public static double StatusSize { get; private set; }
        public static double ButtonSize { get; private set; }

        /// <summary>卡片内容区最大宽度（不含外壳 padding）。随字号缩放。</summary>
        public static double CardWidth { get; private set; }

        public static Brush Background { get; private set; }
        public static Brush Foreground { get; private set; }
        public static Brush Muted { get; private set; }
        public static Brush Border { get; private set; }
        public static Brush ButtonBackground { get; private set; }
        public static Brush ButtonBorder { get; private set; }
        public static Brush ButtonForeground { get; private set; }

        /// <summary>鼠标悬停时的按钮底色与边框。用主题色，系统默认模板写死的浅蓝配浅色文字看不清。</summary>
        public static Brush ButtonHoverBackground { get; private set; }
        public static Brush ButtonHoverBorder { get; private set; }

        /// <summary>鼠标悬停时的按钮文字色，取纯黑/纯白，把与悬停底色的对比度拉到最大。</summary>
        public static Brush ButtonHoverForeground { get; private set; }

        /// <summary>按下时的按钮底色。</summary>
        public static Brush ButtonPressedBackground { get; private set; }

        /// <summary>点击复制成功后，该行文字短暂变成的颜色。</summary>
        public static Brush Copied { get; private set; }

        public static void Load()
        {
            int baseSize = Select.FONT_SIZE;
            if (baseSize < 12 || baseSize > 28)
                baseSize = 15;

            string family = Select.FONT_FAMILY;
            if (string.IsNullOrWhiteSpace(family))
                family = "Microsoft YaHei UI";

            Font = new FontFamily(family);
            WordSize = Math.Round(baseSize * WordRatio);
            LayeredWordSize = Math.Round(baseSize * LayeredWordRatio);
            PhoneticSize = Math.Round(baseSize * PhoneticRatio);
            MeaningSize = Math.Round(baseSize * MeaningRatio);
            SentenceSize = Math.Round(baseSize * SentenceRatio);
            StatusSize = Math.Round(baseSize * StatusRatio);
            ButtonSize = Math.Round(baseSize * ButtonRatio);
            CardWidth = Math.Round(baseSize * ContentWidthRatio);

            IsDark = Select.THEME == 2 || (Select.THEME == 0 && SystemUsesDarkApps());

            if (IsDark)
            {
                Background = Brush("#23262B");
                Foreground = Brush("#F2F4F7");
                Muted = Brush("#757D88");
                Border = Brush("#34383F");
                ButtonBackground = Brush("#33383F");
                ButtonBorder = Brush("#434A53");
                ButtonForeground = Brush("#EAEEF3");
                ButtonHoverBackground = Brush("#262A30");
                ButtonHoverBorder = Brush("#7C8794");
                ButtonHoverForeground = Brush("#FFFFFF");
                ButtonPressedBackground = Brush("#23272C");
                Copied = Brush("#5FD08A");
            }
            else
            {
                Background = Brush("#F7F7F8");
                Foreground = Brush("#14161A");
                Muted = Brush("#8B9199");
                Border = Brush("#E2E5EA");
                ButtonBackground = Brush("#E8EAEE");
                ButtonBorder = Brush("#D6D9DE");
                ButtonForeground = Brush("#22262C");
                ButtonHoverBackground = Brush("#EAECF0");
                ButtonHoverBorder = Brush("#98A1AD");
                ButtonHoverForeground = Brush("#000000");
                ButtonPressedBackground = Brush("#C6CCD5");
                Copied = Brush("#2E9E5B");
            }
        }

        public static void Apply(Window window)
        {
            window.FontFamily = Font;
        }

        private static Brush Brush(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        private static bool SystemUsesDarkApps()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key == null)
                        return false;
                    object value = key.GetValue("AppsUseLightTheme");
                    if (value == null)
                        return false;
                    return Convert.ToInt32(value) == 0;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
