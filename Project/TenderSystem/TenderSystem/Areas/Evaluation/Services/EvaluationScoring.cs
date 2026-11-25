using TenderSystem.Data.Entities.Evaluation;
using TenderSystem.Data.Entities.Tenders;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Evaluation.Services;

public static class EvaluationScoring
{
    public static bool IsOverallFinancialAssessment(IEnumerable<EvaluationCriterion> source)
    {
        var financial = source.Where(c => c.Type == Statuses.CriterionType.Financial).ToList();
        return financial.Count == 1
            && financial[0].Name == "Financial assessment (lowest eligible price suggested)"
            && financial[0].MaxScore == 5;
    }

    public static bool ValidRubric(IEnumerable<EvaluationCriterion> source)
    {
        var criteria = source.ToList();
        return criteria.Any(c => c.Type == Statuses.CriterionType.Financial)
            && criteria.All(c => c.IsLocked && c.MaxScore > 0 && c.WeightPercent > 0
                && (c.Type == Statuses.CriterionType.Technical || c.Type == Statuses.CriterionType.Financial))
            && criteria.Sum(c => c.WeightPercent) == 100;
    }

    // Legacy table name: rows are identified by CriterionId; the criterion owns its Technical/Financial type.
    public static decimal? Points(IEnumerable<EvaluationCriterion> criteria, IEnumerable<TechnicalEvaluation> saved, string member)
    {
        var questions = criteria.ToList();
        // A financial-only rubric has a zero technical subtotal. Rubric validation still requires financial questions.
        if (questions.Count == 0) return 0;
        decimal total = 0;
        foreach (var question in questions)
        {
            var row = saved.SingleOrDefault(e => e.EvaluatorUserId == member && e.CriterionId == question.Id);
            if (row?.Score == null || question.MaxScore <= 0 || row.Score < 0 || row.Score > question.MaxScore
                || row.Score != decimal.Round(row.Score.Value, 2)) return null;
            total += row.Score.Value / question.MaxScore * question.WeightPercent;
        }
        return total;
    }
}
