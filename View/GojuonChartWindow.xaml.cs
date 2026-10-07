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
    /// 右侧显示选中假名的平假名 / 片假名 / 罗马音对照。
    /// 单击假名选中，双击播放发音。
    /// </summary>
    public partial class GojuonChartWindow : Window
    {
        /// <summary>已经打开的窗口。同一时刻只留一个，重复点菜单只把它提到前面。</summary>
        private static GojuonChartWindow _open;

        private readonly Dictionary<GoinWord, Border> _tiles = new Dictionary<GoinWord, Border>();
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
            // 字号调到 28 时窗口高约 1232px，超过 1080p 的工作区，必须收住，
            // 放不下的内容由左栏的 ScrollViewer 兜底。
            Rect work = SystemParameters.WorkArea;
            Width = Math.Min(S(720), work.Width * 0.92);
            Height = Math.Min(S(660), work.Height * 0.92);
            MinWidth = Math.Min(S(600), Width);
            MinHeight = Math.Min(S(420), Height);

            RootGrid.Margin = new Thickness(S(16));
            Hint.Margin = new Thickness(S(2), 0, 0, S(12));
            Hint.FontSize = S(13);
            GapColumn.Width = new GridLength(S(20));
            DetailColumn.Width = new GridLength(S(250));
            DetailTitle.Margin = new Thickness(S(2), S(8), 0, S(10));
            DetailTitle.FontSize = S(12);

            Background = NotifyTheme.Background;
            Hint.Foreground = NotifyTheme.Muted;
            DetailTitle.Foreground = NotifyTheme.Muted;
            Hint.Text = "单击假名查看对照，双击播放发音";
            DetailTitle.Text = "对照";
            BuildGrid();
        }

        private void BuildGrid()
        {
            Dictionary<string, GoinWord> byRomaji = new Select().GetGoinWords()
                .ToDictionary(w => w.romaji);

            // 6 列（行名 + 5 段）× 12 行（段名 + 11 行）。列宽行高都固定，
            // 空位才占得住格子，各行的方块才会对齐。
            ChartHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(S(RowLabelWidth)) });
            for (int c = 0; c < ColumnNames.Length; c++)
                ChartHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(S(56)) });

            ChartHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int r = 0; r < KanaGrid.Length; r++)
                ChartHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(S(48)) });

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

        private Border CreateTile(GoinWord word)
        {
            var label = new TextBlock
            {
                Text = word.hiragana,
                // 假名不用 Calibri（它没有假名字形），跟随界面字体
                FontFamily = NotifyTheme.Font,
                FontSize = S(22),
                Foreground = NotifyTheme.Foreground,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var tile = new Border
            {
                Width = S(50),
                Height = S(44),
                Margin = new Thickness(0, 0, S(6), S(4)),
                CornerRadius = new CornerRadius(S(6)),
                BorderThickness = new Thickness(1),
                BorderBrush = NotifyTheme.Border,
                Background = NotifyTheme.ButtonBackground,
                Cursor = Cursors.Hand,
                ToolTip = "单击查看对照，双击播放发音",
                Child = label
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

        /// <summary>选中假名并显示对照。重复选中同一个不取消，双击时的两次点击因此没有副作用。</summary>
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

            ShowDetail(word);
        }

        private void ShowDetail(GoinWord word)
        {
            DetailHost.Children.Clear();
            DetailHost.RowDefinitions.Clear();
            DetailHost.ColumnDefinitions.Clear();

            DetailHost.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            DetailHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            string[] labels = { "平假名", "片假名", "罗马音" };
            string[] values = { word.hiragana, word.katakana, word.romaji };
            for (int i = 0; i < labels.Length; i++)
            {
                DetailHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                AddDetailCell(i, 0, labels[i], NotifyTheme.Muted, S(13));
                AddDetailCell(i, 1, values[i], NotifyTheme.Foreground, S(16));
            }
        }

        private void AddDetailCell(int row, int column, string text, Brush foreground, double fontSize)
        {
            var block = new TextBlock
            {
                Text = text,
                Foreground = foreground,
                FontSize = fontSize,
                FontFamily = NotifyTheme.Font,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, column == 0 ? S(12) : 0, S(8)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(block, row);
            Grid.SetColumn(block, column);
            DetailHost.Children.Add(block);
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
