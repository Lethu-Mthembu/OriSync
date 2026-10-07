namespace OriSync.Api.Domain;

public enum PersonType
{
    Admin,
    Mentor,
    Student
}

public enum EmailType
{
    Student,
    Personal,
    Login
}

public enum AccountRole
{
    Admin,
    Mentor
}

public enum StudentAgeGroup
{
    C1,
    C2,
    C3,
    C4,
    C5
}

public enum Gender
{
    Male,
    Female,
    Other
}

public enum Ethnicity
{
    White,
    African,
    Coloured,
    Indian,
    Other
}

public enum AttendanceStatus
{
    Absent,
    Present
}

public enum AttendanceCaptureMethod
{
    Automatic,
    Qr,
    Manual,
    Registration
}
