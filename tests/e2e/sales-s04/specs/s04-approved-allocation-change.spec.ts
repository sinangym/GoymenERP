/**
 * INV-AL-1 change — revise an existing allocation from ST-01 / the ST-02 label detail against the REAL stack
 * (`browser:s04:ci`). Nothing is mocked or intercepted; the browser only carries the actor's identity header.
 *
 * Why s04 and not r14: the change re-reads the APPROVED Sales line through the Sales owner port, and only the s04
 * stack has an approved order with an allocation label (the ONE seeded draft, approved in S04-03; its allocation
 * label SYNS04-ALLOC-LABEL-1 is back to fully free after S04-06). The s04 preparer already holds
 * inventory.physical-allocation.write beside inventory.labels.read + inventory.balances.read in this disposable run
 * database (s04.override.yml), so this journey needs NO new actor, grant or override. File order: after
 * s04-approval-journey (the draft is Approved and S04-06 has released its allocation), before the cancellation file.
 *
 * Journey (serial, one allocation):
 *   INV-AL-1-01 arrange: the preparer reserves 5 kg of the label for the approved line through the real API;
 *               opens ST-01, finds the label, opens its detail from the row; changes 5 → 3 kg with a reason;
 *               close and reopen shows 3 kg and the history "5 kg → 3 kg"; ST-01 and the Sales line read 3 kg.
 *   INV-AL-1-02 the same key replays once (200 + Idempotency-Replayed), the consumed version is 409, the viewer
 *               without the grant is 403; nothing new is written; the change is audited from the Web channel.
 *   AT-063-REL  ST-01 → the row menu's "Tahsisi serbest bırak" (offered because the row holds active allocation kg)
 *               → ST-02 opens with the release form of the one active allocation → reason → the EXISTING release
 *               command (200, version 3, Released) → the panel shows "Serbest bırakıldı" with the reason; back on
 *               ST-01 the row's free kg is up by the 3 kg released and the menu no longer offers release; one
 *               release command and one Web audit, no stock movement.
 *   afterAll    releases the reservation this file made if it is still active, at its current version — also when
 *               01 or 02 failed half-way — so the label is free again for the later files (the cancellation file).
 *
 * The MOVE path (target = another label of the same product) is not driven here: the s04 seed has ONE allocation
 * label, and a second one is a seed + central validator change (SUITE_SEED_FIELDS). It is proven on real SQL
 * (InventoryAllocationChangeSqlServerRelationalAcceptanceTests) and by the panel's component tests.
 */
import { randomUUID } from 'node:crypto'
import { expect, test, type Page } from '@playwright/test'
import sql from 'mssql'
import { readEnvironment } from '../src/env.js'

const env = readEnvironment()
const { preparerActor, readerActor, companyId, draftId, allocationLineId, allocationLabelId } = env.seed
const DEVELOPMENT_USER_HEADER = 'X-Development-User'
const ALLOCATIONS = '/api/v1/inventory/physical-allocations'
const LABEL_ALLOCATIONS = `/api/v1/inventory/labels/${allocationLabelId}/physical-allocations`
const POSITIONS = '/api/v1/inventory/positions'
const LABEL_CODE = 'SYNS04-ALLOC-LABEL-1'
const REASON = 'SENTETIK INV-AL-1 sipariş miktarı azaldı'

interface AllocationView {
  readonly allocationId: string
  readonly version: string
  readonly status: string
  readonly labelId: string
  readonly remainingKg: string
  readonly allocatedKg: string
}

interface StockPositionView {
  readonly labelId: string
  readonly physicalKg: string
  readonly blockedPhysicalKg: string
  readonly freePhysicalKg: string
  readonly activeAllocationKg: string
  readonly availableKg: string
}

let allocationId = ''
let changeKey = ''
let changeBody: Record<string, unknown> = {}

function headers(actor: string, key?: string): Record<string, string> {
  return {
    [DEVELOPMENT_USER_HEADER]: actor,
    'X-Company-Id': companyId,
    ...(key === undefined ? {} : { 'Content-Type': 'application/json', 'Idempotency-Key': key }),
  }
}

async function signInAs(page: Page, actor: string): Promise<void> {
  await page.setExtraHTTPHeaders({ [DEVELOPMENT_USER_HEADER]: actor })
}

async function labelVersion(page: Page): Promise<string> {
  const response = await page.request.get(`${env.apiBaseUrl}/api/v1/inventory/labels/${allocationLabelId}/detail`, {
    headers: headers(preparerActor),
  })
  expect(response.status()).toBe(200)
  return ((await response.json()) as { readonly labelVersion: string }).labelVersion
}

async function positionOf(page: Page): Promise<StockPositionView> {
  const response = await page.request.get(`${env.apiBaseUrl}${POSITIONS}?q=${encodeURIComponent(LABEL_CODE)}`, {
    headers: headers(preparerActor),
  })
  expect(response.status()).toBe(200)
  const body = (await response.json()) as { readonly items: readonly StockPositionView[] }
  const item = body.items.find((candidate) => candidate.labelId.toLowerCase() === allocationLabelId.toLowerCase())
  expect(item, 'ST-01 lists the allocation label').toBeDefined()
  return item!
}

async function lineAllocations(page: Page): Promise<readonly AllocationView[]> {
  const response = await page.request.get(
    `${env.apiBaseUrl}${ALLOCATIONS}?salesOrderId=${draftId}&salesOrderLineId=${allocationLineId}&limit=100`,
    { headers: headers(preparerActor) })
  expect(response.status()).toBe(200)
  return ((await response.json()) as { readonly items: readonly { readonly allocation: AllocationView }[] }).items
    .map((item) => item.allocation)
}

type SqlPool = Awaited<ReturnType<InstanceType<typeof sql.ConnectionPool>['connect']>>

async function effects(pool: SqlPool): Promise<{ changes: number; commands: number; webAudits: number; movements: number }> {
  const result = await pool.request()
    .input('companyId', sql.UniqueIdentifier, companyId)
    .input('allocationId', sql.UniqueIdentifier, allocationId)
    .input('entityId', sql.NVarChar, allocationId)
    .input('labelId', sql.UniqueIdentifier, allocationLabelId)
    .query<{ changes: number; commands: number; webAudits: number; movements: number }>(`SELECT
      (SELECT COUNT(*) FROM [inventory].[AllocationChanges]
        WHERE [CompanyId] = @companyId AND [SourceAllocationId] = @allocationId) AS [changes],
      (SELECT COUNT(*) FROM [inventory].[Commands]
        WHERE [CompanyId] = @companyId AND [TargetId] = @allocationId
          AND [CommandName] = N'inventory.physical-allocation.change.v1') AS [commands],
      (SELECT COUNT(*) FROM [audit].[AuditEvents]
        WHERE [CompanyId] = @companyId AND [EntityType] = N'Inventory.StockAllocation' AND [EntityId] = @entityId
          AND [EventType] = N'inventory.physical-allocation.resized.v1' AND [SourceChannel] = N'Web') AS [webAudits],
      (SELECT COUNT(DISTINCT m.[Id]) FROM [inventory].[Movements] AS m
        JOIN [inventory].[MovementLines] AS l ON l.[CompanyId] = m.[CompanyId] AND l.[MovementId] = m.[Id]
        WHERE m.[CompanyId] = @companyId AND l.[LabelId] = @labelId) AS [movements]`)
  return result.recordset[0]
}

test.describe.configure({ mode: 'serial' })

test('INV-AL-1-01 an allocation opened from ST-01 changes 5 → 3 kg with a reason and reopens with its history', async ({ page }) => {
  const pool = await new sql.ConnectionPool(env.sqlConnection).connect()
  try {
    // The effect probe binds the allocation id as a uniqueidentifier, so the id is minted before the first probe.
    allocationId = randomUUID()
    const movementsBefore = (await effects(pool)).movements
    const freeBefore = await positionOf(page)

    // Arrange through the real API: the approved line reserves 5 kg of the label (S04-06 proves the screen create).
    const created = await page.request.post(`${env.apiBaseUrl}${ALLOCATIONS}`, {
      headers: headers(preparerActor, `SENTETIK-inv-al-1-create-${allocationId}`),
      data: { allocationId, salesOrderId: draftId, salesOrderLineId: allocationLineId, labelId: allocationLabelId,
        expectedLabelVersion: await labelVersion(page), quantityKg: '5', reason: 'SENTETIK INV-AL-1 tahsis',
        reservedUntilUtc: null },
    })
    expect(created.status()).toBe(201)

    await signInAs(page, preparerActor)
    await page.goto('/stok/pozisyonlar')
    await expect(page.getByRole('heading', { level: 1, name: 'Stok / Malzeme Pozisyonu' })).toBeVisible({ timeout: 30_000 })
    await page.getByLabel('Etiket kodu', { exact: true }).fill(LABEL_CODE)
    const positionsRead = page.waitForResponse((response) => response.request().method() === 'GET'
      && new URL(response.url()).pathname === POSITIONS
      && new URL(response.url()).searchParams.get('q') === LABEL_CODE)
    await page.getByRole('button', { name: 'Ara', exact: true }).click()
    const positionsResponse = await positionsRead
    expect(positionsResponse.status()).toBe(200)
    const positionsBody = (await positionsResponse.json()) as { readonly items: readonly StockPositionView[] }
    const position = positionsBody.items.find((item) => item.labelId.toLowerCase() === allocationLabelId.toLowerCase())
    expect(position, 'ST-01 filtered response contains the allocation label').toBeDefined()
    expect(Number(position!.freePhysicalKg)).not.toBe(Number(position!.availableKg))

    const stockTable = page.getByRole('table')
    await expect(stockTable.getByRole('columnheader', { name: 'Fiziksel kg', exact: true })).toBeVisible()
    await expect(stockTable.getByRole('columnheader', { name: 'Bloklu kg', exact: true })).toBeVisible()
    await expect(stockTable.getByRole('columnheader', { name: 'Tahsisli kg', exact: true })).toBeVisible()
    await expect(stockTable.getByRole('columnheader', { name: 'Kullanılabilir kg', exact: true })).toBeVisible()
    await expect(stockTable.getByRole('columnheader', { name: 'Serbest kg', exact: true })).toHaveCount(0)
    const stockPositionRow = page.locator(`tr[data-label-id="${allocationLabelId}"]`)
    await expect(stockPositionRow).toBeVisible()
    const quantityCells = stockPositionRow.locator('td')
    await expect(quantityCells.nth(4)).toHaveText(position!.physicalKg)
    await expect(quantityCells.nth(5)).toHaveText(position!.blockedPhysicalKg)
    await expect(quantityCells.nth(6)).toHaveText(position!.activeAllocationKg)
    await expect(quantityCells.nth(7)).toHaveText(position!.availableKg)

    const allocationsRead = page.waitForResponse((response) => response.request().method() === 'GET'
      && new URL(response.url()).pathname === LABEL_ALLOCATIONS)
    await page.getByRole('link', { name: LABEL_CODE, exact: true }).click()
    await expect(page.getByRole('heading', { level: 1, name: LABEL_CODE })).toBeVisible({ timeout: 30_000 })
    expect((await allocationsRead).status()).toBe(200)

    const row = page.getByTestId(`inventory-allocation-${allocationId}`)
    await expect(row).toContainText('Aktif')
    await expect(row).toContainText('5 kg / 5 kg')
    await row.getByRole('button', { name: 'Tahsisi revize et' }).click()
    await row.getByLabel('Yeni tahsis kg').fill('3')
    await row.getByLabel('Değişiklik gerekçesi').fill(REASON)
    const changeAnswer = page.waitForResponse((response) => response.request().method() === 'POST'
      && new URL(response.url()).pathname === `${ALLOCATIONS}/${allocationId}/change`)
    await row.getByRole('button', { name: 'Tahsisi değiştir' }).click()
    const changed = await changeAnswer
    expect(changed.status()).toBe(200)
    expect(changed.headers()['idempotency-replayed']).toBeUndefined()
    expect(await changed.json()).toMatchObject({ kind: 'Resize', replaced: null,
      allocation: { allocationId, version: '2', status: 'Active', remainingKg: '3', allocatedKg: '3' } })
    changeKey = changed.request().headers()['idempotency-key']
    changeBody = JSON.parse(changed.request().postData() ?? '{}') as Record<string, unknown>
    expect(changeBody).toMatchObject({ expectedAllocationVersion: '1', salesOrderId: draftId,
      salesOrderLineId: allocationLineId, targetLabelId: allocationLabelId, quantityKg: '3', reason: REASON,
      newAllocationId: null })
    await expect(page.getByText(/Tahsis güncellendi: 3 kg ayrılmış durumda/)).toBeVisible()

    // Close and reopen: the server's allocation and its history.
    await page.goto('about:blank')
    await page.goto(`/stok/etiket/${allocationLabelId}`)
    const reopened = page.getByTestId(`inventory-allocation-${allocationId}`)
    await expect(reopened).toContainText('3 kg / 3 kg', { timeout: 30_000 })
    await expect(reopened).toContainText('sürüm 2')
    await expect(reopened).toContainText('Miktar 5 kg → 3 kg')
    await expect(reopened).toContainText(REASON)

    // ST-01 and the Sales order's line read the new state.
    const freeAfter = await positionOf(page)
    expect(Number(freeAfter.activeAllocationKg)).toBe(Number(freeBefore.activeAllocationKg) + 3)
    expect(Number(freeAfter.availableKg)).toBe(Number(freeBefore.availableKg) - 3)
    const line = (await lineAllocations(page)).find((allocation) => allocation.allocationId === allocationId)
    expect(line).toMatchObject({ status: 'Active', version: '2', remainingKg: '3' })
    await page.goto(`/satis/siparisler/${draftId}`)
    await expect(page.getByTestId(`sales-linked-allocation-line-${allocationLineId}`)).toHaveText('Satır 1: aktif tahsis 1',
      { timeout: 30_000 })

    expect(await effects(pool)).toEqual({ changes: 1, commands: 1, webAudits: 1, movements: movementsBefore })
  } finally {
    await pool.close()
  }
})

test('INV-AL-1-02 the same key replays once, the consumed version is 409, and no grant is 403', async ({ page }) => {
  expect(allocationId, 'INV-AL-1-01 recorded the change').not.toBe('')
  const pool = await new sql.ConnectionPool(env.sqlConnection).connect()
  try {
    const before = await effects(pool)
    const replay = await page.request.post(`${env.apiBaseUrl}${ALLOCATIONS}/${allocationId}/change`, {
      headers: headers(preparerActor, changeKey), data: changeBody,
    })
    expect(replay.status()).toBe(200)
    expect(replay.headers()['idempotency-replayed']).toBe('true')
    expect(await replay.json()).toMatchObject({ allocation: { allocationId, version: '2', remainingKg: '3' } })

    const stale = await page.request.post(`${env.apiBaseUrl}${ALLOCATIONS}/${allocationId}/change`, {
      headers: headers(preparerActor, `SENTETIK-inv-al-1-stale-${randomUUID()}`),
      data: { ...changeBody, quantityKg: '2' },
    })
    expect(stale.status()).toBe(409)
    expect(await stale.json()).toMatchObject({ code: 'inventory_allocation_version_conflict' })

    const denied = await page.request.post(`${env.apiBaseUrl}${ALLOCATIONS}/${allocationId}/change`, {
      headers: headers(readerActor, `SENTETIK-inv-al-1-denied-${randomUUID()}`),
      data: { ...changeBody, expectedAllocationVersion: '2', quantityKg: '2' },
    })
    expect(denied.status()).toBe(403)
    expect(await effects(pool)).toEqual(before)
  } finally {
    await pool.close()
  }
})

test('AT-063-REL the ST-01 row action releases the allocation with a reason and ST-01 shows the kg free again', async ({ page }) => {
  expect(allocationId, 'INV-AL-1-01 recorded the allocation').not.toBe('')
  const RELEASE_REASON = 'SENTETIK AT-063 sipariş satırı iptal edildi'
  const pool = await new sql.ConnectionPool(env.sqlConnection).connect()
  const releaseEffects = async () => (await pool.request()
    .input('companyId', sql.UniqueIdentifier, companyId)
    .input('allocationId', sql.UniqueIdentifier, allocationId)
    .input('entityId', sql.NVarChar, allocationId)
    .query<{ commands: number; webAudits: number }>(`SELECT
      (SELECT COUNT(*) FROM [inventory].[Commands]
        WHERE [CompanyId] = @companyId AND [TargetId] = @allocationId
          AND [CommandName] = N'inventory.physical-allocation.release.v1') AS [commands],
      (SELECT COUNT(*) FROM [audit].[AuditEvents]
        WHERE [CompanyId] = @companyId AND [EntityType] = N'Inventory.StockAllocation' AND [EntityId] = @entityId
          AND [EventType] = N'inventory.physical-allocation.released.v1' AND [SourceChannel] = N'Web') AS [webAudits]`))
    .recordset[0]
  try {
    const movementsBefore = (await effects(pool)).movements
    expect(await releaseEffects()).toEqual({ commands: 0, webAudits: 0 })
    const before = await positionOf(page)

    await signInAs(page, preparerActor)
    const firstRead = page.waitForResponse((response) => response.request().method() === 'GET'
      && new URL(response.url()).pathname === POSITIONS && new URL(response.url()).searchParams.get('q') === LABEL_CODE)
    await page.goto(`/stok/pozisyonlar?etiket=${encodeURIComponent(LABEL_CODE)}`)
    expect((await firstRead).status()).toBe(200)
    const stockRow = page.locator(`tr[data-label-id="${allocationLabelId}"]`)
    await expect(stockRow).toBeVisible({ timeout: 30_000 })
    await stockRow.getByRole('button', { name: `${LABEL_CODE} işlemleri`, exact: true }).click()
    const menu = page.getByRole('list', { name: `${LABEL_CODE} için işlemler`, exact: true })
    await expect(menu.getByRole('link', { name: 'Tahsisi serbest bırak', exact: true })).toBeVisible()

    const allocationsRead = page.waitForResponse((response) => response.request().method() === 'GET'
      && new URL(response.url()).pathname === LABEL_ALLOCATIONS)
    await menu.getByRole('link', { name: 'Tahsisi serbest bırak', exact: true }).click()
    await expect(page).toHaveURL(new RegExp(`/stok/etiket/${allocationLabelId}\\?islem=serbest-birak$`))
    expect((await allocationsRead).status()).toBe(200)
    await expect(page.locator('#islem-tahsis')).toBeFocused()
    const row = page.getByTestId(`inventory-allocation-${allocationId}`)
    await expect(row.getByRole('group', { name: 'Tahsisi serbest bırak' })).toBeVisible({ timeout: 30_000 })
    await expect(row).toContainText('Kalan 3 kg ayrılmış miktarın tamamı serbest bırakılır')
    await row.getByLabel('Serbest bırakma gerekçesi').fill(RELEASE_REASON)
    const releaseAnswer = page.waitForResponse((response) => response.request().method() === 'POST'
      && new URL(response.url()).pathname === `${ALLOCATIONS}/${allocationId}/release`)
    await row.getByRole('button', { name: 'Serbest bırakmayı onayla' }).click()
    const released = await releaseAnswer
    expect(released.status()).toBe(200)
    expect(released.headers()['idempotency-replayed']).toBeUndefined()
    expect(JSON.parse(released.request().postData() ?? '{}')).toEqual({ expectedAllocationVersion: '2', reason: RELEASE_REASON })
    expect(await released.json()).toMatchObject({ allocationId, version: '3', status: 'Released', remainingKg: '0' })
    await expect(page.getByText(/Tahsis serbest bırakıldı: .* artık aktif değil/)).toBeVisible()
    await expect(row).toContainText('Serbest bırakıldı')
    await expect(row).toContainText(`Serbest bırakma: ${RELEASE_REASON}`)
    await expect(row.getByRole('button', { name: 'Tahsisi serbest bırak' })).toHaveCount(0)

    // Back on ST-01 (the same filter, read afresh): the 3 kg are free again and release is no longer offered.
    const back = page.waitForResponse((response) => response.request().method() === 'GET'
      && new URL(response.url()).pathname === POSITIONS && new URL(response.url()).searchParams.get('q') === LABEL_CODE)
    await page.getByRole('link', { name: '← Stok pozisyonlarına dön', exact: true }).click()
    const backBody = (await (await back).json()) as { readonly items: readonly { readonly labelId: string; readonly availableKg: string; readonly activeAllocationKg: string }[] }
    const after = backBody.items.find((item) => item.labelId.toLowerCase() === allocationLabelId.toLowerCase())!
    expect(Number(after.availableKg)).toBe(Number(before.availableKg) + 3)
    // This file's 3 kg was the label's only active allocation (S04-06 left it fully free): none is left.
    expect(Number(before.activeAllocationKg)).toBe(3)
    expect(Number(after.activeAllocationKg)).toBe(0)
    const freeCell = page.locator(`tr[data-label-id="${allocationLabelId}"] td`).nth(7)
    await expect(freeCell).toHaveText(after.availableKg)
    await page.locator(`tr[data-label-id="${allocationLabelId}"]`)
      .getByRole('button', { name: `${LABEL_CODE} işlemleri`, exact: true }).click()
    await expect(page.getByRole('list', { name: `${LABEL_CODE} için işlemler`, exact: true })
      .getByRole('link', { name: 'Tahsisi serbest bırak', exact: true })).toHaveCount(0)

    expect(await releaseEffects()).toEqual({ commands: 1, webAudits: 1 })
    expect((await effects(pool)).movements).toBe(movementsBefore)
  } finally {
    await pool.close()
  }
})

test.afterAll(async ({ playwright }) => {
  // Cleanup never depends on 01/02 having passed: the allocation this file reserved, if it is still Active, is
  // released at the version the server answers now. A file that failed before its create has nothing to release.
  if (allocationId === '') return
  const api = await playwright.request.newContext()
  try {
    const read = async (): Promise<readonly AllocationView[]> => {
      const listed = await api.get(
        `${env.apiBaseUrl}${ALLOCATIONS}?salesOrderId=${draftId}&salesOrderLineId=${allocationLineId}&limit=100`,
        { headers: headers(preparerActor) })
      expect(listed.status(), 'the cleanup reads the line allocations').toBe(200)
      return ((await listed.json()) as { readonly items: readonly { readonly allocation: AllocationView }[] }).items
        .map((item) => item.allocation)
    }
    const ours = (await read()).filter((allocation) =>
      allocation.allocationId.toLowerCase() === allocationId.toLowerCase() && allocation.status === 'Active')
    for (const allocation of ours) {
      const released = await api.post(`${env.apiBaseUrl}${ALLOCATIONS}/${allocation.allocationId}/release`, {
        headers: headers(preparerActor, `SENTETIK-inv-al-1-release-${allocation.allocationId}-${allocation.version}`),
        data: { expectedAllocationVersion: allocation.version, reason: 'SENTETIK INV-AL-1 temizlik' },
      })
      expect(released.status(), 'the cleanup releases the reservation').toBe(200)
    }
    expect((await read()).filter((allocation) => allocation.status === 'Active'),
      'the label is free again for the later files').toEqual([])
  } finally {
    await api.dispose()
  }
})
