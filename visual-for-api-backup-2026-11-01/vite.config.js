import react from "@vitejs/plugin-react";
import { defineConfig, loadEnv } from "vite";

export default defineConfig(({ mode }) => {
  const environment = loadEnv(mode, process.cwd(), "ASPNET_");
  const apiUrl =
    environment.ASPNET_URL || process.env.ASPNET_URL || "http://localhost:5000";
  return {
    plugins: [react()],
    server: {
      proxy: {
        "/api": { target: apiUrl },
        "/hubs": { target: apiUrl, ws: true },
      },
    },
  };
});
