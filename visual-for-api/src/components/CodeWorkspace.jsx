import { useEffect, useRef, useState } from "react";
import Editor from "@monaco-editor/react";
import { languages } from "../api/languages.jsx";
import { useSharedDocument } from "../hooks/useSharedDocument.jsx";
import { useAction } from "../hooks/useResource.jsx";
import { ActionState, Notice } from "./Common.jsx";

export default function CodeWorkspace({ api, room, task, refreshRoom }) {
  const shared = useSharedDocument(api, room.id, task.id, refreshRoom);
  const action = useAction();
  const [run, setRun] = useState(null);
  const [running, setRunning] = useState(false);
  const [runError, setRunError] = useState("");
  const mounted = useRef(true);
  const abortRef = useRef(null);
  const canEdit = room.canEditCode && shared.ready;
  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
      abortRef.current?.abort();
    };
  }, []);

  // Запускает серверную задачу, затем опрашивает статус. В браузере код не выполняется.
  async function runTests() {
    setRunning(true);
    setRunError("");
    setRun(null);
    const controller = new AbortController();
    abortRef.current = controller;
    try {
      const documentVersion = room.canEditCode
        ? await shared.flush()
        : shared.document.version;
      let result = await api.request(`/rooms/${room.id}/runs`, {
        method: "POST",
        body: { taskId: task.id, documentVersion },
        signal: controller.signal,
      });
      for (
        let attempt = 0;
        ["queued", "running"].includes(result.status) && attempt < 40;
        attempt++
      ) {
        if (!mounted.current) return;
        setRun(result);
        await new Promise((resolve) => setTimeout(resolve, 750));
        if (controller.signal.aborted) return;
        result = await api.request(`/rooms/${room.id}/runs/${result.id}`, {
          signal: controller.signal,
        });
      }
      if (mounted.current) setRun(result);
    } catch (error) {
      if (mounted.current && !controller.signal.aborted)
        setRunError(error.message);
    } finally {
      if (mounted.current) setRunning(false);
    }
  }

  async function submitSolution() {
    await action.run(async () => {
      const documentVersion = await shared.flush();
      const result = await api.request(`/rooms/${room.id}/submissions`, {
        method: "POST",
        body: { taskId: task.id, documentVersion },
      });
      refreshRoom();
      return result;
    }, "Решение отправлено. Сервер сохранил снимок кода.");
  }

  // Проверка статуса не создаёт новый запуск и не расходует ещё одну квоту.
  async function refreshRun() {
    setRunning(true);
    setRunError("");
    try {
      const result = await api.request(`/rooms/${room.id}/runs/${run.id}`);
      if (mounted.current) setRun(result);
    } catch (error) {
      if (mounted.current) setRunError(error.message);
    } finally {
      if (mounted.current) setRunning(false);
    }
  }

  return (
    <section className="panel editor-panel">
      <div className="editor-toolbar">
        <div>
          <span className="eyebrow">
            {room.canEditCode ? "Ваше решение" : "Код кандидата · просмотр"}
          </span>
          <p className="muted" role="status">
            {shared.status}
          </p>
        </div>
        <label className="language-select">
          <span className="sr-only">Язык программирования</span>
          <select
            disabled={!canEdit}
            value={shared.document?.language ?? task.language}
            onChange={(event) =>
              shared.change({ language: event.target.value })
            }
          >
            {Object.keys(languages).map((language) => (
              <option key={language}>{language}</option>
            ))}
          </select>
        </label>
      </div>
      {canEdit && (
        <div className="editor-actions">
          <button
            className="quiet"
            onClick={() => {
              const language = shared.document.language;
              shared.change({
                code:
                  language === task.language
                    ? task.starterCode
                    : languages[language].code,
              });
            }}
          >
            Заменить код стартовым шаблоном
          </button>
        </div>
      )}
      <Editor
        height="430px"
        theme="vs-dark"
        language={
          languages[shared.document?.language ?? task.language]?.id ??
          "plaintext"
        }
        path={`${room.id}/${task.id}`}
        value={shared.document?.code ?? ""}
        loading="Загрузка редактора…"
        onChange={(value, event) => {
          if (canEdit && !event.isFlush) shared.change({ code: value ?? "" });
        }}
        options={{
          readOnly: !canEdit,
          minimap: { enabled: false },
          fontSize: 14,
          automaticLayout: true,
          scrollBeyondLastLine: false,
          padding: { top: 18 },
        }}
      />
      <div className="editor-actions">
        <button
          className="secondary"
          disabled={
            !shared.ready ||
            running ||
            !shared.document ||
            (!room.canEditCode && !room.canInterview) ||
            room.status !== "active"
          }
          onClick={runTests}
        >
          {running ? "Тестирование…" : "▷ Запустить тесты"}
        </button>
        {room.canEditCode && (
          <>
            <button
              className="quiet"
              disabled={!shared.ready || action.busy}
              onClick={() => action.run(shared.flush, "Код сохранён")}
            >
              Сохранить код
            </button>
            <button
              disabled={!shared.ready || action.busy}
              onClick={submitSolution}
            >
              Отправить решение →
            </button>
          </>
        )}
      </div>
      <ActionState action={action} />
      <Notice error>{runError}</Notice>
      <div className="run-output">
        <span className="eyebrow">Результаты тестов</span>
        {run ? (
          <>
            <p>
              Статус:{" "}
              {{
                queued: "в очереди",
                running: "выполняется",
                completed: "завершено",
                failed: "ошибка",
              }[run.status] ?? run.status}
            </p>
            {run.summary && <p>{run.summary}</p>}
            {run.output && <pre>{run.output}</pre>}
            {run.tests?.map((test, index) => (
              <p key={test.id ?? index}>
                Тест {index + 1}: {test.passed ? "✓ пройден" : "✕ не пройден"}
                {test.message ? ` · ${test.message}` : ""}
              </p>
            ))}
            {["queued", "running"].includes(run.status) && !running && (
              <Notice>
                Проверка ещё идёт.{" "}
                <button className="secondary" onClick={refreshRun}>
                  Обновить статус
                </button>
              </Notice>
            )}
          </>
        ) : (
          <p className="muted">
            Запустите тесты, чтобы получить результат от сервера.
          </p>
        )}
      </div>
    </section>
  );
}
