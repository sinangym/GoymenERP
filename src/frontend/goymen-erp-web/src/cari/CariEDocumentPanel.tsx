/**
 * SAT-D102 — the e-belge profile of the Cari card's selected-company account.
 *
 * What the user can do here, and nothing more:
 *  - READ the e-Fatura / e-Arşiv status ("Kontrol edilmedi" until someone records a check), who checked it and when,
 *    the customer's scenario override, the Settings default and the effective scenario the server resolved.
 *  - RECORD a status check (the server stamps the time and the checker; GİB is not queried — follow-up).
 *  - SET a scenario override from the codes the server read from Settings, or clear it. With no Settings list
 *    ("kaynak yok") there is nothing to choose and the card says so; no code is invented on the client.
 *
 * What is offered comes from the read's `viewer` block, measured by the server for this actor and company; the server
 * re-authorizes every command. The read is a side read of the card and never moves its command-readiness slot; the
 * commands use the card's own contract context.
 */

import { useCallback, useId, useMemo, useState, type FormEvent } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import type { ContractContext } from './clientContract'
import {
  E_DOCUMENT_STATUSES,
  E_DOCUMENT_TEXT as T,
  effectiveScenarioText,
  formatCheckedAt,
  type AccountEDocumentProfile,
  type EDocumentStatus,
} from './eDocument'
import { fetchAccountEDocumentProfile, recordAccountEDocumentStatus, setAccountEInvoiceScenario } from './eDocumentApi'
import { useEDocumentCommand } from './useEDocumentCommand'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import styles from './CariEDocument.module.css'

interface Props {
  readonly partyId: string
  readonly companyId: string
  readonly namespace: string
  readonly contractContext: ContractContext | null
}

const USE_DEFAULT = ''

function settingsDefaultText(profile: AccountEDocumentProfile): string {
  const value = profile.settingsDefault
  if (value.state === 'Set' && value.scenarioCode !== null) return value.scenarioCode
  return value.state === 'NotSet' ? T.defaultNotSet : T.defaultUnavailable
}

export function CariEDocumentPanel({ partyId, companyId, namespace, contractContext }: Props) {
  const queryClient = useQueryClient()
  const boundary = useAuthBoundary()
  const statusSelectId = useId()
  const scenarioSelectId = useId()
  const queryKey = useMemo(() => [namespace, 'counterparty-account-e-document', partyId, companyId], [
    namespace,
    partyId,
    companyId,
  ])

  const read = useQuery({
    queryKey,
    queryFn: async ({ signal }) => {
      const security = boundary.capture()
      const outcome = await fetchAccountEDocumentProfile(partyId, companyId, signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, security)
      }
      return outcome
    },
  })

  const refresh = useCallback(() => {
    void queryClient.invalidateQueries({ queryKey })
  }, [queryClient, queryKey])
  const command = useEDocumentCommand(companyId, contractContext, `cari.e-document:${partyId}`, refresh)
  const [statusChoice, setStatusChoice] = useState<EDocumentStatus | null>(null)
  const [scenarioChoice, setScenarioChoice] = useState<string | null>(null)

  if (read.isPending) {
    return (
      <div className={styles.panel} data-testid="cari-e-document" data-state="loading">
        <h3 className={styles.title}>{T.panelHeading}</h3>
        <p className={styles.muted} role="status">
          {T.panelLoading}
        </p>
      </div>
    )
  }

  const outcome = read.data
  if (outcome === undefined || outcome.type !== 'ok') {
    const forbidden = outcome?.type === 'http' && outcome.status === 403
    return (
      <div className={styles.panel} data-testid="cari-e-document" data-state={forbidden ? 'forbidden' : 'error'}>
        <h3 className={styles.title}>{T.panelHeading}</h3>
        <p className={styles.error} role="alert">
          {forbidden ? T.panelForbidden : T.panelError}
        </p>
        {forbidden ? null : (
          <div className={styles.actions}>
            <button type="button" className={styles.button} onClick={() => void read.refetch()}>
              {T.readAgain}
            </button>
          </div>
        )}
      </div>
    )
  }

  const profile = outcome.data
  const locked = command.busy || command.unresolved
  const selectedStatus = statusChoice ?? profile.status
  // Unknown → Unknown changes nothing; every other choice records a new check (a re-check of the same status counts).
  const statusSubmittable = !(selectedStatus === 'Unknown' && profile.status === 'Unknown')
  const currentOverride = profile.scenarioOverrideCode ?? USE_DEFAULT
  const selectedScenario = scenarioChoice ?? currentOverride
  const scenarioChanged = selectedScenario !== currentOverride
  const listedCodes = profile.scenarioCodes.state === 'Set' ? profile.scenarioCodes.codes : []
  const overrideNotListed = profile.scenarioOverrideCode !== null && !listedCodes.includes(profile.scenarioOverrideCode)

  // The forms are offered only with a VKN, so a token exists whenever a command can be sent.
  const profileToken = profile.profileToken ?? ''
  const checkedText =
    profile.statusCheckedAtUtc === null
      ? T.notChecked
      : profile.statusCheckedInActiveCompany === true
        ? T.checkedValue(formatCheckedAt(profile.statusCheckedAtUtc), profile.statusCheckedByDisplayName ?? T.checkerUnknown)
        : T.checkedValue(formatCheckedAt(profile.statusCheckedAtUtc), T.checkedInOtherCompany)

  const submitStatus = async (event: FormEvent) => {
    event.preventDefault()
    if (!statusSubmittable || locked || profile.profileToken === null) return
    const ok = await command.run(
      `status:${partyId}`,
      (context) => recordAccountEDocumentStatus(context, partyId, profile.version, profileToken, selectedStatus),
      selectedStatus === 'Unknown' ? T.statusReset : T.statusRecorded,
    )
    if (ok) setStatusChoice(null)
  }

  const submitScenario = async (event: FormEvent) => {
    event.preventDefault()
    if (!scenarioChanged || locked || profile.profileToken === null) return
    const target = selectedScenario === USE_DEFAULT ? null : selectedScenario
    const ok = await command.run(
      `scenario:${partyId}`,
      (context) => setAccountEInvoiceScenario(context, partyId, profile.version, profileToken, target),
      target === null ? T.scenarioCleared : T.scenarioSaved,
    )
    if (ok) setScenarioChoice(null)
  }

  return (
    <div className={styles.panel} data-testid="cari-e-document" data-state="ready">
      <h3 className={styles.title}>{T.panelHeading}</h3>
      <p className={styles.muted}>{T.panelNote}</p>
      {profile.accountIsActive ? null : <p className={styles.muted}>{T.passiveAccountNote}</p>}
      <dl className={styles.facts}>
        <dt>{T.statusLabel}</dt>
        <dd data-e-document-status={profile.status}>{T.status[profile.status]}</dd>
        <dt>{T.checkedLabel}</dt>
        <dd
          data-e-document-checked={
            profile.statusCheckedAtUtc === null
              ? 'none'
              : profile.statusCheckedInActiveCompany === true
                ? 'this-company'
                : 'other-company'
          }
        >
          {checkedText}
        </dd>
      </dl>

      {!profile.taxIdentityNumberPresent ? (
        <p className={styles.notice} data-no-tax-identity role="note">
          {T.noTaxIdentity}
        </p>
      ) : profile.viewer.canUpdate ? (
        <form className={styles.form} onSubmit={(event) => void submitStatus(event)} aria-busy={command.busy}>
          <label htmlFor={statusSelectId}>{T.statusSelectLabel}</label>
          <select
            id={statusSelectId}
            value={selectedStatus}
            disabled={locked}
            onChange={(event) => setStatusChoice(event.target.value as EDocumentStatus)}
          >
            {E_DOCUMENT_STATUSES.map((status) => (
              <option key={status} value={status}>
                {T.status[status]}
              </option>
            ))}
          </select>
          <p className={styles.muted}>{T.gibNote}</p>
          <div className={styles.actions}>
            <button type="submit" className={styles.button} disabled={!statusSubmittable || locked}>
              {command.busy ? T.saving : T.recordStatus}
            </button>
          </div>
        </form>
      ) : (
        <p className={styles.muted}>{T.readOnlyNote}</p>
      )}

      <h4 className={styles.subtitle}>{T.scenarioHeading}</h4>
      <dl className={styles.facts}>
        <dt>{T.overrideLabel}</dt>
        <dd>
          {profile.scenarioOverrideCode === null ? (
            T.noOverride
          ) : (
            <span className={styles.code} data-e-invoice-override={profile.scenarioOverrideCode}>
              {profile.scenarioOverrideCode}
              {overrideNotListed && profile.scenarioCodes.state !== 'Unavailable' ? T.notListedSuffix : ''}
            </span>
          )}
        </dd>
        <dt>{T.settingsDefaultLabel}</dt>
        <dd data-e-invoice-default={profile.settingsDefault.state}>{settingsDefaultText(profile)}</dd>
        <dt>{T.effectiveLabel}</dt>
        <dd data-e-invoice-effective={profile.effectiveScenario.outcome}>
          {effectiveScenarioText(profile.effectiveScenario)}
        </dd>
      </dl>
      <p className={styles.muted}>{T.evaluatedOn(profile.evaluatedOn)}</p>

      {profile.viewer.canUpdate && profile.taxIdentityNumberPresent ? (
        profile.scenarioCodes.state === 'Unavailable' ? (
          <p className={styles.error} role="alert">
            {T.codeListUnavailable}
          </p>
        ) : (
          <>
            {profile.scenarioCodes.state === 'NotSet' ? (
              <p className={styles.notice} data-e-invoice-codes="missing">
                {T.codeListMissing}
              </p>
            ) : null}
            {profile.scenarioCodes.state === 'Set' || profile.scenarioOverrideCode !== null ? (
              <form className={styles.form} onSubmit={(event) => void submitScenario(event)} aria-busy={command.busy}>
                <label htmlFor={scenarioSelectId}>{T.scenarioSelectLabel}</label>
                <select
                  id={scenarioSelectId}
                  value={selectedScenario}
                  disabled={locked}
                  onChange={(event) => setScenarioChoice(event.target.value)}
                >
                  <option value={USE_DEFAULT}>{T.useDefaultOption}</option>
                  {listedCodes.map((code) => (
                    <option key={code} value={code}>
                      {code}
                    </option>
                  ))}
                  {overrideNotListed && profile.scenarioOverrideCode !== null ? (
                    <option value={profile.scenarioOverrideCode}>
                      {`${profile.scenarioOverrideCode}${T.notListedSuffix}`}
                    </option>
                  ) : null}
                </select>
                <div className={styles.actions}>
                  <button type="submit" className={styles.button} disabled={!scenarioChanged || locked}>
                    {command.busy ? T.saving : T.saveScenario}
                  </button>
                </div>
              </form>
            ) : null}
          </>
        )
      ) : null}

      <div aria-live="polite">
        {command.notice === null ? null : (
          <p
            className={command.notice.tone === 'ok' ? styles.ok : styles.error}
            role={command.notice.tone === 'ok' ? 'status' : 'alert'}
            data-notice={command.notice.tone}
          >
            {command.notice.text}
          </p>
        )}
      </div>
      {command.unresolved && !command.busy ? (
        <div className={styles.actions}>
          <button type="button" className={styles.button} onClick={() => void command.retry()}>
            {T.retrySame}
          </button>
        </div>
      ) : null}
    </div>
  )
}
