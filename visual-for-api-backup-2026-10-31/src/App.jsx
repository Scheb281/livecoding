import LiveCoding from "./livecoding";
import Interviewer from "./interviewer";
import "./App.css";

function App() {
  // Это выбор страницы для разработки. Настоящие права определяет ASP.NET.
  const parameters = new URLSearchParams(window.location.search);
  const roomId = parameters.get("room") ?? "";
  return parameters.get("view") === "teamlead" ? (
    <Interviewer key={roomId} roomId={roomId} />
  ) : (
    <LiveCoding key={roomId} roomId={roomId} />
  );
}

export default App;
