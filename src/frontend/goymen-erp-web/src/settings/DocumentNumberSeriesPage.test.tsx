import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { COMPANY_A, commands, installFetch, json, noContent, renderAt } from './testHarness'
import { SETTINGS_CAPABILITY } from './capabilities'
import { NUMBER_SERIES_TEXT as T } from './documentNumberSeriesText'
afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks() })
describe('KIM-S19 document number format screen', () => {
  it('never selects a document, prefix or sequence width and saves only an explicit format', async () => {
    const calls = installFetch({ capabilities: [SETTINGS_CAPABILITY.documentNumberSeries], respond: (url, init) => {
      if (url.endsWith('/settings/company-policies/document-number-series/4')) return json({ companyId: COMPANY_A, series: 4, versions: [] })
      if (url.endsWith('/settings/company-policies/document-number-series/versions') && init?.method === 'POST') return noContent()
      return undefined
    } })
    renderAt('/ayarlar/belge-numara-serileri'); const user = userEvent.setup()
    expect(await screen.findByLabelText(T.series)).toHaveProperty('value', '')
    expect(commands(calls)).toHaveLength(0)
    await user.selectOptions(screen.getByLabelText(T.series), '4'); await screen.findByText(T.missing)
    await user.click(screen.getByRole('button', { name: T.edit }))
    expect(screen.getByLabelText(T.prefix)).toHaveProperty('value', '')
    expect(screen.getByLabelText(T.digits)).toHaveProperty('value', '')
    await user.type(screen.getByLabelText(T.prefix), 'ALI'); await user.type(screen.getByLabelText(T.digits), '6'); await user.type(screen.getByLabelText(T.reason), 'SENTETIK biçim')
    await user.click(screen.getByRole('button', { name: T.save })); await screen.findByText(T.saved)
    expect(commands(calls)[0].body).toEqual({ series: 4, expectedVersion: '0', prefix: 'ALI', sequenceDigits: 6, reason: 'SENTETIK biçim' })
  })
})
