import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api'
import type { DynamicRecord, Entity } from '../types'
import { DynamicForm } from './DynamicForm'
import { DynamicGrid } from './DynamicGrid'
import { EntityDesigner } from './EntityDesigner'
import { FilterPanel } from './FilterPanel'
import { SavedViewSelector } from './SavedViewSelector'
import { ImportPanel } from './ImportPanel'
import { FieldManager } from './FieldManager'

export function EntityWorkspace({ tenantId, entity }: { tenantId: string; entity: Entity }) {
  const client = useQueryClient()
  const [editing, setEditing] = useState<DynamicRecord>()
  const [queryDefinition, setQueryDefinition] = useState<Record<string, unknown> | null>(null)
  const fields = useQuery({ queryKey: ['fields', tenantId, entity.id], queryFn: () => api.fields(tenantId, entity.id) })
  const records = useQuery({ queryKey: ['records', tenantId, entity.id, queryDefinition],
    queryFn: () => queryDefinition ? api.query(tenantId, entity.id, queryDefinition) : api.records(tenantId, entity.id) })
  const save = useMutation({
    mutationFn: (data: Record<string, unknown>) => editing
      ? api.patchRecord(tenantId, entity.id, editing, data)
      : api.createRecord(tenantId, entity.id, data),
    onSuccess: () => { setEditing(undefined); void client.invalidateQueries({ queryKey: ['records', tenantId, entity.id] }) },
  })
  const remove = useMutation({
    mutationFn: (record: DynamicRecord) => api.deleteRecord(tenantId, entity.id, record),
    onSuccess: () => void client.invalidateQueries({ queryKey: ['records', tenantId, entity.id] }),
  })

  if (fields.isLoading || records.isLoading) return <p>Loading {entity.displayName}…</p>
  if (fields.error || records.error) return <p className="error">{fields.error?.message ?? records.error?.message}</p>
  const definitions = fields.data ?? []

  return <section className="workspace">
    <header><div><p className="eyebrow">Entity</p><h2>{entity.displayName}</h2><p>{entity.description}</p></div>
      <span className="pill">Schema v{entity.schemaVersion}</span></header>
    <div className="workspace-grid">
      <div className="panel"><h3>{editing ? 'Edit record' : 'New record'}</h3>
        {definitions.length === 0 ? <p className="empty">Add a field before creating records.</p> :
          <DynamicForm key={editing?.id ?? 'new'} fields={definitions} record={editing} busy={save.isPending}
            onSubmit={async data => { await save.mutateAsync(data) }} onCancel={editing ? () => setEditing(undefined) : undefined} />}
        {save.error && <p className="error">{save.error.message}</p>}</div>
      <div className="panel wide"><div className="panel-title"><h3>Records</h3><div><span>{records.data?.items.length ?? 0}</span>{' '}
        <button className="link" onClick={async () => { const blob = await api.exportRecords(tenantId, entity.id); const url = URL.createObjectURL(blob)
          const anchor = document.createElement('a'); anchor.href = url; anchor.download = `${entity.name}.csv`; anchor.click(); URL.revokeObjectURL(url) }}>Export CSV</button></div></div>
        <SavedViewSelector tenantId={tenantId} entityId={entity.id} definition={queryDefinition} onSelect={setQueryDefinition} />
        <FilterPanel tenantId={tenantId} entityId={entity.id} fields={definitions} onApply={setQueryDefinition} />
        <DynamicGrid fields={definitions} records={records.data?.items ?? []} onEdit={setEditing}
          onDelete={record => { if (confirm('Delete this record?')) remove.mutate(record) }} /></div>
      <div className="panel"><EntityDesigner tenantId={tenantId} entity={entity} /></div>
      <div className="panel"><FieldManager tenantId={tenantId} entityId={entity.id} fields={definitions} /></div>
      <div className="panel"><ImportPanel tenantId={tenantId} entityId={entity.id} fields={definitions} /></div>
    </div>
  </section>
}
