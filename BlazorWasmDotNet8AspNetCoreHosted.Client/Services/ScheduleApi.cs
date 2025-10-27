using System.Net.Http.Json;
using System.Web;
using BlazorWasmDotNet8AspNetCoreHosted.Shared.DTOs;

namespace BlazorWasmDotNet8AspNetCoreHosted.Client.Services;

public sealed class ScheduleApi(HttpClient http) : IScheduleApi
{
    public async Task<MetaResponseDto> GetMeta(DateOnly? weekStart = null)
    {
        var url = weekStart is DateOnly d
            ? $"api/meta?weekStart={d:yyyy-MM-dd}"
            : "api/meta";
        return await http.GetFromJsonAsync<MetaResponseDto>(url) ?? new MetaResponseDto(new(), new(), new(), new(), new(), new(), new());
    }

    public async Task<List<ScheduleItemDto>> GetWeek(DateOnly weekStart, int? courseId = null, int? groupId = null, int? teacherId = null, int? roomId = null)
    {
        var qb = HttpUtility.ParseQueryString(string.Empty);
        qb["weekStart"] = weekStart.ToString("yyyy-MM-dd");
        if (courseId is int cid) qb["courseId"] = cid.ToString();
        if (groupId is int gid) qb["groupId"] = gid.ToString();
        if (teacherId is int tid) qb["teacherId"] = tid.ToString();
        if (roomId is int rid) qb["roomId"] = rid.ToString();
        var url = $"api/schedule?{qb}";
        return await http.GetFromJsonAsync<List<ScheduleItemDto>>(url) ?? new();
    }

    public async Task<int> Upsert(UpsertScheduleItemRequest request)
    {
        var res = await http.PostAsJsonAsync("api/schedule/upsert", request);
        await res.EnsureSuccessWithDetailsAsync();
        return await res.Content.ReadFromJsonAsync<int>();
    }

    public async Task Delete(int id)
    {
        var res = await http.DeleteAsync($"api/schedule/{id}");
        await res.EnsureSuccessWithDetailsAsync();
    }

    public async Task<int> ClearWeek(ClearWeekRequest req)
    {
        var res = await http.PostAsJsonAsync("api/schedule/clear", req);
        await res.EnsureSuccessWithDetailsAsync();
        var dto = await res.Content.ReadFromJsonAsync<ClearWeekResult>();
        return dto?.Deleted ?? 0;
    }
}
