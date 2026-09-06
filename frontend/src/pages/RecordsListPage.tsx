import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useNavigate, useParams } from 'react-router-dom'
import { Download, Plus, Upload } from 'lucide-react'
import { api } from '../api'
import type { DynamicRecord } from '../types'
import { DynamicGrid } from '../components/DynamicGrid'
import { FilterPanel } from '../components/FilterPanel'
import { ImportPanel } from '../components/ImportPanel'
import { Modal } from '../components/Modal'
import { EntityTabs } from '../components/EntityTabs'
import { useEntityContext } from './useEntityContext'

export function RecordsListPage({ tenantId }: { tenantId: string }) {
  const { entityId = '' } = useParams()
  const navigate = useNavigate()
  const client = useQueryClient()
  const { entity, fields, isLoading, error } = useEntityContext(tenantId, entityId)
  const [queryDefinition, setQueryDefinition] = useState<Record<string, unknown> | null>(null)
  const [filterFieldId, setFilterFieldId] = useState('')
  const [importOpen, setImportOpen] = useState(false)
  const records = useQuery({
    queryKey: ['records', tenantId, entityId, queryDefinition],
    queryFn: () => queryDefinition ? api.query(tenantId, entityId, queryDefinition) : api.records(tenantId, entityId),
    enabled: Boolean(entityId),
  })
  const remove = useMutation({
    mutationFn: (record: DynamicRecord) => api.deleteRecord(tenantId, entityId, record),
    onSuccess: () => void client.invalidateQueries({ queryKey: ['records', tenantId, entityId] }),
  })
  const appliedFilterFieldId = getAppliedFilterFieldId(queryDefinition)

  if (isLoading) return <p>Loading…</p>
  if (error) return <p className="error">{error.message}</p>
  if (!entity) return <p className="error">Entity not found.</p>

  return <section className="page">
    <header><div><p className="eyebrow">Entity</p><h2>{entity.displayName}</h2><p>{entity.description}</p></div>
      <span className="pill">Schema v{entity.schemaVersion}</span></header>
    <EntityTabs entityId={entityId} />
    <div className="panel">
    <div className="panel-title"><h3>Records</h3>
      <div className="page-actions">
        <button className="link" onClick={async () => {
          const blob = await api.exportRecords(tenantId, entityId); const url = URL.createObjectURL(blob)
          const anchor = document.createElement('a'); anchor.href = url; anchor.download = `${entity.name}.csv`; anchor.click(); URL.revokeObjectURL(url)
        }}><Download />Export CSV</button>
        <button type="button" className="secondary" onClick={() => setImportOpen(true)}><Upload />Import</button>
        <button type="button" disabled={fields.length === 0} onClick={() => navigate(`/entities/${entityId}/records/new`)}><Plus />New record</button>
      </div>
    </div>
    {records.isLoading ? <p>Loading records…</p> : records.error ? <p className="error">{records.error.message}</p> : <>
      <DynamicGrid fields={fields} records={records.data?.items ?? []}
        selectedFilterFieldId={filterFieldId} appliedFilterFieldId={appliedFilterFieldId}
        onSelectFilter={field => setFilterFieldId(current => current === field.id ? '' : field.id)}
        renderFilter={field => <FilterPanel key={field.id} tenantId={tenantId} entityId={entityId}
          field={field} condition={getAppliedFilterCondition(queryDefinition, field.id)}
          onApply={setQueryDefinition} onClose={() => setFilterFieldId('')} />}
        onEdit={record => navigate(`/entities/${entityId}/records/${record.id}/edit`, { state: { record } })}
        onDelete={record => { if (confirm('Delete this record?')) remove.mutate(record) }} />
      <p className="record-count">{records.data?.items.length ?? 0} record{records.data?.items.length === 1 ? '' : 's'}</p>
    </>}
    </div>
    {importOpen && <Modal title="Import CSV / Excel" onClose={() => setImportOpen(false)}>
      <ImportPanel tenantId={tenantId} entityId={entityId} fields={fields} />
    </Modal>}
  </section>
}

export function getAppliedFilterFieldId(query: Record<string, unknown> | null) {
  return getAppliedFilterCondition(query)?.fieldId
}

export function getAppliedFilterCondition(query: Record<string, unknown> | null, fieldId?: string) {
  if (!query || typeof query.filter !== 'object' || query.filter === null) return undefined
  const conditions = (query.filter as { conditions?: unknown }).conditions
  if (!Array.isArray(conditions)) return undefined
  const condition = conditions.find(item => typeof item === 'object' && item !== null &&
    typeof (item as { fieldId?: unknown }).fieldId === 'string' &&
    (!fieldId || (item as { fieldId: string }).fieldId === fieldId)) as Record<string, unknown> | undefined
  if (!condition || typeof condition.fieldId !== 'string' || typeof condition.operator !== 'string') return undefined
  return { fieldId: condition.fieldId, operator: condition.operator, value: condition.value }
}
