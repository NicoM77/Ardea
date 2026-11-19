# 17 · Stack der Organisation und Hinweise

Ardea ist eine Hinweis- und Alert-Anwendung, keine Ressourcenplanung. Dieses Kapitel macht das Profil zweistufig: grob für die Organisation, genau für Projekte mit Repository. Es ersetzt zugleich die Massnahme (Aufgabe mit Zuweisung und Frist) durch den **Reminder** (Erinnerung ohne Zuweisung und ohne Frist). Alle anderen Kapitel sind entsprechend umbenannt; bei Widerspruch gilt dieses Kapitel.

## 17.1 Zwei Tiefen

| Tiefe | Wo | Angabe | Ergebnis |
|---|---|---|---|
| **Grob** | Stack der Organisation; Projekte ohne Repository | Technologien ohne Version («Angular, .NET, PostgreSQL»), Version optional | **Hinweis** «könnte euch betreffen» mit Ja/Nein |
| **Genau** | Projekte mit Repository oder mit exakten Versionen | Pakete und Versionen aus dem Repository, Plattformen als Vorschlag | Befund und **Reminder** mit Stufe, wie bisher |

Die Tiefe hängt an der einzelnen Komponente, nicht am Projekt: Eine Komponente mit exakter Version geht den genauen Weg, eine ohne Version oder nur mit Linie («PostgreSQL 17») den groben. Wer später eine exakte Version einträgt, wechselt von selbst.

## 17.2 Der Stack der Organisation

Der Stack ist ein festes Systemprojekt der Organisation (`assets.project.kind = organization_stack`). Dadurch gelten Komponenten, Suchindex und Kandidatensuche unverändert. Je Technologie ein Eintrag aus dem Katalog, breit gefasst (Angular, .NET, PostgreSQL, Windows Server, FortiGate). `component.version_precision` ist `none`, `line` oder `exact`. Je Eintrag gibt es eine Schwelle: **nur Wichtiges** (Vorgabe: hoher oder kritischer Schweregrad oder ausgenutzt) oder **alles**.

Ein breiter Eintrag deckt nur das Produkt selbst ab (Produkt, CPE, purl des Produkts), nicht das Ökosystem: «.NET» meldet nichts zu einem beliebigen NuGet-Paket, «Angular» nichts zu einem beliebigen npm-Paket. Der Wizard fragt zuerst den Stack ab; Projekte und Repositories sind danach optional (Kapitel 11.6, 16.7).

## 17.3 Hinweis

Zu einem Eintrag, der ein Produkt des Stacks trifft, entsteht ein Hinweis (`assess.notice`, je Organisation und Eintrag, eindeutig), sofern die Schwelle des Stack-Eintrags erreicht ist. Eine angegebene Versionslinie grenzt vorher ein. Er zeigt die drei Antworten und den Grund («Euer Stack nennt PostgreSQL»), Schweregrad und Ausnutzung der Quelle. Er hat keine eigene Stufe und ist danach sortiert (E23).

| Entscheidung | Folge |
|---|---|
| **Betrifft uns** | Es entsteht ein ganz normaler Reminder (Kapitel 7.4), optional mit Projekt und Version; mit Version wird genau geprüft (E21) |
| **Betrifft uns nicht** | Der Hinweis ist erledigt und kommt nicht zurück, ausser die Lücke wird später aktiv ausgenutzt oder in KEV aufgenommen; dann erscheint er einmal erneut (E22) |

Kein Sprachmodell für Hinweise, ausser auf Anforderung. Die Zahl der Hinweise je Woche für einen typischen Stack ist vor dem Bau zu messen (17.9).

## 17.4 Genau bleibt genau

Projekte mit Repository oder exakten Versionen verhalten sich wie in Kapitel 7 und 16: Befunde, Stufe, Reminder. Ein Repository macht Stack-Einträge im Projekt genau («Angular» im Stack und `@angular/core` 22.2.1 im Repository: im Projekt genau, im Stack grob).

## 17.5 Der Reminder

| Eigenschaft | Festlegung |
|---|---|
| Was er ist | Erinnerung: «Das betrifft euch, das ist zu tun» |
| Entfällt | Zuweisung, Zuständige, Frist, Restzeit, Eskalation, Status «in Prüfung», Dauer bis Abschluss |
| Status | `open` und `done` (Grund: behoben, Risiko akzeptiert, nicht betroffen) |
| Erinnerung | Offene Reminder erinnern nach Stufe: 5 täglich, 4 wöchentlich, 3 monatlich, darunter nur in der Sammelnachricht; je Organisation einstellbar (E26) |

## 17.6 Migration (E25)

Offene Massnahmen zu Komponenten ohne Version werden zu Hinweisen; Reminders aus genauen Komponenten bleiben. Abgeschlossene bleiben unverändert. Die Spalten `assigned_to` und `due_at` entfallen nach der Verträglichkeitsregel in 4.11 erst eine Version später.

## 17.7 Oberfläche

Seite **Notices** als Posteingang: Karte mit den drei Antworten, Grund, «Affects us» und «Doesn't affect us», per Tastatur bedienbar. Die Übersicht zeigt «n neue Hinweise». Die Detailseite des Reminders zeigt die drei Antworten und den Status; Punkterechnung, Faktoren und Verlauf sind zugeklappt.

## 17.8 Beispiele für Stack-Einträge

Angular · React · Vue.js · Node.js · .NET / ASP.NET Core · Java / Spring · PHP · Python / Django · Go · WordPress · Umbraco · TYPO3 · Shopware · PostgreSQL · Microsoft SQL Server · MySQL / MariaDB · Redis · MongoDB · Elasticsearch · RabbitMQ · Windows Server · Ubuntu Linux (mit Schwelle «nur Wichtiges») · nginx · Apache HTTP Server · IIS · Docker · Kubernetes · Microsoft Azure · Fortinet FortiGate · Microsoft Exchange Server. Eine typische Organisation gibt 8 bis 15 an.

## 17.9 Offen

- **Lärm messen:** Hinweise je Woche für typische Stacks mit den Wochendaten des Spikes; danach Schwellen festlegen (Aufgabe 5a.1). Der Linux-Kernel liefert ohne Schwelle hunderte je Woche.
- Erinnerungsrhythmus und «nur das Produkt selbst» sind Empfehlungen (E26, 17.2), vom Eigentümer noch nicht bestätigt.
