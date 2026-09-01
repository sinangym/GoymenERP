#!/usr/bin/env python3
"""Verify the real C-01 API/capability boundary before browser acceptance."""

from __future__ import annotations

import argparse
import json
import pathlib
import urllib.error
import urllib.request
from typing import Any


GDC = "10000000-0000-0000-0000-000000000001"
CAPABILITY = "cari.parties.list"


def request(base_url: str, path: str, subject: str, company_id: str | None) -> tuple[int, Any]:
    headers = {"X-Development-User": subject}
    if company_id is not None:
        headers["X-Company-Id"] = company_id
    probe = urllib.request.Request(base_url.rstrip("/") + path, headers=headers)
    try:
        with urllib.request.urlopen(probe, timeout=15) as response:
            body = response.read()
            return response.status, json.loads(body) if body else None
    except urllib.error.HTTPError as exception:
        body = exception.read()
        return exception.code, json.loads(body) if body else None


def require(condition: bool, message: str) -> None:
    if not condition:
        raise RuntimeError(message)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--base-url", required=True)
    parser.add_argument("--tested-commit", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()

    status, selected = request(
        args.base_url,
        "/api/v1/shell/capabilities",
        "group-bootstrap-admin",
        GDC,
    )
    require(status == 200, f"authorized capability status was {status}")
    require(selected.get("contractVersion") == "SHELL-CAPABILITY-V1", "capability contract drift")
    require(selected.get("capabilities") == [CAPABILITY], "selected-company capability drift")
    selected_generation = selected.get("generation")
    require(
        isinstance(selected_generation, str)
        and selected_generation.startswith("hmac-sha256:")
        and len(selected_generation) > len("hmac-sha256:"),
        "selected-company generation is missing",
    )

    status, group = request(
        args.base_url,
        "/api/v1/shell/capabilities",
        "group-bootstrap-admin",
        None,
    )
    require(status == 200 and group.get("capabilities") == [], "group context must not publish C-01")
    group_generation = group.get("generation")
    require(
        isinstance(group_generation, str)
        and group_generation.startswith("hmac-sha256:")
        and group_generation != selected_generation,
        "group generation must be valid and context-bound",
    )

    status, unauthorized_capabilities = request(
        args.base_url,
        "/api/v1/shell/capabilities",
        "company-a-user",
        GDC,
    )
    require(
        status == 200 and unauthorized_capabilities.get("capabilities") == [],
        "unauthorized actor must not receive C-01 capability",
    )
    unauthorized_generation = unauthorized_capabilities.get("generation")
    require(
        isinstance(unauthorized_generation, str)
        and unauthorized_generation.startswith("hmac-sha256:")
        and unauthorized_generation != selected_generation,
        "unauthorized generation must be valid and actor-bound",
    )

    status, page = request(
        args.base_url,
        "/api/v1/counterparties?includeCount=true&sort=code&direction=asc&pageSize=50",
        "group-bootstrap-admin",
        GDC,
    )
    require(status == 200, f"authorized C-01 list status was {status}")
    items = page.get("items")
    require(isinstance(items, list) and len(items) == 50, "authorized first page must contain 50 rows")
    require(page.get("totalCount") == 52, "authorized visible count must be 52")
    require(page.get("hasMore") is True and page.get("nextCursor"), "continuation cursor is missing")
    codes = [item.get("code") for item in items]
    require(codes == [f"CAR-{number:06d}" for number in range(1, 51)], "first page ordering drift")
    require("CAR-000054" not in json.dumps(page, ensure_ascii=False), "GSP-only record leaked")
    for item in items:
        accounts = item.get("companyAccounts")
        require(isinstance(accounts, list) and len(accounts) == 1, "selected account projection drift")
        require(accounts[0].get("companyId") == GDC, "foreign company account leaked")

    status, unauthorized_body = request(
        args.base_url,
        "/api/v1/counterparties",
        "company-a-user",
        GDC,
    )
    require(status == 403, f"unauthorized C-01 list status was {status}")
    require("CAR-000054" not in json.dumps(unauthorized_body, ensure_ascii=False), "403 body leaked GSP data")

    output = {
        "schemaVersion": 1,
        "suiteId": "C01-E2E-API-PREFLIGHT-V1",
        "testedCommit": args.tested_commit,
        "authorizedCompanyId": GDC,
        "capability": CAPABILITY,
        "selectedGenerationBound": True,
        "groupGenerationBound": True,
        "unauthorizedGenerationBound": True,
        "authorizedFirstPageCount": 50,
        "authorizedVisibleCount": 52,
        "groupCapabilityCount": 0,
        "unauthorizedCapabilityCount": 0,
        "unauthorizedListStatus": 403,
        "foreignRecordAbsent": True,
    }
    destination = pathlib.Path(args.output)
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text(
        json.dumps(output, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print("C-01 real API/capability preflight passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
