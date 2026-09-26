import { createHash, randomUUID } from 'node:crypto'
import {
  expect,
  test,
  type APIResponse,
  type Page,
  type Request as BrowserRequest,
  type Response,
} from '@playwright/test'
import sql from 'mssql'

/**
 * U-01-360 activation (#466) on the REAL API and UI of the u02 disposable stack. Nothing is mocked; the only
 * interception holds the real server answer back from the page for a moment (after the server committed) to
 * show that the screen does not paint Active before the server says so, and then delivers it unchanged.
 *
 * Authority (user decision 2026-09-27): the Product master Activate operation is granted ONLY to the disposable
 * u02 activator of this per-run database (u02 fixture, issued by the suite's synthetic issuer). The existing
 * authorized actor (group-bootstrap-admin: Read / DraftCreate / DraftBasicEdit) and every seeded role hold no
 * Activate; the unauthorized actor (company-a-user) holds no Product authority at all. Measured, not assumed:
 * the server's capability snapshots and read-only SQL over identity.ProductMasterAssignments.
 *
 * Journey, one Draft card of this run:
 *  1. Precondition: the authorized actor creates the Draft (with the synthetic release's family, template,
 *     attributes and kilogram units, chosen from the real reference-options read as U02-FIX-01's form does) and
 *     edits it once through the real commands, so the card has a stale version (v1), a current one (v2) and a
 *     server-resolved name for every reference it carries.
 *  2. The authorized actor opens the Draft card: its own edit link is there, the activation control is not, and
 *     its direct activation command (the UI's headers, from the page's request context) answers 403.
 *  3. The unauthorized actor is closed out of the card (no Product request) and its command answers 403.
 *  4. The activator's command with the stale expected version answers 409 version_conflict; nothing is written.
 *  5. The activator opens the card and activates it in the browser. The server commits; while its answer is
 *     held, the screen still shows Taslak, the full labels and a disabled in-flight control. Once the 200 is
 *     delivered, the page re-reads the card exactly once, and right then — before any close/reopen — the
 *     screen shows Aktif and every family/template/fact/attribute/unit name, none as "ad okunamadı" or
 *     "atanmadı" (#466 review P1-1: the command's own card carries the group label only).
 *  6. Close and reopen (a fresh navigation): Active and v3 come from the server's card read, and the history
 *     read (and its list) holds exactly one activation row: v2 → v3, "Ürün kartı onayı", changed field lifecycle.
 *  7. The same Idempotency-Key and body resent answers the replay (Idempotency-Replayed, isReplay) with no
 *     second history row, audit event or command terminal and no version bump; the same key with a changed
 *     body is an idempotency_conflict; a new key with the now-stale v2 is a lifecycle_conflict; the authorized
 *     actor sending the activator's exact request is 403 (authority before replay). A last reopen still shows
 *     one activation row.
 *
 * SQL is read-only (the workflow's BROWSER_E2E_SQL_CONNECTION) and every count is scoped to this run's product.
 */

type ActivationSeed = {
  readonly companyId: string
  readonly codeGroupId: string
  /** The gated u02 SYNTHETIC released family (never a real Q6 release). */
  readonly technicalFamilyId: string
  readonly authorizedActor: string
  readonly unauthorizedActor: string
  /** Supplied by the gated u02 fixture: the only actor of the run holding Product master Activate. */
  readonly activatorActor: string
}

type ActivationEnvironment = {
  readonly apiBaseUrl: string
  readonly unauthorizedBaseUrl: string
  readonly sqlConnection: string
  readonly seed: ActivationSeed
}

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/
/** The screen's attempt key: 128 random bits as 32 lowercase hex characters (commandAttempt.generateAttemptKey). */
const ATTEMPT_KEY = /^[0-9a-f]{32}$/

function required(name: string): string {
  const value = process.env[name]?.trim()
  if (!value) throw new Error(`${name} is not set; the u02 activation journey runs only inside the disposable stack.`)
  return value
}

function readEnvironment(): ActivationEnvironment {
  const raw = JSON.parse(required('BROWSER_E2E_U02_SEED')) as Record<string, unknown>
  const text = (key: keyof ActivationSeed, guid = false): string => {
    const value = raw[key]
    if (typeof value !== 'string' || value.trim() === '' || (guid && !GUID.test(value))) {
      throw new Error(`BROWSER_E2E_U02_SEED.${key} is missing or malformed.`)
    }
    return value
  }
  const seed: ActivationSeed = {
    companyId: text('companyId', true),
    codeGroupId: text('codeGroupId', true),
    technicalFamilyId: text('technicalFamilyId', true),
    authorizedActor: text('authorizedActor'),
    unauthorizedActor: text('unauthorizedActor'),
    activatorActor: text('activatorActor'),
  }
  if (new Set([seed.authorizedActor, seed.unauthorizedActor, seed.activatorActor]).size !== 3) {
    throw new Error('BROWSER_E2E_U02_SEED binds one actor to more than one persona.')
  }
  if (!/^u02-[a-z0-9-]+$/.test(seed.activatorActor)) {
    throw new Error('BROWSER_E2E_U02_SEED.activatorActor is not a disposable u02 actor.')
  }
  const apiBaseUrl = required('BROWSER_E2E_API_BASE_URL').replace(/\/$/, '')
  if (new URL(apiBaseUrl).hostname !== '127.0.0.1') {
    throw new Error(`BROWSER_E2E_API_BASE_URL must be the disposable loopback API, got ${apiBaseUrl}`)
  }
  return {
    apiBaseUrl,
    unauthorizedBaseUrl: required('BROWSER_E2E_UNAUTHORIZED_BASE_URL'),
    sqlConnection: required('BROWSER_E2E_SQL_CONNECTION'),
    seed,
  }
}

const DEVELOPMENT_USER_HEADER = 'X-Development-User'
const PRODUCTS_PATH = '/api/v1/products'
const CAPABILITIES_PATH = '/api/v1/shell/capabilities'
const BOOTSTRAP_ISSUER = 'urn:goymen:development-bootstrap'

/** identity ProductMasterOperation: Read = 1, DraftCreate = 2, DraftBasicEdit = 3, Activate = 4. */
const OPERATION = { read: 1, draftCreate: 2, draftBasicEdit: 3, activate: 4 } as const
/** product.Products.Lifecycle: Draft = 0, Active = 1. */
const LIFECYCLE = { draft: 0, active: 1 } as const

// The screen's own texts (src/product/productText.ts), asserted verbatim.
const ACTIVATE_BUTTON = 'Ürünü aktifleştir'
const ACTIVATE_SUBMITTING = 'Aktifleştiriliyor…'
const ACTIVATE_IMPACT = 'Aktifleştirme ürün kartını grup genelinde kesin işlemlerde kullanılabilir duruma getirir.'
const ACTIVATE_SUCCESS = 'Ürün aktifleştirildi.'
const HISTORY_ACTIVATION_LABEL = 'Ürün aktifleştirildi'
// The server's system label for the approval (ProductDraftOperation): no user-supplied reason is sent.
const ACTIVATION_REASON = 'Ürün kartı onayı'

/** The server-resolved labels the card read carries (ProductReader, ProductCardPresentation); the command card does not. */
interface ProductLabels {
  readonly group: string | null
  readonly class: string | null
  readonly family: string | null
  readonly primaryStockUnit: string | null
  readonly template?: string | null
  readonly familyFacts?: { readonly cins: string; readonly tur: string; readonly tip: string } | null
  readonly attributes?: Record<string, string> | null
  readonly units?: Record<string, string> | null
}

interface ProductCore {
  readonly id: string
  readonly code: string
  readonly name: string
  readonly groupId: string
  readonly classId: string | null
  readonly familyId: string | null
  readonly lifecycle: string
  readonly version: string
  readonly labels: ProductLabels
}

interface ProductCardBody {
  readonly product: ProductCore
  readonly references: {
    readonly templateId: string | null
    readonly templateVersion: string | null
    readonly attributes: Record<string, unknown> | null
    readonly units: Record<string, string | null>
  }
}

/** The parts of `GET /api/v1/products/reference-options` the precondition reads (as U02-FIX-01's form does). */
interface ReferenceOptions {
  readonly familyFacts: { id: string; templateId: string; templateVersion: string }[]
  readonly templates: {
    id: string
    version: string
    fields: { key: string; kind: string; role: string | null; dictionary: string | null; appliesWhenField: string | null }[]
  }[]
  readonly dictionaries: { name: string; entries: { code: string; status: string; isExplicitNone: boolean }[] }[]
  readonly units: { id: string; isKilogram: boolean }[]
}

interface MutationBody {
  readonly card: ProductCardBody
  readonly isReplay: boolean
}

interface HistoryEntry {
  readonly eventId: string
  readonly operation: string
  readonly occurredAtUtc: string
  readonly versionBefore: string | null
  readonly versionAfter: string
  readonly reason: string
  readonly changedFields: string[]
}

interface ProductHistory {
  readonly productId: string
  readonly entries: HistoryEntry[]
  readonly truncated: boolean
}

interface Problem {
  readonly code?: string
  readonly expectedVersion?: string
  readonly currentVersion?: string
}

interface ActivationEvidence {
  readonly lifecycle: number | null
  readonly version: string | null
  /** Every Product audit event of this card (the source of its history). */
  readonly auditEvents: number
  /** Its Activate audit events. */
  readonly activations: number
  /** Stored activation command terminals whose canonical response names this card. */
  readonly activationTerminals: number
}

interface ActivationAudit {
  readonly actorId: string
  readonly originCompanyId: string
  readonly permissionCode: string
  readonly eventType: string
  readonly keyHash: string
  readonly versionBefore: string
  readonly versionAfter: string
}

function nextVersion(version: string): string {
  return (BigInt(version) + 1n).toString()
}

/** The server's Idempotency-Key hash: SHA-256 of the UTF-8 key, uppercase hex (ProductCommandIntent.KeyHash). */
function keyHash(key: string): string {
  return createHash('sha256').update(key, 'utf8').digest('hex').toUpperCase()
}

function apiHeaders(actor: string, companyId: string): Record<string, string> {
  return { [DEVELOPMENT_USER_HEADER]: actor, 'X-Company-Id': companyId }
}

/** The headers the screen's command transport sends (api.ts sendCommand), for a named actor. */
function commandHeaders(actor: string, companyId: string, idempotencyKey: string): Record<string, string> {
  return { ...apiHeaders(actor, companyId), 'Content-Type': 'application/json', 'Idempotency-Key': idempotencyKey }
}

async function signInAs(page: Page, actor: string): Promise<void> {
  await page.setExtraHTTPHeaders({ [DEVELOPMENT_USER_HEADER]: actor })
}

function isCall(response: Response, method: 'GET' | 'POST', path: string): boolean {
  return response.request().method() === method && new URL(response.url()).pathname === path
}

/** Records the page's own Product requests (any method) until the returned stop is called. */
function watchProductRequests(page: Page): { readonly calls: string[]; stop: () => void } {
  const calls: string[] = []
  const watch = (request: BrowserRequest) => {
    const path = new URL(request.url()).pathname
    if (path.startsWith(PRODUCTS_PATH)) calls.push(`${request.method()} ${path}`)
  }
  page.on('request', watch)
  return { calls, stop: () => page.off('request', watch) }
}

async function productCapabilities(page: Page, env: ActivationEnvironment, actor: string): Promise<string[]> {
  const response = await page.request.get(`${env.apiBaseUrl}${CAPABILITIES_PATH}`, {
    headers: apiHeaders(actor, env.seed.companyId),
  })
  expect(response.status(), `capabilities of ${actor}`).toBe(200)
  const snapshot = (await response.json()) as { readonly capabilities?: unknown }
  const capabilities = Array.isArray(snapshot.capabilities) ? (snapshot.capabilities as string[]) : []
  return capabilities.filter((id) => id.startsWith('product.')).sort()
}

async function readHistory(page: Page, env: ActivationEnvironment, actor: string, productId: string): Promise<ProductHistory> {
  const response = await page.request.get(`${env.apiBaseUrl}${PRODUCTS_PATH}/${productId}/history`, {
    headers: apiHeaders(actor, env.seed.companyId),
  })
  expect(response.status()).toBe(200)
  return (await response.json()) as ProductHistory
}

async function readCard(page: Page, env: ActivationEnvironment, actor: string, productId: string): Promise<ProductCardBody> {
  const response = await page.request.get(`${env.apiBaseUrl}${PRODUCTS_PATH}/${productId}`, {
    headers: apiHeaders(actor, env.seed.companyId),
  })
  expect(response.status()).toBe(200)
  return (await response.json()) as ProductCardBody
}

/**
 * Closes whatever is open and opens the card with a fresh navigation (a fresh application: no remembered
 * attempt, no cached card). Both reads are armed before the navigation, so they can only match this visit.
 */
async function openCard(page: Page, productId: string): Promise<{ card: ProductCardBody; history: ProductHistory }> {
  await page.goto('about:blank')
  const cardRead = page.waitForResponse((response) => isCall(response, 'GET', `${PRODUCTS_PATH}/${productId}`))
  const historyRead = page.waitForResponse((response) => isCall(response, 'GET', `${PRODUCTS_PATH}/${productId}/history`))
  await page.goto(`/urun/${productId}`)
  const cardResponse = await cardRead
  expect(cardResponse.status(), 'the real card read').toBe(200)
  const historyResponse = await historyRead
  expect(historyResponse.status(), 'the real history read').toBe(200)
  return {
    card: (await cardResponse.json()) as ProductCardBody,
    history: (await historyResponse.json()) as ProductHistory,
  }
}

function lifecycleBadge(page: Page) {
  return page.locator('span[data-lifecycle]')
}

function historyItems(page: Page) {
  return page.getByTestId('product-history').getByRole('listitem')
}

/** The unit roles this run's Draft assigns (stock, purchase and sales: the kilogram, as U02-FIX-01). */
const ASSIGNED_UNIT_ROLES = ['stock', 'purchase', 'sales'] as const

/**
 * The card read's labels for every reference this run's Draft carries. Required, so the screen checks below
 * compare against real names: a null here would mean the card read itself could not resolve them.
 */
function requireFullLabels(card: ProductCardBody): ProductLabels {
  const { labels } = card.product
  expect(labels.family, 'the family label').toEqual(expect.any(String))
  expect(labels.primaryStockUnit, 'the primary stock unit label').toEqual(expect.any(String))
  expect(labels.template, 'the template label').toEqual(expect.any(String))
  expect(labels.familyFacts, 'the family facts').toEqual({
    cins: expect.any(String), tur: expect.any(String), tip: expect.any(String),
  })
  for (const role of ASSIGNED_UNIT_ROLES) expect(labels.units?.[role], `the ${role} unit label`).toEqual(expect.any(String))
  const attributeKeys = Object.keys(card.references.attributes ?? {})
  expect(attributeKeys.length, 'the technical attributes').toBeGreaterThan(0)
  return labels
}

/**
 * The card on screen shows the server-resolved names — never "ad okunamadı" (an id whose label is missing) or
 * "atanmadı" (nothing assigned) for a reference the card carries. #466 review P1-1: the activation answer's
 * card holds the group label only, so painting it would flip exactly these fields.
 */
async function expectFullLabels(page: Page, labels: ProductLabels, classText: string): Promise<void> {
  await expect(page.getByTestId('product-family')).toHaveText(labels.family ?? '')
  await expect(page.getByTestId('product-primary-stock-unit')).toHaveText(labels.primaryStockUnit ?? '')
  for (const role of ASSIGNED_UNIT_ROLES) {
    await expect(page.getByTestId(`product-unit-${role}`)).toHaveText(labels.units?.[role] ?? '')
  }
  await expect(definitionOf(page, 'Teknik şablon')).toHaveText(labels.template ?? '')
  await expect(definitionOf(page, 'Cins')).toHaveText(labels.familyFacts?.cins ?? '')
  await expect(definitionOf(page, 'Tür')).toHaveText(labels.familyFacts?.tur ?? '')
  await expect(definitionOf(page, 'Tip')).toHaveText(labels.familyFacts?.tip ?? '')
  for (const [key, label] of Object.entries(labels.attributes ?? {})) {
    await expect(definitionOf(page, key)).toHaveText(label)
  }
  // The class is not part of the synthetic release: whatever the card read showed, it still shows.
  await expect(page.getByTestId('product-class')).toHaveText(classText)
  await expect(page.getByText('ad okunamadı', { exact: true })).toHaveCount(0)
  await expect(page.getByText('Katalog adı okunamadı', { exact: true })).toHaveCount(0)
}

/** The card's `<dd>` for one `<dt>` term, matched exactly. */
function definitionOf(page: Page, term: string) {
  const exact = new RegExp(`^${term.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}$`)
  return page.locator('dt', { hasText: exact }).locator('xpath=following-sibling::dd[1]')
}

/* ── Read-only SQL evidence, scoped to this run's product and actors ─────────────────────────────────── */

/** mssql ships no types in this workspace (./mssql.d.ts, as purchasing-pur01). */
type SqlPool = any

async function readUserId(pool: SqlPool, actor: string): Promise<string> {
  const result = await pool.request()
    .input('issuer', sql.NVarChar(500), BOOTSTRAP_ISSUER)
    .input('subject', sql.NVarChar(500), actor)
    .query(`SELECT LOWER(CONVERT(nvarchar(36), e.UserId)) AS userId
            FROM identity.ExternalIdentities AS e
            WHERE e.Issuer = @issuer AND e.Subject = @subject`)
  expect(result.recordset, `exactly one Identity user for ${actor}`).toHaveLength(1)
  return result.recordset[0].userId as string
}

/** Every user holding an ACTIVE Product master Activate assignment in this database. */
async function readActivateGrantees(pool: SqlPool): Promise<string[]> {
  const result = await pool.request()
    .input('operation', sql.Int, OPERATION.activate)
    .query(`SELECT LOWER(CONVERT(nvarchar(36), g.UserId)) AS userId
            FROM identity.ProductMasterAssignments AS g
            WHERE g.Operation = @operation AND g.IsActive = 1
            ORDER BY userId`)
  return (result.recordset as { userId: string }[]).map((row) => row.userId)
}

async function readOperations(pool: SqlPool, userId: string): Promise<number[]> {
  const result = await pool.request()
    .input('userId', sql.UniqueIdentifier, userId)
    .query(`SELECT g.Operation AS operation
            FROM identity.ProductMasterAssignments AS g
            WHERE g.UserId = @userId AND g.IsActive = 1
            ORDER BY g.Operation`)
  return (result.recordset as { operation: number }[]).map((row) => row.operation)
}

async function readEvidence(pool: SqlPool, productId: string): Promise<ActivationEvidence> {
  const result = await pool.request()
    .input('productId', sql.UniqueIdentifier, productId)
    .input('entityId', sql.NVarChar(128), productId)
    .query(`SELECT
              (SELECT p.Lifecycle FROM product.Products AS p WHERE p.Id = @productId) AS lifecycle,
              (SELECT CONVERT(nvarchar(20), p.Version) FROM product.Products AS p WHERE p.Id = @productId) AS version,
              (SELECT COUNT(*) FROM audit.AuditEvents AS a
                WHERE a.SubjectKind = N'GroupSharedProduct' AND a.EntityType = N'Product.Product'
                  AND a.EntityId = @entityId) AS auditEvents,
              (SELECT COUNT(*) FROM audit.AuditEvents AS a
                WHERE a.SubjectKind = N'GroupSharedProduct' AND a.EntityType = N'Product.Product'
                  AND a.EntityId = @entityId AND a.Operation = N'Activate') AS activations,
              (SELECT COUNT(*) FROM idempotency.GroupSharedCommandTerminals AS t
                WHERE t.Subject = 'Product' AND t.CommandName = 'product.card.activate.v1'
                  AND JSON_VALUE(t.ResponseJson, '$.card.product.id') = @entityId) AS activationTerminals`)
  return result.recordset[0] as ActivationEvidence
}

async function readActivationAudit(pool: SqlPool, productId: string): Promise<ActivationAudit[]> {
  const result = await pool.request()
    .input('entityId', sql.NVarChar(128), productId)
    .query(`SELECT LOWER(a.ActorId) AS actorId,
                   LOWER(CONVERT(nvarchar(36), a.OriginCompanyId)) AS originCompanyId,
                   a.PermissionCode AS permissionCode,
                   a.EventType AS eventType,
                   a.IdempotencyKeyHash AS keyHash,
                   CONVERT(nvarchar(20), a.EntityVersionBefore) AS versionBefore,
                   CONVERT(nvarchar(20), a.EntityVersionAfter) AS versionAfter
            FROM audit.AuditEvents AS a
            WHERE a.SubjectKind = N'GroupSharedProduct' AND a.EntityType = N'Product.Product'
              AND a.EntityId = @entityId AND a.Operation = N'Activate'`)
  return result.recordset as ActivationAudit[]
}

async function readTerminalsByKey(pool: SqlPool, hash: string): Promise<number> {
  const result = await pool.request()
    .input('keyHash', sql.VarChar(64), hash)
    .query(`SELECT COUNT(*) AS terminals
            FROM idempotency.GroupSharedCommandTerminals AS t
            WHERE t.Subject = 'Product' AND t.CommandName = 'product.card.activate.v1'
              AND t.IdempotencyKeyHash = @keyHash`)
  return result.recordset[0].terminals as number
}

test('U01-360-ACT the disposable activator activates a Draft card in the browser; the server confirms, a fresh reopen shows Active with one activation row, the same key replays with nothing new, stale versions are refused, and non-activators get no control and 403', async ({ page }) => {
  test.setTimeout(240_000)
  const env = readEnvironment()
  const { companyId, codeGroupId, authorizedActor, unauthorizedActor, activatorActor } = env.seed
  const runTag = randomUUID().slice(0, 8)
  const pool = await new sql.ConnectionPool(env.sqlConnection).connect()
  try {
    // ── 0. Who holds what: the server's capability snapshots and the per-run database agree. ──────────────
    const activatorUserId = await test.step('only the disposable activator holds Product master Activate', async () => {
      const activatorId = await readUserId(pool, activatorActor)
      const authorizedId = await readUserId(pool, authorizedActor)
      const unauthorizedId = await readUserId(pool, unauthorizedActor)
      expect(await readActivateGrantees(pool), 'no real user and no seeded role holds Activate').toEqual([activatorId])
      expect(await readOperations(pool, activatorId)).toEqual([OPERATION.read, OPERATION.activate])
      expect(await readOperations(pool, authorizedId)).toEqual([OPERATION.read, OPERATION.draftCreate, OPERATION.draftBasicEdit])
      expect(await readOperations(pool, unauthorizedId)).toEqual([])

      expect(await productCapabilities(page, env, activatorActor))
        .toEqual(['product.items.activate', 'product.items.detail', 'product.items.list'])
      expect(await productCapabilities(page, env, authorizedActor))
        .toEqual(['product.items.create', 'product.items.detail', 'product.items.edit', 'product.items.list'])
      expect(await productCapabilities(page, env, unauthorizedActor)).toEqual([])
      return activatorId
    })

    // ── 1. Precondition: one Draft of this run through the authorized actor's real create and edit. ─────────
    // It carries the synthetic release's family, template, attributes and units — chosen from the real
    // reference-options read exactly as U02-FIX-01's form does — so every label the card read resolves is
    // on the card, and a screen that lost them after activation (#466 review P1-1) is visible.
    const draft = await test.step('arrange one labelled Draft card with a stale and a current version', async () => {
      const optionsRead = await page.request.get(
        `${env.apiBaseUrl}${PRODUCTS_PATH}/reference-options?groupId=${codeGroupId}`,
        { headers: apiHeaders(authorizedActor, companyId) },
      )
      expect(optionsRead.status(), 'the real reference-options read').toBe(200)
      const options = (await optionsRead.json()) as ReferenceOptions
      const family = options.familyFacts.find((item) => item.id === env.seed.technicalFamilyId)
      expect(family, 'the test-only released family comes from the real registry read').toBeDefined()
      const template = options.templates.find((item) => item.id === family!.templateId && item.version === family!.templateVersion)
      expect(template).toBeDefined()
      const attributes: Record<string, number | string> = {}
      for (const field of template!.fields.filter((item) => item.appliesWhenField === null)) {
        if (field.kind === 'measureMillimetre') {
          attributes[field.key] = field.role === 'thickness' ? 1.25 : 500
        } else if (field.role === 'quality' || field.role === 'coating') {
          const entries = options.dictionaries.find((dictionary) => dictionary.name === field.dictionary)?.entries ?? []
          const entry = entries.find((item) => item.status === 'approved' && (field.role !== 'coating' || item.isExplicitNone))
          expect(entry, `an approved ${field.key} value`).toBeDefined()
          attributes[field.key] = entry!.code
        }
      }
      const kilogram = options.units.find((unit) => unit.isKilogram)
      expect(kilogram).toBeDefined()

      const name = `SENTETIK u02 aktivasyon adayi ${runTag}`
      const created = await page.request.post(`${env.apiBaseUrl}${PRODUCTS_PATH}`, {
        headers: commandHeaders(authorizedActor, companyId, `u02-activation-draft-${runTag}`),
        data: {
          name,
          groupId: codeGroupId,
          familyId: env.seed.technicalFamilyId,
          references: {
            templateId: template!.id,
            templateVersion: template!.version,
            attributes,
            units: { stock: kilogram!.id, purchase: kilogram!.id, sales: kilogram!.id },
          },
        },
      })
      expect(created.status(), 'the real create').toBe(201)
      const createdBody = (await created.json()) as MutationBody
      expect(createdBody.isReplay).toBe(false)
      expect(createdBody.card.product).toMatchObject({
        name, groupId: codeGroupId, familyId: env.seed.technicalFamilyId, lifecycle: 'Draft',
      })
      expect(createdBody.card.references.attributes).toEqual(attributes)
      const id = createdBody.card.product.id
      expect(id).toMatch(GUID)

      const editedName = `${name} duzeltildi`
      const edited = await page.request.patch(`${env.apiBaseUrl}${PRODUCTS_PATH}/${id}/draft`, {
        headers: commandHeaders(authorizedActor, companyId, `u02-activation-edit-${runTag}`),
        data: {
          expectedVersion: createdBody.card.product.version,
          changes: { name: editedName },
          reason: 'SENTETIK u02 aktivasyon oncesi ad duzeltmesi',
        },
      })
      expect(edited.status(), 'the real basic edit').toBe(200)
      const editedBody = (await edited.json()) as MutationBody
      expect(editedBody.card.product).toMatchObject({
        id, name: editedName, lifecycle: 'Draft', version: nextVersion(createdBody.card.product.version),
      })
      return {
        id,
        code: editedBody.card.product.code,
        name: editedName,
        staleVersion: createdBody.card.product.version,
        version: editedBody.card.product.version,
      }
    })
    const cardPath = `${PRODUCTS_PATH}/${draft.id}`
    const activationPath = `${cardPath}/activation`
    const activationUrl = `${env.apiBaseUrl}${activationPath}`
    const activatedVersion = nextVersion(draft.version)
    const beforeActivation: ActivationEvidence = {
      lifecycle: LIFECYCLE.draft, version: draft.version, auditEvents: 2, activations: 0, activationTerminals: 0,
    }
    expect(await readEvidence(pool, draft.id)).toEqual(beforeActivation)

    // ── 2. The authorized actor (no Activate): its edit link, no activation control, a 403 command. ────────
    const fullCard = await test.step('the authorized actor sees no activation control and its command is 403', async () => {
      await signInAs(page, authorizedActor)
      const traffic = watchProductRequests(page)
      const opened = await openCard(page, draft.id)
      expect(opened.card.product).toMatchObject({ id: draft.id, lifecycle: 'Draft', version: draft.version })
      await expect(page.getByRole('heading', { level: 1, name: draft.name })).toBeVisible()
      // The baseline: the real card read's names for every reference this Draft carries.
      const labels = requireFullLabels(opened.card)
      const classText = (await page.getByTestId('product-class').textContent()) ?? ''
      await expectFullLabels(page, labels, classText)
      await expect(lifecycleBadge(page)).toHaveAttribute('data-lifecycle', 'Draft')
      // Its own edit capability is rendered, so the shell snapshot is in: the missing control is a decision.
      await expect(page.locator(`a[href="/urun/${draft.id}/duzenle"]`)).toBeVisible()
      await expect(page.getByRole('button', { name: ACTIVATE_BUTTON })).toHaveCount(0)
      await expect(page.getByText(ACTIVATE_IMPACT)).toHaveCount(0)
      traffic.stop()
      expect(traffic.calls.filter((call) => call.startsWith('POST'))).toEqual([])

      const denied = await page.request.post(activationUrl, {
        headers: commandHeaders(authorizedActor, companyId, `u02-activation-denied-authorized-${runTag}`),
        data: { expectedVersion: draft.version },
      })
      expect(denied.status(), 'Activate is not the authorized actor\'s grant').toBe(403)
      const deniedText = await denied.text()
      expect(deniedText).not.toContain(draft.name)
      expect(deniedText).not.toContain(draft.code)
      expect(await readEvidence(pool, draft.id)).toEqual(beforeActivation)
      return { labels, classText }
    })

    // ── 3. The unauthorized actor gets nothing: no card screen, no Product request, a 403 command. ─────────
    await test.step('the unauthorized actor is closed out and its command is 403', async () => {
      await signInAs(page, unauthorizedActor)
      const traffic = watchProductRequests(page)
      await page.goto(new URL(`/urun/${draft.id}`, env.unauthorizedBaseUrl).toString())
      await expect(page.locator('[data-state="not-found"]')).toBeVisible({ timeout: 30_000 })
      await expect(page.getByRole('button', { name: ACTIVATE_BUTTON })).toHaveCount(0)
      traffic.stop()
      expect(traffic.calls).toEqual([])

      const denied = await page.request.post(activationUrl, {
        headers: commandHeaders(unauthorizedActor, companyId, `u02-activation-denied-unauthorized-${runTag}`),
        data: { expectedVersion: draft.version },
      })
      expect(denied.status()).toBe(403)
      const deniedText = await denied.text()
      expect(deniedText).not.toContain(draft.name)
      expect(deniedText).not.toContain(draft.code)
      const deniedRead = await page.request.get(`${env.apiBaseUrl}${cardPath}`, {
        headers: apiHeaders(unauthorizedActor, companyId),
      })
      expect(deniedRead.status()).toBe(403)
      expect(await deniedRead.text()).not.toContain(draft.name)
      expect(await readEvidence(pool, draft.id)).toEqual(beforeActivation)
    })

    // ── 4. A stale expected version is refused before anything is written. ────────────────────────────────
    await test.step('the activator\'s stale expected version is refused', async () => {
      const stale = await page.request.post(activationUrl, {
        headers: commandHeaders(activatorActor, companyId, `u02-activation-stale-${runTag}`),
        data: { expectedVersion: draft.staleVersion },
      })
      expect(stale.status()).toBe(409)
      expect((await stale.json()) as Problem).toMatchObject({
        code: 'version_conflict', expectedVersion: draft.staleVersion, currentVersion: draft.version,
      })
      expect(await readEvidence(pool, draft.id)).toEqual(beforeActivation)
    })

    // ── 5. The activator activates in the browser; the screen waits for the server. ───────────────────────
    const activation = await test.step('the activator activates the Draft card and the server confirms it', async () => {
      await signInAs(page, activatorActor)
      const opened = await openCard(page, draft.id)
      expect(opened.card.product).toMatchObject({ id: draft.id, lifecycle: 'Draft', version: draft.version })
      expect(opened.card.product.labels).toEqual(fullCard.labels)
      expect(opened.history.entries.map((entry) => entry.operation)).toEqual(['DraftBasicEdit', 'DraftCreate'])
      await expect(page.getByRole('heading', { level: 1, name: draft.name })).toBeVisible()
      await expectFullLabels(page, fullCard.labels, fullCard.classText)
      await expect(lifecycleBadge(page)).toHaveAttribute('data-lifecycle', 'Draft')
      await expect(lifecycleBadge(page)).toHaveText('Taslak')
      const activate = page.getByRole('button', { name: ACTIVATE_BUTTON, exact: true })
      await expect(activate).toBeVisible()
      await expect(activate).toBeEnabled()
      await expect(page.getByText(ACTIVATE_IMPACT)).toBeVisible()
      // The activator holds no DraftBasicEdit: the card offers it no edit.
      await expect(page.locator(`a[href="/urun/${draft.id}/duzenle"]`)).toHaveCount(0)
      await expect(historyItems(page)).toHaveCount(2)

      // The real request goes to the real server as the activator (route.fetch does not carry the page's extra
      // header, so the actor is named exactly as signInAs does). Only the delivery of its answer is held.
      const held: { response?: APIResponse } = {}
      let release: () => void = () => undefined
      const delivered = new Promise<void>((resolve) => { release = resolve })
      const pattern = `**${activationPath}`
      await page.route(pattern, async (route) => {
        if (route.request().method() !== 'POST' || held.response !== undefined) return route.fallback()
        held.response = await route.fetch({
          headers: { ...route.request().headers(), [DEVELOPMENT_USER_HEADER.toLowerCase()]: activatorActor },
        })
        await delivered
        await route.fulfill({ response: held.response })
      })
      try {
        const answer = page.waitForResponse((response) => isCall(response, 'POST', activationPath))
        await activate.click()
        await expect.poll(() => held.response?.status() ?? null, { timeout: 30_000 }).toBe(200)

        // The server has committed; the page has not heard it yet. No optimistic Active.
        expect(await readEvidence(pool, draft.id)).toMatchObject({ lifecycle: LIFECYCLE.active, activations: 1 })
        await expect(page.getByRole('button', { name: ACTIVATE_SUBMITTING, exact: true })).toBeDisabled()
        await expect(lifecycleBadge(page)).toHaveAttribute('data-lifecycle', 'Draft')
        await expect(lifecycleBadge(page)).toHaveText('Taslak')
        await expect(page.getByRole('status').filter({ hasText: ACTIVATE_SUCCESS })).toHaveCount(0)
        await expectFullLabels(page, fullCard.labels, fullCard.classText)

        // From here on, every card read the page makes is recorded: the confirmed answer must be followed by
        // exactly one real re-read of the card, and the screen's Active and labels come from that read.
        const afterAnswer = watchProductRequests(page)
        const reRead = page.waitForResponse((response) => isCall(response, 'GET', cardPath))
        release()
        const response = await answer
        expect(response.status()).toBe(200)
        expect(response.headers()['idempotency-replayed']).not.toBe('true')
        const sent = response.request()
        const idempotencyKey = (await sent.headerValue('Idempotency-Key')) ?? ''
        expect(idempotencyKey).toMatch(ATTEMPT_KEY)
        expect(await sent.headerValue('X-Company-Id')).toBe(companyId)
        const sentBody = sent.postData() ?? ''
        // No lifecycle, completeness or reason claim: the expected version the screen read, and nothing else.
        expect(JSON.parse(sentBody)).toEqual({ expectedVersion: draft.version })
        const body = (await response.json()) as MutationBody
        expect(body.isReplay).toBe(false)
        expect(body.card.product).toMatchObject({
          id: draft.id, code: draft.code, name: draft.name, lifecycle: 'Active', version: activatedVersion,
        })

        // The confirmed answer is followed by the real card read (#466 review P1-1): Active and every label
        // the card carries come from that read, never from the command's own (group-label-only) card.
        const reReadResponse = await reRead
        expect(reReadResponse.status(), 'the card is re-read after the confirmed activation').toBe(200)
        const reReadCard = (await reReadResponse.json()) as ProductCardBody
        expect(reReadCard.product).toMatchObject({ id: draft.id, lifecycle: 'Active', version: activatedVersion })
        expect(reReadCard.product.labels).toEqual(fullCard.labels)

        // Right after activation, before any close/reopen: the server's Active, and the full card.
        await expect(page.getByRole('status').filter({ hasText: ACTIVATE_SUCCESS })).toHaveText(ACTIVATE_SUCCESS)
        await expect(lifecycleBadge(page)).toHaveAttribute('data-lifecycle', 'Active')
        await expect(lifecycleBadge(page)).toHaveText('Aktif')
        await expect(page.getByRole('button', { name: ACTIVATE_BUTTON })).toHaveCount(0)
        await expectFullLabels(page, fullCard.labels, fullCard.classText)
        afterAnswer.stop()
        expect(afterAnswer.calls.filter((call) => call === `GET ${cardPath}`), 'exactly one card re-read').toHaveLength(1)
        return { idempotencyKey, sentBody, body }
      } finally {
        release()
        await page.unroute(pattern)
      }
    })
    const afterActivation: ActivationEvidence = {
      lifecycle: LIFECYCLE.active, version: activatedVersion, auditEvents: 3, activations: 1, activationTerminals: 1,
    }
    const activationAudit: ActivationAudit = {
      actorId: activatorUserId,
      originCompanyId: companyId,
      permissionCode: 'product.card.activate',
      eventType: 'product.card.activated.v1',
      keyHash: keyHash(activation.idempotencyKey),
      versionBefore: draft.version,
      versionAfter: activatedVersion,
    }
    expect(await readEvidence(pool, draft.id)).toEqual(afterActivation)
    // The one activation is the activator's, from GDC, under the browser's own key.
    expect(await readActivationAudit(pool, draft.id)).toEqual([activationAudit])
    expect(await readTerminalsByKey(pool, keyHash(activation.idempotencyKey))).toBe(1)

    // ── 6. Close and reopen: Active from the server, exactly one activation row. ──────────────────────────
    const expectOneActivation = async (label: string) => {
      const reopened = await openCard(page, draft.id)
      expect(reopened.card.product, label).toMatchObject({
        id: draft.id, code: draft.code, name: draft.name, lifecycle: 'Active', version: activatedVersion,
      })
      const history = reopened.history
      expect(history.productId).toBe(draft.id)
      expect(history.truncated).toBe(false)
      expect(history.entries.map((entry) => entry.operation), label).toEqual(['Activate', 'DraftBasicEdit', 'DraftCreate'])
      expect(history.entries.filter((entry) => entry.operation === 'Activate')).toHaveLength(1)
      expect(history.entries[0]).toMatchObject({
        operation: 'Activate',
        versionBefore: draft.version,
        versionAfter: activatedVersion,
        reason: ACTIVATION_REASON,
        changedFields: ['lifecycle'],
      })
      expect(history.entries[1]).toMatchObject({ versionBefore: draft.staleVersion, versionAfter: draft.version })
      expect(JSON.stringify(history)).not.toMatch(/actor|clientIp|userAgent|correlation|causation|idempotency/i)

      expect(reopened.card.product.labels, label).toEqual(fullCard.labels)
      await expect(page.getByRole('heading', { level: 1, name: draft.name })).toBeVisible()
      await expect(lifecycleBadge(page)).toHaveAttribute('data-lifecycle', 'Active')
      await expect(lifecycleBadge(page)).toHaveText('Aktif')
      await expect(page.getByText('Yeni işlemlerde seçilebilir.', { exact: true })).toBeVisible()
      await expectFullLabels(page, fullCard.labels, fullCard.classText)
      // A fresh application: no remembered success, no control on a card that is no longer a Draft.
      await expect(page.getByRole('button', { name: ACTIVATE_BUTTON })).toHaveCount(0)
      await expect(page.getByRole('status').filter({ hasText: ACTIVATE_SUCCESS })).toHaveCount(0)
      await expect(page.getByTestId('product-activation-unknown')).toHaveCount(0)
      await expect(historyItems(page)).toHaveCount(3)
      const activationItems = historyItems(page).filter({ hasText: HISTORY_ACTIVATION_LABEL })
      await expect(activationItems).toHaveCount(1)
      await expect(historyItems(page).nth(0)).toContainText(HISTORY_ACTIVATION_LABEL)
      await expect(historyItems(page).nth(0)).toContainText(`Sürüm ${draft.version} → ${activatedVersion}`)
      await expect(historyItems(page).nth(0)).toContainText(`Gerekçe: ${ACTIVATION_REASON}`)
      await expect(historyItems(page).nth(0)).toContainText('Değişen alanlar: Durum')
    }
    await test.step('close and reopen: the server says Active and the history holds one activation', async () => {
      await expectOneActivation('first reopen')
      expect(await readEvidence(pool, draft.id)).toEqual(afterActivation)
    })

    // ── 7. Replay and refusals: nothing new is written. ───────────────────────────────────────────────────
    await test.step('the same key replays with no second row and no version bump; stale and foreign sends are refused', async () => {
      const replay = await page.request.post(activationUrl, {
        headers: commandHeaders(activatorActor, companyId, activation.idempotencyKey),
        data: activation.sentBody,
      })
      expect(replay.status()).toBe(200)
      expect(replay.headers()['idempotency-replayed']).toBe('true')
      const replayed = (await replay.json()) as MutationBody
      expect(replayed.isReplay).toBe(true)
      expect(replayed.card.product).toMatchObject({
        id: draft.id, code: draft.code, name: draft.name, lifecycle: 'Active', version: activatedVersion,
      })
      expect(replayed.card.product.version).toBe(activation.body.card.product.version)
      expect(await readEvidence(pool, draft.id)).toEqual(afterActivation)

      // The same key under a changed body is another request, not a replay.
      const changed = await page.request.post(activationUrl, {
        headers: commandHeaders(activatorActor, companyId, activation.idempotencyKey),
        data: { expectedVersion: activatedVersion },
      })
      expect(changed.status()).toBe(409)
      expect(((await changed.json()) as Problem).code).toBe('idempotency_conflict')

      // A new attempt with the version the activation consumed: the card is no longer a Draft.
      const staleAfter = await page.request.post(activationUrl, {
        headers: commandHeaders(activatorActor, companyId, `u02-activation-stale-after-${runTag}`),
        data: { expectedVersion: draft.version },
      })
      expect(staleAfter.status()).toBe(409)
      expect(((await staleAfter.json()) as Problem).code).toBe('lifecycle_conflict')

      // The activator's exact request from the authorized actor: authority is checked before any replay.
      const foreign = await page.request.post(activationUrl, {
        headers: commandHeaders(authorizedActor, companyId, activation.idempotencyKey),
        data: activation.sentBody,
      })
      expect(foreign.status()).toBe(403)
      expect(foreign.headers()['idempotency-replayed']).toBeUndefined()
      const foreignText = await foreign.text()
      expect(foreignText).not.toContain(draft.name)
      expect(foreignText).not.toContain(draft.code)

      const history = await readHistory(page, env, activatorActor, draft.id)
      expect(history.entries.map((entry) => entry.operation)).toEqual(['Activate', 'DraftBasicEdit', 'DraftCreate'])
      expect((await readCard(page, env, activatorActor, draft.id)).product)
        .toMatchObject({ lifecycle: 'Active', version: activatedVersion })
      expect(await readEvidence(pool, draft.id)).toEqual(afterActivation)
      expect(await readActivationAudit(pool, draft.id)).toEqual([activationAudit])
      expect(await readTerminalsByKey(pool, keyHash(activation.idempotencyKey))).toBe(1)
    })

    await test.step('a last reopen still shows exactly one activation', async () => {
      await expectOneActivation('reopen after the replay')
      expect(await readActivateGrantees(pool)).toEqual([activatorUserId])
    })
  } finally {
    await pool.close()
  }
})
