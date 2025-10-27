using System.Net.Http.Json;
using BlazorWasmDotNet8AspNetCoreHosted.Client.Services;
using BlazorWasmDotNet8AspNetCoreHosted.Shared.DTOs;

public sealed class TeacherDraftsApi(HttpClient http) : ITeacherDraftsApi
{
    public async Task<List<TeacherDraftItemDto>> GetWeek(DateOnly weekStart, int? teacherId)
    {
        var url = $"api/teacher-drafts?weekStart={weekStart:yyyy-MM-dd}" +
                  (teacherId is int t ? $"&teacherId={t}" : "");
        return await http.GetFromJsonAsync<List<TeacherDraftItemDto>>(url) ?? new();
    }

    public async Task<AutoGenResult> AutogenWeek(AutoGenRequest req)
    {
        var res = await http.PostAsJsonAsync("api/teacher-drafts/autogen/week", req);
        await res.EnsureSuccessWithDetailsAsync();
        return (await res.Content.ReadFromJsonAsync<AutoGenResult>())!;
    }

    public async Task<int> ClearWeek(ClearWeekRequest req)
    {
        var res = await http.PostAsJsonAsync("api/teacher-drafts/clear-week", req);
        await res.EnsureSuccessWithDetailsAsync();
        var dto = await res.Content.ReadFromJsonAsync<ClearWeekResult>();
        return dto?.Deleted ?? 0;
    }

    public async Task<int> Upsert(DraftUpsertRequest req)
    {
        var res = await http.PostAsJsonAsync("api/teacher-drafts/upsert", req);
        await res.EnsureSuccessWithDetailsAsync();
        return await res.Content.ReadFromJsonAsync<int>();
    }

    public async Task Delete(int id)
    {
        var res = await http.DeleteAsync($"api/teacher-drafts/{id}");
        await res.EnsureSuccessWithDetailsAsync();
    }

    public async Task<AutoGenResult> AutogenMonth(AutogenMonthRequest req)
    {
        var res = await http.PostAsJsonAsync("api/teacher-drafts/autogen/month", req);
        await res.EnsureSuccessWithDetailsAsync();
        return (await res.Content.ReadFromJsonAsync<AutoGenResult>())!;
    }

    public async Task<AutoGenResult> AutogenCourse(AutogenCourseRequest req)
    {
        var res = await http.PostAsJsonAsync("api/teacher-drafts/autogen/course", req);
        await res.EnsureSuccessWithDetailsAsync();
        return (await res.Content.ReadFromJsonAsync<AutoGenResult>())!;
    }

    public async Task PublishWeek(PublishWeekRequest req)
    {
        var res = await http.PostAsJsonAsync("api/teacher-drafts/publish-week", req);
        await res.EnsureSuccessWithDetailsAsync();
    }
}
