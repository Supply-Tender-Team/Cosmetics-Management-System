using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;

namespace TenderSystem.Services.Knowledge;

/// <summary>
/// Gets the plain text out of a document an Admin uploads to the knowledge base: PDF, Word, Excel, or a text file (.txt, .md, .csv).
/// Pictures and scanned PDFs have no text to read. Word and Excel files with macros, links or embedded objects, and zip bombs, are refused.
/// </summary>
public static class KnowledgeTextReader
{
    public static readonly string[] Extensions = [".pdf", ".docx", ".xlsx", ".txt", ".md", ".csv"];

    private const int MaxChars = 600_000;
    private const int MaxZipEntries = 2000;
    private const long MaxUncompressedBytes = 200L * 1024 * 1024;

    /// <summary>Returns the cleaned text, or null with a message for the Admin when the file cannot be read.</summary>
    public static (string? Text, string? Error) Read(byte[] bytes, string extension)
    {
        extension = extension.ToLowerInvariant();
        if (!Extensions.Contains(extension))
            return (null, $"This file type is not supported. Use {string.Join(", ", Extensions)}.");

        try
        {
            string raw;
            switch (extension)
            {
                case ".pdf": raw = ReadPdf(bytes); break;
                case ".docx":
                    if (!ZipIsSafe(bytes)) return (null, "This Word file is not safe to read (macros, links or embedded objects).");
                    raw = ReadWord(bytes); break;
                case ".xlsx":
                    if (!ZipIsSafe(bytes)) return (null, "This Excel file is not safe to read (macros, links or embedded objects).");
                    raw = ReadExcel(bytes); break;
                default:
                    using (var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                        raw = reader.ReadToEnd();
                    break;
            }

            var text = Clean(raw);
            if (text.Length > MaxChars) text = text[..MaxChars];
            if (text.Length < 40)
                return (null, extension == ".pdf" ? "No text found. A scanned PDF (a picture of a page) cannot be read." : "The file has no readable text.");
            return (text, null);
        }
        catch (Exception)
        {
            // The parsers throw many kinds of exceptions for a damaged or hostile file; none of them may break the page.
            return (null, "The file is damaged or cannot be read.");
        }
    }

    private static string ReadPdf(byte[] bytes)
    {
        var sb = new StringBuilder();
        using var pdf = PdfDocument.Open(bytes);
        var pages = 0;
        foreach (var page in pdf.GetPages())
        {
            if (++pages > 400 || sb.Length > MaxChars) break;
            sb.Append(page.Text).Append("\n\n");
        }
        return sb.ToString();
    }

    private static string ReadWord(byte[] bytes)
    {
        using var document = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        var body = document.MainDocumentPart?.Document?.Body;
        if (body == null) return "";
        var sb = new StringBuilder();
        foreach (var paragraph in body.Descendants<Paragraph>())
        {
            sb.Append(string.Concat(paragraph.Descendants<DocumentFormat.OpenXml.Wordprocessing.Text>().Select(t => t.Text))).Append('\n');
            if (sb.Length > MaxChars) break;
        }
        return sb.ToString();
    }

    private static string ReadExcel(byte[] bytes)
    {
        using var document = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        var workbook = document.WorkbookPart;
        if (workbook?.Workbook?.Sheets == null) return "";
        var shared = workbook.SharedStringTablePart?.SharedStringTable;
        var sb = new StringBuilder();
        foreach (var sheet in workbook.Workbook.Sheets.Elements<Sheet>().Take(10))
        {
            if (sheet.Id?.Value is null || workbook.GetPartById(sheet.Id.Value) is not WorksheetPart part) continue;
            sb.Append("Sheet ").Append(sheet.Name?.Value).Append(":\n");
            var rows = 0;
            foreach (var row in part.Worksheet.Descendants<Row>())
            {
                if (++rows > 3000) break;
                var line = string.Join(" | ", row.Elements<Cell>().Take(40).Select(c => CellText(c, shared)).Where(t => t.Length > 0));
                if (line.Length > 0) sb.Append(line).Append('\n');
                if (sb.Length > MaxChars) return sb.ToString();
            }
        }
        return sb.ToString();
    }

    private static string CellText(Cell cell, SharedStringTable? shared)
    {
        var value = cell.CellValue?.Text;
        if (cell.DataType?.Value == CellValues.SharedString)
            return shared != null && int.TryParse(value, out var i) && i >= 0 && i < shared.ChildElements.Count ? shared.ChildElements[i].InnerText : "";
        if (cell.DataType?.Value == CellValues.InlineString) return cell.InlineString?.InnerText ?? "";
        if (value != null && decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return number.ToString("0.####", CultureInfo.InvariantCulture);
        return value ?? "";
    }

    /// <summary>No macros, embedded objects or links, not a zip bomb.</summary>
    private static bool ZipIsSafe(byte[] bytes)
    {
        try
        {
            using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            if (zip.Entries.Count > MaxZipEntries) return false;
            long total = 0;
            foreach (var entry in zip.Entries)
            {
                total += entry.Length;
                var name = entry.FullName.ToLowerInvariant();
                if (name.EndsWith(".bin") || name.Contains("vbaproject") || name.Contains("/externallinks/") || name.Contains("/embeddings/")) return false;
            }
            return total <= MaxUncompressedBytes;
        }
        catch (InvalidDataException) { return false; }
    }

    /// <summary>Control characters removed, spaces collapsed, blank lines dropped.</summary>
    private static string Clean(string text)
    {
        var t = Regex.Replace(text ?? "", @"[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F]", " ");
        t = Regex.Replace(t, @"[ \t ]+", " ");
        t = Regex.Replace(t, @"\s*\n\s*(\n\s*)*", "\n");
        return t.Trim();
    }
}
