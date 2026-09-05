import type { DynamicRecord, Field } from '../types'

interface Props { fields: Field[]; records: DynamicRecord[]; onEdit: (record: DynamicRecord) => void; onDelete: (record: DynamicRecord) => void }

export function DynamicGrid({ fields, records, onEdit, onDelete }: Props) {
  const shown = fields.slice(0, 8)
  return <div className="grid-wrap"><table><thead><tr>{shown.map(field => <th key={field.id}>{field.displayName}</th>)}<th>Updated</th><th /></tr></thead>
    <tbody>{records.map(record => <tr key={record.id}>{shown.map(field =>
      <td key={field.id}>{display(record.data[field.storageKey])}</td>)}
      <td>{new Date(record.updatedAt).toLocaleString()}</td><td className="row-actions">
        <button className="link" onClick={() => onEdit(record)}>Edit</button>
        <button className="link danger" onClick={() => onDelete(record)}>Delete</button></td></tr>)}</tbody></table>
    {records.length === 0 && <p className="empty">No records yet.</p>}</div>
}

function display(value: unknown) {
  if (value == null) return '—'
  if (Array.isArray(value)) return value.join(', ')
  if (typeof value === 'boolean') return value ? 'Yes' : 'No'
  return String(value)
}
