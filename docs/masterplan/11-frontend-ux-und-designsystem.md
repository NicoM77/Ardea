# 11 · Frontend, UX und Designsystem

## 11.1 Stand

Die visuelle Richtung ist gewählt und als Mockups ausgearbeitet.

| Ergebnis | Ort |
|---|---|
| Produktgrundlage (Nutzer, Zweck, Prinzipien, bindende Vorgaben) | [`PRODUCT.md`](../../PRODUCT.md) |
| Designsystem als verbindliche Referenz: Tokens, Komponenten, benannte Regeln | [`DESIGN.md`](../../DESIGN.md), maschinenlesbar in `.impeccable/design.json` |
| Mockups des Kernflusses: Übersicht, Massnahmen, Massnahme, Schwachstelle, Designsystem-Seite | [`docs/design/mockups/`](../design/mockups/) (statisches HTML, CSS und JavaScript; `overview.html` im Browser öffnen) |

**Wie die Richtung entstand.** Eine erste Richtung, das «Gefahrenbulletin» mit nummerierten Stufen und einer Gefahrenleiter, hat der Eigentümer am 18.11.2026 verworfen: zu wenig schlicht und nicht intuitiv genug. Die Mockups wurden gelöscht und neu gebaut, nach seiner Vorgabe: einfach, intuitiv, gestaltet wie eine professionelle Arbeitsanwendung nach den Gestaltungsrichtlinien von Apple, ohne eigene Bildsprache. Zwei weitere Festlegungen vom selben Tag: Die Priorität erscheint als Wort mit eigenem Symbol, ohne Ziffer; die Liste zeigt Massnahmen, die Schwachstelle ist die Detailansicht dahinter.

Auch diese zweite Fassung hat der Eigentümer am selben Tag zurückgewiesen: zu viel Text, zu nüchtern. Seine Vorgabe für die dritte, jetzt gültige Fassung: Diagramme, wenig Text, flüssige Bewegung, intuitive Bedienung; der Aufbau durfte neu gedacht werden. Damit entfallen drei Regeln der zweiten Fassung: keine Diagramme, keine Bewegung beim Laden, nur die Systemschrift.

Englisch ist die Leitsprache der Oberfläche, Deutsch folgt als Übersetzung. Der Produktname ist Ardea, lateinisch für Reiher.

**Was die Mockups zeigen.** Die Fassung ohne Sprachmodell, die Vorgabe einer frischen Installation (Kapitel 7.5): Die drei Antworten entstehen aus den Quelldaten, die Begründung der Priorität ist die Punkterechnung des Regelwerks (Kapitel 7.6). Alle Zahlen in den Mockups werden aus einem Beispieldatensatz berechnet (`data.js`): sechs offene und acht abgeschlossene Massnahmen. Echt sind die CVE-Kennungen der offenen Massnahmen und zwei Zahlen der Woche aus dem Spike (6 432 publizierte Records, 173 Kandidaten); alles Übrige ist erfunden.

## 11.2 Die Idee in drei Sätzen

Jeder Bildschirm beantwortet seine eine Frage zuerst mit einem Bild und erst dann mit einem Satz: Fristen sind Balken auf einer gemeinsamen Zeitachse, die CVE-Records der Woche ein Feld aus Punkten, die Betroffenheit eine Markierung im Versionsbereich, die Priorität ein Balken aus Punkten. Farbe liegt auf den Daten: Priorität erscheint nie ohne Symbol und Wort, ein Petrolton trägt die übrigen Diagramme. Der Rahmen ist ruhig: ein graublauer Grund für Seitenleiste und Kopfzeile, die Arbeit auf einem weissen Blatt, Schaltflächen und Navigation in Grau und Schwarz.

| Vorgabe aus den Anforderungen | Umsetzung |
|---|---|
| In fünf Sekunden: was, betroffen, was tun | Die Massnahme beginnt mit genau diesen drei Antworten; am Telefon liegen sie im ersten Bildschirm über der Aktionsleiste (geprüft bei 390 × 844). |
| Kritisches im Vordergrund | Die Übersicht nennt in einem Satz, wie viele kritische Massnahmen offen sind, zeigt die nächste Frist als laufende Restzeit mit dem Weg zur Massnahme und ordnet alle offenen Massnahmen auf einer Zeitachse nach ihrer Frist. |
| Nie nur Farbe | Jede Priorität hat eine eigene Form (Achteck, Dreieck, Raute, Kreis mit Pfeil, Kreis mit Strich) und ihr Wort. Arbeitszustände sind neutral und ebenfalls an der Form zu erkennen. |
| Hell, ruhig, dezente Akzente | Graublauer Rahmen, weisses Blatt, Karten mit feinem Rand. Farbe nur auf Daten, Priorität und Ergebnis; die Hauptaktion ist fast schwarz. |
| Keine überladenen Dashboards | Jedes Diagramm beantwortet eine Frage und nennt seine Zahl. Keine Kennzahlenkacheln mit Symbolen, keine Sparklines, eine Farbe je Diagramm. |
| Dezente, funktionale Bewegung | Diagramme zeichnen sich einmal beim Erscheinen, danach ist Bewegung Rückmeldung: Druck 140 ms, Wechsel 220 ms. Nur Transform und Deckkraft; Tastaturaktionen bleiben ohne Animation; «Bewegung reduzieren» lässt nur kurze Überblendungen. |

## 11.3 Designsystem in Kürze

Massgeblich ist `DESIGN.md`; die Tokens stehen in `docs/design/mockups/app.css`. Die wichtigsten Festlegungen:

| Bereich | Festlegung |
|---|---|
| Farben | Rahmen `#F1F4F6`, Blatt und Karten `#FFFFFF`, Text `#131416`, Nebentext `#53595D`, Datenfarbe `#1B6C80`. Priorität: Critical `#C8102E`, High `#B84A00`, Medium `#8A6100`, Low und Not relevant `#53595D`. Behoben `#1E7B4F`. |
| Kontraste | Text 18,4 : 1, Nebentext 7,1 : 1, Datenfarbe 6,0 : 1, Prioritätsfarben zwischen 5,2 : 1 und 7,1 : 1, Feldrahmen 3,7 : 1, jeweils auf Weiss. |
| Schrift | IBM Plex Sans für die Oberfläche, IBM Plex Mono für Kennungen, Versionen und den CVSS-Vektor; die Systemschrift ist der Rückfall. In den Mockups kommt die Schrift von Google Fonts, das Produkt liefert sie selbst aus. Leitzahl 36 px, Zahl 28 px, Titel 22 px, Abschnitt 16 px, Oberfläche 14 px, Nebentext 13 und 12 px. |
| Form | Blatt mit 16 px Radius, Karten mit 8 px und feinem Rand, Bedienelemente mit 6 px, Etiketten und Filter rund. Schatten nur an dem, was schwebt: Tooltip, Bestätigung, Suchdialog. |
| Diagramme | Fristen auf einer Zeitachse, Ring nach Priorität, Punktefeld der Wochen-Records, Verlauf der offenen Massnahmen, Punktebalken der Priorität, Versionsbereich mit installierter Version, CVSS als Bogen. Beschreibung und Regeln in `DESIGN.md`. |
| Priorität | Symbol und Wort, das Wort immer sichtbar. Ein Wechsel ist zwei Prioritäten mit Pfeil. |
| Arbeitszustände | Form und Wort (Kreis, halber Kreis, Haken, Fahne, durchgestrichener Kreis), neutral; nur der Haken für «Remediated» ist grün. |
| Signale | Kleine Etiketten: «Exploited» hellrot mit rotem Punkt, «Fix available» hellgrün. |
| Layout | Seitenleiste 228 px und Kopfzeile 48 px auf dem Rahmen, gewählter Eintrag grau hinterlegt; Raster mit 12 Spalten und 24 px Abstand, Inhalt bis 1240 px. Unter 900 px wird die Seitenleiste zum Menü hinter einer Schaltfläche. Unter 640 px werden Tabellenzeilen zu zweizeiligen Datensätzen. |
| Ladeanzeige | Der Reiher zeichnet sich vom Schnabel her und löst sich wieder (Variante «Draw», Wahl des Eigentümers vom 18.11.2026), in 96, 20 und 16 px; am Ende des Ladens zeichnet sich die Marke einmal fertig. Erscheint erst nach 300 ms. Prototyp in `docs/design/loader/`, Regeln in `DESIGN.md`. |
| Ziele | 32 px am Desktop, 40 bis 44 px bei Telefonbreite; Fokusring 2 px in der Datenfarbe. |

## 11.4 Aufbau der Anwendung

| Bereich | Zweck | Wichtigste Zustände |
|---|---|---|
| **Overview** | Lage des Tages: Fristen aller offenen Massnahmen auf einer Zeitachse, die nächste Frist als Restzeit, offene Massnahmen je Priorität, die CVE-Records der Woche vom Eingang bis zur Massnahme, Verlauf über 30 Tage, Massnahmen je Projekt, Änderungen und jüngste Warnungen ohne Kennung, Stand der Quellen | ruhig (keine offene Massnahme), Erstbefüllung läuft, Quelle veraltet |
| **Actions** | Liste der Massnahmen mit Umschalter offen und abgeschlossen, Suche, Filter nach Projekt, Priorität und fehlender Zuweisung; jede Zeile nennt Ziel, Projekt, installierte Version, Kennung, Restzeit mit verbrauchtem Anteil, Zuständige und Status; daneben die Verteilung auf Zuständige und die Dauer bis zum Abschluss je Priorität | leer nach Filter, keine abgeschlossenen, lädt; eine Zeile unter der Liste nennt, wie viele Befunde der Stufe 1 in der Woche geprüft und verworfen wurden |
| **Action** | Eine Massnahme: die drei Antworten, Schwachstellen nach Priorität, Status, Zuweisung, Frist, Notiz, Verlauf | ein Befund, viele Befunde, keine Korrektur verfügbar |
| **Vulnerability** | Eine Schwachstelle aus Sicht der Organisation: betroffene Projekte, Begründung der Priorität als Punkterechnung, Beschreibung, Details, Quellen mit Betreiber und Trägerschaft, Verlauf | ohne KI-Einschätzung (Vorlagen), Vorschlag des Modells zu bestätigen, zurückgezogen |
| **Vulnerabilities** | Suche über alle konsolidierten Einträge, unabhängig vom eigenen Profil | kein Treffer |
| **Projects** | Projekte und ihr Technologieprofil; verbundene Repositories mit Commit und Zeitpunkt des letzten Einlesens; Pakete je Ökosystem; offene Vorschläge; Hinweise auf Lücken im Profil | ohne Komponenten, Version fehlt, Repository wird eingelesen, Repository nicht lesbar, Stand zur Bestätigung zurückgehalten, Token läuft ab |
| **Sources** | Stand der Quellen mit Betreiber und Trägerschaft (Kapitel 5.6), Filter nach Trägerschaft; für den Instanz-Administrator auch Steuerung und die Prüfliste des Katalogs mit gelernten Einträgen (Kapitel 6.9) | Quelle fehlerhaft, deaktiviert |
| **Notification rules** | Regeln, Webhook-Ziele, Zustellprotokoll | Zustellung fehlgeschlagen |
| **Team** | Mitglieder, Rollen, Einladungen | Einladung offen |
| **Settings** | Organisation, Fristen, KI-Datenrichtlinie, Audit-Protokoll; persönlich: Sprache, MFA, aktive Sitzungen, Geräte für Push | |
| **Administration** | Nur für den Instanz-Administrator: Registrierung, SMTP und KI-Anbieter mit Verbindungstest, Grenzen, aufgegebene Jobs mit Wiederholen, Kennzahlen des Betriebs, installierte Version | Wert durch Umgebungsvariable gesperrt, Verbindungstest fehlgeschlagen |
| **Setup** | Geführte Ersteinrichtung (11.6) | abgebrochen und fortsetzbar |

Am Desktop liegt die Navigation in einer Seitenleiste mit Organisationswechsel, darüber eine Kopfzeile mit der Suche (öffnet mit «/» oder Ctrl K). Unter 900 px wird die Seitenleiste zum Menü hinter einer Schaltfläche in der Kopfzeile. In der Massnahme liegt am Telefon eine Aktionsleiste am unteren Rand: Hauptaktion und Status.

## 11.5 Kernabläufe

**Ankunft aus einer Benachrichtigung (Entwickler, Telefon).** Push öffnet die Massnahme. Ohne Scrollen sichtbar: Priorität, Titel, Projekt und Frist, was passiert ist, ob das Projekt betroffen ist, was zu tun ist. Ein Tipp auf «Take into review» setzt den Status, weist zu und bestätigt an Ort und Stelle mit «Undo». Kein Dialog, kein Seitenwechsel.

**Sichtung (verantwortliche Person, Desktop).** Die Übersicht zeigt, wie viele Massnahmen je Priorität offen sind, welche als Nächstes dran sind und was sich geändert hat. Der Ring nach Priorität führt in die gefilterte Liste; von dort in die Massnahme, Zuweisung oder Abschluss mit Ergebnis, zurück in die Liste an dieselbe Stelle.

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

- Mockups für Setup-Wizard mit Repository und Vorschlägen, Projektprofil mit Verknüpfungen und Paketen je Ökosystem, Benachrichtigungsregeln und Quellenstatus. Die Umsetzung entwirft sie selbst nach `DESIGN.md` und wartet nicht auf eine Freigabe; der Eigentümer korrigiert im Nachhinein (Kapitel 15, E15).
- Darstellung von über tausend Paketen je Projekt: Zusammenfassung je Ökosystem, Filter nach direkt, transitiv und «nur Entwicklung», Suche; welche Spalten die Liste braucht, klärt das Mockup.
- Eine eigene Seite für die ruhige Übersicht. Heute existiert der Zustand nur als Muster im Designsystem.
- Die Massnahme mit mehreren Befunden und die Fassung mit KI-Einschätzung als eigene Mockups.
- Deutsche Texte in allen Komponenten; die Layouts sind für längere Wörter ausgelegt, geprüft ist es noch nicht.
- Ein dunkles Design ist ausdrücklich nicht vorgesehen; die Tokens lassen es später zu.
