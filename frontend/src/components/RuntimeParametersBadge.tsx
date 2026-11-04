import { SlidersHorizontal } from 'lucide-react'
import { metricParameterNames } from './MetricParametersForm'
import './RuntimeParametersBadge.css'

export function RuntimeParametersBadge({ filter }: { filter: unknown }) {
  const names = metricParameterNames(filter, true)
  if (!names.length) {
    return null
  }
  const description = `Required runtime parameters: ${names.join(', ')}`
  return (
    <span className="runtime-parameters-badge" title={description} aria-label={description}>
      <SlidersHorizontal aria-hidden="true" />
      Requires parameters
    </span>
  )
}
