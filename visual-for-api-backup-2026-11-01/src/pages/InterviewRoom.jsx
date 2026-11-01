import { formatDate } from "../api/format.jsx";
import { useState } from "react";
import { useAction, useResource } from "../hooks/useResource.jsx";
import {
  ActionState,
  Empty,
  Notice,
  PageTitle,
  ResourceState,
} from "../components/Common.jsx";
import CodeWorkspace from "../components/CodeWorkspace.jsx";
import {
  ChatPanel,
  EvaluationPanel,
  HintsPanel,
} from "../components/RoomPanels.jsx";

export default function InterviewRoom({ api, roomId, onBack }) {
  const resource = useResource(api, `/rooms/${encodeURIComponent(roomId)}`);
  const action = useAction();
  const [selectedTaskId, setSelectedTaskId] = useState("");
  const [invitation, setInvitation] = useState(null);
  const [copyStatus, setCopyStatus] = useState("");
  const room = resource.data;
  const task =
    room?.tasks.find((item) => item.id === selectedTaskId) ?? room?.tasks[0];

  async function invite() {
    const result = await action.run(
      () => api.request(`/rooms/${room.id}/invitations`, { method: "POST" }),
      "Одноразовое приглашение создано",
    );
    if (!result) return;
    const url = new URL(window.location.origin + window.location.pathname);
    url.searchParams.set("invite", result.data.token);
    setInvitation({ url: url.href, expiresAt: result.data.expiresAt });
  }
  async function copy() {
    try {
      await navigator.clipboard.writeText(invitation.url);
      setCopyStatus("Ссылка скопирована");
    } catch {
      setCopyStatus("Выделите ссылку в поле и скопируйте вручную.");
    }
  }
  return (
    <>
      <button className="quiet back-button" onClick={onBack}>
        ← Все комнаты
      </button>
      <ResourceState resource={resource} />
      {room && (
        <>
          <PageTitle
            eyebrow={`Кандидат: ${room.candidateName}`}
            title={room.title}
          >
            Статус:{" "}
            {room.status === "active"
              ? "интервью идёт"
              : room.status === "draft"
                ? "подготовка"
                : "завершено"}{" "}
            · Записано решений: {room.submissions.length}
          </PageTitle>
          {room.canInterview && (
            <section className="panel">
              <div className="toolbar">
                {room.status === "draft" && (
                  <button
                    disabled={action.busy}
                    onClick={async () => {
                      if (
                        await action.run(
                          () =>
                            api.request(`/rooms/${room.id}/start`, {
                              method: "POST",
                            }),
                          "Комната запущена",
                        )
                      )
                        resource.refresh();
                    }}
                  >
                    Запустить комнату
                  </button>
                )}
                <button
                  className="secondary"
                  disabled={action.busy || room.status !== "active"}
                  onClick={invite}
                >
                  Создать одноразовое приглашение
                </button>
              </div>
              {invitation && (
                <div className="stack invitation">
                  <label>
                    Ссылка кандидату
                    <input
                      readOnly
                      value={invitation.url}
                      onFocus={(event) => event.target.select()}
                    />
                  </label>
                  <div className="toolbar">
                    <button className="secondary" onClick={copy}>
                      Скопировать
                    </button>
                    <span className="muted">
                      До {formatDate(invitation.expiresAt)}
                    </span>
                  </div>
                  <Notice>{copyStatus}</Notice>
                  {api.mode === "demo" && (
                    <Notice>
                      В демо приглашение действует только внутри этой вкладки.
                      Для входа с другого компьютера нужен API.
                    </Notice>
                  )}
                </div>
              )}
              <ActionState action={action} />
            </section>
          )}
          {task ? (
            <>
              <div className="room-grid">
                <aside className="stack">
                  <section className="panel">
                    <label>
                      Задача
                      <select
                        value={task.id}
                        onChange={(event) =>
                          setSelectedTaskId(event.target.value)
                        }
                      >
                        {room.tasks.map((item, index) => (
                          <option key={item.id} value={item.id}>
                            {index + 1}. {item.title}
                          </option>
                        ))}
                      </select>
                    </label>
                    <h2>{task.title}</h2>
                    <p className="prose">{task.description}</p>
                    <div className="tags">
                      <span>{task.timeLimitMs} мс</span>
                      <span>{task.memoryLimitMb} МБ</span>
                    </div>
                    <h3>Примеры</h3>
                    {task.tests
                      .filter((test) => !test.hidden)
                      .map((test, index) => (
                        <div className="example" key={test.id ?? index}>
                          <small>Вход</small>
                          <pre>{test.input || "(пустой ввод)"}</pre>
                          <small>Вывод</small>
                          <pre>{test.expected || "(пустой вывод)"}</pre>
                        </div>
                      ))}
                  </section>
                  <HintsPanel
                    key={task.id}
                    api={api}
                    room={room}
                    taskId={task.id}
                    refreshRoom={resource.refresh}
                  />
                </aside>
                <div className="stack">
                  <CodeWorkspace
                    key={`${room.id}-${task.id}`}
                    api={api}
                    room={room}
                    task={task}
                    refreshRoom={resource.refresh}
                  />
                  <ChatPanel
                    api={api}
                    room={room}
                    refreshRoom={resource.refresh}
                  />
                </div>
              </div>
              {room.canInterview && room.rubric && (
                <EvaluationPanel
                  key={room.rubric.id}
                  api={api}
                  room={room}
                  refreshRoom={resource.refresh}
                />
              )}
            </>
          ) : (
            <Empty>В комнате пока нет задач.</Empty>
          )}
        </>
      )}
    </>
  );
}
