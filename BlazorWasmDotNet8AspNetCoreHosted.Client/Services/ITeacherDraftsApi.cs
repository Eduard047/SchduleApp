using BlazorWasmDotNet8AspNetCoreHosted.Shared.DTOs;

public interface ITeacherDraftsApi
{
    Task<List<TeacherDraftItemDto>> GetWeek(DateOnly weekStart, int? teacherId);
    Task<int> Upsert(DraftUpsertRequest req);
    Task Delete(int id);
    Task<AutoGenResult> AutogenWeek(AutoGenRequest req);
    Task<int> ClearWeek(ClearWeekRequest req);

    Task<AutoGenResult> AutogenMonth(AutogenMonthRequest req);
    Task<AutoGenResult> AutogenCourse(AutogenCourseRequest req);
    Task PublishWeek(PublishWeekRequest req);
}
