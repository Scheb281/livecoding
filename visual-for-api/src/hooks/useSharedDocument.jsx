import { useEffect, useRef, useState } from "react";
import { HubConnectionBuilder } from "@microsoft/signalr";

// Один кандидат пишет, остальные смотрят. Последовательная отправка защищает порядок версий.
export function useSharedDocument(api, roomId, taskId, refreshRoom) {
  const [document, setDocument] = useState(null);
  const [status, setStatus] = useState("Загрузка кода…");
  const [ready, setReady] = useState(false);
  const [canWrite, setCanWrite] = useState(false);
  const [editorOwner, setEditorOwner] = useState("");
  const [peers, setPeers] = useState({});
  const controllerRef = useRef(null);

  useEffect(() => {
    let disposed = false;
    let timer;
    let retryTimer;
    let dirty = false;
    let draft;
    let serverVersion;
    let sending = null;
    let connected = false;
    let connection;
    let accessRevoked = false;
    let ownsEditor = api.mode === "demo";
    let cursorTimer;
    let latestCursor;

    function controlChanged(control) {
      ownsEditor = control.connectionId === connection?.connectionId;
      setCanWrite(ownsEditor);
      setEditorOwner(control.name ?? "Редактор свободен");
      if (!ownsEditor) clearTimeout(timer);
      if (!ownsEditor && dirty)
        report(
          "Управление передано. Есть несохранённый текст: скопируйте его или загрузите сохранённую версию.",
        );
    }

    function report(text) {
      if (!disposed) setStatus(text);
    }
    function receive(next) {
      if (disposed || next.taskId !== taskId || dirty || sending) return;
      // События с предыдущей версией не должны затирать более новый документ.
      if (serverVersion !== undefined && next.version < serverVersion) return;
      draft = next;
      serverVersion = next.version;
      setDocument(next);
    }

    async function load() {
      const next = await api.request(`/rooms/${roomId}/documents/${taskId}`);
      receive(next);
    }

    async function flush() {
      clearTimeout(timer);
      if (sending) {
        await sending;
        if (dirty) return flush();
        return serverVersion;
      }
      if (!dirty) return serverVersion;
      if (!connected || !ownsEditor)
        throw new Error("Нет связи. Изменения пока не сохранены.");
      const update = {
        taskId,
        code: draft.code,
        language: draft.language,
        baseVersion: serverVersion,
        mutationId: crypto.randomUUID(),
      };
      dirty = false;
      report("Сохранение кода…");
      sending =
        api.mode === "demo"
          ? api.updateDocument(roomId, update)
          : connection.invoke("UpdateCode", roomId, update);
      try {
        const saved = await sending;
        serverVersion = saved.version;
        draft = { ...draft, version: saved.version };
        if (!disposed) setDocument(draft);
        report(dirty ? "Есть новые изменения…" : "Код сохранён");
      } catch (error) {
        dirty = true;
        report(`Не сохранено: ${error.message} Скопируйте код перед выходом.`);
        throw error;
      } finally {
        sending = null;
      }
      if (dirty) return flush();
      return serverVersion;
    }

    controllerRef.current = {
      change(next) {
        draft = { ...draft, ...next };
        dirty = true;
        setDocument(draft);
        report("Есть несохранённые изменения…");
        clearTimeout(timer);
        timer = setTimeout(() => {
          void flush().catch(() => {});
        }, 300);
      },
      flush,
      async takeControl() {
        try {
          if (api.mode === "demo") {
            ownsEditor = true;
            setCanWrite(true);
            return;
          }
          setReady(false);
          controlChanged(await connection.invoke("TakeEditorControl", roomId));
          await load();
        } catch (error) {
          report(error.message);
        } finally {
          if (!disposed) setReady(connected);
        }
      },
      async releaseControl() {
        try {
          await flush();
          if (api.mode === "demo") {
            ownsEditor = false;
            setCanWrite(false);
            return;
          }
          await connection.invoke("ReleaseEditorControl", roomId);
        } catch (error) {
          report(error.message);
        }
      },
      async reload() {
        if (sending) await sending.catch(() => {});
        clearTimeout(timer);
        dirty = false;
        await load();
        report("Загружена сохранённая версия");
      },
      cursor(position) {
        if (!connected || api.mode === "demo") return;
        latestCursor = position;
        if (!cursorTimer)
          cursorTimer = setTimeout(() => {
            cursorTimer = undefined;
            void connection
              .invoke("UpdateCursor", roomId, {
                taskId,
                lineNumber: latestCursor.lineNumber,
                column: latestCursor.column,
              })
              .catch(() => {});
          }, 200);
      },
    };

    function beforeUnload(event) {
      if (!dirty && !sending) return;
      event.preventDefault();
      event.returnValue = "";
    }
    window.addEventListener("beforeunload", beforeUnload);

    async function join() {
      controlChanged(await connection.invoke("JoinInterview", roomId));
      // Сначала подписка, потом снимок: поздний участник получает код из БД.
      await load();
      connected = true;
      if (!disposed) setReady(true);
      report(
        dirty
          ? "Есть несохранённые изменения. Нажмите «Сохранить код»."
          : "Онлайн · изменения видны участникам комнаты",
      );
      refreshRoom();
    }

    async function start() {
      try {
        await connection.start();
        if (!disposed) await join();
      } catch (error) {
        if (disposed) return;
        report(`Нет подключения к комнате: ${error.message}`);
        await connection.stop();
        clearTimeout(retryTimer);
        retryTimer = setTimeout(start, 5000);
      }
    }

    let unsubscribe;
    if (api.mode === "demo") {
      connected = true;
      unsubscribe = api.subscribe(roomId, () => {
        refreshRoom();
        void load().catch((error) => report(error.message));
      });
      void load()
        .then(() => {
          if (!disposed) { setReady(true); setCanWrite(true); }
          report("Демо · данные в памяти этой вкладки");
        })
        .catch((error) => report(error.message));
    } else {
      connection = new HubConnectionBuilder()
        .withUrl("/hubs/interviews")
        .withAutomaticReconnect()
        .build();
      connection.on("CodeUpdated", receive);
      connection.on("EditorControlChanged", controlChanged);
      connection.on("CursorUpdated", (cursor) => {
        if (cursor.taskId === taskId && !disposed)
          setPeers((current) => ({
            ...current,
            [cursor.connectionId]: cursor,
          }));
      });
      connection.on("PeerLeft", ({ connectionId }) =>
        setPeers((current) => {
          const next = { ...current };
          delete next[connectionId];
          return next;
        }),
      );
      connection.on("AccessRevoked", () => {
        accessRevoked = true;
        connected = false;
        setReady(false);
        report("Доступ к комнате отозван. Войдите в приложение заново.");
        void connection.stop();
      });
      connection.on("RoomChanged", refreshRoom);
      connection.onreconnecting(() => {
        connected = false;
        if (!disposed) setReady(false);
        report("Переподключение. Несохранённый текст остаётся в редакторе.");
      });
      connection.onreconnected(() => {
        void join().catch((error) => {
          report(error.message);
          void connection.stop();
        });
      });
      connection.onclose(() => {
        if (disposed || accessRevoked) return;
        connected = false;
        setReady(false);
        clearTimeout(retryTimer);
        retryTimer = setTimeout(start, 5000);
      });
      void start();
    }

    return () => {
      disposed = true;
      clearTimeout(timer);
      clearTimeout(retryTimer);
      clearTimeout(cursorTimer);
      window.removeEventListener("beforeunload", beforeUnload);
      unsubscribe?.();
      // При переключении задачи сначала отправляем последний набранный фрагмент.
      void flush()
        .catch(() => {})
        .finally(() => {
          void connection?.stop();
        });
    };
  }, [api, roomId, taskId, refreshRoom]);

  return {
    document,
    status,
    ready,
    canWrite,
    editorOwner,
    peers,
    takeControl: () => controllerRef.current?.takeControl(),
    releaseControl: () => controllerRef.current?.releaseControl(),
    reload: () => controllerRef.current?.reload(),
    cursor: (position) => controllerRef.current?.cursor(position),
    change: (next) => controllerRef.current?.change(next),
    flush: () => controllerRef.current?.flush(),
  };
}
