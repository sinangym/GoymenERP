import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { DispatchNotePdfButton } from './DispatchNotePdfButton'
import { DISPATCH_PDF_TEXT as T } from './dispatchPdfText'
vi.mock('../shell/shellContext', () => ({ useShellState: () => ({ activeContext: { userId: 'SENTETIK', capabilityGeneration: 'g1' }, sessionEpoch: 1, committedContextRevision: 1 }) }))
const boundary = vi.hoisted(() => ({ capture: vi.fn(() => 'SENTETIK-boundary'), report: vi.fn() }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => boundary }))
afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.clearAllMocks() })
const props = { companyId: '58000000-0000-4000-8000-000000000001', instructionId: '58000000-0000-4000-8000-000000000002', dispatchNoteId: '58000000-0000-4000-8000-000000000003' }
describe('Logistics dispatch PDF source workflow', () => {
  it('shows the explicit page limit refusal instead of claiming an empty or partial PDF', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ code: 'logistics_dispatch_pdf_too_large' }), { status: 422 })))
    render(<DispatchNotePdfButton {...props} />); fireEvent.click(screen.getByRole('button', { name: T.generate }))
    expect(await screen.findByText(T.tooLarge)).toBeTruthy(); expect(screen.queryByRole('link', { name: T.download })).toBeNull()
  })
  it.each([403, 404, 409, 503])('keeps refusal %i distinct and never offers a PDF link', async (status) => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status })))
    render(<DispatchNotePdfButton {...props} />); fireEvent.click(screen.getByRole('button', { name: T.generate }))
    await waitFor(() => expect(boundary.report).toHaveBeenCalledWith(status, 'SENTETIK-boundary'))
    expect(screen.queryByRole('link', { name: T.download })).toBeNull()
  })
})
