import { useState } from "react";
import AddQuestion from "./addquestion";
import CodeBox from "./CodeBox";
import useInterviewRoom from "./useInterviewRoom";
import "./livecoding.css";

function Interviewer({ roomId = "" }) {
  const [localTasks, setLocalTasks] = useState([]);
  const room = useInterviewRoom(roomId);
  const [copyStatus, setCopyStatus] = useState("");
  const tasks = roomId ? (room.snapshot?.tasks ?? []) : localTasks;

  async function addQuestion(task) {
    if (roomId) {
      await room.addTask(task);
    } else {
      setLocalTasks((current) => [
        ...current,
        { ...task, id: crypto.randomUUID() },
      ]);
    }
  }

  async function copyInterviewLink() {
    const link = new URL(window.location.href);
    link.searchParams.delete("view");
    link.searchParams.set("room", roomId);
    try {
      await navigator.clipboard.writeText(link.href);
      setCopyStatus("Ссылка скопирована");
    } catch {
      setCopyStatus(`Скопируйте ссылку: ${link.href}`);
    }
  }

  return (
    <main className="live-coding">
      <header className="live-coding__header">
        <h1>Кабинет тимлида</h1>
        <p role="status">
          {roomId
            ? room.status
            : "Локальный просмотр: задачи пока хранятся только на этой странице."}
        </p>
        {roomId && (
          <button onClick={copyInterviewLink}>
            Скопировать ссылку на интервью
          </button>
        )}
        {copyStatus && <p role="status">{copyStatus}</p>}
      </header>
      <div className="live-coding__layout">
        <aside className="live-coding__questions">
          <h2>Добавить задачу</h2>
          <AddQuestion
            onAddQuestion={addQuestion}
            disabled={
              Boolean(roomId) &&
              (!room.isConnected || !room.snapshot?.canManageTasks)
            }
          />
          <h2 className="task-list-title">Задачи интервью</h2>
          {tasks.length === 0 && (
            <p className="question-message">Задач пока нет.</p>
          )}
          <ol className="question-list">
            {tasks.map((task) => (
              <li key={task.id}>
                <p>{task.description}</p>
                {task.hint && (
                  <details>
                    <summary>Подсказка тимлида</summary>
                    <p>{task.hint}</p>
                  </details>
                )}
              </li>
            ))}
          </ol>
        </aside>
        <section
          className="live-coding__editor"
          aria-label="Код интервьюируемого"
        >
          <CodeBox sharedDocument={room.snapshot} readOnly={Boolean(roomId)} />
        </section>
      </div>
    </main>
  );
}

export default Interviewer;
