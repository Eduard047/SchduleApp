using BlazorWasmDotNet8AspNetCoreHosted.Shared.DTOs;

namespace BlazorWasmDotNet8AspNetCoreHosted.Client.Services
{
    
    
    
    
    public interface IAdminApi
    {
        
        Task<MetaResponseDto> GetMeta();

        Task<List<CalendarExceptionEditDto>> GetCalendar();
        Task<int> UpsertCalendar(CalendarExceptionEditDto dto);
        Task DeleteCalendar(int id);

        
        Task<List<LunchConfigEditDto>> GetLunch();
        Task<int> UpsertLunch(LunchConfigEditDto dto);
        Task DeleteLunch(int id);

        
        Task<List<TeacherViewDto>> GetTeachers();
        Task<TeacherEditDto?> GetTeacher(int id);
        Task<int> UpsertTeacher(TeacherEditDto dto);
        Task DeleteTeacher(int id);

        
        Task<List<GroupEditDto>> GetGroups();
        Task<int> UpsertGroup(GroupEditDto dto);
        Task DeleteGroup(int id, bool force = false);

        
        Task<List<ModuleEditDto>> GetModules();
        Task<int> UpsertModule(ModuleEditDto dto);
        Task DeleteModule(int id);
        
        Task<List<ModuleTopicViewDto>> GetModuleTopics(int moduleId);
        Task<int> UpsertModuleTopic(int moduleId, ModuleTopicDto dto);
        Task ReorderModuleTopics(int moduleId, List<int> orderedIds);
        Task DeleteModuleTopic(int moduleId, int topicId);

        
        Task<List<RoomEditDto>> GetRooms();
        Task<int> UpsertRoom(RoomEditDto dto);
        Task DeleteRoom(int id);

        
        Task<List<BuildingEditDto>> GetBuildings();
        Task<List<BuildingTravelEditDto>> GetBuildingTravels();
        Task<int> UpsertBuilding(BuildingEditDto dto);
        Task DeleteBuilding(int id);
        Task<int> UpsertBuildingTravel(BuildingTravelEditDto dto);
        Task DeleteBuildingTravel(int fromId, int toId);

        
        Task<List<CourseEditDto>> GetCourses();
        Task<int> UpsertCourse(CourseEditDto dto);
        Task DeleteCourse(int id, bool force = false);

        
        Task<List<LessonTypeEditDto>> GetLessonTypes();
        Task UpsertLessonType(LessonTypeEditDto dto);
        Task DeleteLessonType(int id);

        
        Task<List<LessonColorDto>> GetLessonColorPalette();


        
        Task<List<CourseModulePlanDto>> GetModulePlans(int moduleId);
        Task UpsertModulePlans(int moduleId, List<SaveCourseModulePlanDto> rows);
        Task<CourseModulePlanDto> GetCourseModulePlan(int moduleId);
        Task UpsertCourseModulePlan(int moduleId, SaveCourseModulePlanDto dto);


        Task<ModuleSequenceConfigDto?> GetModuleSequence(int courseId);
        Task SaveModuleSequence(ModuleSequenceSaveRequestDto dto);
    }
}


