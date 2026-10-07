using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ToastFish.Model.Mp3;
using ToastFish.Model.Notify;
using ToastFish.Model.SqliteControl;

namespace ToastFish.View
{
    /// <summary>
    /// 日语五十音图：46 个清音按传统「行 × 段」铺成 5×10 网格（外加 ん 一行），
    /// 每个格子里并排着平假名 / 片假名 / 罗马音。
    /// 底部两个开关分别控制片假名、罗马音那一段的显隐，默认都开。
    /// 单击假名标记选中，双击播放发音。
    /// </summary>
    public partial class GojuonChartWindow : Window
    {
        /// <summary>已经打开的窗口。同一时刻只留一个，重复点菜单只把它提到前面。</summary>
        private static GojuonChartWindow _open;

        private readonly Dictionary<GoinWord, Border> _tiles = new Dictionary<GoinWord, Border>();
        private readonly List<TextBlock> _katakanaLabels = new List<TextBlock>();
        private readonly List<TextBlock> _romajiLabels = new List<TextBlock>();
        private GoinWord _selected;

        // 整窗口随基准字号等比缩放，基准 15 时比例为 1。
        private readonly double _scale = NotifyTheme.BaseSize / 15.0;

        /// <summary>行名那一列的宽度（基准字号下），已含与方块之间的 10px 间距。</summary>
        private const double RowLabelWidth = 58;

        /// <summary>行名与方块之间的间距（基准字号下）。</summary>
        private const double RowLabelGap = 10;

        /// <summary>
        /// 11 行 × 5 段。字符串是数据库的 romaji，null 是五十音图固有的空位
        /// （や行缺 い/え 段、わ行缺 い/う/え 段）。
        /// 不能靠 wordRank 推列号：や行 / わ行 / ん 都不是整行 5 个。
        /// </summary>
        private static readonly string[][] KanaGrid =
        {
            //  あ段    い段    う段    え段    お段
            new[] { "a",   "i",   "u",   "e",   "o"   },  // あ行
            new[] { "ka",  "ki",  "ku",  "ke",  "ko"  },  // か行
            new[] { "sa",  "si",  "su",  "se",  "so"  },  // さ行
            new[] { "ta",  "chi", "tsu", "te",  "to"  },  // た行
            new[] { "na",  "ni",  "nu",  "ne",  "no"  },  // な行
            new[] { "ha",  "hi",  "fu",  "he",  "ho"  },  // は行
            new[] { "ma",  "mi",  "mu",  "me",  "mo"  },  // ま行
            new[] { "ya",  null,  "yu",  null,  "yo"  },  // や行
            new[] { "ra",  "ri",  "ru",  "re",  "ro"  },  // ら行
            new[] { "wa",  null,  null,  null,  "wo"  },  // わ行
            new[] { "n",   null,  null,  null,  null  },  // ん
        };

        private static readonly string[] RowNames =
        {
            "あ行", "か行", "さ行", "た行", "な行", "は行",
            "ま行", "や行", "ら行", "わ行", "ん"
        };

        private static readonly string[] ColumnNames = { "あ段", "い段", "う段", "え段", "お段" };

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
            _open = new GojuonChartWindow();
            _open.Closed += (s, e) => _open = null;
            _open.Show();
        }

        private GojuonChartWindow()
        {
            InitializeComponent();
            NotifyTheme.Apply(this);

            // 窗口与内边距先按比例铺开，再夹到屏幕工作区内：
            // 三段并排后格子变宽变矮，基准 15 时内容高约 500px、宽约 490px；
            // 字号继续调大时窗口会被工作区收住，放不下的内容由 ScrollViewer 兜底。
            Rect work = SystemParameters.WorkArea;
            Width = Math.Min(S(520), work.Width * 0.92);
            Height = Math.Min(S(680), work.Height * 0.92);
            MinWidth = Math.Min(S(460), Width);
            MinHeight = Math.Min(S(420), Height);

            RootGrid.Margin = new Thickness(S(16));
            Hint.Margin = new Thickness(S(2), 0, 0, S(12));
            Hint.FontSize = S(13);

            Background = NotifyTheme.Background;
            Hint.Foreground = NotifyTheme.Muted;
            Hint.Text = "单击假名标记选中，双击播放发音";
            BuildGrid();
            BuildToggles();
        }

        private void BuildGrid()
        {
            Dictionary<string, GoinWord> byRomaji = new Select().GetGoinWords()
                .ToDictionary(w => w.romaji);

            // 6 列（行名 + 5 段）× 12 行（段名 + 11 行）。列宽固定，空位才占得住格子，
            // 各行的方块才会对齐。行高交给 Auto：三段并排后格子只占一行文字的高度。
            ChartHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(S(RowLabelWidth)) });
            for (int c = 0; c < ColumnNames.Length; c++)
                ChartHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(S(80)) });

            ChartHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int r = 0; r < KanaGrid.Length; r++)
                ChartHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // 第 0 行：左上角空着，5 个段名居中压在各自的方块列上
            for (int c = 0; c < ColumnNames.Length; c++)
            {
                var head = new TextBlock
                {
                    Text = ColumnNames[c],
                    Foreground = NotifyTheme.Muted,
                    FontSize = S(13),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, S(4))
                };
                Grid.SetRow(head, 0);
                Grid.SetColumn(head, c + 1);
                ChartHost.Children.Add(head);
            }

            // 第 1-11 行：行名靠右对齐，方块落在模板指定的格子里
            for (int r = 0; r < KanaGrid.Length; r++)
            {
                var label = new TextBlock
                {
                    Text = RowNames[r],
                    Foreground = NotifyTheme.Muted,
                    FontSize = S(12),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    // 靠右对齐的行名右边缘正好落在列边界上，不留间距就是零距离
                    Margin = new Thickness(0, 0, S(RowLabelGap), 0)
                };
                Grid.SetRow(label, r + 1);
                Grid.SetColumn(label, 0);
                ChartHost.Children.Add(label);

                for (int c = 0; c < ColumnNames.Length; c++)
                {
                    string romaji = KanaGrid[r][c];
                    GoinWord word;
                    if (romaji == null || !byRomaji.TryGetValue(romaji, out word))
                        continue;

                    Border tile = CreateTile(word);
                    Grid.SetRow(tile, r + 1);
                    Grid.SetColumn(tile, c + 1);
                    ChartHost.Children.Add(tile);
                }
            }
        }

        /// <summary>
        /// 一个方块里并排一行：平假名（大）、片假名、罗马音。
        /// 后两者各自记进列表，底部开关一拨就整体显隐。
        /// 方块宽度写死 —— 藏掉某一段后留白，格子不会跟着伸缩，各行才对得齐。
        /// </summary>
        private Border CreateTile(GoinWord word)
        {
            var hiragana = new TextBlock
            {
                Text = word.hiragana,
                FontFamily = NotifyTheme.Font,
                FontSize = S(20),
                Foreground = NotifyTheme.Foreground,
                VerticalAlignment = VerticalAlignment.Center
            };
            var katakana = new TextBlock
            {
                Text = word.katakana,
                FontFamily = NotifyTheme.Font,
                FontSize = S(12),
                Foreground = NotifyTheme.Muted,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(S(5), 0, 0, 0)
            };
            var romaji = new TextBlock
            {
                Text = word.romaji,
                FontFamily = NotifyTheme.Font,
                FontSize = S(10),
                Foreground = NotifyTheme.Muted,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(S(5), 0, 0, 0)
            };
            _katakanaLabels.Add(katakana);
            _romajiLabels.Add(romaji);

            var stack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            stack.Children.Add(hiragana);
            stack.Children.Add(katakana);
            stack.Children.Add(romaji);

            var tile = new Border
            {
                Width = S(74),
                Padding = new Thickness(S(2), S(6), S(2), S(6)),
                Margin = new Thickness(0, 0, S(6), S(4)),
                CornerRadius = new CornerRadius(S(6)),
                BorderThickness = new Thickness(1),
                BorderBrush = NotifyTheme.Border,
                Background = NotifyTheme.ButtonBackground,
                Cursor = Cursors.Hand,
                ToolTip = "单击标记选中，双击播放发音",
                Child = stack
            };
            // 用 MouseLeftButtonDown 而不是 Button：Button 自带的点击逻辑会和双击抢事件
            tile.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2)
                    PlayKana(word);
                else
                    Select(word);
            };
            _tiles[word] = tile;
            return tile;
        }

        private void BuildToggles()
        {
            ToggleBar.Margin = new Thickness(S(2), S(12), 0, 0);
            InitToggle(ShowKatakana, "片假名", S(16));
            InitToggle(ShowRomaji, "罗马音", 0);
        }

        private void InitToggle(CheckBox box, string text, double leftMargin)
        {
            box.Content = text;
            box.IsChecked = true;
            box.Foreground = NotifyTheme.Foreground;
            box.FontSize = S(13);
            box.Margin = new Thickness(leftMargin, 0, 0, 0);
            box.Checked += (s, e) => ApplyToggles();
            box.Unchecked += (s, e) => ApplyToggles();
        }

        private void ApplyToggles()
        {
            Visibility katakana = ShowKatakana.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            Visibility romaji = ShowRomaji.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            foreach (TextBlock block in _katakanaLabels)
                block.Visibility = katakana;
            foreach (TextBlock block in _romajiLabels)
                block.Visibility = romaji;
        }

        /// <summary>标记选中的假名。重复选中同一个不取消，双击时的两次点击因此没有副作用。</summary>
        private void Select(GoinWord word)
        {
            if (_selected != null)
            {
                Border previous = _tiles[_selected];
                previous.Background = NotifyTheme.ButtonBackground;
                previous.BorderBrush = NotifyTheme.Border;
            }

            _selected = word;
            Border tile = _tiles[word];
            tile.Background = NotifyTheme.ButtonHoverBackground;
            tile.BorderBrush = NotifyTheme.ButtonHoverBorder;
        }

        /// <summary>播放假名发音（内置 mp3）。播放是阻塞的 MCI 调用，放到后台线程。</summary>
        private static void PlayKana(GoinWord word)
        {
            string path = ".\\Resources\\Goin\\" + word.romaji + ".mp3";
            Task.Run(() =>
            {
                MUSIC music = new MUSIC();
                music.FileName = path;
                music.play();
            });
        }
    }
}
