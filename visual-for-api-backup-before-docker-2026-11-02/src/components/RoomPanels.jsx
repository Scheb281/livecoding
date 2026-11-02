import { formatDate } from "../api/format.jsx";
import { useState } from "react";
import { finalScore } from "../api/scoring.js";
import { useAction } from "../hooks/useResource.jsx";
import { ActionState, Empty } from "./Common.jsx";

export function ChatPanel({ api, room, refreshRoom }) {
  const [text, setText] = useState("");
  const action = useAction();
  async function send(event) {
    event.preventDefault();
    if (
      await action.run(
        () =>
          api.request(`/rooms/${room.id}/messages`, {
            method: "POST",
            body: { text },
          }),
        "",
      )
    ) {
      setText("");
      refreshRoom();
    }
  }
  return (
    <section className="panel">
      <h2>Чат интервью</h2>
      <ol className="chat-messages" aria-label="Сообщения чата">
        {room.messages.map((message) => (
          <li key={message.id}>
            <div className="section-heading">
              <strong>{message.author}</strong>
              <time>{formatDate(message.createdAt)}</time>
            </div>
            <p>{message.text}</p>
          </li>
        ))}
      </ol>
      {room.messages.length === 0 && (
        <Empty>Здесь можно обсудить условие и задать вопрос.</Empty>
      )}
      <form className="inline-form" onSubmit={send}>
        <label className="grow">
          <span className="sr-only">Сообщение</span>
          <input
            required
            maxLength={2000}
            placeholder="Написать сообщение…"
            value={text}
            onChange={(event) => setText(event.target.value)}
          />
        </label>
        <button disabled={action.busy || room.status !== "active"}>
          Отправить
        </button>
      </form>
      <ActionState action={action} />
    </section>
  );
}

export function HintsPanel({ api, room, taskId, refreshRoom }) {
  const [text, setText] = useState("");
  const action = useAction();
  const hints = room.hints.filter((hint) => hint.taskId === taskId);
  async function add(event) {
    event.preventDefault();
    if (
      await action.run(
        () =>
          api.request(`/rooms/${room.id}/hints`, {
            method: "POST",
            body: { taskId, text },
          }),
        "Подсказка передана кандидату",
      )
    ) {
      setText("");
      refreshRoom();
    }
  }
  async function markUsed(hintId) {
    if (
      await action.run(
        () =>
          api.request(`/rooms/${room.id}/hint-usage`, {
            method: "POST",
            body: { hintId },
          }),
        "Использование подсказки записано",
      )
    )
      refreshRoom();
  }
  return (
    <section className="panel">
      <div className="section-heading">
        <h2>Подсказки</h2>
        <span className="badge">
          Использовано: {hints.filter((hint) => hint.usedAt).length}
        </span>
      </div>
      {hints.map((hint) => (
        <article className="hint" key={hint.id}>
          <p>{hint.text}</p>
          {hint.usedAt ? (
            <span className="muted">
              ✓ Использована · {formatDate(hint.usedAt)}
            </span>
          ) : (
            <button
              className="quiet"
              disabled={action.busy}
              onClick={() => markUsed(hint.id)}
            >
              Отметить «Использована подсказка»
            </button>
          )}
        </article>
      ))}
      {hints.length === 0 && (
        <Empty>Для этой задачи подсказки не выдавались.</Empty>
      )}
      {room.canInterview && (
        <form className="stack" onSubmit={add}>
          <label>
            Подсказка кандидату
            <textarea
              rows={3}
              required
              maxLength={4000}
              value={text}
              onChange={(event) => setText(event.target.value)}
            />
          </label>
          <button
            className="secondary"
            disabled={action.busy || room.status !== "active"}
          >
            Передать подсказку
          </button>
        </form>
      )}
      <ActionState action={action} />
    </section>
  );
}

export function EvaluationPanel({ api, room, refreshRoom }) {
  const previous = room.evaluations[0];
  const [scores, setScores] = useState(previous?.scores ?? {});
  const [comment, setComment] = useState(previous?.comment ?? "");
  const action = useAction();
  async function save(event) {
    event.preventDefault();
    if (
      await action.run(() =>
        api.request(`/rooms/${room.id}/evaluations`, {
          method: "POST",
          body: { rubricId: room.rubric.id, scores, comment },
        }),
      )
    )
      refreshRoom();
  }
  return (
    <section className="panel">
      <h2>Оценка по рубрике</h2>
      <p className="muted">{room.rubric.name}</p>
      <form className="stack" onSubmit={save}>
        {room.rubric.criteria.map((criterion) => (
          <label key={criterion.id}>
            {criterion.label} · от 0 до {criterion.maxScore} · вес{" "}
            {criterion.weight ?? 1}
            <input
              required
              type="number"
              min={0}
              max={criterion.maxScore}
              value={scores[criterion.id] ?? ""}
              onChange={(event) =>
                setScores({
                  ...scores,
                  [criterion.id]:
                    event.target.value === "" ? "" : Number(event.target.value),
                })
              }
            />
          </label>
        ))}
        <label>
          Обоснование оценки
          <textarea
            rows={4}
            maxLength={5000}
            value={comment}
            onChange={(event) => setComment(event.target.value)}
          />
        </label>
        <p>
          Итог с весами: {finalScore(room.rubric.criteria, scores) ?? "—"} / 100
        </p>
        <button disabled={action.busy}>Сохранить оценку</button>
        <ActionState action={action} />
      </form>
    </section>
  );
}
