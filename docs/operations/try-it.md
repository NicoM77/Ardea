# Try it

Look at Ardea with data in it: an organisation, three projects, real vulnerabilities, the actions that follow
from them and a few notifications. `ardea seed-demo` loads this into an empty instance in well under a
minute. It needs no GitHub token, no initial load and no access to any source: the records are part of the
image, and the instance never reaches out.

## Steps

You need Docker with Compose v2. Until the first release (task 6.8) build the image from a checkout and set
`ARDEA_IMAGE`: `docker build -f deploy/Dockerfile -t ardea:dev .` and `export ARDEA_IMAGE=ardea:dev`.

1. Create a folder with the Compose file and the generated secrets. The public address is plain HTTP on this
   machine, which is only for trying it out:

   ```bash
   mkdir ardea-demo && cd ardea-demo
   curl -fsSLO https://raw.githubusercontent.com/NicoM77/Ardea/main/deploy/compose.yaml
   docker run --rm --user "$(id -u):$(id -g)" -v "$PWD:/out" ghcr.io/nicom77/ardea:0 init-env --dir /out --public-url http://localhost:8080
   ```

2. Start the database, the migration and the API. Leave the worker out: the demo needs none.

   ```bash
   docker compose up -d --wait api
   ```

3. Load the sample data. The command checks that the instance is empty, so it needs the owner password of the
   database besides the settings of `.env.app`:

   ```bash
   docker compose run --rm -e ARDEA_DB_OWNER_PASSWORD="$(sed -n 's/^ARDEA_DB_OWNER_PASSWORD=//p' .env)" api seed-demo
   ```

   It prints what it made and, once, the sign-in of the demo owner (`nina.meier@example.org` and a generated
   password; the database keeps only a hash of it).

4. Open <http://localhost:8080> and sign in.

## What it loads

- **Example Digital AG**, the organisation of the mockups, with three members: Nina Meier (owner and instance
  administrator), Marc Keller (administrator) and Lea Brunner (member). Only Nina's password is shown; the
  others cannot sign in until an owner resets it (`ardea admin reset-password <email>`).
- **Three projects**, the example projects A, B and C of the requirements under the names of the mockups:
  *Customer Portal* (.NET, Angular, Umbraco, Azure SQL), *Webshop* (WordPress, PHP, MySQL) and *Platform Ops*
  (Kubernetes, Linux, Redis, PostgreSQL; business-critical, internal). Their versions are those installed
  before the security updates of the week of 11 to 17 November 2026.
- **16 real CVE records** of that week, curated from the reference data of the repository
  (`src/Modules/Intel/Ardea.Intel/Demo/demo-records.jsonl`): the ones behind the mockups' actions (for
  example CVE-2026-109619 for PHP and CVE-2026-116325 for PostgreSQL) and a few that do not concern any
  project.
- What the pipeline makes of them, through the real jobs and not as rows written by hand: entries, findings
  with their version checks and levels, six actions, their history and the in-app notifications of the default
  rules.
- A little work by the three people on those actions: one in review with its assignee, one assigned, one
  closed as remediated, one with a note. History and notifications of these steps are real as well.

The records are the CVE Program's own; the demo organisation, its people, projects and versions are invented.
The instance's sources are registered but **switched off**, so a worker started later does not load the world
into the demo. Turn them on in the administration to continue with live data.

## An instance with data is refused

`seed-demo` works on an empty instance only. If the database holds accounts, organisations, projects, source
records or security entries, it says so and exits with code 1 before it writes anything. To start over,
remove the volumes (`docker compose down -v`) and begin at step 2. The command takes the same steps as the
worker would for the records, and if one of them fails it stops with a message: the instance is then partly
filled and is best started over as well.
