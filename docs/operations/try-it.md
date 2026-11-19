# Try it

Look at Ardea with data in it: an organisation, three projects, real vulnerabilities, the actions that follow
from them and the notifications they raise. `ardea seed-demo` loads this into an empty instance in well under
a minute. It needs no GitHub token, no initial load and no access to any source: the records are part of the
image, and the instance fetches nothing.

## Steps

You need Docker with Compose v2. Until the first release (task 7.3) build the image from a checkout and set
`ARDEA_IMAGE`: `docker build -f deploy/Dockerfile -t ardea:dev .` and `export ARDEA_IMAGE=ardea:dev`.

1. Create a folder with the Compose file and the generated secrets. The public address is plain HTTP on this
   machine, which is only for trying it out:

   ```bash
   mkdir ardea-demo && cd ardea-demo
   curl -fsSLO https://raw.githubusercontent.com/NicoM77/Ardea/main/deploy/compose.yaml
   docker run --rm --user "$(id -u):$(id -g)" -v "$PWD:/out" "${ARDEA_IMAGE:-ghcr.io/nicom77/ardea:0}" init-env --dir /out --public-url http://localhost:8080
   ```

2. Start the database, the migration and the API, but not yet the worker:

   ```bash
   docker compose up -d --wait api
   ```

3. Load the sample data. The command checks that the instance is empty, so it needs the owner password of the
   database besides the settings of `.env.app`:

   ```bash
   docker compose run --rm -e ARDEA_DB_OWNER_PASSWORD="$(sed -n 's/^ARDEA_DB_OWNER_PASSWORD=//p' .env)" api seed-demo
   ```

   It prints what it made and, once, the sign-in of the demo owner (`nina.meier@example.org` and a generated
   password; the database keeps only a hash of it). Nothing of this goes to the logs.

4. Start the rest, the worker. With the sources switched off it fetches nothing; it keeps the instance alive
   (notifications, digests in the app) and stops the administration from warning that no worker runs:

   ```bash
   docker compose up -d --wait
   ```

5. Open <http://localhost:8080> and sign in.

## What it loads

- **Example Digital AG**, the organisation of the mockups, with three members: Nina Meier (owner and instance
  administrator), Marc Keller (administrator) and Lea Brunner (member). Their addresses are on `example.org`, a
  domain reserved for examples. Only Nina's password is shown; the others cannot sign in until an owner resets
  it (`ardea admin reset-password <email>`). The setup wizard counts as done, and stays open under *Setup*.
- **Three projects**, the example projects A, B and C of the requirements under the names of the mockups:
  *Customer Portal* (.NET, Angular, Umbraco, Azure SQL), *Webshop* (WordPress, PHP, MySQL) and *Platform Ops*
  (Kubernetes, Linux, Redis, PostgreSQL; business-critical, internal). Their versions are those installed
  before the security updates of the week of 11 to 17 November 2026.
- **16 real CVE records** of that week, curated from the reference data of the repository
  (`src/Modules/Intel/Ardea.Intel/Demo/demo-records.jsonl`): the ones behind the mockups' actions (for
  example CVE-2026-109619 for PHP and CVE-2026-116325 for PostgreSQL) and a few that turn out not to concern
  any project once the versions are checked.
- What the pipeline makes of them, through the real jobs and not as rows written by hand: entries, findings
  with their version checks and levels, six actions with their history, and the in-app notifications of the
  default rules.
- A little work on those actions, again through the real commands: Marc notes the rollout of the PHP update,
  Lea notes what the PostgreSQL update depends on, Marc closes the Linux kernel action as not affected with
  his reason, and Nina records the .NET update of the Customer Portal in its profile, after which Ardea closes
  that action by itself. Four actions stay open, one of them critical.

The records are the CVE Program's own; the demo organisation, its people, projects and versions are invented.
The instance's sources are registered but **switched off**, so the worker does not load the world into the
demo. Turn them on in the administration to continue with live data. Messages outside the app that the demo's
events would cause, e-mail on an instance with SMTP, are suppressed; the delivery log says why.

All of it happens at the moment you run the command: the actions were raised «just now», and their deadlines
count from then.

## An instance with data is refused

`seed-demo` works on an empty instance only. If the database holds accounts, organisations, projects, source
records or security entries, it says so and exits with code 1 before it writes anything. To start over,
remove the volumes (`docker compose down -v`) and begin at step 2. Run it before the worker starts, as above:
a worker that takes the demo's jobs at the same time does the same work, only out of the command's sight. If a
step fails, the command stops with a message: the instance is then partly filled and is best started over as
well.
