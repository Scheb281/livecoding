import { useEffect, useMemo, useState } from "react";
import { createApi, roles } from "./api/client.jsx";
import AuthScreen from "./components/AuthScreen.jsx";
import { Notice } from "./components/Common.jsx";
import Rooms from "./pages/Rooms.jsx";
import InterviewRoom from "./pages/InterviewRoom.jsx";
import TaskBank from "./pages/TaskBank.jsx";
import Rubrics from "./pages/Rubrics.jsx";
import Reports from "./pages/Reports.jsx";
import Admin from "./pages/Admin.jsx";
import "./App.css";

const navigation = {
  candidate: [["rooms", "Моё интервью"]],
  interviewer: [
    ["rooms", "Комнаты интервью"],
    ["tasks", "Задачи"],
  ],
  teamlead: [
    ["tasks", "Банк задач"],
    ["rubrics", "Шаблоны рубрик"],
    ["reports", "Отчёты"],
  ],
  admin: [
    ["users", "Пользователи и квоты"],
    ["audit", "Аудит"],
  ],
};

function Application({ api, onModeChange }) {
  const [user, setUser] = useState(null);
  const [checking, setChecking] = useState(true);
  const [error, setError] = useState("");
  const [page, setPage] = useState("");
  const [roomId, setRoomId] = useState(
    () => new URLSearchParams(window.location.search).get("room") ?? "",
  );
  const [inviteToken] = useState(
    () => new URLSearchParams(window.location.search).get("invite") ?? "",
  );
  const [showInvitation, setShowInvitation] = useState(Boolean(inviteToken));
  const [setupToken] = useState(
    () => new URLSearchParams(window.location.search).get("setup") ?? "",
  );
  const [showSetup, setShowSetup] = useState(Boolean(setupToken));

  useEffect(() => {
    let active = true;
    if (inviteToken || setupToken)
      window.history.replaceState(null, "", window.location.pathname);
    api
      .request("/auth/me")
      .then((result) => {
        if (active) setUser(result.user);
      })
      .catch((error) => {
        if (active && error.status !== 401) setError(error.message);
      })
      .finally(() => {
        if (active) setChecking(false);
      });
    return () => {
      active = false;
    };
  }, [api, inviteToken, setupToken]);

  function signedIn(account, invitedRoomId) {
    setUser(account);
    setError("");
    setShowInvitation(false);
    setShowSetup(false);
    setRoomId(invitedRoomId ?? account.roomId ?? "");
    setPage((invitedRoomId ?? account.roomId) ? "room" : "");
  }
  async function switchRole(role) {
    try {
      const result = await api.request("/demo/role", {
        method: "POST",
        body: { role },
      });
      signedIn(result.user);
    } catch (error) {
      setError(error.message);
    }
  }
  function openRoom(id) {
    setRoomId(id);
    setPage("room");
    const url = new URL(window.location.href);
    url.searchParams.set("room", id);
    window.history.replaceState(null, "", url);
  }
  function navigate(next) {
    setPage(next);
    window.history.replaceState(null, "", window.location.pathname);
  }
  async function logout() {
    try {
      await api.request("/auth/logout", { method: "POST" });
      setUser(null);
      navigate("");
    } catch (error) {
      setError(error.message);
    }
  }
  if (checking)
    return <main className="loading-screen">Проверяем сессию…</main>;
  if (!user || showInvitation || showSetup)
    return (
      <AuthScreen
        api={api}
        error={error}
        initialToken={showInvitation ? inviteToken : ""}
        initialSetupToken={showSetup ? setupToken : ""}
        onUser={signedIn}
        onDemo={() => onModeChange("demo")}
        onCancel={
          user
            ? () => {
                setShowInvitation(false);
                setShowSetup(false);
              }
            : undefined
        }
      />
    );
  if (!navigation[user.role])
    return <Notice error>Сервер вернул неизвестную роль пользователя.</Notice>;
  const items = navigation[user.role];
  const currentPage =
    page ||
    (roomId && ["candidate", "interviewer"].includes(user.role)
      ? "room"
      : items[0][0]);

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <div className="brand">
          <span className="brand-icon">&lt;/&gt;</span> interview
          <span className="brand-dot">.</span>
        </div>
        <span className="sidebar-caption">РАБОЧЕЕ ПРОСТРАНСТВО</span>
        <nav aria-label="Основное меню">
          {items.map(([id, label], index) => (
            <button
              key={id}
              className={
                currentPage === id || (id === "rooms" && currentPage === "room")
                  ? "nav-active"
                  : ""
              }
              onClick={() => navigate(id)}
            >
              <span className="nav-number">0{index + 1}</span>
              {label}
            </button>
          ))}
        </nav>
        <div className="sidebar-bottom">
          <span className="avatar">{user.name.slice(0, 1)}</span>
          <div>
            <strong>{user.name}</strong>
            <small>{roles[user.role]}</small>
          </div>
          <button className="quiet" onClick={logout}>
            Выйти
          </button>
        </div>
      </aside>
      <div className="app-main">
        <header className="topbar">
          <span>{roles[user.role]}</span>
          <div className="toolbar">
            <span
              className={`connection-label ${api.mode === "demo" ? "demo-label" : ""}`}
            >
              {api.mode === "demo" ? "● Демонстрация" : "● ASP.NET API"}
            </span>
            <button className="quiet" onClick={() => setShowInvitation(true)}>
              Ввести приглашение
            </button>
          </div>
        </header>
        {api.mode === "demo" && (
          <div className="demo-banner">
            <div>
              <strong>Демонстрационный режим</strong>
              <p>
                Данные только в памяти вкладки. База и выполнение кода не
                подключены.
              </p>
            </div>
            <label>
              Посмотреть роль
              <select
                value={user.role}
                onChange={(event) => switchRole(event.target.value)}
              >
                {Object.entries(roles).map(([id, label]) => (
                  <option key={id} value={id}>
                    {label}
                  </option>
                ))}
              </select>
            </label>
            <button className="secondary" onClick={() => onModeChange("api")}>
              Перейти к API
            </button>
          </div>
        )}
        <main className="page-content" key={`${user.id}-${user.role}`}>
          <Notice error>{error}</Notice>
          {currentPage === "rooms" && (
            <Rooms api={api} user={user} onOpen={openRoom} />
          )}
          {currentPage === "room" &&
            ["candidate", "interviewer"].includes(user.role) && (
              <InterviewRoom
                key={roomId}
                api={api}
                roomId={roomId}
                onBack={() => navigate("rooms")}
              />
            )}
          {currentPage === "tasks" && <TaskBank api={api} />}
          {currentPage === "rubrics" && <Rubrics api={api} />}
          {currentPage === "reports" && <Reports api={api} />}
          {["users", "audit"].includes(currentPage) && (
            <Admin key={currentPage} api={api} section={currentPage} />
          )}
        </main>
        <footer className="app-footer">
          interview. <span>Задачи → интервью → обратная связь</span>
        </footer>
      </div>
    </div>
  );
}

export default function App() {
  const [session, setSession] = useState({ mode: "api", revision: 0 });
  const api = useMemo(() => createApi(session.mode), [session]);
  function changeMode(mode) {
    setSession((current) => ({ mode, revision: current.revision + 1 }));
  }
  return (
    <Application
      key={`${session.mode}-${session.revision}`}
      api={api}
      onModeChange={changeMode}
    />
  );
}
