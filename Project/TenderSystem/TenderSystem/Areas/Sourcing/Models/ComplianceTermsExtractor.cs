using System.Text.RegularExpressions;

namespace TenderSystem.Areas.Sourcing.Models;

/// <summary>
/// Finds sentences that mention common tender requirements. This is keyword matching only;
/// it does not interpret intent or verify supplier evidence.
/// </summary>
public static class ComplianceTermsExtractor
{
    private static readonly (string Name, string Pattern)[] RequirementPatterns =
    [
        ("Technical specifications", @"\btechnical\s+specifications?\b|\btechnical\s+compliance\b"),
        ("Packaging requirements", @"\bpackaging\s+(?:requirements?|specifications?|standards?)\b"),
        ("Delivery period", @"\bdelivery\s+(?:period|time|timeline|schedule)\b"),
        ("Payment terms", @"\bpayment\s+terms?\b|\bpayment\s+conditions?\b"),
        ("Offer validity", @"\b(?:offer|bid|proposal)\s+validity\b|\bvalidity\s+period\b"),
        ("Required documents", @"\b(?:required|supporting|mandatory)\s+documents?\b|\brequired\s+documentation\b")
    ];

    private static readonly Regex SentenceSeparator = new(@"(?<=[.!?])\s+|[;\r\n]+", RegexOptions.Compiled);
    private static readonly Regex MandatoryWording = new(
        @"\b(?:must|shall|required|mandatory|compulsory)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NonMandatoryOrAmbiguousWording = new(
        @"\b(?:not\s+(?:required|mandatory|compulsory)|optional|may\s+be\s+provided|if\s+applicable|if\s+available|where\s+applicable|as\s+applicable|unless|except)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SupplierEligibilityCredentials = new(
        @"\b(?:tax\s+(?:clearance|certificate|certification|compliance)|(?:company|business|commercial)\s+registration|(?:trade|business|commercial)\s+licen[cs]e|CR\s+(?:certificate|document))\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static List<CompliancePageViewModel.ExtractedRequirement> Extract(string? terms)
    {
        if (string.IsNullOrWhiteSpace(terms)) return [];

        var extracted = new List<CompliancePageViewModel.ExtractedRequirement>();
        foreach (var sentence in SentenceSeparator.Split(terms)
                     .Select(value => value.Trim())
                     .Where(value => value.Length > 0))
        {
            foreach (var (name, pattern) in RequirementPatterns)
            {
                if (name == "Required documents" && SupplierEligibilityCredentials.IsMatch(sentence))
                    continue;

                if (!Regex.IsMatch(sentence, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    continue;

                var hasExplicitMandatoryWording = MandatoryWording.IsMatch(sentence);
                var hasAmbiguousWording = NonMandatoryOrAmbiguousWording.IsMatch(sentence);
                var classification = hasExplicitMandatoryWording && !hasAmbiguousWording
                    ? "Explicit mandatory wording"
                    : "Needs Manual Review";

                if (extracted.Any(item => item.Name == name
                    && string.Equals(item.SourceText, sentence, StringComparison.OrdinalIgnoreCase)))
                    continue;

                extracted.Add(new CompliancePageViewModel.ExtractedRequirement(name, sentence, classification, []));
            }
        }

        return extracted;
    }

    public static bool CouldRelateDocument(string requirementName, string docType, string fileName)
    {
        var documentText = $"{docType} {fileName}";
        var pattern = RequirementPatterns.FirstOrDefault(item => item.Name == requirementName).Pattern;
        return !string.IsNullOrEmpty(pattern)
            && Regex.IsMatch(documentText, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
