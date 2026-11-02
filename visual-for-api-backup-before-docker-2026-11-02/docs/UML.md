# UML платформы технических интервью

Диаграммы соответствуют реализованному коду. `DockerSandbox` выполняет роль
CodeRunner и находится в отдельном процессе `Interview.Runner`.

## Диаграмма классов

```mermaid
classDiagram
    class AppUser {
        string Id
        string Role
        bool IsActive
        int RunQuota
    }
    class Room {
        Guid Id
        string Status
        int DurationMinutes
        DateTime SessionExpiresAt
        int NextEventSequence
    }
    class RoomTask {
        Guid TaskId
        string TaskSnapshot
    }
    class CodeDocument {
        string Code
        int Version
    }
    class Submission {
        string Code
        int DocumentVersion
        Guid VerifiedRunId
    }
    class CodeRun {
        string Status
        bool AllPassed
        string Output
    }
    class CodeRunTest {
        bool Passed
        string ErrorKind
        int DurationMs
        int MemoryKb
    }
    class RubricCriterion {
        string Label
        int MaxScore
        decimal Weight
    }
    class Evaluation {
        string Comment
        EvaluationScore[] Scores
    }
    class SessionEvent {
        int Sequence
        string Type
        string Visibility
        string Payload
    }
    class SessionRecorder {
        Append(roomId, type, actor, payload)
    }
    class ScoringService {
        Calculate(rubric, scores) decimal
    }
    class RunWorker {
        ExecuteAsync()
    }
    class DockerSandbox {
        Execute(job) SandboxResult
        ExecuteTest(job, test)
    }
    AppUser "1" --> "*" Room : участвует
    Room "1" --> "*" RoomTask : снимки задач
    RoomTask "1" --> "1" CodeDocument
    Room "1" --> "*" Submission
    Room "1" --> "*" CodeRun
    CodeRun "1" --> "*" CodeRunTest
    Submission --> CodeRun : точная проверенная версия
    Room "1" --> "*" Evaluation
    Evaluation ..> RubricCriterion : снимок критериев и весов
    ScoringService ..> Evaluation
    Room "1" --> "*" SessionEvent
    SessionRecorder --> SessionEvent : сохраняет атомарно
    RunWorker ..> DockerSandbox : HTTP с Bearer-ключом
    DockerSandbox --> CodeRunTest : результат контейнера
```

## Use Case по ролям

```mermaid
flowchart LR
    C[Кандидат] --> I([Погасить одноразовое приглашение])
    C --> E([Писать код и видеть курсор собеседника])
    C --> T([Запустить тесты])
    C --> S([Отправить снимок решения])
    C --> H([Отметить использование подсказки])
    C --> Chat([Чат])
    C --> Replay([Просмотреть доступную запись])
    V[Интервьюер] --> Tasks([Создать задачу с тестами и лимитами])
    V --> Room([Создать, открыть, начать и завершить комнату])
    V --> Invite([Создать приглашение с TTL])
    V --> E
    V --> T
    V --> Chat
    V --> Hint([Передать подсказку])
    V --> Eval([Оценить по рубрике с весами])
    V --> Replay
    L[Тимлид] --> Bank([Банк задач и теги])
    L --> Rubric([Настроить критерии и веса])
    L --> Reports([Сравнить кандидатов и качество задач])
    L --> Replay
    A[Администратор] --> Users([Пользователи и доступ])
    A --> Quota([Квоты запусков])
    A --> Audit([Аудит])
    A --> Replay
```

## Состояния комнаты

```mermaid
stateDiagram-v2
    [*] --> Created: создание
    Created --> Waiting: POST open
    Waiting --> Waiting: приглашение и вход кандидата
    Waiting --> InProgress: POST start
    InProgress --> Finished: POST complete
    InProgress --> Finished: истёк SessionExpiresAt
    Finished --> [*]
```

В БД значения называются `draft`, `waiting`, `active`, `completed`.
Для совместимости со старым клиентом API допускает `start` сразу из `draft`;
новый интерфейс использует полный путь через Waiting. Таймер начинает отсчёт
при переходе в InProgress. После окончания сервер запрещает изменения кода,
новые запуски, чат и подсказки. Запись и оценки остаются доступны по правам.

## Последовательность запуска тестов

```mermaid
sequenceDiagram
    actor Candidate as Кандидат
    participant React
    participant Hub as SignalR Hub
    participant API
    participant PG as PostgreSQL
    participant Worker as RunWorker
    participant Runner as DockerSandbox
    participant Container as Одноразовый контейнер
    Candidate->>React: Запустить тесты
    React->>Hub: UpdateCode(baseVersion, mutationId)
    Hub->>PG: транзакция: документ, версия, CodeEvent, SessionEvent
    PG-->>Hub: commit
    Hub-->>React: подтверждённая version
    React->>API: POST runs(taskId, documentVersion)
    API->>PG: права, срок комнаты, квота, снимок кода, очередь
    PG-->>API: commit
    API-->>React: queued + runId
    Worker->>PG: взять задание
    Worker->>Runner: HTTPS POST execute + Bearer
    loop каждый тест
        Runner->>Container: создать без сети и host-монтов
        Runner->>Container: исходник и ввод через stdin
        Container-->>Runner: компиляция готова
        Runner->>Container: уменьшить память до лимита задачи
        Container-->>Runner: stdout, exitCode, timeout, memory
        Runner->>Container: rm --force в finally
    end
    Runner-->>Worker: результаты всех тестов
    Worker->>PG: сравнить с expected, сохранить результаты и SessionEvent
    Worker-->>Hub: RoomChanged
    Hub-->>React: уведомление участникам
    React->>API: GET runs/runId
    API-->>React: результаты без скрытого ввода/вывода
```

## Связи основных сущностей БД

```mermaid
erDiagram
    AppUser ||--o{ Room : interviewer
    AppUser ||--o{ Submission : candidate
    BankTask ||--o{ TaskTest : tests
    Room ||--o{ RoomTask : assignments
    BankTask ||--o{ RoomTask : task
    Room ||--o{ Invitation : invites
    RoomTask ||--|| CodeDocument : current
    RoomTask ||--o{ CodeEvent : versions
    RoomTask ||--o{ Hint : hints
    Hint ||--o{ HintUsage : usage
    Room ||--o{ ChatMessage : chat
    Room ||--o{ SessionEvent : recording
    Room ||--o{ Evaluation : evaluation
    Evaluation ||--o{ EvaluationScore : scores
    Rubric ||--o{ RubricCriterion : criteria
    RoomTask ||--o{ CodeRun : runs
    CodeRun ||--o{ CodeRunTest : results
    CodeRun o|--o{ Submission : verifiedSnapshot
```

Полный SQL, включая Identity, индексы, ограничения и историю миграций,
находится в `backend/database/initial-schema.sql`. После обновления таблиц — 29.
