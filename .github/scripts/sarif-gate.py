"""Fails when CodeQL found security issues with security-severity >= 7.0 (high/critical).

Used when the results cannot be uploaded to GitHub code scanning (private repository without GitHub Code Security):
the analysis still runs, the SARIF is kept as an artifact, and this gate keeps the check meaningful.

    python .github/scripts/sarif-gate.py <directory with *.sarif>
"""
import json
import pathlib
import sys

THRESHOLD = 7.0


def severity(rule: dict) -> float:
    try:
        return float((rule.get("properties") or {}).get("security-severity", 0))
    except (TypeError, ValueError):
        return 0.0


def main(directory: str) -> int:
    files = sorted(pathlib.Path(directory).glob("*.sarif"))
    if not files:
        print(f"::error::No SARIF files in {directory}")
        return 1

    blocking, total = [], 0
    for file in files:
        sarif = json.loads(file.read_text(encoding="utf-8"))
        for run in sarif.get("runs", []):
            # CodeQL puts the rule metadata into the query packs (tool.extensions), other tools into the driver.
            tool = run.get("tool", {})
            components = [tool.get("driver", {}), *tool.get("extensions", [])]
            rules = {r.get("id"): r for c in components for r in c.get("rules", [])}
            for result in run.get("results", []):
                total += 1
                rule_id = result.get("ruleId") or (result.get("rule") or {}).get("id") or "?"
                rule = rules.get(rule_id, {})
                score = severity(rule)
                if score >= THRESHOLD:
                    location = (result.get("locations") or [{}])[0].get("physicalLocation", {})
                    path = location.get("artifactLocation", {}).get("uri", "?")
                    line = location.get("region", {}).get("startLine", "?")
                    message = (result.get("message", {}).get("text", "").splitlines() or [""])[0][:200]
                    blocking.append(f"{score:.1f} {rule_id} {path}:{line} {message}")

    print(f"CodeQL: {total} result(s) in {len(files)} file(s), {len(blocking)} with security-severity >= {THRESHOLD}.")
    for item in blocking:
        print(f"::error::{item}")
    return 1 if blocking else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1] if len(sys.argv) > 1 else "sarif-results"))
