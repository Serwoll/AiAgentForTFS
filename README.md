# AiAgentForTFS

Консольный AI-агент для генерации QA чек-листов по User Story из Azure DevOps/TFS.

Агент умеет обогащать чек-лист контекстом из связанных Pull Request: названием PR, ветками, commit messages и списком изменённых файлов. Пункты чек-листа, основанные на таком контексте, помечаются маркером `[code-context]`.

## Требования

- .NET 9 SDK
- Запущенная локальная Ollama
- Установленная модель Ollama, например `qwen3:8b`
- PAT для Azure DevOps/TFS с правами на чтение Work Items
- Права на чтение кода, если нужен PR/commit/file context

## Настройка

Скопируйте пример настроек:

```powershell
Copy-Item .\AiAgentForTFS\appsettings.example.json .\AiAgentForTFS\appsettings.json
```

Заполните `AiAgentForTFS/appsettings.json`:

```json
{
  "AzureDevOps": {
    "BaseUrl": "https://dev.azure.com/your-organization/your-project",
    "Pat": "put-your-pat-here",
    "ApiVersion": "7.0"
  },
  "Ollama": {
    "Url": "http://localhost:11434",
    "Model": "qwen3:8b"
  }
}
```

Для локального TFS/Azure DevOps Server `BaseUrl` обычно выглядит так:

```text
https://tfs.company.local/tfs/Collection/Project
```

`appsettings.json` не коммитится в git, потому что может содержать PAT.

## Запуск

Сгенерировать чек-лист по ID User Story:

```powershell
dotnet run --project .\AiAgentForTFS\AiAgentForTFS.csproj -- generate-qa-checklist 12345
```

Интерактивный режим:

```powershell
dotnet run --project .\AiAgentForTFS\AiAgentForTFS.csproj -- chat
```

В режиме `chat`:

1. Вставьте текст User Story.
2. Напишите `done` на отдельной строке.
3. Вставьте optional PR links, commit links, заметки по коду или ссылки вида `vstfs:///Git/PullRequestId/...`.
4. Снова напишите `done`.

Markdown выводится в консоль и сохраняется в папку `output/`.

## Что смотреть в результате

- `[code-context]` означает, что пункт чек-листа основан на PR/commit/changed-file context.
- В конце файла есть компактная секция `Context used by agent`.
- Там видно, был ли загружен кодовый контекст, сколько commit messages и changed files попало в prompt.
- Там же пишется `LLM response time`: время ответа модели.
- Длинные списки файлов в финальный Markdown не выводятся, чтобы не раздувать файл.
- Если PR enrichment не сработал, чек-лист всё равно генерируется, а предупреждение пишется в финальной audit-секции.
