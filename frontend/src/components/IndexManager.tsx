import { useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api'
import type { Field } from '../types'
import { Modal } from './Modal'

interface Props {
  tenantId: string
  entityId: string
  fields: Field[]
}

const UNINDEXABLE_TYPES = new Set(['LongText', 'MultiChoice'])

export function IndexManager({ tenantId, entityId, fields }: Props) {
  const [creating, setCreating] = useState(false)
  const client = useQueryClient()
  const indexes = useQuery({
    queryKey: ['indexes', tenantId, entityId],
    queryFn: () => api.indexes(tenantId, entityId),
  })
  const deleteIndex = useMutation({
    mutationFn: (indexId: string) => api.deleteIndex(tenantId, entityId, indexId),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['indexes', tenantId, entityId] })
      void client.invalidateQueries({ queryKey: ['fields', tenantId, entityId] })
    },
  })
  const fieldsById = new Map(fields.map(field => [field.id, field]))

  function confirmRemoval(indexId: string, indexName: string) {
    if (confirm(`Remove index ${indexName}? Queries relying on it may become slower.`)) {
      deleteIndex.mutate(indexId)
    }
  }

  return <div className="index-manager">
    <div className="index-manager-header">
      <h3>Indexes</h3>
      <button type="button" onClick={() => setCreating(true)}>Create index</button>
    </div>
    <p className="field-note">Composite SQL indexes can speed up filtering and sorting across multiple columns.</p>
    {indexes.isLoading && <p>Loading…</p>}
    {indexes.error && <p className="error">{indexes.error.message}</p>}
    {indexes.data && indexes.data.length === 0 && <p className="empty">No indexes have been created.</p>}
    {indexes.data && indexes.data.length > 0 &&
      <ul className="index-list">{indexes.data.map(index => <li key={index.id}>
        <div>
          <strong>{index.indexName}</strong>
          <small>{index.columns.slice().sort((a, b) => a.sortOrder - b.sortOrder)
            .map(column => `${fieldsById.get(column.fieldId)?.displayName ?? column.fieldId} ${column.isDescending ? 'DESC' : 'ASC'}`)
            .join(', ')}</small>
        </div>
        <div className="field-actions">
          <span className="index-status">{index.status}</span>
          <button type="button" className="link danger" disabled={deleteIndex.isPending}
            onClick={() => confirmRemoval(index.id, index.indexName)}>
            {deleteIndex.isPending && deleteIndex.variables === index.id ? 'Removing…' : 'Remove'}
          </button>
        </div>
      </li>)}</ul>}
    {creating && <Modal title="Create index" onClose={() => setCreating(false)}>
      <IndexCreator tenantId={tenantId} entityId={entityId} fields={fields} onClose={() => setCreating(false)} />
    </Modal>}
  </div>
}

function IndexCreator({ tenantId, entityId, fields, onClose }: {
  tenantId: string
  entityId: string
  fields: Field[]
  onClose: () => void
}) {
  const client = useQueryClient()
  const indexableFields = fields.filter(field => !UNINDEXABLE_TYPES.has(field.dataType))
  const [selection, setSelection] = useState<Array<{ fieldId: string; descending: boolean }>>([])
  const create = useMutation({
    mutationFn: () => api.createIndex(tenantId, entityId, selection),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['indexes', tenantId, entityId] })
      void client.invalidateQueries({ queryKey: ['fields', tenantId, entityId] })
      onClose()
    },
  })

  function toggleField(fieldId: string, checked: boolean) {
    setSelection(current => checked
      ? [...current, { fieldId, descending: false }]
      : current.filter(entry => entry.fieldId !== fieldId))
  }

  function setDescending(fieldId: string, descending: boolean) {
    setSelection(current => current.map(entry => entry.fieldId === fieldId ? { ...entry, descending } : entry))
  }

  function move(fieldId: string, delta: number) {
    setSelection(current => {
      const index = current.findIndex(entry => entry.fieldId === fieldId)
      const target = index + delta
      if (index < 0 || target < 0 || target >= current.length) return current
      const next = current.slice()
      const [entry] = next.splice(index, 1)
      next.splice(target, 0, entry)
      return next
    })
  }

  function submit(event: FormEvent) {
    event.preventDefault()
    if (selection.length === 0) return
    create.mutate()
  }

  return <form className="index-creator" onSubmit={submit}>
    <p className="field-note">Select one or more columns and pick a sort direction for each. Order determines column order in the index.</p>
    <ul className="field-list">{indexableFields.map(field => {
      const selected = selection.find(entry => entry.fieldId === field.id)
      return <li key={field.id}>
        <label className="check">
          <input type="checkbox" checked={!!selected}
            onChange={event => toggleField(field.id, event.target.checked)} />
          {field.displayName}<small> {field.name} · {field.dataType}</small>
        </label>
        {selected && <div className="field-actions">
          <select value={selected.descending ? 'DESC' : 'ASC'}
            onChange={event => setDescending(field.id, event.target.value === 'DESC')}>
            <option value="ASC">Ascending</option>
            <option value="DESC">Descending</option>
          </select>
          <button type="button" className="link" onClick={() => move(field.id, -1)}>Up</button>
          <button type="button" className="link" onClick={() => move(field.id, 1)}>Down</button>
        </div>}
      </li>
    })}</ul>
    {selection.length > 0 && <ol className="index-order-preview">
      {selection.map(entry => <li key={entry.fieldId}>
        {fields.find(field => field.id === entry.fieldId)?.displayName} ({entry.descending ? 'DESC' : 'ASC'})
      </li>)}
    </ol>}
    {create.error && <p className="error">{create.error.message}</p>}
    <div className="actions">
      <button disabled={selection.length === 0 || create.isPending}>
        {create.isPending ? 'Creating…' : 'Create index'}
      </button>
      <button type="button" className="secondary" onClick={onClose}>Cancel</button>
    </div>
  </form>
}
