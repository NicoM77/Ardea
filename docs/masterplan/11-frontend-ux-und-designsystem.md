# 11 · Frontend, UX und Designsystem

## 11.1 Stand

Die visuelle Richtung ist gewählt und als Mockups ausgearbeitet.

| Ergebnis | Ort |
|---|---|
| Produktgrundlage (Nutzer, Zweck, Prinzipien, bindende Vorgaben) | [`PRODUCT.md`](../../PRODUCT.md) |
| Designsystem als verbindliche Referenz: Tokens, Komponenten, benannte Regeln | [`DESIGN.md`](../../DESIGN.md), maschinenlesbar in `.impeccable/design.json` |
| Mockups des Kernflusses: Übersicht, Massnahmen, Massnahme, Schwachstelle, Designsystem-Seite | [`docs/design/mockups/`](../design/mockups/) (statisches HTML, CSS und JavaScript; `overview.html` im Browser öffnen) |

**Wie die Richtung entstand.** Eine erste Richtung, das «Gefahrenbulletin» mit nummerierten Stufen und einer Gefahrenleiter, hat der Eigentümer am 18.11.2026 verworfen: zu wenig schlicht und nicht intuitiv genug. Die Mockups wurden gelöscht und neu gebaut, nach seiner Vorgabe: einfach, intuitiv, gestaltet wie eine professionelle Arbeitsanwendung nach den Gestaltungsrichtlinien von Apple, ohne eigene Bildsprache. Zwei weitere Festlegungen vom selben Tag: Die Priorität erscheint als Wort mit eigenem Symbol, ohne Ziffer; die Liste zeigt Massnahmen, die Schwachstelle ist die Detailansicht dahinter.

Englisch ist die Leitsprache der Oberfläche, Deutsch folgt als Übersetzung. Der Produktname ist Ardea, lateinisch für Reiher.

**Was die Mockups zeigen.** Die Fassung ohne Sprachmodell, die Vorgabe einer frischen Installation (Kapitel 7.5): Die drei Antworten entstehen aus den Quelldaten, die Begründung der Priorität ist die Punkterechnung des Regelwerks (Kapitel 7.6). Die Zahlen in den Mockups ergeben sich aus den sechs Beispiel-Massnahmen und sind nicht aus echten Daten berechnet.

## 11.2 Die Idee in drei Sätzen

Ardea sieht aus und verhält sich wie eine gut gemachte Systemanwendung: vertraute Bedienelemente, je Bildschirm ein klarer nächster Schritt, und nichts auf der Seite, das nicht hilft zu beantworten, was passiert ist, ob man betroffen ist und was zu tun ist. Farbe bedeutet Priorität und erscheint nie ohne Symbol und Wort. Alles andere ist ruhig: grauer Grund, weisse Gruppen, feine Linien, ein Blau für Aktionen und Links.

| Vorgabe aus den Anforderungen | Umsetzung |
|---|---|
| In fünf Sekunden: was, betroffen, was tun | Die Massnahme beginnt mit genau diesen drei Antworten; am Telefon liegen sie im ersten Bildschirm über der Aktionsleiste (geprüft bei 390 × 844). |
| Kritisches im Vordergrund | Die Übersicht nennt in einem Satz, wie viele kritische Massnahmen offen sind, zählt je Priorität und listet unter «Next up» die dringendsten zuerst. |
| Nie nur Farbe | Jede Priorität hat eine eigene Form (Achteck, Dreieck, Raute, Kreis mit Pfeil, Kreis mit Strich) und ihr Wort. Arbeitszustände sind neutral und ebenfalls an der Form zu erkennen. |
| Hell, ruhig, dezente Akzente | Grauer Grund, weisse Gruppen, Systemschrift, ein Blau für die Hauptaktion, Links und Fokus. |
| Keine überladenen Dashboards | Keine Kennzahlenkacheln, keine Diagramme. Die Übersicht ist ein Satz, eine Zählleiste und Listen. |
| Dezente, funktionale Bewegung | Nur Zustandswechsel, 150 ms; nichts bewegt sich beim Laden. |

## 11.3 Designsystem in Kürze

Massgeblich ist `DESIGN.md`; die Tokens stehen in `docs/design/mockups/app.css`. Die wichtigsten Festlegungen:

| Bereich | Festlegung |
|---|---|
| Farben | Grund `#F5F5F7`, Gruppen `#FFFFFF`, Seitenleiste `#EBEBEF`, Text `#1D1D1F`, Nebentext `#5E5E64`, Akzent `#0062C4`. Priorität: Critical `#C8102E`, High `#B84A00`, Medium `#8A6100`, Low und Not relevant `#5E5E64`. Behoben `#1E7D34`. |
| Kontraste | Text 16,8 : 1, Nebentext 6,4 : 1, Akzent 5,9 : 1, Prioritätsfarben zwischen 5,2 : 1 und 6,4 : 1, Feldrahmen 3,7 : 1, jeweils auf Weiss. |
| Schrift | Die Systemschrift der Plattform (SF Pro, Segoe UI, Roboto); nichts wird geladen. Titel 28 px, Überschrift 17 px, Lesetext 16 px, Oberfläche 15 px, Nebentext 13 px. Monospace nur für wörtlich zu kopierende Zeichenketten wie einen CVSS-Vektor. |
| Form | Gruppen mit 12 px Radius und feinem Rand, Bedienelemente mit 8 px. Keine Schatten ausser dem Schieber des Segmentschalters. |
| Priorität | Symbol und Wort, das Wort immer sichtbar. Ein Wechsel ist zwei Prioritäten mit Pfeil. |
| Arbeitszustände | Form und Wort (Kreis, halber Kreis, Haken, Fahne, durchgestrichener Kreis), neutral; nur der Haken für «Remediated» ist grün. |
| Signale | Kleine Etiketten: «Exploited in the wild» dunkel, «Fix available» hell. |
| Layout | Seitenleiste 248 px am Desktop, 220 px am Tablet, gewählter Eintrag hellblau hinterlegt; Inhalt bis 1160 px. Am Telefon Titelzeile, grosser Titel und Tab-Leiste. Tabellen werden zu Datensätzen, sobald ihre Gruppe schmaler als 640 px ist. |
| Ziele | 36 px am Desktop, 44 px bei Telefonbreite; Fokusring 2 px im Akzent. |

## 11.4 Aufbau der Anwendung

| Bereich | Zweck | Wichtigste Zustände |
|---|---|---|
| **Overview** | Lage des Tages: offene Massnahmen je Priorität, die dringendsten Massnahmen, Änderungen, Priorität je Projekt, jüngste Warnungen ohne Kennung, Stand der Quellen | ruhig (keine offene Massnahme), Erstbefüllung läuft, Quelle veraltet |
| **Actions** | Liste der Massnahmen mit Umschalter offen und abgeschlossen, Suche, Filter nach Projekt und Priorität; jede Zeile nennt Ziel, Projekt, installierte Version, Kennung, Fälligkeit und Status | leer nach Filter, keine abgeschlossenen, lädt; ein aufklappbarer Abschnitt «Not relevant» zeigt Befunde der Stufe 1, die zu keiner Massnahme gehören |
| **Action** | Eine Massnahme: die drei Antworten, Schwachstellen nach Priorität, Status, Zuweisung, Frist, Notiz, Verlauf | ein Befund, viele Befunde, keine Korrektur verfügbar |
| **Vulnerability** | Eine Schwachstelle aus Sicht der Organisation: betroffene Projekte, Begründung der Priorität als Punkterechnung, Beschreibung, Details, Quellen, Verlauf | ohne KI-Einschätzung (Vorlagen), Vorschlag des Modells zu bestätigen, zurückgezogen |
| **Vulnerabilities** | Suche über alle konsolidierten Einträge, unabhängig vom eigenen Profil | kein Treffer |
| **Projects** | Projekte und ihr Technologieprofil; verbundene Repositories mit Commit und Zeitpunkt des letzten Einlesens; Pakete je Ökosystem; offene Vorschläge; Hinweise auf Lücken im Profil | ohne Komponenten, Version fehlt, Repository wird eingelesen, Repository nicht lesbar, Stand zur Bestätigung zurückgehalten, Token läuft ab |
| **Sources** | Stand der Quellen; für den Instanz-Administrator auch Steuerung | Quelle fehlerhaft, deaktiviert |
| **Notification rules** | Regeln, Webhook-Ziele, Zustellprotokoll | Zustellung fehlgeschlagen |
| **Team** | Mitglieder, Rollen, Einladungen | Einladung offen |
| **Settings** | Organisation, Fristen, KI-Datenrichtlinie, Audit-Protokoll; persönlich: Sprache, MFA, aktive Sitzungen, Geräte für Push | |
| **Administration** | Nur für den Instanz-Administrator: Registrierung, SMTP und KI-Anbieter mit Verbindungstest, Grenzen, aufgegebene Jobs mit Wiederholen, Kennzahlen des Betriebs, installierte Version | Wert durch Umgebungsvariable gesperrt, Verbindungstest fehlgeschlagen |
| **Setup** | Geführte Ersteinrichtung (11.6) | abgebrochen und fortsetzbar |

Am Desktop und Tablet liegt die Navigation in einer Seitenleiste mit Organisationswechsel, am Telefon in einer Tab-Leiste am unteren Rand mit vier Zielen (Overview, Actions, Projects, More). In der Massnahme ersetzt eine Aktionsleiste die Tab-Leiste: Hauptaktion und Status.

## 11.5 Kernabläufe

**Ankunft aus einer Benachrichtigung (Entwickler, Telefon).** Push öffnet die Massnahme. Ohne Scrollen sichtbar: Priorität, Titel, Projekt und Frist, was passiert ist, ob das Projekt betroffen ist, was zu tun ist. Ein Tipp auf «Take into review» setzt den Status, weist zu und bestätigt an Ort und Stelle mit «Undo». Kein Dialog, kein Seitenwechsel.

**Sichtung (verantwortliche Person, Desktop).** Die Übersicht zeigt, wie viele Massnahmen je Priorität offen sind, welche als Nächstes dran sind und was sich geändert hat. Die Zählleiste führt in die nach Priorität gefilterte Liste; von dort in die Massnahme, Zuweisung oder Abschluss mit Ergebnis, zurück in die Liste an dieselbe Stelle.

**Abschluss.** «Remediated», «Risk accepted» und «Not affected» verlangen eine kurze Begründung. Der Status wird an der Massnahme gesetzt und gilt für ihre Befunde; einzelne Befunde lassen sich abweichend setzen. Wer im Profil die Version anhebt, schliesst damit die Befunde, die die neue Version nicht mehr betreffen.

**Korrektur.** «Correct this assessment» erfasst, was falsch war: Urteil, Stufe oder Massnahme. Die Meldung wird neu bewertet; die Korrektur bleibt im Verlauf sichtbar.

## 11.6 Setup-Wizard

Noch nicht als Mockup gebaut; der Aufbau steht fest.

| Schritt | Inhalt | Regel |
|---|---|---|
| 1 Organisation | Name, Sprache, Zeitzone | Vorgaben aus dem Browser |
| 2 Erstes Projekt | Name, getragener Geschäftsprozess, Kritikalität, Erreichbarkeit | je drei Wahlmöglichkeiten mit einem Satz Erklärung |
| 3 Repository | Optional, empfohlen. Zugang wählen oder anlegen (öffentlich ohne Token, oder feingranulares Token mit Anleitung und vorbelegten Rechten), ein oder mehrere Repositories aus der Liste des Zugangs wählen, Zweig und bei Monorepos Unterordner. Das Einlesen startet sofort im Hintergrund (Kapitel 16.2). | Überspringbar; wer kein Repository hat oder es nicht verbinden will, erfasst alles im nächsten Schritt von Hand. Fehlt der Dependency Graph, sagt der Wizard, wie man ihn einschaltet, und fährt mit dem fort, was die eigenen Leser finden. |
| 4 Betrieb | Fragen, die das Repository nicht beantwortet und die der Wizard bis zum Ende des Einlesens stellt: Wo läuft das Projekt (eigener Server, Container-Plattform, verwalteter Dienst), welches Betriebssystem hat der Host, was steht davor | je eine Frage mit wenigen Wahlmöglichkeiten; «weiss nicht» ist gültig |
| 5 Technologien | Drei Gruppen (Kapitel 16.7): **aus dem Repository** als Zusammenfassung je Ökosystem; **Vorschläge** mit Beleg zum Bestätigen, Ändern oder Ablehnen; **ergänzen**: gezielte Fragen aus dem Gefundenen, darunter die Suche im Katalog, gruppiert nach Betriebssystem, Laufzeit und Framework, CMS und Shop, Datenbank und Messaging, Cloud und Hosting, Container und Virtualisierung, Netzwerk und Sicherheit, weitere Software und Abhängigkeiten, externe Dienste | Version ist erwünscht, aber nicht Pflicht; «Version unbekannt» ist eine gültige Angabe. Bei Paketen fragt der Wizard, ob sie aus einer Distribution stammen. Externe Dienste haben keine Version. Freie Einträge sind möglich und als ungeprüft gekennzeichnet. Je Technologie lässt sich eine Schutzmassnahme mit Begründung angeben. Ohne Repository ist nur die Gruppe «ergänzen» sichtbar. |
| 6 Abdeckung | Zeigt, was sich prüfen lässt und was nicht: fehlende Versionen, Plattformen ohne Plugins, Pakete ohne Lockdatei, noch nicht prüfbare Ökosysteme | Hinweise, keine Sperren |
| 7 Benachrichtigung | Die drei Vorgaberegeln, Push auf diesem Gerät, Team einladen | alles überspringbar |
| 8 Erste Übersicht | Die Ersteinschätzung läuft im Hintergrund; die Seite zeigt den Fortschritt und dann das Ergebnis | eine Sammelnachricht statt Einzelalarme |

Der Wizard speichert nach jedem Schritt und lässt sich jederzeit fortsetzen. Wer ihn abbricht, landet in einer funktionierenden, leeren Organisation mit einem Hinweis in der Übersicht; ein bereits verbundenes Repository wird trotzdem eingelesen. Weitere Projekte entstehen später über denselben Ablauf ab Schritt 2, ein weiteres Repository für ein bestehendes Projekt über die Projektseite mit den Schritten 3 bis 6. Einmal eingegebene Angaben werden nie erneut abgefragt: Ein Zugang wird einmal angelegt und danach nur ausgewählt, ein abgelehnter Vorschlag kommt nicht wieder.

Ist das Einlesen bei Schritt 5 noch nicht fertig, zeigt die Gruppe «aus dem Repository» den Fortschritt, und die beiden anderen Gruppen lassen sich schon bearbeiten. Vorschläge erscheinen, sobald sie da sind; nichts springt unter dem Zeiger weg.

## 11.7 Technischer Aufbau

```
web/
  projects/
    app/                     die Anwendung
      src/app/
        core/                Sitzung, API-Basis, SSE, Fehlerbehandlung, Übersetzung
        features/
          overview/  actions/  vulnerabilities/  projects/  sources/  rules/  team/  settings/  admin/  setup/  auth/
        api/generated/       aus dem OpenAPI-Dokument erzeugt, nicht von Hand geändert
    ui/                      Komponentenbibliothek: Tokens, Prioritätszeichen, Zustände, Listen, Tabelle, Formulare, Hinweise
```

- Jeder Bereich wird verzögert geladen und besitzt seine Datendienste: `httpResource` für Lesen, `HttpClient` für Schreiben, danach gezieltes Neuladen. Kein globaler Store.
- Ein Dienst hält die Verbindung für Server-Sent Events und löst das Neuladen betroffener Daten aus; so bleiben Zähler und Listen aktuell, ohne zu pollen.
- Die Tokens aus `docs/design/mockups/app.css` werden zur Token-Datei der Bibliothek `ui`. Komponenten verwenden ausschliesslich Tokens.
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

- Mockups für Setup-Wizard mit Repository und Vorschlägen, Projektprofil mit Verknüpfungen und Paketen je Ökosystem, Benachrichtigungsregeln und Quellenstatus; sie folgen nach Freigabe der Richtung.
- Darstellung von über tausend Paketen je Projekt: Zusammenfassung je Ökosystem, Filter nach direkt, transitiv und «nur Entwicklung», Suche; welche Spalten die Liste braucht, klärt das Mockup.
- Eine eigene Seite für die ruhige Übersicht. Heute existiert der Zustand nur als Muster im Designsystem.
- Die Massnahme mit mehreren Befunden und die Fassung mit KI-Einschätzung als eigene Mockups.
- Deutsche Texte in allen Komponenten; die Layouts sind für längere Wörter ausgelegt, geprüft ist es noch nicht.
- Ein dunkles Design ist ausdrücklich nicht vorgesehen; die Tokens lassen es später zu.
