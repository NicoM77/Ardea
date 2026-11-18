Real responses of the public JSON interface of the NCSC Cyber Security Hub (`https://security-hub.ncsc.admin.ch`), recorded on 18 Nov 2026 with `curl` and an identifying User-Agent. No login was involved; all posts are TLP:CLEAR.

| File | Request |
|---|---|
| `dashboard-page0.json` | `GET /api/v1/posts/dashboard?pageSize=50&pageIndex=0` (50 of 336 posts) |
| `dashboard-page1.json` | `GET /api/v1/posts/dashboard?pageSize=50&pageIndex=1` |
| `post-{id}.json` | `GET /api/v1/posts/{id}/details` for 13196 (no CVE anywhere), 13203 (CVE ids only in the text), 13198 (CVE in title; edited three times, one edit added the CVE id), 13083, 13054, 13163 (several CVE ids), 13180 (no CVE) |

Facts the tests rely on: `Cache-Control: no-store`, no ETag, no Last-Modified; `lastChange` is on every list item, `lastModified` only on edited posts; the details repeat `title` and `summary` and add `content` (Markdown), `history[]` and `files[]`.

Terms: the owner decided on 18 Nov 2026 (master plan decision E10) that Ardea may use this interface and store the advisory texts. The interface is undocumented and `robots.txt` asks automated agents to stay away; the adapter therefore polls every 30 minutes at most and can be switched off.
