import { useEffect, useRef, useState } from "react";
import { HubConnectionBuilder } from "@microsoft/signalr";

export default function useInterviewRoom(roomId) {
  const connectionRef = useRef(null);
  const [snapshot, setSnapshot] = useState(null);
  const [status, setStatus] = useState("Нет подключения");
  const [isConnected, setIsConnected] = useState(false);

  useEffect(() => {
    if (!roomId) return;
    let disposed = false;
    let retryTimer;
    const connection = new HubConnectionBuilder()
      .withUrl("/hubs/interviews")
      .withAutomaticReconnect()
      .build();
    connectionRef.current = connection;

    function applyCode(document) {
      if (!disposed)
        setSnapshot((current) => current && { ...current, ...document });
    }

    connection.on("CodeUpdated", applyCode);
    connection.on("TasksUpdated", (tasks) => {
      if (!disposed) setSnapshot((current) => current && { ...current, tasks });
    });

    async function joinRoom() {
      const current = await connection.invoke("JoinInterview", roomId);
      if (disposed) return;
      setSnapshot(current);
      setIsConnected(true);
      setStatus("Подключено к интервью");
    }

    async function start() {
      try {
        setStatus("Подключение…");
        await connection.start();
        if (disposed) return;
        await joinRoom();
      } catch {
        if (disposed) return;
        setIsConnected(false);
        setStatus(
          "Нет связи с комнатой.",
        );
        await connection.stop();
        clearTimeout(retryTimer);
        if (!disposed) retryTimer = setTimeout(start, 5000);
      }
    }

    connection.onreconnecting(() => {
      if (disposed) return;
      setIsConnected(false);
      setStatus("Связь потеряна. Переподключение…");
    });
    connection.onreconnected(async () => {
      try {
        await joinRoom();
      } catch {
        await connection.stop();
      }
    });
    connection.onclose(() => {
      if (disposed) return;
      setIsConnected(false);
      setStatus("Соединение закрыто. Повторное подключение…");
      clearTimeout(retryTimer);
      retryTimer = setTimeout(start, 5000);
    });

    void start();
    return () => {
      disposed = true;
      clearTimeout(retryTimer);
      connectionRef.current = null;
      void connection.stop();
    };
  }, [roomId]);

  async function updateCode(document) {
    if (!isConnected || !snapshot?.canEditCode) return;
    setSnapshot((current) => ({ ...current, ...document }));
    try {
      await connectionRef.current.invoke("UpdateCode", roomId, document);
    } catch {
      setStatus(
        "Изменения не отправлены. Скопируйте свой код перед обновлением страницы.",
      );
    }
  }

  async function addTask(task) {
    if (!isConnected || !snapshot?.canManageTasks)
      throw new Error("Нет доступа");
    await connectionRef.current.invoke("AddTask", roomId, task);
  }

  return { snapshot, status, isConnected, updateCode, addTask };
}
