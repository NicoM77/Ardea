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
     ├─ Reminders (Komponente in einem Projekt) mit Status
     └─ Regeln, Benachrichtigungsstand, Zustellungen, Webhook-Ziele
```

- Ein Benutzer kann mehreren Organisationen angehören und wechselt in der Oberfläche zwischen ihnen. Die aktive Organisation steht in der URL (`/o/{slug}/…`) und in jedem API-Pfad (`/api/v1/orgs/{orgId}/…`), nicht in einem versteckten Sitzungszustand.
- Die Rolle gilt je Organisation. Der erste Benutzer einer Instanz ist zusätzlich **Instanz-Administrator**: Er verwaltet Quellen, KI-Anbieter, SMTP und Registrierung, sieht aber keine Daten fremder Organisationen.

| Recht | Owner | Admin | Member | Viewer |
|---|:-:|:-:|:-:|:-:|
| Reminders, Meldungen, Projekte, Übersicht lesen | ● | ● | ● | ● |
| Status setzen, Notiz, Einschätzung korrigieren | ● | ● | ● | |
| Projekte und Komponenten pflegen, Repositories mit einem Projekt verbinden, Vorschläge bestätigen | ● | ● | ● | |
| Zugänge zu GitHub anlegen und löschen | ● | ● | | |
| Benachrichtigungsregeln, Webhook-Ziele | ● | ● | | |
| Mitglieder einladen und Rollen ändern | ● | ● | | |
| KI-Datenrichtlinie, MFA-Pflicht, Erinnerungsrhythmus | ● | ● | | |
| Owner übertragen | ● | | | |

Eine Organisation zu exportieren oder zu löschen ist im MVP ein Wartungsbefehl des Betreibers (Kapitel 12.8); über die Oberfläche kommt es mit Ausbaustufe 1.

Rechte je Projekt (ein Dienstleister sieht nur «sein» Projekt) sind eine Ausbaustufe. Das Datenmodell lässt sie zu, weil jeder Befund sein Projekt trägt.

Registrierung ist pro Instanz einstellbar: offen, nur auf Einladung (Vorgabe nach dem ersten Benutzer) oder geschlossen. Eine frische Instanz nimmt den ersten Benutzer nur mit einem **Einrichtungstoken** an, das beim ersten Start im Log steht oder per Umgebungsvariable gesetzt wird. So kann niemand eine eben gestartete, schon erreichbare Instanz übernehmen.

## 9.2 Trennung der Mandanten

Jede Tabelle gehört zu einer von drei Arten (Kapitel 4.1), und jede Art hat eine Zugriffsregel:

| Daten | Wer liest und schreibt |
|---|---|
| Organisation | nur mit Mandantenkontext. Er entsteht in der API aus der geprüften Mitgliedschaft, im Worker aus der Organisation des Jobs. |
| Benutzer | der Benutzer selbst, über die Pfade unter `/account` und `/me`. Dazu genau drei weitere Zugriffe: Mitglieder derselben Organisation lesen über das Mitgliederverzeichnis Name, E-Mail-Adresse und Rolle, sonst nichts. Der Worker schreibt In-App-Benachrichtigungen und liest Push-Abos und Stummschaltungen für einen benannten Empfänger innerhalb des Mandantenkontexts seines Jobs. Die Anmeldung sucht ein Konto über seine E-Mail-Adresse, bevor ein Benutzerkontext besteht. |
| Global: Einträge, Quellen, Katalog | lesbar für jeden Angemeldeten; schreibbar für den Worker und, bei Quellen, für den Instanz-Administrator. |
| Global: Jobs, Zeitpläne, Einstellungen der Instanz | nur Worker und Instanz-Administrator. Jobs tragen nur Kennungen. |

**Zwei Schichten sind Pflicht, eine dritte kommt nach einem Versuch hinzu.**

| Schicht | Mechanismus | Schützt vor |
|---|---|---|
| 1 Zugriff | Jeder Endpunkt unter `/orgs/{orgId}` prüft Mitgliedschaft und Rolle, bevor ein Handler läuft. Kennungen in Anfragekörpern (Projekt, Komponente, Regel, Ziel) werden innerhalb des Mandanten aufgelöst; eine fremde Kennung ist «nicht gefunden». | Aufruf mit fremder Organisations- oder Objektkennung |
| 2 Abfrage | Benannte Abfragefilter in EF Core 10 auf jeder Entität der Arten Organisation und Benutzer. Ohne Kontext wirft der `DbContext`. | vergessene `WHERE`-Bedingung |
| 3 Datenbank | Row-Level Security auf denselben Tabellen. Die Anwendung verbindet sich als Rolle ohne Eigentum und ohne `BYPASSRLS`. | Fehler in Schicht 2, rohes SQL, künftige Abfragen ausserhalb von EF |

Die Recherche fand für Schicht 3 keine Primärquelle, die das Zusammenspiel von Row-Level Security, Verbindungspool und EF Core beschreibt, und stuft sie als Härtung ein, nicht als Fundament. Der Plan folgt dem: Die Schichten 1 und 2 mit einem automatischen Fremdzugriffstest für jeden Endpunkt sind das Fundament. Schicht 3 wird eingeführt, wenn der Versuch 0.6 in Phase 0 zeigt, dass sie mit der unten beschriebenen Arbeitseinheit sauber zusammenspielt. Sie schützt vor Programmierfehlern, nicht vor einem Angreifer, der bereits beliebiges SQL ausführen kann.

**Eine Arbeitseinheit, eine Transaktion.** Die Plattform stellt Verbindung und Transaktion bereit, setzt darin Organisation und Benutzer, und alle Modul-`DbContext` der Arbeitseinheit benutzen sie gemeinsam. Was eine Arbeitseinheit ist:

| Fall | Arbeitseinheit |
|---|---|
| Anfrage | die ganze Anfrage. Ausnahme: Der Strom der Server-Sent Events hält keine Transaktion offen; er liest in kurzen eigenen. |
| Job ohne Aussenkontakt | der ganze Job: Zustand und Folgejobs entstehen gemeinsam oder gar nicht. |
| Job mit Aussenkontakt (Abruf, Aufruf des Sprachmodells, Versand) | drei Schritte: kurz lesen, dann ohne offene Transaktion nach aussen sprechen, dann Ergebnis und Folgejobs in einer Transaktion schreiben. Wiederholungen sind über die Schlüssel der Kette unschädlich (Kapitel 3.3). |
| Abruf mit Haltepunkten | jede Strecke bis zum nächsten Haltepunkt ist eine eigene Transaktion aus Rohdatensätzen, Cursor und Folgejobs (Kapitel 5.2). |

Keine Transaktion bleibt über einen Aufruf nach aussen offen, und Zeilensperren wie die des Nummernzählers liegen immer im schreibenden Schritt. Die Verlängerung einer Lease läuft auf einer eigenen Verbindung. Der Mandantenkontext wird zu Beginn jeder Transaktion gesetzt, nicht je Verbindung.

**Systemoperationen.** Mehrere Abläufe müssen über Organisationen hinweg fragen. Sie folgen einem Grundsatz: Ein Systemjob ermittelt nur, *welche* Organisationen betroffen sind, und reiht je Organisation einen Job ein. Gelesen und geschrieben wird ausschliesslich in Jobs mit Mandantenkontext.

| Ablauf | Was organisationsübergreifend gefragt wird |
|---|---|
| Kandidatensuche | Welche Organisationen, Projekte und Komponenten führen diese Produktschlüssel? |
| Neubewertung, Rückzug eines Eintrags | Welche Organisationen haben Befunde zu diesem Eintrag? |
| Neues Regelwerk | Welche Organisationen haben offene Befunde? |
| Warnung ohne Kennung | Welche Organisationen haben eine Regel für Warnungen? |
| Sammelnachrichten, Eskalation | Welche Ziele sind jetzt fällig? |
| Repositories einlesen | Welche Verknüpfungen sind jetzt fällig? |
| Anmeldung | Welches Konto gehört zu dieser E-Mail-Adresse, und welchen Organisationen gehört es an? (benutzerbezogene Regel auf `membership`) |
| Einladung annehmen | Zu welcher Organisation gehört dieser Token? |
| Kennzahlen der Instanz | Zählwerte ohne Inhalte |
| Selbstprüfung der Instanz | Welche Konten sind Instanz-Administratoren? (nur Kennungen; gelesen wird je Konto mit diesem Konto als Benutzer, ADR 043) |

Jede dieser Fragen ist eine eigene, schmale Datenbankfunktion, die nur Kennungen oder Zählwerte liefert. Die Liste ist abschliessend; eine neue Systemoperation verlangt eine neue Funktion und einen ADR. Empfänger einer Regel werden nicht organisationsübergreifend aufgelöst: Das geschieht im Job der Organisation über das Mitgliederverzeichnis.

Export und Löschen einer Organisation sowie das Aufräumen alter Daten sind keine Systemoperationen des Betriebs. Sie laufen als Wartungsbefehl mit der Eigentümerrolle.

**Datenbankrollen.** `ardea_migrator` besitzt die Tabellen; sein Passwort kennen nur der Befehl `migrate` und die Wartungsbefehle. `ardea_app` wird von API und Worker benutzt und hat ein eigenes Passwort. Ein Test im Build prüft, dass jede Tabelle einer der drei Arten zugeordnet ist und dass der Fremdzugriffstest jeden Endpunkt abdeckt, auch mit fremden Kennungen im Anfragekörper.

## 9.3 Authentifizierung

| Thema | Entscheidung |
|---|---|
| Grundlage | ASP.NET Core Identity mit eigenen Endpunkten auf `SignInManager` und `UserManager`. `MapIdentityApi` wird nicht verwendet: kein Logout, keine Passkeys, nicht anpassbar. |
| Sitzung im Browser | Cookie `__Host-ardea`: `HttpOnly`, `Secure`, `SameSite=Lax`, gleitend 12 Stunden, absolut 14 Tage. API-Endpunkte antworten mit 401/403 statt Umleitung. Passwortwechsel, MFA-Änderung und «überall abmelden» entwerten alle Sitzungen über den Sicherheitsstempel. |
| CSRF | Antiforgery-Token im Header für alle ändernden Anfragen, zusätzlich Prüfung von `Sec-Fetch-Site` und `Origin`. Angular überträgt das Token über seinen eingebauten XSRF-Mechanismus, sobald Cookie- und Headername übereinstimmen. |
| Passwort | Mindestlänge 12, keine Zusammensetzungsregeln, Abgleich mit einer mitgelieferten Liste häufiger Passwörter. Hashing nach Identity-Vorgabe (PBKDF2), Iterationen konfigurierbar. |
| MFA | TOTP mit Wiederherstellungscodes im MVP. Eine Organisation kann MFA für ihre Mitglieder verlangen. |
| Passkeys | Ausbaustufe 1, über die in .NET 10 eingebauten Passkey-Dienste. |
| Fremdanmeldung | Ausbaustufe 1: ein generischer OIDC-Anbieter (Authentik, Keycloak, Entra ID) per Konfiguration. |
| Native App | Später: eigene Token-Endpunkte mit kurzlebigem Zugriffstoken und rotierendem Refresh-Token, gespeichert im sicheren Speicher des Geräts. |
| Missbrauch | Sperre nach Fehlversuchen, Ratenbegrenzung auf Anmeldung, Registrierung und Passwort-Reset, gleichlautende Antworten unabhängig davon, ob ein Konto existiert. |

## 9.4 Schutz sensibler Daten

| Daten | Einstufung | Schutz |
|---|---|---|
| Geheimnisse: SMTP-Passwort, KI-Schlüssel, Webhook-URLs und -Geheimnisse, VAPID-Privatschlüssel, Tokens für GitHub | kritisch | In der Datenbank verschlüsselt über ASP.NET Data Protection. Deren Schlüssel liegen in der Datenbank und sind mit einem Hauptschlüssel aus `ARDEA_MASTER_KEY` oder einer Secret-Datei geschützt. Nie im Klartext in API-Antworten oder Logs. |
| Technologieprofile einschliesslich eingelesener Abhängigkeiten und gespeicherter SBOMs, Meldungen, Einschätzungen | hoch | Mandantentrennung (9.2), Verschlüsselung des Datenträgers durch den Betreiber, keine Inhalte in Logs oder Telemetrie. Quellcode wird nicht gespeichert (Kapitel 16.9). |
| Konten: E-Mail, Passwort-Hash, MFA-Schlüssel | hoch | Identity-Standard; MFA-Schlüssel zusätzlich verschlüsselt. |
| Quellen, Einträge, Katalog | öffentlich | keine besonderen Reminders |

Dazu:

- **Datensparsamkeit gegenüber der KI:** Nur getroffene Komponenten verlassen das System, Projektnamen als Platzhalter; die Organisation wählt `keine KI`, `nur lokal` oder `Cloud erlaubt` (Kapitel 7.8).
- **Keine Rückmeldung an das Projekt:** Die Anwendung sendet nichts an ihre Entwickler. Telemetrie geht nur an einen Endpunkt, den der Betreiber selbst setzt.
- **Audit-Protokoll:** Anmeldungen, Rollenänderungen, Änderungen an Profil, Regeln und Einstellungen sowie Statuswechsel, je mit Benutzer, Zeit, Organisation und Herkunft. Unveränderlich für die Anwendung; Löschung nur über Aufbewahrungsfristen.
- **Löschen und Exportieren:** Eine Organisation lässt sich vollständig exportieren und löschen; die globalen Daten bleiben unberührt.

## 9.5 Nicht vertrauenswürdige Inhalte und ausgehende Verbindungen

Das System liest fremde Inhalte und ruft fremde Adressen auf. Beides ist Angriffsfläche.

| Risiko | Massnahme |
|---|---|
| Schadcode in Advisory-Texten (gespeichertes XSS) | HTML wird beim Normalisieren auf eine Positivliste reduziert; die Oberfläche bindet Texte nie als HTML ein, ausser dem bereinigten Beschreibungsfeld; strikte Content Security Policy ohne Inline-Skripte. |
| Anweisungen an das Sprachmodell in Quelltexten | Keine Werkzeuge für das Modell, Schema-Prüfung der Ausgabe, kein «nicht betroffen» ohne Bestätigung durch einen Menschen, Untergrenzen im Regelwerk (Kapitel 7.5, 7.6). |
| Webhook oder Push-Abo als Sprungbrett ins interne Netz (SSRF) | Webhook-Ziele werden aufgelöst und gegen private, lokale und Metadaten-Adressbereiche geprüft; die geprüfte Adresse wird für die Verbindung verwendet. Interne Ziele nur über eine Freigabeliste des Instanz-Administrators. Keine Weiterleitungen. Push-Abos werden nur für die Push-Dienste der Browserhersteller angenommen. |
| Manipulierte oder übergrosse Antworten von Quellen | Adapter rufen nur die konfigurierten Hosts auf; Grössen- und Zeitlimits; XML ohne DTD und externe Entitäten; Entpacken mit Obergrenzen für Grösse und Dateizahl. |
| Manifeste, Lockdateien und SBOMs aus Repositories | Nur lesen, nie ausführen: kein Build, keine Installation, kein Skript. Grenzen für Zahl und Grösse der Dateien; JSON- und XML-Leser ohne DTD. Ziele fest auf die API von GitHub und die Download-Hosts der SBOM; Adressen für GitHub Enterprise Server nur vom Instanz-Administrator (Kapitel 16.9). |
| Kompromittierte Quelle liefert falsche Daten | Rangfolge der Quellen, Herkunft je Feld sichtbar. Ein Anhalten bei auffälligen Massenänderungen ist eine Ausbaustufe. |

## 9.6 Härtung der Anwendung

- **Header:** Content Security Policy, `X-Content-Type-Options`, `Referrer-Policy`, `Permissions-Policy`, `frame-ancestors 'none'`. HSTS setzt der vorgeschaltete Proxy; die Dokumentation liefert Beispiele.
- **Container:** läuft als nicht privilegierter Benutzer auf einem Image ohne Shell und Paketmanager, Dateisystem schreibgeschützt, keine zusätzlichen Capabilities.
- **Datenbank:** nur im Compose-Netz erreichbar, kein veröffentlichter Port in der Vorgabe.
- **Lieferkette:** Lockfiles für NuGet und npm; automatische Abhängigkeits-Updates; Lizenzprüfung im Build gegen eine Positivliste (Kapitel 13); SBOM je Release; signierte Images mit Herkunftsnachweis (Aufgabe 7.3).
- **Selbstanwendung:** Das Projekt verbindet sein eigenes Repository mit einer eigenen Instanz und meldet sich Lücken in seinen Abhängigkeiten selbst.
- **Meldeweg:** `SECURITY.md` mit Kontakt und Frist für die Antwort; koordinierte Offenlegung.

## 9.7 Bedrohungen im Überblick

| Bedrohung | Wirkung | Wichtigste Gegenmassnahmen |
|---|---|---|
| Mitglied von A liest Daten von B | Offenlegung der Angriffsfläche | Zugriffsprüfung und Abfragefilter, Row-Level Security nach bestandenem Versuch, automatischer Fremdzugriffstest |
| Kontoübernahme | Zugriff auf alle Organisationen des Kontos | MFA, Sperre, Ratenbegrenzung, Sitzungsentwertung |
| Frische Instanz wird gekapert | vollständige Kontrolle | Einrichtungstoken |
| Datenbank-Dump gerät in falsche Hände | Profile lesbar, Geheimnisse nicht | Spaltenverschlüsselung der Geheimnisse, Hauptschlüssel ausserhalb der Datenbank, Empfehlung zur Datenträgerverschlüsselung |
| Unterdrückung einer Warnung über manipulierten Quelltext | übersehene Lücke | das Modell kann Stufe 1 nicht allein setzen; Untergrenzen im Regelwerk; Schema-Prüfung |
| Alarmflut durch fehlerhafte oder böswillige Quelle | Abstumpfung, Ausfall | Benachrichtigung je Reminder statt je CVE, Wesentlichkeitsprüfung, Obergrenzen je Kanal |
| Missbrauch als Proxy ins interne Netz | Zugriff auf interne Dienste | SSRF-Schutz für Webhooks, Quellen und Code-Plattformen |
| Abgezogenes Token für GitHub | Lesezugriff auf Quellcode der Organisation | feingranular, nur lesend, nur ausgewählte Repositories, mit Ablauf; verschlüsselt, nie angezeigt, nie geloggt |
| Abgezogene Sitzung oder Token | Handeln im Namen des Benutzers | `HttpOnly`-Cookie, CSP, kurze Laufzeit, CSRF-Schutz |
| Schädliche Abhängigkeit | Codeausführung im System | Lockfiles, Lizenz- und Update-Automatik, minimales Image |
