using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
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

        /// <summary>把 Markdown 文本渲染成 FlowDocument。空文本返回一个空文档。</summary>
        public static FlowDocument Render(string markdown, double fontSize)
        {
            var document = new FlowDocument
            {
                FontFamily = NotifyTheme.Font,
                FontSize = fontSize,
                Foreground = NotifyTheme.Foreground,
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
                Margin = new Thickness(0, fontSize * (level <= 2 ? 0.9 : 0.6), 0, fontSize * 0.35)
            };
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
                BorderBrush = NotifyTheme.Border,
                BorderThickness = new Thickness(fontSize * 0.2, 0, 0, 0),
                Padding = new Thickness(fontSize * 0.6, 0, 0, 0),
                Margin = new Thickness(0, 0, 0, fontSize * 0.4),
                Foreground = NotifyTheme.Muted
            };
            AddBlocks(section.Blocks, source, fontSize);
            return section;
        }

        private static WpfBlock Code(CodeBlock source, double fontSize)
        {
            return new Paragraph(new Run(CodeText(source)))
            {
                FontFamily = MonoFont,
                FontSize = fontSize * 0.95,
                Background = NotifyTheme.ButtonBackground,
                Padding = new Thickness(fontSize * 0.5),
                Margin = new Thickness(0, 0, 0, fontSize * 0.5)
            };
        }

        /// <summary>把代码块的多行拼成一段文本。</summary>
        private static string CodeText(LeafBlock source)
        {
            var text = new StringBuilder();
            foreach (var line in source.Lines.Lines)
            {
                if (text.Length > 0)
                    text.Append('\n');
                text.Append(line.ToString());
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

        private static WpfBlock Table(MdTable source, double fontSize)
        {
            var table = new System.Windows.Documents.Table
            {
                CellSpacing = 0,
                BorderBrush = NotifyTheme.Border,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, fontSize * 0.5)
            };
            var group = new TableRowGroup();
            foreach (MdBlock rowBlock in source)
            {
                var row = rowBlock as MdTableRow;
                if (row == null)
                    continue;
                var tableRow = new TableRow();
                if (row.IsHeader)
                    tableRow.FontWeight = FontWeights.Bold;
                foreach (MdBlock cellBlock in row)
                {
                    var cell = cellBlock as MdTableCell;
                    if (cell == null)
                        continue;
                    var tableCell = new TableCell
                    {
                        BorderBrush = NotifyTheme.Border,
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(fontSize * 0.4, fontSize * 0.15, fontSize * 0.4, fontSize * 0.15)
                    };
                    AddBlocks(tableCell.Blocks, cell, fontSize);
                    tableRow.Cells.Add(tableCell);
                }
                group.Rows.Add(tableRow);
            }
            table.RowGroups.Add(group);
            return table;
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
                target.Add(new Run(code.Content)
                {
                    FontFamily = MonoFont,
                    FontSize = fontSize * 0.95,
                    Background = NotifyTheme.ButtonBackground
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

        /// <summary>链接用主题的链接色，点不点得开取决于 URL 合不合法，不合法就只当普通文字。</summary>
        private static Hyperlink MakeHyperlink(string url, ContainerInline children, double fontSize)
        {
            var hyperlink = new Hyperlink { Foreground = NotifyTheme.Link };
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
