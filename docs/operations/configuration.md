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
| `ARDEA_WEBHOOK_INTERNAL_HOSTS` | none | *admin*. Hosts that webhook targets may use although they resolve to a private address (10/8, 172.16/12, 192.168/16, 100.64/10, fc00::/7), comma-separated. Loopback, link-local and cloud metadata addresses are never reachable. Without it, a webhook into the private network is refused (master plan 9.5). |
| `ARDEA_VAPID__PUBLIC_KEY`, `ARDEA_VAPID__PRIVATE_KEY` | generated | The instance's Web Push key pair (P-256, base64url: the 65-byte uncompressed public point and the 32-byte private key), set together or not at all. Without them the instance generates a pair on its first start and keeps it encrypted in the database. A new pair makes every device subscribe again. |
| `ARDEA_VAPID__SUBJECT` | the public address | The contact the push services see in every message, `mailto:` or `https:`. |
| `ARDEA_PUSH_SERVICE_HOSTS` | none | Further push services a browser's subscription may name, comma-separated, e.g. a self-hosted one. Google's, Mozilla's, Apple's and Microsoft's are always accepted. Internal addresses are never reachable for push. |
| `ARDEA_AI__PROVIDERS__<Name>__KIND`, `__ENDPOINT`, `__MODEL`, `__API_KEY`, `__LOCAL` | none | Named language model providers (master plan 7.8). `KIND` is `openai` (any OpenAI-compatible endpoint: vLLM, llama.cpp, LM Studio, Ollama's `/v1`, hosted services), `ollama` (Ollama's own API) or `anthropic`. `ENDPOINT` is required for `openai` and `ollama`, `API_KEY` for `anthropic`. `LOCAL=true` marks a provider whose data stays under your control; organisations with the policy «local only» use only those. Without `LOCAL`, only `ollama` counts as local; `anthropic` never does. Without providers, Ardea assesses with rules and templates alone. A wrong provider stops the start with the reason; *Administration → AI* lists them and tests the connection. |
| `ARDEA_AI__PARALLELISM` | `1` | Assessments that wait for a language model at the same time, per worker. One suits a local model on one GPU or a CPU. |
| `ARDEA_AI__CALL_TIMEOUT_SECONDS` | `120` | Time limit of one call to a model. A call over it counts as failed; the assessment tries once more, then the rules alone apply. |
| `ARDEA_AI__DAILY_TOKENS` | `2000000` | Tokens (in and out) per UTC day for the whole instance. Once used up, new alerts are assessed with rules and templates alone until the next day. |
| `ARDEA_AI__DAILY_TOKENS_PER_ORGANIZATION` | `100000` | The same per organisation. One assessment needs about 2 000 to 3 600 tokens; the default covers about 40 a day. A call that fails or times out is charged its estimate (prompt length / 4 plus 2 000). |
| `ARDEA_AI__DAILY_REQUESTS_PER_ORGANIZATION` | `20` | Assessments the people of one organisation may ask for per UTC day. Their requests run before the system's, so the cap keeps one organisation from holding the others back. Beyond it the request is refused with 429. |
| `ARDEA_AI__CATALOG_PROVIDER` | first local provider | The provider the catalogue asks when it learns unknown products (master plan 6.9, ADR 040): a provider's name, or `none` for none. The catalogue sends only public data of the sources, so the organisations' data policies do not apply; a provider that is not local is used only when it is named here or in *Sources → Catalogue*. Set here, the field in the administration is locked. |
| `ARDEA_AI__CATALOG_DAILY_CALLS` | `200` | Calls per UTC day for the catalogue, retries included; the rest waits for the next day. |
| `ARDEA_RATE_LIMIT__AUTH_PER_MINUTE` | `10` | Sign-in, registration and reset attempts per client network and minute; read-only lookups and the confirmation and unsubscribe links of external addresses get six times as many. |
| `ARDEA_RATE_LIMIT__OUTBOUND_PER_MINUTE` | `10` | Requests per account and minute that call out of the instance while they wait or build a large answer: connection tests (SMTP, AI providers, GitHub, webhooks, push devices), the repository search at GitHub, invitations and confirmation e-mails, the catalogue export. |
| `ARDEA_PASSWORD_HASH_ITERATIONS` | Identity's default | PBKDF2 iterations for new password hashes. |
| `ARDEA_INTEL__BACKFILL_DAYS` | `180` | Look-back of the first load. |
| `ARDEA_SCRATCH_DIR` | `/scratch` | Working space for large archives (a volume in Compose). |
| `ARDEA_DATA_DIR` | `/app/data` | Master data shipped with the image (catalogue). |
| `ARDEA_WORKER_ID` | host name | Name of a worker in the job leases; set it when running several workers. |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | none | Send traces, metrics and logs to an OpenTelemetry collector. |

GitHub Enterprise Server hosts are added by the instance administrator in the administration, not in the
environment; organisations choose from them.
