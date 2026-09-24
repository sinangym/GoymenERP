import json
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]


class C03BrowserSuiteContractTests(unittest.TestCase):
    def test_c03_has_one_exact_runner_and_remains_read_only(self):
        package = json.loads((ROOT / "tests/e2e/package.json").read_text(encoding="utf-8"))
        self.assertEqual(
            "playwright test --config playwright.cari-c03.config.ts",
            package["scripts"]["browser:c03:ci"],
        )
        validator = (ROOT / "tools/validate_browser_acceptance.py").read_text(encoding="utf-8")
        self.assertIn('READ_ONLY_SUITES = ("smoke", "c03")', validator)

    def test_c03_binds_real_browser_api_sql_and_negative_edges(self):
        config = (ROOT / "tests/e2e/playwright.cari-c03.config.ts").read_text(encoding="utf-8")
        environment = (ROOT / "tests/e2e/cari-c03/src/env.ts").read_text(encoding="utf-8")
        spec = (ROOT / "tests/e2e/cari-c03/specs/c03-company-relationships.spec.ts").read_text(
            encoding="utf-8"
        )
        self.assertIn(r"c03-company-relationships\.spec\.ts", config)
        self.assertIn("retries: 0", config)
        self.assertIn("/^GoymenErpC01_[0-9]+_[0-9]+$/", environment)
        self.assertIn("new sql.ConnectionPool(env.sqlConnection)", spec)
        self.assertIn("expect(await snapshot(pool)).toEqual(before)", spec)
        self.assertIn("expect(denied.status()).toBe(404)", spec)
        self.assertIn("deniedPanel.locator('[data-state=\"not-found\"]')", spec)
        self.assertIn("expect(deniedBody).not.toMatch(/\"code\"\\s*:/)", spec)
        self.assertIn("request.headers()['x-company-id'] === undefined", spec)
        for method in (".post(", ".put(", ".patch(", ".delete("):
            self.assertNotIn(method, spec.lower())


if __name__ == "__main__":
    unittest.main()
