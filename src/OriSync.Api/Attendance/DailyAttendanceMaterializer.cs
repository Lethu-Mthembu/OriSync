using Microsoft.EntityFrameworkCore;
using OriSync.Api.Data;

namespace OriSync.Api.Attendance;

public sealed class DailyAttendanceMaterializer(OriSyncDbContext dbContext)
{
    public Task EnsureGroupAsync(
        long orientationId,
        long groupId,
        DateOnly attendanceDate,
        CancellationToken cancellationToken) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO attendance_records
                (student_enrollment_id, orientation_id, group_id, attendance_date, status, capture_method)
            SELECT
                enrollment.id,
                enrollment.orientation_id,
                enrollment.current_group_id,
                {attendanceDate},
                'Absent',
                'Automatic'
            FROM student_enrollments AS enrollment
            WHERE enrollment.orientation_id = {orientationId}
              AND enrollment.current_group_id = {groupId}
              AND NOT EXISTS (
                  SELECT 1
                  FROM attendance_records AS attendance
                  WHERE attendance.student_enrollment_id = enrollment.id
                    AND attendance.attendance_date = {attendanceDate})
            ON CONFLICT (student_enrollment_id, attendance_date) DO NOTHING;
            """, cancellationToken);

    public Task EnsureOrientationAsync(
        long orientationId,
        DateOnly attendanceDate,
        CancellationToken cancellationToken) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO attendance_records
                (student_enrollment_id, orientation_id, group_id, attendance_date, status, capture_method)
            SELECT
                enrollment.id,
                enrollment.orientation_id,
                enrollment.current_group_id,
                {attendanceDate},
                'Absent',
                'Automatic'
            FROM student_enrollments AS enrollment
            WHERE enrollment.orientation_id = {orientationId}
              AND enrollment.current_group_id IS NOT NULL
              AND NOT EXISTS (
                  SELECT 1
                  FROM attendance_records AS attendance
                  WHERE attendance.student_enrollment_id = enrollment.id
                    AND attendance.attendance_date = {attendanceDate})
            ON CONFLICT (student_enrollment_id, attendance_date) DO NOTHING;
            """, cancellationToken);
}
