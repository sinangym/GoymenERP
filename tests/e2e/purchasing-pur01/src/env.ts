export interface Pur01Seed {
  readonly approverActor: string
  readonly readerActor: string
  readonly companyId: string
  readonly otherCompanyId: string
  readonly orderId: string
  readonly lineId: string
}

export interface Pur01Environment {
  readonly apiBaseUrl: string
  readonly testedCommit: string
  readonly seed: Pur01Seed
}

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

function required(name: string): string {
  const value = process.env[name]?.trim()
  if (!value) throw new Error(`${name} is not set; pur01 runs only inside the BROWSER-CI disposable stack.`)
  return value
}

function text(raw: Record<string, unknown>, field: keyof Pur01Seed, guid = false): string {
  const value = raw[field]
  if (typeof value !== 'string' || value.trim().length === 0 || (guid && !GUID.test(value.trim()))) {
    throw new Error(`BROWSER_E2E_PUR01_SEED field "${field}" is missing or malformed`)
  }
  return value.trim()
}

export function readEnvironment(): Pur01Environment {
  const testedCommit = required('BROWSER_E2E_TESTED_COMMIT')
  if (!/^[0-9a-f]{40}$/.test(testedCommit)) {
    throw new Error(`BROWSER_E2E_TESTED_COMMIT must be a full 40-character commit, got "${testedCommit}"`)
  }
  const apiBaseUrl = required('BROWSER_E2E_API_BASE_URL')
  if (new URL(apiBaseUrl).hostname !== '127.0.0.1') {
    throw new Error(`BROWSER_E2E_API_BASE_URL must be the disposable loopback API, got ${apiBaseUrl}`)
  }

  const raw = JSON.parse(required('BROWSER_E2E_PUR01_SEED')) as Record<string, unknown>
  const seed: Pur01Seed = {
    approverActor: text(raw, 'approverActor'),
    readerActor: text(raw, 'readerActor'),
    companyId: text(raw, 'companyId', true),
    otherCompanyId: text(raw, 'otherCompanyId', true),
    orderId: text(raw, 'orderId', true),
    lineId: text(raw, 'lineId', true),
  }
  if (seed.approverActor === seed.readerActor) throw new Error('BROWSER_E2E_PUR01_SEED binds one actor to both personas')
  if (seed.companyId === seed.otherCompanyId) throw new Error('BROWSER_E2E_PUR01_SEED names the same company twice')
  return { apiBaseUrl, testedCommit, seed }
}
