using System.Linq;
using System.Net.Http.Json;
using BlazorWasmDotNet8AspNetCoreHosted.Shared.DTOs;

namespace BlazorWasmDotNet8AspNetCoreHosted.Client.Services
{
    
    
    
    
    public sealed class AdminApi(HttpClient http) : IAdminApi
    {
        private readonly HttpClient _http = http;

        private static async Task Ensure(HttpResponseMessage resp)
        {
            if (resp.IsSuccessStatusCode) return;
            var body = await resp.Content.ReadAsStringAsync();
            var msg = string.IsNullOrWhiteSpace(body) ? resp.ReasonPhrase : body;
            throw new HttpRequestException(msg ?? "Request failed", null, resp.StatusCode);
        }

        public async Task<MetaResponseDto> GetMeta()
            => await _http.GetFromJsonAsync<MetaResponseDto>("api/meta")
               ?? new MetaResponseDto(new(), new(), new(), new(), new(), new(), new());

        public async Task<List<CalendarExceptionEditDto>> GetCalendar()
            => await _http.GetFromJsonAsync<List<CalendarExceptionEditDto>>("api/admin/config/calendar") ?? new();
        public async Task<int> UpsertCalendar(CalendarExceptionEditDto dto)
        {
            var resp = await _http.PostAsJsonAsync("api/admin/config/calendar/upsert", dto);
            await Ensure(resp);
            return (await resp.Content.ReadFromJsonAsync<int>())!;
        }
        public async Task DeleteCalendar(int id)
            => await Ensure(await _http.DeleteAsync($"api/admin/config/calendar/{id}"));

        
        public async Task<List<LunchConfigEditDto>> GetLunch()
            => await _http.GetFromJsonAsync<List<LunchConfigEditDto>>("api/admin/config/lunch") ?? new();
        public async Task<int> UpsertLunch(LunchConfigEditDto dto)
        {
            var resp = await _http.PostAsJsonAsync("api/admin/config/lunch/upsert", dto);
            await Ensure(resp);
            return (await resp.Content.ReadFromJsonAsync<int>())!;
        }
        public async Task DeleteLunch(int id)
            => await Ensure(await _http.DeleteAsync($"api/admin/config/lunch/{id}"));

        
        public async Task<List<TeacherViewDto>> GetTeachers()
            => await _http.GetFromJsonAsync<List<TeacherViewDto>>("api/admin/teachers") ?? new();
        public async Task<TeacherEditDto?> GetTeacher(int id)
            => await _http.GetFromJsonAsync<TeacherEditDto>($"api/admin/teachers/{id}");
        public async Task<int> UpsertTeacher(TeacherEditDto dto)
        {
            var resp = await _http.PostAsJsonAsync("api/admin/teachers/upsert", dto);
            await Ensure(resp);
            return (await resp.Content.ReadFromJsonAsync<int>())!;
        }
        public async Task DeleteTeacher(int id)
            => await Ensure(await _http.DeleteAsync($"api/admin/teachers/{id}"));

        
        public async Task<List<GroupEditDto>> GetGroups()
            => await _http.GetFromJsonAsync<List<GroupEditDto>>("api/admin/groups") ?? new();
        public async Task<int> UpsertGroup(GroupEditDto dto)
        {
            var resp = await _http.PostAsJsonAsync("api/admin/groups/upsert", dto);
            await Ensure(resp);
            return (await resp.Content.ReadFromJsonAsync<int>())!;
        }
        public async Task DeleteGroup(int id, bool force = false)
            => await Ensure(await _http.DeleteAsync(force ? $"api/admin/groups/{id}?force=true" : $"api/admin/groups/{id}"));

        
        public async Task<List<ModuleEditDto>> GetModules()
            => await _http.GetFromJsonAsync<List<ModuleEditDto>>("api/admin/modules") ?? new();
        public async Task<int> UpsertModule(ModuleEditDto dto)
        {
            var resp = await _http.PostAsJsonAsync("api/admin/modules/upsert", dto);
            await Ensure(resp);
            return (await resp.Content.ReadFromJsonAsync<int>())!;
        }
        public async Task DeleteModule(int id)
            => await Ensure(await _http.DeleteAsync($"api/admin/modules/{id}"));
        
        public async Task<List<ModuleTopicViewDto>> GetModuleTopics(int moduleId)
            => await _http.GetFromJsonAsync<List<ModuleTopicViewDto>>($"api/admin/modules/{moduleId}/topics") ?? new();
        public async Task<int> UpsertModuleTopic(int moduleId, ModuleTopicDto dto)
        {
            var resp = await _http.PostAsJsonAsync($"api/admin/modules/{moduleId}/topics/upsert", dto);
            await Ensure(resp);
            return (await resp.Content.ReadFromJsonAsync<int>())!;
        }
        public async Task ReorderModuleTopics(int moduleId, List<int> orderedIds)
            => await Ensure(await _http.PostAsJsonAsync($"api/admin/modules/{moduleId}/topics/reorder", orderedIds));
        public async Task DeleteModuleTopic(int moduleId, int topicId)
            => await Ensure(await _http.DeleteAsync($"api/admin/modules/{moduleId}/topics/{topicId}"));

        
        public async Task<List<RoomEditDto>> GetRooms()
            => await _http.GetFromJsonAsync<List<RoomEditDto>>("api/admin/rooms") ?? new();
        public async Task<int> UpsertRoom(RoomEditDto dto)
        {
            var resp = await _http.PostAsJsonAsync("api/admin/rooms/upsert", dto);
            await Ensure(resp);
            return (await resp.Content.ReadFromJsonAsync<int>())!;
        }
        public async Task DeleteRoom(int id)
            => await Ensure(await _http.DeleteAsync($"api/admin/rooms/{id}"));

        
        private sealed record BuildingsVm(List<BuildingEditDto> buildings, List<BuildingTravelEditDto> travels);
        public async Task<List<BuildingEditDto>> GetBuildings()
            => (await _http.GetFromJsonAsync<BuildingsVm>("api/admin/buildings") ?? new(new(), new())).buildings;
        public async Task<List<BuildingTravelEditDto>> GetBuildingTravels()
            => (await _http.GetFromJsonAsync<BuildingsVm>("api/admin/buildings") ?? new(new(), new())).travels;
        public async Task<int> UpsertBuilding(BuildingEditDto dto)
        {
            var resp = await _http.PostAsJsonAsync("api/admin/buildings/upsert", dto);
            await Ensure(resp);
            return (await resp.Content.ReadFromJsonAsync<int>())!;
        }
        public async Task DeleteBuilding(int id)
            => await Ensure(await _http.DeleteAsync($"api/admin/buildings/{id}"));
        public async Task<int> UpsertBuildingTravel(BuildingTravelEditDto dto)
        {
            var resp = await _http.PostAsJsonAsync("api/admin/buildings/travel/upsert", dto);
            await Ensure(resp);
            return 0;
        }
        public async Task DeleteBuildingTravel(int fromId, int toId)
            => await Ensure(await _http.PostAsJsonAsync("api/admin/buildings/travel/delete", new BuildingTravelEditDto(fromId, toId, 0)));

        
        public async Task<List<CourseEditDto>> GetCourses()
            => await _http.GetFromJsonAsync<List<CourseEditDto>>("api/admin/courses") ?? new();
        public async Task<int> UpsertCourse(CourseEditDto dto)
        {
            var resp = await _http.PostAsJsonAsync("api/admin/courses/upsert", dto);
            await Ensure(resp);
            return (await resp.Content.ReadFromJsonAsync<int>())!;
        }
        public async Task DeleteCourse(int id, bool force = false)
            => await Ensure(await _http.DeleteAsync(force ? $"api/admin/courses/{id}?force=true" : $"api/admin/courses/{id}"));

        
        public async Task<List<LessonTypeEditDto>> GetLessonTypes()
            => await _http.GetFromJsonAsync<List<LessonTypeEditDto>>("api/admin/types/lesson") ?? new();
        public async Task UpsertLessonType(LessonTypeEditDto dto)
            => await Ensure(await _http.PostAsJsonAsync("api/admin/types/lesson/upsert", dto));
        public async Task DeleteLessonType(int id)
            => await Ensure(await _http.DeleteAsync($"api/admin/types/lesson/{id}"));

        
        public async Task<List<LessonColorDto>> GetLessonColorPalette()
            => await _http.GetFromJsonAsync<List<LessonColorDto>>("api/admin/types/lesson/palette") ?? new();


        
        public async Task<List<CourseModulePlanDto>> GetModulePlans(int moduleId)
            => await _http.GetFromJsonAsync<List<CourseModulePlanDto>>($"api/admin/plans/module/{moduleId}") ?? new();
        public async Task UpsertModulePlans(int moduleId, List<SaveCourseModulePlanDto> rows)
            => await Ensure(await _http.PostAsJsonAsync($"api/admin/plans/module/{moduleId}/upsert", rows));
        public async Task<CourseModulePlanDto> GetCourseModulePlan(int moduleId)
        {
            var list = await GetModulePlans(moduleId);
            return list.FirstOrDefault() ?? new CourseModulePlanDto(
                CourseId: 0, ModuleId: moduleId, TargetHours: 0, ScheduledHours: 0, IsActive: false);
        }
        public async Task UpsertCourseModulePlan(int moduleId, SaveCourseModulePlanDto dto)
            => await UpsertModulePlans(moduleId, new List<SaveCourseModulePlanDto> { dto });


        public async Task<ModuleSequenceConfigDto?> GetModuleSequence(int courseId)
            => await _http.GetFromJsonAsync<ModuleSequenceConfigDto>($"api/admin/module-sequence/{courseId}");

        public async Task SaveModuleSequence(ModuleSequenceSaveRequestDto dto)
            => await Ensure(await _http.PostAsJsonAsync("api/admin/module-sequence/save", dto));
    }
}


