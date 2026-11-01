import { createDemoApi } from "./demo.jsx";

export function createApi(mode) {
  if (mode === "demo") return createDemoApi();
  let csrfToken = ""; // CSRF-токен для cookie-сессии. Получается при первом POST-запросе.
  
  async function request(path, options = {}) {
    const method = options.method ?? "GET";
    if (method !== "GET" && !csrfToken) {
      const response = await fetch("/api/auth/csrf", {
        credentials: "include",
      });
      if (!response.ok)
        throw new Error(
          "Не удалось получить защитный токен. Проверьте ASP.NET API.",
        );
      csrfToken = (await response.json()).token;
    }
    const response = await fetch(`/api${path}`, {
      method,
      credentials: "include",
      signal: options.signal,
      headers: {
        Accept: "application/json",
        ...(options.body !== undefined
          ? { "Content-Type": "application/json" }
          : {}),
        ...(method !== "GET" ? { "X-CSRF-Token": csrfToken } : {}),
      },
      body:
        options.body === undefined ? undefined : JSON.stringify(options.body),
    });
    const data =
      response.status === 204 ? null : await response.json().catch(() => null);
    if (!response.ok || (response.status !== 204 && data === null)) {
      const error = new Error(
        data?.detail ??
          data?.message ??
          (response.status === 401
            ? "Необходимо войти в приложение."
            : "API недоступно или вернуло неверный ответ."),
      );
      error.status = response.status;
      throw error;
    }
    // После смены пользователя нужен новый CSRF-токен для его cookie-сессии.
    if (
      [
        "/auth/login",
        "/auth/register/candidate",
        "/auth/setup-password",
        "/auth/logout",
        "/invitations/redeem",
      ].includes(path)
    )
      csrfToken = "";
    return data;
  }

  return { mode, request };
}

export const roles = {
  candidate: "Кандидат",
  interviewer: "Интервьюер",
  teamlead: "Ведущий / тимлид",
  admin: "Администратор",
};
