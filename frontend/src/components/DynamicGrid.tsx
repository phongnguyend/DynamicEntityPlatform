import { useEffect, useLayoutEffect, useRef, useState, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import type { DynamicRecord, Field } from '../types'

interface Props {
  fields: Field[]
  records: DynamicRecord[]
  selectedFilterFieldId?: string
  appliedFilterFieldId?: string
  onSelectFilter: (field: Field) => void
  renderFilter: (field: Field) => ReactNode
  onEdit: (record: DynamicRecord) => void
  onDelete: (record: DynamicRecord) => void
}

export function DynamicGrid({ fields, records, selectedFilterFieldId, appliedFilterFieldId,
  onSelectFilter, renderFilter, onEdit, onDelete }: Props) {
  const shown = fields.slice(0, 8)
  return <div className="grid-wrap"><table><thead><tr>{shown.map(field => <th key={field.id}>
    {field.isFilterable ? <FilterHeader field={field}
      selected={selectedFilterFieldId === field.id}
      filtered={appliedFilterFieldId === field.id}
      onSelect={() => onSelectFilter(field)}>{renderFilter(field)}</FilterHeader> : field.displayName}
  </th>)}<th>Created</th><th>Updated</th><th /></tr></thead>
    <tbody>{records.map(record => <tr key={record.id}>{shown.map(field =>
      <td key={field.id}>{display(record.data[field.storageKey])}</td>)}
      <td>{new Date(record.createdAt).toLocaleString()}</td>
      <td>{new Date(record.updatedAt).toLocaleString()}</td><td className="row-actions">
        <button className="link" onClick={() => onEdit(record)}>Edit</button>
        <button className="link danger" onClick={() => onDelete(record)}>Delete</button></td></tr>)}</tbody></table>
    {records.length === 0 && <p className="empty">No records yet.</p>}</div>
}

function FilterHeader({ field, selected, filtered, onSelect, children }: {
  field: Field
  selected: boolean
  filtered: boolean
  onSelect: () => void
  children: ReactNode
}) {
  const buttonRef = useRef<HTMLButtonElement>(null)
  const popoverRef = useRef<HTMLDivElement>(null)
  const [position, setPosition] = useState<{ top: number; left: number }>()

  useLayoutEffect(() => {
    if (!selected) {
      setPosition(undefined)
      return
    }
    function placePopover() {
      const bounds = buttonRef.current?.getBoundingClientRect()
      if (!bounds) return
      const width = Math.min(280, window.innerWidth - 24)
      setPosition({
        top: bounds.bottom + 8,
        left: Math.max(12, Math.min(bounds.right - width, window.innerWidth - width - 12)),
      })
    }
    placePopover()
    window.addEventListener('resize', placePopover)
    window.addEventListener('scroll', placePopover, true)
    return () => {
      window.removeEventListener('resize', placePopover)
      window.removeEventListener('scroll', placePopover, true)
    }
  }, [selected])

  useEffect(() => {
    if (!selected) return
    function closeOnOutsideClick(event: PointerEvent) {
      const target = event.target as Node
      if (buttonRef.current?.contains(target) || popoverRef.current?.contains(target)) return
      onSelect()
    }
    document.addEventListener('pointerdown', closeOnOutsideClick)
    return () => document.removeEventListener('pointerdown', closeOnOutsideClick)
  }, [selected, onSelect])

  return <div className="column-filter-anchor">
    <button ref={buttonRef} type="button"
      className={`column-filter${selected || filtered ? ' active' : ''}`}
      aria-pressed={filtered} aria-expanded={selected} onClick={onSelect}>
      <span>{field.displayName}</span>
      <svg className="filter-icon" viewBox="0 0 20 20" aria-hidden="true">
        <path d="M3 4h14l-5.5 6.3V15l-3 1.5v-6.2L3 4Z" />
      </svg>
    </button>
    {selected && position && createPortal(
      <div ref={popoverRef} className="column-filter-popover" style={position}>{children}</div>, document.body)}
  </div>
}

function display(value: unknown) {
  if (value == null) return '—'
  if (Array.isArray(value)) return value.join(', ')
  if (typeof value === 'boolean') return value ? 'Yes' : 'No'
  return String(value)
}
