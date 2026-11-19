# Update

Versions follow Semantic Versioning; within a major version (`ghcr.io/nicom77/ardea:0`, published from the
first release on) an update needs nothing but a pull and a restart. Read the release notes first.

```bash
docker compose pull
docker compose up -d
docker compose logs migrate      # "Migration finished: N migration(s) applied, dump: …"
```

`migrate` takes a dump before it applies migrations and exits with code 70 if one fails; API and worker
then stay stopped, on the old schema state of the dump.

## Going back

1. Stop API and worker: `docker compose stop api worker`.
2. Restore the dump `migrate` wrote before the update (its path is in the `migrate` log):
   `docker compose exec db pg_restore -U ardea_migrator -d ardea --clean --if-exists /var/lib/postgresql/backup/<file>`.
3. Pin the previous image (`ARDEA_IMAGE=ghcr.io/nicom77/ardea:<previous>`) and `docker compose up -d`.
