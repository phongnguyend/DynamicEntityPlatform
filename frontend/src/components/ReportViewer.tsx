import { memo } from 'react'
import type { AnalyticsResult, VisualizationType } from '../types'
import { AnalyticsTable } from './AnalyticsTable'
import { SimpleChart } from './charts/SimpleChart'

export const ReportViewer = memo(function ReportViewer({ result, visualization }: { result: AnalyticsResult; visualization: VisualizationType }) {
  return <div className="report-viewer">
    {visualization === 'Table' ? <AnalyticsTable result={result} /> : <SimpleChart result={result} type={visualization} />}
    <small>Generated {new Date(result.generatedAt).toLocaleString()}</small>
  </div>
})
