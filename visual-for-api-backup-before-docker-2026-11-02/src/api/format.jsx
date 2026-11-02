export function formatDate(value) {
  return value ? new Date(value).toLocaleString("ru-RU") : "—";
}
