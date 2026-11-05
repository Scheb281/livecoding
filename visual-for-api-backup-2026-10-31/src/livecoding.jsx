import CodeBox from "./CodeBox";
import ViewQuestion from "./viewquesion";
import useInterviewRoom from "./useInterviewRoom";
import "./livecoding.css";

function LiveCoding({ user, roomId = "" }) {
  const room = useInterviewRoom(roomId);
  return (
    <main className="live-coding">
      <header className="live-coding__header">
        <h1>Live Coding</h1>
        <p role="status">
          {roomId ? room.status : "Задание и решение в одном окне"}
        </p>
      </header>
      <div className="live-coding__layout">
        <aside className="live-coding__questions">
          <h2>Задания</h2>
          {roomId ? (
            <ol className="question-list">
              {(room.snapshot?.tasks ?? []).map((task) => (
                <li key={task.id}>
                  <p>{task.description}</p>
                </li>
              ))}
            </ol>
          ) : (
            <ViewQuestion user={user} />
          )}
        </aside>
        <section
          className="live-coding__editor"
          aria-label="Редактор решения и консоль"
        >
          <CodeBox
            sharedDocument={room.snapshot}
            readOnly={
              Boolean(roomId) &&
              (!room.isConnected || !room.snapshot?.canEditCode)
            }
            onDocumentChange={roomId ? room.updateCode : undefined}
          />
        </section>
      </div>
    </main>
  );
}

export default LiveCoding;
