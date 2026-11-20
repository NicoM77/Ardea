# Update

Versions follow Semantic Versioning; within a major version (`ghcr.io/nicom77/ardea:0`, published from the
first release on) an update needs nothing but a pull and a restart. Read the release notes first. A new major version
never arrives under this tag: change the tag yourself and read its notes. Ardea does not ask anywhere for new
versions; the installed version is shown in the administration (and by `ardea version`).

```bash
docker compose pull
docker compose up -d
docker compose logs migrate      # "Migration finished: N migration(s) applied, dump: …"
```

`migrate` takes a dump before it applies migrations and exits with code 70 if one fails; API and worker
then stay stopped, on the old schema state of the dump.

## Upgrading from a build before the stack

The migration of phase 5a ([ADR 046](../adr/0046-stack-notices-and-reminders-without-deadline.md), decision 9)
removes assignment, deadline and «in review» from actions, which are called reminders from now on and are open or
done. Every organisation gets an empty stack. **Open actions on components without a version are moved to notices:**
the reminder and its findings are closed with the reason `moved_to_notice` (numbers and history stay), and one open
notice per entry takes their place on the *Notices* page, so the person who had the reminder finds the question there.
Closed actions and actions on components with an exact version stay as they are; an open one on a component with a
version line moves on its next assessment. Notices from the stack are generated from the first assessment after the
upgrade, so the *Notices* page may hold more than the moved ones. The overview's figures do not count these closings
as news. Read the release notes before upgrading a running instance.

## Going back

The dump `migrate` wrote before the update holds the state before it. Restore it into an empty database: restoring
over the migrated tables fails, because the newer schemas depend on the older ones.

1. Stop API and worker: `docker compose stop api worker`.
2. Find the dump (its path is in the `migrate` log, `docker compose logs migrate`) and recreate the database empty:

   ```bash
   docker compose exec db psql -U ardea_migrator -d postgres -c 'DROP DATABASE ardea WITH (FORCE)'
   docker compose exec db psql -U ardea_migrator -d postgres -c 'CREATE DATABASE ardea'
   ```

3. Restore: `docker compose exec db pg_restore -U ardea_migrator -d ardea --no-owner /var/lib/postgresql/backup/<file>`.
4. Pin the previous image (`ARDEA_IMAGE=ghcr.io/nicom77/ardea:<previous>`) and `docker compose up -d`. `migrate`
   gives the application role its rights on the restored database again.

Only the newest three dumps are kept. The integration test `PreReleaseRestoreTests` runs this path on every build:
a dump of an earlier state is restored, migrated to the current version, restored from the dump `migrate` wrote,
and migrated again.

A dump of a restored or newer database taken with `pg_dump` (see [Operation](operations.md)) is restored the same way.
