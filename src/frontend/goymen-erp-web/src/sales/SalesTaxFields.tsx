/**
 * MOD-03 → Sales (SAT-D93/D97/D140/D147/D158): the tax inputs the order forms share — the order's buyer tax situation (with the
 * exemption code and the buyer's document where the chosen situation needs them) and a line's tax choice. Every option comes
 * from the Settings lists the server answered (`useTaxOptions`); nothing is typed free and nothing is computed here. While the
 * lists cannot be read the fields say why (loading, unavailable, empty with the Settings reason).
 */
import type { ViewOutcome } from '../shell/uiState'
import {
  readOptions,
  situationOf,
  VAT_MODE,
  type BuyerTaxInput,
  type LineTaxChoiceInput,
  type TaxOptions,
} from './orderTax'
import { ORDER_TAX_TEXT as T } from './salesText'
import styles from './SalesPages.module.css'

export function BuyerTaxFields({
  idPrefix,
  value,
  outcome,
  disabled,
  onChange,
}: {
  readonly idPrefix: string
  readonly value: BuyerTaxInput
  readonly outcome: ViewOutcome<TaxOptions> | undefined
  readonly disabled: boolean
  readonly onChange: (next: BuyerTaxInput) => void
}) {
  const { options, notice } = readOptions(outcome)
  const situation = situationOf(options, value.situation)
  const listed = options?.buyerSituations ?? []
  // The stored / start value stays selectable even before the list is read (it is the server's to accept or refuse).
  const choices = listed.some((candidate) => candidate.code === value.situation) || value.situation === ''
    ? listed
    : [{ code: value.situation, name: value.situation, withholdingApplies: true, vatMode: VAT_MODE.normal, documentRequired: false }, ...listed]
  return (
    <fieldset className={styles.line} disabled={disabled} data-testid={`${idPrefix}-buyer-tax`}>
      <legend>{T.buyerSituationLabel}</legend>
      <p className={styles.muted}>{T.buyerSituationHint}</p>
      {notice === null ? null : <p className={styles.muted} role="status" data-testid={`${idPrefix}-buyer-tax-notice`}>{notice}</p>}
      <label htmlFor={`${idPrefix}-buyer-situation`}>{T.buyerSituationLabel}</label>
      <select
        id={`${idPrefix}-buyer-situation`}
        value={value.situation}
        onChange={(event) => onChange({ ...value, situation: event.target.value, exemptionCode: '', documentNo: '', documentDate: '' })}
      >
        {choices.map((candidate) => <option key={candidate.code} value={candidate.code}>{candidate.name}</option>)}
      </select>
      {situation?.vatMode === VAT_MODE.zeroWithExemptionCode ? (
        <>
          <label htmlFor={`${idPrefix}-buyer-exemption`}>{T.exemptionCodeLabel}</label>
          <select
            id={`${idPrefix}-buyer-exemption`}
            value={value.exemptionCode}
            onChange={(event) => onChange({ ...value, exemptionCode: event.target.value })}
          >
            <option value="">{T.exemptionCodeNone}</option>
            {(options?.exemptionCodes ?? []).map((code) => <option key={code.code} value={code.code}>{`${code.code} — ${code.name}`}</option>)}
          </select>
          {options !== null && options.exemptionCodes.length === 0
            ? <p className={styles.muted} data-testid={`${idPrefix}-exemption-empty`}>{T.exemptionCodesEmpty}</p> : null}
        </>
      ) : null}
      {situation?.documentRequired ? (
        <>
          <p className={styles.muted}>{T.documentHint}</p>
          <label htmlFor={`${idPrefix}-buyer-document-no`}>{T.documentNoLabel}</label>
          <input
            id={`${idPrefix}-buyer-document-no`}
            maxLength={64}
            value={value.documentNo}
            onChange={(event) => onChange({ ...value, documentNo: event.target.value })}
          />
          <label htmlFor={`${idPrefix}-buyer-document-date`}>{T.documentDateLabel}</label>
          <input
            id={`${idPrefix}-buyer-document-date`}
            type="date"
            value={value.documentDate}
            onChange={(event) => onChange({ ...value, documentDate: event.target.value })}
          />
        </>
      ) : null}
    </fieldset>
  )
}

export function LineTaxFields({
  idPrefix,
  value,
  options,
  onChange,
}: {
  readonly idPrefix: string
  readonly value: LineTaxChoiceInput
  readonly options: TaxOptions | null
  readonly onChange: (next: LineTaxChoiceInput) => void
}) {
  const situation = situationOf(options, value.buyerSituation)
  return (
    <div data-testid={`${idPrefix}-tax`}>
      <label htmlFor={`${idPrefix}-tax-mode`}>{T.lineTaxLabel}</label>
      <select
        id={`${idPrefix}-tax-mode`}
        value={value.mode}
        onChange={(event) => onChange({ ...value, mode: event.target.value as LineTaxChoiceInput['mode'] })}
      >
        <option value="keep">{T.lineTaxMode.keep}</option>
        <option value="default">{T.lineTaxMode.default}</option>
        <option value="override">{T.lineTaxMode.override}</option>
      </select>
      {value.mode !== 'override' ? null : (
        <>
          <p className={styles.muted}>{T.lineTaxHint}</p>
          <label htmlFor={`${idPrefix}-tax-situation`}>{T.lineTaxSituation}</label>
          <select
            id={`${idPrefix}-tax-situation`}
            value={value.buyerSituation}
            onChange={(event) => onChange({ ...value, buyerSituation: event.target.value, exemptionCode: '' })}
          >
            <option value="">—</option>
            {(options?.buyerSituations ?? []).map((candidate) => <option key={candidate.code} value={candidate.code}>{candidate.name}</option>)}
          </select>
          <label htmlFor={`${idPrefix}-tax-item-type`}>{T.lineTaxItemType}</label>
          <select id={`${idPrefix}-tax-item-type`} value={value.itemType} onChange={(event) => onChange({ ...value, itemType: event.target.value })}>
            <option value="">{T.lineTaxItemTypeProduct}</option>
            {(options?.itemTypes ?? []).map((itemType) => <option key={itemType} value={itemType}>{T.itemType[itemType] ?? itemType}</option>)}
          </select>
          {situation?.vatMode === VAT_MODE.zeroWithExemptionCode ? (
            <>
              <label htmlFor={`${idPrefix}-tax-exemption`}>{T.exemptionCodeLabel}</label>
              <select id={`${idPrefix}-tax-exemption`} value={value.exemptionCode} onChange={(event) => onChange({ ...value, exemptionCode: event.target.value })}>
                <option value="">{T.exemptionCodeNone}</option>
                {(options?.exemptionCodes ?? []).map((code) => <option key={code.code} value={code.code}>{`${code.code} — ${code.name}`}</option>)}
              </select>
            </>
          ) : null}
          <label htmlFor={`${idPrefix}-tax-reason`}>{T.lineTaxReason}</label>
          <textarea
            id={`${idPrefix}-tax-reason`}
            maxLength={4000}
            value={value.reason}
            onChange={(event) => onChange({ ...value, reason: event.target.value })}
          />
        </>
      )}
    </div>
  )
}
