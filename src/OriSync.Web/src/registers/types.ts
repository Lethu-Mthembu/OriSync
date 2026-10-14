export type RegisterFilter = 'All' | 'Present' | 'Absent'

export interface RegisterSummary {
  rowCount: number
  groupCount: number
  attendanceStatus: RegisterFilter
}

export interface RegisterGroupSummary {
  groupId: number
  groupName: string
  badgeColor: string
  totalStudents: number
  presentStudents: number
  absentStudents: number
  matchingStudents: number
}

export interface RegisterOrientation {
  id: number
  year: number
  name: string
  isActive: boolean
  operatingDates: string[]
  groups: Array<{
    id: number
    name: string
    badgeColor: string
    isActive: boolean
  }>
}
