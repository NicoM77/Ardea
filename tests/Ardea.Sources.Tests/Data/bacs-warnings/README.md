Real responses of the JSON the website of the Federal Office for Cyber Security (BACS) is built from (`https://www.bacs.admin.ch/api/collector/...`), recorded on 18 Nov 2026 with `curl` and an identifying User-Agent. No login was involved.

| File | Request |
|---|---|
| `current-incidents.json` | `GET /api/collector/en/current-incidents.json` (overview with 12 teasers; the overview itself has `updatedAt`) |
| `warnung-java-2-en.json` | `GET /api/collector/en/warnung-java-2-en.json` (CVE in title and lead, links to hub post 13198) |
| `warnung-java-en.json` | same for `warnung-java-en` (no CVE id, links to hub post 13198) |
| `warnung-php-en.json` | same for `warnung-php-en` (links to hub post 13083) |
| `warnung-estv-en.json` | same for `warnung-estv-en` (phishing, no CVE, no hub link) |

Facts the tests rely on: `Cache-Control: public,max-age=0,s-maxage=20,stale-while-revalidate=60`, no ETag, no Last-Modified; a page has `documentId`, `updatedAt`, `metadata.groupId`, `metadata.description`, `metadata.publishDate`; the overview's teasers carry slug, title, lead and date but no `updatedAt`.

Terms (https://www.admin.ch/en/terms-and-conditions): reproduction requires the prior written consent of the copyright holder. The adapter therefore keeps metadata only (title, lead, link), and so do these fixtures. They were reduced after recording to the fields the adapter reads: ids, groupId, slug, title, lead/description, dates, `updatedAt`, and the overview's teasers (slug, title, lead, date). No page body or other overview content is kept. Each page's `content` is a one-paragraph SYNTHETIC PLACEHOLDER (not BACS text) that only carries the hub links the real page contained, so link extraction stays tested.
