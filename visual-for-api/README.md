# interview. — технические интервью

чтобы запустить приложение если нет sln
dotnet restore backend/Interview.sln

bash deploy/setup.sh
ы
в .env
COMPOSE_FILE=compose.yml:compose.postgres.yml
COMPOSE_PATH_SEPARATOR=:
DOMAIN=interview.example.com

Развёртывание на Linux: [Docker Compose, HTTPS, БД и перенос существующих данных](docs/LINUX_DOCKER.md).

React + Monaco + ASP.NET 10 + SignalR + EF Core/Npgsql.

Роли: кандидат, интервьюер, тимлид, администратор. Реализованы банк задач,
комнаты, приглашения, код в реальном времени, чат, подсказки, оценки,
снимки решений, отчёты, пользователи, квоты и аудит.

## Первый запуск с PostgreSQL

Пошагово: [backend/README.md](backend/README.md).
Создай пустую БД, укажи ConnectionStrings:Database через user-secrets,
выполни `dotnet run -- --migrate`, создай первого администратора через
`--bootstrap-admin`, затем запусти API на localhost:5000.

Фронтенд из корня проекта:

```powershell
npm install
npm run dev
```

Vite проксирует /api и /hubs на ASPNET_URL, по умолчанию http://localhost:5000.
Работай в обычном API-режиме. Сотрудников создаёт администратор и передаёт им
ссылки настройки пароля. Кандидат может зарегистрироваться по email или
войти гостем по одноразовому приглашению.

Отдельный деморежим на странице входа сохраняет данные только в памяти вкладки.
Он не заменяет БД при ошибках и не выполняет код.

## Проверки

```powershell
npm run lint
npm test
npm run build
dotnet test backend/Interview.sln
```

Для восстановления локального dotnet-ef: перейди в backend и выполни
`dotnet tool restore`. Типы таблиц, миграция и SQL лежат в backend.
Контракт фронтенда: [docs/API_AND_DATABASE.md](docs/API_AND_DATABASE.md).

Изолированный Docker runner находится в backend/Interview.Runner.
API содержит очередь, квоты и адаптер собственного runner; до его настройки
кнопка тестов получает понятный ответ 503. API пользовательский код не запускает.

Резервная копия перед этим обновлением: visual-for-api-backup-2026-11-01.

## Дополнения по заданию

Добавлены Docker-исполнитель на семи языках, таймер комнаты, курсоры и передача
управления редактором, оценки с весами, журнал с воспроизведением и шифрование
содержимого в БД.

- [Запуск Docker runner](backend/Interview.Runner/README.md)
- [Защита и обновление существующей БД](docs/SECURITY_AND_EXECUTION.md)
- [UML и ER-диаграмма](docs/UML.md)
- [Соответствие заданию и проверки](docs/REQUIREMENTS_CHECK.md)

Перед запуском обновлённого API примени миграции по backend/README.md.
Существующие открытые данные шифрует команда --encrypt-existing-data.



- [Как работают все классы и методы API](docs/API_CLASSES_AND_METHODS.md)

- [Подключение Yandex Cloud PostgreSQL](docs/YANDEX_DATABASE.md)
