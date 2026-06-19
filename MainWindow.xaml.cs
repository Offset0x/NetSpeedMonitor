using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace NetSpeedMonitor
{
    public class AppConfig
    {
        public double Top { get; set; } = 100;
        public double Left { get; set; } = 100;
        public double WindowWidth { get; set; } = 110;
        public double WindowHeight { get; set; } = 44;

        public byte BackgroundAlpha { get; set; } = 1;
        public string BorderColor { get; set; } = "#33FFFFFF";
        public bool ShowBorder { get; set; } = true;
        public double BorderThickness { get; set; } = 1;
        public double CornerRadius { get; set; } = 6;
        public double HorizontalPadding { get; set; } = 8;
        public double VerticalPadding { get; set; } = 2;
        public bool HideInFullScreen { get; set; } = true;

        public int SampleIntervalMs { get; set; } = 1000;
        public int SmoothingFactor { get; set; } = 3;
        public string UnitSystem { get; set; } = "Binary";
        public string UnitBase { get; set; } = "Bytes";
        public bool MinUnitKB { get; set; } = false;
        public int DecimalPlaces { get; set; } = 1;
        public bool ShowUnitSuffix { get; set; } = true;
        public bool SwapUpDown { get; set; } = false;
        public bool ShowIcons { get; set; } = true;
        public string UploadIcon { get; set; } = "\u2191";
        public string DownloadIcon { get; set; } = "\u2193";
        public string UploadColor { get; set; } = "#FFAA33";
        public string DownloadColor { get; set; } = "#33AAFF";
        public string UploadIconColor { get; set; } = "";
        public string DownloadIconColor { get; set; } = "";
        public double FontSize { get; set; } = 11;
        public string FontFamily { get; set; } = "Segoe UI";
        public bool BoldText { get; set; } = true;
        public double RowSpacing { get; set; } = 1;
        public bool ShowSessionInTooltip { get; set; } = true;

        public string AdapterMode { get; set; } = "Auto";
        public string ManualAdapterName { get; set; } = "";

        public int StatsFlushMinutes { get; set; } = 5;
        public int StatsRetentionDays { get; set; } = 30;
        public bool StatsEnabled { get; set; } = true;
    }

    public partial class MainWindow : Window
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int left; public int top; public int right; public int bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public class MONITORINFO
        {
            public int cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
        [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMonitor, [In, Out] MONITORINFO lpmi);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;

        private readonly string _configPath = "config.json";
        private readonly string _statsPath = "netstats.json";
        private NetworkSampler _sampler;
        private StatsEngine _stats;
        private DispatcherTimer _sampleTimer;
        private DispatcherTimer _keepAliveTimer;
        private DispatcherTimer _flushTimer;
        private DispatcherTimer _tooltipTimer;
        private bool _isDragging = false;
        private bool _isLoaded = false;
        public AppConfig CurrentConfig { get; set; }
        public StatsEngine Stats => _stats;

        public MainWindow()
        {
            InitializeComponent();
            CurrentConfig = LoadConfig();
            this.LocationChanged += Window_LocationChanged;

            _sampler = new NetworkSampler();
            _sampler.Configure(ParseAdapterMode(CurrentConfig.AdapterMode), CurrentConfig.ManualAdapterName);

            if (CurrentConfig.StatsEnabled)
            {
                _stats = new StatsEngine(_statsPath, CurrentConfig.StatsRetentionDays);
                _stats.SetRetention(CurrentConfig.StatsRetentionDays);
            }

            _sampleTimer = new DispatcherTimer();
            _sampleTimer.Tick += (s, e) => UpdateSpeed();

            _keepAliveTimer = new DispatcherTimer();
            _keepAliveTimer.Interval = TimeSpan.FromMilliseconds(500);
            _keepAliveTimer.Tick += (s, e) => EnforceZOrder();
            _keepAliveTimer.Start();

            _flushTimer = new DispatcherTimer();
            _flushTimer.Tick += (s, e) => _stats?.Flush();

            _tooltipTimer = new DispatcherTimer();
            _tooltipTimer.Interval = TimeSpan.FromSeconds(2);
            _tooltipTimer.Tick += (s, e) => UpdateTooltip();
            _tooltipTimer.Start();

            ApplyConfig();
        }

        private static AdapterMode ParseAdapterMode(string s)
        {
            return s switch
            {
                "All" => AdapterMode.All,
                "Manual" => AdapterMode.Manual,
                _ => AdapterMode.Auto,
            };
        }

        private bool IsForegroundFullScreen()
        {
            IntPtr hWnd = GetForegroundWindow();
            if (hWnd == IntPtr.Zero || hWnd == GetDesktopWindow() || hWnd == GetShellWindow()) return false;

            GetWindowRect(hWnd, out RECT windowRect);
            IntPtr hMonitor = MonitorFromWindow(hWnd, 2);
            if (hMonitor == IntPtr.Zero) return false;

            MONITORINFO mi = new MONITORINFO();
            GetMonitorInfo(hMonitor, mi);

            return windowRect.left <= mi.rcMonitor.left &&
                   windowRect.top <= mi.rcMonitor.top &&
                   windowRect.right >= mi.rcMonitor.right &&
                   windowRect.bottom >= mi.rcMonitor.bottom;
        }

        private void EnforceZOrder()
        {
            if (CurrentConfig.HideInFullScreen && IsForegroundFullScreen())
            {
                if (this.Visibility != Visibility.Hidden) this.Visibility = Visibility.Hidden;
                return;
            }
            if (this.Visibility != Visibility.Visible) this.Visibility = Visibility.Visible;
            if (this.WindowState == WindowState.Minimized) this.WindowState = WindowState.Normal;

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        private SolidColorBrush GetBrush(string hex, SolidColorBrush fallback)
        {
            try { return (SolidColorBrush)new BrushConverter().ConvertFrom(hex); }
            catch { return fallback; }
        }

        public void ApplyConfig()
        {
            this.Width = CurrentConfig.WindowWidth;
            this.Height = CurrentConfig.WindowHeight;

            MainBorder.Background = new SolidColorBrush(Color.FromArgb(CurrentConfig.BackgroundAlpha, 29, 35, 38));
            MainBorder.BorderBrush = GetBrush(CurrentConfig.BorderColor, new SolidColorBrush(Color.FromArgb(51, 255, 255, 255)));
            MainBorder.BorderThickness = CurrentConfig.ShowBorder
                ? new Thickness(CurrentConfig.BorderThickness)
                : new Thickness(0);
            MainBorder.CornerRadius = new CornerRadius(CurrentConfig.CornerRadius);
            MainBorder.Padding = new Thickness(CurrentConfig.HorizontalPadding, CurrentConfig.VerticalPadding, CurrentConfig.HorizontalPadding, CurrentConfig.VerticalPadding);

            UpRow.Margin = new Thickness(0, 0, 0, CurrentConfig.RowSpacing);
            DownRow.Margin = new Thickness(0, CurrentConfig.RowSpacing, 0, 0);

            UpIcon.Visibility = CurrentConfig.ShowIcons ? Visibility.Visible : Visibility.Collapsed;
            DownIcon.Visibility = CurrentConfig.ShowIcons ? Visibility.Visible : Visibility.Collapsed;
            UpIcon.Text = CurrentConfig.UploadIcon;
            DownIcon.Text = CurrentConfig.DownloadIcon;

            var upBrush = GetBrush(CurrentConfig.UploadColor, new SolidColorBrush(Color.FromRgb(0xFF, 0xAA, 0x33)));
            var downBrush = GetBrush(CurrentConfig.DownloadColor, new SolidColorBrush(Color.FromRgb(0x33, 0xAA, 0xFF)));
            UpText.Foreground = upBrush;
            DownText.Foreground = downBrush;

            UpIcon.Foreground = !string.IsNullOrEmpty(CurrentConfig.UploadIconColor)
                ? GetBrush(CurrentConfig.UploadIconColor, upBrush) : upBrush;
            DownIcon.Foreground = !string.IsNullOrEmpty(CurrentConfig.DownloadIconColor)
                ? GetBrush(CurrentConfig.DownloadIconColor, downBrush) : downBrush;

            UpText.FontSize = CurrentConfig.FontSize;
            DownText.FontSize = CurrentConfig.FontSize;
            UpIcon.FontSize = CurrentConfig.FontSize;
            DownIcon.FontSize = CurrentConfig.FontSize;
            UpText.FontWeight = CurrentConfig.BoldText ? FontWeights.Bold : FontWeights.Normal;
            DownText.FontWeight = CurrentConfig.BoldText ? FontWeights.Bold : FontWeights.Normal;
            UpIcon.FontWeight = CurrentConfig.BoldText ? FontWeights.Bold : FontWeights.Normal;
            DownIcon.FontWeight = CurrentConfig.BoldText ? FontWeights.Bold : FontWeights.Normal;

            try { UpText.FontFamily = new FontFamily(CurrentConfig.FontFamily); } catch { }
            try { DownText.FontFamily = new FontFamily(CurrentConfig.FontFamily); } catch { }
            try { UpIcon.FontFamily = new FontFamily(CurrentConfig.FontFamily); } catch { }
            try { DownIcon.FontFamily = new FontFamily(CurrentConfig.FontFamily); } catch { }

            if (CurrentConfig.SwapUpDown)
            {
                Grid.SetRow(UpRow, 1);
                Grid.SetRow(DownRow, 0);
            }
            else
            {
                Grid.SetRow(UpRow, 0);
                Grid.SetRow(DownRow, 1);
            }

            _sampler.Configure(ParseAdapterMode(CurrentConfig.AdapterMode), CurrentConfig.ManualAdapterName);
            _sampler.ResetSmoothing();

            _sampleTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(100, CurrentConfig.SampleIntervalMs));
            _sampleTimer.Start();

            int flushMin = CurrentConfig.StatsFlushMinutes <= 0 ? 5 : CurrentConfig.StatsFlushMinutes;
            _flushTimer.Interval = TimeSpan.FromMinutes(flushMin);
            if (CurrentConfig.StatsEnabled)
            {
                if (_stats == null)
                {
                    _stats = new StatsEngine(_statsPath, CurrentConfig.StatsRetentionDays);
                }
                _stats.SetRetention(CurrentConfig.StatsRetentionDays);
                _flushTimer.Start();
            }
            else
            {
                _flushTimer.Stop();
            }
        }

        private void UpdateSpeed()
        {
            _sampler.Sample(out double up, out double down, out long dUp, out long dDown);

            double alpha = CurrentConfig.SmoothingFactor <= 1 ? 1.0 : 1.0 / CurrentConfig.SmoothingFactor;
            _sampler.ApplySmoothing(up, down, alpha);

            double sUp = _sampler.SmoothedUpload;
            double sDown = _sampler.SmoothedDownload;

            UpText.Text = FormatSpeed(sUp);
            DownText.Text = FormatSpeed(sDown);

            if (CurrentConfig.StatsEnabled && _stats != null)
            {
                _stats.AddDelta(dUp, dDown);
            }

            UpdateTooltip();
        }

        private string FormatSpeed(double bytesPerSec)
        {
            bool binary = string.Equals(CurrentConfig.UnitSystem, "Binary", StringComparison.OrdinalIgnoreCase);
            bool bits = string.Equals(CurrentConfig.UnitBase, "Bits", StringComparison.OrdinalIgnoreCase);
            double baseUnit = binary ? 1024 : 1000;

            string[] suffixes;
            double v;
            if (bits)
            {
                suffixes = binary
                    ? new[] { "bits/s", "Kibit/s", "Mibit/s", "Gibit/s", "Tibit/s" }
                    : new[] { "bps", "Kbps", "Mbps", "Gbps", "Tbps" };
                v = bytesPerSec * 8;
            }
            else
            {
                suffixes = binary
                    ? new[] { "B/s", "KiB/s", "MiB/s", "GiB/s", "TiB/s" }
                    : new[] { "B/s", "KB/s", "MB/s", "GB/s", "TB/s" };
                v = bytesPerSec;
            }

            int idx = 0;
            int minIdx = CurrentConfig.MinUnitKB ? 1 : 0;
            while (v >= baseUnit && idx < suffixes.Length - 1)
            {
                v /= baseUnit;
                idx++;
            }
            if (idx < minIdx)
            {
                v /= baseUnit;
                idx = minIdx;
            }

            string fmt = CurrentConfig.DecimalPlaces <= 0 ? "0" : "0." + new string('0', CurrentConfig.DecimalPlaces);
            string num = v.ToString(fmt);
            if (CurrentConfig.ShowUnitSuffix) return num + " " + suffixes[idx];
            return num;
        }

        private void UpdateTooltip()
        {
            if (!CurrentConfig.ShowSessionInTooltip)
            {
                this.ToolTip = null;
                return;
            }
            try
            {
                long sUp = _stats?.SessionUpload ?? 0;
                long sDown = _stats?.SessionDownload ?? 0;
                long tUp = _stats?.TotalUpload ?? 0;
                long tDown = _stats?.TotalDownload ?? 0;
                var today = _stats?.Today() ?? (0, 0);
                this.ToolTip = $"Session:  Up {FormatBytes(sUp)}  |  Down {FormatBytes(sDown)}\n" +
                               $"Today:    Up {FormatBytes(today.up)}  |  Down {FormatBytes(today.down)}\n" +
                               $"Total:    Up {FormatBytes(tUp)}  |  Down {FormatBytes(tDown)}";
            }
            catch { }
        }

        public static string FormatBytes(long bytes)
        {
            double v = bytes;
            string[] s = { "B", "KB", "MB", "GB", "TB", "PB" };
            int i = 0;
            while (v >= 1000 && i < s.Length - 1) { v /= 1000; i++; }
            return v.ToString("0.##") + " " + s[i];
        }

        private AppConfig LoadConfig()
        {
            if (File.Exists(_configPath))
            {
                try { return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(_configPath)) ?? new AppConfig(); }
                catch { return new AppConfig(); }
            }
            return new AppConfig();
        }

        public void SaveConfig()
        {
            CurrentConfig.Top = this.Top;
            CurrentConfig.Left = this.Left;
            try { File.WriteAllText(_configPath, JsonSerializer.Serialize(CurrentConfig)); }
            catch { }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            this.Top = CurrentConfig.Top;
            this.Left = CurrentConfig.Left;
            _isLoaded = true;
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            SaveConfig();
            _stats?.Flush(force: true);
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                _isDragging = true;
                try { this.DragMove(); }
                finally { _isDragging = false; }
                CurrentConfig.Top = this.Top;
                CurrentConfig.Left = this.Left;
                SaveConfig();
            }
        }

        private void Window_LocationChanged(object sender, EventArgs e)
        {
            if (!_isDragging && _isLoaded)
            {
                if (this.Top != CurrentConfig.Top || this.Left != CurrentConfig.Left)
                {
                    this.LocationChanged -= Window_LocationChanged;
                    try
                    {
                        this.Top = CurrentConfig.Top;
                        this.Left = CurrentConfig.Left;
                    }
                    finally { this.LocationChanged += Window_LocationChanged; }
                }
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            var w = new SettingsWindow(this);
            w.Show();
        }

        private void Stats_Click(object sender, RoutedEventArgs e)
        {
            var w = new StatsWindow(this);
            w.Show();
        }

        private void ResetSession_Click(object sender, RoutedEventArgs e)
        {
            _stats?.Flush(force: true);
        }

        public void CenterOnScreen()
        {
            var screen = SystemParameters.WorkArea;
            this.Left = (screen.Width - this.Width) / 2;
            this.Top = (screen.Height - this.Height) / 2;
            CurrentConfig.Top = this.Top;
            CurrentConfig.Left = this.Left;
            SaveConfig();
        }

        public void ClearAllStats()
        {
            if (_stats != null)
            {
                _stats.Clear();
                _stats.Flush(force: true);
            }
        }
    }
}
