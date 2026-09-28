import { createHash, randomUUID } from 'node:crypto'
import { expect, test, type Page, type Request as BrowserRequest, type Response } from '@playwright/test'
import sql from 'mssql'

/**
 * U-01-360 deactivation and reactivation (DEC-CAR-003, PRD-SCOPE-001 #98 §7.9, PRD-PERM-001 #99) on the REAL API
 * and UI of the u02 disposable stack. Nothing is mocked or intercepted.
 *
 * Authority: the disposable u02 lifecycle actor of this per-run database (the u02 fixture's `activatorActor`)
 * holds Product master Read + Activate + Deactivate + Reactivate, each its own explicit assignment issued by the
 * suite's synthetic issuer. The authorized actor (Read / DraftCreate / DraftBasicEdit) and the unauthorized actor
 * hold neither move. Measured, not assumed: capability snapshots and read-only SQL over the assignments.
 *
 * Journey, one card of this run:
 *  1. Arrange: the authorized actor creates a Draft, the lifecycle actor approves it through the real command.
 *  2. The authorized and the unauthorized actor get no control and a 403 from the command.
 *  3. The lifecycle actor opens the Active card, is refused a blank reason on the client, then deactivates with a
 *     reason. The screen shows Pasif, when and why only after the server's re-read.
 *  4. Close and reopen: Inactive from the server; the history's newest row is the deactivation with that reason;
 *     the history carries no actor or request metadata (its keys are parsed, never substring-matched).
 *  5. SQL: one Deactivate audit event by the lifecycle actor, SourceChannel Web, the reason, one terminal.
 *  6. Replay with the same key and body answers the stored result and writes nothing; the same key with another
 *     reason is an idempotency_conflict; a stale reactivation is a version_conflict; a second deactivation is a
 *     lifecycle_conflict; the authorized actor sending the exact request is 403.
 *  7. New selection refuses it: the Active list no longer holds it while the Inactive list does (Product's own
 *     operational read side; the Inventory/Sales ports are proven on real SQL by
 *     ProductDeactivationSqlServerRelationalAcceptanceTests).
 *  8. The lifecycle actor reactivates it in the browser with its own reason: Aktif after the re-read.
 */

type LifecycleSeed = {
  readonly companyId: string
  readonly codeGroupId: string
  readonly authorizedActor: string
  readonly unauthorizedActor: string
  /** The disposable u02 actor holding the lifecycle grants (the fixture names it activatorActor). */
  readonly activatorActor: string
}

type LifecycleEnvironment = {
  readonly apiBaseUrl: string
  readonly unauthorizedBaseUrl: string
  readonly sqlConnection: string
  readonly seed: LifecycleSeed
}

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/
const ATTEMPT_KEY = /^[0-9a-f]{32}$/

function required(name: string): string {
  const value = process.env[name]?.trim()
  if (!value) throw new Error(`${name} is not set; the u02 deactivation journey runs only inside the disposable stack.`)
  return value
}

function readEnvironment(): LifecycleEnvironment {
  const raw = JSON.parse(required('BROWSER_E2E_U02_SEED')) as Record<string, unknown>
  const text = (key: keyof LifecycleSeed, guid = false): string => {
    const value = raw[key]
    if (typeof value !== 'string' || value.trim() === '' || (guid && !GUID.test(value))) {
      throw new Error(`BROWSER_E2E_U02_SEED.${key} is missing or malformed.`)
    }
    return value
  }
  const seed: LifecycleSeed = {
    companyId: text('companyId', true),
    codeGroupId: text('codeGroupId', true),
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

/** identity ProductMasterOperation: Read = 1, DraftCreate = 2, DraftBasicEdit = 3, Activate = 4, Deactivate = 5, Reactivate = 6. */
const OPERATION = { read: 1, draftCreate: 2, draftBasicEdit: 3, activate: 4, deactivate: 5, reactivate: 6 } as const
/** product.Products.Lifecycle: Draft = 0, Active = 1, Inactive = 2. */
const LIFECYCLE = { active: 1, inactive: 2 } as const

// The screen's own texts (src/product/productText.ts), asserted verbatim.
const DEACTIVATE_OPEN = 'Ürünü pasife al'
const DEACTIVATE_SUBMIT = 'Pasife al'
const DEACTIVATE_SUCCESS = 'Ürün pasife alındı.'
const REACTIVATE_OPEN = 'Ürünü yeniden aktifleştir'
const REACTIVATE_SUBMIT = 'Yeniden aktifleştir'
const REACTIVATE_SUCCESS = 'Ürün yeniden aktifleştirildi.'
const REASON_LABEL = 'Gerekçe'
const REASON_REQUIRED = 'Gerekçe zorunludur.'
const HISTORY_DEACTIVATION_LABEL = 'Ürün pasife alındı'
const HISTORY_REACTIVATION_LABEL = 'Ürün yeniden aktifleştirildi'
/** The wire shape of one history entry (ProductHistoryEntryView): what, when, the step and the reason. Never who. */
const HISTORY_ENTRY_KEYS = ['changedFields', 'eventId', 'occurredAtUtc', 'operation', 'reason', 'versionAfter', 'versionBefore']

interface ProductCore {
  readonly id: string
  readonly code: string
  readonly name: string
  readonly lifecycle: string
  readonly version: string
}

interface MutationBody {
  readonly card: { readonly product: ProductCore }
  readonly isReplay: boolean
}

interface HistoryEntry {
  readonly operation: string
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

interface LifecycleEvidence {
  readonly lifecycle: number | null
  readonly version: string | null
  readonly deactivations: number
  readonly reactivations: number
  readonly deactivationTerminals: number
}

interface LifecycleAudit {
  readonly actorId: string
  readonly originCompanyId: string
  readonly permissionCode: string
  readonly eventType: string
  readonly sourceChannel: string
  readonly reason: string
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

function commandHeaders(actor: string, companyId: string, idempotencyKey: string): Record<string, string> {
  return { ...apiHeaders(actor, companyId), 'Content-Type': 'application/json', 'Idempotency-Key': idempotencyKey }
}

async function signInAs(page: Page, actor: string): Promise<void> {
  await page.setExtraHTTPHeaders({ [DEVELOPMENT_USER_HEADER]: actor })
}

function isCall(response: Response, method: 'GET' | 'POST', path: string): boolean {
  return response.request().method() === method && new URL(response.url()).pathname === path
}

function watchProductRequests(page: Page): { readonly calls: string[]; stop: () => void } {
  const calls: string[] = []
  const watch = (request: BrowserRequest) => {
    const path = new URL(request.url()).pathname
    if (path.startsWith(PRODUCTS_PATH)) calls.push(`${request.method()} ${path}`)
  }
  page.on('request', watch)
  return { calls, stop: () => page.off('request', watch) }
}

async function productCapabilities(page: Page, env: LifecycleEnvironment, actor: string): Promise<string[]> {
  const response = await page.request.get(`${env.apiBaseUrl}${CAPABILITIES_PATH}`, {
    headers: apiHeaders(actor, env.seed.companyId),
  })
  expect(response.status(), `capabilities of ${actor}`).toBe(200)
  const snapshot = (await response.json()) as { readonly capabilities?: unknown }
  const capabilities = Array.isArray(snapshot.capabilities) ? (snapshot.capabilities as string[]) : []
  return capabilities.filter((id) => id.startsWith('product.')).sort()
}

async function listIds(page: Page, env: LifecycleEnvironment, actor: string, lifecycle: string): Promise<string[]> {
  const response = await page.request.get(`${env.apiBaseUrl}${PRODUCTS_PATH}?lifecycle=${lifecycle}&pageSize=100`, {
    headers: apiHeaders(actor, env.seed.companyId),
  })
  expect(response.status()).toBe(200)
  const body = (await response.json()) as { readonly items: { readonly id: string }[] }
  return body.items.map((item) => item.id)
}

async function openCard(page: Page, productId: string): Promise<{ card: { product: ProductCore }; history: ProductHistory }> {
  await page.goto('about:blank')
  const cardRead = page.waitForResponse((response) => isCall(response, 'GET', `${PRODUCTS_PATH}/${productId}`))
  const historyRead = page.waitForResponse((response) => isCall(response, 'GET', `${PRODUCTS_PATH}/${productId}/history`))
  await page.goto(`/urun/${productId}`)
  const cardResponse = await cardRead
  expect(cardResponse.status(), 'the real card read').toBe(200)
  const historyResponse = await historyRead
  expect(historyResponse.status(), 'the real history read').toBe(200)
  return {
    card: (await cardResponse.json()) as { product: ProductCore },
    history: (await historyResponse.json()) as ProductHistory,
  }
}

function lifecycleBadge(page: Page) {
  return page.locator('span[data-lifecycle]')
}

function historyItems(page: Page) {
  return page.getByTestId('product-history').getByRole('listitem')
}

/* ── Read-only SQL evidence, scoped to this run's product and actors ─────────────────────────────────── */

/** mssql ships no types in this workspace (./mssql.d.ts). */
type SqlPool = any

async function readUserId(pool: SqlPool, actor: string): Promise<string> {
  const result = await pool.request()
    .input('issuer', sql.NVarChar(500), BOOTSTRAP_ISSUER)
    .input('subject', sql.NVarChar(500), actor)
    .query(`SELECT LOWER(CONVERT(nvarchar(36), e.UserId)) AS userId
            FROM [identity].[ExternalIdentities] AS e
            WHERE e.Issuer = @issuer AND e.Subject = @subject`)
  expect(result.recordset, `exactly one Identity user for ${actor}`).toHaveLength(1)
  return result.recordset[0].userId as string
}

async function readOperations(pool: SqlPool, userId: string): Promise<number[]> {
  const result = await pool.request()
    .input('userId', sql.UniqueIdentifier, userId)
    .query(`SELECT g.Operation AS operation
            FROM [identity].[ProductMasterAssignments] AS g
            WHERE g.UserId = @userId AND g.IsActive = 1
            ORDER BY g.Operation`)
  return (result.recordset as { operation: number }[]).map((row) => row.operation)
}

/** Every user holding an ACTIVE Product master assignment for one operation in this database. */
async function readGrantees(pool: SqlPool, operation: number): Promise<string[]> {
  const result = await pool.request()
    .input('operation', sql.Int, operation)
    .query(`SELECT LOWER(CONVERT(nvarchar(36), g.UserId)) AS userId
            FROM [identity].[ProductMasterAssignments] AS g
            WHERE g.Operation = @operation AND g.IsActive = 1
            ORDER BY userId`)
  return (result.recordset as { userId: string }[]).map((row) => row.userId)
}

async function readEvidence(pool: SqlPool, productId: string): Promise<LifecycleEvidence> {
  const result = await pool.request()
    .input('productId', sql.UniqueIdentifier, productId)
    .input('entityId', sql.NVarChar(128), productId)
    .query(`SELECT
              (SELECT p.Lifecycle FROM [product].[Products] AS p WHERE p.Id = @productId) AS lifecycle,
              (SELECT CONVERT(nvarchar(20), p.Version) FROM [product].[Products] AS p WHERE p.Id = @productId) AS version,
              (SELECT COUNT(*) FROM [audit].[AuditEvents] AS a
                WHERE a.SubjectKind = N'GroupSharedProduct' AND a.EntityType = N'Product.Product'
                  AND a.EntityId = @entityId AND a.Operation = N'Deactivate') AS deactivations,
              (SELECT COUNT(*) FROM [audit].[AuditEvents] AS a
                WHERE a.SubjectKind = N'GroupSharedProduct' AND a.EntityType = N'Product.Product'
                  AND a.EntityId = @entityId AND a.Operation = N'Reactivate') AS reactivations,
              (SELECT COUNT(*) FROM [idempotency].[GroupSharedCommandTerminals] AS t
                WHERE t.Subject = 'Product' AND t.CommandName = 'product.card.deactivate.v1'
                  AND JSON_VALUE(t.ResponseJson, '$.card.product.id') = @entityId) AS deactivationTerminals`)
  return result.recordset[0] as LifecycleEvidence
}

async function readLifecycleAudit(pool: SqlPool, productId: string, operation: 'Deactivate' | 'Reactivate'): Promise<LifecycleAudit[]> {
  const result = await pool.request()
    .input('entityId', sql.NVarChar(128), productId)
    .input('operation', sql.NVarChar(64), operation)
    .query(`SELECT LOWER(a.ActorId) AS actorId,
                   LOWER(CONVERT(nvarchar(36), a.OriginCompanyId)) AS originCompanyId,
                   a.PermissionCode AS permissionCode,
                   a.EventType AS eventType,
                   a.SourceChannel AS sourceChannel,
                   a.Reason AS reason,
                   a.IdempotencyKeyHash AS keyHash,
                   CONVERT(nvarchar(20), a.EntityVersionBefore) AS versionBefore,
                   CONVERT(nvarchar(20), a.EntityVersionAfter) AS versionAfter
            FROM [audit].[AuditEvents] AS a
            WHERE a.SubjectKind = N'GroupSharedProduct' AND a.EntityType = N'Product.Product'
              AND a.EntityId = @entityId AND a.Operation = @operation`)
  return result.recordset as LifecycleAudit[]
}

test('U01-360-DEACT the lifecycle actor takes an Active card out of new use with a reason in the browser; a fresh reopen shows Pasif with when and why and one deactivation row, the same key replays with nothing new, stale and wrong-state sends are refused, the Active list drops it, and it is reactivated with its own reason', async ({ page }) => {
  test.setTimeout(240_000)
  const env = readEnvironment()
  const { companyId, codeGroupId, authorizedActor, unauthorizedActor, activatorActor: lifecycleActor } = env.seed
  const runTag = randomUUID().slice(0, 8)
  const pool = await new sql.ConnectionPool(env.sqlConnection).connect()
  try {
    // ── 0. Who holds what. ──────────────────────────────────────────────────────────────────────────────
    const lifecycleUserId = await test.step('only the disposable lifecycle actor holds Deactivate and Reactivate', async () => {
      const lifecycleId = await readUserId(pool, lifecycleActor)
      const authorizedId = await readUserId(pool, authorizedActor)
      const unauthorizedId = await readUserId(pool, unauthorizedActor)
      expect(await readGrantees(pool, OPERATION.deactivate)).toEqual([lifecycleId])
      expect(await readGrantees(pool, OPERATION.reactivate)).toEqual([lifecycleId])
      expect(await readOperations(pool, lifecycleId))
        .toEqual([OPERATION.read, OPERATION.activate, OPERATION.deactivate, OPERATION.reactivate])
      expect(await readOperations(pool, authorizedId)).toEqual([OPERATION.read, OPERATION.draftCreate, OPERATION.draftBasicEdit])
      expect(await readOperations(pool, unauthorizedId)).toEqual([])
      expect(await productCapabilities(page, env, lifecycleActor)).toEqual([
        'product.items.activate', 'product.items.deactivate', 'product.items.detail', 'product.items.list',
        'product.items.reactivate',
      ])
      expect(await productCapabilities(page, env, authorizedActor))
        .toEqual(['product.items.create', 'product.items.detail', 'product.items.edit', 'product.items.list'])
      expect(await productCapabilities(page, env, unauthorizedActor)).toEqual([])
      return lifecycleId
    })

    // ── 1. Arrange: an Active card of this run, through the real create and approval commands. ─────────────
    const active = await test.step('arrange one Active card', async () => {
      const name = `SENTETIK u02 pasife alma adayi ${runTag}`
      const created = await page.request.post(`${env.apiBaseUrl}${PRODUCTS_PATH}`, {
        headers: commandHeaders(authorizedActor, companyId, `u02-deactivation-draft-${runTag}`),
        data: { name, groupId: codeGroupId },
      })
      expect(created.status(), 'the real create').toBe(201)
      const draft = ((await created.json()) as MutationBody).card.product
      expect(draft.id).toMatch(GUID)
      const approved = await page.request.post(`${env.apiBaseUrl}${PRODUCTS_PATH}/${draft.id}/activation`, {
        headers: commandHeaders(lifecycleActor, companyId, `u02-deactivation-approve-${runTag}`),
        data: { expectedVersion: draft.version },
      })
      expect(approved.status(), 'the real approval').toBe(200)
      const product = ((await approved.json()) as MutationBody).card.product
      expect(product).toMatchObject({ id: draft.id, name, lifecycle: 'Active', version: nextVersion(draft.version) })
      return product
    })
    const cardPath = `${PRODUCTS_PATH}/${active.id}`
    const deactivationPath = `${cardPath}/deactivation`
    const deactivationUrl = `${env.apiBaseUrl}${deactivationPath}`
    const reactivationUrl = `${env.apiBaseUrl}${cardPath}/reactivation`
    const inactiveVersion = nextVersion(active.version)
    const reason = `SENTETIK u02 uretimden kalkti ${runTag}`
    const beforeDeactivation: LifecycleEvidence = {
      lifecycle: LIFECYCLE.active, version: active.version, deactivations: 0, reactivations: 0, deactivationTerminals: 0,
    }
    expect(await readEvidence(pool, active.id)).toEqual(beforeDeactivation)
    expect(await listIds(page, env, lifecycleActor, 'Active')).toContain(active.id)

    // ── 2. Neither the authorized nor the unauthorized actor may take it out of use. ──────────────────────
    await test.step('the authorized and the unauthorized actor get no control and a 403', async () => {
      await signInAs(page, authorizedActor)
      const traffic = watchProductRequests(page)
      await openCard(page, active.id)
      await expect(lifecycleBadge(page)).toHaveAttribute('data-lifecycle', 'Active')
      await expect(page.getByRole('button', { name: DEACTIVATE_OPEN })).toHaveCount(0)
      traffic.stop()
      expect(traffic.calls.filter((call) => call.startsWith('POST'))).toEqual([])
      for (const actor of [authorizedActor, unauthorizedActor]) {
        const denied = await page.request.post(deactivationUrl, {
          headers: commandHeaders(actor, companyId, `u02-deactivation-denied-${actor}-${runTag}`),
          data: { expectedVersion: active.version, reason },
        })
        expect(denied.status(), `${actor} holds no Deactivate`).toBe(403)
        const deniedText = await denied.text()
        expect(deniedText).not.toContain(active.name)
        expect(deniedText).not.toContain(active.code)
      }
      expect(await readEvidence(pool, active.id)).toEqual(beforeDeactivation)
    })

    // ── 3. The lifecycle actor deactivates in the browser. ─────────────────────────────────────────────────
    const deactivation = await test.step('the lifecycle actor deactivates with a reason and the server confirms it', async () => {
      await signInAs(page, lifecycleActor)
      const opened = await openCard(page, active.id)
      expect(opened.card.product).toMatchObject({ id: active.id, lifecycle: 'Active', version: active.version })
      await expect(lifecycleBadge(page)).toHaveText('Aktif')
      await expect(page.getByRole('button', { name: REACTIVATE_OPEN })).toHaveCount(0)
      await page.getByRole('button', { name: DEACTIVATE_OPEN, exact: true }).click()
      await expect(page.getByText(/Hiçbir kayıt silinmez/)).toBeVisible()

      // A blank reason is refused on the client: nothing is sent.
      const blank = watchProductRequests(page)
      await page.getByRole('button', { name: DEACTIVATE_SUBMIT, exact: true }).click()
      await expect(page.getByText(REASON_REQUIRED, { exact: true })).toBeVisible()
      blank.stop()
      expect(blank.calls.filter((call) => call.startsWith('POST'))).toEqual([])

      await page.getByLabel(REASON_LABEL, { exact: true }).fill(`  ${reason} `)
      const answer = page.waitForResponse((response) => isCall(response, 'POST', deactivationPath))
      const reRead = page.waitForResponse((response) => isCall(response, 'GET', cardPath))
      await page.getByRole('button', { name: DEACTIVATE_SUBMIT, exact: true }).click()
      const response = await answer
      expect(response.status()).toBe(200)
      const sent = response.request()
      const idempotencyKey = (await sent.headerValue('Idempotency-Key')) ?? ''
      expect(idempotencyKey).toMatch(ATTEMPT_KEY)
      expect(await sent.headerValue('X-Company-Id')).toBe(companyId)
      const sentBody = sent.postData() ?? ''
      // The version the screen read and the trimmed reason; no lifecycle claim.
      expect(JSON.parse(sentBody)).toEqual({ expectedVersion: active.version, reason })
      const body = (await response.json()) as MutationBody
      expect(body.isReplay).toBe(false)
      expect(body.card.product).toMatchObject({ id: active.id, lifecycle: 'Inactive', version: inactiveVersion })
      expect((await reRead).status(), 'the card is re-read after the confirmed move').toBe(200)

      await expect(page.getByRole('status').filter({ hasText: DEACTIVATE_SUCCESS })).toHaveText(DEACTIVATE_SUCCESS)
      await expect(lifecycleBadge(page)).toHaveAttribute('data-lifecycle', 'Inactive')
      await expect(lifecycleBadge(page)).toHaveText('Pasif')
      await expect(page.getByTestId('product-inactive-detail')).toContainText(reason)
      await expect(page.getByRole('button', { name: DEACTIVATE_OPEN })).toHaveCount(0)
      await expect(page.getByRole('button', { name: REACTIVATE_OPEN, exact: true })).toBeVisible()
      return { idempotencyKey, sentBody }
    })
    const afterDeactivation: LifecycleEvidence = {
      lifecycle: LIFECYCLE.inactive, version: inactiveVersion, deactivations: 1, reactivations: 0, deactivationTerminals: 1,
    }
    const deactivationAudit: LifecycleAudit = {
      actorId: lifecycleUserId,
      originCompanyId: companyId,
      permissionCode: 'product.card.deactivate',
      eventType: 'product.card.deactivated.v1',
      sourceChannel: 'Web',
      reason,
      keyHash: keyHash(deactivation.idempotencyKey),
      versionBefore: active.version,
      versionAfter: inactiveVersion,
    }
    expect(await readEvidence(pool, active.id)).toEqual(afterDeactivation)
    expect(await readLifecycleAudit(pool, active.id, 'Deactivate')).toEqual([deactivationAudit])

    // ── 4. Close and reopen: Pasif from the server, one deactivation row with its reason. ──────────────────
    const expectOneDeactivation = async (label: string) => {
      const reopened = await openCard(page, active.id)
      expect(reopened.card.product, label).toMatchObject({
        id: active.id, code: active.code, name: active.name, lifecycle: 'Inactive', version: inactiveVersion,
      })
      const history = reopened.history
      expect(history.entries.map((entry) => entry.operation), label).toEqual(['Deactivate', 'Activate', 'DraftCreate'])
      expect(history.entries[0]).toMatchObject({
        operation: 'Deactivate', versionBefore: active.version, versionAfter: inactiveVersion, reason, changedFields: ['lifecycle'],
      })
      // Parsed, never substring-matched: each entry carries exactly the documented keys, no actor or request data.
      for (const entry of history.entries) expect(Object.keys(entry).sort()).toEqual(HISTORY_ENTRY_KEYS)
      expect(Object.keys(history).sort()).toEqual(['entries', 'productId', 'truncated'])

      await expect(lifecycleBadge(page)).toHaveText('Pasif')
      await expect(page.getByText(/her iki şirkette de yeni kullanıma kapalıdır/)).toBeVisible()
      await expect(page.getByTestId('product-inactive-detail')).toContainText(reason)
      await expect(page.getByRole('status').filter({ hasText: DEACTIVATE_SUCCESS })).toHaveCount(0)
      await expect(historyItems(page)).toHaveCount(3)
      await expect(historyItems(page).nth(0)).toContainText(HISTORY_DEACTIVATION_LABEL)
      await expect(historyItems(page).nth(0)).toContainText(`Sürüm ${active.version} → ${inactiveVersion}`)
      await expect(historyItems(page).nth(0)).toContainText(`Gerekçe: ${reason}`)
    }
    await test.step('close and reopen: the server says Inactive and the history holds one deactivation', async () => {
      await expectOneDeactivation('first reopen')
      expect(await readEvidence(pool, active.id)).toEqual(afterDeactivation)
    })

    // ── 5–6. Replay and refusals: nothing new is written. ─────────────────────────────────────────────────
    await test.step('the same key replays; another reason, a stale version, the wrong state and a foreign actor are refused', async () => {
      const replay = await page.request.post(deactivationUrl, {
        headers: commandHeaders(lifecycleActor, companyId, deactivation.idempotencyKey),
        data: deactivation.sentBody,
      })
      expect(replay.status()).toBe(200)
      expect(replay.headers()['idempotency-replayed']).toBe('true')
      const replayed = (await replay.json()) as MutationBody
      expect(replayed.isReplay).toBe(true)
      expect(replayed.card.product).toMatchObject({ id: active.id, lifecycle: 'Inactive', version: inactiveVersion })

      const changed = await page.request.post(deactivationUrl, {
        headers: commandHeaders(lifecycleActor, companyId, deactivation.idempotencyKey),
        data: { expectedVersion: active.version, reason: `${reason} degisti` },
      })
      expect(changed.status()).toBe(409)
      expect(((await changed.json()) as { code?: string }).code).toBe('idempotency_conflict')

      const again = await page.request.post(deactivationUrl, {
        headers: commandHeaders(lifecycleActor, companyId, `u02-deactivation-again-${runTag}`),
        data: { expectedVersion: inactiveVersion, reason },
      })
      expect(again.status()).toBe(409)
      expect(((await again.json()) as { code?: string }).code).toBe('lifecycle_conflict')

      const stale = await page.request.post(reactivationUrl, {
        headers: commandHeaders(lifecycleActor, companyId, `u02-reactivation-stale-${runTag}`),
        data: { expectedVersion: active.version, reason },
      })
      expect(stale.status()).toBe(409)
      expect(((await stale.json()) as { code?: string }).code).toBe('version_conflict')

      const foreign = await page.request.post(deactivationUrl, {
        headers: commandHeaders(authorizedActor, companyId, deactivation.idempotencyKey),
        data: deactivation.sentBody,
      })
      expect(foreign.status()).toBe(403)
      expect(foreign.headers()['idempotency-replayed']).toBeUndefined()

      expect(await readEvidence(pool, active.id)).toEqual(afterDeactivation)
      expect(await readLifecycleAudit(pool, active.id, 'Deactivate')).toEqual([deactivationAudit])
    })

    // ── 7. New selection refuses it: the Active list drops it; the Inactive list keeps it readable. ──────────
    await test.step('the Active list no longer offers the card and the Inactive list still shows it', async () => {
      expect(await listIds(page, env, lifecycleActor, 'Active')).not.toContain(active.id)
      expect(await listIds(page, env, lifecycleActor, 'Inactive')).toContain(active.id)
    })

    // ── 8. Reactivation in the browser, with its own reason. ────────────────────────────────────────────────
    await test.step('the lifecycle actor reactivates it with its own reason', async () => {
      await openCard(page, active.id)
      await page.getByRole('button', { name: REACTIVATE_OPEN, exact: true }).click()
      const back = `SENTETIK u02 tedarik yeniden basladi ${runTag}`
      await page.getByLabel(REASON_LABEL, { exact: true }).fill(back)
      const answer = page.waitForResponse((response) => isCall(response, 'POST', `${cardPath}/reactivation`))
      await page.getByRole('button', { name: REACTIVATE_SUBMIT, exact: true }).click()
      const response = await answer
      expect(response.status()).toBe(200)
      expect(JSON.parse(response.request().postData() ?? '')).toEqual({ expectedVersion: inactiveVersion, reason: back })
      await expect(page.getByRole('status').filter({ hasText: REACTIVATE_SUCCESS })).toHaveText(REACTIVATE_SUCCESS)
      await expect(lifecycleBadge(page)).toHaveText('Aktif')
      await expect(page.getByTestId('product-inactive-detail')).toHaveCount(0)

      const reopened = await openCard(page, active.id)
      expect(reopened.card.product).toMatchObject({ lifecycle: 'Active', version: nextVersion(inactiveVersion) })
      expect(reopened.history.entries[0]).toMatchObject({ operation: 'Reactivate', reason: back })
      await expect(historyItems(page).nth(0)).toContainText(HISTORY_REACTIVATION_LABEL)
      const [audit] = await readLifecycleAudit(pool, active.id, 'Reactivate')
      expect(audit).toMatchObject({
        actorId: lifecycleUserId, permissionCode: 'product.card.reactivate', sourceChannel: 'Web', reason: back,
      })
      expect(await readEvidence(pool, active.id)).toMatchObject({ lifecycle: LIFECYCLE.active, deactivations: 1, reactivations: 1 })
      expect(await listIds(page, env, lifecycleActor, 'Active')).toContain(active.id)
    })
  } finally {
    await pool.close()
  }
})
