using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using TenderSystem.Areas.Shared.Models.Reports;

namespace TenderSystem.Services.Reports;

public class ReportPdfWriter
{
    public async Task<byte[]> WriteAsync(ReportPageViewModel report, string webRootPath, CancellationToken ct)
    {
        // PdfPig is already installed. Generate every page instead of truncating an evaluation-library preview.
        using var document = new PdfDocumentBuilder();
        var fontPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf");
        if (!File.Exists(fontPath)) fontPath = "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf";
        var font = File.Exists(fontPath)
            ? document.AddTrueTypeFont(await File.ReadAllBytesAsync(fontPath, ct))
            : document.AddStandard14Font(Standard14Font.Helvetica);
        var table = ReportTable.Create(report);
        PdfPageBuilder page = null!;
        double y = 0;
        var pageNumber = 0;
        var currentRecord = "";
        const double left = 42, width = 511, bottom = 52;

        void NewPage(bool continued = false)
        {
            page = document.AddPage(595, 842);
            pageNumber++;
            page.SetTextAndFillColor(241, 230, 228);
            page.AddText("MS.WAND", 48, new PdfPoint(170, 400), font);
            page.SetTextAndFillColor(119, 47, 58);
            page.AddText("MS. WAND | Reports", 11, new PdfPoint(left, 806), font);
            page.SetTextAndFillColor(110, 95, 90);
            page.AddText("Page " + pageNumber, 9, new PdfPoint(500, 806), font);
            page.AddText(report.GeneratedAt.ToString("dd MMM yyyy, HH:mm") + " UTC", 8, new PdfPoint(left, 27), font);
            page.AddText("MS.WAND", 8, new PdfPoint(490, 27), font);
            y = 775;
            if (continued && currentRecord.Length > 0)
            {
                page.AddText("Continued: " + currentRecord[..Math.Min(60, currentRecord.Length)], 9, new PdfPoint(left, y), font);
                y -= 22;
            }
        }

        double Measure(string text, double size)
        {
            var letters = page.MeasureText(text, size, new PdfPoint(0, 0), font);
            return letters.Count == 0 ? 0 : letters.Max(letter => letter.BoundingBox.Right) - letters.Min(letter => letter.BoundingBox.Left);
        }

        List<string> Wrap(string text, double size, double available)
        {
            var lines = new List<string>();
            foreach (var paragraph in text.Replace("\r", "").Replace("\t", " ").Split('\n'))
            {
                var line = "";
                foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    var candidate = line.Length == 0 ? word : line + " " + word;
                    if (Measure(candidate, size) <= available) { line = candidate; continue; }
                    if (line.Length > 0) { lines.Add(line); line = ""; }
                    foreach (var character in word)
                    {
                        if (line.Length > 0 && Measure(line + character, size) > available)
                        { lines.Add(line); line = ""; }
                        line += character;
                    }
                }
                lines.Add(line);
            }
            return lines;
        }

        void Paragraph(string text, double size = 10, bool heading = false)
        {
            foreach (var line in Wrap(text, size, width))
            {
                ct.ThrowIfCancellationRequested();
                if (y < bottom) NewPage(true);
                if (heading) page.SetTextAndFillColor(119, 47, 58);
                else page.SetTextAndFillColor(65, 48, 43);
                page.AddText(line, size, new PdfPoint(left, y), font);
                y -= size + 5;
            }
        }

        void Field(string label, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var lines = Wrap(value, 10, 345);
            var height = lines.Count * 15 + 7;
            if (y - Math.Min(height, 100) < bottom) NewPage(true);
            page.SetTextAndFillColor(115, 82, 73);
            var labelLines = Wrap(label, 9, 150);
            for (var i = 0; i < Math.Max(lines.Count, labelLines.Count); i++)
            {
                ct.ThrowIfCancellationRequested();
                if (y < bottom) NewPage(true);
                if (i < labelLines.Count)
                {
                    page.SetTextAndFillColor(115, 82, 73);
                    page.AddText(labelLines[i], 9, new PdfPoint(left, y), font);
                }
                if (i < lines.Count)
                {
                    page.SetTextAndFillColor(65, 48, 43);
                    page.AddText(lines[i], 10, new PdfPoint(left + 165, y), font);
                }
                y -= 15;
            }
            y -= 7;
        }

        NewPage();
        Paragraph(report.Selected?.Title ?? "Report", 19, true);
        Paragraph("Generated by " + report.GeneratedBy, 9);
        Paragraph(report.TotalCount + " matching records", 10);
        foreach (var filter in report.AppliedFilters) Field(filter.Label, filter.Value);
        foreach (var metric in report.Metrics) Field(metric.Label, metric.Value);
        foreach (var note in report.CoverageNotes) Field("Data coverage", note);
        y -= 8;
        for (var r = 0; r < report.Rows.Count; r++)
        {
            if (y < 150) NewPage();
            var record = report.Rows[r];
            currentRecord = record.Reference;
            page.SetStrokeColor(210, 189, 179);
            page.DrawLine(new PdfPoint(left, y), new PdfPoint(left + width, y), .6);
            y -= 20;
            Paragraph(table.Rows[r][0], 12, true);
            for (var c = 1; c < table.Headers.Count; c++) Field(table.Headers[c], table.Rows[r][c]);
            foreach (var detail in record.MoreDetails) Field(detail.Label, detail.Value);
            y -= 10;
        }
        if (report.Rows.Count == 0) Paragraph("No matching records.");
        return document.Build();
    }
}
