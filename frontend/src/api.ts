import type { Alert, AlertHistory, AnalyticsQuery, AnalyticsResult, DynamicRecord, Entity, EntityIndex, Field, FieldDataType, ImportJob, ImportPreview, Metric, MetricEvaluation, RecordPage, Report, SavedView, Tenant, WebhookSubscription } from './types'

const baseUrl = import.meta.env.VITE_API_URL ?? ''

async function request<T>(path: string, init: RequestInit = {}, tenantId?: string): Promise<T> {
  const headers = new Headers(init.headers)
  if (init.body && !(init.body instanceof FormData)) headers.set('Content-Type', 'application/json')
  if (tenantId) headers.set('X-Tenant-Id', tenantId)
  const response = await fetch(`${baseUrl}${path}`, { ...init, headers })
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as { title?: string; errors?: Array<{ message: string }> } | null
    const details = problem?.errors?.map(error => error.message).join(' ') ?? problem?.title
    throw new Error(details || `Request failed with status ${response.status}.`)
  }
  if (response.status === 204) return undefined as T
  return response.json() as Promise<T>
}

export const api = {
  tenants: () => request<Tenant[]>('/api/tenants'),
  createTenant: (name: string, connectionString: string) => request<Tenant>('/api/tenants', { method: 'POST', body: JSON.stringify({ name, connectionString }) }),
  updateTenant: (tenantId: string, name: string) =>
    request<Tenant>(`/api/tenants/${tenantId}`, { method: 'PATCH', body: JSON.stringify({ name }) }),
  configureTenantConnection: (tenantId: string, connectionString: string) =>
    request<Tenant>(`/api/tenants/${tenantId}/connection`, { method: 'PUT', body: JSON.stringify({ connectionString }) }),
  disableTenant: (tenantId: string) => request<void>(`/api/tenants/${tenantId}/disable`, { method: 'POST' }),
  entities: (tenantId: string) => request<Entity[]>('/api/entities', {}, tenantId),
  createEntity: (tenantId: string, input: { name: string; displayName: string; description?: string }) =>
    request<Entity>('/api/entities', { method: 'POST', body: JSON.stringify(input) }, tenantId),
  fields: (tenantId: string, entityId: string) => request<Field[]>(`/api/entities/${entityId}/fields`, {}, tenantId),
  createField: (tenantId: string, entityId: string, input: {
    name: string; displayName: string; dataType: FieldDataType; isRequired: boolean
    isUnique: boolean; isFilterable: boolean; isSortable: boolean; isFacetable: boolean
    isSearchable: boolean; configuration?: unknown; sortOrder: number
  }) => request<Field>(`/api/entities/${entityId}/fields`, { method: 'POST', body: JSON.stringify(input) }, tenantId),
  updateField: (tenantId: string, entityId: string, fieldId: string, input: {
    name?: string; displayName?: string; isRequired?: boolean; isUnique?: boolean
    isFilterable?: boolean; isSortable?: boolean; isFacetable?: boolean; isSearchable?: boolean
    configuration?: unknown; sortOrder?: number
  }) => request<Field>(`/api/entities/${entityId}/fields/${fieldId}`,
    { method: 'PATCH', body: JSON.stringify(input) }, tenantId),
  indexes: (tenantId: string, entityId: string) => request<EntityIndex[]>(`/api/entities/${entityId}/indexes`, {}, tenantId),
  createIndex: (tenantId: string, entityId: string, columns: Array<{ fieldId: string; descending: boolean }>) =>
    request<EntityIndex>(`/api/entities/${entityId}/indexes`, { method: 'POST', body: JSON.stringify({ columns }) }, tenantId),
  deleteIndex: (tenantId: string, entityId: string, indexId: string) =>
    request<void>(`/api/entities/${entityId}/indexes/${indexId}`, { method: 'DELETE' }, tenantId),
  records: (tenantId: string, entityId: string) =>
    request<RecordPage>(`/api/entities/${entityId}/records?pageSize=100`, {}, tenantId),
  createRecord: (tenantId: string, entityId: string, data: Record<string, unknown>) =>
    request<DynamicRecord>(`/api/entities/${entityId}/records`, { method: 'POST', body: JSON.stringify({ data }) }, tenantId),
  patchRecord: (tenantId: string, entityId: string, record: DynamicRecord, data: Record<string, unknown>) =>
    request<DynamicRecord>(`/api/entities/${entityId}/records/${record.id}`,
      { method: 'PATCH', body: JSON.stringify({ data, expectedVersion: record.version }) }, tenantId),
  deleteRecord: (tenantId: string, entityId: string, record: DynamicRecord) =>
    request<void>(`/api/entities/${entityId}/records/${record.id}`,
      { method: 'DELETE', headers: { 'If-Match': record.version } }, tenantId),
  query: (tenantId: string, entityId: string, query: Record<string, unknown>) =>
    request<RecordPage>(`/api/entities/${entityId}/query`, { method: 'POST', body: JSON.stringify(query) }, tenantId),
  facets: (tenantId: string, entityId: string, fieldId: string) =>
    request<Array<{ value: string; recordCount: number }>>(`/api/entities/${entityId}/fields/${fieldId}/facets`, {}, tenantId),
  views: (tenantId: string, entityId: string) => request<SavedView[]>(`/api/entities/${entityId}/views`, {}, tenantId),
  createView: (tenantId: string, entityId: string, name: string, definition: Record<string, unknown>) =>
    request<SavedView>(`/api/entities/${entityId}/views`, { method: 'POST', body: JSON.stringify({ name, definition }) }, tenantId),
  updateView: (tenantId: string, viewId: string, entityId: string, name: string, definition: Record<string, unknown>) =>
    request<SavedView>(`/api/views/${viewId}`, { method: 'PATCH', body: JSON.stringify({ entityId, name, definition }) }, tenantId),
  deleteView: (tenantId: string, viewId: string) =>
    request<void>(`/api/views/${viewId}`, { method: 'DELETE' }, tenantId),
  uploadImport: async (tenantId: string, entityId: string, file: File) => {
    const body = new FormData(); body.append('file', file)
    return request<ImportJob>(`/api/entities/${entityId}/imports`, { method: 'POST', body }, tenantId)
  },
  previewImport: (tenantId: string, importId: string, mappings: Array<{ sourceColumn: string; targetFieldId: string }>) =>
    request<ImportPreview>(`/api/imports/${importId}/preview`, { method: 'POST', body: JSON.stringify({ mappings }) }, tenantId),
  commitImport: (tenantId: string, importId: string) =>
    request<{ importedRows: number }>(`/api/imports/${importId}/commit`, { method: 'POST' }, tenantId),
  exportRecords: async (tenantId: string, entityId: string) => {
    const response = await fetch(`${baseUrl}/api/entities/${entityId}/records/export`, { headers: { 'X-Tenant-Id': tenantId } })
    if (!response.ok) throw new Error('Export failed.')
    return response.blob()
  },
  previewAnalytics: (tenantId: string, entityId: string, query: AnalyticsQuery, signal?: AbortSignal) =>
    request<AnalyticsResult>(`/api/entities/${entityId}/analytics/preview`, { method: 'POST', body: JSON.stringify(query), signal }, tenantId),
  reports: (tenantId: string, entityId: string) => request<Report[]>(`/api/entities/${entityId}/reports`, {}, tenantId),
  report: (tenantId: string, entityId: string, reportId: string) => request<Report>(`/api/entities/${entityId}/reports/${reportId}`, {}, tenantId),
  saveReport: (tenantId: string, entityId: string, input: Omit<Report, 'id' | 'entityId' | 'createdAt' | 'updatedAt'>, reportId?: string) =>
    request<Report>(`/api/entities/${entityId}/reports${reportId ? `/${reportId}` : ''}`, { method: reportId ? 'PATCH' : 'POST', body: JSON.stringify(input) }, tenantId),
  deleteReport: (tenantId: string, entityId: string, reportId: string) => request<void>(`/api/entities/${entityId}/reports/${reportId}`, { method: 'DELETE' }, tenantId),
  runReport: (tenantId: string, entityId: string, reportId: string) => request<AnalyticsResult>(`/api/entities/${entityId}/reports/${reportId}/run`, { method: 'POST' }, tenantId),
  exportReport: async (tenantId: string, entityId: string, reportId: string) => {
    const response = await fetch(`${baseUrl}/api/entities/${entityId}/reports/${reportId}/export`, { headers: { 'X-Tenant-Id': tenantId } })
    if (!response.ok) throw new Error('Report export failed.'); return response.blob()
  },
  metrics: (tenantId: string, entityId: string) => request<Metric[]>(`/api/entities/${entityId}/metrics`, {}, tenantId),
  saveMetric: (tenantId: string, entityId: string, input: Omit<Metric, 'id' | 'entityId' | 'createdAt' | 'updatedAt'>, metricId?: string) =>
    request<Metric>(`/api/entities/${entityId}/metrics${metricId ? `/${metricId}` : ''}`, { method: metricId ? 'PATCH' : 'POST', body: JSON.stringify(input) }, tenantId),
  evaluateMetric: (tenantId: string, entityId: string, metricId: string) => request<MetricEvaluation>(`/api/entities/${entityId}/metrics/${metricId}/evaluate`, { method: 'POST' }, tenantId),
  previewMetric: (tenantId: string, entityId: string, input: Omit<Metric, 'id' | 'entityId' | 'createdAt' | 'updatedAt'>) => request<MetricEvaluation>(`/api/entities/${entityId}/metrics/preview`, { method: 'POST', body: JSON.stringify(input) }, tenantId),
  deleteMetric: (tenantId: string, entityId: string, metricId: string) => request<void>(`/api/entities/${entityId}/metrics/${metricId}`, { method: 'DELETE' }, tenantId),
  alerts: (tenantId: string, entityId: string) => request<Alert[]>(`/api/entities/${entityId}/alerts`, {}, tenantId),
  saveAlert: (tenantId: string, entityId: string, input: Omit<Alert, 'id' | 'entityId' | 'lastState' | 'lastEvaluatedAt' | 'nextEvaluationAt' | 'createdAt' | 'updatedAt'>, alertId?: string) => request<Alert>(`/api/entities/${entityId}/alerts${alertId ? `/${alertId}` : ''}`, { method: alertId ? 'PATCH' : 'POST', body: JSON.stringify(input) }, tenantId),
  deleteAlert: (tenantId: string, entityId: string, alertId: string) => request<void>(`/api/entities/${entityId}/alerts/${alertId}`, { method: 'DELETE' }, tenantId),
  alertHistory: (tenantId: string, entityId: string, alertId: string) => request<AlertHistory>(`/api/entities/${entityId}/alerts/${alertId}/history`, {}, tenantId),
  testAlert: (tenantId: string, entityId: string, alertId: string) => request<import('./types').AlertEvaluation>(`/api/entities/${entityId}/alerts/${alertId}/test`, { method: 'POST' }, tenantId),
  webhooks: (tenantId: string, entityId: string) => request<WebhookSubscription[]>(`/api/entities/${entityId}/webhooks`, {}, tenantId),
  saveWebhook: (tenantId: string, entityId: string, input: Omit<WebhookSubscription, 'id' | 'entityId' | 'createdAt' | 'updatedAt'>, subscriptionId?: string) =>
    request<WebhookSubscription>(`/api/entities/${entityId}/webhooks${subscriptionId ? `/${subscriptionId}` : ''}`, { method: subscriptionId ? 'PATCH' : 'POST', body: JSON.stringify(input) }, tenantId),
  deleteWebhook: (tenantId: string, entityId: string, subscriptionId: string) => request<void>(`/api/entities/${entityId}/webhooks/${subscriptionId}`, { method: 'DELETE' }, tenantId),
}
