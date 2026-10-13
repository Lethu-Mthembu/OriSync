export interface DashboardMetrics {
  totalStudents: number
  presentToday: number
  absentToday: number
  attendanceRate: number
}

export interface GroupStudentCount {
  groupId: number
  groupName: string
  badgeColor: string
  studentCount: number
  rank: number
}

export interface AdminDashboardResponse {
  hasActiveOrientation: boolean
  orientationName: string | null
  attendanceDate: string | null
  metrics: DashboardMetrics
  groupRanking: GroupStudentCount[]
}

export interface MentorDashboardResponse {
  orientationName: string
  attendanceDate: string
  groupId: number
  groupName: string
  badgeColor: string
  metrics: DashboardMetrics
}
