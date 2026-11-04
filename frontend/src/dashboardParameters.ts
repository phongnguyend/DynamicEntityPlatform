import type { DashboardItem, DashboardParameter } from './types'
import { metricParameterNames } from './components/RuntimeParametersForm'

export function resolveCardParameters(item: DashboardItem, dashboardParameters: DashboardParameter[], filter: unknown) {
  const parameters = { ...item.parameters }
  const errors: string[] = []
  for (const name of metricParameterNames(filter)) {
    const id = item.parameterBindings?.[name]
    if (id === undefined) {
      continue
    }
    delete parameters[name]
    const parameter = dashboardParameters.find((value) => value.id === id)
    if (!parameter) {
      errors.push(
        `The dashboard parameter bound to "${name}" no longer exists. Configure this card to select another parameter.`,
      )
    } else if (parameter.value === undefined) {
      errors.push(`Set a value for dashboard parameter "${parameter.label || parameter.name}" to run this card.`)
    } else {
      parameters[name] = parameter.value
    }
  }
  return { parameters, parameterError: errors.join(' ') || undefined }
}
