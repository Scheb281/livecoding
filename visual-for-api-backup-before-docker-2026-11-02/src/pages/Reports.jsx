import { useResource } from "../hooks/useResource.jsx";
import { Empty, PageTitle, ResourceState } from "../components/Common.jsx";

function score(value) {
  return value == null ? "Нет оценок" : Number(value).toFixed(1);
}

export default function Reports({ api }) {
  const resource = useResource(api, "/reports");
  const candidates = [...(resource.data?.candidates ?? [])].sort(
    (left, right) => (right.finalScore ?? -1) - (left.finalScore ?? -1),
  );
  const tasks = resource.data?.tasks ?? [];
  return (
    <>
      <PageTitle eyebrow="Данные собеседований" title="Отчёты">
        Результаты кандидатов и качество задач. Показатели рассчитывает сервер
        по сохранённым данным.
      </PageTitle>
      <ResourceState resource={resource} />
      <section className="panel">
        <h2>Кандидаты</h2>
        <div className="table-scroll">
          <table>
            <thead>
              <tr>
                <th>Кандидат / интервью</th>
                <th>Решений отправлено</th>
                <th>Подсказок использовано</th>
                <th>Средний балл</th>
                <th>Итог с весами, /100</th>
              </tr>
            </thead>
            <tbody>
              {candidates.map((candidate) => (
                <tr key={candidate.id}>
                  <td>
                    <strong>{candidate.name}</strong>
                    <small>{candidate.interview}</small>
                  </td>
                  <td>{candidate.submitted}</td>
                  <td>{candidate.hintsUsed}</td>
                  <td>{score(candidate.averageScore)}</td>
                  <td>
                    {score(candidate.finalScore)}
                    {candidate.finalScore != null && (
                      <meter min={0} max={100} value={candidate.finalScore} />
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {candidates.length === 0 && <Empty />}
      </section>
      <section className="panel">
        <h2>Качество задач</h2>
        <div className="table-scroll">
          <table>
            <thead>
              <tr>
                <th>Задача</th>
                <th>Кандидатов</th>
                <th>Решили</th>
                <th>Доля решений</th>
                <th>Средний балл</th>
              </tr>
            </thead>
            <tbody>
              {tasks.map((task) => (
                <tr key={task.id}>
                  <td>{task.title}</td>
                  <td>{task.attempts}</td>
                  <td>{task.solved}</td>
                  <td>
                    {task.attempts
                      ? `${Math.round((task.solved / task.attempts) * 100)}%`
                      : "—"}
                  </td>
                  <td>{score(task.averageScore)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {tasks.length === 0 && <Empty />}
      </section>
    </>
  );
}
