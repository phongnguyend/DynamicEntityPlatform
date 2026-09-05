import type { AnalyticsResult, VisualizationType } from '../../types'
import { formatValue } from '../AnalyticsTable'

export function SimpleChart({ result, type }: { result: AnalyticsResult; type: VisualizationType }) {
  const dimension = result.columns.find(column => column.role === 'Dimension')
  const measure = result.columns.find(column => column.role === 'Measure')
  if (!measure) return <p className="empty">Add a measure to visualize.</p>
  if (type === 'Number') return <div className="metric-number">{formatValue(result.rows[0]?.[measure.key])}</div>
  const points = result.rows.map(row => ({ label: dimension ? formatValue(row[dimension.key]) : measure.label, value: Number(row[measure.key] ?? 0) }))
  const max = Math.max(1, ...points.map(point => Math.abs(point.value)))
  if (type === 'Line') {
    const coords = points.map((point, index) => `${points.length === 1 ? 50 : index * 100 / (points.length - 1)},${100 - point.value * 90 / max}`).join(' ')
    return <div className="simple-chart"><svg viewBox="0 0 100 105" preserveAspectRatio="none" aria-label={`${measure.label} line chart`}><polyline points={coords} fill="none" stroke="currentColor" strokeWidth="2" vectorEffect="non-scaling-stroke" /></svg></div>
  }
  if (type === 'Donut') {
    const total = points.reduce((sum, point) => sum + Math.max(0, point.value), 0) || 1
    let offset = 0
    return <div className="donut-chart"><svg viewBox="0 0 42 42">{points.map((point, index) => { const length = Math.max(0, point.value) / total * 100; const segment = <circle key={index} cx="21" cy="21" r="15.9" fill="none" stroke={`hsl(${index * 67} 55% 45%)`} strokeWidth="6" strokeDasharray={`${length} ${100 - length}`} strokeDashoffset={-offset} />; offset += length; return segment })}</svg><ul>{points.map(point => <li key={point.label}>{point.label}: {point.value}</li>)}</ul></div>
  }
  return <div className="bar-chart">{points.map(point => <div className="bar-row" key={point.label}><span>{point.label}</span><i style={{ width: `${Math.abs(point.value) / max * 100}%` }} /><strong>{point.value}</strong></div>)}</div>
}
