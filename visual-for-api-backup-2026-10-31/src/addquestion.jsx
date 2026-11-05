import { useState } from "react";

function AddQuestion({ onAddQuestion, disabled = false }) {
  const [description, setDescription] = useState("");
  const [hint, setHint] = useState("");
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState("");

  async function handleSubmit(event) {
    event.preventDefault();
    if (!description.trim() || disabled || isSubmitting) return;

    setIsSubmitting(true);
    setError("");
    try {
      await onAddQuestion({
        description: description.trim(),
        hint: hint.trim(),
      });
      setDescription("");
      setHint("");
    } catch {
      setError(
        "Не удалось добавить задачу. Текст сохранён в форме — попробуйте ещё раз.",
      );
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <form className="task-form" onSubmit={handleSubmit}>
      <label htmlFor="task-description">Условие задачи</label>
      <textarea
        id="task-description"
        value={description}
        onChange={(event) => setDescription(event.target.value)}
        placeholder="Что должен сделать интервьюируемый?"
        rows={6}
        required
        maxLength={10000}
      />
      <label htmlFor="task-hint">Подсказка для тимлида</label>
      <textarea
        id="task-hint"
        value={hint}
        onChange={(event) => setHint(event.target.value)}
        placeholder="Необязательная подсказка"
        rows={3}
        maxLength={4000}
      />
      <button
        type="submit"
        disabled={disabled || isSubmitting || !description.trim()}
      >
        {isSubmitting ? "Добавление…" : "Добавить задачу"}
      </button>
      {error && <p role="alert">{error}</p>}
    </form>
  );
}

export default AddQuestion;
