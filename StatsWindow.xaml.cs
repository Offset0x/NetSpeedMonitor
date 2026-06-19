using System.Windows;

namespace NetSpeedMonitor
{
    public partial class StatsWindow : Window
    {
        private readonly MainWindow _parent;

        public StatsWindow(MainWindow parent)
        {
            InitializeComponent();
            _parent = parent;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e) => Refresh();

        private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            var res = MessageBox.Show("Clear ALL recorded data usage? This cannot be undone.",
                "Clear Usage Data", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (res == MessageBoxResult.OK)
            {
                _parent.ClearAllStats();
                Refresh();
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => this.Close();

        private void Refresh()
        {
            var stats = _parent?.Stats;
            if (stats == null)
            {
                TxtSession.Text = "Stats disabled";
                TxtToday.Text = "-";
                TxtTotal.Text = "-";
                LvHours.Items.Clear();
                LvDays.Items.Clear();
                return;
            }

            long sUp = stats.SessionUpload;
            long sDown = stats.SessionDownload;
            TxtSession.Text = $"Up {MainWindow.FormatBytes(sUp)}  |  Down {MainWindow.FormatBytes(sDown)}";

            var today = stats.Today();
            TxtToday.Text = $"Up {MainWindow.FormatBytes(today.up)}  |  Down {MainWindow.FormatBytes(today.down)}";

            TxtTotal.Text = $"Up {MainWindow.FormatBytes(stats.TotalUpload)}  |  Down {MainWindow.FormatBytes(stats.TotalDownload)}";

            LvHours.Items.Clear();
            foreach (var h in stats.GetHourly(24))
                LvHours.Items.Add(new { Key = h.key, Up = MainWindow.FormatBytes(h.up), Down = MainWindow.FormatBytes(h.down) });

            LvDays.Items.Clear();
            foreach (var d in stats.GetDaily(30))
                LvDays.Items.Add(new { Key = d.key, Up = MainWindow.FormatBytes(d.up), Down = MainWindow.FormatBytes(d.down) });
        }
    }
}
