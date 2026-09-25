export interface R14Seed {
  readonly recorderActor: string
  readonly deniedActor: string
  readonly arrangerActor: string
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
    'recorderActor', 'deniedActor', 'arrangerActor', 'companyId', 'otherCompanyId', 'locationId',
    'productId', 'productVersion', 'productUnitId', 'productUnitProfileId',
    'productUnitProfileVersion', 'productCode', 'productName',
  ]
  if (Object.keys(raw).sort().join('|') !== [...expectedKeys].sort().join('|')) {
    throw new Error('BROWSER_E2E_R14_SEED keys are not the closed contract')
  }
  const seed: R14Seed = {
    recorderActor: text(raw, 'recorderActor'), deniedActor: text(raw, 'deniedActor'),
    arrangerActor: text(raw, 'arrangerActor'), companyId: text(raw, 'companyId', GUID),
    otherCompanyId: text(raw, 'otherCompanyId', GUID), locationId: text(raw, 'locationId', GUID),
    productId: text(raw, 'productId', GUID), productVersion: text(raw, 'productVersion', VERSION),
    productUnitId: text(raw, 'productUnitId', GUID),
    productUnitProfileId: text(raw, 'productUnitProfileId', GUID),
    productUnitProfileVersion: text(raw, 'productUnitProfileVersion', VERSION),
    productCode: text(raw, 'productCode'), productName: text(raw, 'productName'),
  }
  if (new Set([seed.recorderActor, seed.deniedActor, seed.arrangerActor]).size !== 3 ||
      seed.companyId === seed.otherCompanyId || !/^SYNR14-[1-9]\d*$/.test(seed.productCode) ||
      seed.productName !== 'SENTETIK browser-suite:r14 stock candidate') {
    throw new Error('BROWSER_E2E_R14_SEED semantic binding is invalid')
  }
  return { apiBaseUrl, sqlConnection, database, testedCommit, seed }
}
