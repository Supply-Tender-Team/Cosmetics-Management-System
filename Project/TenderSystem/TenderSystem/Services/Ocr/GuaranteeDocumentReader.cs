using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf;
using Aspose.Pdf.Devices;
using TenderSystem.Infrastructure;
using TesseractOCR;
using TesseractOCR.Enums;
using TesseractOCR.Pix;
using UglyToad.PdfPig;

namespace TenderSystem.Services.Ocr;

public sealed record GuaranteeOcrFields(
    string? ReferenceNumber,
    string? IssuerName,
    string? BeneficiaryName,
    decimal? Amount,
    string? Currency,
    DateTime? IssuedAt,
    DateTime? ExpiresAt,
    string? GuarantorName,
    string? GuarantorNationalNumber,
    string? GuarantorAddress,
    string? GuarantorPhone);

public sealed record GuaranteeOcrResult(GuaranteeOcrFields Fields, string Text);

/// <summary>On-server Arabic/English OCR for supplier guarantee files. Files are read in memory and never persisted or sent to an external API.</summary>
public sealed class GuaranteeDocumentReader(IWebHostEnvironment environment)
{
    private const long MaximumFileBytes = 10L * 1024 * 1024;
    private const int MaximumPdfPages = 5;

    public async Task<GuaranteeOcrResult> ReadAsync(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length <= 0 || file.Length > MaximumFileBytes)
            throw new InvalidOperationException("Choose a non-empty file smaller than 10 MB.");

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension is not (".pdf" or ".png" or ".jpg" or ".jpeg"))
            throw new InvalidOperationException("Automatic reading supports PDF, JPG, and PNG files.");

        await using var input = file.OpenReadStream();
        using var memory = new MemoryStream();
        await input.CopyToAsync(memory, cancellationToken);
        if (memory.Length > MaximumFileBytes)
            throw new InvalidOperationException("Choose a file smaller than 10 MB.");

        var bytes = memory.ToArray();
        var tessDataDirectory = Path.Combine(environment.ContentRootPath, "Services", "Ocr", "tessdata");
        if (!File.Exists(Path.Combine(tessDataDirectory, "ara.traineddata"))
            || !File.Exists(Path.Combine(tessDataDirectory, "eng.traineddata")))
            throw new InvalidOperationException("Arabic and English OCR data is missing from the application files.");

        string extracted;
        try
        {
            extracted = await Task.Run(() => ExtractText(bytes, extension, tessDataDirectory), cancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException("Reading this file took too long. Try a clearer image or a shorter PDF.");
        }
        var normalized = NormalizeDigits(extracted);
        return new GuaranteeOcrResult(ParseFields(normalized), normalized[..Math.Min(normalized.Length, 6000)]);
    }

    private static string ExtractText(byte[] bytes, string extension, string tessDataDirectory)
    {
        using var engine = new Engine(tessDataDirectory, "ara+eng", EngineMode.LstmOnly);
        engine.DefaultPageSegMode = PageSegMode.Auto;
        var output = new StringBuilder();

        void ReadImage(byte[] imageBytes)
        {
            using var image = TesseractOCR.Pix.Image.LoadFromMemory(imageBytes);
            using var page = engine.Process(image);
            output.AppendLine(page.Text);
        }

        if (extension != ".pdf")
        {
            ReadImage(bytes);
            return output.ToString();
        }

        // Searchable PDFs give cleaner text than OCR and can be read without the PDF renderer.
        try
        {
            using var searchablePdf = PdfDocument.Open(bytes);
            var searchableText = string.Join('\n', searchablePdf.GetPages().Take(MaximumPdfPages).Select(page => page.Text));
            if (!string.IsNullOrWhiteSpace(searchableText) && searchableText.Count(char.IsLetterOrDigit) > 20)
                return searchableText;
        }
        catch
        {
            // A damaged or image-only text layer falls back to page OCR below.
        }

        using var pdf = new Document(new MemoryStream(bytes));
        var device = new PngDevice(new Resolution(160));
        var pagesToRead = Math.Min(pdf.Pages.Count, MaximumPdfPages);
        for (var pageNumber = 1; pageNumber <= pagesToRead; pageNumber++)
        {
            using var pageImage = new MemoryStream();
            device.Process(pdf.Pages[pageNumber], pageImage);
            ReadImage(pageImage.ToArray());
        }

        return output.ToString();
    }

    private static GuaranteeOcrFields ParseFields(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => Regex.Replace(line, @"\s+", " "))
            .ToArray();

        string? Find(params string[] labels) => FindLabeledValue(lines, labels);

        var reference = Find("reference number", "reference no", "guarantee no", "رقم الكفالة", "رقم المرجع التجريبي", "رقم المرجع");
        var issuer = Find("issuing bank", "bank name", "اسم البنك", "البنك المصدر", "الجهة المصدرة", "يكفل بنك");
        var beneficiary = Find("beneficiary", "in favor of", "المستفيد", "الجهة المستفيدة", "لصالح", "السادة");
        var guarantor = Find("guarantor name", "اسم الكفيل", "اسم الكفيل المالي");
        var nationalNumber = Find("national number", "national no", "رقم وطني", "الرقم الوطني", "رقم الهوية");
        var address = Find("guarantor address", "address", "مكان الإقامة", "العنوان");
        var phone = Find("phone", "telephone", "الهاتف", "رقم الهاتف", "رقم الجوال");

        var amount = ParseAmount(text);
        var issueDate = ParseDateForLabels(lines, "issue date", "issued on", "من تاريخ", "تاريخ الإصدار", "تاريخ الاصدار");
        var expiryDate = ParseDateForLabels(lines, "expiry date", "expires", "to date", "إلى تاريخ", "حتى تاريخ", "تاريخ الانتهاء", "تاريخ الاستحقاق");
        var currency = Regex.IsMatch(text, @"\b(?:JOD|JD)\b|دينار", RegexOptions.IgnoreCase) ? "JOD"
            : Regex.IsMatch(text, @"\bUSD\b|دولار", RegexOptions.IgnoreCase) ? "USD" : null;

        return new GuaranteeOcrFields(
            CleanValue(reference), CleanValue(issuer), CleanValue(beneficiary), amount, currency,
            issueDate, expiryDate, CleanValue(guarantor),
            CleanValue(nationalNumber), CleanValue(address), CleanValue(phone));
    }

    private static string? FindLabeledValue(IReadOnlyList<string> lines, IEnumerable<string> labels)
    {
        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            foreach (var label in labels)
            {
                var index = line.IndexOf(label, StringComparison.OrdinalIgnoreCase);
                if (index < 0) continue;

                var value = line[(index + label.Length)..].Trim(' ', ':', '：', '.', 'ـ', '-', '_', '|');
                if (value.Length < 2 && lineIndex + 1 < lines.Count)
                    value = lines[lineIndex + 1].Trim(' ', ':', '：', '.', 'ـ', '-', '_', '|');

                value = Regex.Replace(value, @"[.…_]{2,}", " ").Trim();
                if (value.Contains('/'))
                    value = value.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? value;
                if (value.Length >= 2 && value.Length <= 180) return value;
            }
        }
        return null;
    }

    private static decimal? ParseAmount(string text)
    {
        var match = Regex.Match(text, @"(?<!\d)(\d{1,3}(?:[,٬.]\d{3})*(?:[.,]\d{1,2})?|\d+(?:[.,]\d{1,2})?)\s*(?:JOD|JD|دينار)", RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        var numeric = match.Groups[1].Value.Replace("٬", ",");
        var lastComma = numeric.LastIndexOf(',');
        var lastDot = numeric.LastIndexOf('.');
        var decimalSeparator = Math.Max(lastComma, lastDot);
        if (decimalSeparator >= 0 && numeric.Length - decimalSeparator is 2 or 3)
        {
            var whole = Regex.Replace(numeric[..decimalSeparator], "[,\\.]", string.Empty);
            numeric = whole + "." + numeric[(decimalSeparator + 1)..];
        }
        else numeric = Regex.Replace(numeric, "[,\\.]", string.Empty);

        return decimal.TryParse(numeric, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ? amount : null;
    }

    private static DateTime? ParseDateForLabels(IReadOnlyList<string> lines, params string[] labels)
    {
        const string pattern = @"(?<!\d)(\d{1,2}[./-]\d{1,2}[./-]\d{4}|\d{4}[./-]\d{1,2}[./-]\d{1,2}|\d{1,2}\s+[A-Za-z]{3,9}\s+\d{4})(?!\d)";
        string[] formats = [
            "d/M/yyyy", "dd/MM/yyyy", "d-M-yyyy", "dd-MM-yyyy", "d.M.yyyy", "dd.MM.yyyy",
            "yyyy-M-d", "yyyy-MM-dd", "yyyy/M/d", "yyyy/MM/dd", "yyyy.M.d", "yyyy.MM.dd",
            "d MMM yyyy", "dd MMM yyyy", "d MMMM yyyy", "dd MMMM yyyy"
        ];
        foreach (var line in lines)
        {
            if (!labels.Any(label => line.Contains(label, StringComparison.OrdinalIgnoreCase))) continue;
            var match = Regex.Match(line, pattern);
            if (match.Success && DateTime.TryParseExact(match.Value, formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date))
                return date.Date;
        }
        return null;
    }

    private static string NormalizeDigits(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            var digit = CharUnicodeInfo.GetDecimalDigitValue(character);
            builder.Append(digit is >= 0 and <= 9 ? (char)('0' + digit) : character);
        }
        return builder.ToString();
    }

    private static string? CleanValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var cleaned = Regex.Replace(value, @"[\u200e\u200f\u202a-\u202e]", string.Empty);
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim(' ', ':', '：', '.', 'ـ', '-', '_', '|');
        return cleaned.Length is >= 2 and <= 180 ? cleaned : null;
    }
}

public sealed class GuaranteeOcrModule : IFeatureModule
{
    public void Register(IServiceCollection services, IConfiguration configuration) =>
        services.AddScoped<GuaranteeDocumentReader>();
}
