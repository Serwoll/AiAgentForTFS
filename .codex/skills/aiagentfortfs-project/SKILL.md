---
name: aiagentfortfs-project
description: Project guidance for AiAgentForTFS, a C#/.NET AI agent that generates QA checklists from Azure DevOps/TFS User Stories using Microsoft Agent Framework and Ollama. Use when working in this repository, explaining the project, planning/refactoring the agent, teaching the user C#, comparing with ChatAiTelegramBot, or evolving from prompt orchestration toward Agent Framework tools/sessions/workflows.
---

# AiAgentForTFS Project Guidance

## Core Goal

Help the user build and understand a production-useful C# AI agent for QA/TFS work. The user wants to write code themselves whenever possible; prefer explanation, small exercises, and review over taking over implementation.

Current product goal:

- Generate QA checklists from Azure DevOps/TFS User Stories.
- Enrich checklist context with linked Pull Request metadata, commit messages, and changed file paths.
- Support multiple LLM providers through `Microsoft.Extensions.AI.IChatClient`.
- Use local Ollama as the original provider and add GigaChat through a custom `GigaChatClient`.
- Evolve from C# prompt orchestration to a real Agent Framework design with sessions and tools.

## Teaching Mode

- The user is learning C# and wants ownership of the code.
- Do not write code unless explicitly asked.
- The user explicitly disliked receiving nearly line-by-line code hints. Prefer explaining types, flow, compiler errors, and next small steps; provide concrete code only when asked or when making an approved edit.
- When teaching, reference exact file/line links where useful.
- Prefer questions that diagnose understanding before explaining.
- Track known weak spots: generics, LINQ, regex, primary constructors, Agent Framework tools/sessions, HTTP request/response DTOs, JSON mapping, `IEnumerable<T>`/collections, and async provider adapters.
- Keep `LEARNING_PLAN.md` updated when milestones, gaps, or direction change.

## Current Architecture

Main files:

- `Program.cs`: CLI dispatch, chat mode, orchestration, Ollama agent call, output saving.
- `AppConfig.cs`: appsettings/env var loading and validation.
- `Models.cs`: internal domain records (`WorkItemContext`, `CodeContextSource`, links, manual input).
- `TfsWorkItemClient.cs`: Azure DevOps/TFS Work Item REST client.
- `AzureDevOpsGitClient.cs`: PR artifact parsing and PR/commit/changed-file enrichment.
- `ChecklistPromptBuilder.cs`: prompt construction and compact result audit.
- `QaChecklistWorkflow.cs`: deterministic checklist workflow that calls an `IChatClient`.
- `GigaChatClient.cs`: custom `IChatClient` adapter for GigaChat REST API.
- `LEARNING_PLAN.md`: living learning/development roadmap.

Important distinction:

- Current MVP: C# orchestration gathers context, builds prompt, calls `AIAgent.RunAsync`.
- Target design: Agent Framework agent receives tools and decides when to call them.

Current GigaChat checkpoint as of 2026-07-03:

- `QaChecklistWorkflow` is provider-agnostic through `IChatClient`.
- `Program.cs` has `CreateChatClient(...)` and can choose Ollama or GigaChat via config.
- `AppConfig` has `Llm.Provider` and GigaChat settings.
- `GigaChatClient` implements `IChatClient`.
- OAuth/token flow is implemented with `Basic` auth key, `RqUID`, `scope`, JSON parsing, and cached access token.
- GigaChat TLS required installing trusted certificates on the work machine.
- `/chat/completions` DTOs and `ChatMessage` -> `GigaChatMessage` mapping are in progress/mostly implemented.
- Next expected step: run real generation with `Llm.Provider = "GigaChat"` and debug runtime API response if needed.
- Do not print access tokens or secrets; use safe diagnostic messages.

## Microsoft Agent Framework Focus

Keep steering toward these concepts:

- `AIAgent`: agent object created from model provider.
- `instructions`: stable agent role/behavior.
- `AgentSession`: conversational state.
- `RunAsync` vs `RunStreamingAsync`.
- tools via `AIFunctionFactory.Create(...)`.
- skills/context providers where relevant.
- Agents vs Workflows: use agents for flexible conversation/tool choice; workflows for deterministic multi-step processes.

Useful docs:

- https://learn.microsoft.com/en-us/agent-framework/overview/?pivots=programming-language-csharp

## ChatAiTelegramBot Comparison

Local reference project: `ChatAiTelegramBot-master/ChatAiTelegramBot`.

Key ideas seen there:

- One shared `AgentClient`.
- Multiple channels/drivers: Web, Telegram, autonomous file task runner.
- Per-user `AgentSession`, messages, model, instructions, tools/skills flags.
- Tools exposed via `AIFunctionFactory.Create(PythonRunner...)`.
- UI/channel code asks `AgentClient.GetAIAgentForUser(...)` for an agent instead of constructing agent details itself.

Architecture lesson to carry into AiAgentForTFS:

- Separate channels (`CLI`, later Telegram/Web) from agent runtime.
- Move toward `QaAgentRuntime`/`QaAgentClient`.
- Expose TFS/PR operations as agent tools instead of doing all orchestration before calling the LLM.

## Git Hygiene

Do not commit:

- local `appsettings.json` with PATs;
- generated `output/`;
- build artifacts `bin/` and `obj/`;
- downloaded comparison repo `ChatAiTelegramBot-master/` unless intentionally vendored.

## Next Direction

Near-term learning/development sequence:

1. Understand current MVP boundaries.
2. Study Agent Framework basics using current `AIAgent` call.
3. Compare with `ChatAiTelegramBot` `AgentClient`.
4. Refactor toward an agent runtime layer.
5. Convert TFS/PR operations into tools.
6. Add sessions/streaming only when the user understands the basic tool flow.
