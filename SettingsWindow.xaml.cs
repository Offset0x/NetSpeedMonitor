using System;
using System.Windows;
using System.Windows.Controls;

namespace NetSpeedMonitor
{
    public partial class SettingsWindow : Window
    {
        private readonly MainWindow _parent;
        private bool _isLoaded = false;

        public SettingsWindow(MainWindow parent)
        {
            InitializeComponent();
            _parent = parent;
            var c = _parent.CurrentConfig;

            ChkHideFullScreen.IsChecked = c.HideInFullScreen;

            TxtWidth.Text = c.WindowWidth.ToString();
            TxtHeight.Text = c.WindowHeight.ToString();
            TxtPadding.Text = c.HorizontalPadding.ToString();
            TxtVPadding.Text = c.VerticalPadding.ToString();
            TxtRowSpacing.Text = c.RowSpacing.ToString();
            TxtRadius.Text = c.CornerRadius.ToString();
            ChkShowBorder.IsChecked = c.ShowBorder;
            TxtBorderThickness.Text = c.BorderThickness.ToString();
            TxtBorderColor.Text = c.BorderColor;
            AlphaSlider.Value = c.BackgroundAlpha;

            SldInterval.Value = c.SampleIntervalMs;
            SldSmoothing.Value = c.SmoothingFactor;
            CmbUnitBase.SelectedIndex = string.Equals(c.UnitBase, "Bits", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            CmbUnit.SelectedIndex = string.Equals(c.UnitSystem, "Binary", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
            SldDecimals.Value = c.DecimalPlaces;
            ChkShowSuffix.IsChecked = c.ShowUnitSuffix;
            ChkMinUnitKB.IsChecked = c.MinUnitKB;
            ChkSwapUpDown.IsChecked = c.SwapUpDown;

            TxtFont.Text = c.FontFamily;
            TxtFontSize.Text = c.FontSize.ToString();
            ChkBold.IsChecked = c.BoldText;
            ChkShowIcons.IsChecked = c.ShowIcons;
            TxtUpIcon.Text = c.UploadIcon;
            TxtDownIcon.Text = c.DownloadIcon;
            TxtUpColor.Text = c.UploadColor;
            TxtDownColor.Text = c.DownloadColor;
            TxtUpIconColor.Text = c.UploadIconColor;
            TxtDownIconColor.Text = c.DownloadIconColor;

            CmbAdapterMode.SelectedIndex = c.AdapterMode switch
            {
                "All" => 1,
                "Manual" => 2,
                _ => 0,
            };
            RefreshAdapterList();
            if (c.AdapterMode == "Manual")
            {
                for (int i = 0; i < CmbAdapter.Items.Count; i++)
                {
                    if (CmbAdapter.Items[i] is ComboBoxItem it && (string)it.Tag == c.ManualAdapterName)
                    {
                        CmbAdapter.SelectedIndex = i;
                        break;
                    }
                }
            }
            CmbAdapter.IsEnabled = CmbAdapterMode.SelectedIndex == 2;

            ChkStatsEnabled.IsChecked = c.StatsEnabled;
            ChkTooltip.IsChecked = c.ShowSessionInTooltip;
            SldFlush.Value = c.StatsFlushMinutes;
            SldRetention.Value = c.StatsRetentionDays;

            UpdateSliderLabels();
            _isLoaded = true;
        }

        private void RefreshAdapterList()
        {
            CmbAdapter.Items.Clear();
            try
            {
                foreach (var n in NetworkSampler.GetCandidateAdapters())
                {
                    var item = new ComboBoxItem { Content = NetworkSampler.Describe(n), Tag = n.Name };
                    CmbAdapter.Items.Add(item);
                }
            }
            catch { }
        }

        private void UpdateSliderLabels()
        {
            LblAlpha.Text = ((int)AlphaSlider.Value).ToString();
            LblInterval.Text = ((int)SldInterval.Value).ToString();
            LblSmoothing.Text = ((int)SldSmoothing.Value).ToString();
            LblDecimals.Text = ((int)SldDecimals.Value).ToString();
            LblFlush.Text = ((int)SldFlush.Value).ToString();
            LblRetention.Text = ((int)SldRetention.Value).ToString();
        }

        private void AdapterModeChanged(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            CmbAdapter.IsEnabled = CmbAdapterMode.SelectedIndex == 2;
            SettingChanged(sender, e);
        }

        private void SettingChanged(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            UpdateSliderLabels();

            var c = _parent.CurrentConfig;

            c.HideInFullScreen = ChkHideFullScreen.IsChecked ?? false;

            if (double.TryParse(TxtWidth.Text, out double w)) c.WindowWidth = w;
            if (double.TryParse(TxtHeight.Text, out double h)) c.WindowHeight = h;
            if (double.TryParse(TxtPadding.Text, out double p)) c.HorizontalPadding = p;
            if (double.TryParse(TxtVPadding.Text, out double vp)) c.VerticalPadding = vp;
            if (double.TryParse(TxtRowSpacing.Text, out double rs)) c.RowSpacing = rs;
            if (double.TryParse(TxtRadius.Text, out double cr)) c.CornerRadius = cr;
            c.ShowBorder = ChkShowBorder.IsChecked ?? false;
            if (double.TryParse(TxtBorderThickness.Text, out double bt)) c.BorderThickness = bt;
            c.BorderColor = TxtBorderColor.Text;
            c.BackgroundAlpha = (byte)AlphaSlider.Value;

            c.SampleIntervalMs = (int)SldInterval.Value;
            c.SmoothingFactor = (int)SldSmoothing.Value;
            c.UnitBase = CmbUnitBase.SelectedIndex == 1 ? "Bits" : "Bytes";
            c.UnitSystem = CmbUnit.SelectedIndex == 0 ? "Binary" : "Decimal";
            c.DecimalPlaces = (int)SldDecimals.Value;
            c.ShowUnitSuffix = ChkShowSuffix.IsChecked ?? false;
            c.MinUnitKB = ChkMinUnitKB.IsChecked ?? false;
            c.SwapUpDown = ChkSwapUpDown.IsChecked ?? false;

            c.FontFamily = TxtFont.Text;
            if (double.TryParse(TxtFontSize.Text, out double fs)) c.FontSize = fs;
            c.BoldText = ChkBold.IsChecked ?? false;
            c.ShowIcons = ChkShowIcons.IsChecked ?? false;
            c.UploadIcon = TxtUpIcon.Text;
            c.DownloadIcon = TxtDownIcon.Text;
            c.UploadColor = TxtUpColor.Text;
            c.DownloadColor = TxtDownColor.Text;
            c.UploadIconColor = TxtUpIconColor.Text;
            c.DownloadIconColor = TxtDownIconColor.Text;

            c.AdapterMode = CmbAdapterMode.SelectedIndex switch
            {
                1 => "All",
                2 => "Manual",
                _ => "Auto",
            };
            if (CmbAdapter.SelectedItem is ComboBoxItem sel) c.ManualAdapterName = (string)sel.Tag;

            c.StatsEnabled = ChkStatsEnabled.IsChecked ?? false;
            c.ShowSessionInTooltip = ChkTooltip.IsChecked ?? false;
            c.StatsFlushMinutes = (int)SldFlush.Value;
            c.StatsRetentionDays = (int)SldRetention.Value;

            _parent.ApplyConfig();
        }

        private void CenterH_Click(object sender, RoutedEventArgs e)
        {
            var screen = SystemParameters.WorkArea;
            _parent.Left = (screen.Width - _parent.Width) / 2;
            _parent.CurrentConfig.Left = _parent.Left;
            _parent.SaveConfig();
        }

        private void CenterV_Click(object sender, RoutedEventArgs e)
        {
            var screen = SystemParameters.WorkArea;
            _parent.Top = (screen.Height - _parent.Height) / 2;
            _parent.CurrentConfig.Top = _parent.Top;
            _parent.SaveConfig();
        }

        private void ClearData_Click(object sender, RoutedEventArgs e)
        {
            var res = MessageBox.Show("Clear ALL recorded data usage? This cannot be undone.",
                "Clear Usage Data", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (res == MessageBoxResult.OK)
            {
                _parent.ClearAllStats();
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            _parent.SaveConfig();
            this.Close();
        }
    }
}
