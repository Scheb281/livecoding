import { languages } from "./languages.jsx";

export function createDemoApi() {
  const events = new EventTarget();
  const users = [
    {
      id: "u1",
      name: "Алексей Морозов",
      email: "interviewer@example.test",
      role: "interviewer",
      active: true,
      runQuota: 30,
    },
    {
      id: "u2",
      name: "Мария Орлова",
      email: "lead@example.test",
      role: "teamlead",
      active: true,
      runQuota: 50,
    },
    {
      id: "u3",
      name: "Администратор",
      email: "admin@example.test",
      role: "admin",
      active: true,
      runQuota: 100,
    },
    {
      id: "u4",
      name: "Демо-кандидат",
      email: "",
      role: "candidate",
      active: true,
      runQuota: 10,
      roomId: "room-1",
    },
  ];
  let user = users[0];
  const credentials = new Map();
  async function hashPassword(password, salt) {
    const key = await crypto.subtle.importKey(
      "raw",
      new TextEncoder().encode(password),
      "PBKDF2",
      false,
      ["deriveBits"],
    );
    const bits = await crypto.subtle.deriveBits(
      { name: "PBKDF2", salt, iterations: 100000, hash: "SHA-256" },
      key,
      256,
    );
    return Array.from(new Uint8Array(bits))
      .map((byte) => byte.toString(16).padStart(2, "0"))
      .join("");
  }
  const tasks = [
    {
      id: "task-1",
      title: "Два числа",
      description:
        "На вход подаются два целых числа. Выведите их сумму.\nПодумайте о граничных значениях и оцените сложность решения.",
      topic: "Базовые алгоритмы",
      difficulty: "easy",
      company: "Учебный проект",
      tags: ["массивы", "разминка"],
      language: "Python",
      starterCode: languages.Python.code,
      timeLimitMs: 2000,
      memoryLimitMb: 128,
      tests: [
        { id: "test-1", input: "2 3", expected: "5", hidden: false },
        { id: "test-2", input: "-1 1", expected: "0", hidden: true },
      ],
    },
    {
      id: "task-2",
      title: "Правильная скобочная последовательность",
      description:
        "Проверьте, правильно ли расставлены круглые скобки в строке. Выведите YES или NO.",
      topic: "Стек",
      difficulty: "medium",
      company: "",
      tags: ["строки", "стек"],
      language: "JavaScript",
      starterCode: languages.JavaScript.code,
      timeLimitMs: 3000,
      memoryLimitMb: 256,
      tests: [{ id: "test-3", input: "(())", expected: "YES", hidden: false }],
    },
  ];
  const rubrics = [
    {
      id: "rubric-1",
      name: "Базовое техническое интервью",
      criteria: [
        { id: "algorithm", label: "Алгоритм", maxScore: 5 },
        { id: "code", label: "Код", maxScore: 5 },
        { id: "communication", label: "Коммуникация", maxScore: 5 },
      ],
    },
  ];
  const rooms = [
    {
      id: "room-1",
      title: "Техническое интервью · демонстрация",
      candidateName: "Демо-кандидат",
      taskIds: ["task-1"],
      taskSnapshots: structuredClone([tasks[0]]),
      rubricId: "rubric-1",
      status: "active",
      ownerId: "u1",
      createdAt: new Date().toISOString(),
      messages: [],
      hints: [],
      evaluations: [],
      submissions: [],
      documents: {},
    },
  ];
  const invitations = new Map();
  const audit = [];
  const id = () => crypto.randomUUID();
  const clone = (value) => structuredClone(value);
  const fail = (message) => {
    throw new Error(message);
  };
  const requireRole = (...allowed) => {
    if (!allowed.includes(user?.role)) fail("Нет доступа для этой роли.");
  };
  const roomById = (roomId) =>
    rooms.find((room) => room.id === roomId) ?? fail("Комната не найдена.");
  const roomAccess = (roomId) => {
    const room = roomById(roomId);
    if (!user || (user.role === "candidate" && user.roomId !== roomId))
      fail("Нет доступа к комнате.");
    return room;
  };
  function notify(roomId) {
    events.dispatchEvent(new CustomEvent("change", { detail: roomId }));
  }
  function publicTask(task) {
    return user.role === "candidate"
      ? { ...task, tests: task.tests.filter((test) => !test.hidden) }
      : task;
  }
  function snapshot(room) {
    return {
      ...room,
      documents: undefined,
      taskSnapshots: undefined,
      tasks: room.taskSnapshots.map(publicTask),
      rubric: rubrics.find((rubric) => rubric.id === room.rubricId),
      canEditCode: user.role === "candidate" && room.status === "active",
      canInterview: user.role === "interviewer",
      evaluations: user.role === "candidate" ? [] : room.evaluations,
    };
  }
  function documentFor(room, taskId) {
    if (!room.taskIds.includes(taskId))
      fail("Задача не назначена этому интервью.");
    if (!room.documents[taskId]) {
      const task = room.taskSnapshots.find((item) => item.id === taskId);
      room.documents[taskId] = {
        taskId,
        language: task.language,
        code: task.starterCode,
        version: 0,
      };
    }
    return room.documents[taskId];
  }

  async function request(path, options = {}) {
    const method = options.method ?? "GET";
    const body = options.body ?? {};
    const parts = path.split("/").filter(Boolean);
    if (path === "/auth/me") return { user: clone(user) };
    if (path === "/auth/register/candidate" && method === "POST") {
      const email = String(body.email ?? "")
        .trim()
        .toLowerCase();
      const name = String(body.name ?? "").trim();
      const password = String(body.password ?? "");
      if (!name || name.length > 100)
        fail("Укажите имя длиной до 100 символов.");
      if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email))
        fail("Укажите корректный email.");
      if (password.length < 8 || password.length > 128)
        fail("Пароль должен содержать от 8 до 128 символов.");
      if (users.some((account) => account.email.toLowerCase() === email))
        fail("Этот email уже зарегистрирован.");
      const salt = crypto.getRandomValues(new Uint8Array(16));
      const hash = await hashPassword(password, salt);
      // Проверяем повторно после await: два параллельных запроса не создадут дубль.
      if (users.some((account) => account.email.toLowerCase() === email))
        fail("Этот email уже зарегистрирован.");
      user = {
        id: id(),
        name,
        email,
        role: "candidate",
        active: true,
        runQuota: 10,
      };
      users.push(user);
      credentials.set(user.id, { salt, hash });
      return { user: clone(user) };
    }
    if (path === "/auth/login" && method === "POST") {
      const email = String(body.email ?? "")
        .trim()
        .toLowerCase();
      const account = users.find(
        (candidate) => candidate.email.toLowerCase() === email,
      );
      const credential = account && credentials.get(account.id);
      if (
        !account?.active ||
        !credential ||
        (await hashPassword(String(body.password ?? ""), credential.salt)) !==
          credential.hash
      )
        fail(
          "Неверный email или пароль. В демо доступны аккаунты, зарегистрированные в этой вкладке.",
        );
      user = account;
      return { user: clone(user) };
    }
    if (path === "/demo/role") {
      user =
        users.find((item) => item.role === body.role && item.active) ??
        fail("Нет активного демо-пользователя этой роли.");
      return { user: clone(user) };
    }
    if (path === "/auth/logout") {
      user = null;
      return null;
    }
    if (path === "/invitations/redeem") {
      const invite = invitations.get(body.token);
      if (!invite || invite.used || invite.expiresAt < Date.now())
        fail(
          "Приглашение недействительно или уже использовано. В демо ссылка работает только в текущей вкладке.",
        );
      invite.used = true;
      user =
        user?.role === "candidate"
          ? user
          : users.find((item) => item.role === "candidate");
      user.roomId = invite.roomId;
      user.name = body.name;
      return { user: clone(user), roomId: invite.roomId };
    }
    if (!user) fail("Необходимо войти.");
    // Даже демо разделяет роли. На настоящем сервере эти проверки обязательны заново.
    if (method !== "GET")
      audit.unshift({
        id: id(),
        createdAt: new Date().toISOString(),
        actor: user.name,
        action: `${method} ${path}`,
      });
    if (parts[0] === "tasks") {
      requireRole("interviewer", "teamlead");
      if (method === "GET") return clone(tasks);
      if (method === "POST") {
        const task = { ...body, id: id() };
        tasks.unshift(task);
        return clone(task);
      }
      if (method === "PUT") {
        const index = tasks.findIndex((task) => task.id === parts[1]);
        if (index < 0) fail("Задача не найдена.");
        tasks[index] = { ...body, id: parts[1] };
        return clone(tasks[index]);
      }
    }
    if (path === "/rubrics") {
      requireRole("teamlead", "interviewer");
      if (method === "GET") return clone(rubrics);
      requireRole("teamlead");
      const rubric = { ...body, id: id() };
      rubrics.push(rubric);
      return clone(rubric);
    }
    if (path === "/rooms" && method === "GET")
      return clone(
        rooms
          .filter(
            (room) => user.role !== "candidate" || room.id === user.roomId,
          )
          .map(snapshot),
      );
    if (path === "/rooms" && method === "POST") {
      requireRole("interviewer");
      const room = {
        ...body,
        id: id(),
        taskSnapshots: body.taskIds.map((taskId) =>
          clone(tasks.find((task) => task.id === taskId)),
        ),
        status: "draft",
        ownerId: user.id,
        createdAt: new Date().toISOString(),
        messages: [],
        hints: [],
        evaluations: [],
        submissions: [],
        documents: {},
      };
      rooms.unshift(room);
      return clone(snapshot(room));
    }
    if (parts[0] === "rooms" && parts[1]) {
      const room = roomAccess(parts[1]);
      const action = parts[2];
      if (!action && method === "GET") return clone(snapshot(room));
      if (action === "documents" && method === "GET")
        return clone(documentFor(room, parts[3]));
      if (action === "start") {
        requireRole("interviewer");
        room.status = "active";
      } else if (action === "invitations") {
        requireRole("interviewer");
        const token = id();
        invitations.set(token, {
          roomId: room.id,
          used: false,
          expiresAt: Date.now() + 3600000,
        });
        return {
          token,
          expiresAt: new Date(Date.now() + 3600000).toISOString(),
        };
      } else if (action === "messages") {
        if (!body.text?.trim()) fail("Введите сообщение.");
        room.messages.push({
          id: id(),
          author: user.name,
          text: body.text.trim(),
          createdAt: new Date().toISOString(),
        });
      } else if (action === "hints") {
        requireRole("interviewer");
        room.hints.push({
          id: id(),
          taskId: body.taskId,
          text: body.text,
          usedAt: null,
          createdAt: new Date().toISOString(),
        });
      } else if (action === "hint-usage") {
        requireRole("candidate", "interviewer");
        const hint =
          room.hints.find((item) => item.id === body.hintId) ??
          fail("Подсказка не найдена.");
        hint.usedAt ??= new Date().toISOString();
      } else if (action === "evaluations") {
        requireRole("interviewer");
        room.evaluations = [{ ...body, id: id(), author: user.name }];
      } else if (action === "submissions") {
        requireRole("candidate");
        if (room.status !== "active") fail("Интервью ещё не запущено.");
        const document = documentFor(room, body.taskId);
        if (document.version !== body.documentVersion)
          fail("Перед отправкой сохраните актуальную версию кода.");
        room.submissions.push({
          id: id(),
          ...clone(document),
          createdAt: new Date().toISOString(),
        });
      } else if (action === "runs") {
        requireRole("candidate", "interviewer");
        fail(
          "В демо код не выполняется. Подключите ASP.NET и изолированный runner для реальных тестов.",
        );
      } else fail("Демо-маршрут не найден.");
      notify(room.id);
      return clone(snapshot(room));
    }
    if (path === "/reports") {
      requireRole("teamlead");
      return {
        candidates: rooms.map((room) => ({
          id: room.id,
          name: room.candidateName,
          interview: room.title,
          submitted: room.submissions.length,
          hintsUsed: room.hints.filter((hint) => hint.usedAt).length,
          averageScore: room.evaluations.length
            ? Object.values(room.evaluations[0].scores).reduce(
                (sum, score) => sum + score,
                0,
              ) / Object.keys(room.evaluations[0].scores).length
            : null,
        })),
        tasks: tasks.map((task) => ({
          id: task.id,
          title: task.title,
          attempts: rooms.filter((room) => room.taskIds.includes(task.id))
            .length,
          solved: 0,
          averageScore: null,
        })),
      };
    }
    if (parts[0] === "admin") {
      requireRole("admin");
      if (parts[1] === "audit") return clone(audit);
      if (parts[1] === "users" && method === "GET") return clone(users);
      if (parts[1] === "users" && method === "POST") {
        if (users.some((item) => item.email === body.email))
          fail("Пользователь с таким email уже существует.");
        const account = { ...body, id: id(), active: true, runQuota: 30 };
        users.push(account);
        return clone(account);
      }
      if (parts[1] === "users" && method === "PATCH") {
        const account =
          users.find((item) => item.id === parts[2]) ??
          fail("Пользователь не найден.");
        if (account.id === user.id && body.active === false)
          fail("Нельзя отключить самого себя.");
        Object.assign(account, body);
        return clone(account);
      }
    }
    fail("Маршрут пока не реализован в демо.");
  }

  return {
    mode: "demo",
    request,
    subscribe(roomId, callback) {
      const listener = (event) => {
        if (event.detail === roomId) callback();
      };
      events.addEventListener("change", listener);
      return () => events.removeEventListener("change", listener);
    },
    async updateDocument(roomId, update) {
      requireRole("candidate");
      const room = roomAccess(roomId);
      if (room.status !== "active") fail("Интервью не активно.");
      const document = documentFor(room, update.taskId);
      if (document.version !== update.baseVersion)
        fail("Версия кода изменилась. Откройте комнату заново.");
      Object.assign(document, {
        code: update.code,
        language: update.language,
        version: document.version + 1,
      });
      notify(roomId);
      return clone(document);
    },
  };
}
