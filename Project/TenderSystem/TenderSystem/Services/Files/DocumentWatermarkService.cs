using Aspose.Pdf;
using Aspose.Pdf.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Word = DocumentFormat.OpenXml.Wordprocessing;
using Excel = DocumentFormat.OpenXml.Spreadsheet;

namespace TenderSystem.Services.Files;

public sealed record DocumentWatermarkEntry(string ViewerName, DateTime AccessedAtUtc, string Action);

public static class DocumentWatermarkService
{
    public static async Task<byte[]> CreateWatermarkedCopyAsync(Stream original, string fileName,
        string viewerName, DateTime accessedAt, string webRootPath, CancellationToken cancellationToken = default,
        IReadOnlyList<DocumentWatermarkEntry>? accessHistory = null)
    {
        await using var buffer = new MemoryStream();
        await original.CopyToAsync(buffer, cancellationToken);
        var content = buffer.ToArray();
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        // Storage may keep a generated filename without its original extension. Detect
        // the PDF signature as well so application/pdf files are never returned raw.
        var isPdf = extension == ".pdf" ||
                    (content.Length >= 5 && content.AsSpan(0, 5).SequenceEqual("%PDF-"u8));
        return isPdf
            ? WatermarkPdf(content, viewerName, accessedAt, accessHistory, webRootPath)
            : content;
    }

    private static byte[] WatermarkPdf(byte[] content, string viewerName, DateTime accessedAt,
        IReadOnlyList<DocumentWatermarkEntry>? accessHistory, string webRootPath)
    {
        using var input = new MemoryStream(content);
        using var document = new Aspose.Pdf.Document(input);
        RemoveLegacyViewerWatermark(document);
        var logoPath = Path.Combine(webRootPath, "ms-wand-home", "img", "ms-wand-lockup.webp");
        byte[]? logoPng = null;
        if (System.IO.File.Exists(logoPath))
        {
            using var logoImage = SixLabors.ImageSharp.Image.Load<Rgba32>(logoPath);
            using var logoBuffer = new MemoryStream();
            logoImage.Save(logoBuffer, new PngEncoder());
            logoPng = logoBuffer.ToArray();
        }
        var entries = accessHistory is { Count: > 0 }
            ? accessHistory.ToArray()
            : [new DocumentWatermarkEntry(viewerName, accessedAt, "View")];
        var logoStreams = new List<MemoryStream>();

        try
        {
            foreach (Aspose.Pdf.Page page in document.Pages)
            {
                var pageBounds = page.GetPageRect(true);
                var pageHeight = pageBounds.Height;
                var pageWidth = pageBounds.Width;
                const int watermarkCopies = 8;
                for (var index = 0; index < watermarkCopies; index++)
                {
                    var column = index % 2;
                    var row = index / 2;
                    var stamp = new Aspose.Pdf.TextStamp("MS.WAND")
                    {
                        Background = false,
                        Opacity = 0.14,
                        RotateAngle = 38,
                        XIndent = pageBounds.LLX + pageWidth * (column == 0 ? 0.06 : 0.54),
                        YIndent = pageBounds.LLY + pageHeight * (0.12 + row * 0.24)
                    };
                    stamp.TextState.Font = FontRepository.FindFont("Arial");
                    stamp.TextState.FontSize = 16;
                    stamp.TextState.FontStyle = FontStyles.Italic;
                    stamp.TextState.ForegroundColor = Aspose.Pdf.Color.DarkRed;
                    page.AddStamp(stamp);
                }
            }

            AddAccessHistoryAtStart(document, entries, logoPng, logoStreams);

            using var output = new MemoryStream();
            document.Save(output);
            return output.ToArray();
        }
        finally
        {
            foreach (var stream in logoStreams) stream.Dispose();
        }
    }

    private static void RemoveLegacyViewerWatermark(Aspose.Pdf.Document document)
    {
        var absorber = new TextFragmentAbsorber(@"MS Cosmetics\s*\|.*?\bUTC\b", new TextSearchOptions(true));
        document.Pages.Accept(absorber);
        foreach (var fragment in absorber.TextFragments)
            fragment.Text = string.Empty;
    }

    private static void AddAccessHistoryAtStart(Aspose.Pdf.Document document,
        IReadOnlyList<DocumentWatermarkEntry> entries, byte[]? logoPng, ICollection<MemoryStream> logoStreams)
    {
        if (document.Pages.Count == 0) return;

        var page = document.Pages[1];
        var pageBounds = page.GetPageRect(true);
        var pageWidth = pageBounds.Width;
        var pageHeight = pageBounds.Height;
        var landscape = pageWidth > pageHeight;
        const double rightInset = 30;
        var historyWidth = Math.Min(pageWidth * 0.4, 220);
        // Keep the logo and access history together in the visible upper-right corner.
        var logoY = pageBounds.LLY + pageHeight - (landscape ? 52 : 53);
        var firstRowY = pageBounds.LLY + pageHeight - (landscape ? 72 : 74);
        var rowSpacing = entries.Count > 4 ? 9 : 13;
        var fontSize = entries.Count > 4 ? 7 : 9;

        if (logoPng is not null)
        {
            var logoStream = new MemoryStream(logoPng, writable: false);
            logoStreams.Add(logoStream);
            page.AddStamp(new ImageStamp(logoStream)
            {
                Background = false,
                Width = 95,
                Height = 32,
                HorizontalAlignment = Aspose.Pdf.HorizontalAlignment.Right,
                XIndent = rightInset,
                YIndent = logoY,
                Opacity = 1
            });
        }

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var text = $"{entry.ViewerName}  |  {entry.AccessedAtUtc.ToString("dd MMM yyyy HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture)}";
            var stamp = new Aspose.Pdf.TextStamp(text)
            {
                Background = false,
                HorizontalAlignment = Aspose.Pdf.HorizontalAlignment.Right,
                TextAlignment = Aspose.Pdf.HorizontalAlignment.Right,
                Width = historyWidth,
                XIndent = rightInset,
                YIndent = firstRowY - index * rowSpacing
            };
            stamp.TextState.Font = FontRepository.FindFont("Arial");
            stamp.TextState.FontSize = fontSize;
            stamp.TextState.FontStyle = FontStyles.Regular;
            stamp.TextState.ForegroundColor = Aspose.Pdf.Color.DarkRed;
            page.AddStamp(stamp);
        }
    }

    private static byte[] WatermarkImage(byte[] content, string extension, string watermark, string webRootPath)
    {
        using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(content);
        var logoPath = Path.Combine(webRootPath, "images", "ms-wand-tilted-blush-logo.png");
        var bandHeight = Math.Clamp(image.Height / 9, 42, 96);
        var fontSize = Math.Clamp(image.Width / 48f, 14f, 30f);
        var font = SystemFonts.CreateFont("Arial", fontSize, FontStyle.Bold);
        image.Mutate(context =>
        {
            context.Fill(SixLabors.ImageSharp.Color.FromRgba(255, 250, 247, 175), new SixLabors.ImageSharp.Rectangle(0, image.Height - bandHeight, image.Width, bandHeight));
            context.DrawText(watermark, font, SixLabors.ImageSharp.Color.FromRgba(112, 39, 52, 235), new PointF(18, image.Height - bandHeight + (bandHeight - fontSize) / 2));
        });

        if (System.IO.File.Exists(logoPath))
        {
            using var logo = SixLabors.ImageSharp.Image.Load<Rgba32>(logoPath);
            var maxLogoSize = Math.Clamp(bandHeight - 12, 28, 64);
            logo.Mutate(context => context.Resize(new ResizeOptions { Size = new Size(maxLogoSize, maxLogoSize), Mode = ResizeMode.Max }));
            image.Mutate(context => context.DrawImage(logo, new SixLabors.ImageSharp.Point(Math.Max(4, image.Width - logo.Width - 12), Math.Max(4, image.Height - bandHeight + (bandHeight - logo.Height) / 2)), 0.7f));
        }

        using var output = new MemoryStream();
        if (extension == ".png") image.Save(output, new PngEncoder());
        else image.Save(output, new JpegEncoder { Quality = 90 });
        return output.ToArray();
    }

    private static byte[] WatermarkWordDocument(byte[] content, string watermark)
    {
        using var output = new MemoryStream();
        output.Write(content);
        output.Position = 0;
        using (var document = WordprocessingDocument.Open(output, true))
        {
            var mainPart = document.MainDocumentPart;
            var documentRoot = mainPart?.Document;
            var body = documentRoot?.Body;
            if (mainPart is null || documentRoot is null || body is null) return content;

            var headers = mainPart.HeaderParts.ToList();
            if (headers.Count == 0)
            {
                var headerPart = mainPart.AddNewPart<HeaderPart>();
                headerPart.Header = new Word.Header();
                headers.Add(headerPart);
                var section = body.Elements<Word.SectionProperties>().LastOrDefault();
                if (section is null)
                {
                    section = new Word.SectionProperties();
                    body.Append(section);
                }
                section.Append(new Word.HeaderReference { Type = HeaderFooterValues.Default, Id = mainPart.GetIdOfPart(headerPart) });
            }

            foreach (var headerPart in headers)
            {
                headerPart.Header ??= new Word.Header();
                headerPart.Header.Append(new Word.Paragraph(
                    new Word.ParagraphProperties(new Word.Justification { Val = Word.JustificationValues.Center }),
                    new Word.Run(new Word.RunProperties(new Word.Color { Val = "7A2637" }, new Word.FontSize { Val = "20" }),
                        new Word.Text(watermark) { Space = SpaceProcessingModeValues.Preserve })));
                headerPart.Header.Save();
            }
            documentRoot.Save();
        }
        return output.ToArray();
    }

    private static byte[] WatermarkSpreadsheet(byte[] content, string watermark)
    {
        using var output = new MemoryStream();
        output.Write(content);
        output.Position = 0;
        using (var document = SpreadsheetDocument.Open(output, true))
        {
            var escaped = watermark.Replace("&", "&&", StringComparison.Ordinal);
            foreach (var worksheetPart in document.WorkbookPart?.WorksheetParts ?? [])
            {
                var worksheet = worksheetPart.Worksheet;
                if (worksheet is null) continue;
                var headerFooter = worksheet.GetFirstChild<Excel.HeaderFooter>();
                if (headerFooter is null)
                {
                    headerFooter = new Excel.HeaderFooter();
                    var trailingElement = worksheet.ChildElements.FirstOrDefault(element => element.LocalName is "rowBreaks" or "colBreaks" or "customSheetViews" or "drawing" or "legacyDrawing" or "legacyDrawingHF" or "picture" or "oleObjects" or "controls" or "webPublishItems" or "tableParts" or "extLst");
                    if (trailingElement is null) worksheet.Append(headerFooter);
                    else worksheet.InsertBefore(headerFooter, trailingElement);
                }
                headerFooter.OddHeader = new Excel.OddHeader($"&C&\"Arial,Bold\"&12 {escaped}");
                headerFooter.EvenHeader = new Excel.EvenHeader($"&C&\"Arial,Bold\"&12 {escaped}");
                worksheet.Save();
            }
        }
        return output.ToArray();
    }
}
