# Docker runner

Запуск всех сервисов на Linux через Compose: [LINUX_DOCKER.md](../../docs/LINUX_DOCKER.md).

Отдельный доверенный ASP.NET-сервис управляет Docker CLI. Только он имеет доступ
к Docker. В контейнер кандидата Docker socket, каталоги хоста и секреты не попадают.
Для публичного сервера выдели отдельную VM/хост; контейнеризация не устраняет
уязвимости общего ядра. Весь проект не нужно запускать внутри контейнера кандидата.

## Локальный запуск

Нужны .NET SDK 10, Docker Engine с Linux-контейнерами и доступ к официальным
реестрам при сборке. Из каталога backend/Interview.Runner:

```powershell
docker build -t visual-interview-sandbox:local sandbox
$runnerKey = [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
dotnet user-secrets set 'Runner:ApiKey' $runnerKey
dotnet user-secrets set 'Runner:ApiKey' $runnerKey --project ../Interview.Api
dotnet user-secrets set 'Runner:BaseUrl' 'http://localhost:5080/' --project ../Interview.Api
dotnet run --urls http://localhost:5080
```

API запускается отдельно. Общий ключ не публикуй и не добавляй в git.
Не выставляй runner в Интернет. В Production API требует HTTPS для Runner:BaseUrl;
настрой TLS на внутреннем reverse proxy, проверяющий сертификат клиент не отключай.
Внешние библиотеки не устанавливаются кодом кандидата; доступны стандартные
библиотеки семи языков. TypeScript поддерживает стандартные модули Node.

POST /execute принимает Bearer-ключ и SandboxJob (см. Program.cs). Сравнение
с ожидаемыми ответами делает API. /health/live проверяет только процесс.

## Ограничения

Новый контейнер для каждого теста; network=none, read-only корень, uid 65532,
cap-drop ALL, no-new-privileges, pids=64, cpu=1, память и swap ограничены.
/work — tmpfs 192 МБ с exec для скомпилированных программ; /tmp — 32 МБ noexec.
Компиляция: до 30 секунд и 768 МБ. Выполнение: лимит задачи (100–10000 мс,
32–1024 МБ). Общий дедлайн задания: 180 секунд. Вывод: до 32768 байт.
Контейнер удаляется при успехе, ошибке и отмене. Образ не скачивается автоматически
во время запуска: сначала оператор собирает его командой выше.

Ошибки compile/runtime/timeout/resource/output сохраняются в результатах тестов.
Квота API и лимит runner ограничивают частоту. Для нескольких runner потребуется
надёжная распределённая очередь; текущий вариант рассчитан на один API.
