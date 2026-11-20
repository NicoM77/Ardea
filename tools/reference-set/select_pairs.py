#!/usr/bin/env python3
"""Selects the reference set's pairs from the spike week (master plan 7.9, task 3.7).

The pairs come from real CVE records by fixed rules, so the set can be rebuilt and checked:

  exact          a catalogue product or package the record names, with an installed version the
                 record's ranges can be compared with: inside an affected range, or above every
                 range the record names (the interpretation of master plan 6.6, row 5)
  unknown        a product the record names, with no installed version, a distribution package,
                 or ranges that cannot be compared (Git commits only)
  similar_name   a catalogue product whose name resembles the record's product, but is not it

The selection only proposes pairs. Whether a pair is affected, and which level it deserves, is a
person's judgement from the record (data/reference/LABELLING.md); this script never labels.

Usage:
  python3 -I tools/reference-set/select_pairs.py <spike-dir> <repo-root>

<spike-dir> holds the unpacked delta archives of 11-17 November 2026 in folders 11 ... 17 (see
the planning spike at tag archive/planning-and-design, prototypes/relevance-funnel/README.md). Writes data/reference/records.jsonl and the unlabelled pairs
to data/reference/pairs.json; existing labels in pairs.json are kept for pairs that are selected again.
"""

import glob
import hashlib
import json
import os
import re
import sys
from collections import defaultdict

TARGET = {"exact_in_range": 44, "exact_above_hint": 26, "exact_above_plain": 20, "unknown": 70, "similar_name": 70, "above_fix_text": 30}
MAX_PER_PRODUCT = {"exact": 4, "unknown": 3, "similar_name": 3}
# Products a Linux distribution typically ships and patches itself (master plan 6.6: backports).
DISTRIBUTED = {
    "openssl", "glibc", "curl", "libxml2", "libxslt", "postgresql", "mysql", "mariadb", "php", "python", "nodejs",
    "openjdk", "linux-kernel", "nginx", "apache-httpd", "git", "openssh", "sudo", "systemd", "bash", "redis", "valkey",
    "memcached", "tomcat", "samba", "bind", "squid", "haproxy", "go", "perl", "ruby", "sqlite", "zlib", "expat", "vim",
}
PLUGIN_PAIRS = 10
STOP_WORDS = {
    "server", "client", "plugin", "plugins", "for", "the", "and", "core", "web", "app", "application", "community",
    "edition", "enterprise", "open", "source", "framework", "manager", "management", "platform", "service", "services",
    "studio", "suite", "cloud", "data", "pro", "lite", "free", "theme", "library", "sdk", "api", "tools", "tool",
    "system", "systems", "portal", "center", "connector", "module", "extension", "addon", "add-on", "microsoft",
    "apache", "oracle", "google", "ibm", "red", "hat", "linux", "windows", "net", "http", "kit", "agent", "desktop",
    "database", "db", "engine", "runtime", "security", "network", "mobile", "online", "office", "file", "files",
    "version", "line", "host", "wordpress", "templates", "template", "blocks", "block", "form", "forms", "page", "pages",
    "builder", "user", "users", "admin", "login", "image", "images", "gallery", "custom", "simple", "easy", "post", "posts",
}
COLLECTIONS = {
    "registry.npmjs.org": "npm", "www.npmjs.com": "npm", "npmjs.com": "npm",
    "pypi.org": "pypi", "pypi.python.org": "pypi",
    "www.nuget.org": "nuget", "nuget.org": "nuget", "api.nuget.org": "nuget",
    "packagist.org": "composer", "repo.packagist.org": "composer",
    "repo.maven.apache.org": "maven", "repo1.maven.org": "maven", "central.sonatype.com": "maven",
    "pkg.go.dev": "golang", "proxy.golang.org": "golang",
    "rubygems.org": "gem", "crates.io": "cargo",
}
VERSION = re.compile(r"^v?(\d+)(?:\.(\d+))?(?:\.(\d+))?(?:\.(\d+))?$")
# A fix named in the prose: «fixed in 2.5», «upgrade to version 2.5», «before 2.5».
FIX_TEXT = re.compile(
    r"(?:fixed in|fixes? (?:it|this|the issue)? ?in|upgrade to|update to|patched in|resolved in|before|prior to|earlier than)"
    r"\s+(?:version\s+)?v?(\d+\.\d+(?:\.\d+){0,2})", re.IGNORECASE)
TEXT_VERSION = re.compile(r"(?<![\w.])v?(\d+\.\d+(?:\.\d+){0,2})(?![\w.])")


def jsonc(text):
    """The catalogue is JSON with comments and trailing commas."""
    out, i, in_string = [], 0, False
    while i < len(text):
        c = text[i]
        if in_string:
            out.append(c)
            if c == "\\":
                out.append(text[i + 1])
                i += 2
                continue
            if c == '"':
                in_string = False
        elif c == '"':
            in_string = True
            out.append(c)
        elif text.startswith("//", i):
            j = text.find("\n", i)
            i = len(text) if j < 0 else j
            continue
        else:
            out.append(c)
        i += 1
    return json.loads(re.sub(r",(\s*[}\]])", r"\1", "".join(out)))


def rank(*parts):
    """A fixed pseudo-random order: the same input always selects the same pairs."""
    return hashlib.sha256("|".join(parts).encode()).hexdigest()


def norm(text):
    return re.sub(r"\s+", " ", (text or "").strip().lower())


def parse(version):
    m = VERSION.match((version or "").strip())
    return tuple(int(p) for p in m.groups() if p is not None) if m else None


def fmt(parts):
    return ".".join(str(p) for p in parts)


def bump(parts):
    """The next patch above a version: 2.3 -> 2.3.1, 2.3.4 -> 2.3.5."""
    if len(parts) < 3:
        return parts + (0,) * (2 - len(parts)) + (1,)
    return parts[:-1] + (parts[-1] + 1,)


def below(parts):
    """A version just below a fix: 2.3.4 -> 2.3.3, 2.3.0 -> 2.2.9 (approximate), 2.0 -> 1.9."""
    parts = parts + (0,) * (3 - len(parts)) if len(parts) < 3 else parts
    if parts[2] > 0:
        return parts[:2] + (parts[2] - 1,) + parts[3:]
    if parts[1] > 0:
        return (parts[0], parts[1] - 1, 9)
    if parts[0] > 0:
        return (parts[0] - 1, 9, 9)
    return None


class Catalog:
    def __init__(self, root):
        self.products = {}
        self.by_cpe = {}
        self.by_package = {}
        self.aliases = []  # (vendor or None, compiled name pattern, key)
        for path in sorted(glob.glob(os.path.join(root, "data", "catalog", "*.json"))):
            doc = jsonc(open(path, encoding="utf-8").read())
            for p in doc["products"]:
                p["vendorName"] = doc["vendor"]
                self.products[p["key"]] = p
                for cpe in p.get("cpe", []):
                    self.by_cpe[cpe.lower()] = p["key"]
                for purl in p.get("packages", []):
                    self.by_package[purl.lower()] = p["key"]
                names = [p["name"]]
                self.aliases.append((norm(doc["vendor"]), self.pattern(p["name"]), p["key"]))
                for alias in p.get("aliases", []):
                    for name in alias["names"]:
                        names.append(name)
                        self.aliases.append((norm(alias.get("vendor")) or None, self.pattern(name), p["key"]))
                p["tokens"] = {t for n in names for t in re.findall(r"[a-z][a-z0-9+#]{2,}", n.lower())} - STOP_WORDS

    @staticmethod
    def pattern(name):
        escaped = re.escape(norm(name)).replace(r"\{line\}", r"[\w.\- ]+").replace(r"\{version\}", r"[\w.\- ]+")
        return re.compile("^" + escaped + "$")

    def resolve(self, affected):
        """The catalogue product an affected entry names, by CPE, package or vendor and name."""
        for cpe in affected.get("cpes", []):
            bits = cpe.split(":")
            if len(bits) > 4 and f"{bits[3]}:{bits[4]}".lower() in self.by_cpe:
                return self.by_cpe[f"{bits[3]}:{bits[4]}".lower()], "cpe"
        purl = package(affected)
        if purl and purl.lower() in self.by_package:
            return self.by_package[purl.lower()], "package"
        vendor, product = norm(affected.get("vendor")), norm(affected.get("product"))
        if product in ("", "n/a"):
            return None, None
        for alias_vendor, pattern, key in self.aliases:
            if pattern.match(product) and (alias_vendor is None or vendor in ("", "n/a") or alias_vendor in vendor or vendor in alias_vendor):
                return key, "name"
        return None, None


def package(affected):
    name, url = affected.get("packageName"), affected.get("collectionURL") or ""
    if not name:
        return None
    host = re.sub(r"^https?://", "", url).split("/")[0].lower()
    kind = COLLECTIONS.get(host)
    if kind == "npm" and name.startswith("@"):
        name = "%40" + name[1:]
    return f"pkg:{kind}/{name}" if kind else None


def load_records(spike):
    """The newest state of every record of the week; later day folders win."""
    latest = {}
    for day in sorted(os.listdir(spike)):
        folder = os.path.join(spike, day)
        if not (os.path.isdir(folder) and day.isdigit()):
            continue
        for path in glob.glob(os.path.join(folder, "**", "CVE-*.json"), recursive=True):
            latest[os.path.basename(path)[:-5]] = path
    records = {}
    for cve, path in latest.items():
        with open(path, encoding="utf-8") as f:
            record = json.load(f)
        if record.get("cveMetadata", {}).get("state") == "PUBLISHED":
            records[cve] = record
    return records


def description(record):
    for d in record["containers"]["cna"].get("descriptions", []):
        if d.get("lang", "").startswith("en"):
            return d.get("value", "")
    return ""


def ranges(affected):
    """(kind, bound) pairs of the affected statements: lessThan, lessThanOrEqual or an exact version."""
    out = []
    for v in affected.get("versions", []):
        if v.get("status") != "affected":
            continue
        if v.get("lessThan") and parse(v["lessThan"]):
            out.append(("lessThan", parse(v["lessThan"]), parse(v.get("version")), v))
        elif v.get("lessThanOrEqual") and parse(v["lessThanOrEqual"]):
            out.append(("lessThanOrEqual", parse(v["lessThanOrEqual"]), parse(v.get("version")), v))
        elif parse(v.get("version")) and not v.get("lessThan") and not v.get("lessThanOrEqual"):
            out.append(("exact", parse(v["version"]), parse(v["version"]), v))
    return out


def only_git(affected):
    versions = affected.get("versions", [])
    return bool(versions) and all(v.get("versionType") == "git" for v in versions)


def main(spike, root):
    catalog = Catalog(root)
    records = load_records(spike)
    print(f"{len(records)} published records, {len(catalog.products)} catalogue products", file=sys.stderr)

    hits = []          # (cve, affected index, key or None, purl or None, how)
    unmatched = []     # (cve, affected index)
    for cve, record in records.items():
        for i, affected in enumerate(record["containers"]["cna"].get("affected", [])):
            key, how = catalog.resolve(affected)
            purl = package(affected)
            if key or purl:
                hits.append((cve, i, key, purl, how))
            else:
                unmatched.append((cve, i))

    pairs = []
    used = defaultdict(set)      # group -> records already used
    per_product = defaultdict(int)

    def take(group, sub, cve, product, component, reason, above=False):
        bucket = "exact" if group == "exact" else group
        if cve in used[bucket] or per_product[(bucket, product)] >= MAX_PER_PRODUCT[bucket]:
            return False
        used[bucket].add(cve)
        per_product[(bucket, product)] += 1
        pairs.append({"group": group, "subgroup": sub, "aboveRanges": above, "record": cve, "component": component, "selection": reason})
        return True

    def component_for(key, purl, version, packaging="upstream"):
        if key:
            p = catalog.products[key]
            return {"productKey": key, "name": p["name"], "kind": p.get("kind", "other"), "version": version, "packaging": packaging}
        name = purl.split("/", 1)[1]
        return {"purl": f"{purl}@{version}" if version else purl, "name": name, "kind": "library", "version": version,
                "packaging": "upstream", "dependencyScope": "runtime"}

    counts = defaultdict(int)
    for cve, i, key, purl, how in sorted(hits, key=lambda h: rank("exact", h[0], str(h[1]))):
        affected = records[cve]["containers"]["cna"]["affected"][i]
        found = ranges(affected)
        product = key or purl
        if not found or only_git(affected):
            continue
        bounds = [b for _, b, _, _ in found]
        top = max(bounds)
        text_versions = [parse(v) for v in TEXT_VERSION.findall(description(records[cve]))]
        hint = any(v and v > top for v in text_versions)
        # Inside an affected range: the lower bound when it names one, else just below the fix.
        if counts["exact_in_range"] < TARGET["exact_in_range"]:
            kind, bound, start, v = found[0]
            if kind == "exact":
                inside = bound
            elif start and any(start) and start < bound:
                inside = start
            elif kind == "lessThanOrEqual":
                inside = bound
            else:
                inside = below(bound)
            if inside and take("exact", "in_range", cve, product, component_for(key, purl, fmt(inside)),
                               f"affected[{i}] names {product} via {how or 'package'}; installed version inside {kind} {fmt(bound)}"):
                counts["exact_in_range"] += 1
                continue
        sub = "exact_above_hint" if hint else "exact_above_plain"
        if counts[sub] < TARGET[sub]:
            above = bump(top)
            note = "the description names a higher version" if hint else "the description names no higher version"
            if take("exact", sub[6:], cve, product, component_for(key, purl, fmt(above)),
                    f"affected[{i}] names {product} via {how or 'package'}; installed version above every range (highest bound {fmt(top)}); {note}", above=True):
                counts[sub] += 1

    # Unknown or unusable version, in three kinds: a distribution package (backports), ranges that are Git
    # commits only, and no installed version in the profile.
    unknown = {"distribution": 20, "git_only": 10}
    for cve, i, key, purl, how in sorted(hits, key=lambda h: rank("unknown", h[0], str(h[1]))):
        affected = records[cve]["containers"]["cna"]["affected"][i]
        product = key or purl
        if only_git(affected) and counts["git_only"] < unknown["git_only"]:
            version = "6.12.1" if key == "linux-kernel" else "1.0.0"
            if take("unknown", "git_only", cve, product, component_for(key, purl, version), f"affected[{i}] names {product} via {how or 'package'}; Git commits only"):
                counts["git_only"] += 1
                counts["unknown"] += 1
        elif key in DISTRIBUTED and counts["distribution"] < unknown["distribution"] and (found := ranges(affected)):
            kind, bound, start, _ = found[0]
            inside = start if start and any(start) else (bound if kind != "lessThan" else below(bound))
            if inside and take("unknown", "distribution", cve, product, component_for(key, purl, fmt(inside), packaging="distribution"),
                               f"affected[{i}] names {product} via {how or 'package'}; a distribution package of a version inside {kind} {fmt(bound)}"):
                counts["distribution"] += 1
                counts["unknown"] += 1
    for cve, i, key, purl, how in sorted(hits, key=lambda h: rank("unknown", h[0], str(h[1]))):
        if counts["unknown"] >= TARGET["unknown"]:
            break
        product = key or purl
        if take("unknown", "no_version", cve, product, component_for(key, purl, None), f"affected[{i}] names {product} via {how or 'package'}; no version in the profile"):
            counts["unknown"] += 1

    # A plugin or theme of WordPress, with WordPress itself in the profile: the platform is not affected.
    plugins = 0
    for cve, i in sorted(unmatched, key=lambda u: rank("plugin", u[0], str(u[1]))):
        if plugins >= PLUGIN_PAIRS:
            break
        affected = records[cve]["containers"]["cna"]["affected"][i]
        text = description(records[cve]).lower()
        if any(catalog.resolve(a)[0] == "wordpress" for a in records[cve]["containers"]["cna"].get("affected", [])):
            continue
        if "wordpress" in text and re.search(r"\b(plugin|theme)\b", text) and norm(affected.get("product")) not in ("", "n/a", "wordpress"):
            if take("similar_name", "platform_plugin", cve, f"wordpress-plugin:{cve}", component_for("wordpress", None, "6.8.3"),
                    f"affected[{i}] is the WordPress plugin or theme {affected.get('product')}; the profile has WordPress itself"):
                plugins += 1
                counts["similar_name"] += 1

    # Similar names: a record whose product the catalogue does not know, but whose name contains a catalogue
    # product's name from another vendor or as another product ("MySQL Server" and "SQL Server", "Firefox for
    # iOS"), or shares a distinctive word with one. A name that is the catalogue product plus a version or
    # edition is the same product, not a similar one, and is left out.
    catalog_df = defaultdict(int)
    for p in catalog.products.values():
        for t in p["tokens"]:
            catalog_df[t] += 1
    record_df = defaultdict(int)
    for cve, i in unmatched:
        affected = records[cve]["containers"]["cna"]["affected"][i]
        for t in set(re.findall(r"[a-z][a-z0-9+#]{2,}", norm(f"{affected.get('vendor', '')} {affected.get('product', '')}"))):
            record_df[t] += 1
    seen_names = set()
    for cve, i in sorted(unmatched, key=lambda u: rank("similar", u[0], str(u[1]))):
        if counts["similar_name"] >= TARGET["similar_name"]:
            break
        affected = records[cve]["containers"]["cna"]["affected"][i]
        product_name = norm(affected.get("product"))
        named = norm(f"{affected.get('vendor', '')} {affected.get('product', '')}")
        if product_name in ("", "n/a") or named in seen_names:
            continue
        words = set(re.findall(r"[a-z][a-z0-9+#]{2,}", named)) - STOP_WORDS
        contained = []
        for k, p in catalog.products.items():
            name = norm(p["name"])
            if len(name) >= 4 and name in named:
                rest = re.sub(r"\(.*?\)|\b\d[\w.]*\b|\b(gdr|cu|lts|esr|for|x64-based|x86-based|arm64-based|32-bit|64-bit|systems|edition|version|installation|server core)\b", " ", named.replace(name, " "))
                rest = set(re.findall(r"[a-z]{2,}", rest)) - set(re.findall(r"[a-z]{2,}", norm(p["vendorName"]))) - {"microsoft", "inc", "ltd", "corporation", "the"}
                if rest:
                    contained.append(k)
        shared = [k for k, p in catalog.products.items()
                  if any(len(t) >= 5 and catalog_df[t] == 1 and record_df[t] <= 15 for t in p["tokens"] & words)]
        # A record that names the product in another of its affected entries hits it: not a similar name.
        named_elsewhere = {catalog.resolve(a)[0] for a in records[cve]["containers"]["cna"].get("affected", [])}
        contained = [k for k in contained if k not in named_elsewhere]
        shared = [k for k in shared if k not in named_elsewhere]
        close = sorted(contained, key=lambda k: rank(cve, k)) or sorted(shared, key=lambda k: rank(cve, k))
        if not close:
            continue
        key = close[0]
        p = catalog.products[key]
        # No version: a wrong name match then shows as an open finding instead of hiding behind a version check.
        version = None
        why = f"contains «{p['name']}»" if contained else "shares «" + ", ".join(sorted(t for t in p["tokens"] & words if len(t) >= 5 and catalog_df[t] == 1)) + "»"
        if take("similar_name", "contains_name" if contained else "shared_word", cve, key, component_for(key, None, version),
                f"affected[{i}] is {affected.get('vendor')} / {affected.get('product')}, which the catalogue does not know; {why} with {key}"):
            seen_names.add(named)
            counts["similar_name"] += 1

    # Added after the first labelling (2026-11-19): above every range, but the prose names a later fix. Few such
    # records exist, and the first selection found too few of them for master plan 7.9 (half of the pairs above
    # every range affected). They are selected last, so the pairs above keep being selected as before.
    for cve, i, key, purl, how in sorted(hits, key=lambda h: rank("fixtext", h[0], str(h[1]))):
        if counts["above_fix_text"] >= TARGET["above_fix_text"]:
            break
        affected = records[cve]["containers"]["cna"]["affected"][i]
        found = ranges(affected)
        if not found or only_git(affected) or cve in used["exact"]:
            continue
        top = max(b for _, b, _, _ in found)
        installed = bump(top)
        text = description(records[cve]) + " " + " ".join(s.get("value", "") for s in records[cve]["containers"]["cna"].get("solutions", []))
        named = [v for v in (parse(m) for m in FIX_TEXT.findall(text)) if v and v > installed]
        if not named:
            continue
        product = key or purl
        if take("exact", "above_fix_text", cve, f"{product}:fixtext", component_for(key, purl, fmt(installed)),
                f"affected[{i}] names {product} via {how or 'package'}; installed version above every range (highest bound {fmt(top)}); the text names a fix {fmt(min(named))} above it",
                above=True):
            counts["above_fix_text"] += 1

    print(dict(counts), file=sys.stderr)
    write(root, records, pairs)


def write(root, records, pairs):
    folder = os.path.join(root, "data", "reference")
    os.makedirs(folder, exist_ok=True)
    path = os.path.join(folder, "pairs.json")
    previous = {}
    if os.path.exists(path):
        with open(path, encoding="utf-8") as f:
            for p in json.load(f)["pairs"]:
                previous[(p["record"], json.dumps(p["component"], sort_keys=True))] = p
    order = {"exact": 0, "unknown": 1, "similar_name": 2}
    pairs.sort(key=lambda p: (order[p["group"]], p["subgroup"], p["record"]))
    # Ids stay with their pair once given; labels and reports refer to them. New pairs get the next ids.
    taken = [int(p["id"][1:]) for p in previous.values()]
    next_id = max(taken, default=0) + 1
    for p in pairs:
        old = previous.get((p["record"], json.dumps(p["component"], sort_keys=True)))
        if old:
            p["id"] = old["id"]
        else:
            p["id"] = f"P{next_id:03d}"
            next_id += 1
        profile = rank("profile", p["record"], p["component"].get("productKey") or p["component"].get("purl"))
        p["project"] = {
            "criticality": ["normal", "normal", "high", "low"][int(profile[:2], 16) % 4],
            "exposure": ["internal", "internet", "internal", "isolated"][int(profile[2:4], 16) % 4],
        }
        p["label"] = old["label"] if old else None
    pairs.sort(key=lambda p: p["id"])
    keys = ["id", "group", "subgroup", "aboveRanges", "record", "project", "component", "selection", "label"]
    with open(path, "w", encoding="utf-8") as f:
        json.dump({"format": "ardea-reference-set", "version": 1, "pairs": [{k: p[k] for k in keys} for p in pairs]}, f, indent=2, ensure_ascii=False)
        f.write("\n")
    with open(os.path.join(folder, "records.jsonl"), "w", encoding="utf-8") as f:
        for cve in sorted({p["record"] for p in pairs}):
            f.write(json.dumps(records[cve], separators=(",", ":"), ensure_ascii=False) + "\n")
    print(f"{len(pairs)} pairs, {len({p['record'] for p in pairs})} records", file=sys.stderr)


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    main(sys.argv[1], sys.argv[2])
