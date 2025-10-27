namespace BlazorWasmDotNet8AspNetCoreHosted.Shared.DTOs;

public record UpsertScheduleItemRequest(
    int? Id,
    DateOnly Date,
    string TimeStart,
    string TimeEnd,
    int GroupId,
    int ModuleId,
    int? TeacherId,
    int? RoomId,
    int LessonTypeId,
    bool IsLocked,
    bool OverrideNonWorkingDay = false
);
