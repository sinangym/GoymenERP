---
name: decision-docs-validation
description: Use when writing or changing anything under docs/decisions/ (a new SINAN_KARARLARI_*.md decision file, KARAR_SICILI.md/.csv, ACIK_KARARLAR.md), docs/backlog/ or .github/ — gives the Sinan decision file structure, the registry MD/CSV parity and hash re-pin procedure, and the exact local light checks (validate_delivery_backlog, delivery_backlog_validator, dotnet_bootstrap_integrity) whose command, exit code and source commit must be recorded in the PR before merge.
---

# Decision docs and their validators

AGENTS.md (22 Oct rule): for candidates that change `docs/decisions/**`, `docs/backlog/**` or `.github/**`, run
the light checks locally before merge and write **command, exit code and source commit** into the PR. Renew the
evidence whenever the source changes. The registry hash is updated **only** from the output of
`tests/delivery_backlog_validator/recompute_delivery_backlog_provenance.py`, after the approved source change has
been reviewed.

## 1. A new Sinan decision file

Name: `docs/decisions/SINAN_KARARLARI_<YYYY-MM-DD>[_<ALAN>][_EK<n>].md` (e.g. `…_2026-11-03_SATIS_EK37.md`,
`…_2026-11-02_ALIS_EK9.md`, `…_2026-11-03_MENU.md`). Next EK number = highest existing for that area + 1
(`ls docs/decisions | grep _SATIS_EK | sort -V | tail -1`). Decision ids continue the area series
(`SAT-D…`, `ALS-D…`, `FNS-D…`, `URT-D…`, `KLN-D…`, `AI-D…`); check the last id in the previous EK and in open PRs.

Structure (copy EK37):
1. `# Sinan Kararları — <date> (<topics>)`
2. Intro: "Bu belge önceki karar dosyalarını silmez; <ids> ekler." + which earlier ids stay valid.
3. `**Kaynak:**` who answered, when, where (session id or #66 comment link); free-text answers quoted verbatim;
   "(Önerilen)" marks are the asker's; every option is listed **with its description**.
4. `## Soru ve cevaplar` table: `# | Soru | Cevap | Seçilen seçeneğin açıklaması` (unchosen options in the cell).
5. `## Kararlar` table: `No | Konu | Karar | Etkilenen yer`. Mark reversible implementation details as
   "Uygulama ayrıntısı (geri alınabilir)".
6. `## Sınırlar`: what is not decided (→ `NEEDS-SINAN`), and the sentence that human gates (deployment,
   environment, secret, Settings activation, grant/rol/Critical, production, `OPEN-*` closing,
   `.claude/settings.json`) are unchanged.

Never write a decision Sinan did not give; an agent proposal stays `ASSUMPTION-*` (only for reversible technical/UX
points) or `NEEDS-SINAN`. Money/stock/production/formula/permission/security/migration/retention never by assumption.

## 2. KARAR_SICILI changes (rare; DEC-* rows)

`docs/decisions/KARAR_SICILI.md` table and `docs/decisions/KARAR_SICILI.csv` (UTF-8 with BOM, header
`DecisionId,Alan,Karar,Durum,Oncelik,Kaynak,Sahip,KabulKaniti`) must have identical rows in identical order. After
the reviewed edit:
```bash
python3 -I -S -B tests/delivery_backlog_validator/recompute_delivery_backlog_provenance.py > /tmp/provenance.json; echo "exit=$?"
```
Copy `decisionRegistry.canonicalRowsSha256` → `EXPECTED_DECISION_ROWS_SHA256` in `tools/validate_delivery_backlog.py`
and the matching literal in `tests/delivery_backlog_validator/test_decision_round_provenance.py`;
`decisionRegistry.orderedIdsSha256` → `EXPECTED_DECISION_ORDER_SHA256` (+ test) only if ids were added/reordered;
counts (`EXPECTED_DECISION_COUNT`, `…LOCKED…`, `…TEMPORARY…`) if they moved. Example: #863 `95c7d45` (4 files).

## 3. Light checks (run from the repository root; all must exit 0)

```bash
git rev-parse HEAD
python3 -I -S -B tools/validate_delivery_backlog.py; echo "exit=$?"
python3 -I -S -B -m unittest discover -s tests/delivery_backlog_validator -p "test_*.py"; echo "exit=$?"
python3 -I -S -B tools/validate_dotnet_bootstrap_integrity.py; echo "exit=$?"
python3 -I -S -B -m unittest discover -s tests/dotnet_bootstrap_integrity -p "test_*.py"; echo "exit=$?"
git diff --check origin/main...HEAD; echo "exit=$?"
```
(Verified on main `d8a86e4`: 64 + 56 tests OK, all exit 0, a few seconds.) Paste into the PR as:
`Yerel hafif kontroller (kaynak <sha>): validate_delivery_backlog exit 0 · delivery_backlog_validator 64 OK exit 0 · validate_dotnet_bootstrap_integrity exit 0 · dotnet_bootstrap_integrity 56 OK exit 0 · git diff --check exit 0`.

For `docs/backlog/task_manifest.json` also: `python3 -I -S -B tools/validate_task_manifest.py` and
`python3 -I -S -B -m unittest discover -s tests/task_manifest_validator -p "test_*.py"` (as in `.github/workflows/ci.yml`).

## Pitfalls

- A plain `SINAN_KARARLARI_*.md` file is not parsed by the validators, but the 22 Oct rule still asks for the light
  checks on any `docs/decisions/**` change — run them anyway; they are cheap.
- Never hand-compute the registry hash and never "fix" the test literal to match a failing validator without the
  recompute output.
- Docs-only PRs need two DOCS reviews (`review-signatures`) but no CI.
- Editing an older decision file in place is a silent change; add a new EK that references it instead.
