import { formatDate } from "../api/format.jsx";
import { useState } from "react";
import { roles } from "../api/client.jsx";
import { useAction, useResource } from "../hooks/useResource.jsx";
import {
  ActionState,
  Empty,
  PageTitle,
  ResourceState,
} from "../components/Common.jsx";

function UserRow({ account, api, refresh }) {
  const [quota, setQuota] = useState(account.runQuota);
  const action = useAction();
  async function save(body) {
    if (
      await action.run(() =>
        api.request(`/admin/users/${account.id}`, { method: "PATCH", body }),
      )
    )
      refresh();
  }
  return (
    <tr>
      <td>
        <strong>{account.name}</strong>
        <small>{account.email || "Вход по приглашению"}</small>
        <ActionState action={action} />
      </td>
      <td>{roles[account.role]}</td>
      <td>
        <form
          className="inline-form"
          onSubmit={(event) => {
            event.preventDefault();
            void save({ runQuota: Number(quota) });
          }}
        >
          <input
            aria-label={`Квота ${account.name}`}
            type="number"
            min={0}
            max={10000}
            required
            value={quota}
            onChange={(event) => setQuota(event.target.value)}
          />
          <button disabled={action.busy} className="secondary">
            Сохранить
          </button>
        </form>
      </td>
      <td>
        <button
          className="quiet"
          disabled={action.busy}
          onClick={() => save({ active: !account.active })}
        >
          {account.active ? "Отключить" : "Включить"}
        </button>
      </td>
    </tr>
  );
}

export default function Admin({ api, section }) {
  const users = useResource(api, "/admin/users");
  const audit = useResource(api, "/admin/audit");
  const action = useAction();
  const [form, setForm] = useState({
    name: "",
    email: "",
    role: "interviewer",
  });
  const [setupLink, setSetupLink] = useState("");
  async function createUser(event) {
    event.preventDefault();
    const result = await action.run(() =>
      api.request("/admin/users", { method: "POST", body: form }),
    );
    if (result) {
      if (result.data?.setupToken) {
        const url = new URL(window.location.origin + window.location.pathname);
        url.searchParams.set("setup", result.data.setupToken);
        setSetupLink(url.href);
      }
      setForm({ name: "", email: "", role: "interviewer" });
      users.refresh();
      audit.refresh();
    }
  }
  if (section === "audit")
    return (
      <>
        <PageTitle eyebrow="Контроль изменений" title="Аудит">
          Серверный журнал действий. Кандидатский код и секреты сюда не
          выводятся.
        </PageTitle>
        <ResourceState resource={audit} />
        <section className="panel">
          <div className="section-heading">
            <h2>Последние события</h2>
            <button className="secondary" onClick={audit.refresh}>
              Обновить
            </button>
          </div>
          <div className="table-scroll">
            <table>
              <thead>
                <tr>
                  <th>Время</th>
                  <th>Кто</th>
                  <th>Действие</th>
                </tr>
              </thead>
              <tbody>
                {audit.data?.map((event) => (
                  <tr key={event.id}>
                    <td>{formatDate(event.createdAt)}</td>
                    <td>{event.actor}</td>
                    <td>{event.action}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {audit.data?.length === 0 && <Empty />}
        </section>
      </>
    );
  return (
    <>
      <PageTitle eyebrow="Доступ и ресурсы" title="Пользователи и квоты">
        Квота — число запусков за сутки. Ограничение проверяется на сервере
        перед постановкой кода в очередь.
      </PageTitle>
      <ResourceState resource={users} />
      <section className="panel">
        <h2>Добавить сотрудника</h2>
        <form className="form-grid" onSubmit={createUser}>
          <label>
            Имя
            <input
              required
              value={form.name}
              onChange={(event) =>
                setForm({ ...form, name: event.target.value })
              }
            />
          </label>
          <label>
            Email
            <input
              type="email"
              required
              value={form.email}
              onChange={(event) =>
                setForm({ ...form, email: event.target.value })
              }
            />
          </label>
          <label>
            Роль
            <select
              value={form.role}
              onChange={(event) =>
                setForm({ ...form, role: event.target.value })
              }
            >
              {Object.entries(roles)
                .filter(([role]) => role !== "candidate")
                .map(([role, label]) => (
                  <option key={role} value={role}>
                    {label}
                  </option>
                ))}
            </select>
          </label>
          <button disabled={action.busy}>Создать пользователя</button>
        </form>
        <ActionState action={action} />
        {setupLink && (
          <label>
            Одноразовая ссылка настройки пароля сотрудника
            <input
              readOnly
              value={setupLink}
              onFocus={(event) => event.target.select()}
            />
            <span className="muted">
              Передайте ссылку сотруднику. Она действует сутки и больше не будет
              показана после ухода со страницы.
            </span>
          </label>
        )}
      </section>
      <section className="panel">
        <h2>Учётные записи</h2>
        <div className="table-scroll">
          <table>
            <thead>
              <tr>
                <th>Пользователь</th>
                <th>Роль</th>
                <th>Запусков / сутки</th>
                <th>Доступ</th>
              </tr>
            </thead>
            <tbody>
              {users.data?.map((account) => (
                <UserRow
                  key={`${account.id}-${account.runQuota}`}
                  account={account}
                  api={api}
                  refresh={users.refresh}
                />
              ))}
            </tbody>
          </table>
        </div>
        {users.data?.length === 0 && <Empty />}
      </section>
    </>
  );
}
