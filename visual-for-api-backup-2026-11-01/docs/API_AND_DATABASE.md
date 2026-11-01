# API, SignalR и PostgreSQL

Бэкенд теперь реализован в `backend/Interview.Api`, а не только описан контрактом.
Пошаговое подключение БД и создание первого администратора — в
`backend/README.md`. Сгенерированный SQL всех 28 таблиц:
`backend/database/initial-schema.sql`. EF-миграция и снимок модели:
`backend/Interview.Api/Data/Migrations`.

React использует cookie-сеанс и относительные адреса `/api` и `/hubs/interviews`.
Сначала создай пустую PostgreSQL-БД, настрой ConnectionStrings:Database,
выполни `dotnet run -- --migrate`, затем `--bootstrap-admin`. Подробнее в README.

## Авторизация

| Метод | Путь после /api | Данные |
|---|---|---|
| GET | /auth/csrf | `{ token }` и cookie; передавать token в X-CSRF-Token при изменениях |
| GET | /auth/me | `{ user }` или 401 |
| POST | /auth/register/candidate | `{ name, email, password }` → `{ user }`, фиксированная роль candidate |
| POST | /auth/login | `{ email, password }` → `{ user }` и cookie |
| POST | /auth/logout | Завершение сессии, 204 |
| POST | /auth/setup-password | `{ token, password }` → `{ user }`, установка пароля сотрудника по ссылке администратора |
| POST | /invitations/redeem | `{ token, name }` → `{ user, roomId }`, одноразовый вход кандидата |

User: `{ id, name, email, role, active, runQuota, roomId? }`.
Роли: candidate / interviewer / teamlead / admin. Роль нельзя выбрать при регистрации.
Повтор пароля проверяется в форме и в API не отправляется.
После входа/регистрации/выхода/установки пароля React обновляет CSRF-токен.

## Задачи, рубрики, отчёты

| Метод | Путь | Права |
|---|---|---|
| GET, POST | /tasks | Интервьюер и тимлид |
| PUT | /tasks/{id} | Автор или тимлид |
| GET | /rubrics | Интервьюер и тимлид |
| POST | /rubrics | Тимлид |
| GET | /reports | Тимлид |

Task: `{ id, title, description, topic, difficulty, company, tags, language,
starterCode, timeLimitMs, memoryLimitMb, tests }`.
Test: `{ id?, input, expected, hidden }`. Для POST id не нужен.
difficulty: easy/medium/hard. Языки: Python, JavaScript, TypeScript, C++, C#, Go, Java.
Ограничения: 100–10000 мс, 32–1024 МБ; от 1 до 50 тестов, до 20 тегов.
Общий HTTP-запрос ограничен 256 КиБ, даже если отдельные поля укладываются в лимит.
Rubric: `{ id, name, criteria: [{ id, label, maxScore }] }`.
POST рубрики принимает name и criteria, ключи критериев не должны повторяться.

Reports: `{ candidates, tasks }`.
Candidates: `[{ id, name, interview, submitted, hintsUsed, averageScore }]`.
Tasks: `[{ id, title, attempts, solved, averageScore }]`.
Пустая оценка — null. Успех задачи засчитывается по проверенному снимку решения.
Средний балл задачи — среднее оценок интервью, где она назначалась.

## Проведение интервью

| Метод | Путь | Данные |
|---|---|---|
| GET | /rooms | Комнаты текущего кандидата или владельца-интервьюера |
| POST | /rooms | `{ title, candidateName, taskIds, rubricId }` |
| GET | /rooms/{id} | Снимок комнаты с задачами, чатом, подсказками, оценками и решениями |
| POST | /rooms/{id}/start | Запуск подготовленной комнаты |
| POST | /rooms/{id}/complete | Завершение активной комнаты |
| POST | /rooms/{id}/invitations | Ответ `{ token, expiresAt }`; новая ссылка отменяет предыдущие |
| GET | /rooms/{id}/documents/{taskId} | `{ taskId, language, code, version }` |
| POST | /rooms/{id}/messages | `{ text }` |
| POST | /rooms/{id}/hints | `{ taskId, text }`, интервьюер |
| POST | /rooms/{id}/hint-usage | `{ hintId }`, идемпотентная отметка использования |
| POST | /rooms/{id}/evaluations | `{ rubricId, scores, comment }`, интервьюер |
| POST | /rooms/{id}/submissions | `{ taskId, documentVersion }`, кандидат |
| POST | /rooms/{id}/runs | `{ taskId, documentVersion }`, создание запуска |
| GET | /rooms/{id}/runs/{runId} | `{ id, status, summary, output, tests }` |

Room: `{ id, title, candidateName, status, createdAt, taskIds, tasks, rubric,
messages, hints, evaluations, submissions, canEditCode, canInterview }`.
Статусы комнаты: draft / active / completed.
Сообщение: `{ id, author, text, createdAt }`.
Подсказка: `{ id, taskId, text, createdAt, usedAt }`.
Оценка: `{ id, author, scores: { algorithm: 4, code: 3, communication: 5 }, comment }`.
Точный набор scores зависит от снимка рубрики. Баллы проверяются по maxScore.
Submissions содержит метаданные снимков: id, taskId, documentVersion, createdAt.
Скрытые тесты и оценки интервьюера не выдаются кандидату.

Запуски требуют настроенного отдельного runner, по умолчанию возвращается 503.
Статусы: queued / running / completed / failed. Опрос результатов не тратит квоту.
Самого контейнерного sandbox в репозитории нет: контракт адаптера описан в backend/README.md.

## SignalR

Hub: `/hubs/interviews` с cookie-аутентификацией.
`JoinInterview(roomId)` проверяет доступ, входит в группу, назначает писателя.
`UpdateCode(roomId, { taskId, language, code, baseVersion, mutationId })`
сохраняет версию и историю в БД, возвращает `{ taskId, language, code, version }`.
Другие участники получают `CodeUpdated(document)`; отправитель — подтверждение invoke.
`RoomChanged` заставляет React повторно прочитать комнату через API.
Задачи, чат, подсказки и оценки остаются в PostgreSQL, а не в памяти Hub.
`AccessRevoked` блокирует отключённый сеанс.
Один экземпляр API, одна вкладка-писатель кандидата. Для нескольких реплик
потребуются общий реестр соединений и распределённая аренда писателя.

## Администратор

| Метод | Путь | Данные |
|---|---|---|
| GET | /admin/users | Массив User |
| POST | /admin/users | `{ name, email, role }` → `{ user, setupToken, expiresAt }` |
| PATCH | /admin/users/{id} | `{ active }` или `{ runQuota }` → User |
| GET | /admin/audit | Последние 100 событий `{ id, createdAt, actor, action, entityId }` |
| GET | /admin/rooms/{id}/code-history?taskId=...&afterVersion=0 | До 200 версий кода указанной задачи |

setupToken превращается фронтендом в одноразовую ссылку `?setup=...` для сотрудника.
В БД хранятся только хеш токена и срок. Ссылка не отправляется по почте автоматически.
runQuota — целое 0–10000 запусков в сутки UTC, 0 запрещает постановку новых заданий.
Лимит активных тимлидов — 10. Последнего администратора отключить нельзя.

Все входные DTO: `backend/Interview.Api/Contracts/Dtos.cs`.
Связи/типы/ограничения: `backend/Interview.Api/Data/AppDbContext.cs`.
Точная создаваемая схема: `backend/database/initial-schema.sql`.
