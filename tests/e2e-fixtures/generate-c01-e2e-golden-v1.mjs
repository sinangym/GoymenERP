import { createHash } from "node:crypto";
import { mkdirSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const FIXTURE_ID = "C01-E2E-GOLDEN-V1";
const EXPECTED_BYTE_LENGTH = 29_817;
const EXPECTED_SHA256 =
  "f23551944a65fe29681d768422b181dd73d68b1bdf8216e42bc643713e4d79d7";
const GDC = "10000000-0000-0000-0000-000000000001";
const GSP = "10000000-0000-0000-0000-000000000002";

function id(prefix, sequence) {
  return `${prefix}-0000-0000-0000-${sequence.toString().padStart(12, "0")}`;
}

function timestamp(sequence) {
  return new Date(Date.UTC(2026, 7, 31, 0, sequence, 0)).toISOString().replace(
    ".000Z",
    "Z",
  );
}

function stableJson(value) {
  if (Array.isArray(value)) {
    return `[${value.map(stableJson).join(",")}]`;
  }

  if (value !== null && typeof value === "object") {
    const properties = Object.keys(value)
      .sort()
      .map((key) => `${JSON.stringify(key)}:${stableJson(value[key])}`);
    return `{${properties.join(",")}}`;
  }

  return JSON.stringify(value);
}

function describeParty(sequence) {
  const explicit = new Map([
    [1, ["LegalEntity", "Örnek Metal Sanayi ve Ticaret A.Ş.", "Örnek Metal", true]],
    [2, ["LegalEntity", "Pasif Hesap Metal A.Ş.", "Pasif Hesap Metal", true]],
    [3, ["NaturalPerson", "Ayşe Örnek", null, true]],
    [4, ["LegalEntity", "Işık Profil Sanayi A.Ş.", "Işık Profil", true]],
    [5, ["LegalEntity", "İpek Çelik Sanayi A.Ş.", "İpek Çelik", true]],
    [6, ["NaturalPerson", "Şule Yılmaz", null, true]],
    [53, ["LegalEntity", "Tarihsel Cari Sanayi A.Ş.", "Tarihsel Cari", false]],
    [54, ["LegalEntity", "Yalnız GSP Metal A.Ş.", "Yalnız GSP", true]],
  ]);

  if (explicit.has(sequence)) {
    return explicit.get(sequence);
  }

  if (sequence % 2 === 0) {
    return [
      "LegalEntity",
      `Golden Metal ${sequence.toString().padStart(3, "0")} Sanayi A.Ş.`,
      `Golden Metal ${sequence.toString().padStart(3, "0")}`,
      true,
    ];
  }

  return [
    "NaturalPerson",
    `Golden Kişi ${sequence.toString().padStart(3, "0")}`,
    null,
    true,
  ];
}

function rolesFor(sequence) {
  if (sequence === 1) return 3;
  if (sequence === 2) return 1;
  if (sequence === 3) return 8;
  if (sequence === 4) return 1;
  if (sequence === 5) return 2;
  if (sequence === 6) return 8;
  if (sequence === 53) return 4;
  if (sequence === 54) return 2;

  return [1, 2, 4, 8][sequence % 4];
}

const parties = [];
const companyPartyAccounts = [];

for (let sequence = 1; sequence <= 54; sequence += 1) {
  const [kind, legalName, tradeName, isActive] = describeParty(sequence);
  const partyId = id("c0100000", sequence);
  const instant = timestamp(sequence);

  parties.push({
    code: `CAR-${sequence.toString().padStart(6, "0")}`,
    createdAtUtc: instant,
    id: partyId,
    isActive,
    kind,
    legalName,
    tradeName,
    updatedAtUtc: instant,
    version: 1,
  });

  if (sequence !== 54) {
    companyPartyAccounts.push({
      companyId: GDC,
      createdAtUtc: instant,
      externalIntegrationCode: null,
      id: id("c0110000", sequence),
      isActive: sequence !== 2,
      partyId,
      roles: rolesFor(sequence),
      updatedAtUtc: instant,
      version: 1,
    });
  }

  if (sequence === 3 || sequence === 54) {
    companyPartyAccounts.push({
      companyId: GSP,
      createdAtUtc: instant,
      externalIntegrationCode: null,
      id: id("c0120000", sequence),
      isActive: true,
      partyId,
      roles: 2,
      updatedAtUtc: instant,
      version: 1,
    });
  }
}

const fixture = {
  companyPartyAccounts,
  fixtureId: FIXTURE_ID,
  parties,
  schemaVersion: 1,
  sequenceNextValue: 55,
};

const json = stableJson(fixture);
const bytes = Buffer.from(json, "utf8");
const sha256 = createHash("sha256").update(bytes).digest("hex");

if (bytes.length !== EXPECTED_BYTE_LENGTH || sha256 !== EXPECTED_SHA256) {
  throw new Error(
    `Canonical fixture mismatch: byteLength=${bytes.length}, sha256=${sha256}`,
  );
}

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const outputPath = resolve(scriptDirectory, "C01-E2E-GOLDEN-V1.json");
mkdirSync(dirname(outputPath), { recursive: true });
writeFileSync(outputPath, bytes);

process.stdout.write(
  `${FIXTURE_ID} byteLength=${bytes.length} sha256=${sha256}\n`,
);
