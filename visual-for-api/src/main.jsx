import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import "./index.css";
import App from "./App.jsx";
import { loader } from "@monaco-editor/react";

// В Docker редактор загружается с нашего сайта, а не со сторонней CDN.
if (import.meta.env.PROD) loader.config({ paths: { vs: "/monaco/vs" } });

createRoot(document.getElementById("root")).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
