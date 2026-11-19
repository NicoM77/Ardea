# NOTES: Arbeitsstand und Wissen ausserhalb des Repositorys

Stand: 19.11.2026, ca. 17:30 UTC. Geschrieben von Claude als Kontext-Sicherung: alles, was ich über die
Umsetzung weiss und was nicht schon im Repository steht (README, Masterplan, ADR, Abnahmen, PR-Texte,
Issues). Wo etwas im Repository steht, verweise ich nur darauf.

## 1. Woran gerade gearbeitet wird

| Was | Stand | Nächster Schritt |
|---|---|---|
| PR #78 `feat/hardening-ops` (6.4–6.8: Plan-Tests mit Indizes, Restore-Test aus Vorab-Dump, Wartungsbefehle, Doku, SBOM und Lizenzdateien im Image) | fertig, lokal geprüft, nicht gemergt | zusammen mit #79 mergen |
| PR #79 `feat/security-pass` (6.1: Bedrohungstabelle 9.7, acht Lücken behoben, `docs/security/threat-review.md`) | fertig, lokal geprüft, nicht gemergt | zusammen mit #78 mergen |
| Kombinierte Prüfung #78 + #79 | läuft lokal im Worktree `../wt-p6` (Branch `p6-check`, nur lokal): Build, `dotnet format`, ganze Backend-Testsuite, Web Lint/Test/Build | bei grün: #78 mergen mit `[skip ci]` im Merge-Commit, danach #79 normal mergen → genau ein CI-Lauf auf main; dann `wt-p6` und `p6-check` löschen |
| Task 7.6 `seed-demo` | ein Agent (Sonnet) baut ihn auf Branch `feat/seed-demo`, noch kein PR | PR abwarten, lokal prüfen, mergen |

Danach in dieser Reihenfolge sinnvoll: offene Punkte von 6.6 (Benachrichtigung des Instanz-Admins bei
Warnungen, Kapitel 12.7, braucht eine Designentscheidung), Phase 7 ohne die Aufgaben, die den Eigentümer
brauchen (7.4 Repository aufräumen, 7.5 Code aufräumen, 7.7 Screenshots nach 7.6, 7.10 Abdeckung). 7.13
(CI wieder auf PRs) erst, wenn das Budget es erlaubt.

## 2. Arbeitsregeln (vom Eigentümer, nicht im Repo festgehalten)

- **Ziel des Auftrags:** den Masterplan systematisch umsetzen, mit PRs, eigene PRs bei grünen Prüfungen
  selbst mergen, Opus/Sonnet/Haiku je nach Aufgabe, **höchstens zwei Agents gleichzeitig** (auch keine
  Unter-Agents der Agents; einer hat das einmal getan und wurde gestoppt).
- **Nie force-push.** Commits enden mit `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`,
  PR-Texte mit `🤖 Generated with [Claude Code](https://claude.com/claude-code)`. Keine Modellnamen in
  Commits, PRs oder Code.
- **GitHub-Actions-Budget (seit 19.11.2026):** über 90 % verbraucht, wird nicht aufgefüllt. CI läuft
  **vorübergehend nur auf main**, nie auf PRs (#73, #74); Rücknahme ist Aufgabe 7.13 (#75). Deshalb:
  - jeden PR lokal prüfen und im PR-Text die gelaufenen Prüfungen nennen;
  - Merges bündeln: den ersten mit `gh pr merge N --merge --body "… [skip ci]"`, den letzten normal →
    ein Lauf auf main für alle (Concurrency bricht überholte Läufe ohnehin ab);
  - reine Doku-Änderungen (`docs/**`, `*.md`, `prototypes/**`) lösen nichts aus;
  - nie `gh run rerun` bei Flakes.
- **Vor dem Mergen sicherheitsrelevanter PRs eine unabhängige Prüfung** durch einen eigenen Agent (Opus),
  der nur den Code und die Spezifikation bekommt, nicht die Aussagen des Autors. Das hat bei #60, #65, #66,
  #71 und #72 jeweils echte Fehler gefunden (darunter Blocker).
- Entscheidungen E10 (Hub-Texte speichern erlaubt), E15 (Mockups, die ich oder die Agents machen, gelten
  als freigegeben; der Eigentümer prüft im Nachhinein), Loader-Variante «Draw», Quellen mit Betreiber und
  Trägerschaft, `catalog.product` mit Zusatzspalten: vom Eigentümer, stehen teils in ADR/Masterplan.

## 3. Umgebung dieser Sitzung (Cloud-Container, nicht dauerhaft)

- .NET: `export PATH=/home/user/.dotnet-sdk10:$PATH DOTNET_ROOT=/home/user/.dotnet-sdk10`. Node: `eval
  "$(fnm env)"; fnm use 24`. Docker läuft (Testcontainers, Playwright-Image v1.56.1 für die Flows).
- Das frühere Hilfsskript `env.sh` im Scratchpad wechselte mit `cd` ins Haupt-Checkout; das hat einmal
  dazu geführt, dass ein Agent im falschen Verzeichnis baute. Die `cd`-Zeile ist entfernt; Agents bekommen
  die Exports direkt.
- **Abnahme-Instanz** (Phasen 1–3): `<scratchpad>/acceptance/` mit `compose.yaml`,
  `compose.reference.yaml` (2 vCPU, 4 GB), `.env`/`.env.app`, `src/` (Kopie von main zum Bauen), `status.sh`.
  Gestoppt, Volumes behalten. Start: `ARDEA_IMAGE=ardea:acceptance docker compose -p ardea-acceptance -f
  compose.yaml -f compose.reference.yaml up -d`; Image neu bauen mit `docker build -f deploy/Dockerfile -t
  ardea:acceptance .` in `src/`. Enthält ~118 000 Records, 104 000 Einträge, das Projekt «Repositories of
  phase 2» mit 1 109 Komponenten aus actions/checkout, actions/setup-node und expressjs/express. Projekt-ID
  `01a1f2b4-cf7b-721c-aa54-3686b2ac9356`, Organisation `01a1f2b4-cf79-7673-aa31-03bf7364cf42`. Den
  Rückwärtsabgleich habe ich dort per Job-Zeile in `platform.job` angestossen (es gibt keinen Befehl dafür).
- **Spike-Daten** (11.–17.11.2026): `<scratchpad>/spike/11 … 17` (entpackte Delta-Archive, 168 MB). Daraus
  erzeugen `tools/reference-set/select_pairs.py` und `spike_week.py` die Dateien in `data/reference/`.
- Von hier aus nicht erreichbar: NCSC-Hub und BACS-Warnungen (Abruf scheitert in dieser Umgebung; offen,
  ob das an der Umgebung oder an den Quellen liegt). Ein `curl` mit Browser-User-Agent wurde von der
  Sitzungsumgebung blockiert; nicht wiederholen.
- In Tests war `max_connections=500` für Postgres nötig, sonst CSRF-Fehler durch erschöpfte Verbindungen.
- Claude-Ratenlimit: Agents brechen bei «5-hour limit» ab; nach dem Reset mit `SendMessage` an dieselbe
  Agent-ID fortsetzen, ihr Stand bleibt erhalten.

## 4. Offene Punkte für den Eigentümer (gesammelt)

Die meisten stehen in `docs/acceptance/phase-*.md`; hier alle an einem Ort:

- Phase 2: zeitgemessener Wizard-Durchgang mit einer Testperson; Ardeas eigenes (privates) Repository mit
  einem fine-grained Token einlesen.
- Phase 3: Labels des Referenzsatzes prüfen (250 Paare, von einem Modell beschriftet, `reviewed: false`);
  Mockups 3.1 gegen die gebauten Seiten prüfen; **Entscheidung zum Bündeln transitiver Pakete (16.8):** die
  drei Repositories ergaben 34 offene Massnahmen gegen 6 im Spike.
- Phase 4: Beobachtung mit einem lokalen Modell an 50 unbekannten Schlüsseln (Ablauf in
  `docs/operations/operations.md`); KI-Mockups prüfen; noch nie hat ein echtes Modell eine Einschätzung
  geliefert (alle Tests mit aufgezeichneten Antworten).
- Phase 5: Web Push von Hand auf iOS (installierte Web-App); echter Push-Dienst; Mockups der
  Benachrichtigungsseiten prüfen; Eskalation (8.7) ist nicht im MVP.
- Phase 6: 6.2 unabhängige Sicherheitsprüfung durch eine Person; 6.3 manueller Zugänglichkeitsdurchgang;
  6.4/6.8 Beobachtungen an der projekteigenen Instanz (braucht Infrastruktur des Eigentümers); 6.6
  Benachrichtigung des Instanz-Admins bei Warnungen (Designentscheidung).
- Erreichbarkeit von NCSC/BACS klären.

## 5. Bekannte Schwächen und technische Schulden (nicht oder nur verstreut dokumentiert)

- **Initial-Bundle der Web-App** liegt bei ~636 kB über dem Warnbudget von 600 kB (Fehlergrenze 1 MB).
  Ursache vor allem die eingebauten englischen Texte; neue Seiten sind lazy. Kein Fehler, aber wachsend.
- **#69** (seltener 500 durch einen verschmutzten Pool-Connector): wahrscheinlich durch #70 behoben (Live-
  Listener mit eigener Verbindung), Issue bleibt offen zur Beobachtung.
- **#52** (Flakes): Hauptursache in #59 behoben (doppelte Transaktions-Freigabe im UnitOfWork), Issue offen.
- **Quote je Massnahme bei der KI-Einschätzung**: bei Parallelität > 1 kann eine Massnahme pro parallelem
  Slot einen Aufruf zu viel bekommen (ADR 037, bewusst akzeptiert).
- **Version «.0»-Heuristik** wurde im Review von #60 durch eine bessere Regel ersetzt; die Kalibrierung ist
  in ADR 033 Entscheid 11 beschrieben. Der Referenzsatz deckt die Grenzfälle des Reviews nicht ab (die
  Fixes haben eigene Unit-Tests).
- **Gelernte Katalogeinträge**: eine Komponente, die auf einen gelernten Eintrag umgestellt wurde, bekommt
  die Roh-Schlüssel zum Zeitpunkt der Umstellung; später zugeordnete Roh-Schlüssel erst bei ihrer nächsten
  Änderung (#71).
- `repository.unavailable` ist in Regeln wählbar, wird aber noch nirgends ausgelöst.
- Der Restore-Weg aus der Doku (`pg_restore --clean --if-exists` über migrierte Tabellen) funktioniert nicht;
  #78 korrigiert die Anleitung (Datenbank neu anlegen, dann `pg_restore --no-owner`).

## 6. Fehlerklassen, die mehrfach vorkamen (für Reviews und Agent-Aufträge)

1. **Lesen nach `CommitAsync`**: der Commit schliesst die Verbindung; jede EF-Abfrage danach wirft
   `ObjectDisposedException` (zuletzt #76). Antworten vor dem Commit bauen.
2. **Connector-Wiederverwendung**: Transaktion nach dem Commit nochmals freigegeben (#59); LISTEN-Verbindung
   aus dem Pool (#70). Symptom: `ArgumentOutOfRangeException` in `NpgsqlDataReader.HandleUncommon`.
3. **Koaleszierte Jobs** (`ON CONFLICT DO NOTHING`) verloren Ereignisse aus noch offenen Transaktionen; seit
   #66 sperrt das Einreihen die wartende Zeile `FOR SHARE` bis zum Commit (ADR 006 Notiz, ADR 038).
4. **Transaktion offen während eines externen Aufrufs** (SMTP, Webhook, Push, Modell): kurzer Lesebereich,
   Aufruf, dann Schreiben; mehrfach in Reviews gefunden.
5. **Optimistische Statusprüfung ohne Sperre** (#56): `expectedStatus` unter der Komponentensperre prüfen.
6. **Tokens und Geheimnisse in Logs/Traces** (Webhook-URLs, Link-Tokens, Push-Endpunkte): durch
   `RemoveAllLoggers`, Request-Log-Filter und `OutboundAddresses.Untraced` behoben; bei neuen HTTP-Clients
   daran denken.
7. **ADR-Nummern** kollidieren, wenn zwei Agents parallel ADRs schreiben (040 doppelt, umbenannt auf 041).
   Agents die nächste freie Nummer auf main prüfen lassen und beim Mergen nachsehen.
8. **Veraltete Testbinaries** täuschen Fehler vor: vor dem Vertrauen auf ein Ergebnis neu bauen.

## 7. Ablauf, der sich bewährt hat

- Grosse Aufgaben an einen Agent (Opus für Backend/Sicherheit/UI, Sonnet für Doku, Inventur, klar
  umrissene Aufgaben), Review an einen zweiten unabhängigen Agent, Korrekturen an den ersten zurück.
- Beschriftung des Referenzsatzes durch einen Agent, der weder den Abgleich-Code noch Ergebnisse sieht.
- Nach jeder Zusammenführung mehrerer PRs: kombinierten Stand lokal bauen und ganze Testsuite laufen lassen
  (~10 min), erst dann mergen.

## 8. Lokale Reste

- `CSH-Alert/.claude/worktrees/agent-*`: ~20 Worktrees fertiger Agents; können gelöscht werden
  (`git worktree remove --force …`, dann `git worktree prune`), ihre Branches sind gepusht oder gemergt.
- `../wt-p6` (Branch `p6-check`, nur lokal) und `../wt-notes` (dieser Branch): nach Gebrauch löschen.
- Scratchpad: Logs, Hilfsskripte, Abnahme-Instanz, Spike-Daten; nichts davon gehört ins Repository.
