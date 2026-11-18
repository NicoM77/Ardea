# 11 · Frontend, UX und Designsystem

## 11.1 Stand

Die visuelle Richtung ist gewählt und als Mockups ausgearbeitet.

| Ergebnis | Ort |
|---|---|
| Produktgrundlage (Nutzer, Zweck, Prinzipien, bindende Vorgaben) | [`PRODUCT.md`](../../PRODUCT.md) |
| Designsystem als verbindliche Referenz: Tokens, Komponenten, benannte Regeln | [`DESIGN.md`](../../DESIGN.md), maschinenlesbar in `.impeccable/design.json` |
| Mockups des Kernflusses: Bulletin, Meldungen, Meldung, Designsystem-Seite | [`docs/design/mockups/`](../design/mockups/) (statisches HTML und CSS, im Browser zu öffnen) |
| Bildtafel der Mockups | https://claude.ai/artifact/X8uDvTNkNao2FocrXbnTMf |

**Wie die Richtung entstand.** Aus der Welt der Nutzer wurden sieben Kandidaten abgeleitet, ein Zufallswurf wählte einen davon aus, damit nicht jede Planung beim naheliegendsten landet; dazu kamen sechs Herausforderer aus einem Katalog. Zur Wahl standen am Ende vier ausgearbeitete Skizzen: Agenturmeldung, Gefahrenbulletin, Abfahrtstafel und der Kategoriestandard. Der Eigentümer hat das **Gefahrenbulletin** gewählt. Die Mockups wurden danach von einer unabhängigen Instanz abgenommen: acht Befunde, zwei Korrekturrunden, Freigabe.

Englisch ist die Leitsprache der Oberfläche, Deutsch folgt als Übersetzung. Der Produktname ist Ardea, lateinisch für Reiher.

**Was die Mockups noch nicht zeigen.** Sie entstanden vor der Auswertung des Regelwerks am Spike. Zwei Dinge sind seither entschieden und müssen vor Phase 3 nachgezogen werden: Die Liste führt Massnahmen statt einzelner Schwachstellen (Kapitel 7.4), und die Fassung ohne Sprachmodell mit Antworten aus Vorlagen ist die Vorgabe einer frischen Installation (Kapitel 7.5). Die Zahlen im Bulletin-Mockup («6 concern you», 167 auf Stufe 1) sind Beispielwerte und nicht aus dem Regelwerk berechnet.

## 11.2 Die Idee in drei Sätzen

Risiko wird gemeldet wie im Lawinen- oder Naturgefahrenbulletin: eine nummerierte Gefahrenstufe, das Gebiet, für das sie gilt, und das empfohlene Verhalten. Die Stufe ist immer Ziffer und Wort; die Farbe wiederholt nur, was beide sagen. Die Welt liefert Schrift, Farbskala, Dichte und eine Signatur, die Gefahrenleiter; Navigation und Bedienelemente bleiben Standard.

| Vorgabe aus den Anforderungen | Umsetzung |
|---|---|
| In fünf Sekunden: was, betroffen, was tun | Bulletin und Meldung beginnen mit genau diesen drei Antworten; am Telefon liegen sie im ersten Bildschirm über der Aktionsleiste (gemessen bei 390 × 844). |
| Kritisches im Vordergrund | Die höchste Stufe des Tages steht als grosses Stufenzeichen im Bulletin; die Leiter zählt je Stufe. |
| Nie nur Farbe | Ziffer 1 bis 5 plus Name. Die Skala wird von 1 nach 5 schrittweise dunkler und bleibt in Graustufen geordnet. |
| Hell, ruhig, dezente Akzente | Weisser Grund, anthrazitfarbene Schrift, Blau nur für Links und Fokus. Die Skalenfarben bedeuten ausschliesslich Stufe. |
| Keine überladenen Dashboards | Linien statt Karten, keine Kennzahlenkacheln, keine Diagramme ohne Aussage. Die einzige Grafik ist der Versionsbereich in der Meldung. |
| Dezente, funktionale Bewegung | Nur Zustandswechsel, 150 ms; nichts bewegt sich beim Laden. |

## 11.3 Designsystem in Kürze

Massgeblich ist `DESIGN.md`. Die wichtigsten Festlegungen:

| Bereich | Festlegung |
|---|---|
| Farben | Papier `#FFFFFF`, Tinte `#1F2326` mit zwei helleren Tönen für Nebentext, Linie `#D7DADD`, Blau `#0B57B0`. Skala: 1 `#D9F2A0`, 2 `#FFD500`, 3 `#FF9500`, 4 `#D4111E`, 5 `#7A0A12`. |
| Kontraste | Text auf Papier 15,8 : 1, Nebentext 8,2 : 1, Links 7,0 : 1; Ziffern auf der Skala zwischen 5,4 : 1 und 12,9 : 1. |
| Schrift | Source Sans 3 in sechs festen Grössen (13, 15, 17, 20, 24, 30 px), Gewichte 400, 600, 700, 900. Source Code Pro nur für Kennungen, Versionen und Vektoren. Beide liegen der Anwendung bei. |
| Form | Gerade Ecken, höchstens 2 px Radius an Bedienelementen, keine Schlagschatten. Hierarchie durch Linien: 3 px unter Seitentiteln, 2 px unter Tabellenköpfen, 1 px zwischen Zeilen. |
| Stufenzeichen | Quadrat mit Ziffer in vier Grössen. Ein Stufenwechsel ist zwei Zeichen mit Pfeil. |
| Gefahrenleiter | Fünf Stufen mit Zeichen, Name und Anzahl. Im Bulletin Zähler und Einstieg, in der Liste Filter. Am Telefon eine fünfteilige Leiste. |
| Arbeitszustände | Form und Wort (Kreis, Uhr, Haken, Fahne, Strich), nie Skalenfarben. |
| Signale | Umrandete Etiketten: «Exploited in the wild» gefüllt, «Fix available» umrandet. |
| Layout | Kopfband 60 px, Leiterspalte 300 px, Inhalt bis 1440 px. Umbruch in ganzen Spalten bei 1180 und 760 px. |
| Ziele | 40 px am Desktop, 44 px bei Touch-Breiten; Fokusring 2 px. |

## 11.4 Aufbau der Anwendung

| Bereich | Zweck | Wichtigste Zustände |
|---|---|---|
| **Bulletin** | Lage des Tages: höchste Stufe, führende Massnahme, Stufe je Projekt, Änderungen, jüngste Warnungen ohne Kennung | ruhig (nichts ab Stufe 2), Erstbefüllung läuft, Quelle veraltet |
| **Actions** (in den Mockups noch «Alerts») | Liste der Massnahmen mit Leiter als Filter, Suche, Projekt, Status; jede Zeile nennt Komponente, Ziel und Anzahl der Befunde und lässt sich aufklappen | leer nach Filter, lädt; die Ansicht «Not relevant» zeigt Befunde der Stufe 1, die zu keiner Massnahme gehören |
| **Action** | Eine Massnahme: Ziel, Befunde nach Stufe, Status, Zuweisung, Frist, Verlauf | ein Befund, viele Befunde, keine Korrektur verfügbar |
| **Alert** | Eine Schwachstelle: drei Antworten, Einschätzung, Faktoren je Komponente, Details, Quellen | ohne KI-Einschätzung (Vorlagen), Vorschlag des Modells zu bestätigen, zurückgezogen |
| **Entries** | Suche über alle konsolidierten Einträge, unabhängig vom eigenen Profil | kein Treffer |
| **Projects** | Projekte und ihr Technologieprofil; Hinweise auf Lücken im Profil | ohne Komponenten, Version fehlt |
| **Sources** | Stand der Quellen; für den Instanz-Administrator auch Steuerung | Quelle fehlerhaft, deaktiviert |
| **Notification rules** | Regeln, Webhook-Ziele, Zustellprotokoll | Zustellung fehlgeschlagen |
| **Team** | Mitglieder, Rollen, Einladungen | Einladung offen |
| **Settings** | Organisation, Fristen, KI-Datenrichtlinie, Audit-Protokoll; persönlich: Sprache, MFA, aktive Sitzungen, Geräte für Push | |
| **Administration** | Nur für den Instanz-Administrator: Registrierung, SMTP und KI-Anbieter mit Verbindungstest, Grenzen, aufgegebene Jobs mit Wiederholen, Kennzahlen des Betriebs, installierte Version | Wert durch Umgebungsvariable gesperrt, Verbindungstest fehlgeschlagen |
| **Setup** | Geführte Ersteinrichtung (11.6) | abgebrochen und fortsetzbar |

Am Desktop liegt die Navigation im Kopfband, am Telefon in einer Leiste am unteren Rand mit vier Zielen. In Massnahme und Meldung ersetzt eine Aktionsleiste die Navigationsleiste: Hauptaktion, Status, Zuweisen.

## 11.5 Kernabläufe

**Ankunft aus einer Benachrichtigung (Entwickler, Telefon).** Push öffnet die Massnahme; bei einem einzigen Befund sieht sie aus wie die Meldung im Mockup. Ohne Scrollen sichtbar: Stufe, Titel, was passiert ist, ob das Projekt betroffen ist, was zu tun ist. Ein Tipp auf «Take into review» setzt den Status, weist zu und bestätigt an Ort und Stelle mit «Undo». Kein Dialog, kein Seitenwechsel.

**Sichtung (verantwortliche Person, Desktop).** Bulletin zeigt die höchste Stufe und was sich geändert hat. Die Leiter führt in die gefilterte Liste; von dort in die Massnahme, Zuweisung oder Abschluss mit Ergebnis, zurück in die Liste an dieselbe Stelle.

**Abschluss.** «Remediated», «Risk accepted» und «Not affected» verlangen eine kurze Begründung. Der Status wird an der Massnahme gesetzt und gilt für ihre Befunde; einzelne Befunde lassen sich abweichend setzen. Wer im Profil die Version anhebt, schliesst damit die Befunde, die die neue Version nicht mehr betreffen.

**Korrektur.** «Correct this assessment» erfasst, was falsch war: Urteil, Stufe oder Massnahme. Die Meldung wird neu bewertet; die Korrektur bleibt im Verlauf sichtbar.

## 11.6 Setup-Wizard

Noch nicht als Mockup gebaut; der Aufbau steht fest.

| Schritt | Inhalt | Regel |
|---|---|---|
| 1 Organisation | Name, Sprache, Zeitzone | Vorgaben aus dem Browser |
| 2 Erstes Projekt | Name, getragener Geschäftsprozess, Kritikalität, Erreichbarkeit | je drei Wahlmöglichkeiten mit einem Satz Erklärung |
| 3 Technologien | Suche im Katalog, gruppiert nach Betriebssystem, Laufzeit und Framework, CMS und Shop, Datenbank und Messaging, Cloud und Hosting, Container und Virtualisierung, Netzwerk und Sicherheit, weitere Software und Abhängigkeiten, externe Dienste | Version ist erwünscht, aber nicht Pflicht; «Version unbekannt» ist eine gültige Angabe. Bei Paketen fragt der Wizard, ob sie aus einer Distribution stammen. Externe Dienste haben keine Version. Freie Einträge sind möglich und als ungeprüft gekennzeichnet. Je Technologie lässt sich eine Schutzmassnahme mit Begründung angeben. |
| 4 Abdeckung | Zeigt, was sich prüfen lässt und was nicht: fehlende Versionen, Plattformen ohne Plugins | Hinweise, keine Sperren |
| 5 Benachrichtigung | Die drei Vorgaberegeln, Push auf diesem Gerät, Team einladen | alles überspringbar |
| 6 Erstes Bulletin | Die Ersteinschätzung läuft im Hintergrund; die Seite zeigt den Fortschritt und dann das Ergebnis | eine Sammelnachricht statt Einzelalarme |

Der Wizard speichert nach jedem Schritt und lässt sich jederzeit fortsetzen. Wer ihn abbricht, landet in einer funktionierenden, leeren Organisation mit einem Hinweis im Bulletin. Weitere Projekte entstehen später über denselben Ablauf ab Schritt 2. Einmal eingegebene Angaben werden nie erneut abgefragt.

## 11.7 Technischer Aufbau

```
web/
  projects/
    app/                     die Anwendung
      src/app/
        core/                Sitzung, API-Basis, SSE, Fehlerbehandlung, Übersetzung
        features/
          bulletin/  actions/  entries/  projects/  sources/  rules/  team/  settings/  admin/  setup/  auth/
        api/generated/       aus dem OpenAPI-Dokument erzeugt, nicht von Hand geändert
    ui/                      Komponentenbibliothek: Tokens, Stufenzeichen, Leiter, Tabelle, Formulare, Hinweise
```

- Jeder Bereich wird verzögert geladen und besitzt seine Datendienste: `httpResource` für Lesen, `HttpClient` für Schreiben, danach gezieltes Neuladen. Kein globaler Store.
- Ein Dienst hält die Verbindung für Server-Sent Events und löst das Neuladen betroffener Daten aus; so bleiben Zähler und Listen aktuell, ohne zu pollen.
- Die Tokens aus `docs/design/mockups/csh.css` werden zur Token-Datei der Bibliothek `ui`. Komponenten verwenden ausschliesslich Tokens.
- Jede Komponente der Bibliothek hat eine Seite mit allen Zuständen; diese Seiten sind zugleich die Grundlage der visuellen Regressionstests.
- Texte liegen in Übersetzungsdateien; Englisch ist eingebaut, weitere Sprachen werden nachgeladen. Pluralformen über ICU von Beginn an. Server-Texte (Nachrichten, E-Mails) werden auf dem Server übersetzt.
- Datum, Zahl und Zeitabstand über `Intl` in der Sprache des Benutzers; Zeiten in der Zeitzone der Organisation.

## 11.8 Zugänglichkeit

Ziel ist WCAG 2.2 AA. Die europäische Norm EN 301 549 verweist seit September 2026 auf diese Fassung.

| Kriterium aus 2.2 | Vorkehrung |
|---|---|
| Fokus nicht verdeckt | Feste Leisten am Telefon; scrollbare Bereiche erhalten einen Abstand, damit das fokussierte Element darüber bleibt. |
| Zielgrösse | Mindestens 24 px überall, 40 px für Bedienelemente, 44 px bei Touch-Breiten. |
| Ziehbewegungen | Keine Funktion verlangt Ziehen. |
| Zugängliche Anmeldung | Einfügen aus Passwortmanagern erlaubt, keine Rätsel; Passkeys in der Ausbaustufe. |
| Redundante Eingabe | Der Wizard fragt nichts zweimal. |
| Einheitliche Hilfe | Hilfe an derselben Stelle auf jeder Seite. |

Dazu die Grundlagen: semantisches HTML mit echten Tabellen, Überschriften in Ordnung, Live-Regionen für Zählzeilen und Bestätigungen, sichtbarer Fokusring, `lang` am Dokument und an fremdsprachigen Quelltexten.

Prüfung in drei Lagen: Lint-Regeln für Vorlagen, Playwright mit axe in den Ende-zu-Ende-Tests (Regelsatz für 2.2 AA einschliesslich Zielgrösse eingeschaltet), und vor jeder Hauptversion ein manueller Durchgang der Kernabläufe mit Tastatur und Screenreader. Automatik allein weist Konformität nicht nach.

## 11.9 Telefon: erst PWA, dann App

**MVP: installierbare Web-App.** Web Push mit eigenem Schlüsselpaar der Instanz braucht keine Infrastruktur des Projekts und keinen Entwickler-Account. Es funktioniert in Desktop-Browsern, auf Android und auf iOS ab 16.4, dort nur für Web-Apps auf dem Home-Bildschirm. Die Oberfläche führt deshalb auf iOS durch «Zum Home-Bildschirm» und fragt erst danach nach der Berechtigung, ausgelöst durch einen Tipp. Die Nachricht wird in einer Form gesendet, die sowohl der Service Worker als auch das deklarative Web Push von Safari versteht.

**Ausbaustufe: Capacitor-App.** Vier Dinge sind vorher zu entscheiden, weil sie Backend und Betrieb betreffen:

| Thema | Sachverhalt | Folge |
|---|---|---|
| Push | In der App gibt es kein Web Push. Native Push verlangt die Zugangsdaten des App-Herausgebers; eine selbst gehostete Instanz kann sie nicht haben. | Das Projekt betreibt ein kleines Relay, das wie ein Web-Push-Endpunkt aussieht und den verschlüsselten Inhalt ungelesen weiterreicht. Das ist eine dauerhafte Betriebspflicht. Android zusätzlich über UnifiedPush ohne Relay. |
| Anmeldung | Die App spricht mit einer fremden Herkunft; Cookies sind dort unzuverlässig. | Zugriffstoken mit rotierendem Refresh-Token im sicheren Speicher des Geräts; das Backend unterstützt beide Verfahren. |
| Serveradresse | Jede Instanz hat eine eigene Adresse. | Erster Bildschirm fragt sie ab und prüft sie gegen einen offenen Endpunkt mit Produktname und API-Version. HTTPS mit gültigem Zertifikat ist Pflicht. |
| Versionen | App und Server werden unabhängig aktualisiert. | Die API ist versioniert; die App kennt den Zustand «Server zu alt» und «Server zu neu». |

Der Server sendet für die Web-App wie für die native App nur Web Push. Die native App ändert am Server nichts ausser der zweiten Anmeldeart.

## 11.10 Offen

- Überarbeitete Mockups der Liste und des Bulletins mit Massnahmen statt einzelner Schwachstellen, die Detailansicht einer Massnahme und die Fassung ohne Sprachmodell; fällig vor Phase 3.
- Mockups für Setup-Wizard, Projektprofil, Benachrichtigungsregeln und Quellenstatus; sie folgen nach Freigabe der Richtung.
- Eine eigene Seite für das ruhige Bulletin. Heute existiert der Zustand nur als Muster im Designsystem.
- Benennung der Stufe 1 und ob sie in der Leiter standardmässig sichtbar ist.
- Deutsche Texte in allen Komponenten: Die Leiter am Telefon ist dafür ausgelegt, geprüft ist es noch nicht.
- Ein dunkles Design ist ausdrücklich nicht vorgesehen; die Tokens lassen es später zu.
