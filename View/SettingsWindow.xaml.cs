using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using ToastFish.Model.Notify;
using ToastFish.Model.SqliteControl;
using ToastFish.View.Notify;

namespace ToastFish.View
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            LoadCurrent();
        }

        private void LoadCurrent()
        {
            SelectNumber(Select.WORD_NUMBER);
            EngTypeBox.SelectedIndex = Select.ENG_TYPE == 1 ? 0 : 1;
            ThemeBox.SelectedIndex = Select.THEME < 0 || Select.THEME > 2 ? 0 : Select.THEME;

            var fonts = Fonts.SystemFontFamilies
                .Select(f => f.Source)
                .OrderBy(name => name)
                .ToList();
            if (!fonts.Contains(Select.FONT_FAMILY))
                fonts.Insert(0, Select.FONT_FAMILY);
            FontBox.ItemsSource = fonts;
            FontBox.SelectedItem = Select.FONT_FAMILY;

            FontSizeBox.Text = Select.FONT_SIZE.ToString();
        }

        private void SelectNumber(int number)
        {
            string target = number.ToString();
            foreach (object item in NumberBox.Items)
            {
                if (((System.Windows.Controls.ComboBoxItem)item).Content.ToString() == target)
                {
                    NumberBox.SelectedItem = item;
                    return;
                }
            }
            NumberBox.SelectedIndex = 1;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (NumberBox.SelectedItem != null)
                Select.WORD_NUMBER = int.Parse(
                    ((System.Windows.Controls.ComboBoxItem)NumberBox.SelectedItem).Content.ToString());

            Select.ENG_TYPE = EngTypeBox.SelectedIndex == 0 ? 1 : 2;
            Select.THEME = ThemeBox.SelectedIndex < 0 ? 0 : ThemeBox.SelectedIndex;

            if (FontBox.SelectedItem != null)
                Select.FONT_FAMILY = FontBox.SelectedItem.ToString();

            int size;
            if (int.TryParse(FontSizeBox.Text.Trim(), out size) && size >= 12 && size <= 28)
                Select.FONT_SIZE = size;

            new Select().UpdateGlobalConfig();

            NotifyTheme.Load();
            if (NotifyWindowBase.Current != null)
            {
                // 正在显示的卡片立刻跟着变大 / 换配色
                NotifyWindowBase.Current.Close();
            }

            DialogResult = true;
            Close();
        }
    }
}
