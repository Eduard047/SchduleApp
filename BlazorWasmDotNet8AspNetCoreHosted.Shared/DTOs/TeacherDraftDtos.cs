// DTO структури для чернеток викладачів
namespace BlazorWasmDotNet8AspNetCoreHosted.Shared.DTOs;

public enum DraftStatusDto { Draft = 0, Published = 1 }

public record TeacherDraftItemDto(
    int Id,
    DateOnly Date,
    string TimeStart,
    string TimeEnd,
    int DayNumber,
    string Group,
    int GroupId,
    string Module,
    int ModuleId,
    string? TopicCode,
    int? ModuleTopicId,
    string Teacher,
    int? TeacherId,
    string Room,
    int? RoomId,
    bool RequiresRoom,
    int LessonTypeId,
    string LessonTypeCode,
    string LessonTypeName,
    DraftStatusDto Status,
    int? PublishedItemId,
    string? Warnings,
    bool IsLocked = false,
    bool IsRescheduled = false,
    int? RescheduledFromLessonTypeId = null,
    string? BatchKey = null,
    List<string>? TeacherNames = null,
    string? LessonTypeCss = null,
    bool IsSelfStudy = false
);

public record DraftUpsertRequest(
    int? Id,
    DateOnly Date,
    string TimeStart,
    string TimeEnd,
    int GroupId,
    int ModuleId,
    int? ModuleTopicId,
    int? TeacherId,
    int? RoomId,
    bool RequiresRoom,
    int LessonTypeId,
    bool OverrideNonWorkingDay = false,
    string? BatchKey = null,
    bool IsLocked = false,
    bool IgnoreValidationErrors = false,
    bool IsSelfStudy = false
);

public record DraftValidationIssueDto(
    string Severity,
    string Code,
    string Title,
    string Description
);

public record DraftValidationReportDto(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<DraftValidationIssueDto> Issues
);

public record DraftWeekQuery(DateOnly WeekStart, int? TeacherId);

public record PublishWeekRequest(DateOnly WeekStart, int? TeacherId);

public record AutogenMonthRequest(
    DateOnly MonthStart,
    int? CourseId,
    int? GroupId,
    int? TeacherId,
    bool AllowOnDaysOff,
    WeekPreset Days
);

public record AutogenCourseRequest(
    DateOnly From,
    DateOnly To,
    int? CourseId,
    int? GroupId,
    int? TeacherId,
    bool AllowOnDaysOff,
    WeekPreset Days
);
