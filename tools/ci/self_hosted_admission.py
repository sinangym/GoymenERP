"""Pre-job admission for Sinan's private development runners; no network or secrets."""
from __future__ import annotations
import json
import os
from pathlib import Path
import sys

REPOSITORY = "sinangym/GoymenERP"
OWNER_ACTOR = "sinangym"
ALLOWED_EVENTS = {"push", "pull_request", "workflow_dispatch"}


def validate(environment: dict[str, str], event: dict) -> None:
    if environment.get("GITHUB_REPOSITORY") != REPOSITORY:
        raise ValueError("Repository is not admitted")
    if environment.get("GITHUB_ACTOR") != OWNER_ACTOR:
        raise ValueError("Only the configured repository owner may start local jobs")
    event_name = environment.get("GITHUB_EVENT_NAME")
    if event_name not in ALLOWED_EVENTS:
        raise ValueError("Event type is not admitted")
    repository = event.get("repository", {})
    if repository.get("full_name") != REPOSITORY or repository.get("private") is not True:
        raise ValueError("A private repository event is required")
    if event_name == "pull_request":
        pr = event.get("pull_request", {})
        for side in ("head", "base"):
            if pr.get(side, {}).get("repo", {}).get("full_name") != REPOSITORY:
                raise ValueError("Fork pull requests are not admitted")
    elif not environment.get("GITHUB_REF", "").startswith("refs/heads/"):
        raise ValueError("An internal branch is required")


def main() -> int:
    try:
        event_path = Path(os.environ["GITHUB_EVENT_PATH"])
        if event_path.stat().st_size > 2_000_000:
            raise ValueError("Event payload exceeds the local admission limit")
        event = json.loads(event_path.read_text(encoding="utf-8"))
        if not isinstance(event, dict):
            raise ValueError("Invalid event object")
        validate(dict(os.environ), event)
    except (KeyError, ValueError, OSError, TypeError, AttributeError):
        print("LOCAL_RUNNER_ADMISSION_DENIED", file=sys.stderr)
        return 1
    print("LOCAL_RUNNER_ADMISSION_OK: private repository, owner actor, trusted source")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
