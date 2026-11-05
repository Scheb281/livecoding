import { useEffect, useState } from "react";
import axios from "axios";

function ViewQuestion({ user }) {
  const [data, setData] = useState([]);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    if (user == null || user === "") return;

    const controller = new AbortController();

    async function loadQuestions() {
      setData([]);
      setIsLoading(true);
      setError("");

      try {
        const response = await axios.post(
          "https://api/getQuestionByUser",
          { user },
          { signal: controller.signal },
        );

        if (!Array.isArray(response.data)) {
          throw new Error("Сервер вернул задания в неожиданном формате.");
        }

        setData(response.data);
      } catch (error) {
        if (!controller.signal.aborted) {
          setError(error.message || "Не удалось загрузить задания.");
        }
      } finally {
        if (!controller.signal.aborted) setIsLoading(false);
      }
    }

    loadQuestions();
    return () => controller.abort();
  }, [user]);

  if (user == null || user === "") {
    return <p className="question-message">Пока заданий нет</p>;
  }

  if (isLoading) {
    return (
      <p className="question-message" role="status">
        Загрузка заданий…
      </p>
    );
  }

  if (error) {
    return (
      <p className="question-message question-message--error" role="alert">
        {error}
      </p>
    );
  }

  if (data.length === 0) {
    return <p className="question-message">Пока заданий нет</p>;
  }

  return (
    <ol className="question-list">
      {data.map((question, index) => (
        <li
          key={
            typeof question === "object" && question !== null
              ? (question.id ?? index)
              : index
          }
        >
          {typeof question === "string" ? (
            <p>{question}</p>
          ) : (
            <>
              {question?.title && <h3>{question.title}</h3>}
              <p>
                {question?.description ??
                  question?.text ??
                  "Условие пока не указано"}
              </p>
            </>
          )}
        </li>
      ))}
    </ol>
  );
}

export default ViewQuestion;
