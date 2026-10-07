using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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
            AddGroup("元音", PhoneticData.Vowels);
            AddGroup("辅音", PhoneticData.Consonants);
        }

        private void AddGroup(string title, List<PhoneticSymbol> symbols)
        {
            ChartHost.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = NotifyTheme.Muted,
                FontSize = S(12),
                Margin = new Thickness(S(2), S(10), 0, S(6))
            });

            var row = new WrapPanel();
            foreach (PhoneticSymbol symbol in symbols)
                row.Children.Add(CreateTile(symbol));
            ChartHost.Children.Add(row);
        }

        private Border CreateTile(PhoneticSymbol symbol)
        {
            var label = new TextBlock
            {
                Text = symbol.Ipa,
                FontSize = S(20),
                Foreground = NotifyTheme.Foreground,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var tile = new Border
            {
                Width = S(54),
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
            ExampleTitle.Text = "/" + symbol.Ipa + "/  例词";
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
