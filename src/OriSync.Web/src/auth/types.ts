export type AuthView = 'login' | 'mentor-reset' | 'admin-recovery' | 'change-password'

export interface Session {
  accountId: number
  firstName: string
  surname: string
  role: 'Admin' | 'Mentor'
  groupId: number | null
  mustChangePassword: boolean
  absoluteExpiresAt: string
}
