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

public static class DocumentWatermarkService
{
    public static async Task<byte[]> CreateWatermarkedCopyAsync(Stream original, string fileName,
        string viewerName, DateTime accessedAt, string webRootPath, CancellationToken cancellationToken = default)
    {
        await using var buffer = new MemoryStream();
        await original.CopyToAsync(buffer, cancellationToken);
        var content = buffer.ToArray();
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var watermark = $"MS Cosmetics | {viewerName} | {accessedAt.ToString("yyyy-MM-dd HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture)}";

        return extension switch
        {
            ".pdf" => WatermarkPdf(content, watermark, webRootPath),
            ".png" or ".jpg" or ".jpeg" => WatermarkImage(content, extension, watermark, webRootPath),
            ".docx" => WatermarkWordDocument(content, watermark),
            ".xlsx" => WatermarkSpreadsheet(content, watermark),
            _ => content
        };
    }

    private static byte[] WatermarkPdf(byte[] content, string watermark, string webRootPath)
    {
        using var input = new MemoryStream(content);
        using var document = new Aspose.Pdf.Document(input);
        var logoPath = Path.Combine(webRootPath, "images", "ms-wand-tilted-blush-logo.png");

        foreach (Aspose.Pdf.Page page in document.Pages)
        {
            var stamp = new Aspose.Pdf.TextStamp(watermark)
            {
                Background = false,
                Opacity = 0.22,
                RotateAngle = 45,
                XIndent = 44,
                YIndent = page.PageInfo.Height / 2
            };
            stamp.TextState.Font = FontRepository.FindFont("Arial");
            stamp.TextState.FontSize = 20;
            stamp.TextState.FontStyle = FontStyles.Bold;
            stamp.TextState.ForegroundColor = Aspose.Pdf.Color.DarkRed;
            page.AddStamp(stamp);

            if (System.IO.File.Exists(logoPath))
            {
                page.AddStamp(new ImageStamp(logoPath)
                {
                    Width = 54,
                    Height = 54,
                    XIndent = 26,
                    YIndent = 24,
                    Opacity = 0.28
                });
            }
        }

        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
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
