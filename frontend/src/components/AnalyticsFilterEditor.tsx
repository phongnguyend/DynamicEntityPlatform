import { memo, startTransition, useEffect, useMemo, useRef, useState } from 'react'
import type { Field } from '../types'
import { FolderPlus, Funnel, Trash2 } from 'lucide-react'

type FilterLogic = 'And' | 'Or'
type FilterCondition = { fieldId: string; operator: string; value?: unknown }
type FilterNode = FilterCondition | FilterGroup
type FilterGroup = { logic: FilterLogic; conditions: FilterNode[] }

export function AnalyticsFilterEditor({ fields, value, onChange }: { fields: Field[]; value?: unknown; onChange: (value?: FilterGroup) => void }) {
  const filterableFields = useMemo(() => fields.filter(field => field.isFilterable), [fields])
  const [group, setGroup] = useState<FilterGroup | undefined>(() => isGroup(value) ? value : undefined)
  const previousValue = useRef(value)

  useEffect(() => {
    if (value === previousValue.current) return
    previousValue.current = value
    setGroup(isGroup(value) ? value : undefined)
  }, [value])

  function changeGroup(nextGroup?: FilterGroup) {
    setGroup(nextGroup)
    startTransition(() => onChange(nextGroup))
  }

  function startFilter() {
    const field = filterableFields[0]
    if (field) changeGroup({ logic: 'And', conditions: [defaultCondition(field)] })
  }

  function startGroup() {
    if (filterableFields.length) changeGroup({ logic: 'And', conditions: [{ logic: 'And', conditions: [] }] })
  }

  return <div className="builder-section analytics-filters">
    <div className="panel-header"><h3>Filters</h3><div className="actions">
      {group && <button type="button" className="link danger" onClick={() => changeGroup(undefined)}><Trash2 />Clear all</button>}
      {!group && <><button type="button" className="secondary" disabled={!filterableFields.length} onClick={startFilter}><Funnel />Add filter</button>
        <button type="button" className="secondary" disabled={!filterableFields.length} onClick={startGroup}><FolderPlus />Add group</button></>}
    </div></div>
    {!group && <p className="empty">No filters. All records are included.</p>}
    {group && <FlatFilterTree fields={filterableFields} root={group} onChange={changeGroup} />}
  </div>
}

type FlatFilterItem =
  | { kind: 'group'; group: FilterGroup; path: number[]; parentPath?: number[]; nodeIndex?: number; depth: number; parentLogic?: FilterLogic }
  | { kind: 'condition'; condition: FilterCondition; groupPath: number[]; nodeIndex: number; depth: number; logic: FilterLogic }
  | { kind: 'end'; path: number[]; depth: number }

function FlatFilterTree({ fields, root, onChange }: {
  fields: Field[]
  root: FilterGroup
  onChange: (group: FilterGroup) => void
}) {
  const items = flattenFilterTree(root)
  return <div className="flat-filter-tree">{items.map(item => {
    if (item.kind === 'end') return <div className="filter-group-end" style={{ marginLeft: `${item.depth * .75}rem` }} key={`end-${item.path.join('-') || 'root'}`}>
      <span>{item.depth ? `End group level ${item.depth}` : 'End root group'}</span>
    </div>
    if (item.kind === 'condition') return <div className="flat-filter-item" style={{ marginLeft: `${item.depth * .75}rem` }} key={`condition-${item.groupPath.join('-')}-${item.nodeIndex}`}>
      {item.nodeIndex > 0 && <LogicSelect logic={item.logic} label={`Logic before filter ${item.nodeIndex + 1}`}
        onChange={logic => onChange(updateGroup(root, item.groupPath, group => ({ ...group, logic })))} />}
      <FilterConditionEditor fields={fields} condition={item.condition} label={`Filter ${item.nodeIndex + 1}`}
        onChange={condition => onChange(replaceNode(root, item.groupPath, item.nodeIndex, condition))}
        onRemove={() => onChange(replaceNode(root, item.groupPath, item.nodeIndex))} />
    </div>

    const full = item.group.conditions.length >= 50
    return <section className={`flat-filter-group${item.depth ? ' nested' : ''}`} style={{ marginLeft: `${item.depth * .75}rem` }} key={`group-${item.path.join('-') || 'root'}`}>
      {item.nodeIndex !== undefined && item.nodeIndex > 0 && item.parentLogic && <LogicSelect logic={item.parentLogic} label={`Logic before group ${item.nodeIndex + 1}`}
        onChange={logic => onChange(updateGroup(root, item.parentPath ?? [], group => ({ ...group, logic })))} />}
      <div className="filter-group-header"><span className="filter-group-label">{item.depth ? `Group level ${item.depth}` : 'Root group'}</span><div className="actions">
        <button type="button" className="secondary" disabled={!fields.length || full} onClick={() => {
          const field = fields[0]
          if (field) onChange(updateGroup(root, item.path, group => ({ ...group, conditions: [...group.conditions, defaultCondition(field)] })))
        }}><Funnel />Add filter</button>
        <button type="button" className="secondary" disabled={!fields.length || full || item.depth >= 10}
          onClick={() => onChange(updateGroup(root, item.path, group => ({
            ...group,
            conditions: [...group.conditions, { logic: 'And', conditions: [] }],
          })))}><FolderPlus />Add group</button>
        {item.parentPath && item.nodeIndex !== undefined && <button type="button" className="link danger"
          onClick={() => onChange(replaceNode(root, item.parentPath!, item.nodeIndex!))}><Trash2 />Remove group</button>}
      </div></div>
      {item.group.conditions.length === 0 && <p className="empty">This group is empty.</p>}
    </section>
  })}</div>
}

function LogicSelect({ logic, label, onChange }: { logic: FilterLogic; label: string; onChange: (logic: FilterLogic) => void }) {
  return <select className="condition-logic" aria-label={label} value={logic} onChange={event => onChange(event.target.value as FilterLogic)}>
    <option value="And">AND</option><option value="Or">OR</option>
  </select>
}

function flattenFilterTree(root: FilterGroup) {
  const result: FlatFilterItem[] = []
  const stack: FlatFilterItem[] = [{ kind: 'group', group: root, path: [], depth: 0 }]
  while (stack.length) {
    const item = stack.pop()!
    result.push(item)
    if (item.kind !== 'group') continue
    stack.push({ kind: 'end', path: item.path, depth: item.depth })
    for (let index = item.group.conditions.length - 1; index >= 0; index--) {
      const node = item.group.conditions[index]
      if (isGroup(node)) stack.push({ kind: 'group', group: node, path: [...item.path, index], parentPath: item.path, nodeIndex: index, depth: item.depth + 1, parentLogic: item.group.logic })
      else stack.push({ kind: 'condition', condition: node, groupPath: item.path, nodeIndex: index, depth: item.depth + 1, logic: item.group.logic })
    }
  }
  return result
}

function updateGroup(root: FilterGroup, path: number[], update: (group: FilterGroup) => FilterGroup) {
  const parents: Array<{ group: FilterGroup; nodeIndex: number }> = []
  let current = root
  for (const nodeIndex of path) {
    const child = current.conditions[nodeIndex]
    if (!isGroup(child)) return root
    parents.push({ group: current, nodeIndex })
    current = child
  }
  let next = update(current)
  for (let index = parents.length - 1; index >= 0; index--) {
    const parent = parents[index]
    next = { ...parent.group, conditions: parent.group.conditions.map((node, nodeIndex) => nodeIndex === parent.nodeIndex ? next : node) }
  }
  return next
}

function replaceNode(root: FilterGroup, groupPath: number[], nodeIndex: number, node?: FilterNode) {
  return updateGroup(root, groupPath, group => ({ ...group, conditions: node === undefined
    ? group.conditions.filter((_, index) => index !== nodeIndex)
    : group.conditions.map((current, index) => index === nodeIndex ? node : current) }))
}

function FilterConditionEditor({ fields, condition, label, onChange, onRemove }: {
  fields: Field[]
  condition: FilterCondition
  label: string
  onChange: (condition: FilterCondition) => void
  onRemove: () => void
}) {
  const field = fields.find(item => item.id === condition.fieldId)
  const operators = operatorsFor(field)
  const hasValue = condition.operator !== 'IsNull' && condition.operator !== 'IsNotNull'

  function changeField(fieldId: string) {
    const nextField = fields.find(item => item.id === fieldId)
    if (nextField) onChange(defaultCondition(nextField))
  }

  function changeOperator(operator: string) {
    onChange({ ...condition, operator, value: operator === 'IsNull' || operator === 'IsNotNull' ? undefined : condition.value ?? defaultValue(field) })
  }

  return <div className="analytics-filter-row">
    <select aria-label={`${label} field`} value={condition.fieldId} onChange={event => changeField(event.target.value)}><FieldOptions fields={fields} /></select>
    <select aria-label={`${label} operator`} value={condition.operator} onChange={event => changeOperator(event.target.value)}>{operators.map(operator => <option key={operator}>{operator}</option>)}</select>
    {hasValue ? <FilterValue field={field} value={condition.value} onChange={value => onChange({ ...condition, value })} /> : <span className="filter-no-value">No value</span>}
    <button type="button" className="link danger icon-only" aria-label={`Remove ${label}`} title="Remove filter" onClick={onRemove}><Trash2 /></button>
  </div>
}

function FilterValue({ field, value, onChange }: { field?: Field; value: unknown; onChange: (value: unknown) => void }) {
  if (field?.dataType === 'Boolean') return <select aria-label={`${field.displayName} value`} value={String(value ?? false)} onChange={event => onChange(event.target.value === 'true')}><option value="true">True</option><option value="false">False</option></select>
  const inputType = field?.dataType === 'Date' ? 'date' : field?.dataType === 'DateTime' ? 'datetime-local' : field?.dataType === 'Integer' || field?.dataType === 'Decimal' ? 'number' : 'text'
  return <input aria-label={`${field?.displayName ?? 'Filter'} value`} type={inputType} step={field?.dataType === 'Integer' ? '1' : field?.dataType === 'Decimal' ? 'any' : undefined} value={String(value ?? '')} onChange={event => onChange(field?.dataType === 'Integer' || field?.dataType === 'Decimal' ? Number(event.target.value) : event.target.value)} />
}

function defaultCondition(field: Field): FilterCondition { return { fieldId: field.id, operator: 'Equal', value: defaultValue(field) } }
function defaultValue(field?: Field) { return field?.dataType === 'Boolean' ? false : field?.dataType === 'Integer' || field?.dataType === 'Decimal' ? 0 : '' }
const FieldOptions = memo(function FieldOptions({ fields }: { fields: Field[] }) {
  return <>{fields.map(field => <option value={field.id} key={field.id}>{field.displayName}</option>)}</>
})
function operatorsFor(field?: Field) {
  if (field?.dataType === 'MultiChoice') return ['Contains', 'IsNull', 'IsNotNull']
  if (field?.dataType === 'Boolean' || field?.dataType === 'Lookup') return ['Equal', 'NotEqual', 'IsNull', 'IsNotNull']
  if (field?.dataType === 'Integer' || field?.dataType === 'Decimal' || field?.dataType === 'Date' || field?.dataType === 'DateTime') return ['Equal', 'NotEqual', 'GreaterThan', 'GreaterThanOrEqual', 'LessThan', 'LessThanOrEqual', 'IsNull', 'IsNotNull']
  return ['Equal', 'NotEqual', 'Contains', 'StartsWith', 'EndsWith', 'IsNull', 'IsNotNull']
}
function isGroup(value: unknown): value is FilterGroup {
  return typeof value === 'object' && value !== null && !Array.isArray(value) &&
    ((value as FilterGroup).logic === 'And' || (value as FilterGroup).logic === 'Or') &&
    Array.isArray((value as FilterGroup).conditions)
}
