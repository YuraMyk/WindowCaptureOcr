using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace WindowCaptureOcr.Services;

/// <summary>
/// Wraps the built-in <see cref="OcrEngine"/> (Windows.Media.Ocr).
/// 100 % local – no internet required, no external libraries.
/// </summary>
public class OcrService
{
    private OcrEngine _engine;

    public string CurrentLanguage { get; private set; }

    public OcrService(string languageTag = "en-US")
    {
        CurrentLanguage = languageTag;
        _engine = CreateEngine(languageTag);
    }

    // ─── Language helpers ────────────────────────────────────────────────────

    /// <summary>Returns language tags installed on this machine that OCR can use.</summary>
    public static IReadOnlyList<string> GetAvailableLanguages() =>
        OcrEngine.AvailableRecognizerLanguages
                 .Select(l => l.LanguageTag)
                 .ToList()
                 .AsReadOnly();

    public void SetLanguage(string languageTag)
    {
        _engine = CreateEngine(languageTag);
        CurrentLanguage = languageTag;
    }

    private static OcrEngine CreateEngine(string tag)
    {
        var lang = new Language(tag);
        return OcrEngine.TryCreateFromLanguage(lang)
            ?? OcrEngine.TryCreateFromUserProfileLanguages()
            ?? throw new InvalidOperationException(
                $"No OCR recognizer found for '{tag}'. " +
                "Install the language pack in Windows Settings → Time & language → Language.");
    }

    // ─── Recognition ─────────────────────────────────────────────────────────

    /// <summary>
    /// Recognises text in <paramref name="bitmap"/> and returns the plain text result.
    /// </summary>
    public async Task<OcrResult> RecognizeAsync(Bitmap bitmap)
    {
        // Convert System.Drawing.Bitmap → SoftwareBitmap via an in-memory BMP stream
        using var ms = new System.IO.MemoryStream();
        bitmap.Save(ms, ImageFormat.Bmp);
        byte[] bytes = ms.ToArray();

        using var ras = new InMemoryRandomAccessStream();
        await ras.WriteAsync(bytes.AsBuffer());
        ras.Seek(0);

        var decoder      = await BitmapDecoder.CreateAsync(ras);
        var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

        var winOcrResult = await _engine.RecognizeAsync(softwareBitmap);
        return new OcrResult(winOcrResult.Text, winOcrResult.Lines);
    }
}

/// <summary>Simple value object returned by <see cref="OcrService.RecognizeAsync"/>.</summary>
public sealed class OcrResult
{
    public string Text  { get; }
    public IReadOnlyList<OcrLine> Lines { get; }

    internal OcrResult(string text, IReadOnlyList<OcrLine> lines)
    {
        Text  = text;
        Lines = lines;
    }
}
