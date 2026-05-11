using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WindowCaptureOcr.Services;
using Brush = System.Windows.Media.Brush;

namespace WindowCaptureOcr;

public partial class MainWindow : Window
{
    // ─── Services ────────────────────────────────────────────────────────────
    private readonly ScreenCaptureService _capture = new();
    private readonly OcrService _ocr = new("en-US");
    private readonly SettingsService _settings = new();
    private readonly ClanDownloadService _downloader = new();
    private readonly ClanLookupService _lookup;      // constructed after settings are loaded

    // ─── Capture loop ────────────────────────────────────────────────────────
    private DispatcherTimer? _timer;
    private bool _ocrBusy;
    private bool _running;
    private DateTime _lastFrameTime = DateTime.MinValue;

    // ─── Manual-search lock ──────────────────────────────────────────────────
    private const int ManualLockSeconds = 8;
    private DateTime _manualLockUntil = DateTime.MinValue;
    private DispatcherTimer? _countdownTimer;

    // ─── Win32 ───────────────────────────────────────────────────────────────
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hWnd);

    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HT_CAPTION = 0x0002;

    // ─── Brush helper ────────────────────────────────────────────────────────
    private static Brush Brush(string hex) =>
        (Brush)new BrushConverter().ConvertFrom(hex)!;

    // ═════════════════════════════════════════════════════════════════════════

    public MainWindow()
    {
        InitializeComponent();

        // Build lookup with threshold from persisted settings
        _lookup = new ClanLookupService { FuzzyThreshold = _settings.Current.FuzzyThreshold };

        // Sync interval box to saved setting
        IntervalBox.Text = _settings.Current.CaptureIntervalMs.ToString();

        TopBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                ReleaseCapture();
                SendMessage(new WindowInteropHelper(this).Handle,
                            WM_NCLBUTTONDOWN, (IntPtr)HT_CAPTION, IntPtr.Zero);
            }
        };

        CaptureZone.SizeChanged += (_, _) => UpdateSizeHint();
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
    }

    // ─── Init ────────────────────────────────────────────────────────────────

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (!ScreenCaptureService.ExcludeFromCapture(hwnd))
            OcrTextBox.Text = "[Note: WDA_EXCLUDEFROMCAPTURE not supported – upgrade to Win10 build 19041+]\n\n";
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        UpdateClanCount();
        StartCapture();
    }

    // ─── Capture loop ────────────────────────────────────────────────────────

    private void StartCapture()
    {
        _timer?.Stop();
        _ocrBusy = false;
        _lastFrameTime = DateTime.MinValue;

        if (!int.TryParse(IntervalBox.Text, out int ms) || ms < 100) ms = 800;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        _timer.Tick += Timer_Tick;
        _timer.Start();

        _running = true;
        BtnStartStop.Content = "■  Stop";
        BtnStartStop.Foreground = Brush("#F38BA8");
        StatusDot.Fill = Brush("#A6E3A1");
    }

    private void StopCapture()
    {
        _timer?.Stop();
        _timer = null;
        _running = false;

        BtnStartStop.Content = "▶  Start";
        BtnStartStop.Foreground = Brush("#A6E3A1");
        StatusDot.Fill = Brush("#45475A");
        FpsText.Text = string.Empty;
    }

    private async void Timer_Tick(object? sender, EventArgs e)
    {
        if (_ocrBusy) return;
        _ocrBusy = true;

        try
        {
            var (x, y, w, h) = GetCaptureZonePhysicalRect();
            if (w <= 0 || h <= 0) return;

            Bitmap frame = await Task.Run(() => _capture.CaptureRect(x, y, w, h));

            var ocrResult = await _ocr.RecognizeAsync(frame);   // WinRT: must NOT be in Task.Run
            frame.Dispose();

            OcrTextBox.Text = ocrResult.Text;

            bool locked = DateTime.UtcNow < _manualLockUntil;
            if (!locked)
            {
                string? clanName = ClanParserService.ExtractClanName(ocrResult.Text);
                if (clanName is not null)
                    ShowClanInfo(await Task.Run(() => _lookup.Lookup(clanName)));
                else
                    ClearClanInfo();
            }

            var now = DateTime.UtcNow;
            if (_lastFrameTime != DateTime.MinValue)
                FpsText.Text = $"{1.0 / (now - _lastFrameTime).TotalSeconds:F1} fps";
            _lastFrameTime = now;
        }
        catch (Exception ex)
        {
            OcrTextBox.AppendText($"\n[Error: {ex.Message}]");
        }
        finally
        {
            _ocrBusy = false;
        }
    }

    // ─── Refresh clans button ─────────────────────────────────────────────────

    private async void RefreshClans_Click(object sender, RoutedEventArgs e)
    {
        BtnRefresh.IsEnabled = false;
        BtnRefresh.Content = "…";

        var url = _settings.Current.ClansJsonUrl;

        if (!string.IsNullOrWhiteSpace(url))
        {
            // Try to download first
            var result = await _downloader.DownloadAsync(url);
            if (result.Success)
            {
                _lookup.Reload();
                UpdateClanCount();
                SetTempStatus($"⟳ {result.Message}");
            }
            else
            {
                // Download failed — still reload local file
                _lookup.Reload();
                UpdateClanCount();
                SetTempStatus($"⚠ Download failed: {result.Message}. Local file reloaded.");
            }
        }
        else
        {
            // No URL configured — just reload local file
            _lookup.Reload();
            UpdateClanCount();
            SetTempStatus($"⟳ Local clans.json reloaded ({_lookup.Count} clans). Configure a URL in ⚙ Settings to enable download.");
        }

        BtnRefresh.Content = "⟳";
        BtnRefresh.IsEnabled = true;
    }

    // ─── Settings button ──────────────────────────────────────────────────────

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var win = new SettingsWindow(_settings) { Owner = this };
        if (win.ShowDialog() == true)
        {
            // Apply updated settings
            _lookup.FuzzyThreshold = _settings.Current.FuzzyThreshold;
            IntervalBox.Text = _settings.Current.CaptureIntervalMs.ToString();

            // Restart timer if interval changed
            if (_running) StartCapture();

            SetTempStatus("Settings saved.");
        }
    }

    // ─── Manual search ────────────────────────────────────────────────────────

    private void ManualSearch_Click(object sender, RoutedEventArgs e) => RunManualSearch();

    private void ManualSearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter) RunManualSearch();
    }

    private async void RunManualSearch()
    {
        var query = ManualSearchBox.Text.Trim();
        if (string.IsNullOrEmpty(query)) return;

        BtnManualSearch.IsEnabled = false;
        var info = await Task.Run(() => _lookup.Lookup(query));
        ShowClanInfo(info);
        _manualLockUntil = DateTime.UtcNow.AddSeconds(ManualLockSeconds);
        StartCountdown();
        BtnManualSearch.IsEnabled = true;
    }

    // ─── Countdown ───────────────────────────────────────────────────────────

    private void StartCountdown()
    {
        _countdownTimer?.Stop();
        LockCountdown.Visibility = Visibility.Visible;
        UpdateCountdownLabel();
        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += (_, _) =>
        {
            if (_manualLockUntil - DateTime.UtcNow <= TimeSpan.Zero)
            {
                _countdownTimer?.Stop();
                _countdownTimer = null;
                LockCountdown.Visibility = Visibility.Collapsed;
            }
            else UpdateCountdownLabel();
        };
        _countdownTimer.Start();
    }

    private void UpdateCountdownLabel()
    {
        int s = (int)Math.Ceiling((_manualLockUntil - DateTime.UtcNow).TotalSeconds);
        LockCountdown.Text = $"locked {s}s";
    }

    // ─── Clan info strip ──────────────────────────────────────────────────────

    private void ShowClanInfo(ClanLookupResult info)
    {
        ClanNameText.Text = info.ClanName;
        StatusText.Text = info.Status.ToUpperInvariant();
        StatusBadge.Background = Brush(info.StatusColour);

        if (info.Found)
        {
            string leader = string.IsNullOrEmpty(info.Leader) ? "" : $"· {info.Leader}";
            string hint = info.IsFuzzy
                ? $"  [{info.MatchLabel}, dist {info.EditDistance}]"
                : info.MatchMethod == MatchMethod.Contains ? "  [contains]" : "";
            LeaderText.Text = leader + hint;
        }
        else LeaderText.Text = "· not in list";
    }

    private void ClearClanInfo()
    {
        ClanNameText.Text = "—";
        StatusText.Text = "—";
        StatusBadge.Background = Brush("#45475A");
        LeaderText.Text = string.Empty;
    }

    // ─── Status toast (temporary message in FPS area) ────────────────────────

    private DispatcherTimer? _toastTimer;
    private void SetTempStatus(string msg)
    {
        FpsText.Text = msg;
        _toastTimer?.Stop();
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _toastTimer.Tick += (_, _) => { FpsText.Text = ""; _toastTimer?.Stop(); };
        _toastTimer.Start();
    }

    private void UpdateClanCount()
    {
        // Show count in the OCR panel header area via FpsText only briefly;
        // permanent count lives in the title tooltip
        ToolTip = $"OCR Overlay — {_lookup.Count} clans loaded";
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private (int x, int y, int w, int h) GetCaptureZonePhysicalRect()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        double scale = GetDpiForWindow(hwnd) / 96.0;
        var tl = CaptureZone.PointToScreen(new System.Windows.Point(0, 0));
        return (
            (int)Math.Round(tl.X * scale),
            (int)Math.Round(tl.Y * scale),
            (int)Math.Round(CaptureZone.ActualWidth * scale),
            (int)Math.Round(CaptureZone.ActualHeight * scale)
        );
    }

    private void UpdateSizeHint()
    {
        double scale = GetDpiForWindow(new WindowInteropHelper(this).Handle) / 96.0;
        SizeHint.Text = $"{(int)Math.Round(CaptureZone.ActualWidth * scale)} × " +
                        $"{(int)Math.Round(CaptureZone.ActualHeight * scale)} px";
    }

    // ─── Button handlers ─────────────────────────────────────────────────────

    private void StartStop_Click(object sender, RoutedEventArgs e)
    {
        if (_running) StopCapture(); else StartCapture();
    }

    private void CopyText_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(OcrTextBox.Text))
            Clipboard.SetText(OcrTextBox.Text);
    }

    private void ClearText_Click(object sender, RoutedEventArgs e)
    {
        OcrTextBox.Text = string.Empty;
        ClearClanInfo();
    }

    private void Close_Click(object sender, RoutedEventArgs e) =>
        Application.Current.Shutdown();
}
