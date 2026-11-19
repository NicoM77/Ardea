# 12 · Docker, Deployment und Betrieb

## 12.1 Ziel

Wer Docker hat, startet eine vollständige Instanz mit einer Datei und einem Befehl. Updates sind `pull` und `up`. Alles Weitere ist freiwillig.

## 12.2 Aufbau

Ein Image, drei Rollen, eine Datenbank:

```yaml
# compose.yaml (Skizze)
services:
  db:
    image: postgres:18
    environment:
      POSTGRES_DB: ardea
      POSTGRES_USER: ardea_migrator
      POSTGRES_PASSWORD: ${ARDEA_DB_OWNER_PASSWORD:?set in .env}
    volumes: ["db:/var/lib/postgresql"]        # ab PostgreSQL 18: neues Volume-Ziel; darin auch backup/
    healthcheck: { test: ["CMD-SHELL", "pg_isready -U ardea_migrator -d ardea"], interval: 5s, retries: 20 }
    restart: unless-stopped

  migrate:                                     # kennt als einziger Dienst das Eigentümer-Passwort
    image: ghcr.io/OWNER/ardea:1
    command: ["migrate"]
    environment:
      ARDEA_DB_OWNER_PASSWORD: ${ARDEA_DB_OWNER_PASSWORD}
      ARDEA_DB_APP_PASSWORD: ${ARDEA_DB_APP_PASSWORD:?set in .env}
    depends_on: { db: { condition: service_healthy } }
    restart: "no"

  api:
    image: ghcr.io/OWNER/ardea:1
    command: ["api"]
    env_file: .env.app                         # ohne Eigentümer-Passwort
    ports: ["127.0.0.1:8080:8080"]
    depends_on: { migrate: { condition: service_completed_successfully } }
    healthcheck: { test: ["CMD", "/app/ardea", "healthcheck", "api"], interval: 15s, retries: 5 }
    read_only: true
    tmpfs: ["/tmp"]
    restart: unless-stopped

  worker:
    image: ghcr.io/OWNER/ardea:1
    command: ["worker"]
    env_file: .env.app
    depends_on: { migrate: { condition: service_completed_successfully } }
    healthcheck: { test: ["CMD", "/app/ardea", "healthcheck", "worker"], interval: 30s, retries: 5 }
    read_only: true
    tmpfs: ["/tmp"]
    volumes: ["scratch:/scratch"]              # Gesamtarchiv der CVEs beim Erststart
    restart: unless-stopped

volumes: { db: {}, scratch: {} }
```

- Die API liefert auch die Web-App aus. Ein eigener Webserver-Container entfällt, ebenso CORS.
- Der Port ist in der Vorgabe nur lokal gebunden. Die Anwendung verlangt HTTPS: Ihr Sitzungs-Cookie wird nur über eine sichere Verbindung gesetzt. TLS und öffentliche Erreichbarkeit übernimmt ein vorgeschalteter Proxy oder Tunnel. Der Schnellstart liefert deshalb eine zweite Compose-Datei mit Caddy mit; Beispiele für Traefik und Cloudflare Tunnel liegen daneben.
- `migrate` legt beim ersten Lauf die Anwendungsrolle `ardea_app` mit eigenem Passwort an, wendet Migrationen an und spielt Stammdaten ein. Steht eine Migration an, entsteht vorher ein Dump im Datenvolume neben dem Datenverzeichnis (`backup/`); die letzten drei bleiben. Er schützt vor einer missglückten Migration, nicht vor dem Verlust des Volumes; dafür ist die Sicherung aus 12.5 da. Das Anwendungsimage enthält kein `pg_dump`. `migrate` lässt den Dump deshalb vom Datenbankserver schreiben (`COPY … TO PROGRAM` mit dem `pg_dump` des Datenbank-Images), was mit der mitgelieferten Datenbank und ihrer Eigentümerrolle möglich ist. Bei einer fremden Datenbank entfällt dieser Schritt mit einem deutlichen Hinweis; die Sicherung liegt dann beim Betreiber. Der Weg wird im Versuch 0.7 geprüft. API und Worker starten erst danach; sie verlangen mindestens ihren Schemastand und vertragen einen neueren, verträglich erweiterten.

## 12.3 Das Image

| Punkt | Entscheidung |
|---|---|
| Bau | Mehrstufiges Dockerfile: Node baut die Angular-App, das .NET-SDK veröffentlicht, das Ergebnis liegt auf `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra`. |
| Laufzeit | Keine Shell, kein Paketmanager, nicht privilegierter Benutzer, Port 8080; Zeitzonendaten und ICU laut Beschreibung des Images enthalten, in Phase 0 geprüft. |
| Architekturen | `linux/amd64` und `linux/arm64`. |
| Einstieg | Ein Programm `ardea` mit den Rollen `api`, `worker` und `migrate`, den Hilfsbefehlen `healthcheck`, `init-env` und `doctor`, dazu Wartungsbefehle (12.8). |
| Tags | `1.4.2`, `1.4`, `1`, `latest`. Die Compose-Vorgabe nutzt die Hauptversion. |
| Nachweis | SBOM je Release; Images mit cosign signiert und mit Herkunftsnachweis (Aufgabe 7.3). |

Health Checks rufen das Programm selbst auf, weil das Image weder `curl` noch eine Shell enthält. Für die API prüft `healthcheck` den Endpunkt `/health/ready`, für den Worker einen Herzschlag in der Datenbank, der nicht älter als zwei Minuten sein darf.

## 12.4 Konfiguration

Umgebungsvariablen mit Präfix `ARDEA_`; verschachtelte Werte mit doppeltem Unterstrich. Jedes Geheimnis lässt sich alternativ als Datei übergeben (`…_FILE`), passend zu Docker Secrets. Beim Start wird alles geprüft; ein Fehler beendet den Prozess mit einer Meldung, die die Variable beim Namen nennt.

| Variable | Pflicht | Bedeutung |
|---|:-:|---|
| `ARDEA_DB_OWNER_PASSWORD` | ● | Passwort der Eigentümerrolle; nur für `db`, `migrate` und Wartungsbefehle |
| `ARDEA_DB_APP_PASSWORD` | ● | Passwort der Anwendungsrolle für API und Worker |
| `ARDEA_MASTER_KEY` | ● | Hauptschlüssel für die Verschlüsselung der Geheimnisse; 32 Byte, Base64 |
| `ARDEA_PUBLIC_URL` | ● | Öffentliche Adresse, für Links in Nachrichten und für Cookies |
| `ARDEA_SETUP_TOKEN` | | Einrichtungstoken; ohne Angabe wird eines erzeugt und einmal geloggt |
| `ARDEA_REGISTRATION` | | `invite` (Vorgabe), `open`, `closed` |
| `ARDEA_SMTP__HOST`, `__PORT`, `__USER`, `__PASSWORD`, `__FROM` | | E-Mail-Versand |
| `ARDEA_AI__PROVIDERS__<Name>__KIND`, `__ENDPOINT`, `__MODEL`, `__API_KEY`, `__LOCAL` | | benannte KI-Anbieter; ohne Angabe läuft das System mit Regeln und Vorlagen |
| `ARDEA_INTEL__BACKFILL_DAYS` | | Rückblick beim Erststart, Vorgabe 180 |
| `ARDEA_REPOSITORIES__CHECK_INTERVAL` | | Abstand, in dem verbundene Repositories auf neue Commits geprüft werden, Vorgabe 60 Minuten (Kapitel 16.6) |
| `ARDEA_TRUSTED_PROXIES` | | Adressen vorgeschalteter Proxys für weitergereichte Header |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | | Ziel für Traces, Metriken und Logs; ohne Angabe kein Export |

Die Pflichtwerte erzeugt ein Befehl: `docker run --rm ghcr.io/OWNER/ardea:1 init-env`. Er schreibt `.env` für Datenbank und Migration und `.env.app` für API und Worker. Was im Betrieb änderbar sein soll (SMTP, KI-Anbieter, Quellen, Grenzen), lässt sich auch in der Oberfläche setzen; eine gesetzte Umgebungsvariable hat Vorrang und sperrt das Feld mit einem Hinweis.

**Ohne SMTP** bleibt die Instanz benutzbar: Eine Einladung zeigt der einladenden Person den Link zum Weitergeben, E-Mail-Adressen gelten ohne Bestätigung, und ein vergessenes Passwort setzt der Betreiber mit `ardea admin reset-password` zurück. Der Kanal E-Mail ist abgeschaltet; die Oberfläche sagt das an jeder Stelle, die ihn braucht.

## 12.5 Daten, Sicherung, Wiederherstellung

- Alle dauerhaften Daten liegen in PostgreSQL. API und Worker sind zustandslos.
- Sicherung: `docker compose exec -T db pg_dump -U ardea_migrator -Fc ardea > ardea.dump`. Für die zeitgesteuerte Sicherung genügt ein Cron-Eintrag des Hosts mit diesem Befehl; ein eigenes Compose-Profil dafür ist eine Ausbaustufe.
- Der Hauptschlüssel gehört getrennt gesichert. Ohne ihn sind die gespeicherten Geheimnisse einer Sicherung nicht lesbar; alles andere schon.
- Wiederherstellung: API und Worker stoppen, eine leere Datenbank anlegen, mit `ardea migrate --roles-only` die Anwendungsrolle anlegen, den Dump mit `pg_restore` einspielen, `migrate` ausführen, starten. Die Reihenfolge zählt: Der Dump enthält Rechte für `ardea_app`, die Rolle muss also vorher bestehen. Der Ablauf ist dokumentiert und Teil der Release-Tests.

## 12.6 Updates

```bash
docker compose pull && docker compose up -d
```

- Semantische Versionierung. Patch- und Minor-Versionen sind für unbeaufsichtigte Updates geeignet, etwa per Cron.
- Migrationen laufen automatisch, vorwärtsgerichtet und in zwei Schritten verträglich (Kapitel 4.11).
- Eine neue Hauptversion kommt nie über das Tag `1`. Wer sie will, ändert das Tag und liest die Hinweise.
- Rückwärts wird nicht migriert. Der Weg zurück ist der Dump, der vor jeder Migration entsteht, zusammen mit dem vorherigen Image. Das ist auch die Absicherung für unbeaufsichtigte Updates.
- Die Instanz fragt nirgends nach neuen Versionen. Die installierte Version steht in der Administration.

## 12.7 Beobachtbarkeit

| Signal | Vorgabe | Auf Wunsch |
|---|---|---|
| Logs | strukturiertes JSON auf der Standardausgabe; keine Geheimnisse, keine Profilinhalte | Export per OTLP |
| Metriken | in der Administration: Verzögerung und Fehlerserie je Quelle, Quarantäne, Jobs wartend und fehlgeschlagen, Zustellungen, KI-Aufrufe und Tokens, Einlesen von Repositories mit Dauer, Fehlern und verbleibendem Kontingent bei GitHub | Export per OTLP |
| Traces | aus | ein Trace von der Quelle bis zur Zustellung per OTLP |
| Betrachter | keiner | Compose-Profil `observability` mit dem eigenständigen Aspire-Dashboard |

Das System warnt selbst, wenn es nicht mehr zuverlässig arbeitet: Eine Quelle, die länger als sechs Stunden nicht abgerufen werden konnte, ein stehender Worker oder dauerhaft fehlschlagende Zustellungen erzeugen eine Benachrichtigung an den Instanz-Administrator und einen Hinweis in der Übersicht («Stand von 02:10, Quelle nicht erreichbar»).

## 12.8 Wartungsbefehle

| Befehl | Zweck |
|---|---|
| `ardea doctor` | Prüft Konfiguration, Datenbank, Schema, SMTP, KI-Anbieter und Erreichbarkeit der Quellen |
| `ardea admin reset-password <email>` | Zugang wiederherstellen |
| `ardea intel import --full` | Gesamten CVE-Bestand einspielen |
| `ardea intel renormalize --source <key>` | Rohdaten einer Quelle mit aktuellem Parser neu verarbeiten |
| `ardea intel recompute` | Einträge aus Beobachtungen neu zusammenführen |
| `ardea assess recompute` | Stufen mit aktuellem Regelwerk neu berechnen |
| `ardea org export <slug>` · `ardea org delete <slug>` | Daten einer Organisation exportieren oder entfernen |

## 12.9 Grössenordnung

| Einsatz | Empfehlung |
|---|---|
| Kleine Instanz, bis etwa 20 Organisationen, ohne lokale KI | 2 vCPU, 4 GB RAM, 20 GB Speicher; das ist auch die Referenzmaschine der Abnahmekriterien (Schätzung; an der eigenen Instanz des Projekts beobachtet) |
| Mit lokalem Sprachmodell | zusätzlich ein Modell-Server mit 8 bis 16 GB Arbeitsspeicher (Modell von grob 6 bis 10 GB plus Kontext) für ein Modell der 8- bis 12-Milliarden-Klasse; getrennt betrieben, per URL angebunden |
| Gesamter CVE-Bestand | zusätzlich rund 10 GB Speicher |

Das Sprachmodell gehört nicht ins Compose der Anwendung. Wer eines lokal betreiben will, startet den Modell-Server daneben; ein Beispiel liegt bei.

## 12.10 Entwicklung

`compose.dev.yaml` startet nur die Umgebung: PostgreSQL, einen Mail-Fänger, auf Wunsch das Aspire-Dashboard und einen lokalen Modell-Server. API und Worker laufen mit `dotnet watch`, die Web-App mit dem Angular-Entwicklungsserver und Proxy auf die API. Ein .NET-Aspire-AppHost wird nicht vorausgesetzt: Er wäre eine zweite Beschreibung derselben Topologie.
