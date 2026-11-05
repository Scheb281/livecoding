import { useState } from "react";
import { useResource } from "../hooks/useResource.jsx";
import TaskForm from "../components/TaskForm.jsx";
import { Empty, PageTitle, ResourceState } from "../components/Common.jsx";

const difficultyLabels = { easy: "Лёгкая", medium: "Средняя", hard: "Сложная" };

export default function TaskBank({ api }) {
  const resource = useResource(api, "/tasks");
  const [search, setSearch] = useState("");
  const [difficulty, setDifficulty] = useState("");
  const [editing, setEditing] = useState(null);
  const tasks = (resource.data ?? []).filter(
    (task) =>
      (!difficulty || task.difficulty === difficulty) &&
      `${task.title} ${task.topic} ${task.company} ${task.tags.join(" ")}`
        .toLowerCase()
        .includes(search.toLowerCase()),
  );
  return (
    <>
      <PageTitle eyebrow="Задачи из базы данных" title="Банк задач">
        Условия, стартовый код, тесты и ограничения в одном месте.
      </PageTitle>
      <ResourceState resource={resource} />
      {editing ? (
        <section className="panel">
          <TaskForm
            key={editing.id ?? "new"}
            api={api}
            task={editing.id ? editing : null}
            onSaved={() => {
              setEditing(null);
              resource.refresh();
            }}
            onCancel={() => setEditing(null)}
          />
        </section>
      ) : (
        <>
          <div className="toolbar">
            <label className="grow">
              Поиск
              <input
                placeholder="Название, тема, тег или компания"
                value={search}
                onChange={(event) => setSearch(event.target.value)}
              />
            </label>
            <label>
              Сложность
              <select
                value={difficulty}
                onChange={(event) => setDifficulty(event.target.value)}
              >
                <option value="">Все уровни</option>
                <option value="easy">Лёгкая</option>
                <option value="medium">Средняя</option>
                <option value="hard">Сложная</option>
              </select>
            </label>
            <button onClick={() => setEditing({})}>+ Новая задача</button>
          </div>
          <div className="card-grid">
            {tasks.map((task) => (
              <article className="panel task-card" key={task.id}>
                <div className="section-heading">
                  <span className={`badge badge--${task.difficulty}`}>
                    {difficultyLabels[task.difficulty]}
                  </span>
                  <span className="muted">{task.language}</span>
                </div>
                <h2>{task.title}</h2>
                <p className="clamp">{task.description}</p>
                <div className="tags">
                  {[task.topic, task.company, ...task.tags]
                    .filter(Boolean)
                    .map((tag, index) => (
                      <span key={`${tag}-${index}`}>{tag}</span>
                    ))}
                </div>
                <p className="muted">
                  {task.tests.length} тестов · {task.timeLimitMs} мс ·{" "}
                  {task.memoryLimitMb} МБ
                </p>
                <button className="secondary" onClick={() => setEditing(task)}>
                  Открыть и редактировать
                </button>
              </article>
            ))}
          </div>
          {!resource.loading && !resource.error && tasks.length === 0 && (
            <Empty>Задач по выбранным условиям нет.</Empty>
          )}
        </>
      )}
    </>
  );
}
