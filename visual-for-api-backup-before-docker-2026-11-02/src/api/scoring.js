export function finalScore(criteria, scores) {
  if (
    !criteria?.length ||
    criteria.some((c) => scores[c.id] === undefined || scores[c.id] === "")
  )
    return null;
  const weight = criteria.reduce((sum, c) => sum + Number(c.weight ?? 1), 0);
  if (weight <= 0 || criteria.some((c) => c.maxScore <= 0)) return null;
  return (
    Math.round(
      (criteria.reduce(
        (sum, c) =>
          sum + (Number(scores[c.id]) / c.maxScore) * Number(c.weight ?? 1),
        0,
      ) /
        weight) *
        10000,
    ) / 100
  );
}
