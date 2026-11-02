import test from "node:test";
import assert from "node:assert/strict";
import { finalScore } from "../src/api/scoring.js";

test("итог учитывает вес и разные максимальные баллы", () => {
  assert.equal(finalScore([{ id: "a", maxScore: 5, weight: 3 }, { id: "b", maxScore: 10, weight: 1 }], { a: 5, b: 0 }), 75);
  assert.equal(finalScore([{ id: "a", maxScore: 5 }], { a: 4 }), 80);
});
test("незаполненные критерии не считаются нулевой итоговой оценкой", () => {
  assert.equal(finalScore([{ id: "a", maxScore: 5, weight: 1 }], {}), null);
});
