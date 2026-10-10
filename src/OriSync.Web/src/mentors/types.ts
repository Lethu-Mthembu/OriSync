export interface MentorDirectoryItem {
  id: number
  recordType: 'Mentor' | 'Invitation'
  status: string
  email: string
  mentorNumber: string | null
  firstName: string | null
  surname: string | null
  groupId: number
  groupName: string
  groupBadgeColor: string
  createdAt: string
  deletionEligibleAt: string | null
  canDelete: boolean
}

export interface MentorDirectoryResponse {
  items: MentorDirectoryItem[]
  page: number
  pageSize: number
  totalItems: number
  totalPages: number
}

export interface MentorActivationDetails {
  email: string
  groupId: number
  groupName: string
  groupBadgeColor: string
  expiresAt: string
}
