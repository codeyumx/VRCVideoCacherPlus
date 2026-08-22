#!/usr/bin/env python3
"""
Locale linter for VRCVideoCacher/Languages/*.loc.json.

English is the fallback, which is exactly why translation problems are invisible at
runtime: a missing key silently renders the English string, an empty one silently
renders nothing at all, and a key nobody references any more just sits there being
translated into eight languages forever. This turns all of that into a build failure.

Checks, all fatal:

  missing      a key en.loc.json has that a translation does not
  unexpected   a key a translation has that en.loc.json does not
  empty        a value that is empty or only whitespace
  untranslated a value byte-identical to the English one. Two escape hatches, kept
               apart on purpose:
                 locale-allow-identical.txt   intentional — proper nouns, brand names,
                                              anything that is the same word everywhere
                 locale-untranslated.txt      known debt, listed as lang:Key. This file
                                              is meant to shrink; the linter fails if an
                                              entry is stale so finishing a translation
                                              forces the line to be deleted.
  unused       a key defined in English that no .cs or .axaml file references

Written in Python rather than bash because every one of these needs real JSON
parsing, and `jq` is not something the build can assume is installed.

  scripts/lint-locales.py            report and exit non-zero on any problem
  scripts/lint-locales.py --list-identical   print keys that are identical to English,
                                             in allowlist format, and exit 0
"""

import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
LANG_DIR = ROOT / "VRCVideoCacher" / "Languages"
ALLOW_IDENTICAL = ROOT / "scripts" / "locale-allow-identical.txt"
UNTRANSLATED_BASELINE = ROOT / "scripts" / "locale-untranslated.txt"
SOURCE_DIRS = [ROOT / "VRCVideoCacher"]

# Deliberately looser than Localizer.Get("Key"): keys are routinely produced in one
# place and resolved in another — VideoDownloader returns a bare "SkipReasonBotCheck",
# and VideoId builds "SkipReasonTooLong|{0:F0}|{1}" — so a precise pattern reports all
# of those as dead. A whole-word search over the source text is used instead. It errs
# towards calling a key used, which is the right direction: an unused-key report that
# cannot be trusted is worse than one that occasionally misses something.
def is_referenced(key, text):
    return re.search(rf"\b{re.escape(key)}\b", text) is not None


def load(path):
    with path.open(encoding="utf-8") as handle:
        return json.load(handle)


def load_list(path):
    if not path.exists():
        return set()
    entries = set()
    for line in path.read_text(encoding="utf-8").splitlines():
        line = line.split("#", 1)[0].strip()
        if line:
            entries.add(line)
    return entries


def referenced_keys(keys):
    """The subset of `keys` mentioned anywhere in the source tree."""
    remaining = set(keys)
    found = set()
    for source_dir in SOURCE_DIRS:
        for pattern in ("**/*.cs", "**/*.axaml"):
            for path in source_dir.glob(pattern):
                if not remaining:
                    return found
                as_posix = path.as_posix()
                if "/obj/" in as_posix or "/bin/" in as_posix:
                    continue
                text = path.read_text(encoding="utf-8", errors="replace")
                hits = {key for key in remaining if is_referenced(key, text)}
                found |= hits
                remaining -= hits
    return found


def main():
    english_path = LANG_DIR / "en.loc.json"
    english = load(english_path)
    translations = sorted(p for p in LANG_DIR.glob("*.loc.json") if p != english_path)

    def language_of(path):
        return path.name.removesuffix(".loc.json")

    if "--list-identical" in sys.argv:
        for path in translations:
            data = load(path)
            for key in sorted(k for k, v in data.items() if k in english and v == english[k]):
                print(f"{language_of(path)}:{key}")
        return 0

    allow_identical = load_list(ALLOW_IDENTICAL)
    baseline = load_list(UNTRANSLATED_BASELINE)
    still_identical = set()
    problems = []

    def report(path, kind, detail):
        problems.append(f"{path.relative_to(ROOT)}: {kind}: {detail}")

    for key, value in english.items():
        if not str(value).strip():
            report(english_path, "empty", key)

    used = referenced_keys(english)
    for key in sorted(set(english) - used):
        report(english_path, "unused", f"{key} is not referenced by any .cs or .axaml file")

    for path in translations:
        data = load(path)

        for key in sorted(set(english) - set(data)):
            report(path, "missing", key)
        for key in sorted(set(data) - set(english)):
            report(path, "unexpected", f"{key} is not defined in en.loc.json")

        for key, value in data.items():
            if not str(value).strip():
                report(path, "empty", key)
            elif key in english and value == english[key] and key not in allow_identical:
                entry = f"{language_of(path)}:{key}"
                still_identical.add(entry)
                if entry not in baseline:
                    report(path, "untranslated", f'{key} is identical to English ("{value}")')

    for stale in sorted(baseline - still_identical):
        report(
            UNTRANSLATED_BASELINE,
            "stale",
            f"{stale} is translated now (or gone) — delete this line",
        )

    if problems:
        print(f"Locale check FAILED with {len(problems)} problem(s):\n", file=sys.stderr)
        for problem in problems:
            print(f"  {problem}", file=sys.stderr)
        print(
            "\nA value that is legitimately the same in every language (a proper noun, a"
            "\nunit, a command name) belongs in scripts/locale-allow-identical.txt."
            "\nA translation that is simply not written yet belongs in"
            "\nscripts/locale-untranslated.txt as lang:Key — run --list-identical to"
            "\nregenerate that list.",
            file=sys.stderr,
        )
        return 1

    print(f"Locale check passed: {len(english)} keys across {len(translations) + 1} languages.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
