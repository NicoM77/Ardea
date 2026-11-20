# NOTES: Arbeitsstand und Wissen ausserhalb des Repositorys

Stand: 20.11.2026, Nachmittag (UTC), main = 4985227. Abschnitt 0 zuerst lesen.
Umsetzung weiss und was nicht schon im Repository steht (README, Masterplan, ADR, Abnahmen, PR-Texte,
Issues). Wo etwas im Repository steht, verweise ich nur darauf.

## 0. Stand 20.11.2026 (zuerst lesen)

- **Repository öffentlich**, Actions auf Standard-Runnern damit kostenlos; Billing-Sperre erledigt. PR-CI wieder an (7.13, #114). Trotzdem sparsam: lokal prüfen, keine unnötigen Reruns.
- **Einzige offene PR: #136 «chore(main): release 0.1.0»** (release-please). Eigentümer-Schritt: prüfen und mergen = erstes echtes Release (7.2/7.3), danach 7.11 Abnahme auf den Release-Images.
- **Website veröffentlicht (7.9):** https://nicom77.github.io/Ardea/ (Pages, build_type workflow). `site.yml` läuft bei Releases, bei Push auf main unter `docs/**`, `README.md`, `SECURITY.md`, `CONTRIBUTING.md`, `openapi/**`, `site/**` und per Hand; der Job Publish nur auf main/bei Releases (#142), so dass `gh workflow run site.yml --ref <branch>` eine Site-Änderung ohne Deploy prüft. Änderungen an `.github/workflows/site.yml` selbst lösen nichts aus → nach Bumps der Pages-Actions einmal per Hand auf main starten.
- **Site lokal nicht baubar** auf diesem Host (Astro/Node-Anforderungen, Image-Node zu alt); Satteri (Markdown-Compiler) läuft aber direkt mit Node 24, Plugins lassen sich so mit `markdownToHtml(src, { hastPlugins: [...] })` testen.
- **Heute gemergt:** #137 (Testuhr nur vorwärts), #139 (Kommentare, E2E-Wizard), #140 (5 Sicherheitsmeldungen: Link-Checker auf `ultrahtml`, npm-`overrides` für minimatch/glob), #141 (`pending_event.seq` für stabile Reihenfolge), #138 (site.yml-Trigger), #129 (NuGet.Versioning 8), #142 (Task-List-Checkboxen mit `aria-label`), #132/#133/#134 (checkout 7, upload-pages-artifact 5, deploy-pages 5). Main-CI grün.
- **Social Preview** (1280×640) ist auf main (#135, `docs/design/brand/ardea-social-preview.png`); Eigentümer lädt sie unter Settings → General → Social preview hoch.
- **Ältere Dependabot-Sichtung** (#115–#130) ist erledigt (gemergt oder geschlossen).
- **7.14 Branches (Entscheidliste 20.11.):** 102 in main gemergte Branches → löschen. Nicht gemergt: `NOTES` → behalten (Arbeitsnotizen, nie mergen); `assets/social-preview` → löschen (Inhalt per #135 auf main); `feat/platform-crosscutting` → löschen (PR #5 geschlossen, 0.5/0.6 anders geliefert); `release-please--branches--main` → gehört zu #136, nicht anfassen; Branches offener PRs bleiben. Löschen bleibt Eigentümer-Schritt (Sitzungsfilter blockiert `git push --delete`). «Automatically delete head branches» ist noch aus.
  ```bash
  git fetch --prune origin && git branch -r --merged origin/main | sed 's/^ *//' | grep -vE "origin/(HEAD|main)$" | grep -v " -> " | sed 's#^origin/##' | xargs -n 15 git push origin --delete
  git push origin --delete feat/platform-crosscutting
  ```
  `NOTES` bleibt. plus `git push origin --delete assets/social-preview`. Danach README-Zeile 7.14 ✅.
- **Lehren heute:** FakeTimeProvider startet bei echter Zeit → nie zurückstellen; `Guid.CreateVersion7()` ist innerhalb einer Millisekunde nicht monoton, nie danach sortieren; Kommentar-Prüfung (7.5) gilt auch für Testcode; Merge-Betreff immer `--subject "<Conventional Titel> (#N)"`.

## 0a. Eigentümer-Schritte (Reihenfolge, Stand 20.11. nachmittags)

1. ~~Billing~~, ~~öffentlich~~, ~~Website~~: erledigt.
2. **#136 prüfen und mergen** (Release 0.1.0); vorher CONTRIBUTING «Releases»-Checkliste (Actions dürfen PRs anlegen, nur Merge-Commits, Paket `ghcr.io/nicom77/ardea` nach dem Release öffentlich und verknüpft).
3. Danach 7.11 (Abnahme auf Release-Images amd64/arm64, `docs/acceptance/release-1.0.md`), 7.12 (Masterplan ins Englische übernehmen, dann löschen).
4. Branch-Schutz auf main (Pflicht-Checks `Backend`, `Dependencies`, `Web`, `Web under CSP`, `End to end`, `Image`), «Automatically delete head branches», 7.14 Branches löschen, Social Preview hochladen, Topics.
5. Bestätigen: E26-Rhythmus und 08:00; Masterplan-Änderungen aus #96; 5a.3-Entscheide; Mockup-Fragen; Lizenz-Ausnahmen in `site/license-exceptions.json`.
6. Menschen/Infrastruktur: 6.2, 6.3, 6.4/6.8 (eigene Instanz).

## 1. Arbeitsstand

| Was | Stand | Was als Nächstes zu tun wäre |
|---|---|---|
| #78 (6.4–6.8), #79 (6.1) | gemergt; der CI-Lauf auf main war abgebrochen, ist aber mit dem Lauf zu #82 (Run 44606106559, alles grün) nachgeholt | — |
| #81 (6.6, ADR 043) | **gemergt**: Selbstcheck alle 5 min in `api` und `worker` (Transaktions-Advisory-Lock), Mail an die Instanz-Admins bei neuer Warnung, Erinnerung nach 24 h, Entwarnung; Zustand in `platform.instance_warning`; `iam.instance_administrators()` gibt nur IDs. Unabhängig geprüft (1 Blocker: OpenAPI-CRLF; behoben) | — |
| #82 (7.6 `seed-demo`) | **gemergt**: Demo ohne Zuweisung/Frist/«in Prüfung», verweigert DB mit Daten, Worker-Herzschlag oder Jobs; Sammel-Mails an erfundene Personen abgeschaltet; KI-Policy `none`. Unabhängig geprüft (keine Blocker, 3 Should-fix behoben) | 5a passt die Demo an (Reminder, Hinweise) |
| #83 | gemergt: Stammdaten-Suche ignoriert `Data/` ohne Katalog (Windows) | — |
| #84 (5a.1, ADR 044), #86 (7.10 Teil 1), #87 (#85 TOTP einmalig, ADR 045), #88, #89 (5a.2, ADR 046), #90, #91 (Mockups 5a.5) | gemergt; #87 und #89 unabhängig geprüft (je 1 Blocker bzw. 4 Should-fix, behoben); main-Lauf zu #86 grün, zu #89 läuft | — |
| #92 (5a.4), #93 (5a.3, ADR 047), #94 (5a.5), #95 (Doku 5a, 6.7 ✅), #97 (7.10 Teil 2), #98 (7.4, Tag `archive/planning-and-design`) | gemergt, jeweils unabhängig geprüft (ausser Doku/Tests) und lokal kombiniert geprüft; **CI gesperrt (Billing)** | — |
| #96 (5a.6, ADR 048), #99 (Abnahme 5a), #101/#103/#105/#110 (7.5), #104 (7.10 ✅), #106 (7.7), #108 (7.8), #109 (#102, ADR 049), #112 (7.9 Bau, nicht veröffentlicht) | gemergt, lokal kombiniert geprüft | — |
| #111 (#107) | gemergt: seed-demo deterministisch; `JobStore.ClaimAsync` liefert geclaimte Jobs in priority/run_at/id-Reihenfolge (RETURNING behielt die Reihenfolge nicht) | — |
| **Rest braucht den Eigentümer** | 7.1 (Branch-Schutz, Dependabot, CodeQL, Beschreibung/Topics), 7.2/7.3 (release-please, Images, cosign), 7.9 Veröffentlichung (Pages für privates Repo, Actions), 7.13 (CI auf PRs), 7.14 (Branches, Einstellung «auto-delete»), danach 7.11 Abnahme auf Release-Images und 7.12 Masterplan löschen; 6.2/6.3 Personen; 6.4/6.8 eigene Instanz | |
| Phase 5a | neu seit #80 (Kapitel 17). **Eigentümer-Entscheid 19.11.: 5a vor Phase 7.** 5a.1 ✅, 5a.2 ✅; **Release-Sperre: kein Release/Upgrade zwischen 5a.2 und 5a.5** (ADR 046 Entscheid 13). 5a.3 (`feat/notices`, ADR 0047) und 5a.4 (`feat/stack-wizard`) laufen als Agents | dann 5a.5 (Seiten, Mockups in #91), 5a.6 |
| Phase 6 | 6.1, 6.5, 6.6 ✅; 6.4/6.8 brauchen die eigene Instanz; 6.2, 6.3 brauchen Menschen; 6.7 offen nur wegen `SECURITY.md`-Zuordnung (README-Absatz) | |
| Phase 7 | 7.6 ✅; 7.4, 7.5, 7.7, 7.8 erst **nach 5a** (5a baut Datenmodell und UI um); 7.10 jederzeit; 7.1/7.2/7.3/7.13 nur nach Rückfrage | |

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


## 3. Umgebung (seit 19.11.2026 abends: Windows-Rechner des Eigentümers)

- Arbeitsordner `C:\Dev\01_Mirello\Ardea` (der frühere Name `CSH-Alert` gilt nicht mehr). Git Bash und PowerShell.
- **.NET SDK 10.0.404** benutzerlokal in `C:\Users\nicom\.dotnet-sdk10` (mit `dotnet-install.ps1`, Erlaubnis des
  Eigentümers); das systemweite SDK 10.0.401 genügt `global.json` nicht. **Node 24** aus nvm
  (`C:\Users\nicom\AppData\Local\nvm\v24.21.0`), ohne `nvm use` (das ändert den globalen Link). Beides setzt
  `<scratchpad>/env.sh` (nur `export`, kein `cd`): `. <scratchpad>/env.sh`. Kein Python installiert; Skripte in
  Node oder C#.
- **Docker Desktop** muss laufen (Testcontainers, Playwright); bei Bedarf `Start-Process "C:\Program
  Files\Docker\Docker\Docker Desktop.exe"`.
- **Zeilenenden:** Git checkt hier mit `core.autocrlf=true` aus, `.editorconfig` verlangt LF. Folgen:
  `dotnet format` meldet im normalen Checkout jede Datei; Agents haben einmal eine README mit CRLF
  eingecheckt (#82, korrigiert). Prüf-Worktrees deshalb mit `git -c core.autocrlf=false worktree add …` und
  `git config core.autocrlf false`; vor dem Merge `git cat-file -p <branch>:<datei> | tr -cd '\r' | wc -c` für
  geänderte Dateien (nicht `grep -c $'\r'`, das zählt in Git Bash falsch).
- **OpenAPI unter Windows:** der Generator schrieb mehrzeilige Beschreibungen mit `\r\n`; seit #81 glättet
  `ApiDocument.Normalize` alle Zeichenketten auf `\n`.
- **Playwright** im Image `mcr.microsoft.com/playwright:v1.56.1-noble`: auf dem Host bauen (`npm run build`,
  ggf. Showcase-Build), dann mit dem Host-`node_modules` im Container:
  `MSYS_NO_PATHCONV=1 docker run --rm --ipc=host -e HOME=/tmp -e CI=true -v "<worktree als Windows-Pfad>":/repo
  -w /repo/web mcr.microsoft.com/playwright:v1.56.1-noble npx playwright test --project csp --project flows`
  (33/33 am 19.11.). Das Image hat Node 22.20, für `ng build` zu alt, für den Testlauf genügt es.
- **Spike-Daten** (11.–17.11.2026) neu geladen nach `<scratchpad>/spike/11 … 17` (Delta-Archive von
  `CVEProject/cvelistV5`, ~20 MB gezippt, Erlaubnis des Eigentümers).
- **Abnahme-Instanz** der Cloud-Umgebung existiert hier nicht. Neu aufbauen nur bei Bedarf: `docker build -f
  deploy/Dockerfile -t ardea:acceptance .`, Compose aus `deploy/`, Erstbefüllung (51 min auf 2 vCPU).
- Kombinierter Stand: ganze Backend-Suite ~10 min (1 382 Tests); `<scratchpad>/combined-tests.sh` läuft alle
  Testprojekte nacheinander in `<scratchpad>/wt-combined`.
- Parallele Testläufe zweier Agents auf diesem Rechner erzeugen gelegentlich Zeitüberschreitungen in
  `Ardea.Iam.Tests` (SignIn/TenantIsolation); allein laufen sie grün.
- Agents bekommen ihre Worktrees unter `.claude/worktrees/agent-*` (gesperrt, solange der Agent existiert).

## 4. Offene Punkte für den Eigentümer (gesammelt)

- **GitHub Actions gesperrt (seit 19.11.2026, 23:35 UTC):** Run 44638908047 (Merge #92) startete keinen Job: «recent account payments have failed or your spending limit needs to be increased». Nur der Eigentümer kann das unter Billing & plans lösen. Bis dahin ist die **lokale Prüfung des kombinierten Stands** (ganze Backend-Suite, Web, Playwright) das einzige Tor; #92 ist nur lokal geprüft (1 600 Backend-Tests, Playwright 38/38). Keine Reruns auslösen.

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
- Phase 5a: Erinnerungsrhythmus und «nur das Produkt selbst» (E26, 17.2) bestätigen. Schwellen aus 5a.1 (ADR 044: kritisch oder ausgenutzt, N = 25) am 19.11. bestätigt (#88).
- Phase 6: 6.2 unabhängige Sicherheitsprüfung durch eine Person (Umfang: Anmeldung, Mandantentrennung,
  ausgehende Verbindungen; Grundlage `docs/security/threat-review.md`); 6.3 manueller Zugänglichkeitsdurchgang
  der Kernabläufe mit Tastatur und Screenreader (sinnvoll erst nach 5a, weil 5a die Kernabläufe ändert);
  6.4/6.8 Beobachtungen an der projekteigenen Instanz (braucht Infrastruktur des Eigentümers).
- **Website (7.9):** `site/` (Astro/Starlight) baut 184 Seiten aus `docs/`; `.github/workflows/site.yml` nur manuell und als «NOT ENABLED» markiert; Lizenz-Ausnahmen im Bau (u. a. sharp-LGPL-Binaries, nie geladen) in `site/license-exceptions.json` zur Durchsicht.
- **README:** vollständige Roadmap bis 1.0 zugeklappt am Ende (Abweichung von 7.8, damit die Roadmap-Regel gilt); bei 1.0 auf «After 1.0» kürzen.
- **Issue #100:** Aufräumen der Übergangsspalten/-trigger aus ADR 046 in der Version nach 1.0.
- **5a.6 / E26 bestätigen:** Erinnerungsrhythmus Stufe 5 täglich, 4 wöchentlich, 3 monatlich, Läufe werktags 08:00 Ortszeit (08:00 vom Agent gewählt); Masterplan-Änderungen in #96 (8.7 eine Runde je Organisation statt Job je Reminder; Kap. 1, 9, 14, 15) zur Abnahme.
- **Mockups (alle Phasen) liegen nur noch am Tag `archive/planning-and-design`** (7.4, #98).
- **5a.3-Entscheide zur Abnahme:** «Betrifft uns» mit Version ohne Projekt macht den Stack-Eintrag exakt (17.1); später ergänzte Stack-Einträge bringen 80–500 «Earlier»-Hinweise aus 180 Tagen (ADR 047).
- **Mockups 5a.5 (#91, E15) prüfen**, offene Fragen: Tasten A/D kollidieren im Deutschen (besser 1/2?); braucht «Remediated» einen Grund (11.5 sagt ja, Mockup nein)?; drei Antworten auf jeder Karte oder hinter Details?; Erinnerungsrhythmus-Diagramm statt Fristen-Laufband ok?; Stack-Eintrag mit exakter Version im Stack lassen oder in ein Projekt verschieben?; Namenskonflikt `ui-notice` (Bibliothek) mit «Notice».
- Masterplan 9.2 hat durch #81 eine Zeile mehr (neue Systemoperation `iam.instance_administrators`); falls
  der Plan unverändert bleiben soll, sagen.
- Phase 7 nur nach Rückfrage: 7.1 (Branch-Schutz, Dependabot, CodeQL: Kosten), 7.2/7.3 (Release), 7.13.
- Erreichbarkeit von NCSC/BACS klären.

## 5. Bekannte Schwächen und technische Schulden (nicht oder nur verstreut dokumentiert)

- **Initial-Bundle der Web-App** liegt bei ~637 kB über dem Warnbudget von 600 kB (Fehlergrenze 1 MB).
  Ursache vor allem die eingebauten englischen Texte; neue Seiten sind lazy. Kein Fehler, aber wachsend.
- **NG8011** in `web/.../features/admin/operations-card.ts:18`: `<span uiCardMeta>` steht in einem `@if` mit
  mehreren Knoten und wird nicht in den Slot projiziert (seit #78). Kleiner echter Fehler; mit 7.5 beheben
  (Build soll ohne Warnung laufen).
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
- **#85 behoben (#87):** TOTP-Code einmal pro Konto; Spalte `totp_last_step` von EF nie geschrieben, Trigger `iam.keep_totp_last_step`, Zeilensperre pro Konto bei Code-Prüfungen. Per Konto, nicht per Schlüssel (2FA aus/ein innerhalb 30 s: erster Code des neuen Schlüssels wartet einen Schritt).
- **5a.2-Übergang (ADR 046):** Trigger `assets.component_fill_precision` und `assess.open_instead_of_review` nur für diese Version (4.11), mit `assigned_to`/`due_at`/`due_at_manual` und der `deadlineHours`-Toleranz in der nächsten Version entfernen.
- `repository.unavailable` ist in Regeln wählbar, wird aber noch nirgends ausgelöst.
- **Selbstcheck (ADR 043):** ein Worker-Neustart über 2 min ergibt eine Warn- und eine Entwarnungsmail;
  Uhrenversatz zwischen getrennten API- und Worker-Hosts lässt `worker_stalled` flattern (gleicher Host
  unkritisch); stirbt ein Prozess zwischen Commit und Versand, geht die Mail verloren (Warnung bleibt sichtbar).
- **seed-demo:** alle Zeitstempel sind «jetzt» (Massnahmen erscheinen als eben entstanden); für die
  Screenshots von 7.7 evtl. zurückdatieren (bräuchte eine verschobene Uhr im Job-System). Der Weg über das
  Docker-Image (`docker compose run … seed-demo`) ist nicht von Hand getestet, nur `dotnet run`.

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
9. **Zeilenenden unter Windows**: CRLF im Commit (README in #82) oder im erzeugten OpenAPI-Dokument (#81);
   vor dem Merge geänderte Dateien auf `` prüfen (Abschnitt 3).
10. **Demo- und Testdaten, die nach aussen wirken**: erfundene Adressen bekamen Sammel-Mails über
   Vorgaberegeln (#82); bei neuen Daten auch zeitgesteuerte Wege (Digests, Erinnerungen) prüfen.

## 7. Ablauf, der sich bewährt hat

- Grosse Aufgaben an einen Agent (Opus für Backend/Sicherheit/UI, Sonnet für Doku, Inventur, klar
  umrissene Aufgaben), Review an einen zweiten unabhängigen Agent, Korrekturen an den ersten zurück.
- Beschriftung des Referenzsatzes durch einen Agent, der weder den Abgleich-Code noch Ergebnisse sieht.
- Nach jeder Zusammenführung mehrerer PRs: kombinierten Stand lokal bauen und ganze Testsuite laufen lassen
  (~10 min), erst dann mergen.


## 8. Lokale Reste

- `.claude/worktrees/agent-a178f361aa5d345d7/site` und `agent-abba53403c95645aa/web`: Reste, die beim Aufräumen von einem laufenden Prozess belegt waren; Worktrees sind abgemeldet, Ordner später löschen.

- `<scratchpad>/wt-combined` (Branch `check/combined`, nur lokal) und `<scratchpad>/wt-notes` (dieser Branch):
  Prüf-Worktrees, nach Gebrauch löschen (`git worktree remove --force …`, `git branch -D check/combined`).
- `.claude/worktrees/agent-*`: Worktrees fertiger Agents (gesperrt); Branches sind gepusht/gemergt.
- Scratchpad: `env.sh`, Spike-Daten, Logs; nichts davon gehört ins Repository.
- Viele gemergte Remote-Branches; Aufräumen ist Aufgabe 7.14.
