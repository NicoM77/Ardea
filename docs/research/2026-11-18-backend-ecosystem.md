# CSH Alert System — backend ecosystem research

Research date: 2026-11-18. Method: live web lookups (official docs, NuGet, GitHub, Docker Hub) on that date.
Items that could not be confirmed against a primary source are marked **UNVERIFIED**.
"Secondary" means the fact comes from a blog/news page rather than the vendor's own page.

---

## 1. .NET versions

| | .NET 10 | .NET 11 |
|---|---|---|
| Type | LTS | STS (now 24 months) |
| GA | 2025-11-11 | **2026-11-10 (GA, one week old)** |
| Latest patch | **10.0.15** (2026-11-10) | 11.0.0 (2026-11-10) |
| End of support | **2028-11-14** | **2028-11-14** |

- .NET 8 and .NET 9 both reached end of support on 2026-11-10; .NET 10 and 11 are the only supported lines.
- Because STS support was extended to 24 months, **.NET 10 LTS and .NET 11 STS end on the same day**. The LTS label buys no extra lifetime over 11; it buys a year of servicing maturity (15 patches) and a finished third-party ecosystem.
- .NET 11 highlights (secondary sources): C# 15, OpenAPI 3.2 by default, async validation in Minimal APIs, automatic fetch-metadata CSRF protection, Zstandard built in; x64 now requires x86-64-v2.
- Practical reasons to stay on .NET 10 today:
  - **Npgsql 11 / Npgsql EF Core provider 11 are not GA** (EF provider latest is `11.0.0-rc.3`, 2026-10-28; `Npgsql` latest stable is 10.0.3). EF Core 11.0.0 itself is on NuGet (2026-11-10).
  - Every library in this report is verified against .NET 10; .NET 11 support in the long tail (Hangfire.PostgreSql, Quartz, Testcontainers modules, etc.) was not checked.
- Reason NOT to treat 10 as "set and forget": whichever you pick, a migration to .NET 12 LTS (Nov 2027) is needed before Nov 2028. Moving 10 → 12 skips one upgrade.

Sources:
- https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core
- https://support.microsoft.com/help/5135241 (.NET 10.0 Update, 2026-11-10)
- https://www.nuget.org/packages/Npgsql.EntityFrameworkCore.PostgreSQL

**Implication for the plan:** Start on .NET 10 (10.0.15) with `TargetFramework=net10.0` and pin via `global.json`; it is the only line where the Postgres EF provider is GA. Budget an upgrade to .NET 12 LTS in early 2028, and keep the code free of APIs that are 11-only.

---

## 2. ASP.NET Core 10 features (shipped and stable unless noted)

| Feature | Status in .NET 10 | Notes |
|---|---|---|
| Built-in OpenAPI (`Microsoft.AspNetCore.OpenApi`, `AddOpenApi` / `MapOpenApi`) | Stable | **OpenAPI 3.1 is the default document version** in .NET 10. Nullable types are emitted as `type: [..., "null"]`. Transformer API broke vs. 9 (`OpenApiAny` replaced by `JsonNode`). YAML endpoint supported (`/openapi/{documentName}.yaml`). Build-time generation via `Microsoft.Extensions.ApiDescription.Server`. No UI included; add Scalar or Swagger UI yourself. (.NET 11 moves the default to 3.2.) |
| Minimal API validation (`builder.Services.AddValidation()`) | Stable top-level API | DataAnnotations-based, source-generated; validates query, header and body; returns 400 ProblemDetails. `.DisableValidation()` per endpoint. The underlying *resolver* APIs are marked experimental; `AddValidation` and the built-in filter are stable. Caveats: the generator only discovers types in the assembly that calls `AddValidation` (matters for a modular monolith with DTOs in other assemblies); validators are synchronous in .NET 10 (async arrives in .NET 11). |
| Passkeys / WebAuthn in ASP.NET Core Identity | Stable, deliberately narrow | Not Blazor-only. The UI ships only in the **Blazor Web App template**, but the APIs are plain Identity services: `SignInManager<TUser>` passkey methods and `IPasskeyHandler<TUser>` (`MakeCreationOptionsAsync`, `PerformAttestationAsync`, ...), `UserManager.AddOrUpdatePasskeyAsync`, `IdentityPasskeyOptions` (`ServerDomain`, `AuthenticatorTimeout`, `ChallengeSize`, `VerifyAttestationStatement`). Microsoft's own doc shows them called from Minimal API endpoints, so **API + SPA works, but you write the endpoints and the browser-side `navigator.credentials` JS yourself**. Requires `IdentitySchemaVersions.Version3` + migration. Limitations (quoted from docs): "deliberately scoped to authentication scenarios. It isn't intended as a general-purpose WebAuthn library"; "No default attestation validation". Set `ServerDomain` explicitly, otherwise the RP ID is taken from the Host header. .NET 11 only adds AAGUID-based display names. |
| `TypedResults.ServerSentEvents` | Stable | Takes `IAsyncEnumerable<SseItem<T>>` (or `IAsyncEnumerable<T>` + `eventType`). Works in Minimal APIs and controllers. |
| `MapIdentityApi<TUser>()` | Stable, intentionally minimal | Endpoints: `POST /register`, `/login`, `/refresh`, `GET /confirmEmail`, `POST /resendConfirmationEmail`, `/forgotPassword`, `/resetPassword`, `/manage/2fa`, `GET`/`POST /manage/info`. Limitations: tokens are **proprietary opaque bearer tokens, not JWTs** ("isn't intended to be a full-featured identity service provider or token server"); bearer tokens are not revoked by `SignOutAsync`; **no logout endpoint, no passkey endpoints, no external-login endpoints, no admin/user-management, 2FA is TOTP only**; routes/DTOs are not customisable short of copying the source. Microsoft recommends **cookies** for browser apps (`/login?useCookies=true`). |
| Rate limiting middleware (`AddRateLimiter` / `UseRateLimiter`) | Stable since .NET 7 | Fixed/sliding window, token bucket, concurrency; partitioned per user/IP; `RequireRateLimiting("policy")`. In-memory only (per instance) — fine for a single API container. |
| Cookie auth + SPA | **New in 10** | Cookie-protected *API* endpoints now return **401/403 instead of 302 redirects** by default (detected via `IApiEndpointMetadata`: `[ApiController]`, Minimal APIs with JSON bodies/`TypedResults`, SignalR). Removes the classic `OnRedirectToLogin` workaround. |
| Antiforgery | No change found for 10 (the release-notes page was only partly readable) | `AddAntiforgery` / `UseAntiforgery`, tokens still manual for JSON APIs (header token pattern) in .NET 10. **.NET 11** adds automatic CSRF protection based on `Sec-Fetch-Site` / `Origin` for apps built with `WebApplication.CreateBuilder` (secondary sources: Duende, Andrew Lock; MS anti-forgery doc updated). Not available on 10. |
| Auth/Identity metrics | New in 10 | `Microsoft.AspNetCore.Authentication` / `.Authorization` / `.Identity` meters (sign-ins, challenges, password checks, 2FA usage). |

Sources:
- https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-10.0
- https://learn.microsoft.com/en-us/aspnet/core/security/authentication/passkeys
- https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-api-authorization
- https://duendesoftware.com/blog/20260901-understanding-dotnet-11-automatic-csrf-protection (11-only CSRF feature, secondary)

**Implication for the plan:** Use cookie auth (SameSite=Strict/Lax, `__Host-` cookie) for the SPA with hand-written auth endpoints on top of `SignInManager`/`UserManager` rather than `MapIdentityApi` (no logout, no passkeys, no customisation). On .NET 10 add your own CSRF defence (antiforgery header token, or a small `Sec-Fetch-Site`/`Origin` check middleware that mirrors what .NET 11 ships). Passkeys are feasible as a later increment using the built-in handler; TOTP MFA is available from day one.

---

## 3. EF Core 10, Npgsql, PostgreSQL, pgvector

Versions:

| Package | Current | Date | Licence |
|---|---|---|---|
| `Microsoft.EntityFrameworkCore` | **10.0.15** (11.0.0 also GA) | 2026-11-10 | MIT |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | **10.0.3** (11.0.0-rc.3 prerelease, 2026-10-28) | 2026-07-10 | PostgreSQL licence |
| `Npgsql` | **10.0.3** | 2026-05-27 | PostgreSQL licence |
| PostgreSQL | **18.7** is the latest GA major (17.12, 16.16, 15.20 supported; 14 EOL 2026-11-12). **19 is at RC 2**, not GA. | 18 GA 2025-09-25, EOL 2030-11-14 | PostgreSQL licence |
| pgvector | **0.8.7** (0.9.0 unreleased; will drop PG 13/14 and add async I/O) | 2026-10-01 | PostgreSQL licence |
| `Pgvector.EntityFrameworkCore` | 0.3.0 (EF Core 9 and 10) | 2025-12-21 | MIT |

Features confirmed:
- **Named query filters** (EF 10): `HasQueryFilter("SoftDelete", ...)`, `HasQueryFilter("Tenant", ...)`, selectively disabled with `IgnoreQueryFilters(["SoftDelete"])`. Several filters per entity are finally possible.
- **Complex types**: optional complex types, struct support, and **JSON mapping** via `ComplexProperty(x => x.Details, c => c.ToJson())`. Npgsql docs call this "the preferred way to perform strongly-typed JSON mapping" (maps to `jsonb`). Owned-entity JSON mapping still works but is the legacy path.
- **Bulk ops**: `ExecuteUpdateAsync` / `ExecuteDeleteAsync` (since EF 7). EF 10 adds JSON-column support (Npgsql: partial `jsonb` updates) and accepts a normal statement lambda for conditional setters. They bypass the change tracker and SaveChanges interceptors.
- Other EF 10: `LeftJoin`/`RightJoin` LINQ operators, parameterised collections become padded scalar parameters by default, inlined constants are redacted from logs, analyzer warning for string concatenation in `FromSqlRaw`.
- **UUIDv7**: the Npgsql provider has generated UUIDv7 client-side by default for `Guid` keys since provider 9. With `SetPostgresVersion(18, 0)` it translates `Guid.CreateVersion7()` to the server-side `uuidv7()` function, which is native in PostgreSQL 18. `Guid.CreateVersion7()` exists in the BCL since .NET 9.
- **Row-level security**: nothing built in; EF Core has no RLS abstraction. Workable patterns (design knowledge, not a single official doc — treat details as **UNVERIFIED** until prototyped):
  - `DbConnectionInterceptor.ConnectionOpenedAsync` running `SELECT set_config('app.tenant_id', @id, false)` (session scope). Npgsql resets session state when a connection returns to the pool unless `No Reset On Close=true`, which is what makes session scope tolerable; it is not safe behind PgBouncer in transaction mode.
  - Transaction scope (`SET LOCAL` / `set_config(..., true)`) only works inside an explicit transaction, so it needs a `DbTransactionInterceptor` or a unit-of-work wrapper that always opens a transaction; a connection-opened interceptor is too early.
  - RLS only applies when the app connects as a non-owner role without `BYPASSRLS`; migrations need a separate owner role.
  - Named query filters are the simpler first line of defence; RLS is defence in depth.
- **Docker tags**: `postgres:18` / `postgres:18.7` / `postgres:18-alpine` (also `-trixie`, `-bookworm`). **Breaking change in the 18+ image:** `PGDATA` is now version-specific (`/var/lib/postgresql/18/docker`) and the declared `VOLUME` is `/var/lib/postgresql` — mount the volume there, not at `/var/lib/postgresql/data` as for ≤17.
- **pgvector image**: `pgvector/pgvector:pg18` (also `0.8.7-pg18`, `pg18-trixie`), pushed ~2026-11-13. Older pgvector 0.6.0–0.8.1 has CVE-2026-3172 (HNSW parallel build overflow); use ≥0.8.2.

Sources:
- https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/whatsnew
- https://www.npgsql.org/efcore/release-notes/10.0.html
- https://www.postgresql.org/support/versioning/
- https://hub.docker.com/_/postgres
- https://github.com/pgvector/pgvector/blob/master/CHANGELOG.md

**Implication for the plan:** Target PostgreSQL 18 (`postgres:18.7`, or `pgvector/pgvector:0.8.7-pg18` if embeddings are in scope) with the new volume path, EF Core 10.0.x + Npgsql provider 10.0.3, UUIDv7 keys, JSON complex types for flexible advisory payloads, and named query filters for tenant/soft-delete. Treat RLS as an optional hardening spike, not a foundation.

---

## 4. Background jobs / scheduling with PostgreSQL persistence

### Snapshot

| Option | Current version (date) | Licence | .NET 10 | Maintenance signal |
|---|---|---|---|---|
| **Quartz.NET 4.x** | 4.7.3 (2026-11-09); **4.0 GA 2026-09-03** | Apache-2.0 | `net10.0` only (also ships `net11.0` assets) | Very active (4.0 → 4.7 in ~10 weeks). 3.x line still serviced: 3.22.10 (2026-11-09), netstandard2.0. |
| **Hangfire** (`Hangfire.Core`) | 1.8.25 (2026-08-28) | LGPL-3.0 core; Pro/Ace paid (Startup $500/yr, Business $1,500/yr, Enterprise $4,500/yr) | Works (netstandard2.0) | Slow but steady; 1.8 line since 2023. |
| **Hangfire.PostgreSql** | 1.21.1 (2026-02-11) | LGPL-3.0 | Works (netstandard2.0, Npgsql ≥ 6) | **Community-maintained** (owners frankhommers, vytautask, azygis — not HangfireIO); 20 open issues / 6 PRs; ~2–3 releases a year. Official Hangfire storage list is SQL Server / Redis / in-memory only. |
| **TickerQ** | 10.5.1 for .NET 8–10, 11.5.1 for .NET 11 (2026-10-27) | **Relicensed: "TickerQ Software License Agreement v1.0", source-available**, not OSS. Free "Community" certificate only for orgs under US$5M revenue, renewed every 12 months; a missing/invalid certificate blocks job execution. Older releases stay MIT/Apache-2.0. | Yes | Active, single vendor (Arcenox). |
| **Coravel** | 6.0.2 (2025-01-17) | MIT (Coravel Pro is a paid UI) | Runs (net6.0 asset) | **Stale: no release in 22 months.** In-memory only, no persistence, no distributed lock, no retries. |
| **Wolverine** (`WolverineFx`) | 6.64.2 (2026-11-16) | MIT (JasperFx sells support; the **CritterWatch** console is BSL/commercial with a limited free tier) | net9.0 / net10.0 | Extremely active (multiple releases per week). |
| **Cronos** | 0.13.0 (2026-04-29) | MIT | Yes (net6.0, netstandard) | Healthy; owned by HangfireIO; used by Hangfire and Wolverine. Still 0.x but stable API. |

### Capability comparison

| | Quartz.NET 4 | Hangfire + Hangfire.PostgreSql | Wolverine + PostgreSQL | TickerQ |
|---|---|---|---|---|
| Model | Scheduler (jobs + triggers) | Fire-and-forget, delayed, recurring jobs | Message bus / command handlers with durable inbox/outbox | Scheduler with EF Core store |
| Dashboard | **Built in since 4.0**: `Quartz.Dashboard` (Blazor, `MapQuartzDashboard()`), plus HTTP API (`MapQuartzHttpApi()`); both require authorization to start | Built in (`/hangfire`), mature | None in the MIT package; CritterWatch is commercial | Built in |
| Retries / backoff | Documented "Retrying failed jobs" how-to; misfire policies are first-class (details not read — **UNVERIFIED** how declarative retries are in 4.x) | `[AutomaticRetry]` with delays, first-class | First-class error policies (retry, cooldown, requeue, dead-letter table) | Yes |
| Cron + time zones | Yes: per-trigger `InTimeZone`, calendars, RFC 5545 recurrence, 5- and 6-field cron | Yes: `RecurringJobOptions.TimeZone` (Cronos) | `opts.Schedules.ScheduleRecurring` since 6.34 (Cronos). "Deliberately not a scheduler": missed occurrences are skipped, never back-filled; min 5 s interval | Yes |
| Transactional enqueue with EF Core `SaveChanges` | Possible: `scheduler.EnlistTransaction(...)` with `AcceptEnlistedTransactions = true` on the ADO job store; Quartz docs show an EF Core transaction example. Same database required; keep transactions short (blocks trigger acquisition) | **Not supported out of the box** with Hangfire.PostgreSql + EF Core (no documented shared-transaction API; community `Hangfire.Community.Outbox` exists). Typical pattern is own outbox table + enqueue after commit | **Best in class**: `UseEntityFrameworkCoreTransactions()` + `AddDbContextWithWolverineIntegration<T>()`; messages are persisted in the same `SaveChangesAsync` transaction; per-module outbox via ancillary stores | EF Core store, so same-DbContext writes are natural (not verified in detail) |
| Multi-worker safety | ADO job store clustering (row locks; "lock-free firing" from 4.6) | Yes by design (multiple servers share one storage via distributed locks; the Postgres plugin's exact fetch/locking mechanism was not verified today) | Yes: `FOR UPDATE SKIP LOCKED` dequeue, advisory-lock leader election, node agents. Breaks behind transaction-mode poolers (PgBouncer) | Yes (node coordination) |
| Main risks | 4.x is brand new and churning: required schema migrations in 4.2, 4.3, 4.6, 4.7; DST fire-time changes vs 3.x; release notes credit an AI model as co-contributor | LGPL (fine for dynamic linking, but note for an OSS project's licence choice); Postgres storage is a third-party plugin; no outbox | Large framework with strong conventions and codegen; weekly releases mean constant upgrade pressure; dashboard is paid | **Licence** — unsuitable as a dependency of an OSS project that others self-host commercially |

### Other Postgres-backed options
- **Own outbox + `SELECT … FOR UPDATE SKIP LOCKED` worker loop** (in the Worker Service): ~200 lines, fully transactional with EF Core, no dependency. Combine with **Cronos** for cron parsing and a `scheduled_jobs` table with `next_run_at`. This is the lowest-risk baseline for a small OSS project.
- **DotNetCore.CAP** 10.0.2 (2026-08-01, MIT, net8–net10): EF Core outbox with PostgreSQL storage and a dashboard; designed around a broker, not a job scheduler.
- **Rebus.PostgreSql** 9.1.1 (**2024-05-20**, MIT): Postgres transport, sagas, timeouts — stale.
- **PGMQ + Npgmq** 1.13.0 (2026-04-21, MIT): needs the `pgmq` extension in the database image.
- **MassTransit SQL transport**: commercial from v9 (see section 6).
- Newer single-author libraries found on NuGet (NexJob.Postgres, BackWave.Postgres, Cabal.PostgreSQL, TaskTower 0.1.0, DotNetWorkQueue.Transport.PostgreSQL 0.11.0) — all claim SKIP LOCKED / transactional enqueue; none has a track record. **UNVERIFIED** quality; not recommended as a foundation.

Sources:
- https://www.quartz-scheduler.net/posts/2026-09-03-quartznet-4.0-released.html , https://github.com/quartznet/quartznet/releases , https://www.quartz-scheduler.net/documentation/quartz-4.x/how-tos/wolverine.html
- https://www.nuget.org/packages/Hangfire.Core , https://www.hangfire.io/pricing/ , https://github.com/hangfire-postgres/Hangfire.PostgreSql
- https://github.com/Arcenox-co/TickerQ , https://tickerq.net/docs/5.x/getting-started/licensing , https://www.nuget.org/packages/TickerQ
- https://wolverinefx.net/guide/durability/postgresql.html , https://wolverinefx.net/guide/durability/efcore/ , https://www.nuget.org/packages/WolverineFx
- https://www.nuget.org/packages/Coravel , https://www.nuget.org/packages/Cronos

**Implication for the plan:** Exclude TickerQ (relicensed) and Coravel (stale, in-memory). The real choice is between (a) a small hand-rolled EF Core outbox + SKIP LOCKED job table with Cronos, (b) Quartz.NET (3.22.x for stability or 4.x for the built-in dashboard and transaction enlistment, accepting churn), or (c) Wolverine if a durable message-handler architecture is wanted across the whole monolith. Hangfire is viable but gives no outbox and depends on a community Postgres plugin under LGPL.

---

## 5. AI integration in .NET

### Microsoft.Extensions.AI (MEAI)
- **GA** (since 9.5.0 in May 2025). Current: **`Microsoft.Extensions.AI` 11.0.0 (2026-11-10)**; last 10.x was 10.11.0 (2026-10-14); monthly cadence. MIT. Targets net8.0–net11.0, netstandard2.0, net462 — the 11.0.0 package runs on .NET 10.
- Abstractions: `IChatClient`, `IEmbeddingGenerator<,>`, `IImageGenerator` (experimental).
- **Structured output**: `chatClient.GetResponseAsync<T>(...)` generates a JSON schema from `T`, sends it as `ChatResponseFormat.ForJsonSchema(...)` when the provider supports native schema output, and deserialises `response.Result`. Equivalent low-level knob: `ChatOptions.ResponseFormat`.
- **Middleware** via `ChatClientBuilder`: `UseFunctionInvocation()`, `UseDistributedCache()` (any `IDistributedCache`), `UseOpenTelemetry()` (GenAI semantic conventions), `UseLogging()`; custom delegating clients for rate limiting.

### `IChatClient` providers

| Provider | Package | Version (date) | Licence | Notes |
|---|---|---|---|---|
| OpenAI / Azure OpenAI | `Microsoft.Extensions.AI.OpenAI` (+ `OpenAI` ≥ 2.14.0, `Azure.AI.OpenAI`) | **11.0.0 stable** (2026-11-10) — no longer preview | MIT | `new OpenAIClient(...).GetChatClient(model).AsIChatClient()` |
| Generic OpenAI-compatible (vLLM, LM Studio, llama.cpp server, Ollama `/v1`) | same package | | | Point `OpenAIClientOptions.Endpoint` at the server. `response_format: json_schema` is honoured by Ollama's `/v1` endpoint (Ollama docs) and LM Studio (secondary source); vLLM and llama.cpp server support it per prior knowledge — test each target. |
| Ollama | `OllamaSharp` | 5.5.2 (2026-11-05) | MIT (**UNVERIFIED** on NuGet page; repo is MIT historically) | `OllamaApiClient` implements `IChatClient` and `IEmbeddingGenerator` natively. `Microsoft.Extensions.AI.Ollama` was retired in favour of OllamaSharp. |
| Anthropic | **`Anthropic`** (official SDK since v10; repo `anthropics/anthropic-sdk-csharp`) | 13.10.0 (2026-11-17) | Apache-2.0 | **Yes, implements `IChatClient`**: `new AnthropicClient().AsIChatClient("model-id")`; depends on `Microsoft.Extensions.AI.Abstractions` ≥ 10.5.1; netstandard2.0/net8/net9 assets. Native structured outputs via `OutputConfig` JSON-schema format. Do not confuse with `Anthropic.SDK` (tghamm, unofficial) or `tryAGI.Anthropic` (community; owned the `Anthropic` package ID up to 3.x). |
| Google Gemini | **`Google.GenAI`** (official) | 2.2.0 (2026-11-16) | Apache-2.0 | `new Client().AsIChatClient("model")`; depends on MEAI Abstractions ≥ 10.6.0. Community alternatives exist (`Mscc.GenerativeAI.Microsoft`, `Google_GenerativeAI.Microsoft`). |

All five routes go through one `IChatClient`, so the provider can be a configuration choice.

### Semantic Kernel vs Microsoft Agent Framework
- **Microsoft Agent Framework 1.0 went GA in early April 2026** (.NET and Python). **Semantic Kernel and AutoGen are in maintenance mode** (bug and security fixes only; secondary sources say SK 1.x is supported at least to ~April 2027).
- Both sit on top of MEAI. For single-shot "advisory text in → typed JSON out" analysis neither is needed: `IChatClient` + `GetResponseAsync<T>` + caching/OTel middleware is the whole stack. Agent Framework becomes relevant only for multi-step tool-using agents or workflows.

### Local open-weight models and schema-constrained output
- **Ollama supports JSON-schema-constrained output**: pass a JSON schema in the `format` field of `/api/chat` (since v0.5); it is compiled to a grammar that constrains sampling, so output is always syntactically valid. Also exposed through the OpenAI-compatible endpoint as `response_format`. Ollama docs recommend temperature 0 and repeating the schema in the prompt (the model does not see the grammar). Not available on Ollama Cloud. Constrained decoding guarantees shape, not truth; keep server-side validation and a bounded retry.
- Thinking-capable models (Gemma 4, Qwen 3.x, gpt-oss) must have thinking disabled or handled, otherwise JSON can land in the reasoning channel (secondary source).
- Models currently listed in the Ollama library and credible for this task (tags from ollama.com/library; memory figures are rough Q4 estimates, **UNVERIFIED**):

| Model | Sizes | Fits | Notes |
|---|---|---|---|
| `gemma4` | e2b, e4b, **12b**, 26b, 31b | 12b ≈ 8–10 GB; e4b ≈ 4–6 GB | tools + thinking + vision; Gemma licence terms (not OSI) |
| `qwen3.5` | 0.8b, 2b, **4b**, **9b**, 27b, 35b | 9b ≈ 6–8 GB; 4b ≈ 3–4 GB | tools + thinking; Apache-2.0 family |
| `qwen3.6` / `qwen3.8` | 27b, 35b | 17–22 GB | strongest local picks for structured output per a May 2026 guide (single secondary source) |
| `gpt-oss` | **20b**, 120b | 20b ≈ 13–16 GB | Apache-2.0, tools + thinking |
| `granite4.1` | 3b, **8b**, 30b | 8b ≈ 5–6 GB | Apache-2.0, enterprise-oriented, tools |
| `mistral-small3.2` | 24b | ≈ 15–16 GB | Apache-2.0, tools |
| `phi4` / `phi4-mini` | 14b / 3.8b | ≈ 9 GB / 3 GB | MIT; older (2025) |

  No independent benchmark for *security-advisory* extraction was found; model choice must be validated with a small golden set. Sub-7B models are consistently reported as weak for extraction quality even when the grammar forces valid JSON.

Sources:
- https://www.nuget.org/packages/Microsoft.Extensions.AI , https://learn.microsoft.com/en-us/dotnet/ai/microsoft-extensions-ai , https://learn.microsoft.com/en-us/dotnet/ai/quickstarts/structured-output
- https://platform.claude.com/docs/en/api/sdks/csharp , https://www.nuget.org/packages/Anthropic
- https://www.nuget.org/packages/OllamaSharp , https://www.nuget.org/packages/Google.GenAI , https://www.nuget.org/packages/Microsoft.Extensions.AI.OpenAI
- https://docs.ollama.com/capabilities/structured-outputs , https://ollama.com/library
- https://visualstudiomagazine.com/articles/2026/04/06/microsoft-ships-production-ready-agent-framework-1-0-for-net-and-python.aspx

**Implication for the plan:** Define one internal `IAdvisoryAnalyzer` port backed by MEAI `IChatClient` with `GetResponseAsync<T>`, distributed-cache and OpenTelemetry middleware; ship provider adapters as configuration (Ollama via OllamaSharp as the self-hosted default, OpenAI-compatible endpoint, Anthropic, Gemini). Skip Semantic Kernel (maintenance mode) and Agent Framework. Make AI optional and always validate model output server-side.

---

## 6. Library licensing landmines

NuGet pages were checked today for version and current licence status. The "last free version" column and revenue thresholds come from prior knowledge of the 2025 relicensing announcements and were not re-read today (**UNVERIFIED** detail).

| Library | Current (date) | Status today | Last OSS line (prior knowledge) |
|---|---|---|---|
| **MediatR** | 15.0.0 (2026-11-12) | **Commercial** (Lucky Penny Software). Requires a licence key (`MEDIATR_LICENSE_KEY`); free community tier for small orgs | 12.x, Apache-2.0 |
| **AutoMapper** | 17.0.0 (2026-11-12) | **Commercial**, same vendor and key model (`AUTOMAPPER_LICENSE_KEY`) | 14.x, MIT |
| **MassTransit** | 9.2.6 (2026-11-10) | **Commercial**: "a commercial product that must be licensed" (Massient) | 8.x, Apache-2.0 (maintenance status not checked) |
| **FluentAssertions** | 8.11.0 (2026-09-14) | **Commercial for commercial use** (Xceed); free for OSS / non-commercial | 7.x, Apache-2.0 — still patched (7.2.2, 2026-03-16) |
| **Moq** | 4.21.0 (2026-09-24) | BSD-3-Clause, free. SponsorLink was removed in 4.20.2 (2023); reputational issue only | — |
| **ImageSharp** | 4.1.3 (2026-10-09) | **Six Labors Split License 1.0**: free for OSS projects and small companies, paid otherwise. Whether v4 enforces a build-time key was not confirmed | 2.x, Apache-2.0 |
| **Duende IdentityServer** | 8.0.11 (2026-11-16) | **Commercial**, source-available; free Community Edition for qualifying small companies / non-profits | IdentityServer4 (EOL) |
| **FluentValidation** | 12.2.0 (2026-10-10) | **Still free**, Apache-2.0; author asks commercial users for sponsorship | — |
| **TickerQ** | 10.5.1 / 11.5.1 | **Relicensed, source-available** (see section 4) | older MIT/Apache releases |
| **Respawn** | 7.0.0 (2025-11-30) | Apache-2.0, free (same author as MediatR, but not relicensed) | — |

Free alternatives (all verified on NuGet today unless noted):

| Need | Alternative | Version (date) | Licence |
|---|---|---|---|
| Mapping | **Riok.Mapperly** (source generator) or hand-written mappers | 4.3.1 (2025-12-22) | Apache-2.0 |
| Mediator / CQRS dispatch | **Mediator** (martinothamar, source-generated), **Wolverine**, or plain handler classes + DI | Mediator.Abstractions 3.0.2 (2026-03-22); WolverineFx 6.64.2 | MIT (Mediator licence not shown on NuGet page; MIT per repo — **UNVERIFIED** today) / MIT |
| Assertions | **AwesomeAssertions** (fork of FluentAssertions 7) or **Shouldly** | 9.6.0 (2026-08-20) / 4.3.0 (2025-01-23) | Apache-2.0 / BSD-3-Clause |
| Mocking | **NSubstitute** | 6.2.0 (2026-08-11) | BSD-3-Clause |
| OAuth2/OIDC server | **OpenIddict** (only if an OIDC server is needed at all; plain ASP.NET Core Identity cookies need none) | 7.9.0 (2026-11-14) | Apache-2.0 |
| Messaging | Wolverine, Rebus, DotNetCore.CAP, or own outbox | see section 4 | MIT |
| Imaging | SkiaSharp (MIT) or Magick.NET (Apache-2.0) — not checked today | — | — |

Notes for an OSS project specifically:
- "Free for OSS" clauses (FluentAssertions 8, ImageSharp, Lucky Penny community tier) cover *your* repository but **not downstream companies that self-host your software commercially** — they may need their own licence. For a self-hosted product this is the real landmine; avoid such dependencies in shipped code. Test-only dependencies are lower risk but still simpler to avoid.
- Add a CI licence gate (e.g. an allow-list of SPDX expressions over `dotnet list package --include-transitive`) so a major-version bump cannot silently pull in a relicensed package.

Sources:
- https://www.nuget.org/packages/MediatR , https://www.nuget.org/packages/AutoMapper , https://www.nuget.org/packages/MassTransit
- https://www.nuget.org/packages/FluentAssertions , https://www.nuget.org/packages/SixLabors.ImageSharp , https://www.nuget.org/packages/Duende.IdentityServer
- https://www.nuget.org/packages/FluentValidation , https://www.nuget.org/packages/AwesomeAssertions , https://www.nuget.org/packages/Riok.Mapperly

**Implication for the plan:** Ban MediatR, AutoMapper, MassTransit 9, FluentAssertions 8, ImageSharp, Duende and TickerQ in the dependency policy from day one; use plain handlers (or Wolverine), Mapperly or manual mapping, AwesomeAssertions/Shouldly, NSubstitute, and FluentValidation or built-in `AddValidation`. Enforce with an automated licence check in CI.

---

## 7. Web Push (VAPID) from .NET

| Library | Version (date) | Licence | Assessment |
|---|---|---|---|
| **`WebPush`** (web-push-csharp, coryjthompson) | 1.0.13 (2026-04-28); previous release 1.0.12 was **2021-07-03** | MPL-2.0 per repo (not shown on NuGet page — **UNVERIFIED** today) | Revived after ~5 years of silence. Targets net8.0 / netstandard2.1 / net48, but still depends on **`Portable.BouncyCastle` ≥ 1.9.0** (the legacy, superseded BouncyCastle package). Most downloaded, weakest maintenance signal. |
| **`Lib.Net.Http.WebPush`** (tpeczek) | 3.3.1 (2025-03-09) | MIT | Cleaner design (`PushServiceClient` on `HttpClient`/`IHttpClientFactory`, VAPID token caching); on .NET 5+ it uses built-in crypto (BouncyCastle only for netstandard2.0/net4x). ~20 months since last release, but the protocol (RFC 8030/8291/8292) is frozen. |
| Hand-rolled | — | — | VAPID (ES256 JWT) + `aes128gcm` content encryption is ~200 lines with `System.Security.Cryptography` (ECDH P-256, HKDF, AES-GCM). Viable if dependency risk matters. |

iOS / Safari requirements (secondary sources; core rules unchanged since iOS 16.4):
- Web Push works on iOS/iPadOS **16.4+ only for web apps added to the Home Screen** (Share → Add to Home Screen); not in a normal Safari tab, and not in other iOS browsers' tabs.
- HTTPS, a service worker, a web app manifest (`display: standalone` — on iOS 26 every Home Screen site opens as a web app by default, but keep the manifest), and the permission prompt must be triggered by a **user gesture** inside the installed app.
- Standard VAPID Web Push; no Apple developer account or APNs certificate needed. Push endpoints are `*.push.apple.com`.
- macOS Safari 16+ supports standard Web Push in the browser without installation.
- EU status: sources conflict. Apple announced and then reversed the removal of Home Screen web apps in the EU in early 2024 (prior knowledge); not re-confirmed from an Apple source today — **UNVERIFIED**.

Declarative Web Push:
- Now part of the **W3C Push API Working Draft (dated 2026-11-16)**: a push message whose payload is JSON with `"web_push": 8030` and a `notification` object (`title` and `navigate` required). The browser shows the notification without running a service worker.
- Shipped in **Safari / WebKit: iOS & iPadOS 18.4 (March 2025), macOS Safari 18.5 (May 2025)**. Mozilla's standards position is positive. **No confirmation found that Chrome/Edge or Firefox ship it** — **UNVERIFIED**.
- It is backwards compatible: a browser without support delivers the same JSON to the service worker's `push` event.

Sources:
- https://www.nuget.org/packages/WebPush , https://www.nuget.org/packages/Lib.Net.Http.WebPush
- https://www.w3.org/TR/push-api/
- https://aimtell.com/blog/state-of-declarative-web-push-2026 (secondary) , https://developer.apple.com/videos/play/wwdc2025/235

**Implication for the plan:** Use `Lib.Net.Http.WebPush` (MIT, HttpClientFactory-friendly) behind an `INotificationChannel` port, or hand-roll VAPID if a zero-dependency path is preferred. Send payloads in the declarative JSON shape and keep a service-worker `push` handler as fallback; document the iOS "install to Home Screen first" requirement in the UI.

---

## 8. Email

- **MailKit 5.0.0** (2026-11-10), MIT, targets net8.0 / netstandard2.0 / net462; actively maintained (MimeKit/MailKit by Jeffrey Stedfast). Supports SMTP with STARTTLS/implicit TLS, OAuth2 (XOAUTH2/OAUTHBEARER), DKIM signing via MimeKit, proper MIME construction.
- **`System.Net.Mail.SmtpClient`**: still ships and works in .NET 10, not formally `[Obsolete]`, but Microsoft's API docs state it is **not recommended for new development** because it "doesn't support many modern protocols", and point to MailKit. Platform-compat analyzer rule **DE0005** flags it. Practical gaps: no OAuth2, no implicit-TLS (port 465) support, weak pooling/async behaviour.
- For local/dev and tests: a Mailpit or smtp4dev container in Compose.

Sources:
- https://www.nuget.org/packages/MailKit
- https://learn.microsoft.com/en-us/dotnet/api/system.net.mail.smtpclient

**Implication for the plan:** Use MailKit 5.x behind an `IEmailSender` abstraction (which also satisfies ASP.NET Core Identity's `IEmailSender<TUser>`); do not use `System.Net.Mail.SmtpClient`.

---

## 9. Observability

- **OpenTelemetry .NET 1.21.0** (2026-11-11), Apache-2.0. From 1.21 the packages target **net10.0, netstandard2.0, net462** (net8/net9 assets dropped, net11 added). Traces, metrics and logs (via `ILogger` → OTLP) are all stable signals in the .NET SDK (stable for several years; not re-confirmed on the releases page today). Recent breaking nits: OTLP exporter `HttpClient`s no longer follow redirects (1.20.1); span status descriptions are truncated (1.21).
- Packages: `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.21.0, instrumentation for ASP.NET Core, HttpClient, runtime; `Npgsql.OpenTelemetry` for database spans; EF Core and Quartz/Wolverine emit their own `ActivitySource`s.
- **Standalone Aspire Dashboard**: image **`mcr.microsoft.com/aspire/dashboard`** (pin a version tag rather than `latest`), MIT. UI on 18888; OTLP gRPC on 18889 and OTLP/HTTP on 18890 inside the container (commonly mapped to 4317/4318). **Auth:** browser-token login by default (token printed in container logs); anonymous mode only via `ASPIRE_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS=true`; the telemetry API is API-key protected by default; OTLP ingestion can require an API key header and the UI can use OIDC (config key names not re-read today — **UNVERIFIED**). **Storage is in-memory by default** with telemetry limits (optional persistence needs a mounted volume). Microsoft's wording: "designed for development and short-term diagnostics rather than production telemetry retention."
- **Logging**: no single official "use X" statement was found today. Current mainstream practice: `Microsoft.Extensions.Logging` with source-generated `[LoggerMessage]`, `AddJsonConsole()` for container stdout, and the OpenTelemetry logging provider for OTLP export — zero extra dependencies. **Serilog** remains healthy (Serilog.AspNetCore 11.0.0, 2026-11-18, Apache-2.0) and is still worth it for rich sinks/enrichers or file rolling; it is no longer needed just to get structured JSON or OTLP.
- **GenAI semantic conventions**: moved out of the main semantic-conventions repo into **`open-telemetry/semantic-conventions-genai`** (spans, metrics, events, agent spans, MCP, provider-specific pages for OpenAI/Anthropic/Bedrock/Azure). No tagged release or stability statement found there — treat as **still unstable/"Development"** (**UNVERIFIED**). MEAI's `UseOpenTelemetry()` emits them; attribute names may still change, and prompt/response content capture is opt-in (`EnableSensitiveData`).

Sources:
- https://github.com/open-telemetry/opentelemetry-dotnet/releases , https://www.nuget.org/packages/OpenTelemetry.Exporter.OpenTelemetryProtocol
- https://aspire.dev/dashboard/standalone/
- https://opentelemetry.io/docs/specs/semconv/gen-ai/ , https://github.com/open-telemetry/semantic-conventions-genai

**Implication for the plan:** Instrument API and Worker with OpenTelemetry 1.21 exporting OTLP; ship the Aspire Dashboard as an *optional* Compose profile for operators (with its token auth left on), and let serious deployments point `OTEL_EXPORTER_OTLP_ENDPOINT` at their own collector/Grafana stack. Use built-in JSON console logging plus OTLP logs; add Serilog only if a concrete sink need appears.

---

## 10. Containers

- **Images** (`mcr.microsoft.com/dotnet/aspnet` for the API, `/runtime` for the Worker, `/runtime-deps` for self-contained/AOT). .NET 10 default OS is **Ubuntu 24.04 "noble"**. Tags (current patch 10.0.15):
  - `10.0` / `10.0-noble` — full Ubuntu, has shell and apt.
  - **`10.0-noble-chiseled`** — distroless-style Ubuntu: no shell, no package manager, non-root.
  - **`10.0-noble-chiseled-extra`** — chiseled plus ICU and tzdata. Needed for culture-aware formatting and for **time-zone-aware cron** (`TimeZoneInfo.FindSystemTimeZoneById`) — prior knowledge, verify in a smoke test.
  - `10.0-noble-chiseled-composite` (aspnet only; faster startup, larger).
  - `10.0-azurelinux3.0-distroless` (+ `-extra`) — Microsoft's own distroless base.
  - `10.0-alpine` / `10.0-alpine3.24` (musl; `-extra` variant for ICU).
- **Multi-arch**: amd64 and arm64 for all variants; arm32v7 for noble/chiseled/alpine (not Azure Linux distroless). The un-suffixed tags are multi-arch manifest lists (long-standing behaviour; the README only shows per-arch tag tables).
- **Non-root**: since .NET 8 all images define user `app` (UID 1654, `$APP_UID`) and listen on **8080**; chiseled/distroless images run as that user by default, the full images need `USER $APP_UID` (prior knowledge; the README confirms "non-root user by default" for distroless and port 8080).
- **HEALTHCHECK without curl** (chiseled has no shell, curl or wget; only exec-form commands work):
  1. Preferred: a tiny self-probe. Either the app itself accepts a `--healthcheck` argument that does an HTTP GET to `http://127.0.0.1:8080/healthz` and exits 0/1 (`HEALTHCHECK CMD ["dotnet","Api.dll","--healthcheck"]`), or a small Native AOT helper binary copied into the image (starts in milliseconds, avoids spinning up a second runtime every interval).
  2. Define it in `compose.yaml` (`healthcheck: test: ["CMD", "/app/healthcheck"]`) rather than in the image, so it also works with SDK-built images.
  3. Worker Service (no HTTP port): heartbeat file touched by a hosted service and checked by the same helper, or expose a minimal Kestrel health endpoint.
  4. Avoid copying busybox/curl into the image — it defeats the point of chiseled.
- **`dotnet publish /t:PublishContainer` vs Dockerfile**: the SDK container tooling is mature — `ContainerFamily=noble-chiseled-extra`, `ContainerRepository`, `ContainerImageTags`, and multi-arch via multiple `RuntimeIdentifiers` (supported since SDK 8.0.405 / 9.0.102). No Docker daemon needed in CI. Limits: no arbitrary `RUN` steps, and no place for a `HEALTHCHECK` instruction (secondary sources; put the health check in Compose). A classic multi-stage Dockerfile is more familiar to OSS contributors and allows `docker compose build` straight from a clone.

Sources:
- https://github.com/dotnet/dotnet-docker/blob/main/README.aspnet.md , https://github.com/dotnet/dotnet-docker/blob/main/README.runtime-deps.md
- https://learn.microsoft.com/en-us/dotnet/core/containers/publish-configuration

**Implication for the plan:** Base both services on `10.0-noble-chiseled-extra` (non-root, tzdata/ICU present), publish amd64 + arm64, and implement health checks as an in-image exec probe declared in Compose. Keep a conventional multi-stage Dockerfile as the canonical build so `docker compose up --build` works for contributors; SDK container publish is an optional CI optimisation.

---

## 11. Resilience / outbound HTTP

- **`Microsoft.Extensions.Http.Resilience`**: 11.0.0 (2026-11-10), MIT, GA since .NET 8 (the package follows dotnet/extensions versioning; the latest 10.x number was not looked up). Pipeline details below are from prior knowledge of the docs, not re-read today. Built on **Polly v8** (`Polly.Core` 8.9.1, 2026-11-07, BSD-3-Clause — still v8, no v9). `AddStandardResilienceHandler()` gives rate limiter → total timeout → retry (exponential + jitter, honours `Retry-After`) → circuit breaker → attempt timeout; `AddResilienceHandler("name", b => ...)` for custom pipelines; `AddStandardHedgingHandler()` for hedging. The old `Microsoft.Extensions.Http.Polly` package is legacy.
- **Outbound rate limiting**: `System.Threading.RateLimiting` is in the BCL (`TokenBucketRateLimiter`, `SlidingWindowRateLimiter`, `FixedWindowRateLimiter`, `ConcurrencyLimiter`, `PartitionedRateLimiter<T>`). Two idiomatic ways: Polly's `AddRateLimiter(...)` strategy inside the resilience handler (it wraps these types), or a small `DelegatingHandler` that awaits `limiter.AcquireAsync()`. Use one named limiter per upstream host (e.g. NVD API: 5 req/30 s without key, 50 with key — figures from prior knowledge, re-check against NVD docs). Limiters are in-process; with a single Worker instance that is sufficient, with several you need a Postgres-backed lease/token table.
- **Conditional GET**: `HttpClient` has **no built-in HTTP cache**. Best practice is manual: persist `ETag` and `Last-Modified` per feed URL in Postgres; send `If-None-Match` (and `If-Modified-Since` as fallback) via `request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(etag))`; treat **304** as "unchanged" (make sure the resilience pipeline and `EnsureSuccessStatusCode` do not treat it as a failure); keep weak validators verbatim (`W/"..."`); additionally store a content hash, because many advisory feeds send no or unstable validators; enable `AutomaticDecompression`. Also honour `Retry-After` and `Cache-Control: max-age` when scheduling the next poll.
- Use `IHttpClientFactory` typed/named clients with `SocketsHttpHandler.PooledConnectionLifetime`; set an identifying `User-Agent` (several CERT/NVD endpoints require it).

Sources:
- https://www.nuget.org/packages/Microsoft.Extensions.Http.Resilience , https://www.nuget.org/packages/Polly.Core
- https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience (reference doc; not fetched today)

**Implication for the plan:** One named `HttpClient` per source with the standard resilience handler plus a per-host token-bucket limiter, and a small `FeedFetchState` table (ETag, Last-Modified, content hash, next-poll-after) driving conditional requests. No extra caching library needed.

---

## 12. Testing stack

| Tool | Version (date) | Licence | Notes |
|---|---|---|---|
| **xUnit v3** | `xunit.v3` 4.0.3 (2026-11-09) | Apache-2.0 | Mainstream default. Tests are standalone executables; 4.0 dropped MTP v1 and uses **Microsoft.Testing.Platform v2** by default. |
| **TUnit** | 1.86.6 (2026-11-17) | MIT | GA (1.x), source-generated, MTP-native, AOT-friendly, built-in DI/dependency ordering. Very fast-moving (near-daily releases), 8.7M downloads — credible, but single-maintainer velocity is a churn risk. |
| **NUnit** | 5.0.0 (2026-09-27) | MIT | Healthy; new major two months old. |
| **Microsoft.Testing.Platform** | 2.5.3 (2026-11-11) | MIT | The strategic runner. On the **.NET 10 SDK, `dotnet test` in MTP mode is opt-in via `global.json`** (`"test": { "runner": "Microsoft.Testing.Platform" }`); MS docs say the old VSTest-target bridge for MTP is no longer supported on SDK 10+. Misspelt keys silently fall back to VSTest (secondary source). |
| **Testcontainers for .NET** | `Testcontainers.PostgreSql` 4.17.0 (2026-11-16) | MIT | net8/9/10; standard for integration tests against real Postgres. |
| **Verify** | `Verify.XunitV3` 33.6.0 (2026-11-18) | MIT | Snapshot testing; releases almost daily (pin and batch upgrades). Good for parser output and AI-prompt/DTO snapshots. |
| **Respawn** | 7.0.0 (2025-11-30) | Apache-2.0 | DB reset between tests; still free. |
| **NetArchTest.Rules** | 1.3.2 (**2021-05-23**) | MIT per repo | **Unmaintained.** Fork `NetArchTest.eNhancedEdition` 1.4.5 (2025-06-04, MIT) is alive but slow. |
| **ArchUnitNET** | `TngTech.ArchUnitNET` 0.13.5 (2026-10-26) | Apache-2.0 | Actively maintained (TNG); richer model, still 0.x. Better choice for module-boundary rules in a modular monolith. |
| Assertions / mocks | AwesomeAssertions 9.6.0 or Shouldly 4.3.0; NSubstitute 6.2.0 | Apache-2.0 / BSD-3 | See section 6 (avoid FluentAssertions 8). |

Sources:
- https://www.nuget.org/packages/xunit.v3 , https://www.nuget.org/packages/TUnit , https://www.nuget.org/packages/NUnit
- https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-migration-from-v1-to-v2
- https://www.nuget.org/packages/Testcontainers.PostgreSql , https://www.nuget.org/packages/TngTech.ArchUnitNET , https://www.nuget.org/packages/NetArchTest.Rules

**Implication for the plan:** xUnit v3 (4.x) on Microsoft.Testing.Platform with the `global.json` runner setting, Testcontainers + Respawn for Postgres integration tests, Verify for snapshots, ArchUnitNET for module-boundary tests, AwesomeAssertions/Shouldly and NSubstitute. TUnit is a legitimate alternative if the team prefers it; avoid NetArchTest.Rules.

---

## 13. Parsing and domain libraries

| Need | Library | Version (date) | Licence | Notes |
|---|---|---|---|---|
| RSS/Atom | **`System.ServiceModel.Syndication`** | 11.0.0 (2026-11-10); 10.0.x line exists | MIT | Official Microsoft package, serviced with each .NET release; targets net10.0/netstandard2.0. Strict parser — real-world feeds with bad dates or stray elements throw unless you customise `Rss20FeedFormatter`/`XmlReader` handling. Always use `XmlReaderSettings { DtdProcessing = Prohibit, XmlResolver = null }` and a size limit. |
| RSS/Atom (lenient) | **`CodeHollow.FeedReader`** | 2.1.1 (2026-11-04) | MIT per repo (not shown on NuGet — **UNVERIFIED** today) | Recently revived (2.x targets net8.0); tolerant of malformed feeds, handles RSS 0.91–2.0, Atom, some extensions. |
| HTML sanitisation | **`HtmlSanitizer`** (Ganss.Xss, mganss) | 11.0.26 (2026-11-09) | MIT | Actively maintained, AngleSharp ≥ 1.10-based allow-list sanitiser. De-facto standard. |
| NuGet versions/ranges | **`NuGet.Versioning`** | 8.0.0 (2026-11-10) | Apache-2.0 | `NuGetVersion`, `VersionRange` — NuGet ecosystem semantics only. |
| SemVer 2.0 + npm ranges | **`Semver`** | 3.0.0 (2024-10-23) | MIT | `SemVersion`, `SemVersionRange.ParseNpm`. Stable; low release frequency is fine for a frozen spec. |
| purl | **`packageurl-dotnet`** | 2.0.1 (2026-06-29) | MIT | Official package-url org; parse/build purls (net8.0, netstandard2.0). No `vers` range support. |
| CVSS | **`CvssSuite`** | 1.1.0 (2024-11-10) | MIT | Claims CVSS 2, 3.0, 3.1 and **4.0**; netstandard1.1; two years without a release. Validate against FIRST's published test vectors before trusting scores. `Cvss.Net` 0.2.0 is v3-only. |
| CPE 2.3 | **none found** | — | — | No credible NuGet package. Parsing CPE 2.3 formatted strings (11 colon-separated components with escaping) and NISTIR 7696 name matching is small enough to implement in-house (reference implementations: Java `CPE-Parser`, Python `cpe`, Go `claircore`). |
| Version-range matching across ecosystems (`vers`, Maven, PyPI, deb/rpm) | **none found** | — | — | Must be written per ecosystem as needed; start with semver/NuGet/"generic numeric" and CPE version-start/end ranges from NVD. |
| CSAF 2.0 / 2.1 | **none found** | — | — | No .NET library surfaced. CSAF is JSON with a published JSON Schema: generate or hand-write `System.Text.Json` records for the subset you need (document, product_tree, vulnerabilities, scores, remediations) and optionally validate with `JsonSchema.Net` (MIT — not checked today). `csaf-rs` and the BSI `csaf` Go tools are useful references. |

Sources:
- https://www.nuget.org/packages/System.ServiceModel.Syndication , https://www.nuget.org/packages/CodeHollow.FeedReader , https://www.nuget.org/packages/HtmlSanitizer
- https://www.nuget.org/packages/NuGet.Versioning , https://www.nuget.org/packages/Semver , https://www.nuget.org/packages/packageurl-dotnet , https://www.nuget.org/packages/CvssSuite

**Implication for the plan:** Buy the commodity pieces (feeds, HTML sanitising, semver, purl) and plan to **build** the security-domain core yourself: CPE parsing/matching, cross-ecosystem version-range evaluation and CSAF models are in-house modules with their own test corpora. Treat CVSS as "library behind an interface, verified against FIRST vectors" with a fallback to an in-house v3.1/v4.0 calculator.

---

## 14. Aspire as a local dev orchestrator

- **Current version: Aspire 17.0.0, released today (2026-11-18)**; previous line 13.6.x (13.6.5 on 2026-11-10). The product is now just "Aspire" (repo `microsoft/aspire`, docs at aspire.dev), MIT. `Aspire.Hosting.AppHost` 17.0.0 targets net10.0 and net11.0. The reason for the 13 → 17 version jump was not found (**UNVERIFIED**); a same-day major release is a reason to start on 13.6.x or wait for 17.0.x patches.
- What it gives a Compose-deployed project in development: one `dotnet run`/`aspire run` starts Postgres (+ pgvector image), Mailpit, Ollama, the API and the Worker with wired connection strings, health-gated startup (`WaitFor`), the dashboard with traces/logs/metrics, and `Aspire.Hosting.Testing` for end-to-end tests. It can also emit Docker Compose artefacts (`Aspire.Hosting.Docker` / `aspire publish`).
- Costs: a second description of the topology next to the hand-written `compose.yaml` (drift risk, unless Compose is generated from the AppHost); fast release cadence with frequent majors (9 → 13 → 17 in 14 months); contributors need the Aspire CLI/workload and a container runtime anyway; the `ServiceDefaults` project is just ordinary OpenTelemetry/health-check code that you can own without Aspire.
- A plain `compose.dev.yaml` (Postgres, Mailpit, Aspire Dashboard, optional Ollama) plus `dotnet watch` gives ~80% of the benefit with zero extra abstraction, and dev stays identical to prod topology.

Sources:
- https://github.com/microsoft/aspire/releases , https://www.nuget.org/packages/Aspire.Hosting.AppHost
- https://aspire.dev/dashboard/standalone/

**Implication for the plan:** Do not make Aspire AppHost a required part of the architecture. Keep Docker Compose as the single source of truth (dev and prod profiles) and adopt only the standalone Aspire Dashboard container; an AppHost can be added later as an optional developer convenience without touching the services.

---

## Consolidated list of UNVERIFIED items

1. RLS implementation details with EF Core/Npgsql (session vs transaction scope, pool reset behaviour) — design knowledge, needs a prototype.
2. Quartz.NET 4.x declarative retry/backoff capabilities; behaviour of `EnlistTransaction` with a DI-scoped `DbContext`.
3. Hangfire.PostgreSql: no documented way to enqueue inside an EF Core transaction was found (absence of evidence).
4. Licence strings not shown on NuGet pages today: OllamaSharp (MIT?), `WebPush` (MPL-2.0?), CodeHollow.FeedReader (MIT?), Mediator (MIT?), NetArchTest.Rules (MIT?).
5. "Last free version" lines and revenue thresholds for MediatR / AutoMapper / MassTransit / FluentAssertions / ImageSharp / Duende; whether ImageSharp 4 enforces a build-time licence key; maintenance status of MassTransit 8.
6. Memory footprints and extraction quality of the local models; no security-domain benchmark found.
7. Declarative Web Push support in Chrome/Edge/Firefox; current EU status of iOS Home Screen web apps.
8. Stability level of the OpenTelemetry GenAI semantic conventions; exact Aspire Dashboard config keys for OTLP API-key and OIDC auth.
9. Chiseled image details taken from prior knowledge: UID 1654 default user, `-extra` containing tzdata + ICU, un-suffixed tags being multi-arch manifests; absence of HEALTHCHECK support in SDK container publish.
10. Reason for Aspire's 13 → 17 version jump and what 17.0 changes.
11. .NET 11 support status of third-party libraries (not checked; only relevant if the plan moves off .NET 10).
12. Quality of the small new Postgres job-queue libraries (NexJob, BackWave, Cabal, TaskTower).
