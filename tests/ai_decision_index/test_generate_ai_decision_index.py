from __future__ import annotations

import importlib.util
import json
import shutil
import sys
import tempfile
import unittest
from pathlib import Path

REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
GENERATOR_PATH = REPOSITORY_ROOT / "tools/generate_ai_decision_index.py"
MODULE_NAME = "goymen_ai_decision_index_generator"
SPEC = importlib.util.spec_from_file_location(MODULE_NAME, GENERATOR_PATH)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError(f"Cannot load {GENERATOR_PATH}")
GENERATOR = importlib.util.module_from_spec(SPEC)
sys.modules[MODULE_NAME] = GENERATOR
SPEC.loader.exec_module(GENERATOR)

REGISTRY_MD = """# Sicil

| ID | Alan | Karar | Durum | Öncelik | Kaynak | Sahip | Kabul kanıtı |
|---|---|---|---|---|---|---|---|
| DEC-TST-001 | Test | Birinci sicil kararı. | KILITLI | K0 | Kullanıcı | Ayşe Örnek | Kanıt |
| DEC-TST-002 | Test | İkinci sicil kararı. | GECICI_VARSAYIM | K1 | Kullanıcı | Ayşe Örnek | Kanıt |
"""

REGISTRY_CSV = """﻿DecisionId,Alan,Karar,Durum,Oncelik,Kaynak,Sahip,KabulKaniti
DEC-TST-001,Test,Birinci sicil kararı.,KILITLI,K0,Kullanıcı,Ayşe Örnek,Kanıt
DEC-TST-002,Test,İkinci sicil kararı farklı.,GECICI_VARSAYIM,K1,Kullanıcı,Ayşe Örnek,Kanıt
"""

FIRST = """# Sinan Kararları — test

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | TST-D9 nedir? | Evet | Soru tablosu karar değildir. |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| TST-D1 | Eski | Eski kural geçerlidir. | Satış |
| TST-D2 | Eski iki | İkinci eski kural. | Satış |
| TST-D3 | Kısmi hedef | Üçüncü kural iki kısımlıdır. | Alış |
| TST-D4 | Tekrar | Aynı metin. | Stok |
| TST-D5 | Çelişen | Birinci metin. | Stok |
| 7 | Numaralı | Numaralı karar. | Genel |
"""

SECOND = """# Sinan Kararları — test 2

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| TST-D10 | Yeni | Bu karar TST-D1'in yerine geçer. | Satış |
| TST-D11 | Kısmi | Bu karar TST-D3'teki "iki kısım" ifadesinin yerine geçer. | Alış |
| TST-D12 | Edilgen | TST-D2 bu kararla güncellendi. | Satış |
| TST-D13 | Kişi | Sorular test@example.com adresine yazılır. | Genel |
| TST-D4 | Tekrar | Aynı metin. | Stok |
| TST-D5 | Çelişen | İkinci metin. | Stok |
| TST-D14 | Araç | Önceki TST-D2 TST-D10 ile geçersizdir. | Genel |
| TST-D16 | Sahiplik | Ayşe Örnek bu kuralı onaylar; iletişim 0532 123 45 67. | Ayşe ekibi |
| TST-D17 | Limit | Kredi limiti 25.000 TL üzerindeyse ikinci onay gerekir. | Finans |
"""

GOLDEN = """# Test Golden v1 (TST-D1)

## 1. Bölüm

| # | Girdi | Sonuç |
|---|---|---|
| G01 | 1 | 2 |
"""


def write_fixture(root: Path) -> None:
    decisions = root / "docs/decisions"
    decisions.mkdir(parents=True)
    (decisions / "KARAR_SICILI.md").write_text(REGISTRY_MD, encoding="utf-8")
    (decisions / "KARAR_SICILI.csv").write_text(REGISTRY_CSV, encoding="utf-8")
    (decisions / "SINAN_KARARLARI_2026-01-01_TEST.md").write_text(FIRST, encoding="utf-8")
    (decisions / "SINAN_KARARLARI_2026-01-02_TEST.md").write_text(SECOND, encoding="utf-8")
    (decisions / "TEST_GOLDEN_V1.md").write_text(GOLDEN, encoding="utf-8")
    (decisions / "NOT_INDEXED.md").write_text("| No | Konu | Karar |\n|---|---|---|\n| TST-D99 | x | y |\n",
                                              encoding="utf-8")
    schema_target = root / GENERATOR.SCHEMA_PATH
    schema_target.parent.mkdir(parents=True)
    shutil.copyfile(REPOSITORY_ROOT / GENERATOR.SCHEMA_PATH, schema_target)


class CommittedIndexTests(unittest.TestCase):
    def test_committed_index_is_current(self) -> None:
        self.assertEqual(0, GENERATOR.main(["--check"]), "regenerate with: python3 -I tools/generate_ai_decision_index.py")

    def test_committed_index_matches_schema_and_invariants(self) -> None:
        index = json.loads((REPOSITORY_ROOT / GENERATOR.INDEX_PATH).read_text(encoding="utf-8"))
        schema = json.loads((REPOSITORY_ROOT / GENERATOR.SCHEMA_PATH).read_text(encoding="utf-8"))
        self.assertEqual([], GENERATOR.validate_schema(index, schema))
        self.assertEqual([], GENERATOR.check_invariants(index))

    def test_build_is_byte_deterministic(self) -> None:
        self.assertEqual(GENERATOR.render(GENERATOR.build_index()), GENERATOR.render(GENERATOR.build_index()))

    def test_known_registry_and_user_decisions_are_present_once(self) -> None:
        index = GENERATOR.build_index()
        identities = [record["id"] for record in index["records"]]
        self.assertEqual(len(identities), len(set(identities)))
        for expected in ("DEC-GOV-001", "AI-D12", "AI-D34", "YOL-D21"):
            self.assertIn(expected, identities)
        record = next(r for r in index["records"] if r["id"] == "AI-D24")
        self.assertEqual("2026-11-22", record["effective_date"])
        self.assertTrue(record["source"]["path"].startswith("docs/decisions/SINAN_KARARLARI_2026-11-22"))


class FixtureTests(unittest.TestCase):
    def setUp(self) -> None:
        self.directory = tempfile.TemporaryDirectory()
        self.root = Path(self.directory.name)
        write_fixture(self.root)
        self.index = GENERATOR.build_index(self.root)
        self.records = {record["id"]: record for record in self.index["records"]}

    def tearDown(self) -> None:
        self.directory.cleanup()

    def test_fixture_matches_schema(self) -> None:
        schema = json.loads((self.root / GENERATOR.SCHEMA_PATH).read_text(encoding="utf-8"))
        self.assertEqual([], GENERATOR.validate_schema(self.index, schema))
        self.assertEqual([], GENERATOR.check_invariants(self.index))

    def test_registry_status_and_question_tables(self) -> None:
        self.assertEqual("kilitli", self.records["DEC-TST-001"]["status"])
        self.assertEqual("gecici", self.records["DEC-TST-002"]["status"])
        self.assertNotIn("TST-D9", self.records)
        self.assertNotIn("TST-D99", self.records)

    def test_explicit_whole_supersede_marks_target_invalid(self) -> None:
        self.assertEqual("gecersiz", self.records["TST-D1"]["status"])
        self.assertIn({"by": "TST-D10", "type": "yerine_gecer", "confidence": "acik", "extent": "tam_veya_belirsiz"},
                      self.records["TST-D1"]["superseded_by"])

    def test_partial_supersede_keeps_status(self) -> None:
        self.assertEqual("kilitli", self.records["TST-D3"]["status"])
        relation = self.records["TST-D11"]["relations"][0]
        self.assertEqual(("yerine_gecer", "TST-D3", "acik", "kismi"),
                         (relation["type"], relation["target"], relation["confidence"], relation["extent"]))

    def test_passive_and_agent_forms_are_ambiguous(self) -> None:
        self.assertEqual("kilitli", self.records["TST-D2"]["status"])
        self.assertTrue(all(r["confidence"] == "belirsiz" for r in self.records["TST-D12"]["relations"]))
        self.assertTrue(all(r["confidence"] == "belirsiz" for r in self.records["TST-D14"]["relations"]))

    def test_duplicates_and_conflicts(self) -> None:
        self.assertEqual(1, len(self.records["TST-D4"]["also_seen_in"]))
        kinds = {(c["id"], c["kind"]) for c in self.index["conflicts"]}
        self.assertIn(("TST-D5", "ayni_kimlik_farkli_metin"), kinds)
        self.assertIn(("DEC-TST-002", "sicil_md_csv_farki"), kinds)
        self.assertTrue(self.records["TST-D5"]["has_conflict"])
        self.assertEqual("Birinci metin.", self.records["TST-D5"]["text"])

    def test_personal_data_candidates(self) -> None:
        self.assertEqual(["e_posta"], self.records["TST-D13"]["personal_data_signals"])
        self.assertEqual("kisisel_veri_adayi", self.records["TST-D13"]["data_class_candidate"])
        self.assertEqual("kisisel_veri_isareti_yok", self.records["TST-D3"]["data_class_candidate"])
        # Plan D2: the name terms are counted, never published.
        self.assertEqual(3, self.index["personal_name_term_count"])
        self.assertNotIn("personal_name_terms", self.index)

    def test_person_names_and_patterns_are_masked_at_generation(self) -> None:
        record = self.records["TST-D16"]
        self.assertTrue(record["masked"])
        self.assertEqual(["kisi_adi", "telefon"], record["personal_data_signals"])
        self.assertEqual("[KİŞİ] bu kuralı onaylar; iletişim [GİZLİ].", record["text"])
        self.assertEqual("[KİŞİ] ekibi", record["scope"]["affected"])
        self.assertEqual("[GİZLİ] adresine", self.records["TST-D13"]["text"].split("Sorular ", 1)[1][:16])
        self.assertFalse(self.records["TST-D3"]["masked"])
        rendered = GENERATOR.render(self.index).decode("utf-8")
        for plain in ("Ayşe", "Örnek", "test@example.com", "0532 123 45 67"):
            self.assertNotIn(plain, rendered)

    def test_registry_owner_column_is_not_copied_into_the_index(self) -> None:
        rendered = GENERATOR.render(self.index).decode("utf-8")
        self.assertNotIn("Ayşe", rendered)
        self.assertNotIn("Örnek", rendered)

    def test_masking_leak_is_a_generation_error(self) -> None:
        original = GENERATOR.mask_text
        try:
            GENERATOR.mask_text = lambda text, names: text
            with self.assertRaises(GENERATOR.DecisionIndexError):
                GENERATOR.build_index(self.root)
        finally:
            GENERATOR.mask_text = original

    def test_cloud_eligibility_follows_content_and_data_class(self) -> None:
        self.assertEqual(("bulut_uygun_aday", "internal"),
                         (self.records["TST-D3"]["cloud_eligibility"], self.records["TST-D3"]["cloud_data_class"]))
        self.assertEqual(("yerel_kalir", "confidential"),
                         (self.records["TST-D16"]["cloud_eligibility"], self.records["TST-D16"]["cloud_data_class"]))
        self.assertEqual(("yerel_kalir", "confidential"),
                         (self.records["TST-D17"]["cloud_eligibility"], self.records["TST-D17"]["cloud_data_class"]))
        self.assertEqual(["para_tutari"], self.records["TST-D17"]["commercial_signals"])
        counts = self.index["statistics"]["cloud_eligibility_counts"]
        self.assertEqual(len(self.index["records"]), sum(counts.values()))

    def test_tampered_cloud_label_fails_the_invariants(self) -> None:
        record = next(r for r in self.index["records"] if r["id"] == "TST-D17")
        record["cloud_eligibility"] = "bulut_uygun_aday"
        self.assertTrue(any("bulut" in error for error in GENERATOR.check_invariants(self.index)))

    def test_numeric_rows_and_goldens(self) -> None:
        self.assertIn("SK-2026-01-01-7", self.records)
        golden = self.records["GOLDEN-TEST-GOLDEN-V1"]
        self.assertEqual("formul_golden", golden["kind"])
        self.assertEqual(["TST-D1"], golden["references"])

    def test_check_mode_detects_stale_and_fresh_index(self) -> None:
        self.assertEqual(1, GENERATOR.main(["--check", "--root", str(self.root)]))
        self.assertEqual(0, GENERATOR.main(["--root", str(self.root)]))
        self.assertEqual(0, GENERATOR.main(["--check", "--root", str(self.root)]))
        decision = self.root / "docs/decisions/SINAN_KARARLARI_2026-01-02_TEST.md"
        decision.write_text(SECOND + "| TST-D15 | Ek | Yeni karar. | Genel |\n", encoding="utf-8")
        self.assertEqual(1, GENERATOR.main(["--check", "--root", str(self.root)]))

    def test_missing_registry_is_an_error(self) -> None:
        (self.root / "docs/decisions/KARAR_SICILI.csv").unlink()
        with self.assertRaises(GENERATOR.DecisionIndexError):
            GENERATOR.build_index(self.root)


if __name__ == "__main__":
    unittest.main()
