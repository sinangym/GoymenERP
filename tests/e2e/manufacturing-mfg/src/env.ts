/**
 * The `mfg` suite's inputs, from the BROWSER-CI workflow's contract (`tests/e2e/browser-ci/README.md`)
 * plus the suite's own seed binding. Every value is required; there is no default and no fallback:
 * a run outside the disposable stack fails and never tests something else.
 *
 * `BROWSER_E2E_MFG_SEED` (JSON) is written by the stage-2 seed step (a2 part 2, CARİ's interface)
 * together with its synthetic test-database grants. This suite adds no actor, role or grant; it
 * only names what it needs:
 *
 *   recorderActor — Development user holding, company-wide in `companyId`, the Manufacturing plan/order
 *                   permissions and the existing owner permissions needed by the real plan form and its
 *                   opening-stock fixture
 *   readerActor   — Development user holding only manufacturing.orders.read there
 *   closerActor   — Development user holding manufacturing.orders.read + manufacturing.orders.close,
 *                   and no fact permission (MFG-ORD: closing is its own authority)
 *   companyId     — the company whose approved kilogram profile the real Settings provider answers
 *   workCenterId  — one of that company's work centers
 *   orderId       — an open order the fixture (tests/GoymenERP.BrowserSuiteFixtureTool/Suites/MFG)
 *                   arranged through the real API; the reader case reads it
 *   rollId        — the reader order's real Inventory parent-roll label, recorded by the fixture command
 *   rolls         — distinct real Inventory parent rolls for every mutating browser case, so one
 *                   case's stock movement cannot change another case's starting balance
 *
 * The screen's capabilities (`manufacturing.orders.detail` / `.facts` / `.list` / `.close`) are published by the normal
 * runtime (MFG-UI-F; first accepted on the disposable `mfg-staged` profile, card D4) and opened to the
 * actors only by their synthetic grants.
 */

export interface MfgRollSeed {
  readonly labelId: string
  readonly labelCode: string
}

export const MFG_PROCESSES = ['Slitting', 'ColdRolling', 'TubeProfile', 'Paint', 'CutToLength'] as const
export type MfgProcess = (typeof MFG_PROCESSES)[number]

const PROCESS_ROLL_SUFFIX: Readonly<Record<MfgProcess, number>> = {
  Slitting: 11,
  ColdRolling: 12,
  TubeProfile: 13,
  Paint: 14,
  CutToLength: 15,
}

export interface MfgRolls {
  readonly issue: MfgRollSeed
  readonly replay: MfgRollSeed
  readonly conflict: MfgRollSeed
  readonly list: MfgRollSeed
  readonly close: MfgRollSeed
  readonly planCreate: MfgRollSeed
  readonly planReplay: MfgRollSeed
  readonly planRefusal: MfgRollSeed
  readonly outputUi: MfgRollSeed
  readonly slittingSpec: MfgRollSeed
  readonly process: Readonly<Record<MfgProcess, MfgRollSeed>>
}

export interface MfgOutputSeed {
  readonly productId: string
  readonly productVersion: string
  readonly requiredDocuments: readonly string[]
  readonly ownerKind: string
  readonly ownerId: string
  readonly custodianKind: string
  readonly custodianId: string
  readonly locationId: string
  readonly initialBlock: string
}

export interface MfgCustomerOwnedSeed {
  readonly orderId: string
  readonly roll: MfgRollSeed
}

export interface MfgSeed {
  readonly recorderActor: string
  readonly readerActor: string
  readonly closerActor: string
  readonly companyId: string
  readonly workCenterId: string
  readonly orderId: string
  readonly rollId: string
  readonly rolls: MfgRolls
  readonly output: MfgOutputSeed
  readonly customerOwned: MfgCustomerOwnedSeed
}

export interface MfgEnvironment {
  readonly apiBaseUrl: string
  readonly testedCommit: string
  readonly seed: MfgSeed
}

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

export function required(name: string): string {
  const value = process.env[name]?.trim()
  if (!value) {
    throw new Error(`${name} is not set; the mfg suite runs only inside the BROWSER-CI disposable stack.`)
  }
  return value
}

function text(raw: Record<string, unknown>, field: string, guid = false): string {
  const value = raw[field]
  if (typeof value !== 'string' || value.trim().length === 0 || (guid && !GUID.test(value.trim()))) {
    throw new Error(`BROWSER_E2E_MFG_SEED field "${field}" is missing or malformed`)
  }
  return value.trim()
}

function stringList(raw: Record<string, unknown>, field: string): readonly string[] {
  const serialized = text(raw, field)
  let value: unknown
  try {
    value = JSON.parse(serialized)
  } catch {
    throw new Error(`BROWSER_E2E_MFG_SEED field "${field}" is malformed JSON`)
  }
  if (!Array.isArray(value) || value.some((item) => typeof item !== 'string' || item.trim().length === 0)) {
    throw new Error(`BROWSER_E2E_MFG_SEED field "${field}" is not a string array`)
  }
  return value
}

function processRoll(process: MfgProcess): MfgRollSeed {
  const suffix = PROCESS_ROLL_SUFFIX[process]
  return {
    // MfgBrowserSeed.Roll owns these deterministic disposable-fixture identities. They stay out of
    // BROWSER_E2E_MFG_SEED so the centrally governed seed-output contract remains unchanged.
    labelId: `c3000000-0000-4000-8000-${suffix.toString().padStart(12, '0')}`,
    labelCode: `SYN-MFG-ROLL-${suffix}`,
  }
}

export function readEnvironment(): MfgEnvironment {
  const testedCommit = required('BROWSER_E2E_TESTED_COMMIT')
  if (!/^[0-9a-f]{40}$/.test(testedCommit)) {
    throw new Error(`BROWSER_E2E_TESTED_COMMIT must be a full 40-character commit, got "${testedCommit}"`)
  }
  const apiBaseUrl = required('BROWSER_E2E_API_BASE_URL')
  if (new URL(apiBaseUrl).hostname !== '127.0.0.1') {
    throw new Error(`BROWSER_E2E_API_BASE_URL must be the disposable loopback API, got ${apiBaseUrl}`)
  }
  const raw = JSON.parse(required('BROWSER_E2E_MFG_SEED')) as Record<string, unknown>
  const roll = (name: string): MfgRollSeed => ({
    labelId: text(raw, `${name}RollId`, true),
    labelCode: text(raw, `${name}RollCode`),
  })
  const seed: MfgSeed = {
    recorderActor: text(raw, 'recorderActor'),
    readerActor: text(raw, 'readerActor'),
    closerActor: text(raw, 'closerActor'),
    companyId: text(raw, 'companyId', true),
    workCenterId: text(raw, 'workCenterId', true),
    orderId: text(raw, 'orderId', true),
    rollId: text(raw, 'rollId', true),
    rolls: {
      issue: roll('issue'),
      replay: roll('replay'),
      conflict: roll('conflict'),
      list: roll('list'),
      close: roll('close'),
      planCreate: roll('planCreate'),
      planReplay: roll('planReplay'),
      planRefusal: roll('planRefusal'),
      outputUi: roll('outputUi'),
      // MfgBrowserSeed owns this isolated opening-count row. It is derived here like the
      // process rolls so the centrally validated seed-output envelope remains unchanged.
      slittingSpec: {
        labelId: 'c3000000-0000-4000-8000-000000000017',
        labelCode: 'SYN-MFG-ROLL-17',
      },
      process: {
        Slitting: processRoll('Slitting'),
        ColdRolling: processRoll('ColdRolling'),
        TubeProfile: processRoll('TubeProfile'),
        Paint: processRoll('Paint'),
        CutToLength: processRoll('CutToLength'),
      },
    },
    output: {
      productId: text(raw, 'outputProductId', true),
      productVersion: text(raw, 'outputProductVersion'),
      requiredDocuments: stringList(raw, 'outputRequiredDocuments'),
      ownerKind: text(raw, 'outputOwnerKind'),
      ownerId: text(raw, 'outputOwnerId', true),
      custodianKind: text(raw, 'outputCustodianKind'),
      custodianId: text(raw, 'outputCustodianId', true),
      locationId: text(raw, 'outputLocationId', true),
      initialBlock: text(raw, 'outputInitialBlock'),
    },
    // These are fixture-owned deterministic identities, like the five process rolls above. Keeping
    // them derived here avoids widening the centrally validated BROWSER_E2E_MFG_SEED export.
    customerOwned: {
      orderId: 'c2000000-0000-4000-8000-000000000002',
      roll: {
        labelId: 'c3000000-0000-4000-8000-000000000016',
        labelCode: 'SYN-MFG-ROLL-16',
      },
    },
  }
  // Three distinct authorities: recording a fact, reading, and closing (MFG-ORD).
  if (new Set([seed.recorderActor, seed.readerActor, seed.closerActor]).size !== 3) {
    throw new Error('BROWSER_E2E_MFG_SEED must bind a distinct recorder, reader and closer')
  }
  return { apiBaseUrl, testedCommit, seed }
}
