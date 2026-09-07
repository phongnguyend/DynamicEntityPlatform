import {
  Ban, Bell, Book, Bookmark, Boxes, Briefcase, Building2, Calendar, ClipboardList, Clock, Cloud, Coffee, Coins,
  Contact, Cpu, CreditCard, Database, Factory, FileText, Flag, Folder, Gift, Globe, GraduationCap, Handshake,
  Heart, House, IdCard, Key, Layers, Leaf, Lock, Mail, MapPin, Newspaper, Package, Phone, Plane, Receipt, Rocket,
  Server, Shield, ShoppingCart, SquareCheck, Star, Stethoscope, Store, TableProperties, Tag, Target, Ticket,
  TrendingUp, Truck, User, Users, Utensils, Wallet, Warehouse, Wrench, type LucideIcon,
} from 'lucide-react'

/**
 * The icon set is owned by the client: the platform stores whichever name it is handed and never
 * interprets it. A stored name that is not in this catalog — an older or hand-edited value — falls
 * back to the default icon rather than leaving a hole in the list.
 */
export const ENTITY_ICONS: Array<{ name: string; label: string; Icon: LucideIcon }> = [
  { name: 'table-properties', label: 'Table', Icon: TableProperties },
  { name: 'layers', label: 'Layers', Icon: Layers },
  { name: 'database', label: 'Database', Icon: Database },
  { name: 'server', label: 'Server', Icon: Server },
  { name: 'cpu', label: 'Device', Icon: Cpu },
  { name: 'cloud', label: 'Cloud', Icon: Cloud },
  { name: 'folder', label: 'Folder', Icon: Folder },
  { name: 'file-text', label: 'Document', Icon: FileText },
  { name: 'clipboard-list', label: 'Checklist', Icon: ClipboardList },
  { name: 'square-check', label: 'Task', Icon: SquareCheck },
  { name: 'book', label: 'Book', Icon: Book },
  { name: 'newspaper', label: 'Article', Icon: Newspaper },
  { name: 'users', label: 'People', Icon: Users },
  { name: 'user', label: 'Person', Icon: User },
  { name: 'contact', label: 'Contact', Icon: Contact },
  { name: 'id-card', label: 'Employee', Icon: IdCard },
  { name: 'handshake', label: 'Partner', Icon: Handshake },
  { name: 'building-2', label: 'Company', Icon: Building2 },
  { name: 'store', label: 'Store', Icon: Store },
  { name: 'factory', label: 'Factory', Icon: Factory },
  { name: 'warehouse', label: 'Warehouse', Icon: Warehouse },
  { name: 'house', label: 'Property', Icon: House },
  { name: 'briefcase', label: 'Briefcase', Icon: Briefcase },
  { name: 'package', label: 'Product', Icon: Package },
  { name: 'boxes', label: 'Inventory', Icon: Boxes },
  { name: 'shopping-cart', label: 'Order', Icon: ShoppingCart },
  { name: 'receipt', label: 'Invoice', Icon: Receipt },
  { name: 'credit-card', label: 'Payment', Icon: CreditCard },
  { name: 'wallet', label: 'Wallet', Icon: Wallet },
  { name: 'coins', label: 'Revenue', Icon: Coins },
  { name: 'trending-up', label: 'Growth', Icon: TrendingUp },
  { name: 'target', label: 'Target', Icon: Target },
  { name: 'truck', label: 'Shipment', Icon: Truck },
  { name: 'plane', label: 'Travel', Icon: Plane },
  { name: 'map-pin', label: 'Location', Icon: MapPin },
  { name: 'globe', label: 'Region', Icon: Globe },
  { name: 'calendar', label: 'Calendar', Icon: Calendar },
  { name: 'clock', label: 'Timesheet', Icon: Clock },
  { name: 'mail', label: 'Message', Icon: Mail },
  { name: 'phone', label: 'Call', Icon: Phone },
  { name: 'bell', label: 'Notification', Icon: Bell },
  { name: 'ticket', label: 'Ticket', Icon: Ticket },
  { name: 'tag', label: 'Category', Icon: Tag },
  { name: 'bookmark', label: 'Bookmark', Icon: Bookmark },
  { name: 'flag', label: 'Flag', Icon: Flag },
  { name: 'star', label: 'Rating', Icon: Star },
  { name: 'heart', label: 'Favorite', Icon: Heart },
  { name: 'gift', label: 'Reward', Icon: Gift },
  { name: 'rocket', label: 'Launch', Icon: Rocket },
  { name: 'wrench', label: 'Maintenance', Icon: Wrench },
  { name: 'key', label: 'Access', Icon: Key },
  { name: 'lock', label: 'Secret', Icon: Lock },
  { name: 'shield', label: 'Policy', Icon: Shield },
  { name: 'stethoscope', label: 'Patient', Icon: Stethoscope },
  { name: 'graduation-cap', label: 'Course', Icon: GraduationCap },
  { name: 'utensils', label: 'Menu item', Icon: Utensils },
  { name: 'coffee', label: 'Café', Icon: Coffee },
  { name: 'leaf', label: 'Sustainability', Icon: Leaf },
]

const DEFAULT_ICON = TableProperties
const iconsByName = new Map(ENTITY_ICONS.map(option => [option.name, option.Icon]))

export function getEntityIcon(icon: string | null | undefined) {
  return (icon ? iconsByName.get(icon) : undefined) ?? DEFAULT_ICON
}

export function EntityIcon({ icon, className }: { icon?: string | null; className?: string }) {
  const Icon = getEntityIcon(icon)
  return <Icon className={className} aria-hidden="true" />
}

export function EntityIconPicker({ icon, onChange }: { icon?: string; onChange: (icon?: string) => void }) {
  return <fieldset className="entity-icon-picker">
    <legend>Icon</legend>
    <div className="entity-icon-options">
      <button type="button" className={`entity-icon-option none${icon ? '' : ' selected'}`}
        title="Default icon" aria-label="Default icon" aria-pressed={!icon} onClick={() => onChange(undefined)}><Ban /></button>
      {ENTITY_ICONS.map(option => <button key={option.name} type="button"
        className={`entity-icon-option${icon === option.name ? ' selected' : ''}`}
        title={option.label} aria-label={option.label} aria-pressed={icon === option.name}
        onClick={() => onChange(option.name)}><option.Icon /></button>)}
    </div>
  </fieldset>
}
