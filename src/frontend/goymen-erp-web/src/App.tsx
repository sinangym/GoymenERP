import { useEffect, useMemo, useState } from 'react'
import {
  getCompanies,
  getOrganizationSnapshot,
  type CompanySummary,
  type OrganizationSnapshot,
} from './api/organizationApi'
import './App.css'

function App() {
  const [companies, setCompanies] = useState<CompanySummary[]>([])
  const [selectedCompanyId, setSelectedCompanyId] = useState('')
  const [snapshot, setSnapshot] = useState<OrganizationSnapshot | null>(null)
  const [error, setError] = useState('')
  const [isLoading, setIsLoading] = useState(true)

  useEffect(() => {
    const controller = new AbortController()

    getCompanies(controller.signal)
      .then((result) => {
        setCompanies(result)
        const storedCompanyId = window.localStorage.getItem('goymen-company-id')
        const initialCompanyId = result.some(
          (company) => company.id === storedCompanyId,
        )
          ? storedCompanyId ?? ''
          : (result[0]?.id ?? '')
        setSelectedCompanyId(initialCompanyId)
      })
      .catch((requestError: unknown) => {
        if (!controller.signal.aborted) {
          setError(errorMessage(requestError))
        }
      })
      .finally(() => {
        if (!controller.signal.aborted) {
          setIsLoading(false)
        }
      })

    return () => controller.abort()
  }, [])

  useEffect(() => {
    if (!selectedCompanyId) {
      return
    }

    const controller = new AbortController()
    window.localStorage.setItem('goymen-company-id', selectedCompanyId)

    getOrganizationSnapshot(selectedCompanyId, controller.signal)
      .then(setSnapshot)
      .catch((requestError: unknown) => {
        if (!controller.signal.aborted) {
          setSnapshot(null)
          setError(errorMessage(requestError))
        }
      })

    return () => controller.abort()
  }, [selectedCompanyId])

  const selectedCompany = useMemo(
    () => companies.find((company) => company.id === selectedCompanyId),
    [companies, selectedCompanyId],
  )

  const handleCompanyChange = (companyId: string) => {
    setSnapshot(null)
    setError('')
    setSelectedCompanyId(companyId)
  }

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <div className="brand-mark" aria-label="Göymen ERP">
          <span>G</span>
          <strong>GÖYMEN ERP</strong>
        </div>
        <nav aria-label="Ana menü">
          <a className="active" href="#dashboard">Genel Bakış</a>
          <span>Satış</span>
          <span>Alış</span>
          <span>Stok</span>
          <span>Cari</span>
          <span>Ürün</span>
          <span>Üretim</span>
          <span>Finans</span>
        </nav>
        <div className="scope-note">
          <strong>ERP-000A+B</strong>
          <span>Platform ve şirket omurgası</span>
        </div>
      </aside>

      <main>
        <header className="topbar">
          <label className="company-picker">
            <span>Firma</span>
            <select
              aria-label="Aktif şirket"
              value={selectedCompanyId}
              onChange={(event) => handleCompanyChange(event.target.value)}
              disabled={isLoading || companies.length === 0}
            >
              {companies.map((company) => (
                <option key={company.id} value={company.id}>
                  {company.legalName}
                </option>
              ))}
            </select>
          </label>
          <div className="environment-badge">Geliştirme Ortamı</div>
        </header>

        <section className="content" id="dashboard">
          <div className="page-heading">
            <div>
              <p className="eyebrow">Organizasyon bağlamı</p>
              <h1>{selectedCompany?.legalName ?? 'Şirket seçiliyor…'}</h1>
              <p>
                Her istek, sunucu tarafında yetkili şirket kapsamıyla doğrulanır.
              </p>
            </div>
            {selectedCompany?.isPlaceholder && (
              <span className="placeholder-badge">Resmî bilgi teyidi bekliyor</span>
            )}
          </div>

          {error && <div className="error-banner" role="alert">{error}</div>}

          <div className="summary-grid" aria-live="polite">
            <article>
              <span>Şirket kodu</span>
              <strong>{selectedCompany?.code ?? '—'}</strong>
            </article>
            <article>
              <span>Tesis / depo</span>
              <strong>{snapshot?.sites.length ?? '—'}</strong>
            </article>
            <article>
              <span>İş merkezi</span>
              <strong>{snapshot?.workCenters.length ?? '—'}</strong>
            </article>
            <article>
              <span>Veri kapsamı</span>
              <strong>Şirket izole</strong>
            </article>
          </div>

          <div className="detail-grid">
            <section className="panel">
              <div className="panel-heading">
                <div>
                  <p className="eyebrow">Organizasyon</p>
                  <h2>Tesis ve depolar</h2>
                </div>
                <span>{snapshot?.sites.length ?? 0} kayıt</span>
              </div>
              <ul className="entity-list">
                {snapshot?.sites.map((site) => (
                  <li key={site.id}>
                    <div>
                      <strong>{site.name}</strong>
                      <span>{site.code}</span>
                    </div>
                    <span className="status">Aktif</span>
                  </li>
                ))}
              </ul>
            </section>

            <section className="panel">
              <div className="panel-heading">
                <div>
                  <p className="eyebrow">Üretim omurgası</p>
                  <h2>İş merkezleri</h2>
                </div>
                <span>{snapshot?.workCenters.length ?? 0} kayıt</span>
              </div>
              <ul className="entity-list">
                {snapshot?.workCenters.map((workCenter) => (
                  <li key={workCenter.id}>
                    <div>
                      <strong>{workCenter.name}</strong>
                      <span>{workCenter.code}</span>
                    </div>
                    <span className="status">Aktif</span>
                  </li>
                ))}
              </ul>
            </section>
          </div>
        </section>
      </main>
    </div>
  )
}

function errorMessage(error: unknown) {
  return error instanceof Error
    ? error.message
    : 'Beklenmeyen bir bağlantı hatası oluştu.'
}

export default App
