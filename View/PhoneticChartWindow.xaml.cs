using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Speech.Synthesis;
using ToastFish.Model.Download;
using ToastFish.Model.Mp3;
using ToastFish.Model.Notify;
using ToastFish.Model.Phonetic;

namespace ToastFish.View
{
    /// <summary>
    /// 英语音标表：左侧 44 个音标（元音 / 辅音分组），右侧显示选中音标的 5 个例词。
    /// 单击音标选中并展开例词，双击播放音标；双击例词行播放单词发音。
    /// </summary>
    public partial class PhoneticChartWindow : Window
    {
        /// <summary>已经打开的窗口。同一时刻只留一个，重复点菜单只把它提到前面。</summary>
        private static PhoneticChartWindow _open;

        private readonly Dictionary<PhoneticSymbol, Border> _tiles = new Dictionary<PhoneticSymbol, Border>();
        private PhoneticSymbol _selected;

        // 整窗口随基准字号等比缩放，基准 15 时比例为 1，视觉与历史值一致。
        private readonly double _scale = NotifyTheme.BaseSize / 15.0;

        /// <summary>子组名那一列的宽度（基准字号下），要放得下最长的「开合双元音」。</summary>
        private const double GroupLabelWidth = 62;

        /// <summary>子组行的左缩进（基准字号下）。</summary>
        private const double GroupIndent = 10;

        /// <summary>组名与方块之间的间距（基准字号下）。</summary>
        private const double GroupLabelGap = 10;

        private double S(double value)
        {
            return value * _scale;
        }

        public static void ShowChart()
        {
            if (_open != null)
            {
                _open.Activate();
                return;
            }
            _open = new PhoneticChartWindow();
            _open.Closed += (s, e) => _open = null;
            _open.Show();
        }

        private PhoneticChartWindow()
        {
            InitializeComponent();
            NotifyTheme.Apply(this);

            // 窗口与内边距先按比例铺开，再夹到屏幕工作区内：
            // 字号调到 28 时窗口高约 1157px，超过 1080p 的工作区，必须收住，
            // 放不下的内容由左栏的 ScrollViewer 兜底。
            Rect work = SystemParameters.WorkArea;
            Width = Math.Min(S(720), work.Width * 0.92);
            Height = Math.Min(S(620), work.Height * 0.92);
            MinWidth = Math.Min(S(600), Width);
            MinHeight = Math.Min(S(420), Height);

            RootGrid.Margin = new Thickness(S(16));
            Hint.Margin = new Thickness(S(2), 0, 0, S(12));
            Hint.FontSize = S(13);
            GapColumn.Width = new GridLength(S(20));
            ExampleColumn.Width = new GridLength(S(250));
            ExampleTitle.Margin = new Thickness(S(2), S(8), 0, S(10));
            ExampleTitle.FontSize = S(12);

            Background = NotifyTheme.Background;
            Hint.Foreground = NotifyTheme.Muted;
            ExampleTitle.Foreground = NotifyTheme.Muted;
            Hint.Text = "单击音标查看例词，双击音标播放发音；双击例词可听单词读音";
            BuildChart();
        }

        private void BuildChart()
        {
            AddTopGroup("元音", PhoneticData.Vowels);
            AddTopGroup("辅音", PhoneticData.Consonants);
        }

        private void AddTopGroup(string title, List<PhoneticSection> sections)
        {
            ChartHost.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = NotifyTheme.Foreground,
                FontSize = S(15),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(S(2), S(12), 0, S(4))
            });
            foreach (PhoneticSection section in sections)
                AddSection(section);
        }

        private void AddSection(PhoneticSection section)
        {
            ChartHost.Children.Add(new TextBlock
            {
                Text = section.Title,
                Foreground = NotifyTheme.Muted,
                FontSize = S(13),
                Margin = new Thickness(S(12), S(8), 0, S(4))
            });
            foreach (PhoneticGroup group in section.Groups)
                AddGroup(group);
        }

        /// <summary>
        /// 一个子组排成一行：左边是固定宽度的组名，右边是这一组的方块。
        /// 组名用固定宽度而不是 Auto —— Auto 会让各行的方块起始位置参差不齐，
        /// 对照表看着就散了。宽度按最长的组名（「开合双元音」5 个字）留够；
        /// 靠右对齐，万一某字体把 5 个字排得更宽，多出来的部分往左边的缩进里溢，
        /// 不会被裁掉（Grid 默认不裁剪子元素）。
        /// 方块区自己带左间距：右对齐的组名右边缘正好落在列边界上，不留间距就是零距离。
        /// </summary>
        private void AddGroup(PhoneticGroup group)
        {
            var row = new Grid { Margin = new Thickness(S(GroupIndent), 0, 0, S(6)) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(S(GroupLabelWidth)) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var label = new TextBlock
            {
                Text = group.Title,
                Foreground = NotifyTheme.Muted,
                FontSize = S(12),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(label, 0);
            row.Children.Add(label);

            var tiles = new WrapPanel { Margin = new Thickness(S(GroupLabelGap), 0, 0, 0) };
            foreach (PhoneticSymbol symbol in group.Symbols)
                tiles.Children.Add(CreateTile(symbol));
            Grid.SetColumn(tiles, 1);
            row.Children.Add(tiles);

            ChartHost.Children.Add(row);
        }

        private Border CreateTile(PhoneticSymbol symbol)
        {
            var label = new TextBlock
            {
                Text = symbol.Ipa,
                FontSize = S(20),
                FontFamily = NotifyTheme.PhoneticFont,
                Foreground = NotifyTheme.Foreground,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var tile = new Border
            {
                // 宽度 50 而不是 54：一行最多 5 个方块，5 × 56 = 280 是横向预算的大头。
                // 窗口边框和滚动条约 32px 的开销不随字号缩放，字号调到 12 时窗口只有 576 宽，
                // 54 的方块会让「后元音」「摩擦音」这些 5 个一行的组折行。
                Width = S(50),
                Height = S(46),
                Margin = new Thickness(0, 0, S(6), S(6)),
                CornerRadius = new CornerRadius(S(6)),
                BorderThickness = new Thickness(1),
                BorderBrush = NotifyTheme.Border,
                Background = NotifyTheme.ButtonBackground,
                Cursor = Cursors.Hand,
                ToolTip = "单击查看例词，双击播放发音",
                Child = label
            };
            // 用 MouseLeftButtonDown 而不是 Button：Button 自带的点击逻辑会和双击抢事件
            tile.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2)
                    PlaySymbol(symbol);
                else
                    Select(symbol);
            };
            _tiles[symbol] = tile;
            return tile;
        }

        /// <summary>选中音标并展开它的例词。重复选中同一个不取消，双击时的两次点击因此没有副作用。</summary>
        private void Select(PhoneticSymbol symbol)
        {
            if (_selected != null)
            {
                Border previous = _tiles[_selected];
                previous.Background = NotifyTheme.ButtonBackground;
                previous.BorderBrush = NotifyTheme.Border;
            }

            _selected = symbol;
            Border tile = _tiles[symbol];
            tile.Background = NotifyTheme.ButtonHoverBackground;
            tile.BorderBrush = NotifyTheme.ButtonHoverBorder;

            ShowExamples(symbol);
        }

        private void ShowExamples(PhoneticSymbol symbol)
        {
            // 标题里音标和中文混排：音标单独用 IPA 字体，中文留在界面字体上，
            // 否则整块设成 Calibri 后中文会落到系统回退字体，跟别处的中文不一样
            ExampleTitle.Inlines.Clear();
            ExampleTitle.Inlines.Add(new Run("/" + symbol.Ipa + "/") { FontFamily = NotifyTheme.PhoneticFont });
            ExampleTitle.Inlines.Add(new Run("  例词"));
            ExampleHost.Children.Clear();
            ExampleHost.RowDefinitions.Clear();
            ExampleHost.ColumnDefinitions.Clear();

            ExampleHost.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ExampleHost.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ExampleHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            int row = 0;
            foreach (PhoneticExample example in symbol.Examples)
            {
                ExampleHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                AddCell(row, 0, example.Word, NotifyTheme.Foreground, S(15), example);
                AddCell(row, 1, "/" + example.Phonetic + "/", NotifyTheme.Muted, S(13), example);
                AddCell(row, 2, example.Meaning, NotifyTheme.Foreground, S(13), example);
                row++;
            }
        }

        private void AddCell(int row, int column, string text, Brush foreground, double fontSize, PhoneticExample example)
        {
            var block = new TextBlock
            {
                Text = text,
                Foreground = foreground,
                FontSize = fontSize,
                // 中间那列是音标，用 IPA 字体；单词和释义跟随界面字体
                FontFamily = column == 1 ? NotifyTheme.PhoneticFont : NotifyTheme.Font,
                // 释义列吃剩余宽度，长释义在这里折行；单词和音标是 Auto 列，量的时候宽度无限，不会折
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, column == 2 ? 0 : S(12), S(8)),
                VerticalAlignment = VerticalAlignment.Center
            };
            // 整行可点，双击播放单词发音
            block.Cursor = Cursors.Hand;
            block.Background = Brushes.Transparent;
            block.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2)
                    PlayWord(example);
            };
            Grid.SetRow(block, row);
            Grid.SetColumn(block, column);
            ExampleHost.Children.Add(block);
        }

        /// <summary>播放音标发音（内置 mp3）。播放是阻塞的 MCI 调用，放到后台线程。</summary>
        private static void PlaySymbol(PhoneticSymbol symbol)
        {
            string path = ".\\Resources\\Phonetic\\" + symbol.Audio;
            Task.Run(() =>
            {
                MUSIC music = new MUSIC();
                music.FileName = path;
                music.play();
            });
        }

        /// <summary>播放例词发音。与背诵卡片同一套：先取有道音频，取不到再用系统朗读兜底。</summary>
        private static void PlayWord(PhoneticExample example)
        {
            Task.Run(() =>
            {
                List<string> request = new List<string>
                {
                    example.Word + "_us",
                    example.Word + "&type=1"
                };
                if (!DownloadMp3.PlayMp3(request))
                {
                    SpeechSynthesizer synth = new SpeechSynthesizer();
                    synth.SpeakAsync(example.Word);
                }
            });
        }
    }
}
