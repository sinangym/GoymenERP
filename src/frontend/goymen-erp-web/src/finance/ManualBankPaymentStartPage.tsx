import { useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { fetchCounterparties } from '../cari/api'
import type { CounterpartyListItem } from '../cari/contract'
import { StateBlock } from '../shell/StateBlock'
import { decodeBankAccountMasterPage, type BankAccountMasterItem } from './bankAccountContract'
import { draftContextRequestFor } from './draftContext'
import { useDraftContext, useFinanceRead } from './financePort'

export interface ManualBankPaymentEntryContext {
  readonly partyId: string
  readonly companyAccountId: string
  readonly treasuryAccountId: string
  readonly currencyId: string
  readonly profileId: string
  readonly profileVersion: string
}

export function ManualBankPaymentStartPage({
  companyId,
  onContinue,
}: {
  readonly companyId: string
  readonly onContinue: (entry: ManualBankPaymentEntryContext) => void
}) {
  const [partyId, setPartyId] = useState('')
  const [treasuryAccountId, setTreasuryAccountId] = useState('')
  const [occurredOn, setOccurredOn] = useState('')
  const counterparties = useQuery({
    queryKey: ['finance', companyId, 'manual-bank-payment-start-counterparties'],
    queryFn: ({ signal }) => fetchCounterparties(
      { mode: 'single', companyId },
      {
        partyState: 'active',
        accountState: 'active',
        sort: 'legalName',
        direction: 'asc',
        pageSize: 100,
      },
      signal,
    ),
  })
  const banks = useFinanceRead(
    'manual-bank-payment-start-bank-accounts',
    [],
    (port, scope, signal) => port.listBankAccountMaster(scope, signal),
    decodeBankAccountMasterPage,
  )

  const partyItems = counterparties.data?.type === 'ok-single'
    ? counterparties.data.data.items.filter(activePartyAccount)
    : []
  const bankItems = banks.state.kind === 'ready'
    ? banks.state.data.items.filter(eligibleTreasuryAccount)
    : []
  const selectedParty = partyItems.find((item) => item.id === partyId) ?? null
  const request = useMemo(
    () => selectedParty === null
      ? null
      : draftContextRequestFor(
          treasuryAccountId,
          { partyId: selectedParty.id, companyAccountId: selectedParty.companyAccounts[0].id },
          occurredOn,
        ),
    [occurredOn, selectedParty, treasuryAccountId],
  )
  const canonical = useDraftContext(request)

  const submit = () => {
    if (canonical.state.kind !== 'ready') return
    const value = canonical.state.context
    onContinue({
      partyId: value.party.partyId,
      companyAccountId: value.party.companyAccountId,
      treasuryAccountId: value.bankAccount.id,
      currencyId: value.currency.currencyId,
      profileId: value.currency.profile.reference.profileId,
      profileVersion: value.currency.profile.reference.version,
    })
  }

  return (
    <section aria-labelledby="manual-bank-payment-start-title">
      <h1 id="manual-bank-payment-start-title">Yeni giden banka ödemesi</h1>
      <p>Cari hesabı, ödeme bankası ve işlem tarihini seçin. Para profili sunucudan doğrulanır.</p>

      {counterparties.isPending ? <p role="status">Cari hesapları yükleniyor...</p> : null}
      {counterparties.data !== undefined && counterparties.data.type !== 'ok-single' ? (
        <p role="alert">Cari hesapları doğrulanamadı. Ödeme başlangıcı kapalı tutuldu.</p>
      ) : null}
      {counterparties.data?.type === 'ok-single' && partyItems.length === 0 ? (
        <p role="status">Kullanılabilir aktif cari hesabı yok.</p>
      ) : null}

      {banks.state.kind !== 'ready' ? (
        <StateBlock state={banks.state} subject="ödeme banka hesapları" onRetry={banks.refetch} />
      ) : bankItems.length === 0 ? (
        <p role="status">Ödemeye uygun onaylı banka hesabı yok.</p>
      ) : null}

      <form onSubmit={(event) => { event.preventDefault(); submit() }}>
        <p>
          <label htmlFor="manual-payment-party">Cari hesabı</label>{' '}
          <select id="manual-payment-party" value={partyId} onChange={(event) => setPartyId(event.target.value)}>
            <option value="">Seçiniz</option>
            {partyItems.map((party) => (
              <option key={party.id} value={party.id}>{partyLabel(party)}</option>
            ))}
          </select>
        </p>
        <p>
          <label htmlFor="manual-payment-bank">Banka hesabı</label>{' '}
          <select id="manual-payment-bank" value={treasuryAccountId}
            onChange={(event) => setTreasuryAccountId(event.target.value)}>
            <option value="">Seçiniz</option>
            {bankItems.map((item) => (
              <option key={item.account.id} value={item.account.id}>{item.account.displayName}</option>
            ))}
          </select>
        </p>
        <p>
          <label htmlFor="manual-payment-date">İşlem tarihi</label>{' '}
          <input id="manual-payment-date" type="date" value={occurredOn}
            onChange={(event) => setOccurredOn(event.target.value)} />
        </p>

        {canonical.state.kind === 'loading' ? <p role="status">Ödeme bağlamı doğrulanıyor...</p> : null}
        {request !== null && canonical.state.kind !== 'loading' && canonical.state.kind !== 'ready' ? (
          <p role="alert">Seçilen ödeme bağlamı doğrulanamadı; kayıt formu açılmadı.</p>
        ) : null}
        <button type="submit" disabled={canonical.state.kind !== 'ready'}>Ödeme formuna devam et</button>
      </form>
    </section>
  )
}

function activePartyAccount(item: CounterpartyListItem): boolean {
  return item.isActive && item.companyAccounts.length === 1 && item.companyAccounts[0].isActive
}

function eligibleTreasuryAccount(item: BankAccountMasterItem): boolean {
  return item.account.isActive
    && item.account.isUsableForReceipt
    && item.card.moneyEligibility === 'Eligible'
    && item.card.iban !== null
}

function partyLabel(item: CounterpartyListItem): string {
  return item.tradeName === null
    ? `${item.code} - ${item.legalName}`
    : `${item.code} - ${item.legalName} (${item.tradeName})`
}
