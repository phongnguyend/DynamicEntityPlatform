import { useState, type FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import { api } from '../api'
import type { Field } from '../types'

export function FilterPanel({ tenantId, entityId, fields, onApply }: {
  tenantId: string; entityId: string; fields: Field[]; onApply: (query: Record<string, unknown> | null) => void
}) {
  const filterable = fields.filter(field => field.isFilterable)
  const [fieldId, setFieldId] = useState(filterable[0]?.id ?? '')
  const [operator, setOperator] = useState('Equal')
  const [value, setValue] = useState('')
  const field = fields.find(item => item.id === fieldId)
  const facets = useQuery({ queryKey: ['facets', tenantId, entityId, fieldId],
    queryFn: () => api.facets(tenantId, entityId, fieldId), enabled: Boolean(field?.isFacetable) })

  function apply(event: FormEvent) {
    event.preventDefault()
    if (!field) return
    let typed: unknown = value
    if (field.dataType === 'Integer' || field.dataType === 'Decimal') typed = Number(value)
    if (field.dataType === 'Boolean') typed = value === 'true'
    onApply({ filter: { logic: 'And', conditions: [{ fieldId, operator, value: typed }] }, pageSize: 100 })
  }
  if (filterable.length === 0) return null
  return <form className="filter-bar" onSubmit={apply}><select value={fieldId} onChange={event => setFieldId(event.target.value)}>
    {filterable.map(item => <option key={item.id} value={item.id}>{item.displayName}</option>)}</select>
    <select value={operator} onChange={event => setOperator(event.target.value)}>
      <option>Equal</option><option>NotEqual</option><option>Contains</option><option>StartsWith</option><option>GreaterThan</option><option>LessThan</option>
    </select>
    {facets.data?.length ? <select value={value} onChange={event => setValue(event.target.value)}><option value="">Value…</option>
      {facets.data.map(item => <option key={item.value} value={item.value}>{item.value} ({item.recordCount})</option>)}</select>
      : <input value={value} placeholder="Value" onChange={event => setValue(event.target.value)} />}
    <button>Apply</button><button type="button" className="secondary" onClick={() => onApply(null)}>Clear</button></form>
}
