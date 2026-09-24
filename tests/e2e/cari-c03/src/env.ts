export interface C03Environment {
  readonly apiBaseUrl: string
  readonly sqlConnection: string
  readonly database: string
  readonly testedCommit: string
}

function required(name: string): string {
  const value = process.env[name]?.trim()
  if (!value) throw new Error(`${name} is not set; c03 runs only inside the disposable browser stack.`)
  return value
}

export function readEnvironment(): C03Environment {
  const testedCommit = required('BROWSER_E2E_TESTED_COMMIT')
  if (!/^[0-9a-f]{40}$/.test(testedCommit)) throw new Error('BROWSER_E2E_TESTED_COMMIT is not an exact commit')
  const apiBaseUrl = required('BROWSER_E2E_API_BASE_URL')
  if (new URL(apiBaseUrl).hostname !== '127.0.0.1') throw new Error('C03 API must use loopback')
  const database = required('BROWSER_E2E_DATABASE')
  if (!/^GoymenErpC01_[0-9]+_[0-9]+$/.test(database)) throw new Error('C03 database is not disposable')
  const sqlConnection = required('BROWSER_E2E_SQL_CONNECTION')
  if (!sqlConnection.toLowerCase().includes(`database=${database.toLowerCase()}`)) {
    throw new Error('C03 SQL connection is not bound to the disposable database')
  }
  return { apiBaseUrl, sqlConnection, database, testedCommit }
}
