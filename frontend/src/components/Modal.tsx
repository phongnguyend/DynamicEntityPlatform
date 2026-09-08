import type { ReactNode } from 'react'
import { X } from 'lucide-react'

export function Modal({ title, onClose, wide, children }: { title: string; onClose: () => void; wide?: boolean; children: ReactNode }) {
  return <div className="modal-backdrop" onClick={onClose}>
    <div className={wide ? 'modal wide' : 'modal'} onClick={event => event.stopPropagation()}>
      <div className="modal-header"><h3>{title}</h3><button type="button" className="link icon-only" aria-label="Close dialog" title="Close" onClick={onClose}><X /></button></div>
      <div className="modal-body">{children}</div>
    </div>
  </div>
}
