import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useNavigate, useParams } from 'react-router-dom'
import { api } from '../api'
import type { DynamicRecord } from '../types'
import { DynamicGrid } from '../components/DynamicGrid'
import { FilterPanel } from '../components/FilterPanel'
import { SavedViewSelector } from '../components/SavedViewSelector'
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

  if (isLoading) return <p>Loading…</p>
  if (error) return <p className="error">{error.message}</p>
  if (!entity) return <p className="error">Entity not found.</p>

  return <section className="page">
    <header><div><p className="eyebrow">Entity</p><h2>{entity.displayName}</h2><p>{entity.description}</p></div>
      <span className="pill">Schema v{entity.schemaVersion}</span></header>
    <EntityTabs entityId={entityId} />
    <div className="panel-title"><h3>Records</h3>
      <div className="page-actions">
        <button className="link" onClick={async () => {
          const blob = await api.exportRecords(tenantId, entityId); const url = URL.createObjectURL(blob)
          const anchor = document.createElement('a'); anchor.href = url; anchor.download = `${entity.name}.csv`; anchor.click(); URL.revokeObjectURL(url)
        }}>Export CSV</button>
        <button type="button" className="secondary" onClick={() => setImportOpen(true)}>Import</button>
        <button type="button" disabled={fields.length === 0} onClick={() => navigate(`/entities/${entityId}/records/new`)}>New record</button>
      </div>
    </div>
    <SavedViewSelector tenantId={tenantId} entityId={entityId} definition={queryDefinition} onSelect={setQueryDefinition} />
    <FilterPanel tenantId={tenantId} entityId={entityId} fields={fields} onApply={setQueryDefinition} />
    {records.isLoading ? <p>Loading records…</p> : records.error ? <p className="error">{records.error.message}</p> : <>
      <DynamicGrid fields={fields} records={records.data?.items ?? []}
        onEdit={record => navigate(`/entities/${entityId}/records/${record.id}/edit`, { state: { record } })}
        onDelete={record => { if (confirm('Delete this record?')) remove.mutate(record) }} />
      <p className="record-count">{records.data?.items.length ?? 0} record{records.data?.items.length === 1 ? '' : 's'}</p>
    </>}
    {importOpen && <Modal title="Import CSV / Excel" onClose={() => setImportOpen(false)}>
      <ImportPanel tenantId={tenantId} entityId={entityId} fields={fields} />
    </Modal>}
  </section>
}
