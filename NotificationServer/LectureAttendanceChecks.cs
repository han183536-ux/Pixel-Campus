using System;
using WebNotifications;

internal static class LectureAttendanceChecks
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("PASS: " + message); }
    public static void Main()
    {
        string Status(long now, bool known = true, bool attended = false) => LectureAttendancePeriod.Status(10000,20000,30000,known,false,attended,now);
        Check(Status(9999).StartsWith("출석 인정 시작까지"), "Before the period starts, show time until attendance opens");
        Check(Status(10000).StartsWith("출석 인정 종료까지"), "The exact start time opens the attendance period");
        Check(Status(19999).StartsWith("출석 인정 종료까지"), "Immediately before the deadline, normal attendance is still open");
        Check(Status(20000).Contains("지각 인정 종료까지"), "At the attendance deadline, only the separate late window remains");
        Check(Status(30000) == "출석 인정 기간 종료", "The exact late deadline closes the late window");
        Check(Status(15000, false) == "출석 인정 기간 확인 필요", "Unknown period metadata never creates a deadline");
        Check(Status(15000, true, true).StartsWith("출석 인정 종료까지"), "An unconfirmed attended flag never hides an unfinished video");
        Check(LectureAttendancePeriod.Status(10000,20000,30000,true,true,true,15000) == "학교 출석 확인됨", "Only confirmed school attendance completes a video");
        Check(LectureAttendancePeriod.Status(20000,10000,30000,true,false,false,15000) == "출석 인정 기간 확인 필요", "Reversed dates cannot become a valid attendance window");
        Check(LectureAttendancePeriod.Status(10000,long.MaxValue,0,true,false,false,15000) == "출석 인정 기간 확인 필요", "Out-of-range dates are rejected without overflowing");
    }
}
