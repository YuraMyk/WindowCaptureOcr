using System.Windows;
using System.Windows.Input;
using WindowCaptureOcr.Models;
using WindowCaptureOcr.Services;

namespace WindowCaptureOcr;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService;

    public SettingsWindow(SettingsService settingsService)
    {
        InitializeComponent();
        _settingsService = settingsService;

        // Populate fields from current settings
        var s = settingsService.Current;
        UrlBox.Text       = s.ClansJsonUrl;
        ThresholdBox.Text = s.FuzzyThreshold.ToString("0.00");
    }

    // ── Window drag (no title bar chrome) ────────────────────────────────────

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    // ── URL test ─────────────────────────────────────────────────────────────

    private void TestUrl_Click(object sender, RoutedEventArgs e)
    {
        var raw = UrlBox.Text.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            ResolvedUrlText.Text = "Enter a URL first.";
            return;
        }
        try
        {
            var resolved = ClanDownloadService.ResolveUrl(raw);
            ResolvedUrlText.Text = resolved == raw
                ? $"Direct URL (no rewrite needed)"
                : $"→ {resolved}";
        }
        catch (Exception ex)
        {
            ResolvedUrlText.Text = $"Error: {ex.Message}";
        }
    }

    // ── Save / Cancel ────────────────────────────────────────────────────────

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(ThresholdBox.Text, out double threshold)
            || threshold is < 0 or > 1)
        {
            StatusText.Text = "Fuzzy threshold must be a number between 0 and 1.";
            return;
        }

        var settings = new AppSettings
        {
            ClansJsonUrl    = UrlBox.Text.Trim(),
            FuzzyThreshold  = threshold,
            CaptureIntervalMs = _settingsService.Current.CaptureIntervalMs,
        };

        _settingsService.Save(settings);
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
