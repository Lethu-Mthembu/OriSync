export type StudentAgeGroup = 'C1' | 'C2' | 'C3' | 'C4' | 'C5'
export type Gender = 'Male' | 'Female' | 'Other'
export type Ethnicity = 'White' | 'African' | 'Coloured' | 'Indian' | 'Other'

export interface StudentFormValues {
  studentNumber: string
  fullNames: string
  surname: string
  studentEmail: string
  personalEmail: string | null
  ageGroup: StudentAgeGroup
  gender: Gender
  ethnicity: Ethnicity
}

export interface StudentDirectoryItem extends StudentFormValues {
  enrollmentId: number
  initials: string
  groupId: number | null
  groupName: string
  groupBadgeColor: string
  attendanceStatus: 'Present' | 'Absent' | '—'
  isIncomingTransfer: boolean
  groupEffectiveDate: string | null
  qrVersion: number
}

export interface StudentDirectoryResponse {
  items: StudentDirectoryItem[]
  page: number
  pageSize: number
  totalItems: number
  totalPages: number
}

export interface StudentLookupResponse {
  match: 'New' | 'Unassigned' | 'Assigned' | 'Unavailable'
  studentNumber: string
  fullNames: string | null
  surname: string | null
  studentEmail: string | null
  groupName: string | null
  message: string | null
}

export interface StudentMutationResponse {
  student: StudentDirectoryItem
  outcome: 'RegisteredPresent' | 'AssignedPresent' | 'RestoredToday' | 'MovesTomorrow' | 'Updated'
}

export interface OrientationGroupOption {
  id: number
  name: string
  badgeColor: string
  isActive: boolean
}

export interface OrientationOption {
  isActive: boolean
  groups: OrientationGroupOption[]
}
