using System.Text.RegularExpressions;

namespace TenderSystem.Areas.Sourcing.Models;

/// <summary>
/// Finds sentences that mention common tender requirements. This is keyword matching only;
/// it does not interpret intent or verify supplier evidence.
/// </summary>
public static class ComplianceTermsExtractor
{
    private static readonly (string Name, string Pattern)[] RequirementPatterns =
    new[]
    {
        ("Technical specifications", @"\btechnical\s+specifications?\b|\btechnical\s+compliance\b"),
        ("Packaging requirements", @"\bpackaging\s+(?:requirements?|specifications?|standards?)\b"),
        ("Delivery period", @"\bdelivery\s+(?:period|time|timeline|schedule)\b"),
        ("Payment terms", @"\bpayment\s+terms?\b|\bpayment\s+conditions?\b"),
        ("Offer validity", @"\b(?:offer|bid|proposal)\s+validity\b|\bvalidity\s+period\b"),
        ("Required documents", @"\b(?:required|supporting|mandatory)\s+documents?\b|\brequired\s+documentation\b"),
        ("Guarantees", @"\b(?:bank|judicial|performance|bid|tender)\s+(?:guarantee|bond|security)\b|\bguarantees?\b|\bbid\s+bond\b")
    };

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
        if (string.IsNullOrWhiteSpace(terms)) return new List<CompliancePageViewModel.ExtractedRequirement>();

        var extracted = new List<CompliancePageViewModel.ExtractedRequirement>();
        var sentences = SentenceSeparator.Split(terms);
        foreach (var part in sentences)
        {
            var sentence = part.Trim();
            if (sentence.Length == 0) continue;

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

                var alreadyAdded = false;
                foreach (var item in extracted)
                {
                    if (item.Name == name
                        && string.Equals(item.SourceText, sentence, StringComparison.OrdinalIgnoreCase))
                    {
                        alreadyAdded = true;
                        break;
                    }
                }

                if (alreadyAdded)
                    continue;

                extracted.Add(new CompliancePageViewModel.ExtractedRequirement(
                    name, sentence, classification,
                    new List<CompliancePageViewModel.DocumentRow>()));
            }
        }

        return extracted;
    }

    public static bool CouldRelateDocument(string requirementName, string docType, string fileName)
    {
        var documentText = Regex.Replace($"{docType} {fileName}", "([a-z])([A-Z])", "$1 $2");
        string? pattern = null;
        foreach (var requirementPattern in RequirementPatterns)
        {
            if (requirementPattern.Name != requirementName) continue;
            pattern = requirementPattern.Pattern;
            break;
        }

        if (string.IsNullOrEmpty(pattern)) return false;
        return Regex.IsMatch(documentText, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
