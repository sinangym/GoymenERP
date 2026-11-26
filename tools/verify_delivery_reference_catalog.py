"""Check delivery references against source; never promote source checks to runtime acceptance."""
from __future__ import annotations

import argparse
import csv
import hashlib
import json
from pathlib import Path
import re
import subprocess

DEFAULT_CATALOG = "docs/delivery/REFERANS_KOD_KATALOGU_v1.json"


def verify_permissions(root: Path) -> list[str]:
    """Check proposal parity only; never assert effective grants or access."""
    mapping = json.loads((root / "docs/delivery/YETKI_KAYNAK_ESLESMESI_v1.json").read_text(encoding="utf-8"))
    source = safe_path(root, mapping["source_path"]).read_text(encoding="utf-8-sig")
    constants = dict(re.findall(r'public const string (\w+)\s*=\s*"([^"]+)";', source))
    def members(name: str) -> set[str]:
        match = re.search(r"public static IReadOnlySet<string> " + name + r".*?\[([^]]*)\]", source, re.S)
        if not match:
            raise ValueError(f"permission set not found: {name}")
        return {constants[key.strip()] for key in re.sub(r"//[^\n]*", "", match[1]).split(",") if key.strip()}
    all_names, critical, wide = members("All"), members("Critical"), members("CompanyWideOnly")
    with (root / "docs/delivery/ROL_YETKI_TASLAGI_v1.csv").open(encoding="utf-8-sig", newline="") as stream:
        reader = csv.DictReader(stream)
        roles = reader.fieldnames[4:-2]
        rows = list(reader)
    indexed = {row["izin"]: row for row in rows}
    entries = mapping["permissions"]
    errors = []
    markdown = (root / "docs/delivery/ROL_YETKI_TASLAGI_v1.md").read_text(encoding="utf-8")
    matrix = markdown.split("### 2.3", 1)[-1].split("### 2.4", 1)[0]
    for row in rows:
        matches = [line for line in matrix.splitlines() if line.startswith(f'| `{row["izin"]}` |')]
        if len(matches) != 1:
            errors.append(f'role markdown row missing or duplicated: {row["izin"]}')
        elif [cell.strip() for cell in matches[0].strip("|").split("|")][1:1 + len(roles)] != [row[role] or "·" for role in roles]:
            errors.append(f'role markdown proposal drift: {row["izin"]}')
    if len(rows) != len(indexed) or set(indexed) != all_names:
        errors.append("role proposal CSV differs from registered permission set")
    if len(entries) != len(all_names) or {entry["permission"] for entry in entries} != all_names:
        errors.append("permission source map differs from registered permission set")
    for key, value in (("count", len(all_names)), ("critical_count", len(critical)), ("company_wide_count", len(wide))):
        if mapping[key] != value:
            errors.append(f"permission count drift: {key}")
    for entry in entries:
        name = entry["permission"]
        row = indexed.get(name)
        if row is None:
            continue
        proposals = {role: row[role] for role in roles if row[role]}
        if entry["role_proposals"] != proposals or entry["proposal_status"] != row["durum"]:
            errors.append(f"permission proposal drift: {name}")
        if entry["critical"] != (name in critical) or entry["company_wide_only"] != (name in wide):
            errors.append(f"permission classification drift: {name}")
        if (row["critical"] == "evet") != (name in critical) or (row["yalniz_sirket_geneli"] == "evet") != (name in wide):
            errors.append(f"permission CSV classification drift: {name}")
    return errors


def verify_approval_navigation(root: Path) -> list[str]:
    mapping = json.loads((root / "docs/delivery/WORK_ONAY_KAPSAM_HARITASI_v1.json").read_text(encoding="utf-8"))
    source = safe_path(root, mapping["source_matrix"]).read_text(encoding="utf-8").splitlines()
    errors = []
    if [row["flow"] for row in mapping["flows"]] != list(range(1, 65)):
        errors.append("approval navigation must contain all 64 unique ordered flows")
    for row in mapping["flows"]:
        line = row["source_row"]
        expected = f'| {row["flow"]} | {row["name"]} | {row["permission_cell"]} |'
        if not 1 <= line <= len(source) or not source[line - 1].startswith(expected):
            errors.append(f'approval matrix row drift: {row["flow"]}')
        if row["runtime_acceptance"] != "NOT_RUN":
            errors.append("navigation cannot claim approval runtime acceptance")
    return errors


def safe_path(root: Path, relative: str) -> Path:
    if not isinstance(relative, str) or not relative or "\\" in relative:
        raise ValueError("reference path must be a nonempty repository-relative POSIX path")
    path = (root / relative).resolve()
    if Path(relative).is_absolute() or not path.is_relative_to(root.resolve()):
        raise ValueError("reference path escapes the repository")
    return path


def git(root: Path, *args: str) -> str:
    return subprocess.check_output(["git", "-C", str(root), *args], text=True, encoding="utf-8").strip()


def verify_catalog(root: Path, catalog: dict) -> list[str]:
    errors: list[str] = []
    source = catalog.get("source_commit", "")
    if not re.fullmatch(r"[0-9a-f]{40}", source):
        return ["source_commit must be an exact commit SHA"]
    seen: set[str] = set()
    for entry in catalog.get("references", []):
        identity = entry.get("id", "")
        if not identity or identity in seen:
            errors.append(f"duplicate or missing reference id: {identity}")
        seen.add(identity)
        if entry.get("runtime_acceptance") != "NOT_RUN":
            errors.append(f"{identity}: this source catalogue cannot assert runtime acceptance")
        if not entry.get("sources") or not entry.get("tests") or not entry.get("limitations"):
            errors.append(f"{identity}: sources, tests and limitations are required")
        for item in entry.get("sources", []) + entry.get("tests", []):
            relative = item.get("path", "")
            try:
                path = safe_path(root, relative)
                data = path.read_bytes()
                if hashlib.sha256(data).hexdigest() != item.get("sha256"):
                    errors.append(f"{identity}: working source drift: {relative}")
                if git(root, "rev-parse", f"{source}:{relative}") != item.get("git_blob"):
                    errors.append(f"{identity}: snapshot blob mismatch: {relative}")
                lines = data.decode("utf-8-sig").splitlines()
                line = item.get("line", 0)
                anchor = item.get("anchor", "")
                if not anchor or not 1 <= line <= len(lines) or anchor not in lines[line - 1]:
                    errors.append(f"{identity}: missing symbol/test anchor: {relative}")
                if entry.get("historical_evidence") == "HISTORICAL_CI_TREE_MATCH":
                    ci_source = catalog.get("historical_ci", {}).get("source_commit", "")
                    if not re.fullmatch(r"[0-9a-f]{40}", ci_source):
                        raise ValueError("historical CI must name an exact source commit")
                    if git(root, "rev-parse", f"{ci_source}:{relative}") != item.get("git_blob"):
                        errors.append(f"{identity}: historical CI source differs: {relative}")
            except (ValueError, OSError, subprocess.CalledProcessError) as exc:
                errors.append(f"{identity}: invalid reference {relative}: {type(exc).__name__}")
    if not seen:
        errors.append("catalogue is empty")
    return errors


def verify_plan(root: Path, plan: dict, catalog: dict) -> list[str]:
    errors: list[str] = []
    backlog = json.loads((root / "docs/backlog/DELIVERY_BACKLOG_v1.json").read_text(encoding="utf-8-sig"))
    parents = {item["id"] for wave in backlog["waves"] for item in wave["slices"]}
    refs = {item["id"] for item in catalog["references"]}
    packages = plan.get("packages", [])
    ids = [item["id"] for item in packages]
    if len(ids) != len(set(ids)):
        errors.append("duplicate implementation package id")
    for item in packages:
        name = item["id"]
        if item.get("backlog_parent") not in parents:
            errors.append(f"{name}: unknown canonical backlog parent")
        if not set(item.get("references", [])) <= refs:
            errors.append(f"{name}: unknown reference id")
        if not set(item.get("depends_on", [])) <= set(ids):
            errors.append(f"{name}: unknown package dependency")
        for key in ("scope", "source_findings", "files", "owner", "risk", "technical_acceptance", "business_acceptance", "reuse_mode"):
            if not item.get(key):
                errors.append(f"{name}: missing {key}")
        if item.get("status") == "READY" and (item.get("open_decisions") or item.get("depends_on")):
            errors.append(f"{name}: READY must not hide unresolved prerequisites")
        for relative in item.get("files", []):
            if not safe_path(root, relative).is_file():
                errors.append(f"{name}: missing existing source file: {relative}")
    visiting: set[str] = set()
    done: set[str] = set()
    by_id = {item["id"]: item for item in packages}
    def visit(name: str) -> None:
        if name in visiting:
            errors.append(f"dependency cycle at {name}")
            return
        if name in done or name not in by_id:
            return
        visiting.add(name)
        for dependency in by_id[name].get("depends_on", []):
            visit(dependency)
        visiting.remove(name)
        done.add(name)
    for name in by_id:
        visit(name)
    return errors


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--catalog", default=DEFAULT_CATALOG)
    parser.add_argument("--plan", default="docs/delivery/UYGULAMA_PAKETLERI_2026-11-26.json")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    catalog = json.loads(safe_path(root, args.catalog).read_text(encoding="utf-8"))
    plan = json.loads(safe_path(root, args.plan).read_text(encoding="utf-8"))
    errors = verify_catalog(root, catalog) + verify_plan(root, plan, catalog) + verify_permissions(root) + verify_approval_navigation(root)
    for error in errors:
        print(error)
    print(json.dumps({"references": len(catalog["references"]), "packages": len(plan["packages"]),
                      "errors": len(errors), "runtime_acceptance": "NOT_RUN"}))
    return 1 if errors else 0


if __name__ == "__main__":
    raise SystemExit(main())
