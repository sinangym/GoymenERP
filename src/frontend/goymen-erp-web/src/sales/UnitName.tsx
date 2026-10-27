import styles from './SalesDraftDetailPage.module.css'

/**
 * TEK-11: a unit shown by the code the order itself carries (`orderUnitCodes`); a unit the order does not name is shown
 * as its stored id — never a guessed code.
 */
export function UnitName({ id, unitCodes }: { readonly id: string; readonly unitCodes: ReadonlyMap<string, string> }) {
  const code = unitCodes.get(id)
  return code === undefined ? <span className={styles.identifier}>{id}</span> : <>{code}</>
}
