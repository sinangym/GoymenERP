import { readJson, send, type CommandContext } from './discountLimitApi'
import { decodeManufacturingProfileReceipt, decodeManufacturingProfiles, manufacturingProfilePath, type ManufacturingProfileKind, type PrepareManufacturingProfileBody, type PublishManufacturingProfileBody } from './manufacturingProfileContract'

export const fetchManufacturingProfiles = (company: string, kind: ManufacturingProfileKind, signal?: AbortSignal) =>
  readJson(manufacturingProfilePath(kind), company, (raw) => decodeManufacturingProfiles(raw, company, kind), signal)
export const transferManufacturingStartingValues = (context: CommandContext, kind: ManufacturingProfileKind, body: PublishManufacturingProfileBody) =>
  send('POST', `${manufacturingProfilePath(kind)}/starting-values-transfer`, context, body, 200, decodeManufacturingProfileReceipt)
export const prepareManufacturingProfile = (context: CommandContext, kind: ManufacturingProfileKind, body: PrepareManufacturingProfileBody) =>
  send('POST', manufacturingProfilePath(kind), context, body, 200, decodeManufacturingProfileReceipt)
export const publishManufacturingProfile = (context: CommandContext, kind: ManufacturingProfileKind, profile: string, body: PublishManufacturingProfileBody) =>
  send('POST', `${manufacturingProfilePath(kind)}/${encodeURIComponent(profile)}/publication`, context, body, 200, decodeManufacturingProfileReceipt)
