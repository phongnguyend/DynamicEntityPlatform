import type { Entity } from './types'

/** The tenant's pinned entities in their stored order — the order the sidebar shortcuts follow. */
export function pinnedEntities(entities: Entity[]) {
  return entities
    .filter(entity => typeof entity.pinnedOrder === 'number')
    .sort((left, right) => (left.pinnedOrder ?? 0) - (right.pinnedOrder ?? 0))
}
