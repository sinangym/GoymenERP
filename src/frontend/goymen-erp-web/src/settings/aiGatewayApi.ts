import { noBody, readJson, send, type CommandContext } from './discountLimitApi'
import {
  AI_GATEWAY_PATH,
  AI_GATEWAY_USAGE_PATH,
  AI_GATEWAY_VERSIONS_PATH,
  decodeAiGatewayPolicy,
  decodeAiGatewayUsage,
  decodeAiSecurityEventPage,
  securityEventsPath,
  type AiEventType,
  type RecordAiGatewayPolicyBody,
} from './aiGatewayContract'

export const fetchAiGatewayPolicy = (company: string, signal?: AbortSignal) =>
  readJson(AI_GATEWAY_PATH, company, (raw) => decodeAiGatewayPolicy(raw, company), signal)

export const fetchAiGatewayUsage = (company: string, signal?: AbortSignal) =>
  readJson(AI_GATEWAY_USAGE_PATH, company, (raw) => decodeAiGatewayUsage(raw, company), signal)

export const fetchAiSecurityEvents = (company: string, eventType: AiEventType, cursor: string | null, signal?: AbortSignal) =>
  readJson(securityEventsPath(eventType, cursor), company, (raw) => decodeAiSecurityEventPage(raw, company, eventType), signal)

export const recordAiGatewayPolicy = (context: CommandContext, body: RecordAiGatewayPolicyBody) =>
  send('POST', AI_GATEWAY_VERSIONS_PATH, context, body, 204, noBody)
