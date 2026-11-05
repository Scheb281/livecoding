import { useEffect, useState } from "react";
import Editor from "@monaco-editor/react";
import { useResource } from "../hooks/useResource.jsx";
import { languages } from "../api/languages.jsx";
import { Empty, Notice, ResourceState } from "./Common.jsx";

const labels = {
  RoomCreated: "Комната создана",
  RoomOpened: "Ожидание кандидата",
  CandidateJoined: "Кандидат вошёл",
  SessionStarted: "Интервью началось",
  SessionFinished: "Интервью завершено",
  SessionExpired: "Время истекло",
  CodeChanged: "Изменение кода",
  ChatMessage: "Сообщение в чате",
  HintAdded: "Подсказка",
  HintUsed: "Подсказка использована",
  RunQueued: "Запуск тестов",
  RunCompleted: "Результат тестов",
  SolutionSubmitted: "Решение отправлено",
  EvaluationSaved: "Оценка сохранена",
  EditorControlTaken: "Управление получено",
  EditorControlReleased: "Управление передано",
};

export default function SessionReplay({ api, roomId }) {
  const [sequence, setSequence] = useState(0);
  const [after, setAfter] = useState(0);
  const [playing, setPlaying] = useState(false);
  const [taskId, setTaskId] = useState("");
  const events = useResource(
    api,
    api.mode === "demo"
      ? null
      : `/rooms/${roomId}/events?afterSequence=${after}&limit=100`,
  );
  const state = useResource(
    api,
    api.mode === "demo"
      ? null
      : `/rooms/${roomId}/recording?atSequence=${sequence}`,
  );
  const total = events.data?.totalSequence ?? state.data?.totalSequence ?? 0;
  const document =
    state.data?.documents.find((d) => d.taskId === taskId) ??
    state.data?.documents[0];
  useEffect(() => {
    if (!playing) return;
    // Один шаг в секунду: запись воспроизводится без большого массива кода в памяти браузера.
    const timer = setInterval(
      () => setSequence((current) => Math.min(total, current + 1)),
      1000,
    );
    return () => clearInterval(timer);
  }, [playing, total]);
  if (api.mode === "demo")
    return (
      <Notice>
        Воспроизведение использует сохранённый журнал ASP.NET. Для него включите
        API-режим.
      </Notice>
    );
  return (
    <section className="panel replay-panel">
      <div className="section-heading">
        <h2>Запись сессии</h2>
        <button className="quiet" onClick={events.refresh}>
          Обновить журнал
        </button>
      </div>
      <ResourceState resource={events} />
      <ResourceState resource={state} />
      <div className="toolbar">
        <button
          disabled={!total || sequence === total}
          onClick={() => setPlaying(!playing)}
        >
          {playing ? "Пауза" : "▶ Воспроизвести"}
        </button>
        <button
          className="secondary"
          onClick={() => {
            setPlaying(false);
            setSequence(0);
          }}
        >
          В начало
        </button>
        <label className="grow">
          Событие {sequence} / {total}
          <input
            aria-label="Позиция записи"
            type="range"
            min={0}
            max={total}
            value={sequence}
            onChange={(event) => {
              setPlaying(false);
              setSequence(Number(event.target.value));
            }}
          />
        </label>
      </div>
      {document && (
        <>
          <label>
            Задача в записи
            <select
              value={document.taskId}
              onChange={(event) => setTaskId(event.target.value)}
            >
              {state.data.documents.map((d) => (
                <option key={d.taskId} value={d.taskId}>
                  {d.title}
                </option>
              ))}
            </select>
          </label>
          <Editor
            height="340px"
            theme="vs-dark"
            language={languages[document.language]?.id ?? "plaintext"}
            value={document.code}
            options={{
              readOnly: true,
              minimap: { enabled: false },
              automaticLayout: true,
            }}
          />
        </>
      )}
      <ol className="session-events">
        {events.data?.items.map((event) => (
          <li key={event.id}>
            <button
              className="quiet"
              onClick={() => {
                setPlaying(false);
                setSequence(event.sequence);
              }}
            >
              #{event.sequence} · {labels[event.type] ?? event.type} ·{" "}
              {event.author}
            </button>
            <time>{new Date(event.createdAt).toLocaleTimeString("ru-RU")}</time>
            {event.payload.text && <p>{event.payload.text}</p>}
            {event.payload.summary && <p>{event.payload.summary}</p>}
          </li>
        ))}
      </ol>
      {events.data?.items.length === 0 && (
        <Empty>В этой части журнала событий пока нет.</Empty>
      )}
      <div className="toolbar">
        <button
          className="secondary"
          disabled={after === 0}
          onClick={() => setAfter(0)}
        >
          Первые события
        </button>
        <button
          className="secondary"
          disabled={
            !events.data?.items.length || events.data.nextAfter >= total
          }
          onClick={() => setAfter(events.data.nextAfter)}
        >
          Следующие события
        </button>
      </div>
    </section>
  );
}
