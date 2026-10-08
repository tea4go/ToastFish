using System.Windows.Media;

namespace ToastFish.Model.Notify
{
    /// <summary>
    /// 译文窗口 Markdown 渲染用的一整套颜色。明暗主题各装配一份，在 NotifyTheme.Load() 里填。
    /// 单独成类是因为这套色板比卡片外壳那十几个色更细，塞进 NotifyTheme 会让它太臃肿。
    /// </summary>
    public class MarkdownPalette
    {
        /// <summary>正文文字色。</summary>
        public Brush Text { get; set; }

        /// <summary>H1–H6 标题色。</summary>
        public Brush Heading { get; set; }

        /// <summary>H1 底下那条通栏横线的颜色。参考图里比正文标题稍淡一点。</summary>
        public Brush HeadingRule1 { get; set; }

        /// <summary>H2 底下那条细横线的颜色。</summary>
        public Brush HeadingRule2 { get; set; }

        /// <summary>链接色。链接一律带下划线，在渲染器里加。</summary>
        public Brush Link { get; set; }

        public Brush QuoteText { get; set; }
        public Brush QuoteBorder { get; set; }
        public Brush QuoteBackground { get; set; }

        public Brush CodeBackground { get; set; }
        public Brush CodeBorder { get; set; }

        public Brush TableBackground { get; set; }
        public Brush TableBorder { get; set; }
        public Brush TableHeaderBackground { get; set; }
        public Brush TableEvenRowBackground { get; set; }
    }
}
