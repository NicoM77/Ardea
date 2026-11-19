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
| `ARDEA_RATE_LIMIT__AUTH_PER_MINUTE` | `10` | Sign-in, registration and reset attempts per client network and minute; read-only lookups get six times as many. |
| `ARDEA_PASSWORD_HASH_ITERATIONS` | Identity's default | PBKDF2 iterations for new password hashes. |
| `ARDEA_INTEL__BACKFILL_DAYS` | `180` | Look-back of the first load. |
| `ARDEA_SCRATCH_DIR` | `/scratch` | Working space for large archives (a volume in Compose). |
| `ARDEA_DATA_DIR` | `/app/data` | Master data shipped with the image (catalogue). |
| `ARDEA_WORKER_ID` | host name | Name of a worker in the job leases; set it when running several workers. |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | none | Send traces, metrics and logs to an OpenTelemetry collector. |

GitHub Enterprise Server hosts are added by the instance administrator in the administration, not in the
environment; organisations choose from them.
