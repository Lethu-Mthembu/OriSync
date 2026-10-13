using QRCoder;
using SkiaSharp;

namespace OriSync.Api.StudentQrCodes;

public sealed class StudentQrCodeImageRenderer : IDisposable
{
    private const int CanvasSize = 1024;
    private const int MaximumQrSize = 800;
    private const float TextWidth = 900;
    private readonly SKTypeface _typeface;

    public StudentQrCodeImageRenderer(IWebHostEnvironment environment)
    {
        var fontPath = Path.Combine(
            environment.ContentRootPath,
            "Assets",
            "Fonts",
            "NotoSans.ttf");
        if (!File.Exists(fontPath))
        {
            throw new InvalidOperationException("The bundled Noto Sans font is unavailable.");
        }

        _typeface = SKTypeface.FromFile(fontPath)
            ?? throw new InvalidOperationException("The bundled Noto Sans font could not be loaded.");
    }

    public byte[] Render(string url, string displayName)
    {
        using var qrData = QRCodeGenerator.GenerateQrCode(
            url,
            QRCodeGenerator.ECCLevel.M,
            forceUtf8: true,
            utf8BOM: false,
            eciMode: QRCodeGenerator.EciMode.Utf8);
        EnsureQuietZone(qrData);
        var moduleCount = qrData.ModuleMatrix.Count;
        var pixelsPerModule = Math.Max(1, MaximumQrSize / moduleCount);
        var qrSize = moduleCount * pixelsPerModule;
        using var surface = SKSurface.Create(new SKImageInfo(
            CanvasSize,
            CanvasSize,
            SKColorType.Rgba8888,
            SKAlphaType.Opaque));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        using var modulePaint = new SKPaint
        {
            Color = SKColors.Black,
            IsAntialias = false,
            Style = SKPaintStyle.Fill
        };
        var qrX = (CanvasSize - qrSize) / 2;
        const int qrY = 24;
        for (var row = 0; row < moduleCount; row++)
        {
            for (var column = 0; column < moduleCount; column++)
            {
                if (!qrData.ModuleMatrix[row][column])
                {
                    continue;
                }

                canvas.DrawRect(
                    qrX + (column * pixelsPerModule),
                    qrY + (row * pixelsPerModule),
                    pixelsPerModule,
                    pixelsPerModule,
                    modulePaint);
            }
        }

        var textTop = qrY + qrSize + 20;
        var availableHeight = CanvasSize - textTop - 24;
        using var textPaint = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.Black
        };
        using var textFont = FitText(displayName, availableHeight, out var lines, out var lineHeight);
        var baseline = textTop - textFont.Metrics.Ascent;
        foreach (var line in lines)
        {
            canvas.DrawText(
                line,
                CanvasSize / 2f,
                baseline,
                SKTextAlign.Center,
                textFont,
                textPaint);
            baseline += lineHeight;
        }

        using var image = surface.Snapshot();
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    private SKFont FitText(
        string text,
        int availableHeight,
        out IReadOnlyList<string> lines,
        out float lineHeight)
    {
        for (var size = 44; size >= 18; size -= 2)
        {
            var font = new SKFont(_typeface, size);
            var wrapped = Wrap(text, font);
            var height = GetLineHeight(font);
            if (wrapped.Count <= 3 && height * wrapped.Count <= availableHeight)
            {
                lines = wrapped;
                lineHeight = height;
                return font;
            }

            font.Dispose();
        }

        var fallback = new SKFont(_typeface, 18);
        lines = Wrap(text, fallback);
        lineHeight = GetLineHeight(fallback);
        return fallback;
    }

    private static List<string> Wrap(string text, SKFont font)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = string.Empty;
        foreach (var word in words)
        {
            var candidate = current.Length == 0 ? word : $"{current} {word}";
            if (current.Length > 0 && font.MeasureText(candidate) > TextWidth)
            {
                lines.Add(current);
                current = word;
            }
            else
            {
                current = candidate;
            }
        }

        if (current.Length > 0)
        {
            lines.Add(current);
        }

        return lines;
    }

    private static float GetLineHeight(SKFont font)
    {
        var metrics = font.Metrics;
        return (metrics.Descent - metrics.Ascent + metrics.Leading) * 1.08f;
    }

    private static void EnsureQuietZone(QRCodeData data)
    {
        const int quietZoneModules = 4;
        var size = data.ModuleMatrix.Count;
        for (var offset = 0; offset < quietZoneModules; offset++)
        {
            for (var index = 0; index < size; index++)
            {
                if (data.ModuleMatrix[offset][index] ||
                    data.ModuleMatrix[size - 1 - offset][index] ||
                    data.ModuleMatrix[index][offset] ||
                    data.ModuleMatrix[index][size - 1 - offset])
                {
                    throw new InvalidOperationException(
                        "The QR encoder did not provide the required four-module quiet zone.");
                }
            }
        }
    }

    public void Dispose() => _typeface.Dispose();
}
