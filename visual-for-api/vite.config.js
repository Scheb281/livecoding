import react from "@vitejs/plugin-react";
import { defineConfig, loadEnv } from "vite";
import { cp } from "node:fs/promises";
import { resolve } from "node:path";

export default defineConfig(({ mode }) => {
  const environment = loadEnv(mode, process.cwd(), "ASPNET_");
  const apiUrl =
    environment.ASPNET_URL || process.env.ASPNET_URL || "http://localhost:5000";
  return {
    plugins: [
      react(),
      {
        name: "copy-monaco-assets",
        apply: "build",
        // Локальные файлы редактора нужны и в Docker, и при npm run preview.
        async writeBundle(output) {
          await cp(
            resolve("node_modules/monaco-editor/min/vs"),
            resolve(output.dir ?? "dist", "monaco/vs"),
            { recursive: true },
          );
        },
      },
    ],
    cacheDir: ".vite-cache",
    server: {
      watch: { ignored: ["**/backend/**"] },
      proxy: {
        "/api": { target: apiUrl },
        "/hubs": { target: apiUrl, ws: true },
      },
    },
  };
});

