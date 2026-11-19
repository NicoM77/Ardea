#!/usr/bin/env python3
"""Writes the spike week as a test dataset: data/reference/spike-week.jsonl.gz (task 3.7).

Every record that appeared or changed from 11 to 17 November 2026 (newest state each, rejected ones
included), as the CVE Program published it, keeping only records that mention one of the technologies of
the three example projects anywhere: in a vendor, product, CPE, package name or text. This is the spike's
naive keyword match, widened (prototypes/relevance-funnel), and a superset of what the structured search
can hit for these profiles; a record that names none of the words cannot match them. The full week was
run once against the same profiles with the same open actions (ADR 036); the filter
keeps the test's run time down.

Usage:
  python3 -I tools/reference-set/spike_week.py <spike-dir> <repo-root>
"""

import glob
import gzip
import json
import os
import re
import sys

WORDS = re.compile(
    r"\.net|dotnet|asp\.?net|angular|umbraco|sql server|sql_server|azure sql|mssql|wordpress|\bphp\b|mysql|kubernetes|\bk8s\b"
    r"|linux|kernel|redis|postgres|nuget|npm",
    re.IGNORECASE)


def main(spike, root):
    latest = {}
    for day in sorted(os.listdir(spike)):
        folder = os.path.join(spike, day)
        if os.path.isdir(folder) and day.isdigit():
            for path in glob.glob(os.path.join(folder, "**", "CVE-*.json"), recursive=True):
                latest[os.path.basename(path)[:-5]] = path
    lines = []
    for cve in sorted(latest):
        with open(latest[cve], encoding="utf-8") as f:
            line = json.dumps(json.load(f), separators=(",", ":"), ensure_ascii=False)
        if WORDS.search(line):
            lines.append(line)
    target = os.path.join(root, "data", "reference", "spike-week.jsonl.gz")
    # mtime 0: the same records give the same bytes.
    with open(target, "wb") as raw, gzip.GzipFile(filename="spike-week.jsonl", mode="wb", fileobj=raw, mtime=0, compresslevel=9) as out:
        out.write(("\n".join(lines) + "\n").encode("utf-8"))
    print(f"{len(lines)} of {len(latest)} records", file=sys.stderr)


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    main(sys.argv[1], sys.argv[2])
