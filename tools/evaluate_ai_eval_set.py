#!/usr/bin/env python3
"""Deterministic evaluator skeleton for the AI evaluation set (Y-A1, DALGA_PLANI_v2 A.2, AI-D24, YOL-D21).

Without a model (the only mode in this slice: Ai:Enabled=false, no provider key, no network) it validates the set:
schema, unique ids, category counts, every expected source id against the decision index, every expected tool call
against the tool catalog, SENTETIK marking and the absence of personal data (the decision index's own
``personal_name_terms`` plus e-mail/phone/national id/IBAN patterns). It never calls a model.

Usage::

    python3 -I tools/evaluate_ai_eval_set.py              # validation report on stdout, exit 1 on any error
    python3 -I tools/evaluate_ai_eval_set.py --model x    # refused: model runs are a later, human-gated step
"""

from __future__ import annotations

import argparse
import importlib.util
import json
import sys
from collections import Counter
from pathlib import Path
from typing import Any

REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
EVAL_SET_PATH = "contracts/ai/eval/eval-set-v1.json"
EVAL_SCHEMA_PATH = "contracts/ai/eval/eval-set-v1.schema.json"
TOOL_CATALOG_PATH = "contracts/ai/tool-catalog-v1.json"
MINIMUM_DECISION_ITEMS = 150
SHIPMENT_SCENARIOS = 20
DECISION_CATEGORIES = ("bilgi_cikarma", "guncellenen_karar", "cevap_verememe", "yetki_disi")
SHIPMENT_TOOL = "logistics.pending-shipments.v1"


def _load_generator() -> Any:
    path = Path(__file__).resolve().parent / "generate_ai_decision_index.py"
    spec = importlib.util.spec_from_file_location("goymen_ai_decision_index_for_eval", path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Cannot load {path}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


GENERATOR = _load_generator()


def _read_json(root: Path, relative: str) -> Any:
    return json.loads((root / relative).read_text(encoding="utf-8"))


def evaluate(root: Path = REPOSITORY_ROOT) -> dict[str, Any]:
    errors: list[str] = []
    warnings: list[str] = []
    eval_set = _read_json(root, EVAL_SET_PATH)
    schema = _read_json(root, EVAL_SCHEMA_PATH)
    errors.extend(GENERATOR.validate_schema(eval_set, schema))
    if errors:
        return _report(eval_set, errors, warnings)

    index = _read_json(root, GENERATOR.INDEX_PATH)
    records = {record["id"]: record for record in index["records"]}
    catalog = _read_json(root, TOOL_CATALOG_PATH)
    tools = {(tool["toolId"], tool["version"]): tool for tool in catalog["tools"]}
    names = index["personal_name_terms"]

    items = eval_set["items"]
    identities = [item["id"] for item in items]
    for identity, count in Counter(identities).items():
        if count > 1:
            errors.append(f"{identity}: kimlik tekrar ediyor")
    counts = Counter(item["category"] for item in items)
    decision_items = sum(counts[category] for category in DECISION_CATEGORIES)
    if decision_items < MINIMUM_DECISION_ITEMS:
        errors.append(f"karar sorusu sayısı {decision_items} < {MINIMUM_DECISION_ITEMS}")
    for category in DECISION_CATEGORIES:
        if counts[category] == 0:
            errors.append(f"{category} kategorisinde madde yok")
    if counts["islem_sorgusu"] != SHIPMENT_SCENARIOS:
        errors.append(f"bekleyen sevkiyat senaryosu sayısı {counts['islem_sorgusu']} != {SHIPMENT_SCENARIOS}")

    for item in items:
        identity = item["id"]
        is_shipment = item["category"] == "islem_sorgusu"
        if is_shipment != identity.startswith("EV-S-"):
            errors.append(f"{identity}: kimlik öneki kategoriyle uyuşmuyor")
        for source in item["expected_source_ids"]:
            record = records.get(source)
            if record is None:
                errors.append(f"{identity}: kaynak {source} karar dizininde yok")
            elif record["status"] == "gecersiz" and item["category"] != "guncellenen_karar":
                warnings.append(f"{identity}: kaynak {source} artık geçersiz")
        if item["category"] in ("bilgi_cikarma", "guncellenen_karar") and not item["expected_source_ids"]:
            errors.append(f"{identity}: kaynaklı cevap maddesinde kaynak yok")
        if item["category"] == "guncellenen_karar":
            sources = item["expected_source_ids"]
            if len(sources) != 2 or not all(source in records for source in sources) or not any(
                    link["by"] == sources[1] for link in records[sources[0]]["superseded_by"]):
                errors.append(f"{identity}: güncelleyen karar ilişkisi dizinde yok")
        text = item["question"] + "\n" + item["expected_answer_summary"]
        signals = GENERATOR._personal_signals(text, names)
        if signals:
            errors.append(f"{identity}: kişisel veri işareti {signals}")
        call = item.get("expected_tool_call")
        if call is not None:
            tool = tools.get((call["toolId"], call["version"]))
            if tool is None:
                errors.append(f"{identity}: araç {call['toolId']} katalogda yok")
            else:
                outside = sorted(set(call["arguments"]) - set(tool["fieldAllowlist"]))
                expects_invalid = item.get("expected_tool_result", {}).get("outcome") == "invalid"
                if outside and not expects_invalid:
                    errors.append(f"{identity}: izin listesi dışı argüman {outside}")
        if is_shipment:
            if call is None or call["toolId"] != SHIPMENT_TOOL:
                errors.append(f"{identity}: sevkiyat senaryosu {SHIPMENT_TOOL} çağrısı beklemiyor")
            if "fixture" not in item or "expected_tool_result" not in item:
                errors.append(f"{identity}: sevkiyat senaryosunda fixture veya beklenen sonuç yok")
            else:
                errors.extend(_check_shipment_arithmetic(identity, item))
        elif "fixture" in item or "expected_tool_result" in item:
            errors.append(f"{identity}: karar sorusunda sevkiyat alanı var")
    return _report(eval_set, errors, warnings)


def _check_shipment_arithmetic(identity: str, item: dict[str, Any]) -> list[str]:
    """The expected counts must follow from the synthetic fixture; the C# tool test runs the same scenarios."""
    result = item["expected_tool_result"]
    if result["outcome"] != "succeeded":
        return []
    status = item["expected_tool_call"]["arguments"].get("status")
    # AI-D35: draft or issued instructions not turned into definite shipment; cancelled and fully shipped do not count;
    # an unknown shipment state or the scan cap withholds the counts (fail-closed).
    rows = [row for row in item["fixture"]["instructions"] if row["company"] == "ayni"
            and row["status"] in ("Draft", "Issued") and status in (None, row["status"])]
    scanned = sum(row["count"] for row in rows)
    unknown = any(row["shipped"] == "bilinmiyor" for row in rows)
    complete = scanned <= 1000 and not unknown
    pending_rows = [row for row in rows if row["shipped"] in ("yok", "kismi")]
    draft = sum(row["count"] for row in pending_rows if row["status"] == "Draft")
    issued = sum(row["count"] for row in pending_rows if row["status"] == "Issued")
    fully = sum(row["count"] for row in rows if row["shipped"] == "tam")
    limit = item["expected_tool_call"]["arguments"].get("limit", 20)
    expected = {
        "pendingCount": draft + issued if complete else None,
        "draftCount": draft if complete else None,
        "issuedCount": issued if complete else None,
        "fullyShippedExcludedCount": fully if complete else None,
        "listedCount": min(limit, draft + issued),
        "complete": complete,
    }
    return [f"{identity}: {key} beklenen {value} ama {result.get(key)}" for key, value in expected.items()
            if result.get(key) != value]


def _report(eval_set: dict[str, Any], errors: list[str], warnings: list[str]) -> dict[str, Any]:
    items = eval_set.get("items", []) if isinstance(eval_set, dict) else []
    return {
        "format": "goymen.ai.eval-report.v1",
        "mode": "modelsiz",
        "model_runs": 0,
        "item_count": len(items),
        "category_counts": dict(sorted(Counter(item.get("category") for item in items).items())),
        "errors": errors,
        "warnings": warnings,
        "ok": not errors,
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--root", type=Path, default=REPOSITORY_ROOT)
    parser.add_argument("--model", help="bu dilimde desteklenmez (model, anahtar ve açma insan kapısıdır)")
    arguments = parser.parse_args(argv)
    if arguments.model is not None:
        print("HATA: model koşusu bu dilimde yok; Ai:Enabled=false, sağlayıcı ve anahtar insan kapısıdır (AI-D6/D7, AI-D24).",
              file=sys.stderr)
        return 2
    report = evaluate(arguments.root.resolve())
    print(json.dumps(report, ensure_ascii=False, indent=1))
    return 0 if report["ok"] else 1


if __name__ == "__main__":
    sys.exit(main())
