using Interview.Api.Contracts;

namespace Interview.Api.Services;

public static class ScoringService
{
    // Нормализуем разные шкалы до 0–100 и учитываем вес каждого критерия.
    public static decimal? Calculate(RubricDto rubric, IReadOnlyDictionary<string, int> scores)
    {
        if (rubric.Criteria.Length == 0 || rubric.Criteria.Any(c => c.Weight <= 0 || c.MaxScore <= 0 || !scores.ContainsKey(c.Id))) return null;
        var totalWeight = rubric.Criteria.Sum(c => c.Weight);
        return decimal.Round(rubric.Criteria.Sum(c => (decimal)scores[c.Id] / c.MaxScore * c.Weight) / totalWeight * 100m, 2);
    }
}
