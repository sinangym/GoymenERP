import { expect, test, type APIResponse, type Page, type Response } from '@playwright/test'
import sql from 'mssql'
import { readEnvironment } from '../src/env'

const env = readEnvironment()
const PARTY_ID = 'c0100000-0000-0000-0000-000000000003'
const COMPANY_A = '10000000-0000-0000-0000-000000000001'
const COMPANY_B = '10000000-0000-0000-0000-000000000002'
const ACTOR = 'group-bootstrap-admin'
const DENIED_ACTOR = 'company-a-user'
const CONTRACT = 'optional-trade-roles-v1'
const DEVELOPMENT_USER_HEADER = 'X-Development-User'
const CONTRACT_HEADER = 'X-ERP-Counterparty-Contract'
const DETAIL_API = `/api/v1/counterparties/${PARTY_ID}`
const DETAIL_ROUTE = `/cari/${PARTY_ID}`
const GDC_LABEL = 'Göymen Demir Çelik İnş. San. ve Tic. Ltd. Şti. (GDC)'
const GSP_LABEL = 'Göymen Sac Profil San. ve Tic. Ltd. Şti. (GSP)'

interface AccountSnapshot {
  readonly partyId: string
  readonly code: string
  readonly legalName: string
  readonly partyIsActive: boolean
  readonly partyVersion: string
  readonly accountId: string
  readonly companyId: string
  readonly roles: number
  readonly accountIsActive: boolean
  readonly accountVersion: string
}

function apiHeaders(actor: string, companyId?: string): Record<string, string> {
  return {
    [DEVELOPMENT_USER_HEADER]: actor,
    [CONTRACT_HEADER]: CONTRACT,
    ...(companyId === undefined ? {} : { 'X-Company-Id': companyId }),
  }
}

async function snapshot(pool: sql.ConnectionPool): Promise<readonly AccountSnapshot[]> {
  const result = await pool.request()
    .input('partyId', sql.UniqueIdentifier, PARTY_ID)
    .query<AccountSnapshot>(`
      SELECT
        CONVERT(varchar(36), p.Id) AS partyId,
        p.Code AS code,
        p.LegalName AS legalName,
        CONVERT(bit, p.IsActive) AS partyIsActive,
        CONVERT(varchar(20), p.Version) AS partyVersion,
        CONVERT(varchar(36), a.Id) AS accountId,
        CONVERT(varchar(36), a.CompanyId) AS companyId,
        a.Roles AS roles,
        CONVERT(bit, a.IsActive) AS accountIsActive,
        CONVERT(varchar(20), a.Version) AS accountVersion
      FROM counterparty.Parties AS p
      INNER JOIN counterparty.CompanyPartyAccounts AS a ON a.PartyId = p.Id
      WHERE p.Id = @partyId
      ORDER BY a.CompanyId, a.Id`)
  return result.recordset
}

function isHeaderlessGroupRead(response: Response): boolean {
  const request = response.request()
  return request.method() === 'GET'
    && new URL(response.url()).pathname === DETAIL_API
    && request.headers()['x-company-id'] === undefined
}

async function body(response: APIResponse): Promise<string> {
  return response.text()
}

test('C03 reads both authorized company relationships, switches company, discloses nothing when denied, and writes nothing', async ({ page }) => {
  const pool = await new sql.ConnectionPool(env.sqlConnection).connect()
  try {
    const before = await snapshot(pool)
    expect(before).toHaveLength(2)
    expect(before.map((row) => row.companyId)).toEqual([COMPANY_A, COMPANY_B])

    await page.setExtraHTTPHeaders({ [DEVELOPMENT_USER_HEADER]: ACTOR })
    await page.goto(DETAIL_ROUTE)
    await expect(page.getByRole('heading', { level: 1, name: 'Ayşe Örnek' })).toBeVisible({ timeout: 30_000 })

    const panel = page.locator('[data-section="company-relationships"]')
    const toggle = panel.getByRole('button', { name: 'Diğer şirketlerdeki hesapları göster', exact: true })
    await expect(toggle).toHaveAttribute('aria-expanded', 'false')

    const groupAnswer = page.waitForResponse(isHeaderlessGroupRead)
    await toggle.click()
    const response = await groupAnswer
    expect(response.status()).toBe(200)
    const groupBody = await response.json() as {
      readonly id: string
      readonly companyAccounts: readonly {
        readonly id: string
        readonly companyId: string
        readonly roles: number
        readonly isActive: boolean
        readonly version: string
      }[]
    }
    expect(groupBody.id).toBe(PARTY_ID)
    expect(groupBody.companyAccounts).toHaveLength(2)
    expect(groupBody.companyAccounts.map((account) => ({
      companyId: account.companyId, roles: account.roles, isActive: account.isActive, version: account.version,
    }))).toEqual([
      { companyId: COMPANY_A, roles: 8, isActive: true, version: '1' },
      { companyId: COMPANY_B, roles: 2, isActive: true, version: '1' },
    ])

    const rows = panel.locator('tbody tr')
    await expect(rows).toHaveCount(2)
    const gdc = rows.filter({ has: page.getByRole('rowheader', { name: GDC_LABEL, exact: true }) })
    const gsp = rows.filter({ has: page.getByRole('rowheader', { name: GSP_LABEL, exact: true }) })
    await expect(gdc).toContainText('Hizmet')
    await expect(gsp).toContainText('Tedarikçi')
    await expect(panel).not.toContainText(PARTY_ID)
    await expect(panel).not.toContainText(groupBody.companyAccounts[0].id)
    await expect(panel).not.toContainText(groupBody.companyAccounts[1].id)

    await gsp.getByRole('button', { name: `${GSP_LABEL} şirketinde aç`, exact: true }).click()
    await expect(page.getByRole('combobox', { name: 'Şirket' })).toHaveValue(COMPANY_B)
    const selectedAccount = page.locator('[data-section="account"]')
    await expect(selectedAccount).toContainText('Tedarikçi')
    await expect(selectedAccount.locator('[data-account-state="active"]')).toBeVisible()
    await expect(panel.getByRole('button', { name: 'Diğer şirketlerdeki hesapları göster', exact: true }))
      .toHaveAttribute('aria-expanded', 'false')

    const fresh = await page.request.get(`${env.apiBaseUrl}${DETAIL_API}`, { headers: apiHeaders(ACTOR) })
    expect(fresh.status()).toBe(200)
    expect(await fresh.json()).toEqual(groupBody)

    await page.setExtraHTTPHeaders({ [DEVELOPMENT_USER_HEADER]: DENIED_ACTOR })
    const deniedPanel = page.locator('[data-section="company-relationships"]')
    const deniedAnswer = page.waitForResponse(isHeaderlessGroupRead)
    await deniedPanel.getByRole('button', { name: 'Diğer şirketlerdeki hesapları göster', exact: true }).click()
    const denied = await deniedAnswer
    expect(denied.status()).toBe(404)
    const deniedBody = await denied.text()
    expect(deniedBody).not.toMatch(/"code"\s*:/)
    expect(deniedBody).not.toContain(PARTY_ID)
    expect(deniedBody).not.toContain('Ayşe Örnek')
    expect(deniedBody).not.toContain(COMPANY_B)

    const terminal = deniedPanel.locator('[data-state="not-found"]')
    await expect(terminal).toBeVisible()
    await expect(terminal).toContainText('Bulunamadı — şirket ilişkileri')
    await expect(terminal).toContainText('İstenen kayıt bulunamadı.')
    await expect(deniedPanel.locator('tbody tr')).toHaveCount(0)
    await expect(deniedPanel).not.toContainText(GSP_LABEL)

    expect(await snapshot(pool)).toEqual(before)
  } finally {
    await pool.close()
  }
})
