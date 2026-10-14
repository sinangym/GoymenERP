/**
 * MFG-PLAN-EDIT on the plan detail: component/DOM claims over the real shell machinery with synthetic fixtures (see
 * ./testHarness). What SQL and the real host do is the relational acceptance and the `mfg` browser suite.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MANUFACTURING_CAPABILITY } from './capabilities'
import { decodePlanRevisions } from './planRevisionContract'
import { revisePlan, type RevisePlanRequest } from './planCommandApi'
import {
  APPROVER,
  COMPANY_A,
  COMPANY_B,
  CUT_LINE_1,
  CUT_LINE_2,
  PLAN,
  ROLL_1,
  ROLL_2,
  WORK_CENTER,
  cutToLengthSpec,
  installFetch,
  json,
  plan,
  planRevision,
  planRevisions,
  plannedInput,
  problem,
  renderAt,
  requestsFor,
  revisionInput,
  technicalSnapshot,
} from './testHarness'

const EDITOR_CAPABILITIES = [
  MANUFACTURING_CAPABILITY.planDetail,
  MANUFACTURING_CAPABILITY.planEdit,
  MANUFACTURING_CAPABILITY.planApprove,
]
const OTHER_LINE = 'd0000000-0000-4000-8000-000000000009'
const ACTOR = '99999999-9999-9999-9999-999999999999'

afterEach(() => {
  vi.unstubAllGlobals()
})

/** A stateful synthetic server: the plan, its history and one revision endpoint answering `answer`. */
function server(
  answer?: (body: RevisePlanRequest, count: number) => Response | 'throw' | undefined,
  initialHistory: readonly unknown[] = [planRevision()],
  initialPlan: Record<string, unknown> = plan(),
) {
  let storedPlan: Record<string, unknown> = initialPlan
  let history: readonly unknown[] = initialHistory
  let revisions = 0
  const respond = (url: string, init: RequestInit | undefined) => {
    if (url.endsWith(`/manufacturing/plans/${PLAN}`) && init?.method !== 'POST') return json(storedPlan)
    if (url.endsWith(`/manufacturing/plans/${PLAN}/revisions`)) return json(planRevisions(history))
    if (url.endsWith('/organization/work-centers')) {
      return json([
        { id: WORK_CENTER, locationId: WORK_CENTER, code: 'DL-1', name: 'Dilme 1', kind: 'Slitting', isActive: true, canOpenLocationDetail: false },
        { id: OTHER_LINE, locationId: WORK_CENTER, code: 'DL-2', name: 'Dilme 2', kind: 'Slitting', isActive: true, canOpenLocationDetail: false },
      ])
    }
    if (url.endsWith(`/manufacturing/plans/${PLAN}/revision`) && init?.method === 'POST') {
      revisions += 1
      const body = JSON.parse(init.body as string) as RevisePlanRequest
      const custom = answer?.(body, revisions)
      if (custom === 'throw') throw new TypeError('connection lost')
      if (custom !== undefined) return custom
      // The server reads an omitted cut spec as null (ManufacturingApiV1 revise): that is the destructive path.
      storedPlan = plan({
        process: storedPlan.process,
        version: body.expectedVersion + 1,
        workCenterId: body.workCenterId,
        inputs: body.inputs.map((input) => plannedInput(input.parentRollId, input.plannedKg)),
        target: body.target,
        cutToLengthSpec: body.cutToLengthSpec ?? null,
      })
      history = [...history, planRevision({
        revision: history.length,
        planVersion: body.expectedVersion + 1,
        workCenterId: body.workCenterId,
        inputs: body.inputs.map((input) => revisionInput(
          input.parentRollId,
          input.plannedKg,
          input.expectedLabelVersion,
          input.commercialContext ?? null,
        )),
        target: body.target,
        reason: body.reason,
        revisedBy: ACTOR,
        recordedAtUtc: '2026-09-27T09:30:00Z',
        cutToLengthSpec: body.cutToLengthSpec ?? null,
      })]
      return json(storedPlan, 200, revisions > 1 ? {
        'Idempotency-Replayed': 'true',
        'Idempotency-Original-Correlation-Id': 'corr-1',
      } : {})
    }
    return undefined
  }
  return {
    respond,
    setPlan: (value: ReturnType<typeof plan>) => { storedPlan = value },
    stored: () => ({ plan: storedPlan, history }),
  }
}

/** A legacy stored plan and history: written before the additive cut spec member existed, so it is absent. */
function withoutCutSpec<T extends { readonly cutToLengthSpec?: unknown }>(value: T): Omit<T, 'cutToLengthSpec'> {
  const { cutToLengthSpec: _omitted, ...legacy } = value
  return legacy
}

const STORED_CUT_LINES = [
  { lineId: CUT_LINE_2, sequence: 20, lengthMm: '1250.5', count: 3 },
  { lineId: CUT_LINE_1, sequence: -1, lengthMm: '0.001', count: 2147483647 },
]

async function changeFirstRollKg(user: ReturnType<typeof userEvent.setup>, kg: string, reason: string) {
  const field = screen.getByRole('textbox', { name: `${ROLL_1} planlanan kg` })
  await user.clear(field)
  await user.type(field, kg)
  await user.type(screen.getByRole('textbox', { name: 'Düzenleme gerekçesi' }), reason)
  await user.click(screen.getByRole('button', { name: 'Değişikliği kaydet' }))
}

async function openEditForm(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('button', { name: 'Planı düzenle' }))
  await screen.findByRole('heading', { name: 'Taslak planı düzenle' })
  // The work centers are read by the form itself.
  await screen.findByRole('option', { name: /DL-2/ })
}

describe('plan edit on the detail screen', () => {
  it('edits the draft as a whole, re-reads the plan and its history, and the reopened plan shows the change', async () => {
    const user = userEvent.setup()
    const fake = server()
    const recorded = installFetch({ capabilities: EDITOR_CAPABILITIES, respond: fake.respond })
    renderAt(`/uretim/plan/${PLAN}`)

    await openEditForm(user)
    // The approval is not offered while the draft is being edited.
    expect(screen.queryByRole('button', { name: 'Planı onayla' })).toBeNull()
    const kg = screen.getByRole('textbox', { name: `${ROLL_1} planlanan kg` })
    expect((kg as HTMLInputElement).value).toBe('1000.125')
    await user.clear(kg)
    await user.type(kg, '800')
    await user.click(screen.getByRole('button', { name: `${ROLL_2} kaldır` }))
    await user.selectOptions(screen.getByRole('combobox', { name: 'İş merkezi' }), OTHER_LINE)
    await user.selectOptions(screen.getByLabelText('Hedef türü'), 'GoodOutputMass')
    await user.type(screen.getByLabelText('Hedef kg'), '700')
    await user.type(screen.getByRole('textbox', { name: 'Düzenleme gerekçesi' }), 'Bobin kalitesi nedeniyle azaltıldı')
    await user.click(screen.getByRole('button', { name: 'Değişikliği kaydet' }))

    expect(await screen.findByText('Plan güncellendi (sürüm 2); güncel kayıt ve geçmiş yeniden okundu.')).toBeTruthy()
    const [sent] = requestsFor(recorded, `/manufacturing/plans/${PLAN}/revision`, 'POST')
    expect(sent.headers).toMatchObject({ 'X-Company-Id': COMPANY_A })
    expect(sent.headers['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/i)
    expect(sent.body).toEqual({
      expectedVersion: 1,
      workCenterId: OTHER_LINE,
      inputs: [{ parentRollId: ROLL_1, plannedKg: '800', expectedLabelVersion: '7', commercialContext: null }],
      target: { basis: 'GoodOutputMass', kg: '700' },
      reason: 'Bobin kalitesi nedeniyle azaltıldı',
    })

    // The detail is the re-read plan: version 2, one roll at 800 kg, the new work center; the form is closed.
    expect(screen.queryByRole('heading', { name: 'Taslak planı düzenle' })).toBeNull()
    expect(screen.getByText(OTHER_LINE)).toBeTruthy()
    expect(screen.getByRole('row', { name: new RegExp(`${ROLL_1}.*800`) })).toBeTruthy()
    const history = screen.getByRole('region', { name: 'Plan geçmişi' })
    const latest = within(history).getByRole('listitem', { name: 'Revizyon 1' })
    expect(within(latest).getByText('Gerekçe: Bobin kalitesi nedeniyle azaltıldı')).toBeTruthy()
    expect(within(latest).getByText(`Ana rulo ${ROLL_1}: 1000.125 kg → 800 kg`)).toBeTruthy()
    expect(within(latest).getByText(`Çıkarılan ana rulo ${ROLL_2}: 500.5 kg`)).toBeTruthy()
    expect(within(latest).getByText(`İş merkezi: ${WORK_CENTER} → ${OTHER_LINE}`)).toBeTruthy()
    expect(within(latest).getByText('Hedef: hedef yok → İyi ürün kütlesi 700 kg')).toBeTruthy()
    expect(within(history).getByRole('listitem', { name: 'Revizyon 0' })).toBeTruthy()
    // The next step reads the edited plan: the approval names the revised version.
    expect(screen.getByRole('button', { name: 'Planı onayla' })).toBeTruthy()
  })

  it('preserves each current input commercial context byte-for-byte in the revision body', async () => {
    const commercialContext = {
      partyId: 'A1000000-0000-4000-8000-000000000001',
      companyPartyAccountId: 'A1000000-0000-4000-8000-000000000002',
      partyVersion: ' party sürümü \u00a0',
      accountVersion: '\taccount-version\n',
    }
    const user = userEvent.setup()
    const fake = server(undefined, [planRevision({
      inputs: [
        revisionInput(ROLL_1, '1000.125', '7', commercialContext),
        revisionInput(ROLL_2, '500.5', '8', null),
      ],
    })])
    const recorded = installFetch({ capabilities: EDITOR_CAPABILITIES, respond: fake.respond })
    renderAt(`/uretim/plan/${PLAN}`)

    await openEditForm(user)
    const kg = screen.getByRole('textbox', { name: `${ROLL_1} planlanan kg` })
    await user.clear(kg)
    await user.type(kg, '900')
    await waitFor(() => {
      expect(requestsFor(recorded, '/manufacturing/plans/preview', 'POST')
        .some((request) => (request.body as { inputs: RevisePlanRequest['inputs'] }).inputs[0]?.plannedKg === '900'))
        .toBe(true)
    })
    const previewRequest = requestsFor(recorded, '/manufacturing/plans/preview', 'POST').at(-1)!
    expect((previewRequest.body as { inputs: RevisePlanRequest['inputs'] }).inputs).toEqual([
      { parentRollId: ROLL_1, plannedKg: '900', expectedLabelVersion: '7', commercialContext },
      { parentRollId: ROLL_2, plannedKg: '500.5', expectedLabelVersion: '8', commercialContext: null },
    ])
    await user.type(screen.getByRole('textbox', { name: 'Düzenleme gerekçesi' }), 'Kg değişti')
    await user.click(screen.getByRole('button', { name: 'Değişikliği kaydet' }))

    expect(await screen.findByText('Plan güncellendi (sürüm 2); güncel kayıt ve geçmiş yeniden okundu.')).toBeTruthy()
    const [sent] = requestsFor(recorded, `/manufacturing/plans/${PLAN}/revision`, 'POST')
    expect((sent.body as RevisePlanRequest).inputs).toEqual([
      { parentRollId: ROLL_1, plannedKg: '900', expectedLabelVersion: '7', commercialContext },
      { parentRollId: ROLL_2, plannedKg: '500.5', expectedLabelVersion: '8', commercialContext: null },
    ])
  })

  it('re-sends an unknown edit with the same key and body and shows the replayed answer', async () => {
    const user = userEvent.setup()
    const fake = server((_, count) => (count === 1 ? 'throw' : undefined))
    const recorded = installFetch({ capabilities: EDITOR_CAPABILITIES, respond: fake.respond })
    renderAt(`/uretim/plan/${PLAN}`)

    await openEditForm(user)
    const kg = screen.getByRole('textbox', { name: `${ROLL_2} planlanan kg` })
    await user.clear(kg)
    await user.type(kg, '450')
    await user.type(screen.getByRole('textbox', { name: 'Düzenleme gerekçesi' }), 'Kg düzeltildi')
    await user.click(screen.getByRole('button', { name: 'Değişikliği kaydet' }))
    await user.click(await screen.findByRole('button', { name: 'Aynı istek ve anahtarla yeniden gönder' }))

    expect(await screen.findByText('Plan güncellendi (sürüm 2); güncel kayıt ve geçmiş yeniden okundu.')).toBeTruthy()
    const [first, second] = requestsFor(recorded, `/manufacturing/plans/${PLAN}/revision`, 'POST')
    expect(second.body).toEqual(first.body)
    expect(second.headers['Idempotency-Key']).toBe(first.headers['Idempotency-Key'])
  })

  it('a legacy plan without the cut spec member reads, revises and reopens without sending one', async () => {
    const user = userEvent.setup()
    const fake = server(undefined, [withoutCutSpec(planRevision())], withoutCutSpec(plan()))
    const recorded = installFetch({ capabilities: EDITOR_CAPABILITIES, respond: fake.respond })
    renderAt(`/uretim/plan/${PLAN}`)

    await openEditForm(user)
    await changeFirstRollKg(user, '900', 'Eski plan kg düzeltmesi')

    expect(await screen.findByText('Plan güncellendi (sürüm 2); güncel kayıt ve geçmiş yeniden okundu.')).toBeTruthy()
    const [sent] = requestsFor(recorded, `/manufacturing/plans/${PLAN}/revision`, 'POST')
    // The legacy request shape is kept: no cut spec member at all, not an explicit null.
    expect(sent.body).not.toHaveProperty('cutToLengthSpec')
    // Reopen: the re-read current history (now with an explicit null member) decodes and starts the next edit.
    await openEditForm(user)
    expect((screen.getByRole('textbox', { name: `${ROLL_1} planlanan kg` }) as HTMLInputElement).value).toBe('900')
  })

  it('keeps stored cut lines unchanged through GET history → edit/revise → reopen when another field changes', async () => {
    const user = userEvent.setup()
    const fake = server(
      undefined,
      [planRevision({ cutToLengthSpec: cutToLengthSpec() })],
      plan({ process: 'CutToLength', cutToLengthSpec: cutToLengthSpec() }),
    )
    const recorded = installFetch({ capabilities: EDITOR_CAPABILITIES, respond: fake.respond })
    renderAt(`/uretim/plan/${PLAN}`)

    await openEditForm(user)
    await changeFirstRollKg(user, '900', 'Kg değişti, kesim aynı')

    expect(await screen.findByText('Plan güncellendi (sürüm 2); güncel kayıt ve geçmiş yeniden okundu.')).toBeTruthy()
    const [sent] = requestsFor(recorded, `/manufacturing/plans/${PLAN}/revision`, 'POST')
    expect((sent.body as RevisePlanRequest).cutToLengthSpec).toEqual({ lines: STORED_CUT_LINES })
    expect(fake.stored().plan.cutToLengthSpec).toEqual({ lines: STORED_CUT_LINES })

    // Reopen from the re-read history and change another field again: the lines still travel as stored.
    await openEditForm(user)
    await user.selectOptions(screen.getByRole('combobox', { name: 'İş merkezi' }), OTHER_LINE)
    await user.type(screen.getByRole('textbox', { name: 'Düzenleme gerekçesi' }), 'Hat değişti')
    await user.click(screen.getByRole('button', { name: 'Değişikliği kaydet' }))

    expect(await screen.findByText('Plan güncellendi (sürüm 3); güncel kayıt ve geçmiş yeniden okundu.')).toBeTruthy()
    const second = requestsFor(recorded, `/manufacturing/plans/${PLAN}/revision`, 'POST')[1]
    expect(second.body).toMatchObject({ expectedVersion: 2, workCenterId: OTHER_LINE, cutToLengthSpec: { lines: STORED_CUT_LINES } })
    expect(fake.stored().history.at(-1)).toMatchObject({ cutToLengthSpec: { lines: STORED_CUT_LINES } })
  })

  it.each([
    ['dropped', null],
    ['re-sorted', cutToLengthSpec({ lines: [...STORED_CUT_LINES].reverse() })],
    ['renumbered', cutToLengthSpec({ lines: STORED_CUT_LINES.map((line, index) => ({ ...line, sequence: index + 1 })) })],
  ])('a 200 whose cut lines come back %s stays unknown and re-sends the same key and body', async (_case, answered) => {
    const user = userEvent.setup()
    const fake = server(
      (body, count) => count === 1
        ? json(plan({
          process: 'CutToLength',
          version: body.expectedVersion + 1,
          inputs: body.inputs.map((input) => plannedInput(input.parentRollId, input.plannedKg)),
          cutToLengthSpec: answered,
        }))
        : undefined,
      [planRevision({ cutToLengthSpec: cutToLengthSpec() })],
      plan({ process: 'CutToLength', cutToLengthSpec: cutToLengthSpec() }),
    )
    const recorded = installFetch({ capabilities: EDITOR_CAPABILITIES, respond: fake.respond })
    renderAt(`/uretim/plan/${PLAN}`)

    await openEditForm(user)
    await changeFirstRollKg(user, '900', 'Kg değişti')

    expect(await screen.findByText('Düzenlemenin sonucu bilinmiyor. Aynı istek ve anahtarla yeniden gönderin.')).toBeTruthy()
    expect(screen.queryByText(/Plan güncellendi/)).toBeNull()
    // While unresolved the form is closed: nothing typed afterwards can reach the frozen attempt.
    expect(screen.queryByRole('heading', { name: 'Taslak planı düzenle' })).toBeNull()
    await user.click(screen.getByRole('button', { name: 'Aynı istek ve anahtarla yeniden gönder' }))

    expect(await screen.findByText('Plan güncellendi (sürüm 2); güncel kayıt ve geçmiş yeniden okundu.')).toBeTruthy()
    const [first, second] = requestsFor(recorded, `/manufacturing/plans/${PLAN}/revision`, 'POST')
    expect((first.body as RevisePlanRequest).cutToLengthSpec).toEqual({ lines: STORED_CUT_LINES })
    expect(second.body).toEqual(first.body)
    expect(second.headers['Idempotency-Key']).toBe(first.headers['Idempotency-Key'])
  })

  it('re-sends an unknown cut-line edit with the same key and the same frozen cut spec', async () => {
    const user = userEvent.setup()
    const fake = server(
      (_, count) => (count === 1 ? 'throw' : undefined),
      [planRevision({ cutToLengthSpec: cutToLengthSpec() })],
      plan({ process: 'CutToLength', cutToLengthSpec: cutToLengthSpec() }),
    )
    const recorded = installFetch({ capabilities: EDITOR_CAPABILITIES, respond: fake.respond })
    renderAt(`/uretim/plan/${PLAN}`)

    await openEditForm(user)
    await changeFirstRollKg(user, '950', 'Bağlantı koptu')
    await user.click(await screen.findByRole('button', { name: 'Aynı istek ve anahtarla yeniden gönder' }))

    expect(await screen.findByText('Plan güncellendi (sürüm 2); güncel kayıt ve geçmiş yeniden okundu.')).toBeTruthy()
    const [first, second] = requestsFor(recorded, `/manufacturing/plans/${PLAN}/revision`, 'POST')
    expect(second.body).toEqual(first.body)
    expect((second.body as RevisePlanRequest).cutToLengthSpec).toEqual({ lines: STORED_CUT_LINES })
    expect(second.headers['Idempotency-Key']).toBe(first.headers['Idempotency-Key'])
  })

  it('a stale version is refused, the plan and history are re-read, and nothing is claimed as saved', async () => {
    const user = userEvent.setup()
    const fake = server(() => problem(409, { code: 'manufacturing_version_conflict', expectedVersion: '1', currentVersion: '2' }))
    const recorded = installFetch({ capabilities: EDITOR_CAPABILITIES, respond: fake.respond })
    renderAt(`/uretim/plan/${PLAN}`)

    await openEditForm(user)
    await user.type(screen.getByRole('textbox', { name: 'Düzenleme gerekçesi' }), 'Hedef eklendi')
    await user.selectOptions(screen.getByLabelText('Hedef türü'), 'InputMass')
    await user.type(screen.getByLabelText('Hedef kg'), '1500')
    await user.click(screen.getByRole('button', { name: 'Değişikliği kaydet' }))

    expect(await screen.findByText(/Plan siz düzenlerken değişti\..*Güncel plan yeniden okundu\./)).toBeTruthy()
    expect(requestsFor(recorded, `/manufacturing/plans/${PLAN}/revisions`, 'GET').length).toBeGreaterThanOrEqual(2)
    expect(screen.queryByText(/Plan güncellendi/)).toBeNull()
  })

  it.each([
    ['manufacturing_plan_process_fixed', 'Seçilen iş merkezi planın sürecini (Slitting) yürütmüyor; planın süreci değişmez.'],
    ['manufacturing_plan_not_draft', 'Plan artık taslak değil (onaylandı ya da emir açtı); onaylı plan düzenlenmez.'],
    ['manufacturing_plan_slitting_spec_fixed', 'Dilme ayrıntısı taşıyan plan düzenlenmez; ana ruloyla dengesi iş kararı bekliyor (AT-044).'],
  ])('a %s refusal is explained and nothing is claimed as saved', async (code, message) => {
    const user = userEvent.setup()
    const fake = server(() => problem(422, { code }))
    installFetch({ capabilities: EDITOR_CAPABILITIES, respond: fake.respond })
    renderAt(`/uretim/plan/${PLAN}`)

    await openEditForm(user)
    await user.selectOptions(screen.getByRole('combobox', { name: 'İş merkezi' }), OTHER_LINE)
    await user.type(screen.getByRole('textbox', { name: 'Düzenleme gerekçesi' }), 'Hat değişikliği')
    await user.click(screen.getByRole('button', { name: 'Değişikliği kaydet' }))

    expect(await screen.findByText(message)).toBeTruthy()
    expect(screen.queryByText(/Plan güncellendi/)).toBeNull()
  })

  it('refuses a roll without a label version and a missing reason before sending', async () => {
    const user = userEvent.setup()
    const fake = server()
    const recorded = installFetch({
      capabilities: EDITOR_CAPABILITIES,
      respond: (url, init) => {
        if (url.endsWith(`/manufacturing/plans/${PLAN}/revisions`)) {
          return json(planRevisions([planRevision({ inputs: [revisionInput(ROLL_1), revisionInput(ROLL_2, '500.5', null)] })]))
        }
        return fake.respond(url, init)
      },
    })
    renderAt(`/uretim/plan/${PLAN}`)

    await openEditForm(user)
    await user.click(screen.getByRole('button', { name: 'Değişikliği kaydet' }))
    expect(await screen.findByText('Etiket sürümü olmayan ana rulo yeniden eklenmeden kaydedilemez.')).toBeTruthy()

    await user.click(screen.getByRole('button', { name: `${ROLL_2} kaldır` }))
    await user.click(screen.getByRole('button', { name: 'Değişikliği kaydet' }))
    expect(await screen.findByText('Düzenleme gerekçesi zorunludur (en fazla 256 karakter).')).toBeTruthy()
    expect(requestsFor(recorded, `/manufacturing/plans/${PLAN}/revision`, 'POST')).toHaveLength(0)

    // Cancelling closes the form; reopening starts from the stored content again, the removed roll back in place.
    await user.click(screen.getByRole('button', { name: 'Vazgeç' }))
    await openEditForm(user)
    expect((screen.getByRole('textbox', { name: `${ROLL_2} planlanan kg` }) as HTMLInputElement).value).toBe('500.5')
  })

  it('refuses a change of nothing before sending', async () => {
    const user = userEvent.setup()
    const fake = server()
    const recorded = installFetch({ capabilities: EDITOR_CAPABILITIES, respond: fake.respond })
    renderAt(`/uretim/plan/${PLAN}`)

    await openEditForm(user)
    await user.type(screen.getByRole('textbox', { name: 'Düzenleme gerekçesi' }), 'Deneme')
    await user.click(screen.getByRole('button', { name: 'Değişikliği kaydet' }))

    expect(await screen.findByText('Düzenleme planda hiçbir şeyi değiştirmiyor.')).toBeTruthy()
    expect(requestsFor(recorded, `/manufacturing/plans/${PLAN}/revision`, 'POST')).toHaveLength(0)
  })

  it('draws no edit control without its capability, and explains why an approved plan is not edited', async () => {
    const withoutEdit = server()
    installFetch({
      capabilities: [MANUFACTURING_CAPABILITY.planDetail, MANUFACTURING_CAPABILITY.planApprove],
      respond: withoutEdit.respond,
    })
    const first = renderAt(`/uretim/plan/${PLAN}`)
    expect(await screen.findByRole('button', { name: 'Planı onayla' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Planı düzenle' })).toBeNull()
    // The history is the plan read's: shown without the edit control.
    expect(await screen.findByRole('region', { name: 'Plan geçmişi' })).toBeTruthy()
    first.view.unmount()
    vi.unstubAllGlobals()

    const approved = server()
    approved.setPlan(plan({ status: 'Approved', version: 2, approvedByUserId: APPROVER, approvedAtUtc: '2026-09-27T09:00:00Z' }))
    installFetch({ capabilities: EDITOR_CAPABILITIES, respond: approved.respond })
    renderAt(`/uretim/plan/${PLAN}`)
    expect(await screen.findByText(/Onaylı plan düzenlenmez: onay bir karar kaydetti/)).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Planı düzenle' })).toBeNull()
  })

  it('does not open the edit when the history does not show the plan version on screen', async () => {
    const fake = server()
    installFetch({
      capabilities: EDITOR_CAPABILITIES,
      respond: (url, init) => {
        if (url.endsWith(`/manufacturing/plans/${PLAN}`) && init?.method !== 'POST') return json(plan({ version: 3 }))
        return fake.respond(url, init)
      },
    })
    renderAt(`/uretim/plan/${PLAN}`)

    expect(await screen.findByText('Plan geçmişi planın güncel sürümünü göstermiyor; güncel planı yeniden okuyun.')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Planı düzenle' })).toBeNull()
  })
})

describe('plan revision contract and transport', () => {
  it('decodes one ordered history and refuses anything that is not this contract', () => {
    const commercialContext = {
      partyId: 'a0000000-0000-4000-8000-000000000001',
      companyPartyAccountId: 'a0000000-0000-4000-8000-000000000002',
      partyVersion: ' party:v01 ',
      accountVersion: 'account:v02/α',
    }
    const capturedTechnicalSnapshot = technicalSnapshot()
    const edit = planRevision({
      revision: 1,
      planVersion: 2,
      reason: 'Gerekçe',
      revisedBy: ACTOR,
      inputs: [revisionInput(ROLL_1, '800', '7', commercialContext, capturedTechnicalSnapshot)],
    })
    const decoded = decodePlanRevisions(planRevisions([planRevision(), edit]))
    expect(decoded?.revisions).toHaveLength(2)
    expect(decoded?.revisions[0].inputs[0].commercialContext).toBeNull()
    expect(decoded?.revisions[1].inputs[0].commercialContext).toEqual(commercialContext)
    expect(decoded?.revisions[1].inputs[0].technicalSnapshot).toEqual(capturedTechnicalSnapshot)

    const { technicalSnapshot: _technicalOmitted, ...commercialOnlyInput } = revisionInput(
      ROLL_1, '1000.125', '7', commercialContext,
    )
    expect(decodePlanRevisions(planRevisions([planRevision({ inputs: [commercialOnlyInput] })]))
      ?.revisions[0].inputs[0]).toMatchObject({ commercialContext, technicalSnapshot: null })

    const {
      commercialContext: _commercialOmitted,
      technicalSnapshot: _legacyTechnicalOmitted,
      ...legacyInput
    } = revisionInput(ROLL_1)
    expect(decodePlanRevisions(planRevisions([planRevision({ inputs: [legacyInput] })]))
      ?.revisions[0].inputs[0]).toMatchObject({ commercialContext: null, technicalSnapshot: null })

    const { commercialContext: _invalidOmitted, ...technicalOnlyInput } = revisionInput(
      ROLL_1, '1000.125', '7', null, capturedTechnicalSnapshot,
    )

    expect(decoded?.revisions.every((revision) => revision.cutToLengthSpec === null)).toBe(true)
    expect(decodePlanRevisions(planRevisions([withoutCutSpec(planRevision()), withoutCutSpec(edit)]))
      ?.revisions.map((revision) => revision.cutToLengthSpec)).toEqual([null, null])
    expect(decodePlanRevisions(planRevisions([planRevision(), { ...edit, cutToLengthSpec: cutToLengthSpec() }]))
      ?.revisions[1].cutToLengthSpec).toEqual({ lines: STORED_CUT_LINES })

    const refused: unknown[] = [
      planRevisions([planRevision({ cutToLengthSpec: { lines: [] } })]),
      planRevisions([planRevision({ cutToLengthSpec: { ...cutToLengthSpec(), extra: 1 } })]),
      planRevisions([planRevision({ cutToLengthSpec: cutToLengthSpec({
        lines: [STORED_CUT_LINES[0], { ...STORED_CUT_LINES[1], lineId: CUT_LINE_2 }],
      }) })]),
      planRevisions([planRevision({ cutToLengthSpec: cutToLengthSpec({
        lines: [{ ...STORED_CUT_LINES[0], lengthMm: '0' }],
      }) })]),
      planRevisions([planRevision({ cutToLengthSpec: cutToLengthSpec({
        lines: [{ ...STORED_CUT_LINES[0], count: 0 }],
      }) })]),
      planRevisions([planRevision({ cutToLengthSpec: 'none' })]),
      planRevisions([]),
      planRevisions([edit]),
      planRevisions([planRevision(), { ...edit, revision: 2 }]),
      planRevisions([planRevision(), { ...edit, planVersion: 1 }]),
      planRevisions([planRevision(), { ...edit, reason: null }]),
      planRevisions([planRevision({ reason: 'yaratılışta gerekçe olmaz' })]),
      planRevisions([planRevision({ inputs: [revisionInput(ROLL_1, '1000.10')] })]),
      planRevisions([planRevision({ inputs: [revisionInput(ROLL_1, '10', '07')] })]),
      planRevisions([planRevision({ inputs: [revisionInput(ROLL_1), revisionInput(ROLL_1)] })]),
      planRevisions([planRevision({ inputs: [revisionInput(ROLL_1, '10', '7', {
        ...commercialContext,
        partyId: '00000000-0000-0000-0000-000000000000',
      })] })]),
      planRevisions([planRevision({ inputs: [revisionInput(ROLL_1, '10', '7', {
        ...commercialContext,
        partyVersion: '',
      })] })]),
      planRevisions([planRevision({ inputs: [revisionInput(ROLL_1, '10', '7', {
        ...commercialContext,
        accountVersion: 2,
      })] })]),
      planRevisions([planRevision({ inputs: [revisionInput(ROLL_1, '10', '7', {
        ...commercialContext,
        companyId: COMPANY_B,
      })] })]),
      planRevisions([planRevision({ inputs: [{ ...revisionInput(ROLL_1), commercialContext: undefined }] })]),
      planRevisions([planRevision({ inputs: [{ ...revisionInput(ROLL_1), technicalSnapshot: undefined }] })]),
      planRevisions([planRevision({ inputs: [revisionInput(
        ROLL_1, '10', '7', null, technicalSnapshot({ thicknessMm: '2.0001' }),
      )] })]),
      planRevisions([planRevision({ inputs: [technicalOnlyInput] })]),
      planRevisions([planRevision({ extra: true })]),
      { ...planRevisions(), extra: 1 },
    ]
    for (const body of refused) expect(decodePlanRevisions(body)).toBeNull()
  })

  it('trusts a 200 only when it describes this attempt', async () => {
    const request: RevisePlanRequest = {
      expectedVersion: 1,
      workCenterId: WORK_CENTER,
      inputs: [{
        parentRollId: ROLL_1,
        plannedKg: '800.500',
        expectedLabelVersion: '7',
        commercialContext: null,
      }],
      target: null,
      reason: 'Gerekçe',
    }
    const answers = [
      plan({ version: 2, inputs: [plannedInput(ROLL_1, '800.5')] }),
      plan({ version: 3, inputs: [plannedInput(ROLL_1, '800.5')] }),
      plan({ version: 2, inputs: [plannedInput(ROLL_1, '800')] }),
      plan({ version: 2, workCenterId: OTHER_LINE, inputs: [plannedInput(ROLL_1, '800.5')] }),
    ]
    const outcomes: string[] = []
    for (const answer of answers) {
      vi.stubGlobal('fetch', vi.fn(async () => json(answer)))
      const outcome = await revisePlan(COMPANY_A, PLAN, { kind: 'revise', idempotencyKey: 'key-1', request })
      outcomes.push(outcome.type)
    }
    expect(outcomes).toEqual(['applied', 'unknown', 'unknown', 'unknown'])

    const cutRequest: RevisePlanRequest = { ...request, cutToLengthSpec: { lines: STORED_CUT_LINES } }
    const cutAnswer = (spec: unknown) => plan({
      process: 'CutToLength',
      version: 2,
      inputs: [plannedInput(ROLL_1, '800.5')],
      cutToLengthSpec: spec,
    })
    const cutAnswers = [
      cutAnswer(cutToLengthSpec()),
      cutAnswer(cutToLengthSpec({ lines: STORED_CUT_LINES.map((line) => ({ ...line, lineId: line.lineId.toUpperCase() })) })),
      cutAnswer(null),
      cutAnswer(cutToLengthSpec({ lines: [...STORED_CUT_LINES].reverse() })),
      cutAnswer(cutToLengthSpec({ lines: [STORED_CUT_LINES[0]] })),
      cutAnswer(cutToLengthSpec({ lines: [STORED_CUT_LINES[0], { ...STORED_CUT_LINES[1], sequence: 1 }] })),
      cutAnswer(cutToLengthSpec({ lines: [STORED_CUT_LINES[0], { ...STORED_CUT_LINES[1], lineId: 'c0000000-0000-4000-8000-000000000003' }] })),
      cutAnswer(cutToLengthSpec({ lines: [STORED_CUT_LINES[0], { ...STORED_CUT_LINES[1], lengthMm: '0.002' }] })),
      cutAnswer(cutToLengthSpec({ lines: [STORED_CUT_LINES[0], { ...STORED_CUT_LINES[1], count: 1 }] })),
    ]
    const cutOutcomes: string[] = []
    for (const answer of cutAnswers) {
      vi.stubGlobal('fetch', vi.fn(async () => json(answer)))
      const outcome = await revisePlan(COMPANY_A, PLAN, { kind: 'revise', idempotencyKey: 'key-3', request: cutRequest })
      cutOutcomes.push(outcome.type)
    }
    expect(cutOutcomes).toEqual(['applied', 'applied', ...Array<string>(7).fill('unknown')])
    // A request without cut lines is not described by an answer that carries some.
    vi.stubGlobal('fetch', vi.fn(async () => json(cutAnswer(cutToLengthSpec()))))
    expect((await revisePlan(COMPANY_A, PLAN, { kind: 'revise', idempotencyKey: 'key-4', request })).type).toBe('unknown')

    vi.stubGlobal('fetch', vi.fn(async () => problem(422, { code: 'manufacturing_plan_revision_no_change' })))
    const refused = await revisePlan(COMPANY_A, PLAN, { kind: 'revise', idempotencyKey: 'key-2', request })
    expect(refused).toMatchObject({ type: 'failed', status: 422, code: 'manufacturing_plan_revision_no_change' })
  })
})
