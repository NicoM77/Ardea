# 7 · Relevanzprüfung, KI-Analyse und Priorisierung

## 7.1 Grundsatz

Die Bewertung ist ein Trichter aus billigen, deterministischen Schritten. Das Sprachmodell sieht nur einen kleinen Rest und vergibt keine Stufe; die berechnet ein Regelwerk.

Was der Daten-Spike für eine Organisation mit den drei Beispielprojekten in einer Woche ergab (`prototypes/relevance-funnel/`):

| Schritt | Menge |
|---|---:|
| Einträge neu oder geändert | 6 432 |
| Kandidaten nach strukturiertem Abgleich | 173 (2,7 %) |
| davon neu publiziert, der Rest sind Änderungen an älteren Records | 82 |
| Massnahmen: Paare aus Projekt und Technologie | 8 |
| Meldungen ab Stufe 3, je nach Version und Erreichbarkeit | 6 bis 170 |

Die letzte Zeile ist die wichtigste Erkenntnis der Planung. Der Vorfilter allein genügt nicht: Trifft eine Organisation ein Sammelupdate, entstehen über hundert Meldungen in einer Woche. Daraus folgen zwei Festlegungen:

- **Die Arbeitseinheit ist die Massnahme, nicht die CVE** (7.4). «PostgreSQL in Projekt C auf 17.12 aktualisieren» ist eine Aufgabe mit 52 Einträgen dahinter. Benachrichtigt, zugewiesen und abgeschlossen wird je Massnahme.
- **Das Sprachmodell läuft nur ab einer Schwelle** und begrenzt je Massnahme (7.5).

Drei Eigenschaften gelten in jeder Ausbaustufe:

1. **Ohne KI vollständig.** Abgleich, Versionsprüfung und Regelwerk liefern Stufe, Begründung und Massnahme aus strukturierten Daten und Vorlagen.
2. **Nachvollziehbar.** Jede Stufe lässt sich aus gespeicherten Eingaben neu berechnen: Eintrag, Profil, Regelversion und, wo vorhanden, das gespeicherte Urteil des Modells.
3. **Überprüfbar.** Jede Aussage verweist auf eine Quelle des Eintrags oder eine Angabe im Profil. Was unbekannt ist, steht ausdrücklich da.

## 7.2 Kandidatensuche

Ausgelöst durch einen geänderten Eintrag (vorwärts: ein Eintrag gegen alle Profile) oder ein geändertes Profil (rückwärts: ein Projekt gegen die Einträge im Rückblick).

Abgeglichen werden die Schlüssel der betroffenen Produkte (`entry_affected`) mit den Schlüsseln der Komponenten (`component_key`):

| Treffer | Bedingung | Stärke |
|---|---|---|
| CPE | Vendor und Produkt der CPE stimmen überein | stark |
| Paket | Ökosystem und Paketname stimmen überein | stark |
| Produkt | Produktschlüssel stimmt, direkt oder über einen Katalog-Alias | stark |
| Variante | der Katalog führt das Produkt als verwandt, aber eigenständig (Azure SQL und SQL Server, MariaDB und MySQL) | schwach |
| Ökosystem | Eintrag betrifft ein Plugin oder Paket einer Plattform im Profil, das Plugin selbst steht nicht im Profil | schwach |
| Freier Eintrag ohne Vendor | nur der Produktname stimmt | schwach |
| Text | Produktname nur im Freitext | zählt nicht |

Ein schwacher Treffer erzeugt Stufe 1 mit Grund («verwandtes Produkt, nicht als betroffen genannt», «Plattform im Einsatz, Komponente nicht im Profil») und keine Benachrichtigung. Die Zahl der schwachen Treffer je Projekt ist eine Kennzahl für Lücken im Profil.

## 7.3 Versionsprüfung und Kontext

Für jeden starken Treffer:

- Versionsvergleich nach Kapitel 6.6 mit dem Ergebnis *im Bereich*, *ausserhalb* oder *unbekannt*. *Ausserhalb* bei allen Komponenten eines Projekts ergibt Stufe 1, ohne weiteren Aufwand.
- **Pakete aus einer Distribution** sind ein Sonderfall: Distributionen portieren Korrekturen zurück, ohne die Versionsnummer des Originals zu ändern. Eine Komponente mit der Herkunft «aus Distribution» wird deshalb nicht gegen die Bereiche des Originals geprüft; ihr Ergebnis ist *unbekannt*, bis Daten der Distribution vorliegen (erste Quellenwelle). Das gilt auch für den Kernel.
- Kontext aus dem Profil, von der Komponente oder ersatzweise vom Projekt: Erreichbarkeit (Internet, intern, isoliert), Kritikalität (hoch, normal, tief) und optional eine **Schutzmassnahme** (keine Angabe, wirksam), etwa eine vorgeschaltete Filterung oder eine abgeschaltete Funktion, mit Freitext zur Begründung.

## 7.4 Die Massnahme als Arbeitseinheit

Eine Massnahme fasst alle offenen Befunde einer Komponente in einem Projekt zusammen.

| Eigenschaft | Regel |
|---|---|
| Schlüssel | Organisation, Projekt, Komponente |
| Stufe | das Maximum über ihre offenen Befunde |
| Ziel | die niedrigste Version im eigenen Zweig, die alle Befunde behebt; sonst «keine Korrektur verfügbar» mit Umgehungen |
| Inhalt | die Befunde, sortiert nach Stufe; jeder führt zu seiner Schwachstelle mit Einschätzung und Quellen |
| Status | wird an der Massnahme gesetzt und gilt für ihre Befunde; einzelne Befunde lassen sich abweichend setzen |
| Zuwachs | ein neuer Befund tritt einer offenen Massnahme bei; benachrichtigt wird nur, wenn dadurch ihre Stufe steigt (Kapitel 8) |
| Abschluss von selbst | wird im Profil die Version angehoben, schliessen sich alle Befunde, deren Bereich die neue Version nicht mehr enthält, mit dem Vermerk «behoben durch Update» |

Eine Massnahme mit einem einzigen Befund sieht aus wie die Meldung in den Mockups. Die Liste der Oberfläche zeigt Massnahmen; die einzelne Schwachstelle bleibt als Detailansicht erhalten.

Die Schwachstelle aus Sicht der Organisation (`alert`) bleibt als Datensatz bestehen: An ihr hängen Einschätzung, Nummer und Verlauf. Sie kann mehrere Projekte betreffen und damit zu mehreren Massnahmen gehören.

## 7.5 Einschätzung mit dem Sprachmodell (optional)

**Aufgabe.** Das Modell beantwortet, was Regeln nicht können: Unter welchen Voraussetzungen ist die Lücke ausnutzbar, treffen sie hier vermutlich zu, wie wahrscheinlich ist ein Schaden, und was ist als Erstes zu tun?

**Wann es läuft.**

- immer für Befunde, die das Regelwerk mit Stufe 4 oder 5 bewertet;
- für Stufe 3 nur, wenn die Versionsprüfung *unbekannt* ergab;
- je Massnahme und Lauf höchstens fünfmal, die höchsten Stufen zuerst;
- für alles Übrige auf Anforderung in der Oberfläche;
- nie während der Erstbefüllung.

Im Spike entspricht das je nach Profil 6 bis rund 40 Aufrufen pro Woche statt 173.

**Eingabe, bewusst knapp:** vom Eintrag Titel, Beschreibung, betroffene Produkte und Bereiche, Schweregrad mit Vektor, Ausnutzungsstand, Abhilfen und eine nummerierte Quellenliste; vom Profil nur die getroffenen Komponenten mit Version, Erreichbarkeit, Kritikalität und Schutzmassnahme. Projektnamen werden durch Platzhalter ersetzt. Der Rest des Profils verlässt das System nicht.

**Ausgabe als JSON nach festem Schema:**

| Feld | Inhalt |
|---|---|
| `verdict` je Projekt | affected, likely, uncertain, unlikely, not_affected |
| `likelihood` | Wahrscheinlichkeit eines erfolgreichen Angriffs in dieser Umgebung: high, medium, low, mit einem Satz Begründung |
| `confidence` | Sicherheit der Einschätzung: high, medium, low |
| `what_happened` | ein bis zwei Sätze in der Sprache der Organisation |
| `preconditions` | Voraussetzungen für einen Angriff, je «erfüllt», «nicht erfüllt», «unbekannt» |
| `impact` | mögliche Auswirkung in einem Satz |
| `actions` | geordnete Massnahmen, jede mit Verweis auf ihre Quelle oder als eigene Empfehlung gekennzeichnet |
| `unknowns` | was das Profil nicht hergibt und die Einschätzung ändern würde |
| `reasoning` | Begründungssätze mit Verweisnummern |

**Schutz gegen manipulierte Quelltexte.** Eine Beschreibung könnte Anweisungen an das Modell enthalten.

- Das Modell hat keine Werkzeuge und löst nichts aus. Sein einziger Ausgang ist das JSON.
- Quelltexte stehen in einem abgegrenzten Datenblock.
- Die Ausgabe wird gegen das Schema geprüft; Projekt- und Quellenverweise müssen aus der Eingabe stammen. Sonst wird verworfen und einmal wiederholt.
- **Das Modell kann keine Meldung zum Verschwinden bringen.** Sein Urteil `not_affected` zählt in der Rechnung wie `unlikely` und erscheint als Vorschlag «vermutlich nicht betroffen», den ein Mensch bestätigen muss. Stufe 1 entsteht nur aus strukturierten Angaben oder durch einen Menschen.
- Untergrenzen im Regelwerk gelten unabhängig vom Urteil des Modells (7.6).
- Schlägt die Einschätzung zweimal fehl, gilt das Ergebnis der Regeln; die Meldung trägt den Hinweis «ohne KI-Einschätzung».

Jede Einschätzung speichert Modell, Prompt-Version, Regelversion, einen Hash der Eingabe, die Ausgabe, Sprache, Dauer und Tokenzahl.

**Ohne Sprachmodell** entstehen dieselben drei Antworten aus Vorlagen:

| Antwort | Herkunft |
|---|---|
| Was ist passiert | Titel des Eintrags und die ersten Sätze der Beschreibung im Original der Quelle, als solche gekennzeichnet |
| Sind wir betroffen | aus dem Treffer: «Projekt X setzt Produkt 1.2.3 ein; betroffen sind 1.0 bis vor 1.2.5», oder «Version nicht erfasst, Betroffenheit unbekannt» |
| Was ist zu tun | die niedrigste korrigierte Version im eigenen Zweig; sonst Lösungs- und Umgehungstexte der Quelle; sonst «keine Korrektur angegeben» mit Verweis auf das Advisory |

Diese Fassung ist die Vorgabe einer frischen Installation und erhält vor Phase 3 ein eigenes Mockup.

## 7.6 Das Regelwerk für die Stufe

Fünf Stufen, in der Oberfläche als Ziffer und Wort (englisch «Level»):

| Stufe | Name | Typische Fälle | Vorgabe für die Frist |
|---|---|---|---|
| 5 | Critical | aktiv ausgenutzt bei betroffener Version, hohem oder kritischem Schweregrad und erreichbarem System; kritischer Schweregrad mit öffentlichem Exploit auf einem exponierten oder geschäftskritischen System | 48 Stunden |
| 4 | High | kritischer Schweregrad bei betroffener Version; hoher Schweregrad auf einem exponierten oder geschäftskritischen System; jede aktiv ausgenutzte Lücke bei betroffener Version | 7 Tage |
| 3 | Medium | hoher Schweregrad auf einem internen System normaler Kritikalität; mittlerer auf einem exponierten; aktiv ausgenutzt bei unbekannter Version | 30 Tage |
| 2 | Low | alle übrigen starken Treffer | 90 Tage |
| 1 | Not relevant | geprüft, betrifft die Organisation nicht | keine |

Regelwerk Version 1 addiert ganzzahlige Punkte. Jeder Faktor erscheint in der Oberfläche als Zeile mit Beleg und Wirkung.

| Faktor | Ausprägung | Punkte |
|---|---|---:|
| Schweregrad (CVSS-Klasse) | kritisch · hoch · mittel · niedrig · unbewertet | 4 · 3 · 2 · 1 · 2 |
| Ausnutzung | aktiv · Exploit öffentlich · keine bekannt | 3 · 2 · 0 |
| Betroffenheit | Version im Bereich, oder bei unbekannter Version Modell oder Mensch «affected/likely» · unbekannt · «unlikely» | 1 · 0 · −2 |
| Kritikalität des Systems | hoch · normal · tief | 1 · 0 · −1 |
| Erreichbarkeit | Internet · intern · isoliert | 1 · 0 · −1 |
| Schutzmassnahme | wirksam · keine Angabe | −1 · 0 |
| Warnung einer nationalen Stelle zu diesem Eintrag | ja · nein | 1 · 0 |
| EPSS | über 50 % · darunter | 1 · 0 |

| Summe | Stufe |
|---|---|
| 8 und mehr | 5 |
| 5 bis 7 | 4 |
| 4 | 3 |
| 3 und weniger | 2 |

Feste Regeln davor und danach:

- **Stufe 1 ohne Rechnung,** wenn die Version bei allen Komponenten ausserhalb liegt, der Hersteller in strukturierten Daten «nicht betroffen» erklärt, der Treffer schwach ist, der Eintrag zurückgezogen wurde oder ein Mensch den Befund so einstuft.
- **Obergrenze 4,** solange die Betroffenheit nicht durch die Versionsprüfung oder einen Menschen bestätigt ist. «Critical» bleibt Bestätigtem vorbehalten.
- **Untergrenze 4,** wenn die Lücke aktiv ausgenutzt wird und die Version im Bereich liegt; **Untergrenze 5,** wenn dazu der Schweregrad hoch oder kritisch und das System nicht isoliert ist.
- **Untergrenze 3,** wenn die Lücke aktiv ausgenutzt wird und der Treffer stark ist, gleich was Versionsprüfung oder Modell sagen.
- Die Stufe des Befunds gilt je Projekt; die der Massnahme und der Meldung ist das Maximum.

Probe an den Beispieldaten der Mockups (Reihenfolge der Summanden: Schweregrad, Ausnutzung, Betroffenheit, Kritikalität, Erreichbarkeit; die übrigen Faktoren sind hier 0):

| Eintrag | Rechnung | Stufe |
|---|---|---|
| Puppet Enterprise, CVE-2026-115731 | 4 + 3 + 1 + 1 + 0 = 9 | 5 |
| dasselbe vor der Aufnahme in KEV | 4 + 0 + 1 + 1 + 0 = 6 | 4 |
| PHP, CVE-2026-109619 | 4 + 3 + 1 + 0 + 1 = 9 | 5 |
| PostgreSQL, CVE-2026-116325 | 3 + 0 + 1 + 1 + 0 = 5 | 4 |
| MySQL Server, CVE-2026-114952 | 4 + 0 + 1 + 0 + 0 = 5 | 4 |
| Umbraco CMS, CVE-2026-130068 | 2 + 0 + 1 + 0 + 1 = 4 | 3 |
| ingress-nginx, CVE-2026-116472 | 1 + 0 + 1 + 1 + 0 = 3 | 2 |
| SQL Server, CVE-2026-67374 | Azure SQL ist eine Variante und nicht als betroffen genannt | 1 |

Das Beispiel Puppet zeigt, warum EPSS allein nicht genügt: Die Lücke steht seit dem 17.11.2026 im KEV-Katalog, ihr EPSS-Wert lag am selben Tag bei 0,4 %.

Die Gewichte sind eine erste Fassung. Sie werden in Phase 3 am Referenzsatz kalibriert (7.9). Das Regelwerk liegt als versionierte Datei vor; eine neue Version berechnet bestehende Meldungen neu und nennt bei geänderter Stufe den Grund «Regelwerk aktualisiert». Gewichte je Organisation sind eine Ausbaustufe; im MVP sind die Fristen je Organisation einstellbar.

## 7.7 Wann neu bewertet wird

| Auslöser | Umfang |
|---|---|
| Eintrag wesentlich geändert (Kapitel 6.4) | alle Organisationen mit Meldung oder neuem Treffer zu diesem Eintrag |
| Profil geändert | das Projekt gegen die Einträge im Rückblick und gegen seine offenen Befunde |
| Mensch bestätigt, verwirft oder korrigiert eine Einschätzung | dieser Befund |
| Regelwerk in neuer Version | alle offenen Befunde, gedrosselt |

Eine Neubewertung ändert den Befund nur, wenn sich Stufe, Urteil, Ausnutzungsstand, verfügbare Korrektur, Massnahme oder betroffene Projekte ändern. Ändert sich an einem Eintrag nur der Text der Abhilfe (etwa eine neue Umgehung), wird die Antwort «Was ist zu tun» aktualisiert, ohne die Stufe neu zu rechnen; dafür führt der Eintrag einen eigenen Fingerabdruck über seine Abhilfetexte.

**Erstbefüllung.** Wer ein Projekt neu anlegt, bekommt nicht hunderte Alarme. Die Rückwärtssuche rechnet nur mit dem Regelwerk, markiert ihre Befunde als Erstbefüllung und löst eine einzige Sammelnachricht aus: «Ersteinschätzung für Projekt X: 3 Massnahmen, höchste Stufe 4».

## 7.8 Anbindung der KI

- **Abstraktion:** `IChatClient` aus Microsoft.Extensions.AI; die Module kennen keinen Anbieter (Kapitel 2.3).
- **Benannte Anbieter:** Die Instanz konfiguriert null bis mehrere Anbieter, jeder mit Art (OpenAI-kompatibler Endpunkt, Ollama, Azure OpenAI, Anthropic, Gemini), Modell und der Angabe, ob er als lokal gilt. Ohne Anbieter läuft das System mit Regeln und Vorlagen.
- **Datenrichtlinie je Organisation:** `keine KI`, `nur lokal`, `Cloud erlaubt`. Eine Organisation mit `nur lokal` wird nie an einen nicht lokalen Anbieter gesendet, auch nicht als Rückfall.
- **Strukturierte Ausgabe:** JSON-Schema, wo der Anbieter es erzwingen kann; sonst Prüfung und ein Wiederholungsversuch.
- **Sprache:** Erzeugte Texte entstehen in der Sprache der Organisation und werden mit dieser Kennzeichnung gespeichert. Quelltexte bleiben im Original.
- **Grenzen:** Parallelität, Tageskontingent an Tokens je Instanz und je Organisation, Zeitlimit je Aufruf. Bei erschöpftem Kontingent gelten die Regeln allein.
- **Telemetrie:** Aufrufe, Tokens, Dauer, Fehler und Wiederholungen als Metriken; keine Prompt-Inhalte in Logs.

Die Extraktion von Produkten aus Freitextquellen durch ein Modell (Kapitel 6.5) ist eine Ausbaustufe.

## 7.9 Qualität messen, bevor man ihr vertraut

- **Referenzsatz:** mindestens 200 Paare aus Eintrag und Projektprofil mit erwartetem Urteil (betroffen, nicht betroffen, nicht entscheidbar) und erwarteter Stufe. Zusammengestellt aus echten Records nach festen Regeln: je ein Drittel exakte Produkt- und Versionstreffer, Treffer mit unbekannter oder nicht auswertbarer Version, und Nichttreffer mit ähnlichen Namen. Jedes Paar wird von einem Menschen anhand der Quelle beschriftet; die Beschriftungsregel liegt im Repository.
- **Messgrössen:** *Übersehen* ist ein als betroffen beschriftetes Paar, das Stufe 1 erhält. *Unbegründet* ist ein als nicht betroffen beschriftetes Paar mit Stufe 3 oder höher.
- **Schwellen:** kein übersehenes Paar unter den exakten Treffern; höchstens 2 % übersehene insgesamt; höchstens 20 % unbegründete. Der Build scheitert sonst.
- **Kalibrierung:** Die Gewichte aus 7.6 werden am Referenzsatz geprüft und bei Bedarf angepasst, bevor Phase 3 abgenommen wird. Gemessen wird auch der Anteil der Paare mit Ergebnis *unbekannt*; er bestimmt, wie viel Arbeit beim Modell und beim Menschen landet.
- **Regressionslauf:** Jede Änderung an Prompt, Regelwerk, Katalog oder Modell läuft gegen den Referenzsatz.
- **Rückmeldung im Betrieb:** «Einschätzung korrigieren» erfasst falsches Urteil oder falsche Stufe mit Begründung. Die Quote der Korrekturen je Modell und Prompt-Version ist eine Kennzahl in der Administration.

## 7.10 Aufwand

Pro Einschätzung fallen geschätzt 1 500 bis 3 000 Tokens Eingabe und 300 bis 600 Tokens Ausgabe an (aus der mittleren Record-Grösse von 3,6 kB). Bei höchstens 40 Aufrufen pro Woche sind das für die Beispielorganisation rund 150 000 Tokens, auf einem lokalen Modell mit 10 bis 20 Sekunden pro Aufruf unter einer Viertelstunde Rechenzeit. Der Aufwand wächst mit der Zahl der Organisationen und Massnahmen, nicht mit der Zahl der Quellen.
