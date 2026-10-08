export interface OrientationGroup {
  id: number
  name: string
  badgeColor: string
  isActive: boolean
  canDelete: boolean
}

export interface Orientation {
  id: number
  year: number
  name: string
  startDate: string
  endDate: string
  attendanceOpensAt: string
  attendanceClosesAt: string
  timeZoneId: string
  isActive: boolean
  retentionDueAt: string
  operatingDates: string[]
  groups: OrientationGroup[]
}

export interface OrientationDraft {
  year: number
  name: string
  startDate: string
  endDate: string
  attendanceOpensAt: string
  attendanceClosesAt: string
}
