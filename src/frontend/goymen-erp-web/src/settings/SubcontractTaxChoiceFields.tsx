import { Field } from '../ui/Field'
import type { SubcontractTaxChoice, SubcontractTaxOptions } from './subcontractTaxContract'
import { SUBCONTRACT_TAX_TEXT as T } from './subcontractTaxText'
export const emptySubcontractTaxChoice = (): SubcontractTaxChoice => ({ useDefault: false, vatRateCode: '', exemptionCode: '', withholdingRatioCode: '' })
export function SubcontractTaxChoiceFields({ options, value, onChange, allowDefault = false }: { readonly options: SubcontractTaxOptions; readonly value: SubcontractTaxChoice
  readonly onChange: (v: SubcontractTaxChoice) => void; readonly allowDefault?: boolean }) {
  const vat = options.vatRates.find(x => x.code === value.vatRateCode)
  return <>
    {allowDefault && <label><input type="checkbox" checked={value.useDefault} disabled={options.default === null}
      onChange={e => onChange({ ...emptySubcontractTaxChoice(), useDefault: e.target.checked })} />{T.default}</label>}
    {!value.useDefault && <><Field label={T.vat}>{p => <select {...p} value={value.vatRateCode} required onChange={e => onChange({ ...value, vatRateCode: e.target.value, exemptionCode: '', withholdingRatioCode: '' })}>
      <option value="">{T.choose}</option>{options.vatRates.map(x => <option key={x.code} value={x.code}>{x.name} — %{x.percent}</option>)}</select>}</Field>
      {vat?.percent === '0' ? <Field label={T.exemption}>{p => <select {...p} required value={value.exemptionCode} onChange={e => onChange({ ...value, exemptionCode: e.target.value })}>
        <option value="">{T.choose}</option>{options.exemptionCodes.map(x => <option key={x.code} value={x.code}>{x.code} — {x.name}</option>)}</select>}</Field> :
        <Field label={T.withholding}>{p => <select {...p} value={value.withholdingRatioCode} disabled={vat === undefined} onChange={e => onChange({ ...value, withholdingRatioCode: e.target.value })}>
          <option value="">{T.none}</option>{options.withholdingRatios.filter(x => x.taxCode !== null && options.withholdingCodes.some(c => c.code === x.taxCode))
            .map(x => <option key={x.code} value={x.code}>{x.name} — {x.taxCode}</option>)}</select>}</Field>}</>}
  </>
}
