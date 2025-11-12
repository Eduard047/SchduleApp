// DTO для очищення тижня в розкладі
namespace BlazorWasmDotNet8AspNetCoreHosted.Shared.DTOs;

public record ClearWeekRequest(
    DateOnly WeekStart,
    int? CourseId = null,
    int? GroupId = null
);

public record ClearWeekResult(int Deleted);
