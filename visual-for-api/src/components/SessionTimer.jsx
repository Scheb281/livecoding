import { useEffect, useState } from "react";

export default function SessionTimer({ expiresAt, status, onExpired }) {
  const [now, setNow] = useState(Date.now);
  const remaining = expiresAt
    ? Math.max(0, Math.ceil((new Date(expiresAt).getTime() - now) / 1000))
    : null;
  useEffect(() => {
    if (!expiresAt || status !== "active") return;
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(timer);
  }, [expiresAt, status]);
  useEffect(() => {
    if (remaining === 0 && status === "active") onExpired();
  }, [remaining, status, onExpired]);
  return (
    <p
      className={`session-timer ${remaining === 0 ? "notice--error" : ""}`}
      role="timer"
    >
      {status === "completed"
        ? "Интервью завершено"
        : remaining == null
          ? "Таймер запустится с началом интервью"
          : `Осталось ${Math.floor(remaining / 60)}:${String(remaining % 60).padStart(2, "0")}`}
    </p>
  );
}
