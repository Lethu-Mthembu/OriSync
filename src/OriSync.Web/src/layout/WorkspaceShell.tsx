import { useState, type ReactNode } from 'react'
import type { Session } from '../auth/types'
import { AppIcon, type IconName } from './AppIcon'

export interface WorkspaceNavigationItem<Section extends string> {
  id: Section
  label: string
  icon: IconName
}

export function WorkspaceShell<Section extends string>({
  session,
  activeSection,
  items,
  onSelect,
  onChangePassword,
  onLogout,
  children,
}: {
  session: Session
  activeSection: Section
  items: WorkspaceNavigationItem<Section>[]
  onSelect: (section: Section) => void
  onChangePassword: () => void
  onLogout: () => void
  children: ReactNode
}) {
  const [mobileOpen, setMobileOpen] = useState(false)
  const [pinned, setPinned] = useState(false)
  const fullName = `${session.firstName} ${session.surname}`

  function choose(section: Section) {
    onSelect(section)
    setMobileOpen(false)
  }

  function toggleNavigation() {
    if (globalThis.matchMedia?.('(max-width: 48rem)').matches) {
      setMobileOpen((current) => !current)
      return
    }
    setPinned((current) => !current)
  }

  return (
    <div className="workspace-shell">
      <header className="mobile-workspace-header">
        <button className="mobile-menu-button" type="button" onClick={toggleNavigation} aria-label="Open navigation" aria-expanded={mobileOpen}>
          <AppIcon name="menu" size={24} />
        </button>
        <span className="mobile-brand-mark" aria-hidden="true">O</span>
        <span className="mobile-brand-name">OriSync</span>
      </header>

      {mobileOpen && <button className="sidebar-backdrop" type="button" aria-label="Close navigation" onClick={() => setMobileOpen(false)} />}

      <aside className={`workspace-sidebar${pinned ? ' sidebar-pinned' : ''}${mobileOpen ? ' sidebar-mobile-open' : ''}`}>
        <div className="sidebar-brand-row">
          <span className="sidebar-brand-mark" aria-hidden="true">O</span>
          <span className="sidebar-label sidebar-brand-name">OriSync</span>
          <button className="sidebar-toggle" type="button" onClick={toggleNavigation} aria-label={mobileOpen ? 'Close navigation' : pinned ? 'Collapse navigation' : 'Keep navigation expanded'}>
            <AppIcon name={mobileOpen ? 'close' : 'menu'} size={21} />
          </button>
        </div>

        <nav className="sidebar-navigation" aria-label={`${session.role} workspace`}>
          {items.map((item) => (
            <button
              className={`sidebar-link${activeSection === item.id ? ' active' : ''}`}
              key={item.id}
              type="button"
              title={item.label}
              aria-current={activeSection === item.id ? 'page' : undefined}
              onClick={() => choose(item.id)}
            >
              <AppIcon name={item.icon} />
              <span className="sidebar-label">{item.label}</span>
            </button>
          ))}
        </nav>

        <div className="sidebar-account">
          <div className="sidebar-user" title={fullName}>
            <span className="sidebar-avatar" aria-hidden="true">{session.firstName[0]}{session.surname[0]}</span>
            <span className="sidebar-label sidebar-user-details"><strong>{fullName}</strong><small>{session.role}</small></span>
          </div>
          <button className="sidebar-link" type="button" title="Change password" onClick={onChangePassword}>
            <AppIcon name="password" />
            <span className="sidebar-label">Change password</span>
          </button>
          <button className="sidebar-link sidebar-signout" type="button" title="Sign out" onClick={onLogout}>
            <AppIcon name="logout" />
            <span className="sidebar-label">Sign out</span>
          </button>
        </div>
      </aside>

      <div className="workspace-content">{children}</div>
    </div>
  )
}
