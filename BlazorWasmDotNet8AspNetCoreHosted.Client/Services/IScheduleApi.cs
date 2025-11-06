using BlazorWasmDotNet8AspNetCoreHosted.Shared.DTOs;

namespace BlazorWasmDotNet8AspNetCoreHosted.Client.Services;

// Контракт клієнта для доступу до даних розкладу
public interface IScheduleApi
{
    Task<MetaResponseDto> GetMeta(DateOnly? weekStart = null);
    Task<List<ScheduleItemDto>> GetWeek(DateOnly weekStart, int? courseId = null, int? groupId = null, int? teacherId = null, int? roomId = null);
    Task<int> Upsert(UpsertScheduleItemRequest request);
    Task Delete(int id);
    Task<int> ClearWeek(ClearWeekRequest req);
}
