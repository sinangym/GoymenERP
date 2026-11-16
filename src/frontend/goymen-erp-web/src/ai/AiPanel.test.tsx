import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { ShellProvider } from '../shell/ShellProvider'
import { shellRoutes } from '../shell/router'
import { SHELL_CAPABILITY_CONTRACT_VERSION } from '../shell/api'
import { AI_PANEL_CAPABILITY } from './aiCapabilities'
import { AI_TEXT } from './aiText'

const A_ID = 'aaaaaaaa-0000-0000-0000-000000000001'
const B_ID = 'bbbbbbbb-0000-0000-0000-000000000002'
const USER_A = '11111111-1111-1111-1111-111111111111'

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

const TOOL_ID = 'test.item-summary.v1'
const TARGET_ID = 'cccccccc-0000-0000-0000-000000000003'
const DISABLED = { availability: 'disabled', panelAvailable: false, tools: [] }
const READY_TOOL = { toolId: TOOL_ID, version: '1', ownerModule: 'test', dataClass: 'Internal' }
const READY = { availability: 'ready', panelAvailable: true, tools: [READY_TOOL] }

type AskHandler = (body: unknown, companyId: string | null) => Response | Promise<Response>

function installFetch(
  capabilities: readonly string[] = [],
  ai: { readonly capabilities?: () => Response; readonly ask?: AskHandler } = {},
) {
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = typeof input === 'string' ? input : input.toString()
    const companyId = (init?.headers as Record<string, string> | undefined)?.['X-Company-Id'] ?? null
    if (url.includes('/identity/me')) {
      return json({
        userId: USER_A,
        displayName: 'Kullanıcı A',
        email: 'test@example.com',
        allowedCompanyIds: [A_ID, B_ID],
        mustChangePassword: false, localCredentialVersion: null, authenticationSessionBinding: `v1.${'a1'.repeat(32)}`,
      })
    }
    if (url.includes('/organization/companies')) {
      return json([
        { id: A_ID, code: 'GDC', legalName: 'Göymen A', isPlaceholder: true },
        { id: B_ID, code: 'GSP', legalName: 'Göymen B', isPlaceholder: true },
      ])
    }
    if (url.includes('/shell/capabilities')) {
      return json({
        contractVersion: SHELL_CAPABILITY_CONTRACT_VERSION,
        generation: companyId === B_ID ? 'gen-b-1' : 'gen-a-1',
        capabilities,
      })
    }
    // Plan AI-3: the server is the authority on whether AI may be used; by default it says AI is off.
    if (url.endsWith('/ai/capabilities')) return ai.capabilities ? ai.capabilities() : json(DISABLED)
    if (url.endsWith('/ai/ask') && init?.method === 'POST' && ai.ask) {
      return ai.ask(JSON.parse(String(init.body)), companyId)
    }
    throw new Error(`unexpected fetch: ${url}`)
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function askCalls(fetchMock: ReturnType<typeof installFetch>) {
  return fetchMock.mock.calls.filter(([input]) => String(input).includes('/ai/ask'))
}

async function openPanel() {
  await userEvent.click(await screen.findByRole('button', { name: AI_TEXT.toggle }))
  return screen.getByRole('complementary', { name: AI_TEXT.panelLabel })
}

function renderShell() {
  const router = createMemoryRouter(shellRoutes, { initialEntries: ['/'] })
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })
  render(
    <ShellProvider queryClient={queryClient}>
      <RouterProvider router={router} />
    </ShellProvider>,
  )
}

beforeEach(() => {
  try {
    window.localStorage.clear()
  } catch {
    // ignore
  }
  vi.stubGlobal('navigator', { onLine: true })
})
afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('Plan AI-5 — sağ AI paneli', () => {
  it('her sayfada açma düğmesi var; panel kapalı başlar ve açılınca AI kapalı durumunu dürüstçe gösterir', async () => {
    const fetchMock = installFetch()
    renderShell()
    const toggle = await screen.findByRole('button', { name: AI_TEXT.toggle })
    expect(toggle.getAttribute('aria-expanded')).toBe('false')
    expect(screen.queryByRole('complementary', { name: AI_TEXT.panelLabel })).toBeNull()

    await userEvent.click(toggle)
    const panel = screen.getByRole('complementary', { name: AI_TEXT.panelLabel })
    expect(toggle.getAttribute('aria-expanded')).toBe('true')
    expect(toggle.getAttribute('aria-controls')).toBe(panel.id)
    expect(await within(panel).findByText(AI_TEXT.unavailableTitle)).toBeTruthy()
    expect(within(panel).getByText(AI_TEXT.unavailableBody)).toBeTruthy()
    // No input, no send button, no mock answer: AI is off and the panel says so.
    expect(within(panel).queryByRole('textbox')).toBeNull()
    expect(within(panel).getAllByRole('button')).toHaveLength(1)
    // ERP stays usable: the page content is still there next to the panel.
    expect(screen.getByText('Ortak kabuk hazır')).toBeTruthy()
    // The panel asked the server (with the committed company as request context) and never sent a question.
    const capabilityRead = fetchMock.mock.calls.find(([input]) => String(input).endsWith('/ai/capabilities'))
    expect((capabilityRead?.[1]?.headers as Record<string, string>)['X-Company-Id']).toBe(A_ID)
    expect(askCalls(fetchMock)).toHaveLength(0)
  })

  it('yetenek bildirilse de runtime yokken sahte cevap veya giriş alanı çizilmez', async () => {
    const fetchMock = installFetch([AI_PANEL_CAPABILITY])
    renderShell()
    const panel = await openPanel()
    expect(await within(panel).findByText(AI_TEXT.unavailableTitle)).toBeTruthy()
    expect(within(panel).queryByRole('textbox')).toBeNull()
    expect(askCalls(fetchMock)).toHaveLength(0)
  })

  it.each([
    ['yapılandırma hazır değil', { availability: 'unavailable', panelAvailable: false, tools: [] }, AI_TEXT.runtimeUnavailableTitle],
    ['panel yetkisi yok', { availability: 'ready', panelAvailable: false, tools: [] }, AI_TEXT.panelForbiddenTitle],
    ['okunabilir araç yok', { availability: 'ready', panelAvailable: true, tools: [] }, AI_TEXT.noToolsTitle],
  ])('%s: yalnız dürüst durum çizilir, giriş alanı ve gönder düğmesi yoktur', async (_name, body, title) => {
    const fetchMock = installFetch([], { capabilities: () => json(body) })
    renderShell()
    const panel = await openPanel()
    expect(await within(panel).findByText(title)).toBeTruthy()
    expect(within(panel).queryByRole('textbox')).toBeNull()
    expect(within(panel).getAllByRole('button')).toHaveLength(1)
    expect(askCalls(fetchMock)).toHaveLength(0)
  })

  it('bozuk yetenek cevabı (kapalı panelde araç) kabul edilmez; form çizilmez', async () => {
    installFetch([], { capabilities: () => json({ availability: 'disabled', panelAvailable: false, tools: [READY_TOOL] }) })
    renderShell()
    const panel = await openPanel()
    await waitFor(() => expect(panel.querySelector('[data-state="malformed-response"]')).not.toBeNull())
    expect(within(panel).queryByText(AI_TEXT.unavailableTitle)).toBeNull()
    expect(within(panel).queryByRole('textbox')).toBeNull()
    expect(within(panel).queryByRole('button', { name: AI_TEXT.submit })).toBeNull()
  })

  it('araç hazırsa soru sunucuya şirket gövdede olmadan gider; cevap kaynak kaydıyla çizilir', async () => {
    const asked: { body: unknown; companyId: string | null }[] = []
    installFetch([], {
      capabilities: () => json(READY),
      ask: (body, companyId) => {
        asked.push({ body, companyId })
        return json({
          outcome: 'answered',
          toolResults: [
            {
              toolId: TOOL_ID,
              toolVersion: '1',
              outcome: 'succeeded',
              contentJson: '{"name":"Sentetik kalem"}',
              source: {
                targetKind: 'test-item',
                targetId: TARGET_ID,
                title: 'Sentetik kaynak',
                sourceVersion: '7',
                asOfUtc: '2026-11-15T08:00:00+00:00',
                dataClass: 'Internal',
              },
            },
          ],
        })
      },
    })
    renderShell()
    const panel = await openPanel()
    const question = await within(panel).findByRole('textbox', { name: AI_TEXT.questionLabel })
    const reminder = within(panel).getByText(AI_TEXT.questionSensitiveReminder)
    expect(question.getAttribute('aria-describedby')?.split(' ')).toContain(reminder.id)
    await userEvent.type(question, 'Kalem özeti nedir?')
    await userEvent.type(within(panel).getByRole('textbox', { name: AI_TEXT.argumentsLabel }), '{{"itemId":"SENTETIK-1"}')
    await userEvent.click(within(panel).getByRole('button', { name: AI_TEXT.submit }))

    expect(await within(panel).findByText('Sentetik kaynak')).toBeTruthy()
    expect(within(panel).getByText('{"name":"Sentetik kalem"}')).toBeTruthy()
    expect(within(panel).getByText('Şirket içi')).toBeTruthy()
    expect(asked).toEqual([
      {
        body: { question: 'Kalem özeti nedir?', toolId: TOOL_ID, toolVersion: '1', arguments: { itemId: 'SENTETIK-1' } },
        companyId: A_ID,
      },
    ])
  })

  it('istemci doğrulaması: boş soru ve nesne olmayan argüman gönderilmez', async () => {
    const fetchMock = installFetch([], { capabilities: () => json(READY), ask: () => json({}, 500) })
    renderShell()
    const panel = await openPanel()
    await within(panel).findByRole('textbox', { name: AI_TEXT.questionLabel })
    await userEvent.type(within(panel).getByRole('textbox', { name: AI_TEXT.argumentsLabel }), '[[1]')
    await userEvent.click(within(panel).getByRole('button', { name: AI_TEXT.submit }))
    expect(within(panel).getByText(AI_TEXT.questionInvalid)).toBeTruthy()
    expect(within(panel).getByText(AI_TEXT.argumentsInvalid)).toBeTruthy()
    expect(askCalls(fetchMock)).toHaveLength(0)
  })

  it('sunucu ai_request_invalid derse yalnız bilinen alanlar işaretlenir', async () => {
    installFetch([], {
      capabilities: () => json(READY),
      ask: () => json({ status: 400, code: 'ai_request_invalid', invalidFields: ['arguments', 'unknown'] }, 400),
    })
    renderShell()
    const panel = await openPanel()
    await userEvent.type(await within(panel).findByRole('textbox', { name: AI_TEXT.questionLabel }), 'Soru')
    await userEvent.click(within(panel).getByRole('button', { name: AI_TEXT.submit }))
    expect(await within(panel).findByText(AI_TEXT.argumentsInvalid)).toBeTruthy()
    expect(within(panel).queryByText(AI_TEXT.questionInvalid)).toBeNull()
  })

  it.each([
    [503, 'ai_tool_unavailable', AI_TEXT.askToolUnavailableTitle],
    [503, 'ai_disabled', AI_TEXT.askDisabledTitle],
    [403, 'ai_tool_not_allowed', AI_TEXT.askRefusedTitle],
  ])('soru %i %s ile reddedilirse dürüst durum gösterilir, sahte cevap yoktur', async (status, code, title) => {
    installFetch([], { capabilities: () => json(READY), ask: () => json({ status, code }, status) })
    renderShell()
    const panel = await openPanel()
    await userEvent.type(await within(panel).findByRole('textbox', { name: AI_TEXT.questionLabel }), 'Soru')
    await userEvent.click(within(panel).getByRole('button', { name: AI_TEXT.submit }))
    expect(await within(panel).findByText(title)).toBeTruthy()
    expect(within(panel).queryByText(AI_TEXT.answerHeading)).toBeNull()
  })

  it('Escape ve kapat düğmesi paneli kapatır; odak açma düğmesine döner', async () => {
    installFetch()
    renderShell()
    const toggle = await screen.findByRole('button', { name: AI_TEXT.toggle })
    await userEvent.click(toggle)
    expect(document.activeElement?.id).toBe('ai-panel-heading')
    await userEvent.keyboard('{Escape}')
    expect(screen.queryByRole('complementary', { name: AI_TEXT.panelLabel })).toBeNull()
    expect(document.activeElement).toBe(toggle)

    await userEvent.click(toggle)
    await userEvent.click(screen.getByRole('button', { name: AI_TEXT.close }))
    expect(screen.queryByRole('complementary', { name: AI_TEXT.panelLabel })).toBeNull()
    expect(document.activeElement).toBe(toggle)
  })

  it('Escape panel dışındayken (sayfada) paneli kapatmaz; panel kapanınca odak dışarıdaysa yerinde kalır', async () => {
    installFetch()
    renderShell()
    const toggle = await screen.findByRole('button', { name: AI_TEXT.toggle })
    await userEvent.click(toggle)
    const main = screen.getByRole('main')
    main.focus()
    await userEvent.keyboard('{Escape}')
    expect(screen.getByRole('complementary', { name: AI_TEXT.panelLabel })).toBeTruthy()
    // Closing from the toggle while focus is on the toggle: focus stays on the toggle.
    await userEvent.click(toggle)
    expect(screen.queryByRole('complementary', { name: AI_TEXT.panelLabel })).toBeNull()
    expect(document.activeElement).toBe(toggle)
  })

  it('şirket değişince panel kapanır; panel durumu başka şirket bağlamına taşınmaz', async () => {
    installFetch()
    renderShell()
    await userEvent.click(await screen.findByRole('button', { name: AI_TEXT.toggle }))
    expect(screen.getByRole('complementary', { name: AI_TEXT.panelLabel })).toBeTruthy()

    const picker = screen.getByRole('combobox')
    await userEvent.selectOptions(picker, B_ID)
    await waitFor(() => expect((screen.getByRole('combobox') as HTMLSelectElement).value).toBe(B_ID))
    // Focus was on the company picker, not in the panel: closing the panel does not move it to the AI toggle.
    expect(document.activeElement).not.toBe(screen.getByRole('button', { name: AI_TEXT.toggle }))
    await waitFor(() => expect(screen.queryByRole('complementary', { name: AI_TEXT.panelLabel })).toBeNull())
    expect(screen.getByRole('button', { name: AI_TEXT.toggle }).getAttribute('aria-expanded')).toBe('false')
  })
})
