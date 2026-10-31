/**
 * SAT-D134 (EK19) component cases — the product card's item type panel. COMPONENT/DOM EVIDENCE ONLY: `fetch` is stubbed with
 * synthetic answers; the real API and SQL are `SettingsTaxMasterSqlServerRelationalAcceptanceTests`. What these cases
 * establish: a forbidden read shows nothing, an unset type says "kaynak yok", the command sends the company, one
 * Idempotency-Key, the exact body, and keeps the key for a retry after an unknown outcome; the route and body are pinned to
 * the canonical OpenAPI document.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ProductItemTypePanel } from './ProductItemTypePanel'
import { decodeItemType, itemTypePath } from './itemTypeApi'
import { ITEM_TYPE_LABEL, ITEM_TYPE_TEXT } from './itemTypeText'

const COMPANY = 'aaaaaaaa-0000-4000-8000-000000000001'
const PRODUCT = '71000000-0000-4000-8000-000000000001'

interface Sent {
  readonly url: string
  readonly method: string
  readonly headers: Record<string, string>
  readonly body: unknown
}

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

function install(respond: (method: string, attempt: number) => Response): Sent[] {
  const sent: Sent[] = []
  let writes = 0
  vi.stubGlobal('fetch', async (url: string, init?: RequestInit) => {
    const method = init?.method ?? 'GET'
    sent.push({
      url,
      method,
      headers: (init?.headers ?? {}) as Record<string, string>,
      body: typeof init?.body === 'string' ? JSON.parse(init.body) : null,
    })
    if (method !== 'GET') writes += 1
    return respond(method, writes)
  })
  return sent
}

function view(version: string, itemType: string | null) {
  return new Response(
    JSON.stringify({ companyId: COMPANY, productId: PRODUCT, version, itemType, updatedAtUtc: null, updatedBy: null }),
    { status: 200, headers: { 'Content-Type': 'application/json' } },
  )
}

function renderPanel() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <ProductItemTypePanel companyId={COMPANY} productId={PRODUCT} queryNamespace="test" />
    </QueryClientProvider>,
  )
}

describe('product item type panel (SAT-D134)', () => {
  it('shows nothing without the permission (403) and says "kaynak yok" for an unset type', async () => {
    install(() => new Response(null, { status: 403 }))
    const { container } = renderPanel()
    await waitFor(() => expect(container.querySelector('section')).toBeNull())
    cleanup()
    vi.unstubAllGlobals()

    const sent = install(() => view('0', null))
    renderPanel()
    expect(await screen.findByText(ITEM_TYPE_TEXT.unset)).toBeTruthy()
    expect(sent[0].headers['X-Company-Id']).toBe(COMPANY)
    expect(sent.every((request) => request.method === 'GET')).toBe(true)
  })

  it('sets the type with the version, the reason and one key, kept for a retry after an unknown outcome', async () => {
    const user = userEvent.setup()
    const sent = install((method, attempt) => {
      if (method === 'GET') return view('2', 'Steel')
      return attempt === 1 ? new Response(null, { status: 500 }) : new Response(null, { status: 204 })
    })
    renderPanel()

    expect(await screen.findByText(ITEM_TYPE_LABEL.Steel)).toBeTruthy()
    await user.click(screen.getByRole('button', { name: ITEM_TYPE_TEXT.change }))
    await user.selectOptions(screen.getByRole('combobox', { name: ITEM_TYPE_TEXT.label }), 'Scrap')
    await user.type(screen.getByRole('textbox', { name: ITEM_TYPE_TEXT.reasonLabel }), '  SENTETIK  ')
    await user.click(screen.getByRole('button', { name: ITEM_TYPE_TEXT.save }))
    expect(await screen.findByText(ITEM_TYPE_TEXT.unknownOutcome)).toBeTruthy()
    await user.click(screen.getByRole('button', { name: ITEM_TYPE_TEXT.retrySame }))
    expect(await screen.findByText(ITEM_TYPE_TEXT.saved)).toBeTruthy()

    const writes = sent.filter((request) => request.method === 'PUT')
    expect(writes).toHaveLength(2)
    expect(writes[0].url).toMatch(new RegExp(`/product-item-types/${PRODUCT}$`))
    expect(writes[0].body).toEqual({ expectedVersion: '2', itemType: 'Scrap', reason: 'SENTETIK' })
    expect(writes[0].headers['X-Company-Id']).toBe(COMPANY)
    expect(writes[0].headers['Idempotency-Key']).toMatch(/.+/)
    expect(writes[1].headers['Idempotency-Key']).toBe(writes[0].headers['Idempotency-Key'])
  })

  it('fails closed on an off-contract read', () => {
    expect(decodeItemType({ productId: PRODUCT, version: '1', itemType: 'Steel' }, PRODUCT)).not.toBeNull()
    expect(decodeItemType({ productId: PRODUCT, version: '1', itemType: 'Copper' }, PRODUCT)).toBeNull()
    expect(decodeItemType({ productId: PRODUCT, version: '0', itemType: 'Steel' }, PRODUCT)).toBeNull()
    expect(decodeItemType({ productId: COMPANY, version: '1', itemType: 'Steel' }, PRODUCT)).toBeNull()
  })
})

describe('product item type client ↔ canonical OpenAPI', () => {
  const nodeProcess = (globalThis as unknown as {
    readonly process: { readonly cwd: () => string; readonly getBuiltinModule: (name: string) => unknown }
  }).process
  const fs = nodeProcess.getBuiltinModule('node:fs') as { readFileSync(path: string, encoding: 'utf8'): string }
  const path = nodeProcess.getBuiltinModule('node:path') as { resolve(...segments: string[]): string }
  const document = JSON.parse(
    fs.readFileSync(path.resolve(nodeProcess.cwd(), '../../../contracts/openapi/goymen-erp-v1.json'), 'utf8'),
  ) as {
    readonly paths: Record<
      string,
      Record<string, { readonly requestBody?: { readonly content?: Record<string, { readonly schema?: { readonly $ref?: string } }> } }>
    >
    readonly components: { readonly schemas: Record<string, { readonly properties?: Record<string, unknown> }> }
  }

  it('calls the declared route and sends exactly the declared members', () => {
    const route = `/api/v1${itemTypePath(PRODUCT)}`.replace(PRODUCT, '{productId}')
    expect(document.paths[route]?.get).toBeDefined()
    const reference = document.paths[route]?.put?.requestBody?.content?.['application/json']?.schema?.$ref ?? ''
    const schema = document.components.schemas[reference.replace('#/components/schemas/', '')]
    expect(Object.keys(schema?.properties ?? {}).sort()).toEqual(['expectedVersion', 'itemType', 'reason'])
  })
})
