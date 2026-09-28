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
 *               without the grant is 403; nothing new is written; the change is audited from the Web channel;
 *               the allocation is released at the end so the label is free again for the later files.
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

async function positionOf(page: Page): Promise<{ readonly activeAllocationKg: string; readonly availableKg: string }> {
  const response = await page.request.get(`${env.apiBaseUrl}${POSITIONS}?q=${encodeURIComponent(LABEL_CODE)}`, {
    headers: headers(preparerActor),
  })
  expect(response.status()).toBe(200)
  const body = (await response.json()) as { readonly items: readonly { readonly labelId: string; readonly activeAllocationKg: string; readonly availableKg: string }[] }
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
    const movementsBefore = (await effects(pool)).movements
    const freeBefore = await positionOf(page)

    // Arrange through the real API: the approved line reserves 5 kg of the label (S04-06 proves the screen create).
    allocationId = randomUUID()
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
    await page.getByRole('button', { name: 'Ara', exact: true }).click()
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

test('INV-AL-1-02 the same key replays once, the consumed version is 409, no grant is 403, and the label is freed', async ({ page }) => {
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

    const released = await page.request.post(`${env.apiBaseUrl}${ALLOCATIONS}/${allocationId}/release`, {
      headers: headers(preparerActor, `SENTETIK-inv-al-1-release-${allocationId}`),
      data: { expectedAllocationVersion: '2', reason: 'SENTETIK INV-AL-1 temizlik' },
    })
    expect(released.status()).toBe(200)
    expect((await lineAllocations(page)).filter((allocation) => allocation.status === 'Active')).toEqual([])
  } finally {
    await pool.close()
  }
})
