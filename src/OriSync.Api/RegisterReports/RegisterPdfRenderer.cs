using SkiaSharp;
using System.Globalization;

namespace OriSync.Api.RegisterReports;

public sealed class RegisterPdfRenderer : IDisposable
{
    private const float PageWidth = 841.89f;
    private const float PageHeight = 595.28f;
    private const float Margin = 28f;
    private const float HeaderHeight = 104f;
    private const float TableHeaderHeight = 30f;
    private const float RowHeight = 25f;
    private static readonly Column[] Columns =
    [
        new("Initials, Surname", 205f),
        new("Student number", 128f),
        new("Age group", 91f),
        new("Gender", 96f),
        new("Ethnicity", 137f),
        new("Attendance", 128.89f)
    ];

    private readonly SKTypeface _typeface;
    private readonly SKBitmap _logo;

    public RegisterPdfRenderer(IWebHostEnvironment environment)
    {
        var fontPath = Path.Combine(environment.ContentRootPath, "Assets", "Fonts", "NotoSans.ttf");
        var logoPath = Path.Combine(environment.ContentRootPath, "Assets", "Branding", "tut-logo.png");
        if (!File.Exists(fontPath) || !File.Exists(logoPath))
        {
            throw new InvalidOperationException("The bundled register font or TUT logo is unavailable.");
        }

        _typeface = SKTypeface.FromFile(fontPath)
            ?? throw new InvalidOperationException("The bundled register font could not be loaded.");
        _logo = SKBitmap.Decode(logoPath)
            ?? throw new InvalidOperationException("The bundled TUT logo could not be loaded.");
    }

    public byte[] Render(
        string groupName,
        DateOnly attendanceDate,
        RegisterRow[] rows)
    {
        if (rows.Length == 0)
        {
            throw new ArgumentException("A register PDF requires at least one row.", nameof(rows));
        }

        using var stream = new MemoryStream();
        using var document = SKDocument.CreatePdf(stream, new SKDocumentPdfMetadata
        {
            Title = "First Year Orientation Attendance Register",
            Author = "Tshwane University of Technology",
            Subject = $"{groupName} - {attendanceDate:dd/MM/yyyy}"
        });

        var rowsPerPage = (int)Math.Floor(
            (PageHeight - Margin - HeaderHeight - TableHeaderHeight - Margin) / RowHeight);
        for (var offset = 0; offset < rows.Length; offset += rowsPerPage)
        {
            var canvas = document.BeginPage(PageWidth, PageHeight);
            DrawPageHeader(canvas, groupName, attendanceDate);
            DrawTableHeader(canvas);
            var pageRows = rows.Skip(offset).Take(rowsPerPage).ToArray();
            DrawRows(canvas, pageRows);
            document.EndPage();
        }

        document.Close();
        return stream.ToArray();
    }

    private void DrawPageHeader(SKCanvas canvas, string groupName, DateOnly attendanceDate)
    {
        canvas.Clear(SKColors.White);
        var logoWidth = 220f;
        var logoHeight = logoWidth * _logo.Height / _logo.Width;
        canvas.DrawBitmap(
            _logo,
            new SKRect(Margin, Margin, Margin + logoWidth, Margin + logoHeight));

        using var headingFont = new SKFont(_typeface, 18f);
        using var detailFont = new SKFont(_typeface, 11f);
        using var headingPaint = TextPaint(new SKColor(22, 59, 111));
        using var detailPaint = TextPaint(SKColors.Black);
        const float contentCenter = 558f;
        canvas.DrawText(
            "First Year Orientation Attendance Register",
            contentCenter,
            49f,
            SKTextAlign.Center,
            headingFont,
            headingPaint);
        canvas.DrawText(
            groupName,
            contentCenter,
            75f,
            SKTextAlign.Center,
            detailFont,
            detailPaint);
        canvas.DrawText(
            attendanceDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            contentCenter,
            94f,
            SKTextAlign.Center,
            detailFont,
            detailPaint);
    }

    private void DrawTableHeader(SKCanvas canvas)
    {
        var top = Margin + HeaderHeight;
        using var fill = new SKPaint { Color = new SKColor(22, 59, 111), Style = SKPaintStyle.Fill };
        using var border = BorderPaint();
        using var text = TextPaint(SKColors.White);
        using var font = new SKFont(_typeface, 9f);
        canvas.DrawRect(Margin, top, PageWidth - (2 * Margin), TableHeaderHeight, fill);

        var left = Margin;
        foreach (var column in Columns)
        {
            canvas.DrawRect(left, top, column.Width, TableHeaderHeight, border);
            canvas.DrawText(
                column.Title,
                left + (column.Width / 2),
                top + 19f,
                SKTextAlign.Center,
                font,
                text);
            left += column.Width;
        }
    }

    private void DrawRows(SKCanvas canvas, RegisterRow[] rows)
    {
        using var border = BorderPaint();
        using var text = TextPaint(SKColors.Black);
        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            var top = Margin + HeaderHeight + TableHeaderHeight + (index * RowHeight);
            var values = new[]
            {
                row.InitialsAndSurname,
                row.StudentNumber,
                row.AgeGroup.ToString(),
                row.Gender.ToString(),
                row.Ethnicity.ToString(),
                row.AttendanceStatus.ToString()
            };
            var left = Margin;
            for (var columnIndex = 0; columnIndex < Columns.Length; columnIndex++)
            {
                var column = Columns[columnIndex];
                canvas.DrawRect(left, top, column.Width, RowHeight, border);
                DrawFittedText(
                    canvas,
                    values[columnIndex],
                    left + 6f,
                    top,
                    column.Width - 12f,
                    text,
                    columnIndex == 0 ? SKTextAlign.Left : SKTextAlign.Center);
                left += column.Width;
            }
        }
    }

    private void DrawFittedText(
        SKCanvas canvas,
        string value,
        float left,
        float top,
        float availableWidth,
        SKPaint paint,
        SKTextAlign alignment)
    {
        using var font = new SKFont(_typeface, 9f);
        while (font.Size > 6f && font.MeasureText(value) > availableWidth)
        {
            font.Size -= 0.5f;
        }

        var x = alignment == SKTextAlign.Left ? left : left + (availableWidth / 2f);
        var baseline = top + ((RowHeight - (font.Metrics.Descent + font.Metrics.Ascent)) / 2f);
        canvas.DrawText(value, x, baseline, alignment, font, paint);
    }

    private static SKPaint TextPaint(SKColor color) => new()
    {
        Color = color,
        IsAntialias = true,
        Style = SKPaintStyle.Fill
    };

    private static SKPaint BorderPaint() => new()
    {
        Color = new SKColor(92, 112, 125),
        IsAntialias = true,
        StrokeWidth = 0.65f,
        Style = SKPaintStyle.Stroke
    };

    public void Dispose()
    {
        _logo.Dispose();
        _typeface.Dispose();
    }

    private sealed record Column(string Title, float Width);
}
