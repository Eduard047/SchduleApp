// DTO для планів навантаження курсів та модулів
namespace BlazorWasmDotNet8AspNetCoreHosted.Shared.DTOs;

public record CourseModulePlanDto(
    int CourseId,
    int ModuleId,
    int TargetHours,
    int ScheduledHours,
    bool IsActive
);

public record SaveCourseModulePlanDto(
    int TargetHours,
    bool IsActive
);
