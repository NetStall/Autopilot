# ADR 0014 — Local model (Ollama) for requirement extraction, gold labels

Status: accepted (2026-10-21, step 07a)

## Context

The rules (ADR 0011, 0012) read postings through headings, markers and a catalog. They miss what is phrased freely.
A local model on my PC (RTX 3080 Ti, 12 GB VRAM) can read the whole posting, costs nothing per call and keeps
personal data and postings on the machine. The app must keep working without it.

## Decision

- **Provider:** `OllamaLlmClient` implements `ILlmClient` (ADR 0013).
  - It uses Ollama's OpenAI-compatible `/v1/chat/completions` with a JSON schema as `response_format`.
  - It is a typed `HttpClient` with no new SDK, retries on 429/5xx/connection errors, and one timeout per attempt.
  - It logs tokens/s.
  - It is off unless `Llm:Providers:Ollama:Enabled`. From Docker the host is reached as `host.docker.internal`.
  - `GET /api/llm/status` shows whether Ollama is reachable, which models are pulled and which tasks are ready.
- **Models:** the task mapping `Llm:Tasks:Extraction = "ollama:qwen3:8b"` is the only place the model is set.
  - `qwen3:8b` (q4_K_M, 5.2 GB) leaves VRAM for a 16K context (`OLLAMA_CONTEXT_LENGTH`).
  - `qwen3:14b` (q4_K_M, 9.3 GB) is the comparison model.
  - The newer `qwen3.5:9b` (6.6 GB, a thinking model) is a candidate for step 07b.
- **Extraction** (`VacancyExtractionService`, prompt `extraction.v1.md`):
  - The validated JSON holds must-have, nice-to-have, German and English level, years, seniority, remote level,
    home-office share, employment type, required domains and a two-sentence summary.
  - The posting is untrusted. It is neutralised, delimited, and the model is told to ignore instructions in it.
  - An invalid answer is sent back once with the validation errors. When the second answer fails too, the failure is
    cached and the rules stay in charge.
- **Cache:** `VacancyExtractions` is keyed by description hash + model + prompt version (unique). The same text is
  never sent twice, even when it comes from two sources.
  - The current result is copied to `Vacancies.ExtractionJson` (with `ExtractionKey`) for scoring without a join.
  - It is ignored as soon as the description changes.
- **Background:** `ExtractionWorker` runs one GPU job at a time.
  - Single vacancies („Mit KI analysieren") go first, then the pending ones, most relevant first, at most
    `Llm:Extraction:MaxPerRun`.
  - It runs after every import (`AutoAfterImport`) and on request. Progress and cancel are in `GET/POST/DELETE
    /api/extractions`.
  - A provider that is down stops the run with its error and changes nothing.
- **Fit check:** with an extraction, `FitAnalyzer` takes requirements, German and years from it
  (`ExtractionNormalizer` maps names to my skills and the catalog; alternatives cost nothing). A required domain I
  don't have is a must-have gap. The UI shows the source („KI" / „Regeln").
- **Gold labels:** `GoldLabels` (one per vacancy, cascade delete) store „Passt" / „Passt nicht" with a timestamp.
  - On the detail page they are set with buttons or `p` / `n`.
  - The header shows „Bewertet: N / 30".
  - `scripts/seed-gold-labels.ps1` sets the eight labels given in chat.

## Revision 07a2 — extraction quality

Meierhofer (my label „Passt") fell to rank 45 with `qwen3:8b` + `extraction.v1`:
- almost everything was a must-have, partly in both lists;
- the company's industry („Healthcare") became a required domain;
- „Senior, 5 Jahre" was inferred.

The changes:
- **`extraction.v2`** is the default (`Llm:Extraction:PromptVersion`), and v1 stays embedded. The rules: must-have
  only when marked as required, one list per item, only concrete items, domains only with required experience,
  years only from a stated number.
- **`ExtractionGuards`** check every answer against the text, so a model that over-reads cannot undo the rules:
  - an item in both lists stays a must-have only with a requirements section or marker;
  - a domain stays only when a sentence naming it asks for experience;
  - `minYears` stays only when a number of years is stated.
- **`ExtractionNormalizer`** maps free wording to my testing and CI skills (generic aliases included) and drops
  generic practices.
- **Methods:** missing DDD/CQRS/Event Sourcing/TDD/BDD cost like a plus, together at most `Fit:MethodsGapMax` (6),
  unless their line marks them as required.
- With an extraction, years come only from `minYears` — never from „Senior".
- **Thinking off** for the extraction (`reasoning_effort: none`, `Llm:Extraction:Thinking`).
- **Himalayas „Remote (…)"** is FullRemote; „hybrid setup" in the perks no longer downgrades it, only „vor Ort" does.
- Real profile:
  - Meierhofer 45 → 65;
  - Apaleo 63 → 29 (5 years stated; Kafka, AWS, Kubernetes required).

## Consequences

- Without Ollama nothing changes: no calls, no queue, rules everywhere (tested with a fake client).
- The extraction does not hold the import lock, so a backfill of hours never blocks an import. A vacancy updated by
  both at the same moment keeps the last write; the next re-score fixes the rank.
- The integration tests now use Testcontainers. The portable test PostgreSQL in Windows Temp lost its files.
