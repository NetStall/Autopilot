# ADR 0013 — LLM abstraction: ILlmClient and task routing

Status: accepted (2026-10-20, step 06a)

## Context

Step 06 called Claude directly from `ClaudeLetterGenerator`. Step 07 is meant to add a local model (Ollama) for some
tasks. The letter code (prompt, parsing, cleaning) should not change when the provider does.

## Decision

- **Core** gets `ILlmClient`, `ILlmRouter`, `LlmRequest`, `LlmResponse` and `LlmException` (`Core/Llm`):
  - a request is a system message, a user message and a JSON schema;
  - the response is the JSON, the token usage and the model;
  - errors are German and safe to show.
- **Infrastructure:**
  - `ClaudeLlmClient` is the only class that talks to the Anthropic SDK (ADR 0010: official SDK, retries, timeout,
    no key in logs).
  - `LlmRouter` picks the provider and model per task from `Llm:Tasks`, e.g. `"Letter": "claude:claude-sonnet-5-5"`.
    A task without an entry, or with an unknown provider, is a configuration error, and no call is made (07a:
    `Claude:Model` removed; `Claude:*` keeps only key, limits and prices).
- `LetterGenerator` (`Infra/Documents`) builds the prompt (`letter.v1`), calls the router for the task `Letter` and
  parses the result; it maps `LlmException` to `LetterGenerationException`.
- **Unchanged:**
  - the API key comes only from user-secrets or environment variables, under `Claude:ApiKey`;
  - `Claude:DailyCallLimit` and the token budget are still checked by `ApplicationDocumentsService` before every
    call.
- The fact guard now also knows the technology catalog (ADR 0012). A technology named in one of my experience,
  achievement or project facts counts as a fact.

## Consequences

- A second provider needs an `ILlmClient` and one line in `Llm:Tasks`, without touching the letter code.
- The model is configured in one place, `Llm:Tasks`.
- Endpoints keep their step-06 names: `/api/applications/{id}/documents`, `…/documents/paragraphs/{i}/rephrase`,
  `PUT …/documents`, `GET …/documents`. These are the `/draft` endpoints of the 06a prompt.
