export type CompanySummary = {
  id: string
  code: string
  legalName: string
  isPlaceholder: boolean
}

export type SiteSummary = {
  id: string
  code: string
  name: string
  type: string
  isActive: boolean
}

export type WorkCenterSummary = {
  id: string
  locationId: string
  code: string
  name: string
  kind: string
  isActive: boolean
}

export type OrganizationSnapshot = {
  sites: SiteSummary[]
  workCenters: WorkCenterSummary[]
}

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? '/api/v1'
const developmentUser = import.meta.env.VITE_DEV_USER ?? 'company-a-user'
const bootstrapIdentityEnabled =
  import.meta.env.DEV ||
  import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

export async function getCompanies(signal?: AbortSignal) {
  return request<CompanySummary[]>('/organization/companies', undefined, signal)
}

export async function getOrganizationSnapshot(
  companyId: string,
  signal?: AbortSignal,
): Promise<OrganizationSnapshot> {
  const headers = { 'X-Company-Id': companyId }
  const [sites, workCenters] = await Promise.all([
    request<SiteSummary[]>('/organization/sites', headers, signal),
    request<WorkCenterSummary[]>('/organization/work-centers', headers, signal),
  ])

  return { sites, workCenters }
}

async function request<T>(
  path: string,
  headers?: Record<string, string>,
  signal?: AbortSignal,
): Promise<T> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    headers: {
      ...developmentHeaders(),
      ...headers,
    },
    signal,
  })

  if (!response.ok) {
    throw new Error(`İstek tamamlanamadı (${response.status}).`)
  }

  return (await response.json()) as T
}

function developmentHeaders(): Record<string, string> {
  return bootstrapIdentityEnabled
    ? { 'X-Development-User': developmentUser }
    : {}
}
