import { useState } from "react";
import { useAction } from "../hooks/useResource.jsx";
import { ActionState, Notice } from "./Common.jsx";

export default function AuthScreen({
  api,
  onUser,
  onDemo,
  initialToken = "",
  initialSetupToken = "",
  error,
  onCancel,
}) {
  const [tab, setTab] = useState(
    initialSetupToken ? "setup" : initialToken ? "invite" : "login",
  );
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [name, setName] = useState("");
  const [invitation, setInvitation] = useState(initialToken);
  const action = useAction();

  async function submit(event) {
    event.preventDefault();
    const result = await action.run(async () => {
      if (tab === "setup") {
        if (password !== confirmPassword)
          throw new Error("Пароли не совпадают.");
        return api.request("/auth/setup-password", {
          method: "POST",
          body: { token: initialSetupToken, password },
        });
      }
      // Публичная регистрация создаёт только кандидата: роль назначает сервер.
      if (tab === "register") {
        if (!name.trim()) throw new Error("Укажите ваше имя.");
        if (password !== confirmPassword)
          throw new Error("Пароли не совпадают.");
        return api.request("/auth/register/candidate", {
          method: "POST",
          body: { name: name.trim(), email: email.trim(), password },
        });
      }
      if (tab === "login")
        return api.request("/auth/login", {
          method: "POST",
          body: { email, password },
        });
      let token = invitation.trim();
      if (token.includes("://"))
        token = new URL(token).searchParams.get("invite") ?? "";
      if (!token) throw new Error("В ссылке отсутствует приглашение.");
      return api.request("/invitations/redeem", {
        method: "POST",
        body: { token, name: name.trim() },
      });
    }, "Вход выполнен");
    if (result) {
      setPassword("");
      setConfirmPassword("");
      onUser(result.data.user, result.data.roomId);
    }
  }
  return (
    <main className="auth-layout">
      <section className="auth-intro">
        <div className="brand">
          <span className="brand-icon">&lt;/&gt;</span> interview
          <span className="brand-dot">.</span>
        </div>
        <span className="eyebrow">Платформа технических интервью</span>
        <h1>
          От задачи
          <br />
          до решения.
        </h1>
        <p>
          Код в реальном времени, понятные критерии и сохранённые результаты
          собеседований.
        </p>
        <div className="tags">
          <span>Live coding</span>
          <span>Банк задач</span>
          <span>Рубрики оценок</span>
        </div>
      </section>
      <section className="panel auth-form">
        <div className="tabs">
          <button
            className={tab === "login" ? "selected" : "quiet"}
            disabled={action.busy}
            onClick={() => setTab("login")}
          >
            Войти
          </button>
          <button
            className={tab === "invite" ? "selected" : "quiet"}
            disabled={action.busy}
            onClick={() => setTab("invite")}
          >
            По приглашению
          </button>
        </div>
        <button
          className={tab === "register" ? "selected" : "quiet"}
          disabled={action.busy}
          onClick={() => setTab("register")}
        >
          Регистрация кандидата
        </button>
        <h2>
          {tab === "setup"
            ? "Установить пароль сотрудника"
            : tab === "register"
              ? "Создать аккаунт кандидата"
              : tab === "login"
                ? "Вход в рабочее пространство"
                : "Вход по приглашению"}
        </h2>
        <Notice error>{error}</Notice>
        <form className="stack" onSubmit={submit}>
          {tab !== "invite" ? (
            <>
              {tab === "register" && (
                <label>
                  Как к вам обращаться
                  <input
                    required
                    maxLength={100}
                    autoComplete="name"
                    value={name}
                    onChange={(event) => setName(event.target.value)}
                  />
                </label>
              )}
              {tab !== "setup" && (
                <label>
                  Email
                  <input
                    type="email"
                    required
                    autoComplete="username"
                    value={email}
                    onChange={(event) => setEmail(event.target.value)}
                  />
                </label>
              )}
              <label>
                Пароль
                <input
                  type="password"
                  required
                  autoComplete={
                    tab !== "login" ? "new-password" : "current-password"
                  }
                  minLength={tab !== "login" ? 8 : undefined}
                  maxLength={tab !== "login" ? 128 : undefined}
                  value={password}
                  onChange={(event) => setPassword(event.target.value)}
                />
              </label>
              {(tab === "register" || tab === "setup") && (
                <>
                  <label>
                    Повторите пароль
                    <input
                      type="password"
                      required
                      autoComplete="new-password"
                      minLength={8}
                      maxLength={128}
                      value={confirmPassword}
                      onChange={(event) =>
                        setConfirmPassword(event.target.value)
                      }
                    />
                  </label>
                  <p className="muted">
                    {tab === "setup"
                      ? "Ссылка одноразовая. Роль учётной записи уже назначена администратором."
                      : "Пароль — от 8 символов. Для подключения к интервью понадобится приглашение интервьюера."}
                  </p>
                </>
              )}
            </>
          ) : (
            <>
              <label>
                Как к вам обращаться
                <input
                  required
                  maxLength={100}
                  autoComplete="name"
                  value={name}
                  onChange={(event) => setName(event.target.value)}
                />
              </label>
              <label>
                Ссылка или код приглашения
                <input
                  required
                  value={invitation}
                  onChange={(event) => setInvitation(event.target.value)}
                />
              </label>
              <p className="muted">
                Приглашение одноразовое. Его срок и доступ проверяет сервер.
              </p>
            </>
          )}
          <ActionState action={action} />
          <button disabled={action.busy}>
            {action.busy
              ? "Подключение…"
              : tab === "register"
                ? "Зарегистрироваться"
                : "Продолжить →"}
          </button>
        </form>
        <div className="auth-footer">
          <button className="secondary" onClick={onDemo}>
            Посмотреть демоверсию
          </button>
          {onCancel && (
            <button className="quiet" onClick={onCancel}>
              Вернуться
            </button>
          )}
          <p className="muted">
            Демо не подключается к БД и не запускает пользовательский код.
          </p>
        </div>
      </section>
    </main>
  );
}
