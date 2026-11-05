import { useState } from "react";
import Editor from "@monaco-editor/react";
import { languages } from "../api/languages.jsx";
import { useAction } from "../hooks/useResource.jsx";
import { ActionState, Notice } from "./Common.jsx";

const blankTask = {
  title: "",
  description: "",
  topic: "",
  difficulty: "easy",
  company: "",
  tags: [],
  language: "Python",
  starterCode: languages.Python.code,
  timeLimitMs: 2000,
  memoryLimitMb: 128,
  tests: [{ input: "", expected: "", hidden: false }],
};

export default function TaskForm({ api, task, onSaved, onCancel }) {
  const [form, setForm] = useState(() => structuredClone(task ?? blankTask));
  const [tagsText, setTagsText] = useState(task?.tags.join(", ") ?? "");
  const [starterCodeError, setStarterCodeError] = useState("");
  const action = useAction();
  function change(name, value) {
    setForm((current) => ({ ...current, [name]: value }));
    if (name === "starterCode") setStarterCodeError("");
  }
  function changeTest(index, name, value) {
    change(
      "tests",
      form.tests.map((test, current) =>
        current === index ? { ...test, [name]: value } : test,
      ),
    );
  }
  async function save(event) {
    event.preventDefault();
    // Monaco не поддерживает HTML required, поэтому проверяем код перед запросом.
    if (!form.starterCode.trim() || form.starterCode.length > 20000) {
      setStarterCodeError("Добавьте стартовый код длиной до 20 000 символов.");
      return;
    }
    const tags = [
      ...new Set(
        tagsText
          .split(",")
          .map((tag) => tag.trim())
          .filter(Boolean),
      ),
    ];
    const result = await action.run(() =>
      api.request(task ? `/tasks/${task.id}` : "/tasks", {
        method: task ? "PUT" : "POST",
        body: { ...form, tags },
      }),
    );
    if (result) onSaved();
  }
  return (
    <form className="stack" onSubmit={save}>
      <h2>{task ? "Редактировать задачу" : "Новая задача"}</h2>
      <label>
        Название
        <input
          required
          maxLength={200}
          value={form.title}
          onChange={(event) => change("title", event.target.value)}
        />
      </label>
      <label>
        Условие
        <textarea
          required
          rows={5}
          maxLength={10000}
          value={form.description}
          onChange={(event) => change("description", event.target.value)}
        />
      </label>
      <div className="form-grid">
        <label>
          Тема
          <input
            required
            value={form.topic}
            onChange={(event) => change("topic", event.target.value)}
          />
        </label>
        <label>
          Сложность
          <select
            value={form.difficulty}
            onChange={(event) => change("difficulty", event.target.value)}
          >
            <option value="easy">Лёгкая</option>
            <option value="medium">Средняя</option>
            <option value="hard">Сложная</option>
          </select>
        </label>
        <label>
          Компания
          <input
            value={form.company}
            onChange={(event) => change("company", event.target.value)}
          />
        </label>
        <label>
          Теги через запятую
          <input
            value={tagsText}
            onChange={(event) => setTagsText(event.target.value)}
          />
        </label>
        <label>
          Время, мс
          <input
            type="number"
            min={100}
            max={10000}
            required
            value={form.timeLimitMs}
            onChange={(event) =>
              change("timeLimitMs", Number(event.target.value))
            }
          />
        </label>
        <label>
          Память, МБ
          <input
            type="number"
            min={32}
            max={1024}
            required
            value={form.memoryLimitMb}
            onChange={(event) =>
              change("memoryLimitMb", Number(event.target.value))
            }
          />
        </label>
      </div>
      <label>
        Язык
        <select
          value={form.language}
          onChange={(event) => {
            // Шаблон меняется вместе с языком только по явному выбору автора задачи.
            const language = event.target.value;
            setStarterCodeError("");
            setForm((current) => ({
              ...current,
              language,
              starterCode: languages[language].code,
            }));
          }}
        >
          {Object.keys(languages).map((language) => (
            <option key={language}>{language}</option>
          ))}
        </select>
      </label>
      <div className="stack">
        <span>Стартовый код</span>
        <div className="starter-code-editor">
          <Editor
            height="340px"
            theme="vs-dark"
            language={languages[form.language]?.id ?? "plaintext"}
            value={form.starterCode}
            loading="Загрузка редактора…"
            onChange={(value) => change("starterCode", value ?? "")}
            options={{
              ariaLabel: "Стартовый код",
              automaticLayout: true,
              minimap: { enabled: false },
              fontSize: 14,
              tabSize: 4,
              insertSpaces: true,
              detectIndentation: false,
              scrollBeyondLastLine: false,
              padding: { top: 12, bottom: 12 },
            }}
          />
        </div>
        <Notice error>{starterCodeError}</Notice>
      </div>
      <div className="section-heading">
        <h3>Тесты</h3>
        <button
          type="button"
          className="secondary"
          onClick={() =>
            change("tests", [
              ...form.tests,
              { input: "", expected: "", hidden: false },
            ])
          }
        >
          + Добавить тест
        </button>
      </div>
      {form.tests.map((test, index) => (
        <fieldset key={test.id ?? index}>
          <legend>Тест {index + 1}</legend>
          <div className="form-grid">
            <label>
              Вход
              <textarea
                rows={2}
                value={test.input}
                onChange={(event) =>
                  changeTest(index, "input", event.target.value)
                }
              />
            </label>
            <label>
              Ожидаемый вывод
              <textarea
                rows={2}
                value={test.expected}
                onChange={(event) =>
                  changeTest(index, "expected", event.target.value)
                }
              />
            </label>
          </div>
          <div className="toolbar">
            <label className="check">
              <input
                type="checkbox"
                checked={test.hidden}
                onChange={(event) =>
                  changeTest(index, "hidden", event.target.checked)
                }
              />
              Скрытый тест
            </label>
            <button
              type="button"
              className="quiet"
              disabled={form.tests.length === 1}
              onClick={() =>
                change(
                  "tests",
                  form.tests.filter((_, current) => current !== index),
                )
              }
            >
              Удалить тест
            </button>
          </div>
        </fieldset>
      ))}
      <ActionState action={action} />
      <div className="toolbar">
        <button disabled={action.busy}>
          {action.busy ? "Сохранение…" : "Сохранить задачу в банк"}
        </button>
        <button type="button" className="secondary" onClick={onCancel}>
          Отмена
        </button>
      </div>
    </form>
  );
}
