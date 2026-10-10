export interface R14Seed {
  readonly recorderActor: string
  readonly deniedActor: string
  readonly arrangerActor: string
  /** R19: the ONLY actor holding inventory.label-transformation.record (disposable, this per-run database). */
  readonly transformerActor: string
  /** AT-037: the ONLY holder of inventory.stock-count.record (disposable). */
  readonly counterActor: string
  /** AT-037: the ONLY holder of inventory.stock-count.approve (disposable), never the counter. */
  readonly countApproverActor: string
  /** ST-02 scrap: the ONLY holder of inventory.stock-scrap.record (disposable). */
  readonly scrapperActor: string
  /** ST-02 scrap: the ONLY holder of inventory.stock-scrap.approve (disposable), never the scrapper. */
  readonly scrapApproverActor: string
  readonly companyId: string
  readonly otherCompanyId: string
  readonly locationId: string
  readonly productId: string
  readonly productVersion: string
  readonly productUnitId: string
  readonly productUnitProfileId: string
  readonly productUnitProfileVersion: string
  readonly productCode: string
  readonly productName: string
  /** STOCK-OWNER-DISPLAY-3: the one synthetic Cari owner the seed opened through the public Cari API. */
  readonly ownerPartyId: string
  /** That owner's account in the seeded company — the opening command's owner-account reference. */
  readonly ownerCompanyAccountId: string
}

export interface R14Environment {
  readonly apiBaseUrl: string
  readonly sqlConnection: string
  readonly database: string
  readonly testedCommit: string
  readonly seed: R14Seed
}

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/
const VERSION = /^[1-9]\d*$/

function required(name: string): string {
  const value = process.env[name]?.trim()
  if (!value) throw new Error(`${name} is not set; R14 runs only inside the disposable browser stack.`)
  return value
}

function text(raw: Record<string, unknown>, key: keyof R14Seed, pattern?: RegExp): string {
  const value = raw[key]
  if (typeof value !== 'string' || value.trim() !== value || value.length === 0 || (pattern && !pattern.test(value))) {
    throw new Error(`BROWSER_E2E_R14_SEED field "${key}" is missing or malformed`)
  }
  return value
}

export function readEnvironment(): R14Environment {
  const testedCommit = required('BROWSER_E2E_TESTED_COMMIT')
  if (!/^[0-9a-f]{40}$/.test(testedCommit)) throw new Error('BROWSER_E2E_TESTED_COMMIT is not an exact commit')
  const apiBaseUrl = required('BROWSER_E2E_API_BASE_URL')
  if (new URL(apiBaseUrl).hostname !== '127.0.0.1') throw new Error('R14 API must use loopback')
  const database = required('BROWSER_E2E_DATABASE')
  if (!/^GoymenErpC01_[0-9]+_[0-9]+$/.test(database)) throw new Error('R14 database is not disposable')
  const sqlConnection = required('BROWSER_E2E_SQL_CONNECTION')
  if (!sqlConnection.toLowerCase().includes(`database=${database.toLowerCase()}`)) {
    throw new Error('R14 SQL connection is not bound to the disposable database')
  }

  const raw = JSON.parse(required('BROWSER_E2E_R14_SEED')) as Record<string, unknown>
  const expectedKeys: (keyof R14Seed)[] = [
    'recorderActor', 'deniedActor', 'arrangerActor', 'transformerActor', 'counterActor', 'countApproverActor', 'scrapperActor', 'scrapApproverActor', 'companyId', 'otherCompanyId', 'locationId',
    'productId', 'productVersion', 'productUnitId', 'productUnitProfileId',
    'productUnitProfileVersion', 'productCode', 'productName', 'ownerPartyId', 'ownerCompanyAccountId',
  ]
  if (Object.keys(raw).sort().join('|') !== [...expectedKeys].sort().join('|')) {
    throw new Error('BROWSER_E2E_R14_SEED keys are not the closed contract')
  }
  const seed: R14Seed = {
    recorderActor: text(raw, 'recorderActor'), deniedActor: text(raw, 'deniedActor'),
    arrangerActor: text(raw, 'arrangerActor'), transformerActor: text(raw, 'transformerActor'),
    counterActor: text(raw, 'counterActor'), countApproverActor: text(raw, 'countApproverActor'),
    scrapperActor: text(raw, 'scrapperActor'), scrapApproverActor: text(raw, 'scrapApproverActor'),
    companyId: text(raw, 'companyId', GUID),
    otherCompanyId: text(raw, 'otherCompanyId', GUID), locationId: text(raw, 'locationId', GUID),
    productId: text(raw, 'productId', GUID), productVersion: text(raw, 'productVersion', VERSION),
    productUnitId: text(raw, 'productUnitId', GUID),
    productUnitProfileId: text(raw, 'productUnitProfileId', GUID),
    productUnitProfileVersion: text(raw, 'productUnitProfileVersion', VERSION),
    productCode: text(raw, 'productCode'), productName: text(raw, 'productName'),
    ownerPartyId: text(raw, 'ownerPartyId', GUID),
    ownerCompanyAccountId: text(raw, 'ownerCompanyAccountId', GUID),
  }
  if (new Set([seed.recorderActor, seed.deniedActor, seed.arrangerActor, seed.transformerActor,
    seed.counterActor, seed.countApproverActor, seed.scrapperActor, seed.scrapApproverActor]).size !== 8 ||
      seed.companyId === seed.otherCompanyId || seed.productCode !== 'SAC-1' ||
      seed.ownerPartyId === seed.ownerCompanyAccountId ||
      [seed.companyId, seed.otherCompanyId].includes(seed.ownerPartyId) ||
      seed.productName !== 'SENTETIK browser-suite:r14 genuine S235JR 3 mm x 248 mm kaplamasiz sac') {
    throw new Error('BROWSER_E2E_R14_SEED semantic binding is invalid')
  }
  return { apiBaseUrl, sqlConnection, database, testedCommit, seed }
}
