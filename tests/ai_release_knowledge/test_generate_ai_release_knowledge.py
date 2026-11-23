from __future__ import annotations

import importlib.util
import json
import shutil
import sys
import tempfile
import unittest
from pathlib import Path

REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
GENERATOR_PATH = REPOSITORY_ROOT / "tools/generate_ai_release_knowledge.py"
MODULE_NAME = "goymen_ai_release_knowledge_generator"
SPEC = importlib.util.spec_from_file_location(MODULE_NAME, GENERATOR_PATH)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError(f"Cannot load {GENERATOR_PATH}")
GENERATOR = importlib.util.module_from_spec(SPEC)
sys.modules[MODULE_NAME] = GENERATOR
SPEC.loader.exec_module(GENERATOR)

PERMISSIONS = """namespace X;
public static class ErpPermissions
{
    public const string WorkNotesWrite = "work.notes.write";
    public const string AiKnowledgeApprove = "ai.knowledge.approve";
}
"""

REGISTRY_CSV = """DecisionId,Alan,Karar,Durum,Oncelik,Kaynak,Sahip,KabulKaniti
DEC-TST-001,Test,Karar.,KILITLI,K0,Kullanıcı,Ayşe Örnek,Kanıt
"""

RELEASED = """---
kimlik: RK-TST-YAYINDA
baslik: Yayındaki özellik
durum: yayinda
yetki: work.notes.write
kanit: src/feature.cs | /api/v1/feature
---
Bu özellik yayındadır.

Ayşe Örnek sorulara ayse@example.com adresinden yanıt verir.
"""

PLANNED = """---
kimlik: RK-TST-PLANLI
baslik: Planlanan özellik
durum: uygulanmadi
yetki: -
kanit: src/feature.cs | /api/v1/feature
---
GİZLİ PLAN METNİ: henüz yok.
"""

MISSING_EVIDENCE = """---
kimlik: RK-TST-KANITSIZ
baslik: Kanıtsız özellik
durum: yayinda
yetki: -
kanit: src/feature.cs | /api/v1/not-there
---
KANITSIZ METİN: dosyada bu yol yok.
"""

MONEY = """---
kimlik: RK-TST-TUTAR
baslik: Limit
durum: yayinda
yetki: -
kanit: src/feature.cs | /api/v1/feature
---
İkinci onay 25.000 TL üzerindeki kredilerde istenir.
"""


def write_fixture(root: Path) -> None:
    notes = root / GENERATOR.NOTES_DIRECTORY
    notes.mkdir(parents=True)
    for name, text in (("RK-TST-YAYINDA.md", RELEASED), ("RK-TST-PLANLI.md", PLANNED),
                       ("RK-TST-KANITSIZ.md", MISSING_EVIDENCE), ("RK-TST-TUTAR.md", MONEY)):
        (notes / name).write_text(text, encoding="utf-8")
    (notes / "README.md").write_text("# not indexed\n", encoding="utf-8")
    (root / "src").mkdir()
    (root / "src/feature.cs").write_text('const string Path = "/api/v1/feature";\n', encoding="utf-8")
    permissions = root / GENERATOR.PERMISSIONS_PATH
    permissions.parent.mkdir(parents=True)
    permissions.write_text(PERMISSIONS, encoding="utf-8")
    registry = root / GENERATOR.REGISTRY_CSV_PATH
    registry.parent.mkdir(parents=True)
    registry.write_text(REGISTRY_CSV, encoding="utf-8")
    schema = root / GENERATOR.SCHEMA_PATH
    schema.parent.mkdir(parents=True)
    shutil.copyfile(REPOSITORY_ROOT / GENERATOR.SCHEMA_PATH, schema)


class CommittedPackageTests(unittest.TestCase):
    def test_committed_package_is_current(self) -> None:
        self.assertEqual(0, GENERATOR.main(["--check"]), "regenerate with: python3 -I tools/generate_ai_release_knowledge.py")

    def test_committed_package_matches_schema_and_invariants(self) -> None:
        package = json.loads((REPOSITORY_ROOT / GENERATOR.PACKAGE_PATH).read_text(encoding="utf-8"))
        schema = json.loads((REPOSITORY_ROOT / GENERATOR.SCHEMA_PATH).read_text(encoding="utf-8"))
        self.assertEqual([], GENERATOR.DECISIONS.validate_schema(package, schema))
        self.assertEqual([], GENERATOR.check_invariants(package))

    def test_committed_package_never_carries_an_unimplemented_note(self) -> None:
        package = GENERATOR.build_package()
        released = {note["id"] for note in package["notes"]}
        self.assertNotIn("RK-AI-KAYNAK-ONAY-EKRANI", released)
        self.assertIn({"id": "RK-AI-KAYNAK-ONAY-EKRANI", "reason": "uygulanmadi",
                       "source": {"path": "docs/release-knowledge/RK-AI-KAYNAK-ONAY-EKRANI.md", "line": 1}},
                      package["excluded"])

    def test_build_is_byte_deterministic(self) -> None:
        self.assertEqual(GENERATOR.render(GENERATOR.build_package()), GENERATOR.render(GENERATOR.build_package()))


class FixtureTests(unittest.TestCase):
    def setUp(self) -> None:
        self.directory = tempfile.TemporaryDirectory()
        self.root = Path(self.directory.name)
        write_fixture(self.root)
        self.package = GENERATOR.build_package(self.root)
        self.notes = {note["id"]: note for note in self.package["notes"]}

    def tearDown(self) -> None:
        self.directory.cleanup()

    def test_fixture_matches_schema(self) -> None:
        schema = json.loads((self.root / GENERATOR.SCHEMA_PATH).read_text(encoding="utf-8"))
        self.assertEqual([], GENERATOR.DECISIONS.validate_schema(self.package, schema))
        self.assertEqual([], GENERATOR.check_invariants(self.package))

    def test_only_released_notes_with_evidence_carry_text(self) -> None:
        self.assertEqual({"RK-TST-YAYINDA", "RK-TST-TUTAR"}, set(self.notes))
        reasons = {item["id"]: item["reason"] for item in self.package["excluded"]}
        self.assertEqual({"RK-TST-PLANLI": "uygulanmadi", "RK-TST-KANITSIZ": "kanit_bulunamadi"}, reasons)
        rendered = GENERATOR.render(self.package).decode("utf-8")
        self.assertNotIn("GİZLİ PLAN METNİ", rendered)
        self.assertNotIn("KANITSIZ METİN", rendered)

    def test_removing_the_evidence_removes_the_note(self) -> None:
        (self.root / "src/feature.cs").write_text("// feature removed\n", encoding="utf-8")
        with self.assertRaises(GENERATOR.ReleaseKnowledgeError):
            # Every note now lacks evidence: an empty package is never produced.
            GENERATOR.build_package(self.root)

    def test_permissions_are_carried_and_must_be_registered(self) -> None:
        self.assertEqual(["work.notes.write"], self.notes["RK-TST-YAYINDA"]["required_permissions"])
        self.assertEqual([], self.notes["RK-TST-TUTAR"]["required_permissions"])
        note = self.root / GENERATOR.NOTES_DIRECTORY / "RK-TST-TUTAR.md"
        note.write_text(MONEY.replace("yetki: -", "yetki: work.unknown.read"), encoding="utf-8")
        with self.assertRaises(GENERATOR.ReleaseKnowledgeError):
            GENERATOR.build_package(self.root)

    def test_person_names_are_masked_at_generation(self) -> None:
        note = self.notes["RK-TST-YAYINDA"]
        self.assertTrue(note["masked"])
        self.assertEqual(["kisi_adi", "e_posta"], note["personal_data_signals"])
        self.assertEqual("Bu özellik yayındadır.\n\n[KİŞİ] sorulara [GİZLİ] adresinden yanıt verir.", note["text"])
        rendered = GENERATOR.render(self.package).decode("utf-8")
        self.assertNotIn("Ayşe", rendered)
        self.assertNotIn("ayse@example.com", rendered)

    def test_cloud_eligibility_follows_content(self) -> None:
        self.assertEqual("maskeli_bulut_uygun_aday", self.notes["RK-TST-YAYINDA"]["cloud_eligibility"])
        self.assertEqual(("yerel_kalir", "confidential"),
                         (self.notes["RK-TST-TUTAR"]["cloud_eligibility"], self.notes["RK-TST-TUTAR"]["cloud_data_class"]))

    def test_identity_must_match_the_file_name(self) -> None:
        (self.root / GENERATOR.NOTES_DIRECTORY / "RK-TST-BASKA.md").write_text(MONEY, encoding="utf-8")
        with self.assertRaises(GENERATOR.ReleaseKnowledgeError):
            GENERATOR.build_package(self.root)

    def test_evidence_outside_the_repository_is_refused(self) -> None:
        note = self.root / GENERATOR.NOTES_DIRECTORY / "RK-TST-TUTAR.md"
        note.write_text(MONEY.replace("src/feature.cs", "../outside.cs"), encoding="utf-8")
        with self.assertRaises(GENERATOR.ReleaseKnowledgeError):
            GENERATOR.build_package(self.root)

    def test_check_mode_detects_stale_and_fresh_package(self) -> None:
        self.assertEqual(1, GENERATOR.main(["--check", "--root", str(self.root)]))
        self.assertEqual(0, GENERATOR.main(["--root", str(self.root)]))
        self.assertEqual(0, GENERATOR.main(["--check", "--root", str(self.root)]))
        note = self.root / GENERATOR.NOTES_DIRECTORY / "RK-TST-TUTAR.md"
        note.write_text(MONEY + "\nEk cümle.\n", encoding="utf-8")
        self.assertEqual(1, GENERATOR.main(["--check", "--root", str(self.root)]))

    def test_excluded_entries_cannot_carry_text(self) -> None:
        self.package["excluded"][0]["text"] = "sızan metin"
        self.assertTrue(GENERATOR.check_invariants(self.package))


if __name__ == "__main__":
    unittest.main()
