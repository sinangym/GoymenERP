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
import { ITEM_TYPE_PROBLEM, decodeItemType, itemTypePath } from './itemTypeApi'
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
    const of = (overrides: Record<string, unknown>) => ({ companyId: COMPANY, productId: PRODUCT, version: '1', itemType: 'Steel', ...overrides })
    expect(decodeItemType(of({}), COMPANY, PRODUCT)).not.toBeNull()
    expect(decodeItemType(of({ itemType: 'Copper' }), COMPANY, PRODUCT)).toBeNull()
    expect(decodeItemType(of({ version: '0' }), COMPANY, PRODUCT)).toBeNull()
    expect(decodeItemType(of({ productId: COMPANY }), COMPANY, PRODUCT)).toBeNull()
    // Another company's answer is off-contract, never shown.
    expect(decodeItemType(of({ companyId: '00000000-0000-4000-8000-000000000009' }), COMPANY, PRODUCT)).toBeNull()
    expect(decodeItemType(of({ companyId: undefined }), COMPANY, PRODUCT)).toBeNull()
  })
})

describe('product item type — explicit choice', () => {
  it('preselects nothing for an unset product and sends only an explicitly chosen type', async () => {
    const user = userEvent.setup()
    const sent = install((method) => (method === 'GET' ? view('0', null) : new Response(null, { status: 204 })))
    renderPanel()

    await user.click(await screen.findByRole('button', { name: ITEM_TYPE_TEXT.change }))
    const select = screen.getByRole('combobox', { name: ITEM_TYPE_TEXT.label }) as HTMLSelectElement
    expect(select.value).toBe('')
    await user.type(screen.getByRole('textbox', { name: ITEM_TYPE_TEXT.reasonLabel }), 'SENTETIK')
    expect((screen.getByRole('button', { name: ITEM_TYPE_TEXT.save }) as HTMLButtonElement).disabled).toBe(true)
    expect(sent.some((request) => request.method === 'PUT')).toBe(false)

    await user.selectOptions(select, 'Service')
    await user.click(screen.getByRole('button', { name: ITEM_TYPE_TEXT.save }))
    expect(await screen.findByText(ITEM_TYPE_TEXT.saved)).toBeTruthy()
    expect(sent.filter((request) => request.method === 'PUT').map((request) => request.body)).toEqual([
      { expectedVersion: '0', itemType: 'Service', reason: 'SENTETIK' },
    ])
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

  it('decodes exactly the declared read members and knows only declared problem codes', () => {
    const route = `/api/v1${itemTypePath(PRODUCT)}`.replace(PRODUCT, '{productId}')
    const operations = document.paths[route] as unknown as Record<
      string,
      {
        readonly responses?: Record<
          string,
          {
            readonly 'x-erp-problem-codes'?: readonly string[]
            readonly content?: Record<string, { readonly schema?: { readonly $ref?: string } }>
          }
        >
      }
    >
    expect(operations.get?.responses?.['200']?.content?.['application/json']?.schema?.$ref).toBe(
      '#/components/schemas/ProductItemTypeViewV1',
    )
    expect(Object.keys(document.components.schemas.ProductItemTypeViewV1?.properties ?? {}).sort()).toEqual([
      'companyId', 'itemType', 'productId', 'updatedAtUtc', 'updatedBy', 'version',
    ])
    const declared = new Set(
      Object.values(operations)
        .flatMap((operation) => Object.values(operation.responses ?? {}))
        .flatMap((response) => response['x-erp-problem-codes'] ?? []),
    )
    for (const code of Object.values(ITEM_TYPE_PROBLEM)) expect(declared.has(code), code).toBe(true)
  })
})
