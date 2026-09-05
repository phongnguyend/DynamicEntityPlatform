export type FieldDataType =
  | 'Text' | 'LongText' | 'Integer' | 'Decimal' | 'Boolean' | 'Date' | 'DateTime'
  | 'Email' | 'Url' | 'Choice' | 'MultiChoice' | 'Lookup'

export interface Tenant { id: string; name: string; status: string; createdAt: string }
export interface Entity {
  id: string; name: string; displayName: string; description?: string
  schemaVersion: number; status: string; createdAt: string; updatedAt: string
}
export interface Field {
  id: string; name: string; displayName: string; storageKey: string; dataType: FieldDataType
  isRequired: boolean; isUnique: boolean; isFilterable: boolean; isSortable: boolean
  isFacetable: boolean; isSearchable: boolean; defaultValue?: unknown
  configuration?: { values?: string[]; [key: string]: unknown }; sortOrder: number
  indexColumnName?: string
}
export interface EntityIndexColumn { fieldId: string; physicalColumnName: string; sortOrder: number; isDescending: boolean }
export interface EntityIndex { id: string; indexName: string; status: string; createdAt: string; columns: EntityIndexColumn[] }
export interface DynamicRecord {
  id: string; data: Record<string, unknown>; createdAt: string; createdBy?: string
  updatedAt: string; updatedBy?: string; version: string
}
export interface RecordPage { items: DynamicRecord[]; nextCursor?: string }
export interface SavedView { id: string; entityId: string; name: string; definition: Record<string, unknown> }
export interface ImportJob { id: string; entityId: string; fileName: string; status: string; columns: string[]; totalRows: number; validRows: number; invalidRows: number }
export interface ImportPreview { totalRows: number; validRows: number; invalidRows: number; errors: Array<{ row: number; fieldId?: string; message: string }> }
