# Code map — where is what

Paths are relative to the repo root. `Core/` = `backend/src/JobAutopilot.Core/`, `Infra/` =
`backend/src/JobAutopilot.Infrastructure/`, `Api/` = `backend/src/JobAutopilot.Api/`, `Tests/` =
`backend/tests/JobAutopilot.Tests/`, `fe/` = `frontend/`. Config = section in `Api/appsettings.json`.
Overview and flows: [ARCHITECTURE.md](ARCHITECTURE.md).

## Import and sources

| Feature | Files | Key symbols | API | Tests |
|---|---|---|---|---|
| Start/queue import | `Api/Features/VacancyImports/ImportEndpoints.cs`, `ImportJobChannel.cs` | `ImportCoordinator`, `ImportJobChannel` | `POST /api/imports`, `GET /api/imports`, `GET /api/imports/{id}`, `GET /api/imports/searches` | `Integration/ImportEndpointsTests.cs` |
| Run import | `Api/Features/VacancyImports/ImportWorker.cs`, `Core/Import/VacancyImportService.cs`, `ImportRun.cs`, `ImportResult.cs` | `ImportWorker`, `VacancyImportService.ImportAsync` | — | `Integration/ImportWorkerTests.cs`, `Unit/Import/VacancyImportServiceTests.cs`, `MultiSourceImportTests.cs`, `ImportRunTests.cs` |
| Source contract | `Core/Import/IVacancySource.cs`, `VacancyCandidate.cs`, `VacancySearch.cs`, `VacancySearchRules.cs` | `IVacancySource.SearchAsync`, `VacancySearchRules.Validate` | — | `Unit/Import/VacancySearchRulesTests.cs`, `Fakes/FakeVacancySource.cs` |
| Arbeitsagentur | `Infra/Arbeitsagentur/ArbeitsagenturClient.cs`, `ArbeitsagenturMapper.cs`, `JobSearchResponse.cs` | `ArbeitsagenturClient`, `ArbeitsagenturMapper` | — | `Unit/Arbeitsagentur/*` |
| Himalayas | `Infra/Himalayas/HimalayasClient.cs`, `HimalayasMapper.cs`, `HtmlText.cs` | `HimalayasClient`, `HtmlText` | — | `Unit/Himalayas/*` |
| Title pre-filter, duplicates | `Core/Import/TitleExclusionFilter.cs`, `Core/Vacancies/DuplicateKeys.cs` | `TitleExclusionFilter`, `DuplicateKeys` | — | `Unit/Import/TitleExclusionFilterTests.cs`, `Unit/Himalayas/HtmlTextAndKeysTests.cs` |
| Storage | `Core/Import/IVacancyStore.cs`, `Infra/Persistence/EfVacancyStore.cs` | `IVacancyStore`, `EfVacancyStore` | — | `Fakes/InMemoryVacancyStore.cs` |

Config: `Arbeitsagentur` (BaseUrl, ApiKey, PageSize, MaxPages, DelayBetweenPagesMs, RemoteHomeOfficeFilter),
`Himalayas` (BaseUrl, Country, ExcludeWorldwide, MaxPages), `Import` (Searches, ExcludeTitleTerms),
`RateLimiting:ImportsPerMinute`. Options classes: `ArbeitsagenturOptions`, `HimalayasOptions`, `Api/Features/VacancyImports/ImportOptions.cs`.

## Details fetch

| Feature | Files | Key symbols | API | Tests |
|---|---|---|---|---|
| Descriptions of new/changed vacancies (most relevant first; a pasted one is kept) | `Core/Import/VacancyDetailsService.cs`, `IVacancyDetailsSource.cs`, `Infra/Persistence/EfVacancyStore.cs` (`FindWithoutDetailsAsync`); called from `ImportWorker` | `VacancyDetailsService.FetchAsync` | — |
| „Beschreibung einfügen" | `Api/Features/Vacancies/VacancyEndpoints.cs` (`SetDescription`), `fe/app/components/DescriptionPaste.vue` | `Vacancy.DescriptionIsManual` | `PUT /api/vacancies/{id}/description` | `Integration/VacancyEndpointsTests.cs`, `fe/test/nuxt/descriptionPaste.test.ts` | `Unit/Import/VacancyDetailsServiceTests.cs`, `Unit/Arbeitsagentur/ArbeitsagenturDetailsTests.cs`, `Fakes/FakeDetailsSource.cs` |

Config: `Import:Details` (Enabled, MaxPerRun, DelayBetweenRequestsMs, SaveEvery).

## Scoring and relevance

| Feature | Files | Key symbols | API | Tests |
|---|---|---|---|---|
| Relevance score | `Core/Scoring/RelevanceScorer.cs`, `RelevanceOptions.cs`, `RelevanceScorerProvider.cs`, `TermMatcher.cs` | `RelevanceScorer.Score`, `ProfileRelevanceScorerProvider`, `TermMatcher.FindFirstMatch` | — | `Unit/Scoring/RelevanceScorerTests.cs`, `TermMatcherTests.cs`, `Integration/RelevanceApiTests.cs` |
| Enrich one vacancy | `Core/Scoring/VacancyEnricher.cs` | `VacancyEnricher.Apply` (relevance, remote, employer, German, rank, unreachable) | — | via the tests above |
| Keywords gated by profile | `Core/Scoring/RelevanceScorerProvider.cs` | `ProfileRelevanceScorerProvider.ForProfile` | — | `Unit/Scoring/ProfileGatedKeywordsTests.cs` |
| Re-score all | `Core/Scoring/VacancyRescoreService.cs`, `Api/Features/Scoring/RescoreCoordinator.cs`, `RescoreWorker.cs` | `RescoreAllAsync`, `RescoreCoordinator.Request` | `POST /api/vacancies/rescore`, `GET /api/vacancies/rescore` | `Integration/RelevanceApiTests.cs` |
| Ranking regression | `Tests/Unit/Scoring/RankingRegressionTests.cs`, `RealWorldFixtures.cs`, `Tests/Fixtures/vacancies/*.txt`, `Tests/Fixtures/profiles/regression-profile.json` | rank ranges per posting | — | itself |
| Shipped config check | `Tests/Unit/Scoring/ShippedConfig.cs` | loads real `appsettings.json` | — | — |

Config: `Relevance` (BaseScore, TitleFactor, SoftenedFactor, RelevantFrom, MaybeFrom, RescoreOnStartup, GroupCaps,
NiceToHaveFactor, LearningKeywordFactor, BasicSkills, BasicsGroup, ProfileSkills, Positive, Negative). Validator: `Infra/RelevanceOptionsValidator.cs`.

## Fit check („Passt zu mir?")

| Feature | Files | Key symbols | API | Tests |
|---|---|---|---|---|
| Fit analysis + penalties | `Core/Fit/FitAnalyzer.cs`, `FitOptions.cs`, `FitSettings.cs`, `VacancyFitService.cs` | `FitAnalyzer.Analyze`, `FitResult`, `FitPenalties` | `GET /api/vacancies/{id}/fit` | `Unit/Fit/FitAnalyzerTests.cs`, `Integration/ProfileAndFitApiTests.cs` |
| Must-have / stack / nice-to-have | `Core/Fit/RequirementSections.cs` | `RequirementSections.Parser`, `RequirementImportance`, `SectionKind` | — | `Unit/Fit/RequirementImportanceTests.cs`, `Unit/Fit/TechnologyCatalogTests.cs` |
| Technology catalog | `Core/Fit/technologies.json` (embedded), `Core/Fit/TechnologyCatalog.cs` | `TechnologyCatalog.All`, `Contains`, `Technology.Parent` | — | `Unit/Fit/TechnologyCatalogTests.cs` |
| Reachability | `Core/Scoring/HomeOfficeShare.cs`, `FitAnalyzer` (`FitResult.Unreachable`), `Vacancy.Unreachable`, `Vacancy.HomeOfficePercent` (`ArbeitsagenturMapper.MapPercent`) | `HomeOfficeShare.Find` | `GET /api/vacancies?includeUnreachable=true` | `Unit/Fit/TechnologyCatalogTests.cs`, `Unit/Fit/FitAnalyzerTests.cs`, `Unit/Arbeitsagentur/ArbeitsagenturDetailsTests.cs` |
| Profile skills | `Core/Profile/CandidateProfile.cs`, `SkillTerms.cs`, `ProfileValidator.cs`, `Infra/Profile/FileCandidateProfileSource.cs` | `SkillTerms.Evidence`, `ProfileValidator` | `GET /api/profile` (`Api/Features/Profile/ProfileEndpoints.cs`) | `Unit/Profile/ProfileTests.cs` |

Config: `Fit` (CommonRequirements, OptionalMarkers, Penalties; heading/marker lists, `UseTechnologyCatalog`,
`ReachableFromHomeOfficePercent` default in `FitOptions`), `Profile:Path`
(default `data/profile.json`; example `docs/profile.example.json`).

## Remote, agency and German detection

| Feature | Files | Key symbols | Tests |
|---|---|---|---|
| Remote level | `Core/Scoring/RemoteClassifier.cs` (`PartialPhrases`), `Core/Scoring/RemoteAssessment.cs` (field vs flag vs text vs extraction), `Core/Vacancies/RemoteLevel.cs` | `RemoteClassifier.Classify`, `FindPartial`, `RemoteAssessor.Assess` | `Unit/Scoring/RemoteClassifierTests.cs`, `Unit/Scoring/RankingRegressionTests.cs` |
| Employer type | `Core/Scoring/EmployerClassifier.cs`, `Core/Vacancies/EmployerType.cs` | `EmployerClassifier.Classify(…, sourceName)` | `Unit/Scoring/EmployerClassifierTests.cs`, `HimalayasEmployerTests.cs` |
| Required German | `Core/Fit/GermanRequirementDetector.cs`, `Core/Vacancies/RequiredGerman.cs` | `GermanRequirementDetector.Detect` | `Unit/Fit/GermanRequirementTests.cs`, `Integration/GermanFilterApiTests.cs` |
| German statistics | `Api/Features/Statistics/StatisticsEndpoints.cs` | — | `GET /api/statistics/german`; `Integration/GermanFilterApiTests.cs` |

Config: `Remote` (FullRemotePhrases, OnSitePhrases, HybridPhrases, FullRemoteTitleTerms), `EmployerDetection`
(KnownAgencies, TemporaryPhrases, AgencyPhrases, DirectPhrases). German phrases are in code.

## Vacancy list and detail

| Feature | Files | Key symbols | API | Tests |
|---|---|---|---|---|
| List, filter, sort, facets | `Api/Features/Vacancies/VacancyEndpoints.cs`, `VacancyListQuery.cs`, `VacancyPageDto.cs`, `Core/Vacancies/VacancyFilter.cs`, `Infra/Persistence/VacancyQueryExtensions.cs` | `ApplyFilter`, `ApplySort`, `Filtered` | `GET /api/vacancies` | `Integration/VacancyQueryTests.cs`, `VacancyEndpointsTests.cs`, `Unit/VacancyQueryExtensionsTests.cs` |
| Detail, edit, manual add | `VacancyEndpoints.cs`, `VacancyDto.cs`, `VacancyMappings.cs`, `CreateVacancyRequest.cs` | `VacancyMappings` | `GET/PATCH /api/vacancies/{id}`, `POST /api/vacancies` | `Unit/VacancyMappingsTests.cs`, `Unit/CreateVacancyRequestValidationTests.cs` |
| Seen / interest | `SeenRequests.cs`, `Core/Vacancies/VacancyInterest.cs` | — | `POST /api/vacancies/seen` | `Integration/VacancySeenTests.cs`, `InterestTests.cs` |
| UI settings | `Api/Features/Settings/UiSettingsEndpoints.cs` | `UiOptions` | `GET /api/settings/ui` | — |

Config: `Ui` (ScoreHigh, ScoreMedium, HighlightKeywords).

## Suchprofil

| Feature | Files | Key symbols | API | Tests |
|---|---|---|---|---|
| Model, validation, summary, searches | `Core/SearchProfiles/SearchProfile.cs`, `SearchProfileRules.cs` | `SearchProfileRules.Validate`, `GenerateSearches`, `SearchPenalties` | — | `Unit/SearchProfiles/SearchProfileRulesTests.cs` |
| Seed + defaults | `Core/SearchProfiles/SearchProfileSeed.cs`, `Api/Features/SearchProfiles/SearchProfileEndpoints.cs` | `SearchProfileDefaults`, `SearchProfileInitializer`, `ISearchProfileSource`, `SearchProfileImports.Current` | — | `Integration/SearchProfileApiTests.cs` |
| Store, history, rollback | `Infra/SearchProfiles/SearchProfileStore.cs` | `SearchProfileStore` | `GET/PUT /api/search-profile`, `POST …/preview`, `GET …/history`, `POST …/rollback`, `GET …/export` | `Integration/SearchProfileApiTests.cs`, `BroaderSearchApiTests.cs`, `Unit/Import/BroaderImportTests.cs` |

Config: `SearchProfileDefaults` (MinScore, TopN, MustHaveFromWeight, RemotePreferencePenalty).

## Applications, documents and tracker import

| Feature | Files | Key symbols | API | Tests |
|---|---|---|---|---|
| Applications + status | `Core/Applications/Application.cs`, `ApplicationStatuses.cs`, `ApplicationWorkflow.cs`, `Api/Features/Applications/ApplicationEndpoints.cs`, `ApplicationDtos.cs` | `ApplicationWorkflow.Approve`, `MarkSent` | `/api/applications` (GET, GET/PATCH/DELETE `{id}`, POST, POST `{id}/status`), `POST /api/vacancies/{id}/apply` | `Integration/ApplicationApiTests.cs`, `Unit/Applications/ApplicationTests.cs`, `Unit/Documents/ApplicationWorkflowTests.cs` |
| Tracker import | `Core/Applications/TrackerImport.cs`, `ApplicationMatcher.cs`, `Infra/Applications/TrackerImporter.cs` | `TrackerImporter.ImportFileAsync` | `POST /api/applications/import-tracker` | `Unit/Applications/TrackerMapperTests.cs` |
| Documents (letter, CV, PDF) | `Core/Documents/*` (`ApplicationDraft`, `CvBuilder`, `FactGuard`, `LetterFacts`, `CheatSheet`, `ToneDetector`, `VacancyTerms`), `Infra/Documents/ApplicationDocumentsService.cs`, `QuestPdfDocumentRenderer.cs`, `DocumentFileStore.cs`, `Api/Features/Documents/*` | `ApplicationDocumentsService.ApproveAsync`, `MarkSentAsync`, `FactGuard` | `POST /api/vacancies/{id}/documents`, `/api/applications/{id}/documents` (GET, PUT, POST, `regenerate`, `paragraphs/{i}/rephrase`, `restore`, `preview/{doc}`, `approve`, `{doc}.pdf`), `POST …/{id}/mark-sent` | `Integration/DocumentsApiTests.cs`, `Unit/Documents/*` |
| Letter generation | `Infra/Documents/LetterGenerator.cs`, `Infra/Claude/LetterPrompt.cs`, `Infra/Claude/Prompts/letter.v1.md`, `Core/Documents/ILetterGenerator.cs` | `GenerateAsync`, `RephraseParagraphAsync` | — | `Unit/Documents/LetterGeneratorTests.cs`, `LetterPromptTests.cs`, `Fakes/FakeLetterGenerator.cs` |
| LLM abstraction | `Core/Llm/ILlmClient.cs`, `Infra/Llm/LlmRouter.cs` (`LlmOptions`), `Infra/Claude/ClaudeLlmClient.cs`, `ClaudeOptions.cs` | `ILlmClient.CompleteJsonAsync`, `ILlmRouter.ModelFor`, `IsConfigured` | — | `Unit/Documents/LetterGeneratorTests.cs` |
| Script | `scripts/draft-top-vacancies.ps1` | real Claude calls, console output only | — | — |

Config: `Applications:TrackerImportPath`, `Documents:OutputPath`, `Llm:Tasks` (`Letter = claude:<model>`), `Claude` (Model, MaxTokens, Effort, TimeoutSeconds,
MaxRetries, DailyCallLimit, DraftTokenBudget, prices, MaxVacancyCharacters; ApiKey only via user-secrets/env).

## Local model, requirement extraction, gold labels (07a)

| Feature | Files | Key symbols | API | Tests |
|---|---|---|---|---|
| Ollama provider + status | `Infra/Llm/OllamaLlmClient.cs` (`OllamaOptions`), `Api/Features/Llm/LlmEndpoints.cs` | `CompleteJsonAsync`, `GetStatusAsync` | `GET /api/llm/status` | `Unit/Llm/OllamaLlmClientTests.cs`, `Integration/HealthEndpointTests.cs` |
| Extraction model + validation | `Core/Extraction/RequirementExtraction.cs` (`StoredExtraction`, `ExtractionKey`, `ExtractionValidator`), `ExtractionGuards.cs` (text has the last word), `ExtractionNormalizer.cs` (skills, catalog, testing/CI concepts, methods), `VacancyExtraction.cs` | `ExtractionValidator.Parse`, `ExtractionGuards.Apply`, `ExtractionNormalizer.Resolve` | — | `Unit/Llm/ExtractionTests.cs`, `ExtractionQualityTests.cs` |
| Extraction service + prompts | `Infra/Llm/VacancyExtractionService.cs`, `ExtractionPrompt.cs` (`For(version)`), `Infra/Llm/Prompts/extraction.v1.md`, `extraction.v2.md` (default) | `ExtractAsync`, `PendingAsync` | — | `Integration/ExtractionApiTests.cs` |
| Extraction regression | `Tests/Unit/Scoring/RankingRegressionTests.cs` (`RealWorldFixtures.ScoredWithExtraction`), `Tests/Fixtures/extractions/*.json` | — | — | itself |
| Background run | `Api/Features/Extraction/ExtractionWorker.cs`, `ExtractionCoordinator.cs`; trigger in `ImportWorker` | `RequestBackfill`, `RequestVacancy`, `Cancel` | `GET/POST /api/extractions`, `DELETE /api/extractions/current`, `POST /api/vacancies/{id}/extract` | `Integration/ExtractionApiTests.cs` |
| Fit check with extraction | `Core/Fit/FitAnalyzer.cs` (`FitInput.Extraction`, `FitSource`), `Core/Fit/VacancyFitService.cs`, `Core/Scoring/VacancyEnricher.cs` | `AddExtracted`, `ExtractedYears` | `GET /api/vacancies/{id}/fit` (`source`) | `Unit/Llm/ExtractionTests.cs` |
| Gold labels | `Core/Extraction/VacancyExtraction.cs` (`GoldLabel`), `Api/Features/Extraction/ExtractionEndpoints.cs`, `scripts/seed-gold-labels.ps1` | `SetLabel`, `GetLabelSummary` | `PUT/DELETE /api/vacancies/{id}/label`, `GET /api/labels/summary` | `Integration/ExtractionApiTests.cs` |
| UI | `fe/app/components/VacancyRequirements.vue`, `GoldLabelButtons.vue`, `ExtractionPanel.vue` (Statistik), `AppHeader.vue` (counter), `composables/useLabelSummary.ts`, `types/extraction.ts` | — | — | `fe/test/nuxt/goldLabelAndRequirements.test.ts` |

Config: `Llm:Tasks:Extraction`, `Llm:Providers:Ollama` (Enabled, BaseUrl, TimeoutSeconds, MaxRetries, Temperature),
`Llm:Extraction` (AutoAfterImport, MaxPerRun, MaxDescriptionCharacters, PromptVersion, Thinking), `Fit:MethodsGapMax`.
Docker: `OLLAMA_ENABLED`, `Llm__Tasks__Extraction` in `.env`.

## Background jobs, scheduler, locks

| Feature | Files | Key symbols | Tests |
|---|---|---|---|
| Import worker | `Api/Features/VacancyImports/ImportWorker.cs` | `ImportWorker` | `Integration/ImportWorkerTests.cs` |
| Daily schedule | `Api/Features/VacancyImports/ImportScheduler.cs`, `Core/Import/DailySchedule.cs`, `Core/TimeZones.cs` | `DailySchedule`, `TimeZones.Berlin` | `Unit/Import/DailyScheduleTests.cs` |
| Re-score worker | `Api/Features/Scoring/RescoreWorker.cs`, `RescoreCoordinator.cs` | `RescoreCoordinator` | `Integration/RelevanceApiTests.cs` |
| Import lock | `Core/Import/IImportLock.cs`, `Infra/Persistence/PostgresImportLock.cs` | `TryAcquireAsync` (advisory lock) | `Integration/ImportLockTests.cs` |
| Suchprofil seed at start | `SearchProfileInitializer` in `Api/Features/SearchProfiles/SearchProfileEndpoints.cs` | logs the legacy `preferences` hint once | `Integration/SearchProfileApiTests.cs` |
| Extraction worker | `Api/Features/Extraction/ExtractionWorker.cs` | one GPU job at a time, no import lock | `Integration/ExtractionApiTests.cs` |

Config: `Import:Schedule` (Enabled, TimeOfDay, TimeZone), `Database:MigrateOnStartup`.

## Security and middleware

| Feature | Files | Key symbols | Tests |
|---|---|---|---|
| CORS, headers, rate limit | `Api/Common/ApiSecurity.cs`, `Api/Program.cs` | `AddApiSecurity`, `UseApiSecurity`, `ValidateOrigins` | `Unit/ApiSecurityTests.cs`, `Integration/SecurityTests.cs` |
| Errors | `Api/Program.cs` (`UseExceptionHandler`, ProblemDetails), `Infra/Persistence/DbErrors.cs` | — | `Integration/*` |
| Health | `Api/Features/Health/HealthEndpoints.cs` | — | `Integration/HealthEndpointTests.cs` |
| Frontend CSP | `fe/server/plugins/content-security-policy.ts` | — | `fe/test/contentSecurityPolicy.test.ts` |
| Docs | `docs/SECURITY.md` | — | — |

Config: `Cors:AllowedOrigins`, `RateLimiting`, `AllowedHosts`.

## Frontend

| Area | Files | Tests (`fe/test/`) |
|---|---|---|
| List `/` | `pages/index.vue`, `components/VacancyList.vue`, `VacancyCard.vue`, `VacancyFilters.vue`, `VacancyPagination.vue`, `ScoreBadge.vue`, `VacancyReasons.vue`, `utils/vacancyFilters.ts`, `listState.ts`, `savedFilters.ts`, `vacancyBadges.ts`, `emptyState.ts`, `composables/useListDefaults.ts`, `useScoreThresholds.ts` | `vacancyFilters`, `listState`, `savedFilters`, `vacancyBadges`, `emptyState`, `nuxt/scoreAndFilters`, `nuxt/listUrl` |
| Detail `/vacancies/[id]` | `pages/vacancies/[id].vue`, `components/VacancyFit.vue`, `HighlightedText.vue`, `KeywordList.vue`, `utils/fitView.ts`, `highlight.ts`, `safeUrl.ts` | `fitView`, `highlight`, `safeUrl` |
| Import | `components/ImportButton.vue`, `RescoreButton.vue`, `composables/useLastImport.ts`, `utils/importProgress.ts`, `importSummary.ts` | `importProgress`, `importSummary` |
| Suchprofil `/suchprofil` | `pages/suchprofil.vue`, `components/TagInput.vue`, `utils/searchProfileForm.ts` | `searchProfileForm`, `nuxt/searchProfilePage` |
| Profil `/profil` | `pages/profil.vue`, `utils/profileView.ts` | `profileView` |
| Bewerbungen `/bewerbungen` | `pages/bewerbungen.vue`, `components/ApplicationDrawer.vue`, `utils/applications.ts`, `optimistic.ts` | `applications`, `optimistic` |
| Unterlagen `/unterlagen/[id]` | `pages/unterlagen/[id].vue`, `components/LetterEditor.vue`, `CvOptionsEditor.vue`, `DraftWarnings.vue`, `ApplicationCheatSheet.vue`, `utils/documents.ts` | `documents`, `nuxt/documentComponents`, `nuxt/reviewPage` |
| Statistik `/statistik` | `pages/statistik.vue`, `types/statistics.ts` | — |
| Shell | `app.vue`, `components/AppHeader.vue`, `ToastHost.vue`, `ShortcutHelp.vue`, `composables/useToasts.ts`, `utils/shortcuts.ts`, `relativeDate.ts`, `apiError.ts`, `employerType.ts`, `assets/styles/main.scss` | `shortcuts`, `relativeDate`, `employerType` |
| API client + types | `composables/useVacanciesApi.ts`, `types/*.ts` | `nuxt/fixtures.ts` (test data) |
| Config | `fe/nuxt.config.ts` (`NUXT_PUBLIC_API_BASE`), `fe/vitest.config.ts` (projects `unit`, `nuxt`), `fe/audit-allowlist.json`, `fe/scripts/audit-check.mjs` | — |

Paths are under `fe/app/` unless they start with `fe/`.

## DB schema and migrations

| Item | Files |
|---|---|
| DbContext + configurations | `Infra/Persistence/AppDbContext.cs`, `Infra/Persistence/Configurations/*Configuration.cs`, `UtcDateTimeOffsetConverter.cs` |
| Migrations | `Infra/Persistence/Migrations/<timestamp>_<Name>.cs` (latest: `20261020155113_AddRequiredGerman`) |
| Schema doc | `docs/schema.md` |
| DI + connection | `Infra/DependencyInjection.cs`, `DatabaseOptions.cs`; `ConnectionStrings`, `Database:MigrateOnStartup` |
| Test DB | `Tests/Integration/PostgresFixture.cs`, `IntegrationTestBase.cs`, `ApiFactory.cs` (fakes: sources, offline Ollama), `VacancySeed.cs`; env `JOBAUTOPILOT_TEST_POSTGRES` or Testcontainers; `Tests/Fakes/FakeLlmClient.cs` |
| Backup/restore | `scripts/backup-db.ps1`, `restore-db.ps1` |

## CI and tooling

| Item | Files |
|---|---|
| CI (backend build/test + NuGet audit, frontend typecheck/test/build + npm audit, Docker smoke test) | `.github/workflows/ci.yml` |
| CodeQL + SARIF gate | `.github/workflows/codeql.yml`, `.github/scripts/sarif-gate.py` |
| Dependabot | `.github/dependabot.yml` |
| Build settings | `backend/Directory.Build.props` (warnings as errors, analyzers), `global.json`, `dotnet-tools.json`, `.editorconfig` |
| Docker | `docker-compose.yml`, `backend/Dockerfile`, `frontend/Dockerfile`, `.env.example`, `scripts/_compose-env.ps1` |
| Dev mode | `dev.ps1` |
| Claude Code | `.claude/settings.json` (read-deny for generated/vendored/personal paths) |
