import type { DynamicRecord, Entity, EntityIndex, Field, FieldDataType, ImportJob, ImportPreview, RecordPage, SavedView, Tenant } from './types'

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
  createTenant: (name: string) => request<Tenant>('/api/tenants', { method: 'POST', body: JSON.stringify({ name }) }),
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
}
