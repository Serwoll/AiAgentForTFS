# План обучения и развития AiAgentForTFS

Дата старта: 2026-06-03

## Главная цель

Самостоятельно написать и развивать рабочего AI-агента на C# с Microsoft Agent Framework для QA/TFS-задач.

Важно: код пишет ученик. Codex выступает наставником: объясняет идеи, задает маленькие задачи, ревьюит код, помогает разбирать ошибки и проектировать следующие шаги.

## Принципы работы

- Сначала понять идею, потом писать код.
- Двигаться маленькими задачами, которые можно проверить за один запуск.
- После каждого слоя делать мини-зачет: ученик объясняет flow своими словами.
- Не копировать готовую реализацию без разбора.
- Любой новый код должен быть понятен автору: зачем он нужен, какие данные принимает, что возвращает, где может упасть.

## Текущий уровень

Уже разобрано:

- CLI flow в `Program.cs`.
- `async/await`, `Task<int>` на прикладном уровне.
- `record` как модель данных.
- `class` как сервис/поведение.
- `static class` как набор функций без состояния.
- `IReadOnlyList` против `List`.
- nullable `string?`.
- `return 0/1/2...` как коды завершения.
- `appsettings.json`, env vars, `??`, `?.`.
- базовый смысл HTTP-клиентов, PAT auth, JSON deserialize.
- общий смысл PR enrichment: найти PR link, загрузить PR metadata/commits/changed files, добавить в prompt.
- `IChatClient` как адаптер между Microsoft Agent Framework и конкретным LLM-провайдером.
- Разница между `Task<T>` и `IAsyncEnumerable<T>`: один асинхронный результат против потоковой выдачи.
- OAuth flow GigaChat: сначала получить `access_token`, потом использовать `Authorization: Bearer`.
- `JsonPropertyName` для positional record через `[property: ...]`.
- `DateTimeOffset.UtcNow` и `FromUnixTimeMilliseconds(...)` для кэша access token.
- `FormUrlEncodedContent` для OAuth body и `StringContent` с `application/json` для chat body.
- Маппинг `ChatMessage`/`ChatRole` из `Microsoft.Extensions.AI` в DTO GigaChat.

Пока требуют практики:

- generics: `T`, `Task<T>`, `GetJsonAsync<T>`.
- LINQ: `Select`, `SelectMany`, `Concat`, `Where`, `ToList`.
- regex и named groups.
- primary constructor и классический constructor с `_fields`.
- граница между DTO, domain model, service и orchestration.
- Microsoft Agent Framework concepts: agents, sessions, tools, middleware, workflows.
- LINQ-преобразования коллекций, особенно `IEnumerable<ChatMessage>` -> `List<GigaChatMessage>`.
- Отличие C# object/DTO от HTTP body (`HttpContent`).
- Работа с API response collections: `choices` как список, выбор первого элемента и проверка пустого ответа.
- Аккуратная диагностика HTTP ошибок без утечки токенов/секретов в console output.

## Текущий технический checkpoint на 2026-07-03

Идет интеграция GigaChat как альтернативного LLM-провайдера вместо Ollama.

Сделано:

- `QaChecklistWorkflow` зависит от `IChatClient`, а не от конкретного Ollama-клиента.
- `Program.cs` выбирает провайдера через `CreateChatClient(...)`.
- `AppConfig` содержит настройки `Llm.Provider` и секцию `GigaChat`.
- Создан `GigaChatClient : IChatClient`.
- Реализован OAuth-запрос к GigaChat:
  - `Authorization: Basic <GigaChatAuthKey>`;
  - header `RqUID`;
  - form body `scope`;
  - парсинг `access_token` и `expires_at`;
  - кэш токена с запасом в 1 минуту.
- Сертификаты для GigaChat на рабочей машине пришлось установить отдельно; без доверенного сертификата .NET падал на SSL.
- Реализованы DTO для `/chat/completions` и маппинг сообщений:
  - `ChatRole.User` -> `user`;
  - `ChatRole.Assistant` -> `assistant`;
  - `ChatRole.System` -> `system`.
- Последний проверенный `dotnet build` после реализации chat request был зеленым.

Следующий шаг:

- Запустить реальную генерацию через `Llm.Provider = "GigaChat"`.
- Если GigaChat вернет runtime-ошибку, смотреть HTTP status и body через `TrimError(body)`.
- После успешного ответа убрать лишние `using` в `GigaChatClient.cs` (`System.Text.Unicode`, `OpenAI.Assistants`, если они остались).
- Дальше причесать `GigaChatClient`: сообщения ошибок, маленькие helper methods, отсутствие утечки токена в вывод.

Важное учебное правило:

- Ученик хочет писать код сам. Codex должен объяснять направление и проверять ошибки, но не выдавать готовые методы построчно, если пользователь прямо не попросил.

## Этап 1. Закрепить владение текущим MVP

Цель: уметь объяснить весь текущий проект без подсказок.

Файлы:

- `Program.cs`
- `Models.cs`
- `AppConfig.cs`
- `TfsWorkItemClient.cs`
- `AzureDevOpsGitClient.cs`
- `ChecklistPromptBuilder.cs`

Мини-зачет:

- объяснить запуск `generate-qa-checklist <id>`;
- объяснить запуск `chat`;
- объяснить, где появляется `[code-context]`;
- объяснить, почему данные приводятся к `WorkItemContext`.

Практическая задача для ученика:

- добавить маленькую команду `version`, которая печатает название и версию MVP.

## Этап 2. Понять Microsoft Agent Framework

Цель: понять, чем Agent Framework отличается от обычного HTTP-вызова LLM.

Темы:

- `AIAgent`.
- `RunAsync`.
- instructions.
- chat/session state.
- tools/function calling.
- middleware.
- context providers.
- Agents vs Workflows.
- Ollama provider vs Azure AI Foundry provider.

Практические задачи:

- создать минимального QA agent на Ollama;
- сделать простую session loop;
- добавить первую tool-функцию, которая возвращает тестовые данные User Story;
- заменить тестовую tool-функцию на реальный вызов TFS.

Материал:

- Microsoft Agent Framework overview: https://learn.microsoft.com/en-us/agent-framework/overview/?pivots=programming-language-csharp

## Этап 3. Разобрать пример коллеги

Цель: понять архитектурные идеи из `ChatAiTelegramBot`, не копируя код вслепую.

Статус:

- Проект добавлен локально: `ChatAiTelegramBot-master/ChatAiTelegramBot`.
- Прочитаны ключевые файлы: `Program.cs`, `AgentClient.cs`, `TelegramBot.cs`, `WebServer.cs`, `AutonomousTaskDriver.cs`, `PythonRunner.cs`, `Components/ChatPage.razor`.

Что разобрать:

- как устроен `Program.cs`;
- где конфигурация;
- где создается AI client/agent;
- как обрабатываются входящие сообщения;
- как разделены Telegram, AI и бизнес-логика;
- какие идеи стоит перенести в AiAgentForTFS.

Наблюдения:

- `Program.cs` создает один `AgentClient` и параллельно запускает три драйвера: web server, Telegram bot и autonomous task driver.
- `AgentClient` является центральной точкой создания `AIAgent` под конкретного пользователя.
- Состояние пользователя хранит session, messages, additions/files, selected model, instruction, flags `Tools` и `Skills`.
- Agent Framework используется глубже, чем в текущем AiAgentForTFS: есть `AgentSession`, `RunStreamingAsync`, tools через `AIFunctionFactory.Create(...)`, skills через `AgentSkillsProvider`.
- Telegram и Web UI не знают деталей создания агента, они берут agent через `AgentClient.GetAIAgentForUser(...)`.
- Главная идея для переноса: отделить "канал общения" от "agent runtime". CLI/TFS/Telegram/Web должны быть драйверами, а agent logic — отдельным слоем.

Ссылка:

- https://github.com/manveldavid/ChatAiTelegramBot/tree/master/ChatAiTelegramBot

## Этап 4. Пересобрать архитектуру проекта

Цель: уйти от MVP-структуры к поддерживаемой структуре.

Предварительная целевая структура:

```text
AiAgentForTFS/
  Agents/
  AzureDevOps/
  Configuration/
  Domain/
  Cli/
  Prompts/
```

Правило:

- переносить код маленькими шагами;
- после каждого переноса запускать `dotnet build`;
- не менять поведение без отдельной причины.

Практические задачи:

- вынести models по доменным папкам;
- отделить CLI orchestration от agent logic;
- сделать отдельный `QaChecklistAgent`;
- оставить Azure DevOps API в отдельном слое.

## Этап 5. Сделать настоящего agent с tools

Цель: агент не получает один огромный prompt, а умеет использовать инструменты.

Потенциальные tools:

- `GetUserStoryById`.
- `GetPullRequestContext`.
- `GetChangedFiles`.
- `BuildQaChecklist`.
- `SaveMarkdown`.

Ожидаемый flow:

```text
Пользователь просит чек-лист
  -> agent решает, какие tools нужны
  -> tools получают TFS/PR context
  -> agent формирует результат
  -> результат сохраняется или возвращается пользователю
```

Мини-зачет:

- объяснить разницу между prompt builder и agent tool use.
- объяснить, почему tool должен иметь четкий вход и выход.

## Этап 6. Рабочие сценарии для QA

Цель: агент полезен в ежедневной работе.

Сценарии:

- QA checklist по User Story ID.
- QA checklist по ручному тексту US.
- regression checklist.
- open questions для аналитика/разработчика.
- summary по PR context.
- возможно: публикация результата обратно в TFS.
- возможно: Telegram/чат-интерфейс.

## Этап 7. Качество и надежность

Цель: проект можно давать коллегам.

Темы:

- логирование.
- graceful errors.
- PAT/security.
- лимиты контекста.
- тесты.
- README.
- sample config.
- проверка качества ответов LLM.

Практические задачи:

- добавить unit tests на `ChecklistPromptBuilder`.
- добавить тесты на парсинг PR artifact links.
- добавить fake Azure DevOps client для локальной разработки без TFS.

## Формат занятий

Каждый цикл:

1. Выбираем маленькую цель.
2. Объясняем идею.
3. Ученик пишет код.
4. Запускает build/test.
5. Codex делает ревью.
6. Обновляем этот план: что понято, что требует повторения.

## Ближайший следующий шаг

Начать Этап 2: Microsoft Agent Framework basics.

Первая задача:

- найти в текущем коде, где используется `AIAgent`;
- объяснить, что сейчас делает agent, а что делает обычный C# orchestration;
- после этого написать маленький эксперимент с agent session или tool-функцией.
