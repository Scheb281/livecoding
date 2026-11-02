import { useState } from "react";
import { useAction, useResource } from "../hooks/useResource.jsx";
import {
  ActionState,
  Empty,
  PageTitle,
  ResourceState,
} from "../components/Common.jsx";

export default function Rubrics({ api }) {
  const resource = useResource(api, "/rubrics");
  const action = useAction();
  const [name, setName] = useState("");
  const [criteria, setCriteria] = useState([
    { id: "algorithm", label: "Алгоритм", maxScore: 5, weight: 2 },
    { id: "code", label: "Код", maxScore: 5, weight: 1 },
    { id: "communication", label: "Коммуникация", maxScore: 5, weight: 1 },
  ]);
  async function save(event) {
    event.preventDefault();
    if (
      await action.run(() =>
        api.request("/rubrics", { method: "POST", body: { name, criteria } }),
      )
    ) {
      setName("");
      resource.refresh();
    }
  }
  return (
    <>
      <PageTitle eyebrow="Единые критерии" title="Шаблоны рубрик">
        Интервьюер выберет шаблон при создании комнаты.
      </PageTitle>
      <div className="two-columns">
        <section className="panel stack">
          <h2>Новый шаблон</h2>
          <form className="stack" onSubmit={save}>
            <label>
              Название
              <input
                required
                maxLength={200}
                value={name}
                onChange={(event) => setName(event.target.value)}
              />
            </label>
            {criteria.map((criterion, index) => (
              <fieldset key={criterion.id}>
                <label>
                  Критерий
                  <input
                    required
                    maxLength={100}
                    value={criterion.label}
                    onChange={(event) =>
                      setCriteria((current) =>
                        current.map((item) =>
                          item.id === criterion.id
                            ? { ...item, label: event.target.value }
                            : item,
                        ),
                      )
                    }
                  />
                </label>
                <label>
                  Максимальный балл: {criterion.label}
                  <input
                    type="number"
                    required
                    min={1}
                    max={100}
                    value={criterion.maxScore}
                    onChange={(event) =>
                      setCriteria((current) =>
                        current.map((item, currentIndex) =>
                          currentIndex === index
                            ? { ...item, maxScore: Number(event.target.value) }
                            : item,
                        ),
                      )
                    }
                  />
                </label>
                <label>
                  Вес
                  <input
                    type="number"
                    required
                    min={0.1}
                    max={100}
                    step={0.1}
                    value={criterion.weight ?? 1}
                    onChange={(event) =>
                      setCriteria((current) =>
                        current.map((item) =>
                          item.id === criterion.id
                            ? { ...item, weight: Number(event.target.value) }
                            : item,
                        ),
                      )
                    }
                  />
                </label>
                <button
                  className="quiet"
                  type="button"
                  disabled={criteria.length === 1}
                  onClick={() =>
                    setCriteria((current) =>
                      current.filter((item) => item.id !== criterion.id),
                    )
                  }
                >
                  Удалить критерий
                </button>
              </fieldset>
            ))}
            <button
              className="secondary"
              type="button"
              disabled={criteria.length >= 10}
              onClick={() =>
                setCriteria((current) => [
                  ...current,
                  {
                    id: crypto.randomUUID(),
                    label: "Новый критерий",
                    maxScore: 5,
                    weight: 1,
                  },
                ])
              }
            >
              Добавить критерий
            </button>
            <p className="muted">
              Итог: сумма (балл / максимум × вес), делённая на сумму весов, ×
              100.
            </p>
            <button disabled={action.busy}>Сохранить шаблон</button>
            <ActionState action={action} />
          </form>
        </section>
        <section className="stack">
          <ResourceState resource={resource} />
          {resource.data?.map((rubric) => (
            <article className="panel" key={rubric.id}>
              <h2>{rubric.name}</h2>
              <ul className="plain-list">
                {rubric.criteria.map((criterion) => (
                  <li key={criterion.id}>
                    {criterion.label}
                    <strong>
                      0–{criterion.maxScore} · вес {criterion.weight ?? 1}
                    </strong>
                  </li>
                ))}
              </ul>
            </article>
          ))}
          {resource.data?.length === 0 && <Empty />}
        </section>
      </div>
    </>
  );
}
