export function Notice({ children, error = false }) {
  return children ? (
    <p
      className={`notice ${error ? "notice--error" : ""}`}
      role={error ? "alert" : "status"}
    >
      {children}
    </p>
  ) : null;
}

export function ResourceState({ resource }) {
  if (resource.loading) return <Notice>Загрузка…</Notice>;
  if (resource.error)
    return (
      <div>
        <Notice error>{resource.error}</Notice>
        <button onClick={resource.refresh}>Повторить запрос</button>
      </div>
    );
  return null;
}

export function ActionState({ action }) {
  return (
    <>
      <Notice error>{action.error}</Notice>
      <Notice>{action.message}</Notice>
    </>
  );
}

export function Empty({ children = "Пока нет записей." }) {
  return <p className="empty">{children}</p>;
}

export function PageTitle({ eyebrow, title, children }) {
  return (
    <header className="page-title">
      <span className="eyebrow">{eyebrow}</span>
      <h1>{title}</h1>
      {children && <p>{children}</p>}
    </header>
  );
}
