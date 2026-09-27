#!/usr/bin/env python3
"""Contract of the selectable browser workflow (.github/workflows/browser-acceptance.yml).

The workflow reuses the C-01 disposable stack. Its artifacts are NOT C-01's, so they get their own
upload allowlist here; the privacy rules themselves are C-01's, loaded from
tools/validate_c01_e2e_contract.py unchanged (by path: the workflow runs tools with `python3 -I`,
which keeps the script directory off sys.path).

Stage 2: each suite runs under one capability profile, each with its own compose file:
- "c01-list-only": the canonical C-01 stack, validated by the canonical validator unchanged;
- "cari-staged": the same disposable envelope plus C01E2e__Profile=cari-list-detail;
- "normal": the same stack without the envelope, so the API serves its normal shell authority.
A suite compose model is mapped back to the canonical shape by exactly its profile's documented
difference and then checked by the canonical validator, so every canonical rule (Testing,
loopback-only ports, per-run disposable database, image digest, identities, labels) applies to
every profile; on top of that the API environment key set must be exact.
"""
from __future__ import annotations

import argparse
import importlib.util
import json
import pathlib
import re
import sys

_C01_PATH = pathlib.Path(__file__).resolve().with_name("validate_c01_e2e_contract.py")
_spec = importlib.util.spec_from_file_location("validate_c01_e2e_contract", _C01_PATH)
assert _spec is not None and _spec.loader is not None
c01 = importlib.util.module_from_spec(_spec)
sys.modules[_spec.name] = c01
_spec.loader.exec_module(c01)

ContractError = c01.ContractError
require = c01.require

SUITES = ("smoke", "c03", "org03", "u02", "s03", "mfg", "fin01", "s04", "acc01", "l04", "pur01", "r14", "c02")
# s03 writes since S03 (#327): V2A-02 attempts a POST (nothing is stored, but it is no read-only run).
READ_ONLY_SUITES = ("smoke", "c03")
_SUITE_PATTERN = re.compile(r"^[a-z0-9]+$")

PROFILES = ("c01-list-only", "cari-staged", "normal")
SUITE_PROFILES = {
    "smoke": "c01-list-only",
    "c03": "cari-staged",
    "org03": "normal",
    "u02": "normal",
    # S-03 v1 (#321): fail-closed evidence on the normal host (staged Sales screens, no grant).
    "s03": "normal",
    # MFG: the Manufacturing order screen, published on the normal host (MFG-UI-F; the staged profile of #323
    # retired).
    "mfg": "normal",
    # FIN-01: Finance's screens, published on the normal host (FIN-UI-1c; the staged profile of #332 retired).
    "fin01": "normal",
    # S-04 (SİPARİŞ, SALES-A1): the approval journey on the normal host over a gated synthetic draft.
    "s04": "normal",
    # ACC-01 (FİNANS): the Accounting screens are published on the normal host (ACC-V0c, after the acc01
    # acceptance on the staged profile), so the suite runs there and the acc-staged envelope is retired.
    "acc01": "normal",
    # CARI-NORMAL: the create/reopen journey runs on the NORMAL host, which is the whole point —
    # C-01's list-only envelope and c03's read-only multi-company detail journey are not acceptance for it.
    "c02": "normal",
    # L-04 (SİPARİŞ): Logistics is published after LOG-4b acceptance; the final journey runs on the normal host.
    "l04": "normal",
    # PUR-01 (SATINALMA): the published order list/detail and approval control run on the normal host.
    "pur01": "normal",
    # R14 (STOK): real opening-stock create/reopen against Product and Organization owner reads.
    "r14": "normal",
}
PROFILE_COMPOSE_FILES = {
    "c01-list-only": "infra/docker-compose.c01-e2e.yml",
    "cari-staged": "infra/docker-compose.browser-cari-staged.yml",
    "normal": "infra/docker-compose.browser-normal.yml",
}
PROFILE_SUITE_LABELS = {
    "c01-list-only": "C01-REAL-BROWSER-E2E-V1",
    "cari-staged": "BROWSER-SUITE-CARI-STAGED-V1",
    "normal": "BROWSER-SUITE-NORMAL-V1",
}
CANONICAL_SUITE_LABEL = PROFILE_SUITE_LABELS["c01-list-only"]
CARI_STAGED_PROFILE_KEY = "C01E2e__Profile"
CARI_STAGED_PROFILE_VALUE = "cari-list-detail"
# Each envelope profile's exact C01E2e__Profile value (C01E2eShellCapabilityActivation's profiles).
STAGED_PROFILE_VALUES = {
    "cari-staged": CARI_STAGED_PROFILE_VALUE,
}
# The disposable C-01 capability envelope, as the canonical compose file sets it on the API.
ENVELOPE_KEYS = (
    "C01E2e__EnableListCapability",
    "GOYMEN_ERP_C01_E2E_DISPOSABLE",
    "GOYMEN_ERP_C01_E2E_TESTED_COMMIT",
    "GOYMEN_ERP_C01_E2E_RUN_ID",
    "GOYMEN_ERP_C01_E2E_COMPOSE_PROJECT",
)
CANONICAL_API_ENVIRONMENT_KEYS = frozenset({
    "ASPNETCORE_ENVIRONMENT",
    "DB_PROVIDER",
    "DB_CONNECTION_STRING",
    "Counterparty__CursorSigningKey",
    *ENVELOPE_KEYS,
    "BootstrapIdentity__HeaderName",
    "BootstrapIdentity__Users__0__UserId",
    "BootstrapIdentity__Users__0__DisplayName",
    "BootstrapIdentity__Users__1__UserId",
    "BootstrapIdentity__Users__1__DisplayName",
})


# Stage 2 (a2): the only API environment keys a suite may add on top of its profile, through its own
# compose override file (infra/browser-suites/<suite>.override.yml). Closed lists: an unlisted key, or a
# listed key with a value outside its shape, fails; a suite without a list may not have an override.
# The VALUES are the suite owner's (their seed's deterministic identities, their synthetic actors).
SUITE_OVERRIDE_DIRECTORY = "infra/browser-suites"
# RFC 4122 on purpose: a version digit 1-8 and the RFC variant (8, 9, a or b). The issuer ids come from the
# suite seed's generated UUIDs (v4/v5 pass); a hand-typed value that is not a UUID is refused (#322 INFO-1).
_GUID = re.compile(r"^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")
# No leading zeros: .NET configuration binds "Users__02" and "Users__2" as two different list items, so a
# zero-padded index would silently add an actor outside the contiguity check (#322 ORG P3).
_ACTOR_KEY = re.compile(r"^BootstrapIdentity__Users__(0|[1-9][0-9]*)__(UserId|DisplayName)$")
MAX_SUITE_ACTORS = 8
SUITE_API_EXTENSIONS = {
    # U-02: the Product runtime's issuer binding, matching the suite seed's local user and
    # external identity (ÜRÜN, #319 P3). U-01-360 (#466, user decision 2026-09-27): one disposable
    # activator actor (u02-activator) with its Identity user and a company membership only; its Product
    # master Read + Activate grant is written by the gated seed writer, never by this override.
    "u02": {"issuer": True, "actors": True, "identity": True},
    # ORG-03: extra synthetic development actors after the canonical two (ORG, #319 P3-1), and
    # since the browser slice, their Identity users and grants — no seeded role reads work centres
    # WITHOUT locations, which is exactly the user this suite exists to walk.
    "org03": {"issuer": False, "actors": True, "identity": True},
    # S-03 (SİPARİŞ): the deployment's Product issuer binding (shared with u02, ÜRÜN's values), the
    # suite's actors and their Identity users and grants (coordinator decisions 1 and 3, a2 part 2).
    "s03": {"issuer": True, "actors": True, "identity": True},
    # MFG (ORG, #323 5735635840): Product's disposable issuer plus a recorder, a reader and (MFG-ORD)
    # a closer actor with their Identity users and grants. The issuer is not an actor and holds no grant.
    "mfg": {"issuer": True, "actors": True, "identity": True},
    # FIN-01 (FİNANS, #332): a proposer and an approver actor with their Identity users and grants.
    "fin01": {"issuer": False, "actors": True, "identity": True},
    # S-04 (SİPARİŞ): the existing Product issuer binding plus its Sales actors, Identity users and grants.
    "s04": {"issuer": True, "actors": True, "identity": True},
    # ACC-01 (FİNANS, ACC-V0b): a preparer and an approver actor with their Identity users and grants.
    "acc01": {"issuer": False, "actors": True, "identity": True},
    # L-04 (SİPARİŞ): the actors plus one non-actor Product issuer materialize the real Product fixture.
    "l04": {"issuer": True, "actors": True, "identity": True},
    # PUR-01 (SATINALMA): the existing Product issuer plus creator, approver and read-only actors with
    # disposable Identity users and grants. The issuer itself has no actor, role, assignment or grant.
    "pur01": {"issuer": True, "actors": True, "identity": True},
    # R14: Product issuer plus recorder, denied and Organization arranger actors in the disposable DB.
    "r14": {"issuer": True, "actors": True, "identity": True},
    # CARI-NORMAL: two actors holding the SAME two names in DIFFERENT companies, with their Identity
    # users and grants — the difference is the company, because that is the fence being measured.
    "c02": {"issuer": False, "actors": True, "identity": True},
}
ISSUER_KEYS = ("ProductRuntime__IssuerLocalUserId", "ProductRuntime__IssuerExternalIdentityId")

# Stage 2 (a2 part 2, coordinator decision 1): a suite's actors get their Identity user, external
# identity, roles and company-wide assignments through the real IdentityDataSeeder, appended to the
# built-in seed (src/backend/GoymenERP.Api/Seed/identity-seed.json) as IdentitySeed__* keys of the suite
# override. Closed: synthetic id prefixes, one user per suite actor (subject = its BootstrapIdentity id),
# SENTETIK names, BROWSER_<SUITE>_* role codes, permissions from the suite's closed list only, company
# scope on a seeded company only. The seeder still checks every permission against ErpPermissions.
IDENTITY_SEED_FILE = "src/backend/GoymenERP.Api/Seed/identity-seed.json"
ORGANIZATION_SEED_FILE = "src/backend/GoymenERP.Api/Seed/organization-seed.json"
BOOTSTRAP_ISSUER = "urn:goymen:development-bootstrap"
IDENTITY_VALID_FROM = "2026-01-01T00:00:00+00:00"
# The built-in seed's approver of every assignment (group-bootstrap-admin's Identity user).
IDENTITY_APPROVER = "20000000-0000-0000-0000-000000000003"
_IDENTITY_KEY = re.compile(r"^IdentitySeed__(Users|Roles|Assignments)__(0|[1-9][0-9]*)__(.+)$")
_IDENTITY_FIELDS = {
    "Users": re.compile(r"^(Id|DisplayName|ExternalIdentities__0__(Id|Issuer|Subject))$"),
    "Roles": re.compile(r"^(Id|Code|Name|IsSystem|Permissions__(0|[1-9][0-9]*))$"),
    "Assignments": re.compile(r"^(Id|UserId|RoleId|CompanyId|ValidFromUtc|ApprovedByUserId|Scopes__0__(Type|Id))$"),
}
_SYNTHETIC_ID_PREFIX = {"Users": "b20", "External": "b21", "Roles": "b30", "Assignments": "b40"}
SUITE_IDENTITY_PERMISSIONS = {
    # ORG-03. TWO names, and which actor holds which SUBSET is the evidence rather than a note —
    # this list is the suite's closed set, not a grant, so an actor holding both would leave the
    # suite green while proving nothing the slice claims. ORG's two actors:
    #   org03-work-centers-only  holds organization.work-centers.read ALONE. This is the user the
    #                            slice exists for: no link into ORG-01 exists for them, so the
    #                            sidebar entry is the only way in.
    #   org03-reader             holds both — the row's location link is offered.
    # Both company-wide, because a suite assignment MUST be: the single `require` in
    # validate_suite_identity below demands `Scopes__0__Type == "Company"` and the scope id to be
    # the assignment's own company. Cited by its condition rather than by a line number, which
    # this very comment shifted when it was added.
    # A third actor was designed and withdrawn for that reason: work centres company-wide with
    # locations scoped to ONE location would show every row while only some open, which is the
    # sharpest demonstration of what CanOpenLocationDetail (#377) decides. The PRODUCT supports it —
    # organization.locations.read is not in CompanyWideOnly — but this contract does not, and the
    # two are separate gates. Loosening the rule without first measuring why it exists is a change
    # that deserves its own slice, not a line in this one — and whoever writes that slice starts at
    # test_suite_fixtures' "scope narrower than the company" case, which sends Scopes__0__Type "Site"
    # and pins the refusal ON PURPOSE. So the rule is intentional, not incidental: the argument to
    # make is not "nobody generalised this" but "this was decided, and here is why the decision does
    # not cover a work-centre reader scoped to one location".
    # CARI-NORMAL. TWO names, and BOTH actors hold BOTH — which is the opposite of org03's design and
    # deliberate. org03 varies the permissions to show a capability boundary; c02 holds them CONSTANT
    # and varies the COMPANY, so that when the other-company actor is refused, permissions cannot be
    # the explanation. A closed list of two, therefore, with no per-actor subset to record.
    "c02": ("counterparty.parties.create", "counterparty.parties.read"),
    # ORG master edit (user decision 2026-09-27): a THIRD actor, org03-editor, holds the three reads its
    # screens need (work-centers, locations, sites) plus the three master-edit writes. The two readers above
    # keep their exact subsets, so the suite also shows a reader is offered no create/edit control.
    "org03": ("organization.work-centers.read", "organization.locations.read", "organization.sites.read",
              "organization.sites.write", "organization.locations.write", "organization.work-centers.write"),
    # u02: the issuer's Identity user holds nothing (the issuer-user rule). The disposable activator's
    # membership role carries only the baseline identity.profile.read, which no endpoint or shell
    # capability uses: it establishes the fresh company membership Product authorization requires and
    # opens nothing. Its Product authority is the seed's gated SYSTEM_ADMINISTRATOR-role grant (PRD-PERM-001 A).
    "u02": ("identity.profile.read",),
    # SALES-A1 (coordinator): the approval and the draft's party read, for the v2b positive path.
    "s03": ("sales.drafts.create", "sales.drafts.read", "product.operational-selection.read",
            "counterparty.parties.read", "sales.orders.approve"),
    # The recorder's Manufacturing plan/order permissions plus the existing owner permissions required
    # by its real plan-create screen and opening-stock fixture. The reader still holds only
    # manufacturing.orders.read and the closer only that plus manufacturing.orders.close.
    "mfg": (
        "manufacturing.plans.write",
        "manufacturing.plans.read",
        "manufacturing.plans.approve",
        "manufacturing.orders.create",
        "manufacturing.facts.record",
        "manufacturing.orders.read",
        "manufacturing.orders.close",
        "organization.work-centers.read",
        "inventory.labels.read",
        "inventory.balances.read",
        "inventory.opening-stock.record",
        "product.operational-selection.read",
    ),
    # The fourteen Finance names and the Cari owner's authorized party read. The list is the complete
    # union of the disposable proposer and approver grants in fin01.override.yml.
    "fin01": (
        "finance.bank-account.read",
        "finance.bank-account.propose",
        "finance.bank-account.approve",
        "finance.manual-receipt.draft.save",
        "finance.manual-receipt.draft.read",
        "finance.manual-receipt.amount.read",
        "counterparty.parties.read",
        "finance.manual-receipt.record",
        "accounting.periods.read",
        "finance.manual-receipt.reverse.propose",
        "finance.manual-receipt.reverse.approve",
        "finance.manual-bank-payment.draft.save",
        "finance.manual-bank-payment.draft.read",
        "finance.manual-bank-payment.approve",
        "finance.manual-bank-payment.record",
    ),
    # SALES-A1's approval axis: the preparer creates and reads, the approver approves and reads, the reader reads.
    # SALES-A2a: the preparer reaches the Cari owner's contact information through its OWN party read.
    # S05 adds the two separate price names to that same disposable preparer; no other actor gains them.
    # DEC-SAL-003 (user decision 2026-09-27): the disposable s04-planner alone adds the delivery-schedule write
    # name beside sales.drafts.read; no other s04 actor holds it.
    "s04": ("sales.drafts.create", "sales.drafts.read", "sales.orders.approve",
            "counterparty.parties.read", "counterparty.parties.update.basic",
            "product.operational-selection.read", "sales.orders.price.read", "sales.orders.price.write",
            "inventory.opening-stock.record", "inventory.labels.read", "inventory.balances.read",
            "inventory.physical-allocation.write", "sales.orders.delivery-schedule.write"),
    # The six Accounting names. The preparer holds .approve on purpose — Ö4 must refuse its OWN proposal
    # although the grant is there — and holds periods.read alone in the second company; the approver holds
    # no journal or queue name, so those routes stay closed to it.
    "acc01": ("accounting.periods.read", "accounting.periods.propose", "accounting.periods.approve",
              "accounting.periods.close", "accounting.journals.read", "accounting.posting-queues.read"),
    # LOG-4a's three Logistics names; the Sales names stay on the two Sales seed actors (the preparer holds none:
    # the picker reads the price-free Logistics approved-lines read under the create name, Sinan §4).
    # ... and the Cari party read the preparer needs to see the customer by name at all: the price-free
    # approved-lines read reaches Cari through the ACTOR's own grant (the s03/s04 precedent), so without it
    # the owner answers nothing and the picker is silently empty (ASSUMPTION-LOG4B-3).
    "l04": ("logistics.loading-instructions.create", "logistics.loading-instructions.read", "logistics.loads.record",
            "logistics.shipments.record", "logistics.deliveries.record",
            "sales.drafts.create", "sales.drafts.read", "sales.orders.approve", "counterparty.parties.read",
            "inventory.opening-stock.record", "inventory.labels.read", "inventory.balances.read",
            "inventory.physical-allocation.write", "product.operational-selection.read"),
    # PUR-01: the complete union of the disposable creator, approver and reader grants. The creator's
    # two owner-read names are the same intersection the real Purchasing create command already uses;
    # this closed list grants nothing outside the disposable normal profile.
    # PUR-HOST (#478, user decision #5831467690): the disposable receiver adds only goods-receipts.record, and a
    # separate stock reader adds only the two Inventory label reads that show the SAME received label.
    "pur01": ("purchasing.orders.read", "purchasing.orders.approve", "purchasing.orders.create",
              "counterparty.parties.read", "product.operational-selection.read",
              "purchasing.goods-receipts.record", "inventory.labels.read", "inventory.balances.read"),
    # R14: one Inventory recorder with opening and detail reads, one owner-read-only denied actor,
    # and one Organization location reader.
    # R19 (user decision 2026-09-27): the disposable r14-transformer adds only the split/merge command name
    # beside the two label reads; no other r14 actor holds it.
    # AT-037 (user decision 2026-09-27): the disposable r14-counter holds only stock-count.record and the
    # disposable r14-count-approver only stock-count.approve, each beside the two label reads (maker != checker).
    "r14": ("inventory.opening-stock.record", "product.operational-selection.read",
            "inventory.labels.read", "inventory.balances.read",
            "organization.locations.read", "inventory.label-transformation.record",
            "inventory.stock-count.record", "inventory.stock-count.approve"),
}


def _seed_file(relative: str, section: str) -> dict:
    document = json.loads((c01.REPOSITORY_ROOT / relative).read_text(encoding="utf-8"))
    require(isinstance(document, dict) and isinstance(document.get(section), dict), f"{relative} has no {section}")
    return document[section]


def _synthetic_id(value: object, kind: str, label: str) -> str:
    require(isinstance(value, str) and _ANY_GUID.fullmatch(value) is not None
            and value.startswith(_SYNTHETIC_ID_PREFIX[kind]),
            f"{label} must be a lowercase synthetic id starting {_SYNTHETIC_ID_PREFIX[kind]}")
    return value


def _indexed(entries: dict[int, dict[str, str]], start: int, label: str) -> list[dict[str, str]]:
    indices = sorted(entries)
    require(indices == list(range(start, start + len(indices))),
            f"suite {label} must continue the built-in seed contiguously from index {start}")
    return [entries[index] for index in indices]


def _issuer_user(suite: str, extra: dict, actor_ids: list[str], users: dict[str, str],
                 user_rows: dict[str, dict[str, str]]) -> str | None:
    """The issuer-user rule (coordinator decision, a narrow extension of D1): a suite that binds the Product
    issuer (issuer list + both ISSUER_KEYS) may seed at most ONE Identity user that is no actor: the
    issuer's own user, whose Id and external identity Id are the issuer keys, whose subject is
    '<suite>-issuer' (never a BootstrapIdentity user, so it cannot sign in) and which holds no role,
    assignment or permission. Returns its id, or None when the suite seeds no such user."""
    non_actor = [user_id for user_id, subject in users.items() if subject not in actor_ids]
    issuer_subject = f"{suite}-issuer"
    require(issuer_subject not in actor_ids, f"no BootstrapIdentity user may be {issuer_subject}")
    if not non_actor:
        return None
    # A second non-actor user fails the actor/user bijection in the caller.
    require(SUITE_API_EXTENSIONS[suite]["issuer"] and all(key in extra for key in ISSUER_KEYS),
            f"suite {suite}: an Identity user without an actor is only the bound Product issuer's")
    issuer_user = non_actor[0]
    row = user_rows[issuer_user]
    require(issuer_user == extra[ISSUER_KEYS[0]],
            "the issuer's Identity user Id must equal ProductRuntime__IssuerLocalUserId")
    require(row["ExternalIdentities__0__Id"] == extra[ISSUER_KEYS[1]],
            "the issuer's external identity Id must equal ProductRuntime__IssuerExternalIdentityId")
    require(users[issuer_user] == issuer_subject, f"the issuer's subject must be {issuer_subject}")
    return issuer_user


def validate_suite_identity(suite: str, extra: dict, actor_ids: list[str]) -> None:
    """The suite's IdentitySeed__* keys: exactly one synthetic Identity user per suite actor, with grants."""
    grouped: dict[str, dict[int, dict[str, str]]] = {"Users": {}, "Roles": {}, "Assignments": {}}
    for key, value in extra.items():
        match = _IDENTITY_KEY.fullmatch(key)
        if match is None:
            continue
        section, index, field = match.group(1), int(match.group(2)), match.group(3)
        require(_IDENTITY_FIELDS[section].fullmatch(field) is not None, f"IdentitySeed field is not allowed: {key}")
        grouped[section].setdefault(index, {})[field] = value
    if not any(grouped.values()):
        # A suite with an Identity list must give its actors their users; one without keeps #322's
        # actors-only shape, whose actors cannot sign in yet. org03 USED to be that example and is no
        # longer: it declared its grants and is `identity: True` above, so citing it here would hand the
        # next reader an example that the same file contradicts. No suite carries the actors-only shape
        # today; the rule stands for the next one that does.
        require(not actor_ids or not SUITE_API_EXTENSIONS[suite]["identity"],
                f"suite {suite} actors need their Identity users (IdentitySeed) to sign in")
        return
    builtin = _seed_file(IDENTITY_SEED_FILE, "IdentitySeed")
    companies = {company["Id"] for company in _seed_file(ORGANIZATION_SEED_FILE, "OrganizationSeed")["Companies"]}
    permitted = set(SUITE_IDENTITY_PERMISSIONS.get(suite, ()))
    role_prefix = f"BROWSER_{suite.upper()}_"
    ids: set[str] = set()

    def unique(value: str) -> str:
        require(value not in ids, f"synthetic identity id repeated: {value}")
        ids.add(value)
        return value

    users = {}
    user_rows = {}
    for position, user in enumerate(_indexed(grouped["Users"], len(builtin["Users"]), "Identity users")):
        label = f"suite Identity user {position}"
        require(set(user) == {"Id", "DisplayName", "ExternalIdentities__0__Id",
                              "ExternalIdentities__0__Issuer", "ExternalIdentities__0__Subject"},
                f"{label} needs Id, DisplayName and exactly one external identity")
        user_id = unique(_synthetic_id(user["Id"], "Users", f"{label} Id"))
        unique(_synthetic_id(user["ExternalIdentities__0__Id"], "External", f"{label} external identity Id"))
        require(user["DisplayName"].startswith("SENTETIK "), f"{label} must be labelled SENTETIK")
        require(user["ExternalIdentities__0__Issuer"] == BOOTSTRAP_ISSUER,
                f"{label} external identity must be the development bootstrap issuer")
        users[user_id] = user["ExternalIdentities__0__Subject"]
        user_rows[user_id] = user
    issuer_user = _issuer_user(suite, extra, actor_ids, users, user_rows)
    require(sorted(subject for user_id, subject in users.items() if user_id != issuer_user) == sorted(actor_ids),
            "every suite actor needs exactly one Identity user whose subject is its id, and nothing else")

    roles = {}
    for position, role in enumerate(_indexed(grouped["Roles"], len(builtin["Roles"]), "roles")):
        label = f"suite role {position}"
        permissions = [role[key] for key in sorted((key for key in role if key.startswith("Permissions__")),
                                                     key=lambda key: int(key.rsplit("__", 1)[1]))]
        require({"Id", "Code", "Name", "IsSystem"} <= set(role) and permissions,
                f"{label} needs Id, Code, Name, IsSystem and at least one permission")
        indices = sorted(int(key.rsplit("__", 1)[1]) for key in role if key.startswith("Permissions__"))
        require(indices == list(range(len(indices))), f"{label} permissions must be indexed contiguously from 0")
        role_id = unique(_synthetic_id(role["Id"], "Roles", f"{label} Id"))
        role_code = role["Code"]
        suite_role = re.fullmatch(rf"{role_prefix}[A-Z0-9_]{{1,40}}", role_code) is not None
        # S-04 exercises the accepted Sales Support persona through the disposable Identity seed.
        # Every other role remains synthetic and suite-prefixed; all existing name, system,
        # permission, scope and assignment checks below still apply to this one exception.
        require(suite_role or (suite == "s04" and role_code == "SALES_SUPPORT"),
                f"{label} code must start {role_prefix}")
        require(role["Name"].startswith("SENTETIK "), f"{label} must be labelled SENTETIK")
        require(role["IsSystem"] == "false", f"{label} must not be a system role")
        require(len(set(permissions)) == len(permissions), f"{label} repeats a permission")
        outside = sorted(set(permissions) - permitted)
        require(not outside, f"{label} grants permissions outside suite {suite}'s closed list: {outside}")
        roles[role_id] = role

    assigned = set()
    for position, assignment in enumerate(_indexed(grouped["Assignments"], len(builtin["Assignments"]),
                                                   "assignments")):
        label = f"suite assignment {position}"
        require(set(assignment) - {"ApprovedByUserId"} == {"Id", "UserId", "RoleId", "CompanyId", "ValidFromUtc",
                                                          "Scopes__0__Type", "Scopes__0__Id"},
                f"{label} needs Id, UserId, RoleId, CompanyId, ValidFromUtc and one company scope")
        unique(_synthetic_id(assignment["Id"], "Assignments", f"{label} Id"))
        require(assignment["UserId"] in users and assignment["UserId"] != issuer_user,
                f"{label} must assign a suite actor's Identity user (the Product issuer holds no grant)")
        require(assignment["RoleId"] in roles, f"{label} must assign a suite role")
        require(assignment["CompanyId"] in companies, f"{label} company must be a seeded company")
        require(assignment["ValidFromUtc"] == IDENTITY_VALID_FROM, f"{label} must be valid from {IDENTITY_VALID_FROM}")
        require(assignment.get("ApprovedByUserId", IDENTITY_APPROVER) == IDENTITY_APPROVER,
                f"{label} approver must be the built-in seed's approver")
        require(assignment["Scopes__0__Type"] == "Company" and assignment["Scopes__0__Id"] == assignment["CompanyId"],
                f"{label} must be company-wide on its own company")
        assigned.add(assignment["UserId"])
    require(set(users) - {issuer_user} <= assigned, "every suite actor's Identity user needs at least one assignment")


# Stage 2 (a2 part 2): suites whose owner seeds data through the fixture runner
# (tests/GoymenERP.BrowserSuiteFixtureTool, class in Suites/<Suite>/), each with the closed list of the
# output keys exported to the browser suite as BROWSER_E2E_<SUITE>_SEED. Values are GUIDs, actor ids
# or the suite's explicitly approved closed scalar values. A read-only suite cannot have a fixture:
# its post-browser SQL check compares against the
# canonical baseline, which a seed would move.
FIXTURE_TOOL_SUITES_DIRECTORY = "tests/GoymenERP.BrowserSuiteFixtureTool/Suites"
SUITE_FIXTURE_OUTPUTS: dict[str, frozenset[str]] = {
    # ÜRÜN's Suites/U02 seed: the company, gated code group, fixed synthetic technical family and actors.
    "u02": frozenset({"companyId", "codeGroupId", "technicalFamilyId", "authorizedActor", "unauthorizedActor",
                      "activatorActor"}),
    # ORG's Suites/MFG seed (#323 5735635840): the actors, the company, the seeded work centre (read
    # through the real Organization API) and the plan -> approval -> order it writes through the real API.
    # MFG-ORD adds the closer actor; the list and the close need no further seeded row.
    "mfg": frozenset({
        "recorderActor", "readerActor", "closerActor", "companyId", "workCenterId", "orderId", "rollId",
        "issueRollId", "issueRollCode", "replayRollId", "replayRollCode", "conflictRollId",
        "conflictRollCode", "listRollId", "listRollCode", "closeRollId", "closeRollCode",
        "planCreateRollId", "planCreateRollCode", "planReplayRollId", "planReplayRollCode",
        "planRefusalRollId", "planRefusalRollCode", "outputUiRollId", "outputUiRollCode",
        "outputProductId", "outputProductVersion",
        "outputRequiredDocuments", "outputOwnerKind", "outputOwnerId", "outputCustodianKind",
        "outputCustodianId", "outputLocationId", "outputInitialBlock",
    }),
    # FİNANS's Suites/Fin01 seed (#332): the company, the customer opened through the real Cari API and
    # its company account, and the two suite actors. No SQL, no gated writer, no Finance row.
    "fin01": frozenset({"companyId", "partyId", "companyAccountId", "proposerActor", "approverActor"}),
    # SİPARİŞ's Suites/S04 seed: the three actors, the company and the one Preparing draft written through
    # the gated Sales draft writer (C01E2eSalesDraftFixtureWriter).
    # SALES-A2a adds the second draft the delivery-reference path uses, the real party and its company
    # account, and the address and contact the owner answered. Allocation acceptance adds the exact Sales
    # line and Inventory label ids plus the label's current positive version. Other texts stay deterministic
    # constants in the spec instead of travelling as seed outputs.
    "s04": frozenset({"preparerActor", "approverActor", "readerActor", "companyId", "draftId",
                      "deliveryDraftId", "realPartyId", "realCompanyAccountId", "deliveryAddressId",
                      "deliveryContactId", "supportCreatorActor", "ordinaryCreatorActor", "supportDraftId",
                      "ordinaryApproveDraftId", "ordinaryRejectDraftId", "withdrawalDraftId",
                      "productId", "productVersion", "productUnitId", "productUnitProfileId",
                      "productUnitProfileVersion", "productCode", "productName",
                      "allocationLineId", "allocationLabelId", "allocationLabelVersion",
                      "plannerActor", "scheduleDraftId"}),
    # FİNANS's Suites/ACC01 seed: the two actors and both companies. Periods are proposed, approved and closed
    # through the real API; since ACC-REV-UI phase 1 the seed also installs one fixed SENTETIK posted journal
    # (gated fixture writer, owner posting path) so ACC01-09 can open M-04 detail and read M-06. Its id is read
    # from the real API, so the output keys stay these four. The second company only shows GDC's rows never appear.
    "acc01": frozenset({"companyId", "companyBId", "preparerActor", "approverActor"}),
    # SİPARİŞ's Suites/L04 seed: the three actors, both companies, the customer (by id and name), the approved draft
    # line, the instruction created and issued through the real API, its open and gated-blocked lines,
    # and the shipment allocation/label created for the same disposable journey.
    # The customer's NAME is not exported: seed_export accepts only a GUID or an actor id, and the spec
    # asserts the deterministic SENTETIK label as a constant instead.
    "l04": frozenset({"preparerActor", "loaderActor", "readerActor", "companyId", "otherCompanyId", "customerPartyId",
                      "companyAccountId", "draftId", "approvedLineId", "instructionId", "blockedLineId",
                      "openLineId", "shipmentAllocationId", "shipmentLabelId"}),
    # SATINALMA's Suites/PUR01 seed: the existing synthetic Draft approval axis plus the separate creator,
    # the real Cari-owned active Supplier account and Product's exact gated purchase-candidate tuple.
    "pur01": frozenset({"creatorActor", "approverActor", "readerActor", "receiverActor", "stockReaderActor",
                        "companyId", "otherCompanyId",
                        "orderId", "lineId", "supplierPartyId", "companyAccountId", "productId",
                        "productVersion", "productUnitId", "productUnitProfileId",
                        "productUnitProfileVersion", "productCode", "productName"}),
    # STOK's Suites/R14 seed: three actors, company fence, active Organization location and Product's
    # exact gated stock-candidate tuple. The browser creates the Inventory record itself.
    "r14": frozenset({"recorderActor", "deniedActor", "arrangerActor", "transformerActor",
                      "counterActor", "countApproverActor", "companyId",
                      "otherCompanyId",
                      "locationId", "productId", "productVersion", "productUnitId",
                      "productUnitProfileId", "productUnitProfileVersion", "productCode", "productName"}),
}
_OUTPUT_KEY = re.compile(r"^[a-z][A-Za-z0-9]{0,39}$")
# Any non-nil lowercase GUID: seeded reference ids (companies, work centres) need not be RFC 4122.
_ANY_GUID = re.compile(r"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$")
CANONICAL_ACTORS = ("group-bootstrap-admin", "company-a-user")


def has_fixture(suite: str) -> bool:
    profile_of(suite)
    present = suite in SUITE_FIXTURE_OUTPUTS
    require(not present or suite not in READ_ONLY_SUITES,
            f"read-only suite {suite} cannot seed data through a fixture")
    return present


def seed_export(suite: str, seed: object) -> str:
    """The GITHUB_ENV line that hands the seed outputs to the browser suite, after the closed-list check."""
    require(has_fixture(suite), f"suite {suite} has no fixture")
    require(isinstance(seed, dict), "seed outputs must be an object")
    expected = SUITE_FIXTURE_OUTPUTS[suite]
    require(set(seed) == set(expected),
            f"seed output keys are not the closed list: missing {sorted(expected - set(seed))}, "
            f"extra {sorted(set(seed) - expected)}")
    mfg_actor_keys = frozenset({"recorderActor", "readerActor", "closerActor"})
    mfg_exact_text = {
        "issueRollCode": "SYN-MFG-ROLL-2",
        "replayRollCode": "SYN-MFG-ROLL-3",
        "conflictRollCode": "SYN-MFG-ROLL-4",
        "listRollCode": "SYN-MFG-ROLL-5",
        "closeRollCode": "SYN-MFG-ROLL-6",
        "planCreateRollCode": "SYN-MFG-ROLL-7",
        "planReplayRollCode": "SYN-MFG-ROLL-8",
        "planRefusalRollCode": "SYN-MFG-ROLL-9",
        "outputUiRollCode": "SYN-MFG-ROLL-10",
        "outputRequiredDocuments": "[]",
        "outputOwnerKind": "Company",
        "outputCustodianKind": "Company",
        "outputInitialBlock": "Free",
    }
    mfg_guid_keys = expected - mfg_actor_keys - frozenset(mfg_exact_text) - {"outputProductVersion"}
    for key, value in seed.items():
        require(isinstance(value, str), f"seed output {key} must be a string")
        is_guid = _ANY_GUID.fullmatch(value) is not None and value != "00000000-0000-0000-0000-000000000000"
        is_actor = value in CANONICAL_ACTORS or re.fullmatch(rf"{suite}-[a-z0-9-]{{1,48}}", value) is not None
        if suite == "mfg":
            if key in mfg_actor_keys:
                require(is_actor, f"mfg seed output {key} must be a current-run actor id")
            elif key in mfg_guid_keys:
                require(is_guid, f"mfg seed output {key} must be a canonical nonzero lowercase GUID")
            elif key == "outputProductVersion":
                require(re.fullmatch(r"[1-9][0-9]*", value) is not None,
                        "mfg seed output outputProductVersion must be a positive integer")
            else:
                require(mfg_exact_text.get(key) == value,
                        f"mfg seed output {key} must match its exact closed-contract value")
            continue
        is_s04_version = suite == "s04" and key in {"productVersion", "productUnitProfileVersion",
                                                     "allocationLabelVersion"} \
            and re.fullmatch(r"[1-9][0-9]*", value) is not None
        is_s04_code = suite == "s04" and key == "productCode" \
            and re.fullmatch(r"SYNS04-[1-9][0-9]*", value) is not None
        is_s04_name = suite == "s04" and key == "productName" \
            and value == "SENTETIK browser-suite:s04 sales candidate"
        is_pur01_version = suite == "pur01" and key in {"productVersion", "productUnitProfileVersion"} \
            and re.fullmatch(r"[1-9][0-9]*", value) is not None
        is_pur01_code = suite == "pur01" and key == "productCode" \
            and re.fullmatch(r"SYNPUR01-[1-9][0-9]*", value) is not None
        is_pur01_name = suite == "pur01" and key == "productName" \
            and value == "SENTETIK browser-suite:pur01 purchase candidate"
        is_r14_version = suite == "r14" and key in {"productVersion", "productUnitProfileVersion"} \
            and re.fullmatch(r"[1-9][0-9]*", value) is not None
        is_r14_code = suite == "r14" and key == "productCode" \
            and re.fullmatch(r"SYNR14-[1-9][0-9]*", value) is not None
        is_r14_name = suite == "r14" and key == "productName" \
            and value == "SENTETIK browser-suite:r14 stock candidate"
        require(is_guid or is_actor or is_s04_version or is_s04_code or is_s04_name
                or is_pur01_version or is_pur01_code or is_pur01_name
                or is_r14_version or is_r14_code or is_r14_name,
                f"seed output {key} must be a GUID or an actor id or an approved suite Product value")
    if suite == "mfg":
        require(seed["outputOwnerId"] == seed["companyId"],
                "mfg output owner must be the seeded company")
        require(seed["outputCustodianId"] == seed["companyId"],
                "mfg output custodian must be the seeded company")
    compact = json.dumps(seed, sort_keys=True, separators=(",", ":"))
    return f"BROWSER_E2E_{suite.upper()}_SEED={compact}"


def api_environment(model: dict, suite: str, expected_commit: str, expected_project: str) -> dict[str, str]:
    """The API container's environment for the fixture runner's in-process host, after the full compose check."""
    validate_suite_compose(model, suite, expected_commit, expected_project)
    environment = model["services"]["api"]["environment"]
    require(all(isinstance(value, str) for value in environment.values()), "API environment values must be strings")
    return dict(sorted(environment.items()))


def suite_override_file(suite: str) -> str | None:
    """The suite's compose override file, if it has one; only suites with a closed extension list may."""
    profile_of(suite)
    relative = f"{SUITE_OVERRIDE_DIRECTORY}/{suite}.override.yml"
    exists = (c01.REPOSITORY_ROOT / relative).is_file()
    require(not exists or suite in SUITE_API_EXTENSIONS,
            f"suite {suite} has an override file but no closed extension list")
    return relative if exists else None


def validate_suite_extensions(suite: str, extra: dict) -> None:
    """The keys a suite adds are exactly allowed by its closed list, with values of the right shape."""
    if not extra:
        return
    allowed = SUITE_API_EXTENSIONS.get(suite)
    require(allowed is not None, f"suite {suite} may not extend the API environment: {sorted(extra)}")
    actors: dict[int, dict[str, str]] = {}
    for key, value in extra.items():
        require(isinstance(value, str), f"suite extension {key} must be a string")
        if key in ISSUER_KEYS:
            require(allowed["issuer"], f"suite {suite} may not set {key}")
            require(_GUID.fullmatch(value) is not None, f"{key} must be a lowercase non-nil GUID")
            continue
        if _IDENTITY_KEY.fullmatch(key) is not None:
            require(allowed["identity"], f"suite {suite} may not seed Identity users")
            continue
        match = _ACTOR_KEY.fullmatch(key)
        require(match is not None, f"suite {suite} extension key is not allowed: {key}")
        require(allowed["actors"], f"suite {suite} may not add development actors")
        actors.setdefault(int(match.group(1)), {})[match.group(2)] = value
    if allowed["issuer"]:
        present = [key for key in ISSUER_KEYS if key in extra]
        require(len(present) in (0, len(ISSUER_KEYS)), "the Product issuer binding needs both keys or neither")
        if present:
            require(extra[ISSUER_KEYS[0]] != extra[ISSUER_KEYS[1]], "the issuer's local and external ids must differ")
    actor_ids: list[str] = []
    if actors:
        indices = sorted(actors)
        require(indices == list(range(2, 2 + len(indices))),
                "suite actors must continue the canonical two contiguously from index 2")
        require(len(indices) <= MAX_SUITE_ACTORS, f"at most {MAX_SUITE_ACTORS} suite actors")
        ids = set()
        for index in indices:
            actor = actors[index]
            require(set(actor) == {"UserId", "DisplayName"}, f"suite actor {index} needs UserId and DisplayName")
            require(re.fullmatch(rf"{suite}-[a-z0-9-]{{1,48}}", actor["UserId"]) is not None,
                    f"suite actor {index} id must be '{suite}-' followed by lowercase letters, digits or '-'")
            require(actor["DisplayName"].startswith("SENTETIK "), f"suite actor {index} must be labelled SENTETIK")
            require(actor["UserId"] not in ids, f"suite actor id repeated: {actor['UserId']}")
            ids.add(actor["UserId"])
        actor_ids = sorted(ids)
    validate_suite_identity(suite, extra, actor_ids)


def profile_of(suite: str) -> str:
    require(isinstance(suite, str) and _SUITE_PATTERN.fullmatch(suite) is not None, "suite name is not safe")
    require(suite in SUITES, f"unknown browser suite: {suite}")
    profile = SUITE_PROFILES.get(suite)
    require(profile in PROFILES, f"browser suite {suite} has no known capability profile")
    return profile


def _relabel(value: object, source: str, target: str) -> object:
    if isinstance(value, dict):
        return {key: _relabel(item, source, target) for key, item in value.items()}
    if isinstance(value, list):
        return [_relabel(item, source, target) for item in value]
    if value == source:
        return target
    return value


def validate_suite_compose(model: dict, suite: str, expected_commit: str, expected_project: str) -> None:
    """The suite's compose model is its profile's exact difference from the canonical C-01 model."""
    profile = profile_of(suite)
    require(isinstance(model, dict), "compose model must be an object")
    label = PROFILE_SUITE_LABELS[profile]
    services = model.get("services")
    require(isinstance(services, dict) and "api" in services, "compose model has no api service")
    for name, service in services.items():
        require(isinstance(service, dict), f"{name} service must be an object")
        require(service.get("labels", {}).get("com.goymen.e2e.suite") == label, f"{name} suite label is not the {profile} label")
    environment = services["api"].get("environment")
    require(isinstance(environment, dict), "API environment must be an object")
    canonical_environment = dict(environment)
    if profile == "c01-list-only":
        expected_keys = CANONICAL_API_ENVIRONMENT_KEYS
    elif profile in STAGED_PROFILE_VALUES:
        require(environment.get(CARI_STAGED_PROFILE_KEY) == STAGED_PROFILE_VALUES[profile],
                f"{profile} API must carry the {STAGED_PROFILE_VALUES[profile]} profile")
        expected_keys = CANONICAL_API_ENVIRONMENT_KEYS | {CARI_STAGED_PROFILE_KEY}
        del canonical_environment[CARI_STAGED_PROFILE_KEY]
    else:
        for key in environment:
            require(not key.startswith("C01E2e__") and not key.startswith("GOYMEN_ERP_C01_E2E_"),
                    f"normal profile API must not carry the disposable capability envelope ({key})")
        expected_keys = CANONICAL_API_ENVIRONMENT_KEYS - set(ENVELOPE_KEYS)
        # Only to let the canonical rules run: the envelope values they expect, never sent to the API.
        canonical_environment.update({
            "C01E2e__EnableListCapability": "true",
            "GOYMEN_ERP_C01_E2E_DISPOSABLE": "CONFIRMED",
            "GOYMEN_ERP_C01_E2E_TESTED_COMMIT": expected_commit,
            "GOYMEN_ERP_C01_E2E_RUN_ID": expected_project.split("-")[-2] if "-" in expected_project else "",
            "GOYMEN_ERP_C01_E2E_COMPOSE_PROJECT": expected_project,
        })
    extra = {key: environment[key] for key in set(environment) - set(expected_keys)}
    missing = sorted(set(expected_keys) - set(environment))
    unlisted = sorted(key for key in extra
                      if key not in ISSUER_KEYS and _ACTOR_KEY.fullmatch(key) is None
                      and _IDENTITY_KEY.fullmatch(key) is None)
    require(not missing and not unlisted,
            f"{profile} API environment key set is not exact: missing {missing}, extra {unlisted}")
    validate_suite_extensions(suite, extra)
    for key in extra:
        canonical_environment.pop(key, None)
    canonical = _relabel(model, label, CANONICAL_SUITE_LABEL)
    canonical["services"]["api"]["environment"] = canonical_environment
    c01.validate_compose(canonical, expected_commit, expected_project)


def upload_allowlist(suite: str) -> tuple[str, ...]:
    """The only files the workflow may upload for one suite, relative to the artifact root."""
    require(isinstance(suite, str) and _SUITE_PATTERN.fullmatch(suite) is not None, "suite name is not safe")
    require(suite in SUITES, f"unknown browser suite: {suite}")
    return (
        f"browser-{suite}-junit.xml",
        "browser-fixture-evidence.json",
        "browser-suite-fixture-evidence.json",
        "browser-suite-seed.json",
        "browser-post-browser-sql-evidence.json",
        "container-logs/compose-ps.txt",
    )


def upload_paths(root: str | pathlib.Path, suite: str) -> list[pathlib.Path]:
    artifact_root = pathlib.Path(root).resolve()
    require(artifact_root.is_dir(), "artifact root is missing")
    selected: list[pathlib.Path] = []
    for relative in upload_allowlist(suite):
        candidate = artifact_root / relative
        if not candidate.exists():
            continue
        require(candidate.is_file(), f"artifact allowlist entry is not a file: {relative}")
        require(not candidate.is_symlink(), f"artifact allowlist entry is a symlink: {relative}")
        require(candidate.resolve().is_relative_to(artifact_root), f"artifact allowlist entry escapes root: {relative}")
        selected.append(candidate)
    require(selected, "artifact upload allowlist selected no files")
    return selected


def validate_privacy(root: str | pathlib.Path, suite: str, forbidden_values: list[str] | tuple[str, ...] = ()) -> None:
    """Every file that would be uploaded is free of C-01's forbidden patterns and of the run's secrets."""
    artifact_root = pathlib.Path(root).resolve()
    selected = upload_paths(artifact_root, suite)
    needles: list[bytes] = []
    for index, value in enumerate(forbidden_values):
        require(isinstance(value, str) and len(value.encode("utf-8")) >= 8,
                f"runtime forbidden value {index} is missing or too short")
        needles.append(value.encode("utf-8"))
    for path in selected:
        content = path.read_bytes()
        relative = path.relative_to(artifact_root).as_posix()
        for label, pattern in c01.ARTIFACT_FORBIDDEN_PATTERNS:
            require(pattern.search(content) is None, f"artifact privacy violation ({label}): {relative}")
        for index, needle in enumerate(needles):
            require(needle not in content, f"artifact privacy violation (runtime secret {index}): {relative}")


def parser() -> argparse.ArgumentParser:
    result = argparse.ArgumentParser()
    commands = result.add_subparsers(dest="command", required=True)
    privacy = commands.add_parser("privacy")
    privacy.add_argument("--root", required=True)
    privacy.add_argument("--suite", required=True)
    privacy.add_argument("--forbidden-value", action="append", default=[])
    profile = commands.add_parser("profile")
    profile.add_argument("--suite", required=True)
    compose = commands.add_parser("compose")
    compose.add_argument("--suite", required=True)
    compose.add_argument("--expected-commit", required=True)
    compose.add_argument("--expected-project", required=True)
    environment = commands.add_parser("api-environment")
    environment.add_argument("--suite", required=True)
    environment.add_argument("--expected-commit", required=True)
    environment.add_argument("--expected-project", required=True)
    environment.add_argument("--out", required=True)
    export = commands.add_parser("seed-export")
    export.add_argument("--suite", required=True)
    export.add_argument("--seed", required=True)
    return result


def main(argv: list[str] | None = None) -> int:
    arguments = parser().parse_args(argv)
    try:
        if arguments.command == "privacy":
            validate_privacy(arguments.root, arguments.suite, arguments.forbidden_value)
        elif arguments.command == "profile":
            selected = profile_of(arguments.suite)
            # GITHUB_OUTPUT lines for the workflow.
            print(f"profile={selected}")
            print(f"compose_file={PROFILE_COMPOSE_FILES[selected]}")
            print(f"override_file={suite_override_file(arguments.suite) or ''}")
            print(f"fixture={'true' if has_fixture(arguments.suite) else 'false'}")
            return 0
        elif arguments.command == "api-environment":
            try:
                model = json.load(sys.stdin)
            except json.JSONDecodeError as error:
                raise ContractError("compose model is not JSON") from error
            environment = api_environment(model, arguments.suite, arguments.expected_commit, arguments.expected_project)
            output = pathlib.Path(arguments.out)
            require(output.suffix == ".json", "the API environment file must be .json")
            require(not output.resolve().is_relative_to((c01.REPOSITORY_ROOT / "artifacts").resolve()),
                    "the API environment holds the run's secrets and must stay outside artifacts/")
            output.write_text(json.dumps(environment, indent=2) + "\n", encoding="utf-8")
            return 0
        elif arguments.command == "seed-export":
            try:
                seed = json.loads(pathlib.Path(arguments.seed).read_text(encoding="utf-8"))
            except (OSError, json.JSONDecodeError) as error:
                raise ContractError("seed outputs are unreadable") from error
            # GITHUB_ENV line only; the values are GUIDs and actor ids.
            print(seed_export(arguments.suite, seed))
            return 0
        elif arguments.command == "compose":
            try:
                model = json.load(sys.stdin)
            except json.JSONDecodeError as error:
                raise ContractError("compose model is not JSON") from error
            validate_suite_compose(model, arguments.suite, arguments.expected_commit, arguments.expected_project)
    except ContractError as error:
        print(f"Browser acceptance contract validation failed: {error}", file=sys.stderr)
        return 1
    print("Browser acceptance contract validation passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
