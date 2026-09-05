import { useState, type FormEvent } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { api } from '../api'
import type { Entity, FieldDataType } from '../types'

const dataTypes: FieldDataType[] = ['Text', 'LongText', 'Integer', 'Decimal', 'Boolean', 'Date', 'DateTime', 'Email', 'Url', 'Choice', 'MultiChoice', 'Lookup']

export function EntityDesigner({ tenantId, entity }: { tenantId: string; entity?: Entity }) {
  const client = useQueryClient()
  const [name, setName] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [dataType, setDataType] = useState<FieldDataType>('Text')
  const [required, setRequired] = useState(false)
  const [unique, setUnique] = useState(false)
  const [filterable, setFilterable] = useState(false)
  const [sortable, setSortable] = useState(false)
  const [facetable, setFacetable] = useState(false)
  const [searchable, setSearchable] = useState(false)
  const [choices, setChoices] = useState('')

  const createEntity = useMutation({
    mutationFn: () => api.createEntity(tenantId, { name, displayName }),
    onSuccess: () => { void client.invalidateQueries({ queryKey: ['entities', tenantId] }); setName(''); setDisplayName('') },
  })
  const createField = useMutation({
    mutationFn: () => api.createField(tenantId, entity!.id, {
      name, displayName, dataType, isRequired: required, isUnique: unique,
      isFilterable: filterable, isSortable: sortable, isFacetable: facetable, isSearchable: searchable,
      configuration: dataType === 'Choice' || dataType === 'MultiChoice'
        ? { allowCustomValues: false, values: choices.split('\n').map(value => value.trim()).filter(Boolean) } : undefined,
      sortOrder: 0,
    }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['fields', tenantId, entity?.id] })
      setName(''); setDisplayName(''); setDataType('Text'); setRequired(false); setUnique(false)
      setFilterable(false); setSortable(false); setFacetable(false); setSearchable(false); setChoices('')
    },
  })

  function submit(event: FormEvent) {
    event.preventDefault()
    if (entity) createField.mutate(); else createEntity.mutate()
  }
  const mutation = entity ? createField : createEntity

  return <form className="designer" onSubmit={submit}>
    <h3>{entity ? `Add a field to ${entity.displayName}` : 'Create an entity'}</h3>
    <label className="field"><span>Machine name</span><input required pattern="[A-Za-z][A-Za-z0-9_]*" value={name} onChange={e => setName(e.target.value)} /></label>
    <label className="field"><span>Display name</span><input required value={displayName} onChange={e => setDisplayName(e.target.value)} /></label>
    {entity && <>
      <label className="field"><span>Data type</span><select value={dataType} onChange={e => setDataType(e.target.value as FieldDataType)}>
        {dataTypes.map(type => <option key={type}>{type}</option>)}</select></label>
      <fieldset className="field-options"><legend>Behavior</legend>
        <label className="check"><input type="checkbox" checked={required} onChange={e => setRequired(e.target.checked)} /> Required</label>
        <label className="check"><input type="checkbox" checked={unique} onChange={e => setUnique(e.target.checked)} /> Unique</label>
        <label className="check"><input type="checkbox" checked={filterable} onChange={e => setFilterable(e.target.checked)} /> Filterable</label>
        <label className="check"><input type="checkbox" checked={sortable} onChange={e => setSortable(e.target.checked)} /> Sortable</label>
        <label className="check"><input type="checkbox" checked={facetable} onChange={e => setFacetable(e.target.checked)} /> Facetable</label>
        <label className="check"><input type="checkbox" checked={searchable} onChange={e => setSearchable(e.target.checked)} /> Searchable</label>
      </fieldset>
      {(dataType === 'Choice' || dataType === 'MultiChoice') && <label className="field"><span>Choices (one per line)</span>
        <textarea value={choices} onChange={e => setChoices(e.target.value)} /></label>}
    </>}
    {mutation.error && <p className="error">{mutation.error.message}</p>}
    <button disabled={mutation.isPending}>{mutation.isPending ? 'Creating…' : 'Create'}</button>
  </form>
}
