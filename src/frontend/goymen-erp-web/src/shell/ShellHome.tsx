/**
 * Index route for SHELL-PILOT-01. Shows the resolved company context and an
 * honest "no module yet" message — no mock KPI / dashboard / business route
 * (DEC-GOV-007, ERP-000K_SHELL_CONTRACT §11).
 *
 * D-05: the "Onaylarım" entry appears only for an actor the server grants the native Finance approve capability; it
 * shows no count or KPI, only the way to the read-only inbox of the Finance pilot.
 */

import { capabilityFailClosed } from './shellState'
import { useShellState } from './shellContext'
import { useCompanyDirectory } from './useShellOrchestrator'
import { ApprovalsHomeEntry } from '../work/ApprovalsHomeEntry'

export function ShellHome() {
  const state = useShellState()
  const directory = useCompanyDirectory()
  const activeCompany =
    directory.find((company) => company.id === state.activeContext?.companyId) ?? null
  const featuresClosed = capabilityFailClosed(state)

  return (
    <section aria-labelledby="shell-home-heading">
      <h1 id="shell-home-heading">Ortak kabuk hazır</h1>
      <p>
        Aktif şirket:{' '}
        <strong>{activeCompany?.legalName ?? state.activeContext?.companyId ?? '—'}</strong>
      </p>
      <p>
        {featuresClosed
          ? 'Server yetenek bilgisi çözülemedi; yalnız temel kabuk açık, iş modülleri kapalı.'
          : 'Uygulanmış iş modülü eklendikçe bu şirket bağlamında burada görünecek. Sahte gösterge yok.'}
      </p>
      <ApprovalsHomeEntry />
    </section>
  )
}
