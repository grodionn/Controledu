import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  test: {
    environment: "jsdom",
    globals: true,
    include: ["src/**/*.test.{ts,tsx}"],
    coverage: {
      provider: "v8",
      reporter: ["text", "lcov"],
      include: ["src/api/**/*.ts", "src/hooks/**/*.ts", "src/hooks/**/*.tsx"],
      thresholds: {
        lines: 30,
        functions: 15,
        statements: 30,
        branches: 20,
      },
    },
  },
  server: {
    host: "127.0.0.1",
    port: 5174,
    proxy: {
      "/api": "http://127.0.0.1:40557",
    },
  },
});
