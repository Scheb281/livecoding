import { useState } from "react";
import { useAction, useResource } from "../hooks/useResource.jsx";
import {
  ActionState,
  Empty,
  PageTitle,
  ResourceState,
} from "../components/Common.jsx";

export default function Rooms({ api, user, onOpen }) {
  const rooms = useResource(api, "/rooms");
  const tasks = useResource(api, user.role === "interviewer" ? "/tasks" : null);
  const rubrics = useResource(
    api,
    user.role === "interviewer" ? "/rubrics" : null,
  );
  const action = useAction();
  const [form, setForm] = useState({
    title: "",
    candidateName: "",
    taskIds: [],
    rubricId: "",
  });
  const [showForm, setShowForm] = useState(false);
  async function createRoom(event) {
    event.preventDefault();
    const result = await action.run(async () => {
      if (!form.taskIds.length)
        throw new Error("Выберите хотя бы одну задачу.");
      return api.request("/rooms", { method: "POST", body: form });
    });
    if (result) onOpen(result.data.id);
  }
  return (
    <>
      <PageTitle
        eyebrow={
          user.role === "candidate"
            ? "Ваше собеседование"
            : "Рабочее пространство интервьюера"
        }
        title="Комнаты интервью"
      >
        {user.role === "candidate"
          ? "Задания, редактор, тесты и чат с интервьюером."
          : "Подготовьте задачи, запустите комнату и пригласите кандидата."}
      </PageTitle>
      {user.role === "interviewer" && (
        <div className="toolbar">
          <button onClick={() => setShowForm(!showForm)}>
            {showForm ? "Закрыть форму" : "+ Создать комнату"}
          </button>
        </div>
      )}
      {showForm && (
        <section className="panel">
          <form className="stack" onSubmit={createRoom}>
            <h2>Новое интервью</h2>
            <ResourceState resource={tasks} />
            <ResourceState resource={rubrics} />
            <div className="form-grid">
              <label>
                Название
                <input
                  required
                  value={form.title}
                  onChange={(event) =>
                    setForm({ ...form, title: event.target.value })
                  }
                />
              </label>
              <label>
                Имя кандидата
                <input
                  required
                  value={form.candidateName}
                  onChange={(event) =>
                    setForm({ ...form, candidateName: event.target.value })
                  }
                />
              </label>
            </div>
            <label>
              Рубрика
              <select
                required
                value={form.rubricId}
                onChange={(event) =>
                  setForm({ ...form, rubricId: event.target.value })
                }
              >
                <option value="">Выберите шаблон</option>
                {rubrics.data?.map((rubric) => (
                  <option key={rubric.id} value={rubric.id}>
                    {rubric.name}
                  </option>
                ))}
              </select>
            </label>
            <fieldset>
              <legend>Задачи из банка</legend>
              {tasks.data?.map((task) => (
                <label className="check" key={task.id}>
                  <input
                    type="checkbox"
                    checked={form.taskIds.includes(task.id)}
                    onChange={(event) =>
                      setForm({
                        ...form,
                        taskIds: event.target.checked
                          ? [...form.taskIds, task.id]
                          : form.taskIds.filter((id) => id !== task.id),
                      })
                    }
                  />
                  {task.title} · {task.language}
                </label>
              ))}
            </fieldset>
            <ActionState action={action} />
            <button
              disabled={
                action.busy || !tasks.data?.length || !rubrics.data?.length
              }
            >
              Создать комнату
            </button>
          </form>
        </section>
      )}
      <ResourceState resource={rooms} />
      <div className="card-grid">
        {rooms.data?.map((room) => (
          <article className="panel" key={room.id}>
            <div className="section-heading">
              <span className="badge">
                {room.status === "active"
                  ? "В процессе"
                  : room.status === "draft"
                    ? "Подготовка"
                    : "Завершено"}
              </span>
              <span className="muted">{room.tasks.length} задач</span>
            </div>
            <h2>{room.title}</h2>
            <p className="muted">{room.candidateName}</p>
            <button className="secondary" onClick={() => onOpen(room.id)}>
              Открыть комнату →
            </button>
          </article>
        ))}
      </div>
      {rooms.data?.length === 0 && <Empty>Доступных комнат пока нет.</Empty>}
    </>
  );
}
