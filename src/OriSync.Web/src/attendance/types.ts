export type AttendanceStatus = 'Present' | 'Absent'

export interface AttendanceMutationResponse {
  outcome: 'MarkedPresent' | 'AlreadyPresent' | 'Corrected'
  enrollmentId: number
  studentNumber: string
  fullNames: string
  surname: string
  status: AttendanceStatus
  message: string
}

export interface AttendanceProblem {
  code?: 'InvalidQr' | 'ExpiredQr' | 'WrongGroup' | 'AttendanceClosed' | string
  detail?: string
  title?: string
  groupName?: string
}
