import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { registerHooks } from "node:module";

// API-файлы переименованы в .jsx, но содержат обычный JavaScript без JSX-разметки.
// Этот загрузчик нужен только Node-тестам; Vite обрабатывает расширение самостоятельно.
registerHooks({
  load(url, context, nextLoad) {
    if (url.endsWith("/api/demo.jsx") || url.endsWith("/api/languages.jsx")) {
      return {
        format: "module",
        source: readFileSync(new URL(url), "utf8"),
        shortCircuit: true,
      };
    }
    return nextLoad(url, context);
  },
});
const { createDemoApi } = await import("../src/api/demo.jsx");

const post = (api, path, body) => api.request(path, { method: "POST", body });
const role = (api, value) => post(api, "/demo/role", { role: value });

test("регистрация выдаёт только роль кандидата и не даёт доступ к чужому интервью", async () => {
  const api = createDemoApi();
  const result = await post(api, "/auth/register/candidate", {
    name: "Новый кандидат",
    email: "candidate@example.test",
    password: "SamplePass123!",
    role: "admin",
  });
  assert.equal(result.user.role, "candidate");
  assert.equal(result.user.password, undefined);
  assert.deepEqual(await api.request("/rooms"), []);
  await assert.rejects(api.request("/rooms/room-1"), /Нет доступа/);
  await assert.rejects(api.request("/admin/users"), /Нет доступа/);
});

test("после регистрации кандидат может выйти и войти с email и паролем", async () => {
  const api = createDemoApi();
  const registered = await post(api, "/auth/register/candidate", {
    name: "Кандидат",
    email: " Candidate@Example.Test ",
    password: "SamplePass123!",
  });
  await post(api, "/auth/logout");
  await assert.rejects(
    post(api, "/auth/login", {
      email: "candidate@example.test",
      password: "wrong",
    }),
    /Неверный email/,
  );
  const signedIn = await post(api, "/auth/login", {
    email: "candidate@example.test",
    password: "SamplePass123!",
  });
  assert.equal(signedIn.user.id, registered.user.id);
  await assert.rejects(
    post(api, "/auth/register/candidate", {
      name: "Дубль",
      email: "CANDIDATE@example.test",
      password: "SamplePass123!",
    }),
    /уже зарегистрирован/,
  );
});

test("приглашение связывается с зарегистрированным аккаунтом кандидата", async () => {
  const api = createDemoApi();
  const invitation = await post(api, "/rooms/room-1/invitations", {});
  const registered = await post(api, "/auth/register/candidate", {
    name: "Кандидат",
    email: "candidate@example.test",
    password: "SamplePass123!",
  });
  const joined = await post(api, "/invitations/redeem", {
    token: invitation.token,
    name: "Кандидат",
  });
  assert.equal(joined.user.id, registered.user.id);
  assert.equal(joined.roomId, "room-1");
});

test("кандидат не получает скрытые тесты и доступ к администрированию", async () => {
  const api = createDemoApi();
  await role(api, "candidate");
  const room = await api.request("/rooms/room-1");
  assert.equal(room.canEditCode, true);
  assert.equal(room.canInterview, false);
  assert.equal(room.tasks[0].tests.length, 1);
  assert.equal(
    room.tasks[0].tests.some((item) => item.hidden),
    false,
  );
  await assert.rejects(api.request("/admin/users"), /Нет доступа/);
  await assert.rejects(post(api, "/tasks", {}), /Нет доступа/);
});

test("приглашение можно погасить только один раз", async () => {
  const api = createDemoApi();
  const invitation = await post(api, "/rooms/room-1/invitations", {});
  await post(api, "/auth/logout");
  const result = await post(api, "/invitations/redeem", {
    token: invitation.token,
    name: "Кандидат",
  });
  assert.equal(result.roomId, "room-1");
  assert.equal(result.user.role, "candidate");
  await assert.rejects(
    post(api, "/invitations/redeem", {
      token: invitation.token,
      name: "Другой",
    }),
    /уже использовано/,
  );
  await assert.rejects(
    post(api, "/invitations/redeem", { token: "unknown", name: "Другой" }),
    /недействительно/,
  );
});

test("устаревшее изменение кода не перезаписывает сохранённую версию", async () => {
  const api = createDemoApi();
  await role(api, "candidate");
  const initial = await api.request("/rooms/room-1/documents/task-1");
  const saved = await api.updateDocument("room-1", {
    taskId: "task-1",
    language: "Python",
    code: "print(5)",
    baseVersion: initial.version,
  });
  assert.equal(saved.version, initial.version + 1);
  await assert.rejects(
    api.updateDocument("room-1", {
      taskId: "task-1",
      language: "Python",
      code: "old",
      baseVersion: initial.version,
    }),
    /Версия/,
  );
  assert.equal(
    (await api.request("/rooms/room-1/documents/task-1")).code,
    "print(5)",
  );
  await role(api, "teamlead");
  await assert.rejects(
    api.updateDocument("room-1", { taskId: "task-1" }),
    /Нет доступа/,
  );
});

test("подсказка и отметка использования попадают в отчёт ровно один раз", async () => {
  const api = createDemoApi();
  const room = await post(api, "/rooms/room-1/hints", {
    taskId: "task-1",
    text: "Разберите вход",
  });
  await role(api, "candidate");
  await post(api, "/rooms/room-1/hint-usage", { hintId: room.hints[0].id });
  await post(api, "/rooms/room-1/hint-usage", { hintId: room.hints[0].id });
  await role(api, "teamlead");
  assert.equal((await api.request("/reports")).candidates[0].hintsUsed, 1);
});

test("деморежим не выдаёт фиктивные результаты выполнения кода", async () => {
  const api = createDemoApi();
  await role(api, "candidate");
  await assert.rejects(
    post(api, "/rooms/room-1/runs", { taskId: "task-1" }),
    /код не выполняется/,
  );
});

test("изменение банка не меняет задачу уже созданного интервью", async () => {
  const api = createDemoApi();
  const [task] = await api.request("/tasks");
  await api.request(`/tasks/${task.id}`, {
    method: "PUT",
    body: { ...task, description: "Новое условие" },
  });
  await role(api, "candidate");
  assert.equal(
    (await api.request("/rooms/room-1")).tasks[0].description,
    task.description,
  );
});

test("отправка решения фиксирует указанную версию и не меняется при дальнейшей правке", async () => {
  const api = createDemoApi();
  await role(api, "candidate");
  await assert.rejects(
    post(api, "/rooms/room-1/submissions", {
      taskId: "task-1",
      documentVersion: 1,
    }),
    /актуальную версию/,
  );
  const room = await post(api, "/rooms/room-1/submissions", {
    taskId: "task-1",
    documentVersion: 0,
  });
  const originalCode = room.submissions[0].code;
  await api.updateDocument("room-1", {
    taskId: "task-1",
    language: "Python",
    code: "new code",
    baseVersion: 0,
  });
  assert.equal(
    (await api.request("/rooms/room-1")).submissions[0].code,
    originalCode,
  );
});
