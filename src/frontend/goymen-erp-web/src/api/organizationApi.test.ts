import { afterEach, describe, expect, it, vi } from 'vitest'
import { getCompanies, getOrganizationSnapshot } from './organizationApi'

describe('organization API contract', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('uses the server-authorized selector endpoint', async () => {
    const fetchMock = vi.fn().mockImplementation(() =>
      Promise.resolve(new Response('[]', {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      })),
    )
    vi.stubGlobal('fetch', fetchMock)

    await getCompanies()

    expect(fetchMock).toHaveBeenCalledOnce()
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/v1/organization/companies',
      expect.objectContaining({
        headers: expect.objectContaining({
          'X-Development-User': 'company-a-user',
        }),
      }),
    )
  })

  it('sends the selected company context to every scoped request', async () => {
    const fetchMock = vi.fn().mockImplementation(() =>
      Promise.resolve(new Response('[]', {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      })),
    )
    vi.stubGlobal('fetch', fetchMock)

    await getOrganizationSnapshot('company-context-id')

    expect(fetchMock).toHaveBeenCalledTimes(2)
    for (const [, request] of fetchMock.mock.calls) {
      expect(request.headers).toMatchObject({
        'X-Company-Id': 'company-context-id',
      })
    }
  })
})
