#!/usr/bin/env python3
"""Fails when a lock file resolves a library the master plan excludes (chapter 2.6, 13.5).

The licence gate catches most licence changes, but some excluded libraries still declare an
allowed licence in their package metadata while requiring a commercial key at runtime. They are
listed here by name and, where only newer versions changed, by the first excluded major version.
"""
import json
import pathlib
import sys

# package id (lower case) -> first excluded major version (0 = every version)
BANNED = {
    "mediatr": 0,
    "automapper": 0,
    "masstransit": 9,
    "fluentassertions": 8,
    "sixlabors.imagesharp": 0,
    "duende.identityserver": 0,
    "tickerq": 0,
    # Verify 33 enforces a maintenance-fee licence check at build time (found 18 Nov 2026).
    "verify": 0,
    "verify.xunitv3": 0,
    "verify.xunit": 0,
}

# npm packages (lower case) -> first excluded major version
BANNED_NPM = {
    "primeng": 22,
}


def major(version: str) -> int:
    head = version.lstrip("[(^~>=v ").split(".")[0]
    return int(head) if head.isdigit() else 0


def main() -> int:
    root = pathlib.Path(__file__).resolve().parent.parent
    hits = []
    for lock in root.glob("**/packages.lock.json"):
        if any(part in ("bin", "obj", "node_modules") for part in lock.parts):
            continue
        data = json.loads(lock.read_text(encoding="utf-8"))
        for deps in data.get("dependencies", {}).values():
            for name, info in deps.items():
                floor = BANNED.get(name.lower())
                if floor is not None and major(info.get("resolved", "0")) >= floor:
                    hits.append(f"{lock.relative_to(root)}: {name} {info.get('resolved')}")
    for lock in root.glob("web/package-lock.json"):
        data = json.loads(lock.read_text(encoding="utf-8"))
        for path, info in data.get("packages", {}).items():
            name = path.rsplit("node_modules/", 1)[-1].lower()
            floor = BANNED_NPM.get(name)
            if floor is not None and major(info.get("version", "0")) >= floor:
                hits.append(f"{lock.relative_to(root)}: {name} {info.get('version')}")
    for hit in hits:
        print(f"::error::Excluded library: {hit}")
    if not hits:
        print("No excluded libraries.")
    return 1 if hits else 0


if __name__ == "__main__":
    sys.exit(main())
