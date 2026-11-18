# 9 · Mandantenfähigkeit und Sicherheitskonzept

Ein Technologieprofil ist eine Karte der Angriffsfläche einer Organisation. Wer die Profile mehrerer Organisationen in einer Datenbank hält, muss ihre Trennung und ihren Schutz als Kernfunktion behandeln.

## 9.1 Mandantenmodell

```
Instanz
 ├─ Benutzer (global; ein Konto, mehrere Mitgliedschaften)
 ├─ Quellen, Einträge, Katalog (global, für alle gleich)
 └─ Organisation
     ├─ Mitgliedschaften mit Rolle
     ├─ Projekte → Komponenten
     ├─ Meldungen → Befunde, Einschätzungen, Verlauf
     └─ Regeln, Zustellungen, Webhook-Ziele
```

- Ein Benutzer kann mehreren Organisationen angehören und wechselt in der Oberfläche zwischen ihnen. Die aktive Organisation steht in der URL (`/o/{slug}/…`) und in jedem API-Pfad (`/api/v1/orgs/{orgId}/…`), nicht in einem versteckten Sitzungszustand.
- Die Rolle gilt je Organisation. Der erste Benutzer einer Instanz ist zusätzlich **Instanz-Administrator**: Er verwaltet Quellen, KI-Anbieter, SMTP und Registrierung, sieht aber keine Daten fremder Organisationen.

| Recht | Owner | Admin | Member | Viewer |
|---|:-:|:-:|:-:|:-:|
| Meldungen, Projekte, Bulletin lesen | ● | ● | ● | ● |
| Status setzen, zuweisen, Notiz, Einschätzung korrigieren | ● | ● | ● | |
| Projekte und Komponenten pflegen | ● | ● | ● | |
| Benachrichtigungsregeln, Webhook-Ziele | ● | ● | | |
| Mitglieder einladen und Rollen ändern | ● | ● | | |
| KI-Datenrichtlinie, MFA-Pflicht, Fristen | ● | ● | | |
| Organisation löschen, Owner übertragen | ● | | | |

Rechte je Projekt (ein Dienstleister sieht nur «sein» Projekt) sind eine Ausbaustufe. Das Datenmodell lässt sie zu, weil jeder Befund sein Projekt trägt.

Registrierung ist pro Instanz einstellbar: offen, nur auf Einladung (Vorgabe nach dem ersten Benutzer) oder geschlossen. Eine frische Instanz nimmt den ersten Benutzer nur mit einem **Einrichtungstoken** an, das beim ersten Start im Log steht oder per Umgebungsvariable gesetzt wird. So kann niemand eine eben gestartete, schon erreichbare Instanz übernehmen.

## 9.2 Trennung der Mandanten in drei Schichten

| Schicht | Mechanismus | Schützt vor |
|---|---|---|
| 1 Zugriff | Jeder Endpunkt unter `/orgs/{orgId}` prüft Mitgliedschaft und Rolle, bevor ein Handler läuft. Der Mandantenkontext entsteht nur aus dieser Prüfung. | Aufruf mit fremder Organisations-ID |
| 2 Abfrage | Benannte Abfragefilter in EF Core 10 auf jeder mandantenbezogenen Entität. Ohne Mandantenkontext wirft der `DbContext`. | vergessene `WHERE`-Bedingung |
| 3 Datenbank | Row-Level Security in PostgreSQL auf jeder Tabelle mit `organization_id`. Die Anwendung verbindet sich als Rolle ohne Eigentum und ohne `BYPASSRLS`; der Mandant wird je Transaktion gesetzt. | Fehler in Schicht 2, rohes SQL, künftige Abfragen ausserhalb von EF |

Row-Level Security schützt vor Programmierfehlern, nicht vor einem Angreifer, der bereits beliebiges SQL ausführen kann. Sie ist die Rückversicherung, nicht die erste Verteidigung.

**Umsetzung der Schicht 3.** Zwei Datenbankrollen: `csh_migrator` besitzt die Tabellen und führt Migrationen aus; `csh_app` wird von API und Worker benutzt. Jede Arbeitseinheit öffnet eine Transaktion und setzt `app.org_id` für deren Dauer. Die Richtlinie lautet sinngemäss `organization_id = current_setting('app.org_id')::uuid`. Ein Test im Build prüft, dass jede Tabelle mit `organization_id` eine aktive, erzwungene Richtlinie hat.

**Die eine Ausnahme.** Die Kandidatensuche muss für einen geänderten Eintrag über alle Organisationen hinweg fragen, wer ein Produkt einsetzt. Dafür gibt es genau eine Datenbankfunktion mit erhöhten Rechten, die zu einer Liste von Produktschlüsseln die Tripel aus Organisation, Projekt und Komponente liefert und nichts sonst. Alles Weitere läuft danach wieder im Kontext der jeweiligen Organisation.

**Offener Punkt.** Das Zusammenspiel von RLS, Verbindungspool und EF Core ist nicht in einer offiziellen Quelle beschrieben (Kapitel 2). Ein Spike in Phase 0 klärt es. Scheitert er, bleiben die Schichten 1 und 2, ergänzt um einen automatischen Test, der für jeden Endpunkt den Zugriff mit einer fremden Organisation versucht.

## 9.3 Authentifizierung

| Thema | Entscheidung |
|---|---|
| Grundlage | ASP.NET Core Identity mit eigenen Endpunkten auf `SignInManager` und `UserManager`. `MapIdentityApi` wird nicht verwendet: kein Logout, keine Passkeys, nicht anpassbar. |
| Sitzung im Browser | Cookie `__Host-csh`: `HttpOnly`, `Secure`, `SameSite=Lax`, gleitend 12 Stunden, absolut 14 Tage. API-Endpunkte antworten mit 401/403 statt Umleitung. Passwortwechsel, MFA-Änderung und «überall abmelden» entwerten alle Sitzungen über den Sicherheitsstempel. |
| CSRF | Antiforgery-Token im Header für alle ändernden Anfragen, zusätzlich Prüfung von `Sec-Fetch-Site` und `Origin`. Angular sendet das Token von Haus aus mit. |
| Passwort | Mindestlänge 12, keine Zusammensetzungsregeln, Abgleich mit einer mitgelieferten Liste häufiger Passwörter. Hashing nach Identity-Vorgabe (PBKDF2), Iterationen konfigurierbar. |
| MFA | TOTP mit Wiederherstellungscodes im MVP. Eine Organisation kann MFA für ihre Mitglieder verlangen. |
| Passkeys | Ausbaustufe 1, über die in .NET 10 eingebauten Passkey-Dienste. |
| Fremdanmeldung | Ausbaustufe 1: ein generischer OIDC-Anbieter (Authentik, Keycloak, Entra ID) per Konfiguration. |
| Native App | Später: eigene Token-Endpunkte mit kurzlebigem Zugriffstoken und rotierendem Refresh-Token, gespeichert im sicheren Speicher des Geräts. |
| Missbrauch | Sperre nach Fehlversuchen, Ratenbegrenzung auf Anmeldung, Registrierung und Passwort-Reset, gleichlautende Antworten unabhängig davon, ob ein Konto existiert. |

## 9.4 Schutz sensibler Daten

| Daten | Einstufung | Schutz |
|---|---|---|
| Geheimnisse: SMTP-Passwort, KI-Schlüssel, Webhook-URLs und -Geheimnisse, VAPID-Privatschlüssel | kritisch | In der Datenbank verschlüsselt über ASP.NET Data Protection. Deren Schlüssel liegen in der Datenbank und sind mit einem Hauptschlüssel aus `CSH_MASTER_KEY` oder einer Secret-Datei geschützt. Nie im Klartext in API-Antworten oder Logs. |
| Technologieprofile, Meldungen, Einschätzungen | hoch | Mandantentrennung (9.2), Verschlüsselung des Datenträgers durch den Betreiber, keine Inhalte in Logs oder Telemetrie. |
| Konten: E-Mail, Passwort-Hash, MFA-Schlüssel | hoch | Identity-Standard; MFA-Schlüssel zusätzlich verschlüsselt. |
| Quellen, Einträge, Katalog | öffentlich | keine besonderen Massnahmen |

Dazu:

- **Datensparsamkeit gegenüber der KI:** Nur getroffene Komponenten verlassen das System, Projektnamen als Platzhalter; die Organisation wählt `keine KI`, `nur lokal` oder `Cloud erlaubt` (Kapitel 7.7).
- **Kein Heimtelefon:** Die Anwendung sendet nichts an das Projekt. Telemetrie geht nur an einen Endpunkt, den der Betreiber selbst setzt.
- **Audit-Protokoll:** Anmeldungen, Rollenänderungen, Änderungen an Profil, Regeln und Einstellungen sowie Statuswechsel, je mit Benutzer, Zeit, Organisation und Herkunft. Unveränderlich für die Anwendung; Löschung nur über Aufbewahrungsfristen.
- **Löschen und Exportieren:** Eine Organisation lässt sich vollständig exportieren und löschen; die globalen Daten bleiben unberührt.

## 9.5 Nicht vertrauenswürdige Inhalte und ausgehende Verbindungen

Das System liest fremde Inhalte und ruft fremde Adressen auf. Beides ist Angriffsfläche.

| Risiko | Massnahme |
|---|---|
| Schadcode in Advisory-Texten (gespeichertes XSS) | HTML wird beim Normalisieren auf eine Positivliste reduziert; die Oberfläche bindet Texte nie als HTML ein, ausser dem bereinigten Beschreibungsfeld; strikte Content Security Policy ohne Inline-Skripte. |
| Anweisungen an das Sprachmodell in Quelltexten | Keine Werkzeuge für das Modell, Schema-Prüfung der Ausgabe, deterministische Untergrenzen (Kapitel 7.4, 7.5). |
| Webhook als Sprungbrett ins interne Netz (SSRF) | Ziele werden aufgelöst und gegen private, lokale und Metadaten-Adressbereiche geprüft; die geprüfte Adresse wird für die Verbindung verwendet. Interne Ziele nur über eine Freigabeliste des Instanz-Administrators. Keine Weiterleitungen. |
| Manipulierte oder übergrosse Antworten von Quellen | Adapter rufen nur die konfigurierten Hosts auf; Grössen- und Zeitlimits; XML ohne DTD und externe Entitäten; Entpacken mit Obergrenzen für Grösse und Dateizahl. |
| Kompromittierte Quelle liefert falsche Daten | Rangfolge der Quellen, Herkunft je Feld sichtbar, auffällige Massenänderungen (viele Rückzüge, Stufensprünge) halten die Verarbeitung an und warnen den Instanz-Administrator. |

## 9.6 Härtung der Anwendung

- **Header:** Content Security Policy, `X-Content-Type-Options`, `Referrer-Policy`, `Permissions-Policy`, `frame-ancestors 'none'`. HSTS setzt der vorgeschaltete Proxy; die Dokumentation liefert Beispiele.
- **Container:** läuft als nicht privilegierter Benutzer auf einem Image ohne Shell und Paketmanager, Dateisystem schreibgeschützt, keine zusätzlichen Capabilities.
- **Datenbank:** nur im Compose-Netz erreichbar, kein veröffentlichter Port in der Vorgabe.
- **Lieferkette:** Lockfiles für NuGet und npm; automatische Abhängigkeits-Updates; Lizenzprüfung im Build gegen eine Positivliste (Kapitel 13); SBOM je Release; signierte Images.
- **Selbstanwendung:** Das Projekt pflegt das eigene Technologieprofil in einer eigenen Instanz und meldet sich Lücken in seinen Abhängigkeiten selbst.
- **Meldeweg:** `SECURITY.md` mit Kontakt und Frist für die Antwort; koordinierte Offenlegung.

## 9.7 Bedrohungen im Überblick

| Bedrohung | Wirkung | Wichtigste Gegenmassnahmen |
|---|---|---|
| Mitglied von A liest Daten von B | Offenlegung der Angriffsfläche | drei Schichten der Trennung, automatischer Fremdzugriffstest |
| Kontoübernahme | Zugriff auf alle Organisationen des Kontos | MFA, Sperre, Ratenbegrenzung, Sitzungsentwertung |
| Frische Instanz wird gekapert | vollständige Kontrolle | Einrichtungstoken |
| Datenbank-Dump gerät in falsche Hände | Profile lesbar, Geheimnisse nicht | Spaltenverschlüsselung der Geheimnisse, Hauptschlüssel ausserhalb der Datenbank, Empfehlung zur Datenträgerverschlüsselung |
| Unterdrückung einer Warnung über manipulierten Quelltext | übersehene Lücke | Untergrenzen im Regelwerk, Schema-Prüfung, Herkunft sichtbar |
| Alarmflut durch fehlerhafte oder böswillige Quelle | Abstumpfung, Ausfall | Wesentlichkeitsprüfung, Obergrenzen je Kanal, Anhalten bei Massenänderungen |
| Missbrauch als Proxy ins interne Netz | Zugriff auf interne Dienste | SSRF-Schutz für Webhooks und Quellen |
| Abgezogene Sitzung oder Token | Handeln im Namen des Benutzers | `HttpOnly`-Cookie, CSP, kurze Laufzeit, CSRF-Schutz |
| Schädliche Abhängigkeit | Codeausführung im System | Lockfiles, Lizenz- und Update-Automatik, minimales Image |
