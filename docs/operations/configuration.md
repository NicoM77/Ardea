# Configuration

Settings come from environment variables with the prefix `ARDEA_` (nested names with `__`, e.g.
`ARDEA_SMTP__HOST`). Settings marked *admin* can also be changed in the administration; a value set in the
environment wins and is shown there as locked.

| Variable | Default | Meaning |
|---|---|---|
| `ARDEA_PUBLIC_URL` | (required) | The address users open, e.g. `https://ardea.example.com`. Links in e-mails, cookie names and the origin check follow it. |
| `ARDEA_MASTER_KEY` | (required) | 32 bytes, base64. Encrypts secrets in the database. Back it up separately. |
| `ARDEA_DB_HOST`, `ARDEA_DB_PORT`, `ARDEA_DB_NAME` | `db`, `5432`, `ardea` | The database. |
| `ARDEA_DB_APP_USER`, `ARDEA_DB_APP_PASSWORD` | `ardea_app`, (required) | The application role used by API and worker; owns nothing. |
| `ARDEA_DB_OWNER_USER`, `ARDEA_DB_OWNER_PASSWORD` | `ardea_migrator`, (required for `migrate` and `org`) | The owner role. Only the `migrate` service knows it. |
| `ARDEA_SETUP_TOKEN` | (generated) | One-time token for the first user. |
| `ARDEA_REGISTRATION` | `invite` | *admin*. `invite`, `open` or `closed`. |
| `ARDEA_TRUSTED_PROXIES` | loopback plus the Docker address pools | Proxies whose `X-Forwarded-For` and `X-Forwarded-Proto` are trusted. Narrow it if you publish port 8080 beyond the host. |
| `ARDEA_HTTP_PORT` | `8080` | Port of the API inside the container. |
| `ARDEA_SMTP__HOST`, `__PORT`, `__SECURITY`, `__USER`, `__PASSWORD`, `__FROM` | none, `587`, `starttls` | *admin*. E-mail. Without SMTP, invitations show a link to pass on and passwords are reset with `ardea admin reset-password`. |
| `ARDEA_AI__PROVIDERS__<Name>__KIND`, `__ENDPOINT`, `__MODEL`, `__API_KEY`, `__LOCAL` | none | Named language model providers (master plan 7.8). `KIND` is `openai` (any OpenAI-compatible endpoint: vLLM, llama.cpp, LM Studio, Ollama's `/v1`, hosted services), `ollama` (Ollama's own API) or `anthropic`. `ENDPOINT` is required for `openai` and `ollama`, `API_KEY` for `anthropic`. `LOCAL=true` marks a provider whose data stays under your control; organisations with the policy «local only» use only those. Without `LOCAL`, only `ollama` counts as local; `anthropic` never does. Without providers, Ardea assesses with rules and templates alone. A wrong provider stops the start with the reason; *Administration → AI* lists them and tests the connection. |
| `ARDEA_AI__PARALLELISM` | `1` | Assessments that wait for a language model at the same time, per worker. One suits a local model on one GPU or a CPU. |
| `ARDEA_AI__CALL_TIMEOUT_SECONDS` | `120` | Time limit of one call to a model. A call over it counts as failed; the assessment tries once more, then the rules alone apply. |
| `ARDEA_AI__DAILY_TOKENS` | `2000000` | Tokens (in and out) per UTC day for the whole instance. Once used up, new alerts are assessed with rules and templates alone until the next day. |
| `ARDEA_AI__DAILY_TOKENS_PER_ORGANIZATION` | `100000` | The same per organisation. One assessment needs about 2 000 to 3 600 tokens; the default covers about 40 a day. |
| `ARDEA_RATE_LIMIT__AUTH_PER_MINUTE` | `10` | Sign-in, registration and reset attempts per client network and minute; read-only lookups get six times as many. |
| `ARDEA_PASSWORD_HASH_ITERATIONS` | Identity's default | PBKDF2 iterations for new password hashes. |
| `ARDEA_INTEL__BACKFILL_DAYS` | `180` | Look-back of the first load. |
| `ARDEA_SCRATCH_DIR` | `/scratch` | Working space for large archives (a volume in Compose). |
| `ARDEA_DATA_DIR` | `/app/data` | Master data shipped with the image (catalogue). |
| `ARDEA_WORKER_ID` | host name | Name of a worker in the job leases; set it when running several workers. |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | none | Send traces, metrics and logs to an OpenTelemetry collector. |

GitHub Enterprise Server hosts are added by the instance administrator in the administration, not in the
environment; organisations choose from them.
