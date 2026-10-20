# ADR 0010 — Application documents: Claude SDK, model, QuestPDF

Status: accepted (2026-10-20, step 06)

## Context

Step 06 drafts the cover letter with the Claude API, builds the CV variant without AI, lets me review and approve
both, and renders PDFs that look like my current documents. Nothing is sent by the tool: I submit applications
myself (portal, form, e-mail) and only record that with „Als gesendet markieren".

## Decision 1 — Claude access: the official C# SDK

- **Package:** NuGet `Anthropic`, the official Anthropic SDK for C#.
  - Listed on platform.claude.com/docs → *SDKs*.
  - Repository: github.com/anthropics/anthropic-sdk-csharp.
  - Pinned to **12.53.0**, released 2026-09-30. The newest at the time was 12.58.0 from 2026-10-15; I prefer
    releases that are at least two weeks old.
  - Since version 10 the package name belongs to Anthropic; ≤ 3.x was a community SDK, now `tryAGI.Anthropic`.
- **Why the SDK and not our own typed client:**
  - It is maintained and typed.
  - It covers structured outputs (`OutputConfig.Format` with a JSON schema), `Effort` and token usage.
  - It brings its own retries with backoff (429, 5xx, connection errors, `Retry-After` honoured) and per-attempt
    timeouts.
  - It accepts our own `HttpClient`, so the client comes from `IHttpClientFactory` (AGENTS.md).
- **How it is wired:**
  - The SDK's retries are configured by `Claude:MaxRetries` and `Claude:TimeoutSeconds`.
  - The typed `HttpClient` has no resilience handler of its own (no double retries) and no own timeout.
  - Tests replace the `HttpMessageHandler`, so no test ever reaches the real API.
- **Structured output:** `output_config.format` with a strict JSON schema.
  - Every object is `additionalProperties: false`, every field is required.
  - Letter fields: `subject`, `salutation`, `paragraphs[]`, `closing`, `language`, `formality`, `factsUsed[]`,
    `gapsMentioned[]`.
  - GA on the Claude API, no beta header.
- **Model:** `claude-sonnet-5-5` (configurable, `Claude:Model`).
  - Reasons: fast (the target is under a minute per application), strong writing in German, and half the price of
    Opus 5.5 ($2 / $10 per million input/output tokens versus $4 / $20).
  - Opus 5.5 is a config change away if the letters are not good enough.
- **Temperature:** current models (Sonnet 5.5, Opus 5.5) reject any value other than the default with 400, and the
  SDK marks the property obsolete. `Claude:Temperature` therefore stays empty and is only sent when set, for older
  models. `Claude:Effort` (default `medium`) controls the thinking depth instead.
- **Prompt:** versioned template `Prompts/letter.v1.md`, an embedded resource.
  - The version is stored with every draft.
  - Changing the prompt means a new file (`letter.v2.md`); a released version is never edited in place.

## Decision 2 — Facts, not free text

- **Input:** the model gets numbered facts from `profile.json` with stable ids (`exp0.b2`, `skill:csharp`,
  `gap:kubernetes`):
  - the matching experience bullets;
  - skills with their level;
  - known gaps with my honest phrasing;
  - languages and links.

  It also gets the fit check (covered / partial / gaps) and the posting. Salary and earliest start from the
  Suchprofil are included only if the posting asks for them.
- **Language and form of address** are detected from the posting by rules (German / English, „du" / „Sie", the
  contact person), not by the model.
- **The posting is untrusted.** It sits in a `<vacancy>` block with `<` and `>` replaced, so it cannot close the
  block or inject other blocks. The system prompt says to ignore instructions inside it. The answer is data: plain
  text, escaped when rendered, never HTML.
- **Fact guard** (rules, after every generation and save) warns about:
  - technologies presented as experience that are not in the profile (or only Learning/None);
  - numbers and durations that appear neither in the profile nor in the posting;
  - required gaps that the letter does not mention;
  - „Sie" and „du" mixed;
  - the wrong language;
  - more than one page (measured on the rendered PDF).

  Style hints: website missing from the first paragraph, clichés. Approving with open warnings needs an explicit
  confirmation, which is recorded in the application history.

## Decision 3 — PDFs with QuestPDF, Community license

- **License:** QuestPDF 2026.9.1 (NuGet) under the **Community License**, checked on questpdf.com/license on
  2026-10-20.
  - It is free for individuals and for organisations with less than USD 1 M annual gross revenue, commercial use
    included. My personal job-search tool qualifies.
  - No attribution is required.
  - The license forbids redistributing QuestPDF's *source*. We only reference the NuGet package, so the public or
    private repository contains no QuestPDF source.
  - Set in code: `QuestPDF.Settings.License = LicenseType.Community`.
- **Look:** the same as my current Lebenslauf and Anschreiben.
  - Font: Inter. It is embedded in the assembly (SIL Open Font License 1.1, see `Documents/Fonts/Inter-OFL.txt`).
    System fonts are switched off, so the output is identical on Windows and in Docker.
  - Colours: text #1D2329, accent #2B5F5A for the role line and section labels, grey #4A535C for the contact line.
  - Layout: a rule under the header; the CV has its section labels in a left column.
  - My Word letter uses Calibri, which cannot be redistributed; the letter uses Inter too, so letter and CV look
    like one set.
- **Preview:** the API sends `X-Frame-Options: DENY`, so a PDF cannot be framed from the API origin. The preview is
  therefore a PNG of page 1, rendered by QuestPDF and shown with `URL.createObjectURL`. The page count travels in
  `X-Page-Count`. The frontend CSP allows `img-src blob:`; the API exposes the header to the frontend origin via
  CORS.

## Decision 4 — CV variant without AI

- **Deterministic:**
  - the summary by the posting's focus (`fullstack` / `backend` / `ai`);
  - a role line from a few templates plus my strongest matching skills;
  - bullets ordered by tag overlap;
  - at most 8 bullets for the current job and 3 for older ones;
  - skill groups by `cvGroup`.
- **One page:** the service renders the CV and, while it is longer than one page, hides the weakest automatically
  shown bullet.
- **German texts:** they come from optional `*De` fields in `profile.json` (`fullstack.de`, `titleDe`, `textDe`,
  `degreeDe`, `notesDe`, …), which I fill in myself. Missing German texts fall back to the base text and are
  counted in a hint. This is a schema extension of `profile.json`; it was decided with me in this step.

## Consequences

- **Data flow:** personal data now leaves the machine — the selected profile facts and the posting go to the Claude
  API for each generation or rephrasing. Nothing else is sent: no e-mail, no full profile file, no other
  applications.
  - Logs contain ids, model, prompt version, token counts, duration and status codes only.
  - The API key comes only from user-secrets or the environment.
- **Cost control:** `Claude:DailyCallLimit` (30 calls per day, German time) and `Claude:DraftTokenBudget`
  (120 000 tokens per draft). Each draft shows its tokens and the estimated cost (`Claude:InputPricePerMTok` /
  `OutputPricePerMTok`).
- **„Gesendet" is only a record:**
  - It can only be reached through „Als gesendet markieren" after „Freigeben".
  - The generic status change can no longer set Entwurf, Freigegeben or Gesendet.
  - Any change after approval sets the application back to Entwurf.
- **Roadmap:** old step 07 (review) is merged into 06. Step 08 (sending) becomes optional / later.
