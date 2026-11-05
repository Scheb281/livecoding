import { useState } from "react";
import { useResource } from "../hooks/useResource.jsx";
import { Empty, PageTitle, ResourceState } from "../components/Common.jsx";
import SessionReplay from "../components/SessionReplay.jsx";

export default function Recordings({ api }) {
  const resource = useResource(api, api.mode === "demo" ? null : "/recordings");
  const [selected, setSelected] = useState("");
  return (
    <>
      <PageTitle eyebrow="История интервью" title="Записи сессий">
        Код, тесты, подсказки, чат и оценки в общем журнале событий.
      </PageTitle>
      {api.mode === "demo" ? (
        <Empty>Записи доступны при подключённом ASP.NET API.</Empty>
      ) : (
        <>
          <ResourceState resource={resource} />
          <label>
            Интервью
            <select
              value={selected}
              onChange={(event) => setSelected(event.target.value)}
            >
              <option value="">Выберите запись</option>
              {resource.data?.map((room) => (
                <option key={room.id} value={room.id}>
                  {room.candidateName} · {room.title}
                </option>
              ))}
            </select>
          </label>
          {selected && (
            <SessionReplay key={selected} api={api} roomId={selected} />
          )}
        </>
      )}
    </>
  );
}
