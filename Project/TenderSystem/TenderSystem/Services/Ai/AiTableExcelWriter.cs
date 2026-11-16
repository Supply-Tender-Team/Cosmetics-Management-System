using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace TenderSystem.Services.Ai;

/// <summary>
/// AI-11: "Export to Excel" for a table the assistant showed in the chat. The browser posts the table it is showing (title, columns, rows),
/// and this makes an .xlsx of exactly that. Every cell is written as TEXT (inline string), so nothing a cell contains can run as a formula.
/// Same pattern as Services/Reports/ReportExcelWriter.cs. The caps keep the endpoint from being used to build huge files.
/// </summary>
public static class AiTableExcelWriter
{
    public const int MaxColumns = 26;
    public const int MaxRows = 500;
    public const int MaxCellChars = 500;
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static byte[] Write(string? title, IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbook = document.AddWorkbookPart();
            workbook.Workbook = new Workbook();

            var styles = workbook.AddNewPart<WorkbookStylesPart>();
            styles.Stylesheet = new Stylesheet(
                new Fonts(new Font(), new Font(new Bold())),
                new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }), new Fill(new PatternFill { PatternType = PatternValues.Gray125 })),
                new Borders(new Border()), new CellStyleFormats(new CellFormat()),
                new CellFormats(new CellFormat(), new CellFormat { FontId = 1, ApplyFont = true }));

            var part = workbook.AddNewPart<WorksheetPart>();
            var data = new SheetData();
            uint rowIndex = 1;
            if (!string.IsNullOrWhiteSpace(title))
            {
                data.Append(MakeRow(rowIndex++, [Clean(title)], 1));
                rowIndex++;                                                       // a blank line between the title and the table
            }
            data.Append(MakeRow(rowIndex++, columns.Select(Clean).ToList(), 1));
            foreach (var row in rows)
                data.Append(MakeRow(rowIndex++, row.Take(columns.Count).Select(Clean).ToList(), 0));

            var widths = new Columns();
            for (var i = 0; i < columns.Count; i++)
                widths.Append(new Column { Min = (uint)i + 1, Max = (uint)i + 1, Width = i == 0 ? 30 : 22, CustomWidth = true });
            part.Worksheet = new Worksheet(widths, data);

            var sheets = workbook.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet { Id = workbook.GetIdOfPart(part), SheetId = 1, Name = "Assistant table" });
            workbook.Workbook.Save();
        }
        return stream.ToArray();
    }

    private static Row MakeRow(uint index, IReadOnlyList<string> values, uint style)
    {
        var row = new Row { RowIndex = index };
        for (var i = 0; i < values.Count; i++)
            row.Append(new Cell
            {
                CellReference = ColumnName(i) + index,
                DataType = CellValues.InlineString,
                StyleIndex = style,
                InlineString = new InlineString(new Text(values[i]) { Space = SpaceProcessingModeValues.Preserve }),
            });
        return row;
    }

    private static string ColumnName(int zeroBased)
    {
        var name = "";
        for (var n = zeroBased + 1; n > 0; n = (n - 1) / 26) name = (char)('A' + (n - 1) % 26) + name;
        return name;
    }

    /// <summary>Control characters removed (they are not allowed in an xlsx), length capped.</summary>
    private static string Clean(string? text)
    {
        var t = Regex.Replace(text ?? "", @"[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F]", "").Trim();
        return t.Length <= MaxCellChars ? t : t[..MaxCellChars];
    }
}
