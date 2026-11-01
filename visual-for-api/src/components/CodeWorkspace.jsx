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
  const editorRef = useRef(null);
  const cursorListener = useRef(null);
  const decorations = useRef(null);
  const [editorReady, setEditorReady] = useState(false);
  const canEdit = room.canEditCode && shared.ready && shared.canWrite;
  const canSubmit = room.canSubmit ?? (!room.canInterview && room.canEditCode);
  useEffect(() => {
    const editor = editorRef.current;
    if (!editorReady || !editor?.getModel()) return;
    const model = editor.getModel();
    decorations.current?.set(
      Object.values(shared.peers).map((peer) => {
        const line = Math.min(peer.lineNumber, model.getLineCount());
        const column = Math.min(peer.column, model.getLineMaxColumn(line));
        return {
          range: {
            startLineNumber: line,
            endLineNumber: line,
            startColumn: column,
            endColumn: column,
          },
          options: {
            className: "peer-cursor",
            afterContentClassName: "peer-cursor-mark",
          },
        };
      }),
    );
  }, [shared.peers, editorReady]);
  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
      abortRef.current?.abort();
      cursorListener.current?.dispose();
      decorations.current?.clear();
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
            {room.canInterview ? "Совместный редактор" : "Ваше решение"}
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
      <div className="editor-actions">
        <span className="muted">
          {shared.canWrite
            ? "Сейчас пишете вы"
            : `Пишет: ${shared.editorOwner || "кандидат"}`}
        </span>
        {room.canEditCode &&
          (shared.canWrite ? (
            <button
              className="quiet"
              disabled={!shared.ready}
              onClick={shared.releaseControl}
            >
              Передать управление
            </button>
          ) : (
            <button
              className="secondary"
              disabled={!shared.ready}
              onClick={shared.takeControl}
            >
              Взять управление
            </button>
          ))}
        <button
          className="quiet"
          disabled={!shared.ready}
          onClick={() => action.run(shared.reload, "Код обновлён")}
        >
          Заменить локальный код сохранённым
        </button>
        <span className="muted">
          {Object.values(shared.peers)
            .map(
              (peer) =>
                `${peer.displayName}: ${peer.lineNumber}:${peer.column}`,
            )
            .join(" · ")}
        </span>
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
        onMount={(editor) => {
          editorRef.current = editor;
          decorations.current = editor.createDecorationsCollection();
          cursorListener.current = editor.onDidChangeCursorPosition(
            ({ position }) => shared.cursor(position),
          );
          setEditorReady(true);
        }}
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
              disabled={!canEdit || action.busy}
              onClick={() => action.run(shared.flush, "Код сохранён")}
            >
              Сохранить код
            </button>
            {canSubmit && (
              <button
                disabled={!shared.ready || action.busy}
                onClick={submitSolution}
              >
                Отправить решение →
              </button>
            )}
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
                {test.durationMs != null ? ` · ${test.durationMs} мс` : ""}
                {test.memoryKb > 0 ? ` · ${test.memoryKb} КиБ` : ""}
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
