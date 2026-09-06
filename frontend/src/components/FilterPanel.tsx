import { useState, type FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Filter, RotateCcw } from 'lucide-react'
import { api } from '../api'
import type { Field } from '../types'

export function FilterPanel({ tenantId, entityId, field, condition, onApply, onClose }: {
  tenantId: string; entityId: string; field: Field
  condition?: { operator: string; value: unknown }
  onApply: (query: Record<string, unknown> | null) => void
  onClose: () => void
}) {
  const [operator, setOperator] = useState(condition?.operator ?? 'Equal')
  const [value, setValue] = useState(condition?.value == null ? '' : String(condition.value))
  const fieldId = field.id
  const facets = useQuery({ queryKey: ['facets', tenantId, entityId, fieldId],
    queryFn: () => api.facets(tenantId, entityId, fieldId), enabled: field.isFacetable })

  function apply(event: FormEvent) {
    event.preventDefault()
    if (!field) return
    let typed: unknown = value
    if (field.dataType === 'Integer' || field.dataType === 'Decimal') typed = Number(value)
    if (field.dataType === 'Boolean') typed = value === 'true'
    onApply({ filter: { logic: 'And', conditions: [{ fieldId, operator, value: typed }] }, pageSize: 100 })
    onClose()
  }
  return <form className="filter-bar" onSubmit={apply}><span className="filter-field">{field.displayName}</span>
    <select value={operator} onChange={event => setOperator(event.target.value)}>
      <option>Equal</option><option>NotEqual</option><option>Contains</option><option>StartsWith</option><option>GreaterThan</option><option>LessThan</option>
    </select>
    {facets.data?.length ? <select value={value} onChange={event => setValue(event.target.value)}><option value="">Value…</option>
      {facets.data.map(item => <option key={item.value} value={item.value}>{item.value} ({item.recordCount})</option>)}</select>
      : <input value={value} placeholder="Value" onChange={event => setValue(event.target.value)} />}
    <button><Filter />Apply</button><button type="button" className="secondary" onClick={() => {
      onApply(null)
      onClose()
    }}><RotateCcw />Clear</button></form>
}
