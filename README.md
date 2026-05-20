# SmartNarrator

AI-assisted pipeline for **long-form fiction**: ingest **EPUB** / PDF / HTML / Markdown / plain text into **canonical Markdown** (paragraph breaks, horizontal rules as PDF page markers / spine separators), derive **chapter boundaries during ingest** (EPUB/HTML structure vs AI major-shift detection for plain Markdown/PDF), run **structured analysis** (**quoted-speech spans** from paired punctuation; **Ollama** for characters, utterance linkage, narrator passages, voice-facing metadata), let you **review and edit** profiles in an **Angular** UI, optionally **render clips** through an OpenAI-compatible **speech HTTP API**, and **import/export** narrator + character bundles as JSON.

> Use only sources you have the legal right to process and synthesize.

## Stack

| Layer | Tech |
|--------|------|
| Domain / application | `net8` class libraries (`SmartNarrator.Domain`, `SmartNarrator.Application`) |
| Persistence & integrations | `SmartNarrator.Infrastructure` — EF Core 8 + Npgsql, PDF (PdfPig), HTML → Markdown (AngleSharp), EPUB spine (VersOne.Epub), Ollama JSON chat, typed `HttpClient` speech bridge |
| API | `SmartNarrator.Api` ASP.NET controllers under routes like `/works`, `/jobs` |
| SPA | Angular 19 in [`client/`](client/) |

## Running locally (.NET + Angular dev servers)

1. Start PostgreSQL and set `ConnectionStrings:Default` in [`SmartNarrator.Api/appsettings.Development.json`](SmartNarrator.Api/appsettings.Development.json) (or override with env `ConnectionStrings__Default`).
2. From the repo root:

   ```bash
   dotnet run --project SmartNarrator.Api
   ```

   API defaults to **`http://localhost:5022`** (see [`Properties/launchSettings.json`](SmartNarrator.Api/Properties/launchSettings.json)).

3. Point Ollama to a running instance in [`SmartNarrator.Api/appsettings.json`](SmartNarrator.Api/appsettings.json) (`Ollama:BaseUri`, `Ollama:Model`, optional **`AnalysisTemperature`** ~0.25–0.35 for richer inference vs steadier output).
4. In another terminal:

   ```bash
   cd client
   npm start
   ```

   The SPA uses [`client/proxy.conf.json`](client/proxy.conf.json) so browser calls like `/api/works` proxy to `http://localhost:5022/works`.

5. Open `http://localhost:4200`. Swagger is at `http://localhost:5022/swagger` when running in Development.

Background jobs (ingest / analyze / render) stream **live status** from the API over **SignalR** at **`/hubs/jobs`**; the SPA uses **`/api/hubs/jobs`** (see [`jobs-hub.connection`](client/src/app/core/jobs-hub.connection.ts)), with a slow HTTP fallback. **Dev**: ensure [`proxy.conf.json`](client/proxy.conf.json) has **`"ws": true`** for upgrades to `5022`.

**LLM prompts / responses for debugging characterization:** set **`Ollama:CaptureAnalyzeLlmTurns`** to `true` so each Phase 3 (and ingest chapter-shift) structured request appends **`prompt`** + **`response`** to **`{Storage:RelativeRoot}/llm-diagnostics/{jobId}/turns.ndjson`** on the machine that executes the job. With Docker, **api** and **worker** already share the **`smartnarrator_storage`** volume at `/app/App_Data/storage`, so the API can serve them via **`GET /jobs/{id}/llm-diagnostics`**; the SPA jobs table includes an **Open** link under **LLM diag** for AI-heavy jobs (`OLLAMA_CAPTURE_ANALYZE_LLM_TURNS=true` via Compose). Responses may contain long excerpts (`Ollama:AnalyzeDiagnosticsMaxCharsPerField` truncates persisted fields). Analyze **Phase 3** structured output binds dialogue rows to **`characters`** profiles via required **`character_external_key`** (**`__not_speech__`** marks quoted-non-dialogue spans so they never keep a narrator/character FK).

Optional **speech** rendering: configure `SpeechSynthesis` in [`appsettings.json`](SmartNarrator.Api/appsettings.json) (`BaseUri`, `RelativePath`, `/v1/audio/speech`-style payloads). Failures during render are logged and skipped per chunk.

**If Characters stay empty after analysis:** Ensure the analyze job ends **Succeeded** (see **Analysis** — failures include **`errorMessage`**). With Docker Compose, the API calls **`http://ollama:11434`**; pull your chat model inside the Ollama container (e.g. `make setup` / `OLLAMA_PULL_MODELS`, or `docker compose exec ollama ollama pull llama3.2`). The API uses **Ollama structured outputs** (JSON Schema on `format`) so the model cannot substitute random shapes like `{title,text,author}`; use a recent Ollama image ([structured outputs blog](https://ollama.com/blog/structured-outputs)). After a successful run, open **Characters** and use **Reload from server** (or switch away and back). The **Profiles** tab exports or imports a **JSON bundle** (character rows + narrator passage metadata from **Summary**); it does not run AI by itself. Voice-facing fields are edited under **Characters** and narrative voice spans under **Summary**. Character count on Profiles reflects rows saved after analysis.

## Make: `setup` and `deploy` (Docker)

From the **repository root**, with **Docker Desktop** (or Docker Engine) **running**:

| Target | What it does |
|--------|----------------|
| **`make setup`** | Verifies `docker` CLI and daemon + `docker compose`; creates **`.env`** from [`.env.example`](.env.example) if missing; **probes NVIDIA GPU via Docker** (`docker run --gpus all`) and prompts **\[Y/n\]** (default **yes**) to set **`OLLAMA_USE_GPU`** and merge [**`docker-compose.gpu.yml`**](docker-compose.gpu.yml); **builds** all images; **`docker compose up -d`**; waits for Ollama; **idempotently** `ollama pull` for each model in **`OLLAMA_PULL_MODELS`** (override in `.env`). |
| **`make deploy`** | Rebuilds **`api`**, **`spa`**, and **`worker`**; ensures **Postgres** + **RabbitMQ** are up; runs **`/app/db-migrate`**; then **recreates** **`api`**, **`spa`**, and **`worker`**. Requires **`.env`**. RabbitMQ ships with the **management UI** on **`${RABBITMQ_MANAGEMENT_PORT:-15672}`** when using **`docker-compose.dev.yml`**. Only **one AI-heavy job** (Analyze / Render speech) runs at a time via a dedicated queue prefetch of **1** *per **`worker` replica*** — keep **`worker`** at a **single replica** in Compose unless you intentionally allow parallel AI jobs. Ingest jobs may run in parallel (`Jobs:RabbitMqGeneralPrefetch`). |

**Requirements:** POSIX `sh` on your PATH (Git Bash, WSL, macOS, Linux). Windows **cmd.exe** is not supported unless you invoke the scripts through `sh`. **Git Bash:** `make deploy` sets **`MSYS_NO_PATHCONV=1`** for Compose so Docker receives Unix paths (`/app/db-migrate`) unchanged.

**Ollama models (manual override):** edit `.env`:

- **`OLLAMA_PULL_MODELS`** — space-separated tags to pull after setup (default: `llama3.2:latest mistral:7b`). Re-run **`make setup`** to pull additions, or execute `docker compose ... exec ollama ollama pull <tag>` manually.
- **`OLLAMA_MAX_CHARS_PER_REQUEST`** — UTF-16 chunk size per analyze call (full stories use many chunks).
- **`OLLAMA_ANALYSIS_CHUNK_OVERLAP`** — overlap between chunks (Compose maps to **`Ollama__AnalysisChunkOverlapUtf16`**).
- **`OLLAMA_ANALYSIS_PRIOR_PARAGRAPH_MAX_BACK`** — max backward UTF-16 extension so each chunk can prepend the previous `\n\n` paragraph and/or prior ingest segment (**`Ollama__AnalysisPriorParagraphMaxBackUtf16`**).
- **`OLLAMA_SPEAKER_REVIEW_THRESHOLD`** — for utterances below this confidence that are **not** already auto-trusted, keep **speaker review** when attribution is unresolved or the model flagged uncertainty (**`Ollama__SpeakerConfidenceNeedsReviewThreshold`**).
- **`OLLAMA_SPEAKER_AUTO_TRUST_THRESHOLD`** — at or above this confidence, a **resolved** character link skips review; fuzzy name/alias matching is used only from this level up (**`Ollama__SpeakerConfidenceAutoTrustThreshold`**, default **0.8**).
- **`OLLAMA_CAPTURE_ANALYZE_LLM_TURNS`** — when `true`, workers append structured **Phase 3 / ingest chapter-shift** prompts and model JSON to **`llm-diagnostics/{jobId}/turns.ndjson`** on the shared storage volume; **`GET /jobs/{id}/llm-diagnostics`** returns them (**`Ollama__CaptureAnalyzeLlmTurns`**). Optional cap: **`OLLAMA_ANALYZE_DIAG_MAX_CHARS_PER_FIELD`**.
- **`OLLAMA_CHAT_MODEL`** — value passed through Compose as **`Ollama__Model`** to the API; must match a name **`ollama list`** reports (e.g. `llama3.2` or `llama3.2:latest` depending on how Ollama registered the model). Change it and run **`make deploy`** so the `api` container is recreated with the new env.

Port defaults live in **`.env.example`** (`WEB_PORT`, `API_PORT`, `OLLAMA_PORT`, `POSTGRES_PORT`).

**Ollama + NVIDIA GPU:** Setup relies on [NVIDIA Container Toolkit](https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/install-guide.html) so **`docker run --gpus all`** works. **`OLLAMA_USE_GPU=1`** in `.env` adds **`docker-compose.gpu.yml`** (Compose merges **`deploy.resources.reservations.devices`** for the **`ollama`** service). Non-interactive **`make setup`** enables GPU when the probe succeeds except in **`CI`**. Re-run setup does not change an existing **`OLLAMA_USE_GPU`** line.

## Docker Compose

For a **manual** compose flow (same stack `make setup` drives):

```bash
docker compose -f docker-compose.yml -f docker-compose.dev.yml --env-file .env build
docker compose -f docker-compose.yml -f docker-compose.dev.yml --env-file .env up -d postgres rabbitmq ollama api worker spa
```

With **`OLLAMA_USE_GPU=1`** in `.env`, add **`-f docker-compose.gpu.yml`** to both commands.


- UI + API proxy: **`http://localhost:${WEB_PORT:-8080}`** (Angular calls **`/api/...`**; nginx strips **`/api`** upstream via **`rewrite`**). **`spa`** **`depends_on`** **`api`**. **`/api/`** uses Docker DNS (**`resolver 127.0.0.11`** + variable **`proxy_pass`**) so **`api`** is re-resolved after container recreates — a static **`upstream { server api; }`** only resolves at nginx startup and often yields **502** once **`api`** gets a new IP. **`proxy_send_timeout`/`proxy_read_timeout` (3600s)**, **`proxy_connect_timeout` (300s)**, **`proxy_request_buffering off`**, **`client_max_body_size 200m`** cover large multipart imports.
- For **direct Postgres / Ollama / API debugging** bindings, merge the dev overrides:

   ```bash
   docker compose -f docker-compose.yml -f docker-compose.dev.yml --env-file .env up -d
   ```
   With **`OLLAMA_USE_GPU=1`**, insert **`-f docker-compose.gpu.yml`** before **`--env-file`** (same as `make setup`).

Copy [`.env.example`](.env.example) to `.env` and tweak ports (`WEB_PORT`, `POSTGRES_PORT`, `API_PORT`, `OLLAMA_PORT`, `SPEECH_SYNTHESIS_BASE_URI`).

The SPA Docker build runs **`npm ci --loglevel=error`** so benign **transitive** npm deprecation lines (for example `glob` / `inflight` pulled in by Angular CLI and Karma dev tooling) do not flood logs; they are not included in the nginx static bundle.

## Typical workflow

1. **Works** → **load a story** from a file on disk or a public **http(s)** URL — the app creates the work and queues **Ingest** automatically; you stay on Works until ingest succeeds, then open **Summary**. URL import is downloaded **on the server** (blocked for localhost and common private IP ranges; only `http`/`https`). A background job converts sources to Markdown → `works.canonical_text`, persists **`work_chapters`** + mirrored **`story_structure_sections`** (chapter kind)—skipping partitions whose slice is **only whitespace and punctuation** (nothing narratable); structural splits from **EPUB spine / HTML headings**, otherwise **AI major-shift detection** during ingest for plain Markdown/PDF; then saves paragraph **`text_segments`**.
2. **Analyze** detects **quoted dialogue** per ingest chapter using paired quotation marks (ASCII `"` and common curly quotes); when speech continues across paragraphs without a closing quote until the end of the utterance, those paragraphs are one span. Results populate **`dialogue_spans`** with global UTF‑16 offsets and **merge by span position** so unchanged offsets keep stable PostgreSQL IDs across reruns; orphaned spans drop when quoted boundaries change. A **chunked Ollama** pass (**`Ollama:MaxCharactersPerRequest`** / **`Ollama:AnalysisChunkOverlapUtf16`** / **`Ollama:AnalysisPriorParagraphMaxBackUtf16`**) infers the **character manifest + utterance linkage** from dialogue-in-chapter context (prior registry carries **`personality_summary`** / **`speech_style_notes`** forward across chapters). **New profiles reuse existing rows** when the model's display name or any alias intersects an established (non-placeholder) character on the work; **unknown / unnamed / anonymous** placeholders are never folded together — each stays its own profile until you merge manually. Narrator **gaps** between dialogue spans become **`narrative_passages`**. **`Ollama:SpeakerConfidenceNeedsReviewThreshold`** marks weak attributions for review. Tune **`OLLAMA_MAX_CHARS_PER_REQUEST`**, **`OLLAMA_ANALYSIS_CHUNK_OVERLAP`**, and **`OLLAMA_SPEAKER_REVIEW_THRESHOLD`** in Compose `.env` when needed.
3. **Summary** / **Characters** → review AI labels (**Summary** lists utterances, narrator passages, structural spans, **`work_chapters`**, and **`dialogue_spans`**); **Characters** lists profiles with editable **AI external key**, **aliases**, and voice notes — **save per card**; **merge selected into …** on the keeper row collapses duplicates (utterances / narrator links repoint; absorbed names and keys become aliases). Analyze prompts steer the model toward **speakers and attributed narrators only**, not every mentioned name.
4. **Render** (optional) queues `RenderSpeech` → writes `audio/<workId>/*.wav` and `audio_artifacts` rows; **Audio** lists download URLs.
5. **Profiles** → export/import JSON bundles (characters + narrator segments).

Background jobs use Postgres as the source of truth (`GET /jobs`, **`GET /jobs/{id}`**, **`POST /jobs/{id}/cancel`**). Job rows expose **`updatedUtc`** (last persisted progress/status change) for staleness in the SPA; apply migrations after upgrading. With Docker Compose (**`Jobs:DispatchMode=RabbitMq`**), the API **publishes job IDs** to **RabbitMQ** (`rabbitmq` service + optional management UI); the **`worker`** container consumes messages, executes **`JobExecutors`**, and POSTs **`/internal/jobs/notify`** so **SignalR** pushes still reach the SPA. Deleting a job removes its Postgres row; any queued RabbitMQ message for that id is ACKed without running because **`TryClaim`** requires a **Pending** row. **`App_Data/storage`** is on a shared Docker volume between **`api`** and **`worker`** so ingest jobs see uploaded sources. Local **`dotnet run`** defaults to **`Jobs:DispatchMode=InProcess`** (hosted **`BackgroundJobWorker`** polls Postgres).

Poll **`GET /jobs/{jobId}`** from the SPA after ingest/analyze/render (payload includes **`progressPercent`** and **`progressPhase`** while running). **`GET /jobs`** powers the SPA **Jobs** page (queued vs running durations). **Mutating HTTP failures** (POST/PUT/PATCH/DELETE) open an **error modal** with the raw JSON body and **copy-ready report** (includes **`stackTrace`** when the API sets **`Api:ExposeExceptionDetails`** to **`true`** or runs under **Development**). For Docker **`Production`** troubleshooting EPUB/import failures, set **`Api__ExposeExceptionDetails=true`** on the **`api`** container (temporary — exposes exception dumps to browsers). During ingest, plain Markdown/PDF sources may show an AI **major divisions** phase before paragraph segmentation; **`progressPhase`** ticks with elapsed seconds while PostgreSQL accepts the full canonical story text; API logs **`Persisted canonical text`** at Information level with **`characterLength`** and **`elapsedMs`** so you can tell extraction cost vs database time.

## Repo layout

- [`SmartNarrator.Domain`](SmartNarrator.Domain/) — entities/enums (`WorkEntity`, segments, chapters, dialogue spans, utterances, narrative passages, story structure sections, jobs, audio artifacts).
- [`SmartNarrator.Application`](SmartNarrator.Application/) — ports (`IStoryIngestionService`, `IRemoteStorySourceDownloader`, `IStructuredStoryAnalysisClient`, `ISpeechSynthesisClient`, `IProfileImportExportService`, coordinators).
- [`SmartNarrator.Infrastructure`](SmartNarrator.Infrastructure/) — EF Core [`SmartNarratorDbContext`](SmartNarrator.Infrastructure/Persistence/SmartNarratorDbContext.cs), ingest parsers, [`OllamaStoryAnalysisClient`](SmartNarrator.Infrastructure/Ai/OllamaStoryAnalysisClient.cs), phased analyze [`StoryPhasedAnalysisOrchestrator`](SmartNarrator.Infrastructure/Services/StoryPhasedAnalysisOrchestrator.cs), [`JobExecutors`](SmartNarrator.Infrastructure/Jobs/JobExecutors.cs), [`BackgroundJobRunner`](SmartNarrator.Infrastructure/Jobs/BackgroundJobRunner.cs), optional in-process [`BackgroundJobWorker`](SmartNarrator.Infrastructure/Jobs/BackgroundJobWorker.cs), RabbitMQ publishers/consumers.
- [`SmartNarrator.Worker`](SmartNarrator.Worker/) — **`Microsoft.NET.Sdk.Worker`** host that consumes RabbitMQ queues and forwards realtime bumps via HTTP (`Dockerfile.worker`).

## Licenses

Ensure you comply with publishers’ and authors’ copyrights before ingesting third-party prose or generating audiobook-style derivatives.
