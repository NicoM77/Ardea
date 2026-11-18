# 7 · Relevanzprüfung, KI-Analyse und Priorisierung

## 7.1 Grundsatz

Die Bewertung ist ein Trichter aus billigen, deterministischen Schritten. Das Sprachmodell sieht nur einen kleinen Rest und vergibt keine Stufe; die berechnet ein Regelwerk.

Was der Daten-Spike für eine Organisation mit den drei Beispielprojekten in einer Woche ergab (`prototypes/relevance-funnel/`):

| Schritt | Menge |
|---|---:|
| Publizierte Records, neu oder geändert | 6 432 |
| Kandidaten nach strukturiertem Abgleich | 173 (2,7 %) |
| davon neu publiziert, der Rest sind Änderungen an älteren Records | 82 |
| Massnahmen: Paare aus Projekt und Technologie | 8 |
| Befunde ab Stufe 3, je nach Version und Erreichbarkeit | 6 bis 170 |

Die letzte Zeile ist die wichtigste Erkenntnis der Planung. Der Vorfilter allein genügt nicht: Trifft eine Organisation ein Sammelupdate, entstehen über hundert Befunde in einer Woche. Daraus folgen zwei Festlegungen:

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

Ein schwacher Treffer erzeugt Stufe 1 mit Grund («verwandtes Produkt, nicht als betroffen genannt», «Plattform im Einsatz, Komponente nicht im Profil») und keine Benachrichtigung. Er wird als Befund mit dem Status `not_affected` gespeichert, gehört zu keiner Massnahme und erhält keine Nummer. Die Zahl der schwachen Treffer je Projekt ist eine Kennzahl für Lücken im Profil.

## 7.3 Versionsprüfung und Kontext

Für jeden starken Treffer:

- Versionsvergleich nach Kapitel 6.6 mit dem Ergebnis *im Bereich*, *ausserhalb* oder *unbekannt*. Ein Befund gehört zu genau einer Komponente; *ausserhalb* ergibt für diesen Befund Stufe 1, ohne weiteren Aufwand. Führt ein Projekt dasselbe Produkt in zwei Versionen, sind das zwei Komponenten, zwei Befunde und zwei Massnahmen.
- **Pakete aus einer Distribution** sind ein Sonderfall: Distributionen portieren Korrekturen zurück, ohne die Versionsnummer des Originals zu ändern. Eine Komponente mit der Herkunft «aus Distribution» wird deshalb nicht gegen die Bereiche des Originals geprüft; ihr Ergebnis ist *unbekannt*, bis Daten der Distribution vorliegen (erste Quellenwelle). Das gilt auch für den Kernel.
- Kontext aus dem Profil: Erreichbarkeit (Internet, intern, isoliert) und Kritikalität (hoch, normal, tief) von der Komponente oder ersatzweise vom Projekt. Dazu optional eine **Schutzmassnahme** (keine Angabe, wirksam) an der Komponente, etwa eine vorgeschaltete Filterung oder eine abgeschaltete Funktion, mit Freitext zur Begründung.

## 7.4 Die Massnahme als Arbeitseinheit

Eine Massnahme fasst alle offenen Befunde einer Komponente in einem Projekt zusammen.

| Eigenschaft | Regel |
|---|---|
| Schlüssel | Organisation, Projekt, Komponente |
| Stufe | das Maximum über ihre offenen Befunde |
| Ziel | Jeder Befund kennt die Korrekturversion des Bereichs, in dem die installierte Version liegt; dieser Bereich steht für den eigenen Versionszweig. Das Ziel der Massnahme ist die höchste dieser Versionen. Nennt ein Befund keine Korrekturversion: Lösungs- und Umgehungstexte der Quelle, sonst «keine Korrektur angegeben». Ist die installierte Version unbekannt, nennt die Massnahme alle Korrekturversionen der Quelle. |
| Inhalt | die Befunde, sortiert nach Stufe; jeder führt zu seiner Schwachstelle mit Einschätzung und Quellen |
| Status | `open` und `in_review` werden an der Massnahme gesetzt und gelten für alle ihre nicht abgeschlossenen Befunde, auch für später hinzukommende. Das Ergebnis (behoben, Risiko akzeptiert, nicht betroffen) steht je Befund und lässt sich für alle auf einmal setzen. Geschlossen ist die Massnahme, sobald kein Befund mehr offen oder in Prüfung ist. |
| Zuwachs | ein neuer Befund ab Stufe 2 tritt der offenen Massnahme seiner Komponente bei; benachrichtigt wird nur, wenn dadurch ihre Stufe oder ihr Ausnutzungsstand steigt (Kapitel 8.2) |
| Abschluss von selbst | wird im Profil die Version angehoben, schliessen sich alle Befunde, deren Bereich die neue Version nicht mehr enthält, mit dem Vermerk «behoben durch Update» |

Eine Massnahme mit einem einzigen Befund sieht aus wie die Meldung in den Mockups. Die Liste der Oberfläche zeigt Massnahmen; die einzelne Schwachstelle bleibt als Detailansicht erhalten.

Die Schwachstelle aus Sicht der Organisation (`alert`) bleibt als Datensatz bestehen: An ihr hängen die Einschätzung und eine Nummer, die sie erhält, sobald einer ihrer Befunde Stufe 2 erreicht. Der Verlauf hängt an Befund und Massnahme. Eine Meldung kann mehrere Komponenten und Projekte betreffen und damit zu mehreren Massnahmen gehören.

## 7.5 Einschätzung mit dem Sprachmodell (optional)

**Aufgabe.** Das Modell beantwortet, was Regeln nicht können: Unter welchen Voraussetzungen ist die Lücke ausnutzbar, treffen sie hier vermutlich zu, wie wahrscheinlich ist ein Schaden, und was ist als Erstes zu tun?

**Was ein Aufruf umfasst.** Eine Einschätzung gilt einer Meldung, also einem Eintrag aus Sicht einer Organisation. Ein Aufruf deckt alle getroffenen Komponenten dieser Organisation ab und liefert je Komponente ein Urteil.

**Wann es läuft.** Die Regeln rechnen zuerst (`AssessAlert`). Danach wird die Einschätzung als eigener Job eingereiht (`EstimateAlert`), wenn

- mindestens ein Befund der Meldung nach den Regeln Stufe 4 oder 5 hat, oder Stufe 3 bei unbekannter Version;
- und das Kontingent jeder betroffenen Massnahme noch frei ist: höchstens fünf selbst ausgelöste Einschätzungen je Massnahme in sieben Tagen. Wartende Aufträge sind nach Stufe geordnet, die höchsten zuerst.

Was darüber liegt, bleibt bei Regeln und Vorlagen, bis jemand die Einschätzung in der Oberfläche anfordert. Während der Erstbefüllung läuft das Modell nie.

Im Spike entspricht das je nach Profil 6 bis höchstens 40 Aufrufen pro Woche (8 Massnahmen mal 5) statt 173.

**Eingabe, bewusst knapp:** vom Eintrag Titel, Beschreibung, betroffene Produkte und Bereiche, Schweregrad mit Vektor, Ausnutzungsstand, Abhilfen und eine nummerierte Quellenliste; vom Profil nur die getroffenen Komponenten mit Version, Erreichbarkeit, Kritikalität und Schutzmassnahme. Projektnamen werden durch Platzhalter ersetzt. Der Rest des Profils verlässt das System nicht.

**Ausgabe als JSON nach festem Schema:**

| Feld | Inhalt |
|---|---|
| `verdict` je getroffener Komponente | affected, likely, uncertain, unlikely, not_affected |
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
- Die Ausgabe wird gegen das Schema geprüft; Verweise auf Komponenten und Quellen müssen aus der Eingabe stammen. Sonst wird verworfen und einmal wiederholt.
- **Das Modell kann keine Meldung zum Verschwinden bringen.** Sein Urteil `not_affected` zählt in der Rechnung wie `unlikely` und erscheint als Vorschlag «vermutlich nicht betroffen», den ein Mensch bestätigen muss. Stufe 1 entsteht nur aus strukturierten Angaben oder durch einen Menschen.
- Untergrenzen im Regelwerk gelten unabhängig vom Urteil des Modells, und das Urteil senkt die Stufe um höchstens eine (7.6).
- Schlägt die Einschätzung zweimal fehl, gilt das Ergebnis der Regeln; die Meldung trägt den Hinweis «ohne KI-Einschätzung».

**Längen.** `what_happened` höchstens 240 Zeichen, jeder Schritt in `actions` höchstens 140, `impact` höchstens 120. Längere Ausgaben werden verworfen und einmal wiederholt. Die Vorlagen ohne Sprachmodell kürzen Quelltexte am Satzende auf dieselben Grenzen. So bleiben die drei Antworten am Telefon im ersten Bildschirm.

Jede Einschätzung speichert Modell, Prompt-Version, Regelversion, einen Hash der Eingabe, die Ausgabe, Sprache, Dauer und Tokenzahl.

**Ohne Sprachmodell** entstehen dieselben drei Antworten aus Vorlagen:

| Antwort | Herkunft |
|---|---|
| Was ist passiert | Titel des Eintrags und die ersten Sätze der Beschreibung im Original der Quelle, als solche gekennzeichnet |
| Sind wir betroffen | aus dem Treffer: «Projekt X setzt Produkt 1.2.3 ein; betroffen sind 1.0 bis vor 1.2.5», oder «Version nicht erfasst, Betroffenheit unbekannt» |
| Was ist zu tun | das Ziel der Massnahme nach 7.4: die Korrekturversion des Bereichs, in dem die installierte Version liegt; sonst Lösungs- und Umgehungstexte der Quelle; sonst «keine Korrektur angegeben» mit Verweis auf das Advisory |

Diese Fassung ist die Vorgabe einer frischen Installation und erhält vor Phase 3 ein eigenes Mockup.

## 7.6 Das Regelwerk für die Stufe

Fünf Stufen, in der Oberfläche als Priorität: das Wort mit einem eigenen Symbol, ohne Ziffer (Kapitel 11.3). Die Zahl ordnet die Stufen intern.

| Stufe | Name | Typische Fälle | Vorgabe für die Frist |
|---|---|---|---|
| 5 | Critical | aktiv ausgenutzt bei betroffener Version, hohem oder kritischem Schweregrad und erreichbarem System; kritischer Schweregrad mit öffentlichem Exploit bei betroffener Version auf einem exponierten oder geschäftskritischen System | 48 Stunden |
| 4 | High | kritischer Schweregrad bei betroffener Version; hoher Schweregrad bei betroffener Version auf einem exponierten oder geschäftskritischen System; jede aktiv ausgenutzte Lücke bei betroffener Version; aktiv ausgenutzt bei unbekannter Version ab mittlerem Schweregrad | 7 Tage |
| 3 | Medium | hoher Schweregrad bei betroffener Version auf einem internen System normaler Kritikalität; mittlerer bei betroffener Version auf einem exponierten; kritischer bei unbekannter Version; aktiv ausgenutzt bei unbekannter Version und niedrigem Schweregrad | 30 Tage |
| 2 | Low | alle übrigen starken Treffer | 90 Tage |
| 1 | Not relevant | geprüft, betrifft die Organisation nicht; kein offener Befund, keine Massnahme | keine |

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

Die Rechnung läuft für jeden Befund in fester Reihenfolge:

1. **Ausschluss, Stufe 1 ohne Rechnung:** Der Treffer ist schwach, die Versionsprüfung ergibt *ausserhalb*, der Hersteller erklärt das Produkt in strukturierten Daten für nicht betroffen, der Eintrag ist zurückgezogen, oder ein Mensch stuft den Befund so ein. Ein Ausschluss beruht immer auf strukturierten Angaben oder auf einem Menschen. Er geht allen folgenden Schritten vor, auch den Untergrenzen.
2. **Punkte** nach der Tabelle, daraus die Stufe.
3. **Obergrenzen:**
   - 4, solange die Betroffenheit nicht durch die Versionsprüfung oder einen Menschen bestätigt ist. «Critical» bleibt Bestätigtem vorbehalten.
   - 2 für Komponenten, die nur in der Entwicklung verwendet werden (Merkmal `development` aus dem Repository, Kapitel 16.5), solange kein Mensch sie anders einstuft. Sie werden nicht ausgeliefert; die Untergrenzen im nächsten Schritt gelten für sie trotzdem.
4. **Untergrenzen,** die kein anderer Faktor unterschreitet:
   - 3, wenn die Lücke aktiv ausgenutzt wird und kein Ausschluss greift, die Version also im Bereich liegt oder unbekannt ist;
   - 4, wenn die Lücke aktiv ausgenutzt wird und die Version im Bereich liegt;
   - 5, wenn dazu der Schweregrad hoch oder kritisch und das System nicht isoliert ist;
   - höchstens eine Stufe unter dem Ergebnis ohne Modell, solange kein Mensch das Urteil des Modells bestätigt hat.

Die Stufe gilt je Befund, also je Komponente. Die Stufe der Massnahme ist das Maximum über ihre offenen Befunde, die der Meldung das Maximum über ihre Befunde.

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

Die Gewichte sind eine erste Fassung. Sie werden in Phase 3 am Referenzsatz kalibriert (7.9). Das Regelwerk liegt als versionierte Datei vor; eine neue Version berechnet offene Befunde neu und nennt bei geänderter Stufe den Grund «Regelwerk aktualisiert». Gewichte je Organisation sind eine Ausbaustufe; im MVP sind die Fristen je Organisation einstellbar.

## 7.7 Wann neu bewertet wird

| Auslöser | Umfang |
|---|---|
| Eintrag wesentlich geändert (Kapitel 6.4) | alle Organisationen mit Befund oder neuem Treffer zu diesem Eintrag |
| Profil geändert, von Hand oder durch ein Einlesen aus dem Repository | das Projekt gegen die Einträge im Rückblick und gegen seine offenen Befunde |
| Mensch bestätigt, verwirft oder korrigiert eine Einschätzung | dieser Befund |
| Regelwerk in neuer Version | alle offenen Befunde, gedrosselt |

Eine Neubewertung ändert den Befund nur, wenn sich Stufe, Urteil, Ausnutzungsstand, verfügbare Korrektur, Massnahme oder betroffene Projekte ändern. Ändert sich an einem Eintrag nur der Text der Abhilfe (etwa eine neue Umgehung), wird die Antwort «Was ist zu tun» aktualisiert, ohne die Stufe neu zu rechnen; dafür führt der Eintrag einen eigenen Fingerabdruck über seine Abhilfetexte.

**Erstbefüllung.** Wer ein Projekt neu anlegt, bekommt nicht hunderte Alarme. Die Rückwärtssuche rechnet nur mit dem Regelwerk, markiert ihre Befunde als Erstbefüllung und löst eine einzige Sammelnachricht aus: «Ersteinschätzung für Projekt X: 3 Massnahmen, höchste Stufe 4». Dasselbe gilt für das erste Einlesen eines neu verbundenen Repositorys, auch an einem bestehenden Projekt (Kapitel 16.5).

## 7.8 Anbindung der KI

- **Abstraktion:** `IChatClient` aus Microsoft.Extensions.AI; die Module kennen keinen Anbieter (Kapitel 2.3).
- **Benannte Anbieter:** Die Instanz konfiguriert null bis mehrere Anbieter, jeder mit Art, Modell und der Angabe, ob er als lokal gilt. Im MVP gibt es drei Arten: OpenAI-kompatibler Endpunkt, Ollama, Anthropic. Azure OpenAI und Gemini folgen über dieselbe Abstraktion (Entscheidung E6). Ohne Anbieter läuft das System mit Regeln und Vorlagen.
- **Datenrichtlinie je Organisation:** `keine KI`, `nur lokal`, `Cloud erlaubt`. Eine Organisation mit `nur lokal` wird nie an einen nicht lokalen Anbieter gesendet, auch nicht als Rückfall.
- **Strukturierte Ausgabe:** JSON-Schema, wo der Anbieter es erzwingen kann; sonst Prüfung und ein Wiederholungsversuch.
- **Sprache:** Erzeugte Texte entstehen in der Sprache der Organisation und werden mit dieser Kennzeichnung gespeichert. Quelltexte bleiben im Original.
- **Grenzen:** Parallelität, Tageskontingent an Tokens je Instanz und je Organisation, Zeitlimit je Aufruf. Bei erschöpftem Kontingent gelten die Regeln allein.
- **Telemetrie:** Aufrufe, Tokens, Dauer, Fehler und Wiederholungen als Metriken; keine Prompt-Inhalte in Logs.

Die Extraktion von Produkten aus Freitextquellen durch ein Modell (Kapitel 6.5) ist eine Ausbaustufe.

## 7.9 Qualität messen, bevor man ihr vertraut

- **Referenzsatz:** mindestens 200 Paare aus Eintrag und Projektprofil mit erwartetem Urteil (betroffen, nicht betroffen, nicht entscheidbar) und erwarteter Stufe. Zusammengestellt aus echten Records nach festen Regeln: je ein Drittel exakte Produkt- und Versionstreffer, Treffer mit unbekannter oder nicht auswertbarer Version, und Nichttreffer mit ähnlichen Namen. Mindestens 30 Paare haben eine installierte Version oberhalb aller genannten Bereiche, je zur Hälfte tatsächlich betroffen und nicht betroffen; sie prüfen die Auslegung aus Kapitel 6.6. Jedes Paar wird von einem Menschen anhand der Quelle beschriftet; die Beschriftungsregel liegt im Repository.
- **Messgrössen:** *Übersehen* ist ein als betroffen beschriftetes Paar, das Stufe 1 erhält. *Unbegründet* ist ein als nicht betroffen beschriftetes Paar mit Stufe 3 oder höher.
- **Schwellen:** kein übersehenes Paar unter den exakten Treffern; höchstens 2 % übersehene insgesamt; höchstens 20 % unbegründete. Der Build scheitert sonst.
- **Kalibrierung:** Die Gewichte aus 7.6 werden am Referenzsatz geprüft und bei Bedarf angepasst, bevor Phase 3 abgenommen wird. Gemessen wird auch der Anteil der Paare mit Ergebnis *unbekannt*; er bestimmt, wie viel Arbeit beim Modell und beim Menschen landet.
- **Regressionslauf:** Jede Änderung an Regelwerk oder Katalog läuft in der Pipeline gegen den Referenzsatz, nur mit Regeln und in unter einer Minute. Ein Lauf mit echtem Sprachmodell gibt es nicht, weder in der Pipeline noch daneben (Kapitel 13.6). Die Einbindung des Modells wird mit aufgezeichneten Antworten getestet; seine Güte zeigt die Korrekturquote im Betrieb.
- **Rückmeldung im Betrieb:** «Einschätzung korrigieren» erfasst falsches Urteil oder falsche Stufe mit Begründung. Die Quote der Korrekturen je Modell und Prompt-Version ist eine Kennzahl in der Administration.

## 7.10 Aufwand

Pro Einschätzung fallen geschätzt 1 500 bis 3 000 Tokens Eingabe und 300 bis 600 Tokens Ausgabe an (aus der mittleren Record-Grösse von 3,6 kB). Bei höchstens 40 Aufrufen pro Woche sind das für die Beispielorganisation rund 150 000 Tokens, auf einem lokalen Modell mit 10 bis 20 Sekunden pro Aufruf unter einer Viertelstunde Rechenzeit. Der Aufwand wächst mit der Zahl der Organisationen und Massnahmen, nicht mit der Zahl der Quellen.
