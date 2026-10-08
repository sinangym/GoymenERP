import { randomUUID } from 'node:crypto'
import { expect, test, type Page, type Response } from '@playwright/test'
import sql from 'mssql'
import { readEnvironment } from '../src/env'

/**
 * ST-04 company stock-movement timeline on the REAL API and UI of the r14 disposable stack. Nothing is mocked.
 *
 * No new grant, policy, route permission or arrange: the recorder already holds the opening command and the two label
 * reads; the denied actor holds neither read; the timeline cursor is signed with the same disposable stock cursor key
 * the r14 override already binds (#604).
 *
 * Journey:
 *  1. Precondition: the recorder records ONE real opening count whose lines are more than one timeline page. Each
 *     opening line is its own immutable Receipt movement, so the company has a real second cursor page.
 *  2. The recorder opens one of those labels, follows "Şirket stok hareketlerini aç" and reads the company timeline:
 *     the first page asks limit only, the continuation asks the SAME limit plus the owner's opaque cursor.
 *  3. The page says it is company-wide and not filtered by the label; the opened label's row shows the same stored
 *     label id/code and the same opening count as its source.
 *  4. "← Etikete dön" reopens the same label.
 *  5. Fences: the denied actor gets no page and a 403 read; another company context is refused; a tampered cursor
 *     and a cursor replayed by another actor are 400 invalid_query; reading wrote no movement.
 */
const env = readEnvironment()
const { recorderActor, deniedActor, transformerActor, companyId, otherCompanyId, locationId, productId,
  productVersion } = env.seed

const DEVELOPMENT_USER_HEADER = 'X-Development-User'
const OPENINGS = '/api/v1/inventory/opening-counts'
const MOVEMENTS = '/api/v1/inventory/movements'
const TIMELINE_ROUTE = '/stok/hareketler'
const PAGE_SIZE = 25
const LINE_COUNT = PAGE_SIZE + 1
const COUNTED_ON = '2026-09-18'
const SHEET = 'SENTETIK browser-suite:r14 ST-04 timeline source'
const LABEL_PREFIX = 'SYNST04-'
const INVALID_QUERY = 'inventory_movement_timeline_invalid_query'
const MAX_PAGES = 40

interface TimelineLine {
  readonly sequence: number
  readonly direction: string
  readonly labelId: string
  readonly labelCode: string
  readonly productId: string
  readonly quantityKg: string
}

interface TimelineMovement {
  readonly movementId: string
  readonly kind: string
  readonly sourceKind: string
  readonly sourceId: string
  readonly sourceLineId: string | null
  readonly sourceGrain: string
  readonly lines: readonly TimelineLine[]
}

interface TimelinePage {
  readonly items: readonly TimelineMovement[]
  readonly nextCursor: string | null
}

function apiHeaders(actor: string, company = companyId): Record<string, string> {
  return { [DEVELOPMENT_USER_HEADER]: actor, 'X-Company-Id': company }
}

async function signInAs(page: Page, actor: string): Promise<void> {
  await page.setExtraHTTPHeaders({ [DEVELOPMENT_USER_HEADER]: actor })
}

function isTimelineCall(response: Response): boolean {
  return response.request().method() === 'GET' && new URL(response.url()).pathname === MOVEMENTS
}

function labelCode(index: number): string {
  return `${LABEL_PREFIX}${String(index + 1).padStart(2, '0')}`
}

type Pool = Awaited<ReturnType<InstanceType<typeof sql.ConnectionPool>['connect']>>

async function movementRows(pool: Pool): Promise<number> {
  const result = await pool.request()
    .input('companyId', sql.UniqueIdentifier, companyId)
    .query<{ count: number }>('SELECT COUNT(*) AS count FROM inventory.Movements WHERE CompanyId=@companyId')
  return result.recordset[0].count
}

test('ST-04 reads the real company timeline from a label, pages it by the owner cursor and keeps every fence', async ({ page }) => {
  const pool = await new sql.ConnectionPool(env.sqlConnection).connect()
  try {
    // ── 1. One real opening count, one Receipt movement per line, more lines than one page. ─────────────────────
    const countId = randomUUID()
    const lines = Array.from({ length: LINE_COUNT }, (_, index) => ({
      lineId: randomUUID(), labelId: randomUUID(), labelCode: labelCode(index), lotId: randomUUID(),
      heatNumber: `SYNST04-HEAT-${index + 1}`, coilNumber: null, productId, productVersion, quantityKg: '2.5',
    }))
    const opening = await page.request.post(`${env.apiBaseUrl}${OPENINGS}`, {
      headers: { ...apiHeaders(recorderActor), 'Content-Type': 'application/json', 'Idempotency-Key': `st04-opening-${countId}` },
      data: { countId, locationId, countedOn: COUNTED_ON, sheetReference: SHEET, lines },
    })
    expect(opening.status()).toBe(201)
    const created = await opening.json() as {
      readonly countId: string
      readonly lines: readonly { readonly lineId: string; readonly labelId: string; readonly labelCode: string }[]
    }
    expect(created.countId).toBe(countId)
    expect(created.lines).toHaveLength(LINE_COUNT)
    const target = created.lines.find((line) => line.labelCode === labelCode(0))!
    expect(target).toMatchObject({ labelId: lines[0].labelId, lineId: lines[0].lineId })

    const sourced = await pool.request()
      .input('companyId', sql.UniqueIdentifier, companyId)
      .input('countId', sql.UniqueIdentifier, countId)
      .query<{ count: number }>(`SELECT COUNT(*) AS count FROM inventory.Movements
        WHERE CompanyId=@companyId AND SourceId=@countId AND Kind=1`)
    expect(sourced.recordset[0].count).toBe(LINE_COUNT)
    const movementsBefore = await movementRows(pool)
    expect(movementsBefore).toBeGreaterThan(PAGE_SIZE)

    // ── 2. Label detail → company timeline (first page: limit only). ─────────────────────────────────────────
    await signInAs(page, recorderActor)
    const labelPath = `/api/v1/inventory/labels/${target.labelId}/detail`
    const labelAnswer = page.waitForResponse((response) => response.request().method() === 'GET'
      && new URL(response.url()).pathname === labelPath)
    await page.goto(`/stok/etiket/${target.labelId}`)
    expect((await labelAnswer).status()).toBe(200)
    await expect(page.getByRole('heading', { level: 1, name: target.labelCode, exact: true })).toBeVisible({ timeout: 30_000 })

    const timelineCalls: Response[] = []
    page.on('response', (response) => { if (isTimelineCall(response)) timelineCalls.push(response) })
    const firstAnswer = page.waitForResponse(isTimelineCall)
    await page.getByRole('link', { name: 'Şirket stok hareketlerini aç', exact: true }).click()
    const firstResponse = await firstAnswer
    expect(firstResponse.status()).toBe(200)
    expect(firstResponse.headers()['cache-control']).toContain('no-store')
    const firstUrl = new URL(firstResponse.url())
    expect([...firstUrl.searchParams.keys()]).toEqual(['limit'])
    expect(firstUrl.searchParams.get('limit')).toBe(String(PAGE_SIZE))
    expect(firstResponse.request().headers()['x-company-id']).toBe(companyId)
    const firstPage = await firstResponse.json() as TimelinePage
    expect(firstPage.items).toHaveLength(PAGE_SIZE)
    expect(firstPage.nextCursor).toEqual(expect.any(String))
    await expect(page).toHaveURL(new RegExp(`${TIMELINE_ROUTE}$`))
    await expect(page.getByRole('heading', { level: 1, name: 'Şirket stok hareketleri', exact: true })).toBeVisible()
    // ── 3. Honest scope: company-wide, not a label filter. ──────────────────────────────────────────────────
    await expect(page.getByText(/tüm etiketlerindeki kayıtlı fiziksel stok hareketlerini/)).toBeVisible()
    await expect(page.getByText(/geldiğiniz etikete göre filtrelenmez/)).toBeVisible()
    await expect(page.getByText(`${PAGE_SIZE} hareket gösteriliyor.`, { exact: true })).toBeVisible()
    const firstPageLabels = new Set(firstPage.items.flatMap((item) => item.lines.map((line) => line.labelId)))
    expect(firstPageLabels.size).toBeGreaterThan(1)

    // Continuations: each asks the SAME limit plus exactly the cursor the previous page returned; no client paging.
    const pages: TimelinePage[] = [firstPage]
    let cursor = firstPage.nextCursor
    while (cursor !== null) {
      expect(pages.length).toBeLessThan(MAX_PAGES)
      const answer = page.waitForResponse(isTimelineCall)
      await page.getByRole('button', { name: 'Daha eski hareketleri yükle', exact: true }).click()
      const response = await answer
      expect(response.status()).toBe(200)
      const url = new URL(response.url())
      expect([...url.searchParams.keys()]).toEqual(['limit', 'cursor'])
      expect(url.searchParams.get('limit')).toBe(String(PAGE_SIZE))
      expect(url.searchParams.get('cursor')).toBe(cursor)
      const next = await response.json() as TimelinePage
      pages.push(next)
      cursor = next.nextCursor
      const shown = pages.reduce((total, item) => total + item.items.length, 0)
      await expect(page.getByText(`${shown} hareket gösteriliyor.`, { exact: true })).toBeVisible()
    }
    expect(pages.length).toBeGreaterThanOrEqual(2)
    expect(timelineCalls).toHaveLength(pages.length)
    await expect(page.getByText('Bu şirketin kayıtlı en eski stok hareketine ulaşıldı.', { exact: true })).toBeVisible()
    const all = pages.flatMap((item) => item.items)
    expect(new Set(all.map((item) => item.movementId)).size).toBe(all.length)
    expect(all).toHaveLength(movementsBefore)

    // Same label / same source evidence: the opened label's own Receipt, sourced by the real opening line.
    const own = all.filter((item) => item.lines.some((line) => line.labelId === target.labelId))
    expect(own).toHaveLength(1)
    expect(own[0]).toMatchObject({
      kind: 'Receipt', sourceKind: 'inventory.opening-count', sourceId: countId, sourceLineId: target.lineId,
      sourceGrain: 'Line',
      lines: [{ direction: 'In', labelId: target.labelId, labelCode: target.labelCode, productId, quantityKg: '2.5' }],
    })
    const ownRow = page.locator(`tr[data-movement-id="${own[0].movementId}"][data-label-id="${target.labelId}"]`)
    await expect(ownRow).toHaveCount(1)
    await expect(ownRow).toHaveAttribute('data-source-id', countId)
    await expect(ownRow).toContainText('inventory.opening-count')
    await expect(ownRow).toContainText(countId)
    await expect(ownRow).toContainText('2.5 kg')
    await expect(ownRow.getByRole('link', { name: target.labelCode, exact: true }))
      .toHaveAttribute('href', `/stok/etiket/${target.labelId}`)
    // Rows of other labels of the same company are on the page too: it is not filtered to the label.
    await expect(page.locator(`tr[data-movement-id]:not([data-label-id="${target.labelId}"])`).first()).toBeVisible()

    // ── 4. Return to the same label. ───────────────────────────────────────────────────────────────────────
    const returnAnswer = page.waitForResponse((response) => response.request().method() === 'GET'
      && new URL(response.url()).pathname === labelPath)
    await page.getByRole('link', { name: '← Etikete dön', exact: true }).click()
    expect((await returnAnswer).status()).toBe(200)
    await expect(page.getByRole('heading', { level: 1, name: target.labelCode, exact: true })).toBeVisible()
    await expect(page).toHaveURL(new RegExp(`/stok/etiket/${target.labelId}$`))

    // ── 5. Fences. ────────────────────────────────────────────────────────────────────────────────────────
    const tampered = await page.request.get(`${env.apiBaseUrl}${MOVEMENTS}?limit=${PAGE_SIZE}&cursor=${encodeURIComponent(`${firstPage.nextCursor}x`)}`, {
      headers: apiHeaders(recorderActor),
    })
    expect(tampered.status()).toBe(400)
    expect(await tampered.json()).toMatchObject({ code: INVALID_QUERY })

    const otherLimit = await page.request.get(`${env.apiBaseUrl}${MOVEMENTS}?limit=${PAGE_SIZE - 1}&cursor=${encodeURIComponent(firstPage.nextCursor!)}`, {
      headers: apiHeaders(recorderActor),
    })
    expect(otherLimit.status()).toBe(400)
    expect(await otherLimit.json()).toMatchObject({ code: INVALID_QUERY })

    const otherActor = await page.request.get(`${env.apiBaseUrl}${MOVEMENTS}?limit=${PAGE_SIZE}&cursor=${encodeURIComponent(firstPage.nextCursor!)}`, {
      headers: apiHeaders(transformerActor),
    })
    expect(otherActor.status()).toBe(400)
    const otherActorBody = await otherActor.text()
    expect(otherActorBody).toContain(INVALID_QUERY)
    expect(otherActorBody).not.toContain(target.labelCode)

    const denied = await page.request.get(`${env.apiBaseUrl}${MOVEMENTS}?limit=${PAGE_SIZE}`, {
      headers: apiHeaders(deniedActor),
    })
    expect(denied.status()).toBe(403)
    expect(denied.headers()['cache-control']).toContain('no-store')
    const deniedBody = await denied.text()
    expect(deniedBody).not.toContain(LABEL_PREFIX)
    expect(deniedBody).not.toContain(countId)

    const foreign = await page.request.get(`${env.apiBaseUrl}${MOVEMENTS}?limit=${PAGE_SIZE}`, {
      headers: apiHeaders(recorderActor, otherCompanyId),
    })
    expect([400, 403]).toContain(foreign.status())
    const foreignBody = await foreign.text()
    expect(foreignBody).not.toContain(LABEL_PREFIX)
    expect(foreignBody).not.toContain(countId)

    // The denied actor's browser mounts no timeline and sends no timeline read.
    const deniedCalls: Response[] = []
    page.on('response', (response) => { if (isTimelineCall(response)) deniedCalls.push(response) })
    await signInAs(page, deniedActor)
    await page.goto(TIMELINE_ROUTE)
    await expect(page.getByText('Bulunamadı — sayfa', { exact: true })).toBeVisible({ timeout: 30_000 })
    await expect(page.getByRole('heading', { level: 1, name: 'Şirket stok hareketleri' })).toHaveCount(0)
    await expect(page.locator('tr[data-movement-id]')).toHaveCount(0)
    expect(deniedCalls).toHaveLength(0)

    // Reading wrote nothing.
    expect(await movementRows(pool)).toBe(movementsBefore)
  } finally {
    await pool.close()
  }
})
