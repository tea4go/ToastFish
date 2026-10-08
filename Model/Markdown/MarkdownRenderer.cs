using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using ToastFish.Model.Notify;
using MdBlock = Markdig.Syntax.Block;
using MdInline = Markdig.Syntax.Inlines.Inline;
using MdTable = Markdig.Extensions.Tables.Table;
using MdTableCell = Markdig.Extensions.Tables.TableCell;
using MdTableRow = Markdig.Extensions.Tables.TableRow;
// Markdig 的 Markdown 块和 WPF 的文档块同名，两个都要用，只能给 WPF 这个起别名
using WpfBlock = System.Windows.Documents.Block;

namespace ToastFish.Model.Markdown
{
    /// <summary>
    /// 把大模型返回的 Markdown 渲染成只读的 FlowDocument，配色和字体跟随当前主题。
    /// 解析交给 Markdig，排版自己做，这样颜色、字号、间距能跟窗口的明暗主题和基准字号对齐。
    /// 认不出的块（HTML 块、引用定义等）直接跳过，不往界面上塞东西。
    /// </summary>
    public static class MarkdownRenderer
    {
        /// <summary>
        /// 只开表格扩展。不能用 UseAdvancedExtensions()：它会打开字母/罗马数字的有序列表标记，
        /// 而词典格式的释义行正是「n. 例子」「v. 举例」开头，会被当成嵌套列表整个吃掉。
        /// </summary>
        private static readonly MarkdownPipeline Pipeline =
            new MarkdownPipelineBuilder().UsePipeTables().Build();

        private static readonly FontFamily MonoFont = new FontFamily("Consolas, Courier New");

        /// <summary>H1~H6 相对正文字号的倍数。</summary>
        private static readonly double[] HeadingScale = { 1.7, 1.5, 1.3, 1.15, 1.05, 1.0 };

        /// <summary>表格外框的圆角半径。</summary>
        private const double TableCornerRadius = 16;

        /// <summary>把 Markdown 文本渲染成 FlowDocument。空文本返回一个空文档。</summary>
        public static FlowDocument Render(string markdown, double fontSize)
        {
            var document = new FlowDocument
            {
                FontFamily = NotifyTheme.Font,
                FontSize = fontSize,
                // 必须显式钉成 Normal：WPF 的 FontWeight 默认值跟随系统「消息字体」，
                // 用户把系统字体设成粗体时整份文档都会变粗，**...** 加粗就完全看不出来了
                FontWeight = FontWeights.Normal,
                Foreground = NotifyTheme.Markdown.Text,
                Background = Brushes.Transparent,
                PagePadding = new Thickness(0),
                // 不设的话 FlowDocument 会按默认列宽分成好几栏
                ColumnWidth = double.PositiveInfinity,
                TextAlignment = TextAlignment.Left
            };

            if (string.IsNullOrEmpty(markdown))
                return document;

            AddBlocks(document.Blocks, Markdig.Markdown.Parse(markdown, Pipeline), fontSize);
            return document;
        }

        private static void AddBlocks(BlockCollection target, ContainerBlock source, double fontSize)
        {
            foreach (MdBlock block in source)
            {
                WpfBlock rendered = ToBlock(block, fontSize);
                if (rendered != null)
                    target.Add(rendered);
            }
        }

        private static WpfBlock ToBlock(MdBlock block, double fontSize)
        {
            var heading = block as HeadingBlock;
            if (heading != null)
                return Heading(heading, fontSize);

            var paragraph = block as ParagraphBlock;
            if (paragraph != null)
                return Paragraph(paragraph, fontSize);

            var list = block as ListBlock;
            if (list != null)
                return List(list, fontSize);

            var quote = block as QuoteBlock;
            if (quote != null)
                return Quote(quote, fontSize);

            // FencedCodeBlock 也是 CodeBlock，围栏和缩进两种代码块一起接
            var code = block as CodeBlock;
            if (code != null)
                return Code(code, fontSize);

            var rule = block as ThematicBreakBlock;
            if (rule != null)
                return Rule(fontSize);

            var table = block as MdTable;
            if (table != null)
                return Table(table, fontSize);

            return null;
        }

        private static WpfBlock Heading(HeadingBlock heading, double fontSize)
        {
            int level = Math.Max(1, Math.Min(6, heading.Level));
            var paragraph = new Paragraph
            {
                FontSize = fontSize * HeadingScale[level - 1],
                FontWeight = FontWeights.Bold,
                Foreground = NotifyTheme.Markdown.Heading,
                Margin = new Thickness(0, fontSize * (level <= 2 ? 0.9 : 0.6), 0, fontSize * 0.35)
            };
            // H1/H2 底下压一条通栏横线：H1 粗、用标题色，H2 细、用浅灰，H3 往下不画
            if (level == 1)
            {
                paragraph.BorderBrush = NotifyTheme.Markdown.HeadingRule1;
                paragraph.BorderThickness = new Thickness(0, 0, 0, Math.Round(fontSize * 0.12));
                paragraph.Padding = new Thickness(0, 0, 0, fontSize * 0.3);
            }
            else if (level == 2)
            {
                paragraph.BorderBrush = NotifyTheme.Markdown.HeadingRule2;
                paragraph.BorderThickness = new Thickness(0, 0, 0, 1);
                paragraph.Padding = new Thickness(0, 0, 0, fontSize * 0.2);
            }
            AddInlines(paragraph.Inlines, heading.Inline, paragraph.FontSize);
            return paragraph;
        }

        private static WpfBlock Paragraph(ParagraphBlock source, double fontSize)
        {
            var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, fontSize * 0.4) };
            AddInlines(paragraph.Inlines, source.Inline, fontSize);
            return paragraph;
        }

        private static WpfBlock List(ListBlock source, double fontSize)
        {
            var list = new System.Windows.Documents.List
            {
                MarkerStyle = source.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                Margin = new Thickness(fontSize * 1.2, 0, 0, fontSize * 0.4),
                Padding = new Thickness(0)
            };
            foreach (MdBlock child in source)
            {
                var item = child as ListItemBlock;
                if (item == null)
                    continue;
                var listItem = new ListItem();
                AddBlocks(listItem.Blocks, item, fontSize);
                list.ListItems.Add(listItem);
            }
            return list;
        }

        private static WpfBlock Quote(QuoteBlock source, double fontSize)
        {
            var section = new Section
            {
                BorderBrush = NotifyTheme.Markdown.QuoteBorder,
                BorderThickness = new Thickness(fontSize * 0.2, 0, 0, 0),
                Padding = new Thickness(fontSize * 0.6, 0, 0, 0),
                Margin = new Thickness(0, 0, 0, fontSize * 0.4),
                Background = NotifyTheme.Markdown.QuoteBackground,
                Foreground = NotifyTheme.Markdown.QuoteText
            };
            AddBlocks(section.Blocks, source, fontSize);
            return section;
        }

        /// <summary>
        /// 代码块。WPF 的 Paragraph 画不出圆角（Block 没有 CornerRadius），
        /// 只能换成 BlockUIContainer 包一层 Border，再塞个 TextBlock 装代码。
        /// 代价是里面的文字不参与 FlowDocument 的选择（选择只覆盖 TextElement），
        /// 所以右上角配一个复制按钮作为取用出口。
        /// </summary>
        private static WpfBlock Code(CodeBlock source, double fontSize)
        {
            string code = CodeText(source);
            var text = new TextBlock
            {
                Text = code,
                FontFamily = MonoFont,
                FontSize = fontSize * 0.95,
                Foreground = NotifyTheme.Markdown.Text,
                TextWrapping = TextWrapping.Wrap
            };

            // 复制按钮单独占一列，正文少占这点宽度，换来按钮永远压不到代码上
            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.Children.Add(text);

            Border copy = CopyButton(code, fontSize);
            Grid.SetColumn(copy, 1);
            content.Children.Add(copy);

            return new BlockUIContainer(new Border
            {
                Background = NotifyTheme.Markdown.CodeBackground,
                BorderBrush = NotifyTheme.Markdown.CodeBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(fontSize * 0.5),
                Margin = new Thickness(0, 0, 0, fontSize * 0.5),
                Child = content
            });
        }

        /// <summary>
        /// 代码块右上角的复制按钮。图标用 Path 画两张叠着的纸，免得受用户自定义字体影响。
        /// 点击区包一层透明 Border：Transparent 参与命中测试而 null 不参与。
        /// </summary>
        private static Border CopyButton(string code, double fontSize)
        {
            var glyph = new Path
            {
                // 后面那张纸只画被前面那张挡不住的三条边，否则两张纸的轮廓会在里面交叉
                Data = Geometry.Parse("M 0.6,0.6 H 7.4 V 4.6 M 0.6,0.6 V 7.4 H 4.6 M 4.6,4.6 H 11.4 V 11.4 H 4.6 Z"),
                Stroke = NotifyTheme.Muted,
                StrokeThickness = 1.2,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Width = fontSize * 0.62,
                Height = fontSize * 0.62,
                Stretch = Stretch.Uniform
            };
            var button = new Border
            {
                Padding = new Thickness(fontSize * 0.2),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = "复制代码",
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Child = glyph
            };
            button.MouseEnter += (s, e) => glyph.Stroke = NotifyTheme.Foreground;
            button.MouseLeave += (s, e) => glyph.Stroke = NotifyTheme.Muted;
            button.MouseLeftButtonUp += (s, e) =>
            {
                if (!TryCopy(code))
                    return;
                // 复制成功图标短暂变绿；鼠标还停在按钮上时恢复成悬停色而不是常态色
                glyph.Stroke = NotifyTheme.Copied;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
                timer.Tick += (t, args) =>
                {
                    timer.Stop();
                    glyph.Stroke = button.IsMouseOver ? NotifyTheme.Foreground : NotifyTheme.Muted;
                };
                timer.Start();
            };
            return button;
        }

        /// <summary>剪贴板常被其他程序短暂占用，失败时重试几次再放弃。</summary>
        private static bool TryCopy(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;
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

        /// <summary>把代码块的多行拼成一段文本。</summary>
        private static string CodeText(LeafBlock source)
        {
            var text = new StringBuilder();
            // 只能按 Count 取：Lines 是 Markdig 内部的扩容数组，长度大于实际行数，
            // 直接 foreach 会把数组尾巴上的空槽也拼进来，代码块底下多出几行空白
            for (int i = 0; i < source.Lines.Count; i++)
            {
                if (text.Length > 0)
                    text.Append('\n');
                text.Append(source.Lines.Lines[i].ToString());
            }
            return text.ToString();
        }

        private static WpfBlock Rule(double fontSize)
        {
            return new BlockUIContainer(new Border
            {
                Height = 1,
                Background = NotifyTheme.Border,
                Margin = new Thickness(0, fontSize * 0.3, 0, fontSize * 0.6)
            });
        }

        /// <summary>
        /// 表格。WPF 的 Table 画不出圆角（Block 没有 CornerRadius），而且它是
        /// FrameworkContentElement，塞不进 BlockUIContainer 包的那层 Border。
        /// 所以整表改用 Grid 重排：外层 Border 画圆角外框和底色，单元格各自负责
        /// 隔行底色与内部网格线，最后把整块按圆角切掉四角。
        /// </summary>
        private static WpfBlock Table(MdTable source, double fontSize)
        {
            var rows = new List<MdTableRow>();
            foreach (MdBlock rowBlock in source)
            {
                var row = rowBlock as MdTableRow;
                if (row != null)
                    rows.Add(row);
            }

            int columnCount = 0;
            foreach (MdTableRow row in rows)
            {
                int count = 0;
                foreach (MdBlock cellBlock in row)
                {
                    if (cellBlock is MdTableCell)
                        count++;
                }
                columnCount = Math.Max(columnCount, count);
            }
            if (columnCount == 0)
                return null;

            var grid = new Grid();
            for (int i = 0; i < columnCount; i++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < rows.Count; i++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                MdTableRow row = rows[rowIndex];
                // 表头算第 1 行，往下第 2、4、6… 行（1 起数）铺一层浅色底，方便横向读
                Brush rowBackground = row.IsHeader
                    ? NotifyTheme.Markdown.TableHeaderBackground
                    : (rowIndex % 2 == 1 ? NotifyTheme.Markdown.TableEvenRowBackground : null);

                int cellIndex = 0;
                foreach (MdBlock cellBlock in row)
                {
                    var cell = cellBlock as MdTableCell;
                    if (cell == null)
                        continue;
                    var host = new Border
                    {
                        Background = rowBackground,
                        // 四个角上的格子单独切圆角，半径取外框内缘（外框 16px 圆角减 1px 边框），
                        // 底色正好贴住描边内侧。靠外框 Clip 裁的话会连描边一起裁掉，圆角处就断线了
                        CornerRadius = new CornerRadius(
                            rowIndex == 0 && cellIndex == 0 ? TableCornerRadius - 1 : 0,
                            rowIndex == 0 && cellIndex == columnCount - 1 ? TableCornerRadius - 1 : 0,
                            rowIndex == rows.Count - 1 && cellIndex == columnCount - 1 ? TableCornerRadius - 1 : 0,
                            rowIndex == rows.Count - 1 && cellIndex == 0 ? TableCornerRadius - 1 : 0),
                        BorderBrush = NotifyTheme.Markdown.TableBorder,
                        // 只画右边和下边：外框交给外层 Border，四边都画会跟它叠成 2px
                        BorderThickness = new Thickness(
                            0,
                            0,
                            cellIndex < columnCount - 1 ? 1 : 0,
                            rowIndex < rows.Count - 1 ? 1 : 0),
                        Padding = new Thickness(fontSize * 0.4, fontSize * 0.15, fontSize * 0.4, fontSize * 0.15),
                        Child = CellText(cell, fontSize, row.IsHeader)
                    };
                    Grid.SetRow(host, rowIndex);
                    Grid.SetColumn(host, cellIndex);
                    grid.Children.Add(host);
                    cellIndex++;
                }
            }

            var frame = new Border
            {
                Background = NotifyTheme.Markdown.TableBackground,
                BorderBrush = NotifyTheme.Markdown.TableBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(TableCornerRadius),
                Margin = new Thickness(0, 0, 0, fontSize * 0.5),
                Child = grid
            };
            return new BlockUIContainer(frame);
        }

        /// <summary>把单元格里的块内容塞进 TextBlock。管道表格的格子里正常只有一个段落。</summary>
        private static TextBlock CellText(MdTableCell cell, double fontSize, bool bold)
        {
            var text = new TextBlock
            {
                FontFamily = NotifyTheme.Font,
                FontSize = fontSize,
                // 跟 FlowDocument 一样显式钉住字重，否则跟随系统「消息字体」变成粗体
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                Foreground = NotifyTheme.Markdown.Text,
                TextWrapping = TextWrapping.Wrap
            };
            foreach (MdBlock block in cell)
            {
                var paragraph = block as ParagraphBlock;
                if (paragraph == null)
                    continue;
                if (text.Inlines.Count > 0)
                    text.Inlines.Add(new LineBreak());
                AddInlines(text.Inlines, paragraph.Inline, fontSize);
            }
            return text;
        }

        private static void AddInlines(InlineCollection target, ContainerInline source, double fontSize)
        {
            if (source == null)
                return;
            for (MdInline inline = source.FirstChild; inline != null; inline = inline.NextSibling)
                AddInline(target, inline, fontSize);
        }

        private static void AddInline(InlineCollection target, MdInline inline, double fontSize)
        {
            var literal = inline as LiteralInline;
            if (literal != null)
            {
                target.Add(new Run(literal.Content.ToString()));
                return;
            }

            var code = inline as CodeInline;
            if (code != null)
            {
                // 行内代码做成米黄小圆角块：Run 没有 Padding / CornerRadius，
                // 只能塞进 InlineUIContainer 里包一层 Border
                target.Add(new InlineUIContainer(new Border
                {
                    Background = NotifyTheme.Markdown.InlineCodeBackground,
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(fontSize * 0.28, fontSize * 0.15, fontSize * 0.28, fontSize * 0.15),
                    Child = new TextBlock
                    {
                        Text = code.Content,
                        FontFamily = MonoFont,
                        FontSize = fontSize * 0.95,
                        Foreground = NotifyTheme.Markdown.InlineCodeText
                    }
                })
                {
                    BaselineAlignment = BaselineAlignment.Center
                });
                return;
            }

            var emphasis = inline as EmphasisInline;
            if (emphasis != null)
            {
                var span = new Span();
                if (emphasis.DelimiterCount >= 2)
                    span.FontWeight = FontWeights.Bold;
                else
                    span.FontStyle = FontStyles.Italic;
                AddInlines(span.Inlines, emphasis, fontSize);
                target.Add(span);
                return;
            }

            var lineBreak = inline as LineBreakInline;
            if (lineBreak != null)
            {
                // 软换行按空格处理，硬换行才真的断行
                if (lineBreak.IsHard)
                    target.Add(new LineBreak());
                else
                    target.Add(new Run(" "));
                return;
            }

            var link = inline as LinkInline;
            if (link != null)
            {
                // 图片在译文里没意义，只留它的说明文字
                if (link.IsImage)
                {
                    AddInlines(target, link, fontSize);
                    return;
                }
                target.Add(MakeHyperlink(link.Url, link, fontSize));
                return;
            }

            var autolink = inline as AutolinkInline;
            if (autolink != null)
            {
                target.Add(MakeHyperlink(autolink.Url, null, fontSize));
                return;
            }

            var container = inline as ContainerInline;
            if (container != null)
                AddInlines(target, container, fontSize);
        }

        /// <summary>链接用主题的链接色并加下划线，点不点得开取决于 URL 合不合法，不合法就只当普通文字。</summary>
        private static Hyperlink MakeHyperlink(string url, ContainerInline children, double fontSize)
        {
            var hyperlink = new Hyperlink
            {
                Foreground = NotifyTheme.Markdown.Link,
                TextDecorations = TextDecorations.Underline
            };
            if (children != null)
                AddInlines(hyperlink.Inlines, children, fontSize);
            else
                hyperlink.Inlines.Add(new Run(url));

            Uri uri;
            if (Uri.TryCreate(url, UriKind.Absolute, out uri))
                hyperlink.NavigateUri = uri;
            return hyperlink;
        }
    }
}
