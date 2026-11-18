# 12 · Docker, Deployment und Betrieb

## 12.1 Ziel

Wer Docker hat, startet eine vollständige Instanz mit einer Datei und einem Befehl. Updates sind `pull` und `up`. Alles Weitere ist freiwillig.

## 12.2 Aufbau

Ein Image, drei Rollen, eine Datenbank:

```yaml
# compose.yaml (Skizze)
services:
  db:
    image: postgres:18.7
    environment:
      POSTGRES_DB: csh
      POSTGRES_USER: csh_migrator
      POSTGRES_PASSWORD: ${CSH_DB_PASSWORD:?set in .env}
    volumes: ["db:/var/lib/postgresql"]        # ab PostgreSQL 18: neues Volume-Ziel
    healthcheck: { test: ["CMD-SHELL", "pg_isready -U csh_migrator -d csh"], interval: 5s, retries: 20 }
    restart: unless-stopped

  migrate:
    image: ghcr.io/OWNER/csh-alert:1
    command: ["migrate"]
    env_file: .env
    depends_on: { db: { condition: service_healthy } }
    restart: "no"

  api:
    image: ghcr.io/OWNER/csh-alert:1
    command: ["api"]
    env_file: .env
    ports: ["127.0.0.1:8080:8080"]
    depends_on: { migrate: { condition: service_completed_successfully } }
    healthcheck: { test: ["CMD", "/app/csh", "healthcheck", "api"], interval: 15s, retries: 5 }
    read_only: true
    restart: unless-stopped

  worker:
    image: ghcr.io/OWNER/csh-alert:1
    command: ["worker"]
    env_file: .env
    depends_on: { migrate: { condition: service_completed_successfully } }
    healthcheck: { test: ["CMD", "/app/csh", "healthcheck", "worker"], interval: 30s, retries: 5 }
    read_only: true
    restart: unless-stopped

volumes: { db: {} }
```

- Die API liefert auch die Web-App aus. Ein eigener Webserver-Container entfällt, ebenso CORS.
- Der Port ist in der Vorgabe nur lokal gebunden. TLS und öffentliche Erreichbarkeit übernimmt ein vorgeschalteter Proxy oder Tunnel; die Dokumentation bringt fertige Beispiele für Caddy, Traefik und Cloudflare Tunnel als zusätzliche Compose-Dateien.
- `migrate` legt beim ersten Lauf die Anwendungsrolle `csh_app` an, wendet Migrationen an und spielt Stammdaten ein. API und Worker starten erst danach und prüfen beim Start, ob das Schema zu ihrer Version passt.

## 12.3 Das Image

| Punkt | Entscheidung |
|---|---|
| Bau | Mehrstufiges Dockerfile: Node baut die Angular-App, das .NET-SDK veröffentlicht, das Ergebnis liegt auf `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra`. |
| Laufzeit | Kein Shell, kein Paketmanager, nicht privilegierter Benutzer, Port 8080, Zeitzonendaten und ICU enthalten. |
| Architekturen | `linux/amd64` und `linux/arm64`. |
| Einstieg | Ein Programm `csh` mit Befehlen: `api`, `worker`, `migrate`, `healthcheck`, `doctor`, dazu Wartungsbefehle (12.8). |
| Tags | `1.4.2`, `1.4`, `1`, `latest`. Die Compose-Vorgabe nutzt die Hauptversion. |
| Nachweis | SBOM und Signatur je Release. |

Health Checks rufen das Programm selbst auf, weil das Image weder `curl` noch eine Shell enthält. Für die API prüft `healthcheck` den Endpunkt `/health/ready`, für den Worker einen Herzschlag in der Datenbank, der nicht älter als zwei Minuten sein darf.

## 12.4 Konfiguration

Umgebungsvariablen mit Präfix `CSH_`; verschachtelte Werte mit doppeltem Unterstrich. Jedes Geheimnis lässt sich alternativ als Datei übergeben (`…_FILE`), passend zu Docker Secrets. Beim Start wird alles geprüft; ein Fehler beendet den Prozess mit einer Meldung, die die Variable beim Namen nennt.

| Variable | Pflicht | Bedeutung |
|---|:-:|---|
| `CSH_DB_PASSWORD` | ● | Passwort der Datenbank |
| `CSH_MASTER_KEY` | ● | Hauptschlüssel für die Verschlüsselung der Geheimnisse; 32 Byte, Base64 |
| `CSH_PUBLIC_URL` | ● | Öffentliche Adresse, für Links in Nachrichten und für Cookies |
| `CSH_SETUP_TOKEN` | | Einrichtungstoken; ohne Angabe wird eines erzeugt und einmal geloggt |
| `CSH_REGISTRATION` | | `invite` (Vorgabe), `open`, `closed` |
| `CSH_SMTP__HOST`, `__PORT`, `__USER`, `__PASSWORD`, `__FROM` | | E-Mail-Versand |
| `CSH_AI__PROVIDER`, `__ENDPOINT`, `__MODEL`, `__API_KEY` | | KI-Anbieter; Vorgabe `none` |
| `CSH_INTEL__BACKFILL_DAYS` | | Rückblick beim Erststart, Vorgabe 180 |
| `CSH_TRUSTED_PROXIES` | | Adressen vorgeschalteter Proxys für weitergereichte Header |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | | Ziel für Traces, Metriken und Logs; ohne Angabe kein Export |

Die drei Pflichtwerte erzeugt ein Befehl: `docker run --rm ghcr.io/OWNER/csh-alert:1 init-env > .env`. Was im Betrieb änderbar sein soll (SMTP, KI-Anbieter, Quellen, Grenzen), lässt sich auch in der Oberfläche setzen; eine gesetzte Umgebungsvariable hat Vorrang und sperrt das Feld mit einem Hinweis.

## 12.5 Daten, Sicherung, Wiederherstellung

- Alle dauerhaften Daten liegen in PostgreSQL. API und Worker sind zustandslos.
- Sicherung: `docker compose exec db pg_dump -Fc csh > csh.dump`. Ein optionales Compose-Profil `backup` erledigt das zeitgesteuert in ein Verzeichnis des Hosts.
- Der Hauptschlüssel gehört getrennt gesichert. Ohne ihn sind die gespeicherten Geheimnisse einer Sicherung nicht lesbar; alles andere schon.
- Wiederherstellung: Dump einspielen, `migrate` ausführen, starten. Der Ablauf ist dokumentiert und Teil der Release-Tests.

## 12.6 Updates

```bash
docker compose pull && docker compose up -d
```

- Semantische Versionierung. Patch- und Minor-Versionen sind für unbeaufsichtigte Updates geeignet, etwa per Cron.
- Migrationen laufen automatisch, vorwärtsgerichtet und in zwei Schritten verträglich (Kapitel 4.10).
- Eine neue Hauptversion kommt nie über das Tag `1`. Wer sie will, ändert das Tag und liest die Hinweise.
- Rückwärts wird nicht migriert. Vor einem Wechsel der Hauptversion sichern.
- Die Instanz fragt nirgends nach neuen Versionen. Die installierte Version steht in der Administration.

## 12.7 Beobachtbarkeit

| Signal | Vorgabe | Auf Wunsch |
|---|---|---|
| Logs | strukturiertes JSON auf der Standardausgabe; keine Geheimnisse, keine Profilinhalte | Export per OTLP |
| Metriken | in der Administration: Verzögerung und Fehlerserie je Quelle, Quarantäne, Jobs wartend und fehlgeschlagen, Zustellungen, KI-Aufrufe und Tokens | Export per OTLP |
| Traces | aus | ein Trace von der Quelle bis zur Zustellung per OTLP |
| Betrachter | keiner | Compose-Profil `observability` mit dem eigenständigen Aspire-Dashboard |

Das System warnt selbst, wenn es nicht mehr zuverlässig arbeitet: Eine Quelle, die länger als sechs Stunden nicht abgerufen werden konnte, ein stehender Worker oder dauerhaft fehlschlagende Zustellungen erzeugen eine Meldung an den Instanz-Administrator und einen Hinweis im Bulletin («Stand von 02:10, Quelle nicht erreichbar»).

## 12.8 Wartungsbefehle

| Befehl | Zweck |
|---|---|
| `csh doctor` | Prüft Konfiguration, Datenbank, Schema, SMTP, KI-Anbieter und Erreichbarkeit der Quellen |
| `csh admin reset-password <email>` | Zugang wiederherstellen |
| `csh intel import --full` | Gesamten CVE-Bestand einspielen |
| `csh intel renormalize --source <key>` | Rohdaten einer Quelle mit aktuellem Parser neu verarbeiten |
| `csh intel recompute` | Einträge aus Beobachtungen neu zusammenführen |
| `csh assess recompute` | Stufen mit aktuellem Regelwerk neu berechnen |
| `csh keys rotate` | Hauptschlüssel wechseln |
| `csh org export <slug>` · `csh org delete <slug>` | Daten einer Organisation exportieren oder entfernen |

## 12.9 Grössenordnung

| Einsatz | Empfehlung |
|---|---|
| Kleine Instanz, bis etwa 20 Organisationen, ohne lokale KI | 2 vCPU, 2 GB RAM, 10 GB Speicher |
| Mit lokalem Sprachmodell | zusätzlich ein Modell-Server mit 8 bis 16 GB Speicher für ein Modell der 8- bis 12-Milliarden-Klasse; getrennt betrieben, per URL angebunden |
| Gesamter CVE-Bestand | zusätzlich rund 10 GB Speicher |

Das Sprachmodell gehört nicht ins Compose der Anwendung. Wer eines lokal betreiben will, startet den Modell-Server daneben; ein Beispiel liegt bei.

## 12.10 Entwicklung

`compose.dev.yaml` startet nur die Umgebung: PostgreSQL, einen Mail-Fänger, auf Wunsch das Aspire-Dashboard und einen lokalen Modell-Server. API und Worker laufen mit `dotnet watch`, die Web-App mit dem Angular-Entwicklungsserver und Proxy auf die API. Ein .NET-Aspire-AppHost wird nicht vorausgesetzt: Er wäre eine zweite Beschreibung derselben Topologie.
