/**
 * Role flag picker shared by C-02 create and the CARI-LINK workspace.
 *
 * Four checkboxes, one combined flag value (Customer=1, Supplier=2, Tolling=4,
 * Service=8). The wire value is the OR of the checked flags — one account with
 * several roles, never one account per role (DEC-CAR-001).
 */

import { PARTY_ROLE_FLAG, type PartyRoleName } from './contract'
import { CARI_ROLE_LABEL } from './cariCommandText'
import styles from './CariRoleFieldset.module.css'

const ROLE_CHOICES: readonly { readonly name: PartyRoleName; readonly flag: number }[] = (
  Object.keys(PARTY_ROLE_FLAG) as PartyRoleName[]
).map((name) => ({ name, flag: PARTY_ROLE_FLAG[name] }))

export function CariRoleFieldset({
  idPrefix,
  legend,
  value,
  onChange,
  error,
  disabled,
}: {
  readonly idPrefix: string
  readonly legend: string
  readonly value: number
  readonly onChange: (next: number) => void
  readonly error?: string
  readonly disabled?: boolean
}) {
  const errorId = `${idPrefix}-roles-error`
  return (
    <fieldset className={styles.roles} disabled={disabled} data-invalid={error ? 'true' : undefined}>
      <legend>{legend}</legend>
      <div className={styles.options}>
        {ROLE_CHOICES.map((choice) => {
          const id = `${idPrefix}-role-${choice.name.toLowerCase()}`
          const checked = (value & choice.flag) !== 0
          return (
            <label key={choice.name} htmlFor={id} className={styles.option}>
              <input
                id={id}
                type="checkbox"
                checked={checked}
                aria-invalid={error ? true : undefined}
                aria-describedby={error ? errorId : undefined}
                onChange={() => onChange(checked ? value & ~choice.flag : value | choice.flag)}
              />
              {CARI_ROLE_LABEL[choice.name]}
            </label>
          )
        })}
      </div>
      {error ? (
        <p id={errorId} className={styles.error}>
          {error}
        </p>
      ) : null}
    </fieldset>
  )
}
