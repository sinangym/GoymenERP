/**
 * The narrow `mssql` surface the s04 specs use for READ-ONLY SQL evidence (the package ships no types and the suite adds
 * no @types dependency). Only what the specs call: a pool from the run's connection string, typed parameters and a
 * typed `query<T>` whose `recordset` is the rows.
 */
declare module 'mssql' {
  export interface IResult<T> {
    readonly recordset: T[]
  }

  export interface Request {
    input(name: string, type: unknown, value: unknown): Request
    query<T = Record<string, unknown>>(command: string): Promise<IResult<T>>
  }

  export interface ConnectionPool {
    connect(): Promise<ConnectionPool>
    request(): Request
    close(): Promise<void>
  }

  const sql: {
    readonly ConnectionPool: new (connectionString: string) => ConnectionPool
    readonly UniqueIdentifier: unknown
    readonly NVarChar: unknown
  }
  export default sql
}
