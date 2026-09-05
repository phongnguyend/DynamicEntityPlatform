import type { AnalyticsResult } from '../types'

export function AnalyticsTable({ result }: { result: AnalyticsResult }) {
  return <div className="grid-wrap"><table><thead><tr>{result.columns.map(column => <th key={column.key}>{column.label}</th>)}</tr></thead>
    <tbody>{result.rows.map((row, index) => <tr key={index}>{result.columns.map(column =>
      <td key={column.key}>{formatValue(row[column.key])}</td>)}</tr>)}</tbody></table>
    {result.rows.length === 0 && <p className="empty">No matching data.</p>}
    {result.truncated && <p className="warning">Result truncated at the configured row limit.</p>}</div>
}

export function formatValue(value: unknown) {
  if (value == null) return '—'
  if (typeof value === 'boolean') return value ? 'Yes' : 'No'
  return String(value)
}
