using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace TenderSystem.Services.Ai;

/// <summary>
/// AI-09: the Excel templates of the command center. The server MAKES the template (headers, a Help sheet with the allowed values) and
/// READS the file the Admin brings back. The model never sees the file: this class is plain code.
///
/// Reading is defensive: only a real .xlsx (no macros, no external links, not a zip bomb), the headers must match the template exactly,
/// every cell is read as plain text (a formula cell is refused), control characters are removed, text is length-capped, and the file
/// is processed in memory and never stored.
/// </summary>
public static class AiExcelTemplates
{
    public const string AccountsKind = "accounts";
    public const string MessagesKind = "messages";
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static readonly string[] AccountColumns = ["Full name", "Email", "Role", "Department", "Job title", "Phone"];
    public static readonly string[] MessageColumns = ["Email", "Subject", "Message"];

    public static string[] ColumnsFor(string kind) => kind == AccountsKind ? AccountColumns : MessageColumns;

    private const long MaxUncompressedBytes = 30L * 1024 * 1024;
    private const int MaxZipEntries = 300;
    private const int MaxCellChars = 2000;

    // ===================================================================== the template

    /// <summary>The template: a header row (nothing else, so an example row can never be created by accident) and a Help sheet.</summary>
    public static byte[] CreateTemplate(string kind, IReadOnlyList<string> roleNames, IReadOnlyList<string> departmentNames)
    {
        var columns = ColumnsFor(kind);
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
                new CellFormats(new CellFormat(), new CellFormat { FontId = 1, ApplyFont = true },
                    new CellFormat { Alignment = new Alignment { WrapText = true, Vertical = VerticalAlignmentValues.Top }, ApplyAlignment = true }));

            // ---- Help sheet (second): explanation + the allowed values, used by the drop-downs of the Template sheet
            var helpPart = workbook.AddNewPart<WorksheetPart>();
            var helpData = new SheetData();
            var helpLines = kind == AccountsKind
                ? new[]
                {
                    "How to use this template",
                    "1. Fill the sheet 'Template': one person per row, starting under the headers. Do not change or reorder the headers.",
                    "2. Role and Department must be one of the values in the columns on the right (the cells have a drop-down).",
                    "3. Phone is a Jordanian mobile number, for example 0791234567.",
                    "4. Do not use formulas: type the values.",
                    "5. Attach the file in the Command tab. You will see a preview, and nothing is created until you approve it with your fingerprint or face.",
                    "Each person receives an email with a temporary password and must change it at the first sign-in.",
                }
                : new[]
                {
                    "How to use this template",
                    "1. Fill the sheet 'Template': one employee per row, starting under the headers. Do not change or reorder the headers.",
                    "2. Email must be the work email of an existing, active employee.",
                    "3. Subject and Message are optional. When a row has none, the common text from your command is used (for example: 'Tell them the system is down tomorrow').",
                    "4. Do not use formulas: type the values.",
                    "5. Attach the file in the Command tab. You will see a preview, and nothing is sent until you approve it with your fingerprint or face.",
                };
            var helpCells = new SortedDictionary<uint, List<Cell>>();
            void Put(string column, uint row, string text, uint style)
            {
                if (!helpCells.TryGetValue(row, out var list)) helpCells[row] = list = [];
                list.Add(MakeCell(column + row, text, style));
            }
            for (var i = 0; i < helpLines.Length; i++) Put("A", (uint)i + 1, helpLines[i], i == 0 ? 1u : 0u);
            if (kind == AccountsKind)
            {
                // the allowed values: roles in column D and departments in column E, from row 2 (the drop-downs of the Template sheet read them)
                Put("D", 1, "Roles", 1);
                Put("E", 1, "Departments", 1);
                for (var i = 0; i < roleNames.Count; i++) Put("D", (uint)i + 2, roleNames[i], 0);
                for (var i = 0; i < departmentNames.Count; i++) Put("E", (uint)i + 2, departmentNames[i], 0);
            }
            foreach (var (rowIndex, cells) in helpCells) helpData.Append(new Row(cells) { RowIndex = rowIndex });
            helpPart.Worksheet = new Worksheet(new Columns(new Column { Min = 1, Max = 1, Width = 110, CustomWidth = true },
                new Column { Min = 4, Max = 5, Width = 28, CustomWidth = true }), helpData);

            // ---- Template sheet (first): only the header row
            var part = workbook.AddNewPart<WorksheetPart>();
            var data = new SheetData();
            data.Append(MakeRow(1, columns.Select((c, i) => ((uint)i, c, 1u)).ToArray()));
            var widths = new Columns();
            for (var i = 0; i < columns.Length; i++)
                widths.Append(new Column { Min = (uint)i + 1, Max = (uint)i + 1, Width = columns[i] == "Message" ? 60 : 26, CustomWidth = true });

            var validations = new DataValidations();
            if (kind == AccountsKind)
            {
                if (roleNames.Count > 0)
                    validations.Append(new DataValidation
                    {
                        Type = DataValidationValues.List, AllowBlank = true, ShowErrorMessage = true, ErrorTitle = "Role", Error = "Pick a role from the list.",
                        SequenceOfReferences = new ListValue<StringValue> { InnerText = "C2:C201" },
                        Formula1 = new Formula1($"Help!$D$2:$D${roleNames.Count + 1}"),
                    });
                if (departmentNames.Count > 0)
                    validations.Append(new DataValidation
                    {
                        Type = DataValidationValues.List, AllowBlank = true, ShowErrorMessage = true, ErrorTitle = "Department", Error = "Pick a department from the list.",
                        SequenceOfReferences = new ListValue<StringValue> { InnerText = "D2:D201" },
                        Formula1 = new Formula1($"Help!$E$2:$E${departmentNames.Count + 1}"),
                    });
            }
            part.Worksheet = validations.ChildElements.Count > 0 ? new Worksheet(widths, data, validations) : new Worksheet(widths, data);

            var sheets = workbook.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet { Id = workbook.GetIdOfPart(part), SheetId = 1, Name = "Template" });
            sheets.Append(new Sheet { Id = workbook.GetIdOfPart(helpPart), SheetId = 2, Name = "Help" });
            workbook.Workbook.Save();
        }
        return stream.ToArray();
    }

    private static Row MakeRow(uint index, IEnumerable<(uint Column, string Text, uint Style)> cells)
    {
        var row = new Row { RowIndex = index };
        foreach (var (column, text, style) in cells) row.Append(MakeCell(ColumnName((int)column) + index, text, style));
        return row;
    }

    private static Cell MakeCell(string reference, string text, uint style) => new()
    {
        CellReference = reference,
        DataType = CellValues.InlineString,
        StyleIndex = style,
        InlineString = new InlineString(new Text(text) { Space = SpaceProcessingModeValues.Preserve }),
    };

    private static string ColumnName(int zeroBased)
    {
        var name = "";
        for (var n = zeroBased + 1; n > 0; n = (n - 1) / 26) name = (char)('A' + (n - 1) % 26) + name;
        return name;
    }

    // ===================================================================== reading the file

    public sealed record ExcelRows(string? Error, List<(int ExcelRow, string[] Cells)> Rows);

    /// <summary>
    /// Reads the data rows (blank rows are skipped). <paramref name="maxRows"/> is the most data rows allowed. Any problem with the FILE
    /// itself comes back as Error with a plain message; problems with single rows are the caller's job.
    /// </summary>
    public static ExcelRows Read(byte[] file, string kind, int maxRows)
    {
        // ---- is it really a plain .xlsx?
        if (file.Length < 4 || file[0] != 0x50 || file[1] != 0x4B)
            return Fail("This is not an Excel (.xlsx) file. Please use the template.");
        try
        {
            using var zip = new ZipArchive(new MemoryStream(file), ZipArchiveMode.Read);
            if (zip.Entries.Count > MaxZipEntries) return Fail("This file is not a normal Excel file.");
            long total = 0;
            foreach (var entry in zip.Entries)
            {
                total += entry.Length;
                var name = entry.FullName.ToLowerInvariant();
                if (name.EndsWith(".bin") || name.Contains("vbaproject") || name.StartsWith("xl/externallinks/") || name.StartsWith("xl/embeddings/"))
                    return Fail("The file contains macros, embedded objects or links. Please save it as a plain .xlsx file and use only the template.");
            }
            if (total > MaxUncompressedBytes) return Fail("The file is too big when opened. Please use only the template.");
            var types = zip.GetEntry("[Content_Types].xml");
            if (types == null) return Fail("This is not an Excel (.xlsx) file. Please use the template.");
            using (var reader = new StreamReader(types.Open()))
                if (reader.ReadToEnd().Contains("macroEnabled", StringComparison.OrdinalIgnoreCase))
                    return Fail("Macro-enabled files (.xlsm) are not accepted. Please save it as a plain .xlsx file.");
        }
        catch (InvalidDataException)
        {
            return Fail("This is not an Excel (.xlsx) file. Please use the template.");
        }

        var headers = ColumnsFor(kind);
        var rows = new List<(int, string[])>();
        try
        {
            using var document = SpreadsheetDocument.Open(new MemoryStream(file), false);
            var workbook = document.WorkbookPart;
            var sheets = workbook?.Workbook?.Sheets?.Elements<Sheet>().ToList();
            if (workbook == null || sheets == null || sheets.Count == 0) return Fail("The file has no sheet. Please use the template.");
            var sheet = sheets.FirstOrDefault(s => string.Equals(s.Name?.Value, "Template", StringComparison.OrdinalIgnoreCase)) ?? sheets[0];
            if (sheet.Id?.Value is null || workbook.GetPartById(sheet.Id.Value) is not WorksheetPart sheetPart) return Fail("The file has no readable sheet. Please use the template.");
            var shared = workbook.SharedStringTablePart?.SharedStringTable;

            var headerChecked = false;
            foreach (var row in sheetPart.Worksheet?.Descendants<Row>() ?? Enumerable.Empty<Row>())
            {
                var cells = new string[headers.Length];
                var any = false;
                foreach (var cell in row.Elements<Cell>())
                {
                    var column = ColumnIndex(cell.CellReference?.Value);
                    if (column < 0 || column >= headers.Length) continue;           // columns outside the template are ignored
                    if (cell.CellFormula != null) return Fail($"Row {row.RowIndex?.Value} uses a formula. Please type the values (copy, then paste as values).");
                    var text = Clean(CellText(cell, shared));
                    cells[column] = text;
                    if (text.Length > 0) any = true;
                }
                var number = (int)(row.RowIndex?.Value ?? 0);

                if (!headerChecked)
                {
                    if (!any) continue;                                              // blank lines above the headers
                    for (var i = 0; i < headers.Length; i++)
                        if (!string.Equals((cells[i] ?? "").Trim(), headers[i], StringComparison.OrdinalIgnoreCase))
                            return Fail($"The headers do not match the template (column {ColumnName(i)} should be \"{headers[i]}\"). Please download the template again and use it as it is.");
                    headerChecked = true;
                    continue;
                }

                if (!any) continue;
                if (rows.Count >= maxRows) return Fail($"The file has more than {maxRows} rows. Please split it into smaller files.");
                rows.Add((number, cells.Select(c => c ?? "").ToArray()));
            }
            if (!headerChecked) return Fail("The sheet is empty. Please use the template.");
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or InvalidDataException or IOException or FormatException or InvalidOperationException or System.Xml.XmlException)
        {
            return Fail("The file could not be read. Please use the template and save it as .xlsx.");
        }
        if (rows.Count == 0) return Fail("The file has no data rows under the headers.");
        return new ExcelRows(null, rows);
    }

    private static ExcelRows Fail(string message) => new(message, []);

    private static string CellText(Cell cell, SharedStringTable? shared)
    {
        var value = cell.CellValue?.Text;
        switch (cell.DataType?.Value)
        {
            case var t when t == CellValues.SharedString:
                return shared != null && int.TryParse(value, out var index) && index >= 0 && index < shared.ChildElements.Count
                    ? shared.ChildElements[index].InnerText : "";
            case var t when t == CellValues.InlineString:
                return cell.InlineString?.InnerText ?? "";
            case var t when t == CellValues.Boolean:
                return value == "1" ? "TRUE" : "FALSE";
            default:
                // a number such as a phone number typed without its leading zero: keep the digits, never "7.91E+08"
                if (value != null && decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number == decimal.Truncate(number))
                    return number.ToString("0", CultureInfo.InvariantCulture);
                return value ?? "";
        }
    }

    private static int ColumnIndex(string? reference)
    {
        if (string.IsNullOrEmpty(reference)) return -1;
        var n = 0;
        foreach (var ch in reference)
        {
            if (!char.IsLetter(ch)) break;
            n = n * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        }
        return n - 1;
    }

    /// <summary>Control characters removed, edges trimmed, length capped.</summary>
    private static string Clean(string? text)
    {
        var t = Regex.Replace(text ?? "", @"[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F]", "").Replace("\r", "").Trim();
        return t.Length <= MaxCellChars ? t : t[..MaxCellChars];
    }

    /// <summary>The first 12 hex characters of the SHA-256 of the file: enough to recognise a file in the audit log, without keeping any of its content.</summary>
    public static string Fingerprint(byte[] file) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(file))[..12].ToLowerInvariant();
}
