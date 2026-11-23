import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { initialShellState, type ShellState } from '../shell/shellState'
import { fetchBranding, fetchTemplate, writeBranding } from './brandingApi'
import { CompanyDocumentBrandingPage, DocumentTemplatePage } from './DocumentBrandingPage'
import { BRANDING_MANAGE, TEMPLATE_CATALOG } from './brandingContract'
import { BRANDING_TEXT as T } from './brandingText'
const company = '58000000-0000-4000-8000-000000000001'
let currentShell: ShellState
vi.mock('../shell/shellContext', () => ({ useShellState: () => currentShell }))
const security = vi.hoisted(() => ({ capture: vi.fn(() => ({ actorId: 'SENTETIK-maker' })), report: vi.fn() }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => security }))
vi.mock('./brandingApi', () => ({ fetchBranding: vi.fn(), fetchTemplate: vi.fn(), writeBranding: vi.fn() }))
vi.mock('./api', () => ({ DOCUMENTS_API_BASE: '/api/v1', uploadDocument: vi.fn() }))
const clients: QueryClient[] = []
const branding = { companyId: company, version: '1', logo: null, letterhead: null, preparedBy: 'SENTETIK-maker', preparedAtUtc: '2026-11-17T08:00:00Z', reason: 'SENTETIK' }
beforeEach(() => {
  vi.clearAllMocks()
  currentShell = { ...initialShellState, phase: 'idle', committedContextRevision: 1, sessionEpoch: Date.now(), activeContext: {
    userId: 'SENTETIK-maker', companyId: company, scopeMode: 'single', capabilityGeneration: 'g1', capabilities: [BRANDING_MANAGE, TEMPLATE_CATALOG[2].prepare, TEMPLATE_CATALOG[2].publish] } }
  vi.mocked(fetchBranding).mockResolvedValue({ type: 'ok-single', data: { companyId: company, branding: null,
    uploadPolicy: { allowedContentTypes: ['image/png'], maximumUploadBytes: 1024, canUpload: false } } })
  vi.mocked(fetchTemplate).mockResolvedValue({ type: 'ok-single', data: { companyId: company, module: 2,
    templateCode: TEMPLATE_CATALOG[2].code, draft: { ...branding, version: '2', module: 2, templateCode: TEMPLATE_CATALOG[2].code, brandingVersion: '1' },
    publication: null, currentBranding: branding } })
})
afterEach(() => { cleanup(); clients.splice(0).forEach((client) => client.clear()) })
function mount(template = false) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false } } }); clients.push(client)
  return render(<QueryClientProvider client={client}>{template ? <DocumentTemplatePage module={2} /> : <CompanyDocumentBrandingPage />}</QueryClientProvider>)
}
describe('Documents branding management UI source', () => {
  it('shows a genuine initial empty company and actual upload denial without fabricating assets', async () => {
    mount(); expect(await screen.findByText(T.noBranding)).toBeTruthy(); expect(screen.getByText(T.uploadForbidden)).toBeTruthy()
    expect(screen.getAllByRole('button', { name: T.upload }).every((button) => (button as HTMLButtonElement).disabled)).toBe(true)
  })
  it('keeps maker publish disabled and does not require the branding manage capability on the module page', async () => {
    currentShell = { ...currentShell, activeContext: { ...currentShell.activeContext!, capabilities: [TEMPLATE_CATALOG[2].prepare, TEMPLATE_CATALOG[2].publish] } }
    mount(true); expect(await screen.findByText(T.maker)).toBeTruthy()
    expect((screen.getByRole('button', { name: T.publish }) as HTMLButtonElement).disabled).toBe(true)
    expect(fetchBranding).not.toHaveBeenCalled()
  })
  it('freezes the exact command and key after 503 and retries it instead of starting a new write', async () => {
    vi.mocked(writeBranding).mockResolvedValueOnce({ type: 'unknown', reason: 'service-unavailable' })
      .mockResolvedValueOnce({ type: 'refused', status: 409, code: 'document_branding_version_conflict' })
    mount(); await screen.findByText(T.noBranding)
    fireEvent.change(screen.getByLabelText(T.reason), { target: { value: 'SENTETIK reason' } })
    fireEvent.click(screen.getByRole('button', { name: T.save }))
    await screen.findByText(T.unknown)
    expect((screen.getByLabelText(T.reason) as HTMLTextAreaElement).disabled).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: T.retry }))
    await waitFor(() => expect(writeBranding).toHaveBeenCalledTimes(2))
    expect(vi.mocked(writeBranding).mock.calls[0]).toEqual(vi.mocked(writeBranding).mock.calls[1])
  })
  it.each([401, 403])('K3 P2-8: releases the key on a first-send %i; the authority answer is definite', async (status) => {
    vi.mocked(writeBranding).mockResolvedValueOnce({ type: 'refused', status, code: null })
    mount(); await screen.findByText(T.noBranding)
    fireEvent.change(screen.getByLabelText(T.reason), { target: { value: 'SENTETIK reason' } })
    fireEvent.click(screen.getByRole('button', { name: T.save }))
    await waitFor(() => expect(security.report).toHaveBeenCalledWith(status, expect.anything()))
    expect(screen.queryByRole('button', { name: T.retry })).toBeNull()
    expect(screen.queryByText(T.unknown)).toBeNull()
  })
  it.each([[401, null], [403, null], [400, null], [404, null], [415, null]] as const)(
    'K3 P2-8: keeps the key when the retry of an unknown write is answered %i before the receipt', async (status, code) => {
      vi.mocked(writeBranding).mockResolvedValueOnce({ type: 'unknown', reason: 'service-unavailable' })
        .mockResolvedValueOnce({ type: 'refused', status, code })
        .mockResolvedValueOnce({ type: 'unknown', reason: 'network-error' })
      mount(); await screen.findByText(T.noBranding)
      fireEvent.change(screen.getByLabelText(T.reason), { target: { value: 'SENTETIK reason' } })
      fireEvent.click(screen.getByRole('button', { name: T.save }))
      fireEvent.click(await screen.findByRole('button', { name: T.retry }))
      await waitFor(() => expect(writeBranding).toHaveBeenCalledTimes(2))
      fireEvent.click(await screen.findByRole('button', { name: T.retry }))
      await waitFor(() => expect(writeBranding).toHaveBeenCalledTimes(3))
      const calls = vi.mocked(writeBranding).mock.calls
      expect(calls[1]).toEqual(calls[0]); expect(calls[2]).toEqual(calls[0])
    })
  it.each([['offline', /Bağlantı yok/], ['network-error', /Sunucuya ulaşılamadı/], ['malformed', /Beklenmeyen yanıt/]] as const)('distinguishes %s', async (type, text) => {
    vi.mocked(fetchBranding).mockResolvedValue({ type }); mount(); expect(await screen.findByText(text)).toBeTruthy()
  })
  it.each([401, 403, 404, 409, 503])('preserves authority not-found conflict and service status %i', async (status) => {
    vi.mocked(fetchBranding).mockResolvedValue({ type: 'http', status }); mount()
    await waitFor(() => expect(security.report).toHaveBeenCalledWith(status, security.capture.mock.results[0].value))
    expect(screen.queryByText(T.noBranding)).toBeNull()
  })
})
