import CodeBox from "./CodeBox";
import ViewQuestion from "./viewquesion";
import "./livecoding.css";

function LiveCoding({ user }) {
  return (
    <main className="live-coding">
      <header className="live-coding__header">
        <h1>Live Coding</h1>
        <p>Задание и решение в одном окне</p>
      </header>

      <div className="live-coding__layout">
        <aside className="live-coding__questions">
          <h2>Задания</h2>
          <ViewQuestion user={user} />
        </aside>

        <section
          className="live-coding__editor"
          aria-label="Редактор решения и консоль"
        >
          <CodeBox />
        </section>
      </div>
    </main>
  );
}

export default LiveCoding;
