using System.Linq;

using System.Collections.Generic;

using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BlazorWasmDotNet8AspNetCoreHosted.Server.Infrastructure;
using BlazorWasmDotNet8AspNetCoreHosted.Server.Domain.Entities;
using BlazorWasmDotNet8AspNetCoreHosted.Shared.DTOs;

namespace BlazorWasmDotNet8AspNetCoreHosted.Server.Controllers.Admin;





[ApiController]
[Route("api/admin/modules")]
public class AdminModulesController(AppDbContext db) : ControllerBase
{
    
    
    
    
    
    
    [HttpGet]
    public async Task<object> List()
    {
        
        var modules = await db.Modules.AsNoTracking()
            .Select(m => new
            {
                m.Id,
                m.Code,
                m.Title,
                m.CourseId,
                m.Credits,
                m.Competences,
                m.LearningOutcomes,
                m.ReportingForm,
                
                AllowedRoomIds = m.AllowedRooms.Select(ar => ar.RoomId).ToList(),
                AllowedBuildingIds = m.AllowedBuildings.Select(ab => ab.BuildingId).ToList()
            })
            .ToListAsync();

        return modules;
    }

    
    
    
    
    
    [HttpPost("upsert")]
    public async Task<ActionResult<int>> Upsert(ModuleEditDto dto)
    {
        
        _ = await db.Courses.FindAsync(dto.CourseId) ?? throw new ArgumentException("Курс не знайдено");

        Module m;
        if (dto.Id is int id && id > 0)
        {
            
            m = await db.Modules
                .Include(x => x.AllowedRooms)
                .Include(x => x.AllowedBuildings)
                .FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new ArgumentException("Модуль не знайдено");

            var oldCourseId = m.CourseId;

            
            m.Code = dto.Code;
            m.Title = dto.Title;
            m.CourseId = dto.CourseId;
            
            m.Credits = dto.Credits;
            m.Competences = dto.Competences;
            m.LearningOutcomes = dto.LearningOutcomes;
            m.ReportingForm = dto.ReportingForm;

            
            var oldRoomIds = m.AllowedRooms.Select(x => x.RoomId).ToHashSet();
            var newRoomIds = dto.AllowedRoomIds.ToHashSet();

            
            db.ModuleRooms.RemoveRange(m.AllowedRooms.Where(x => !newRoomIds.Contains(x.RoomId)));
            
            foreach (var add in newRoomIds.Except(oldRoomIds))
                db.ModuleRooms.Add(new ModuleRoom { ModuleId = m.Id, RoomId = add });

            
            var oldBIds = m.AllowedBuildings.Select(x => x.BuildingId).ToHashSet();
            var newBIds = dto.AllowedBuildingIds.ToHashSet();

            db.ModuleBuildings.RemoveRange(m.AllowedBuildings.Where(x => !newBIds.Contains(x.BuildingId)));
            foreach (var add in newBIds.Except(oldBIds))
                db.ModuleBuildings.Add(new ModuleBuilding { ModuleId = m.Id, BuildingId = add });

            await db.SaveChangesAsync();

            

            
            await CloneIntoAdditionalCoursesAsync(m, dto);
            return Ok(m.Id);
        }
        else
        {
            
            m = new Module
            {
                Code = dto.Code,
                Title = dto.Title,
                CourseId = dto.CourseId,
                Credits = dto.Credits,
                Competences = dto.Competences,
                LearningOutcomes = dto.LearningOutcomes,
                ReportingForm = dto.ReportingForm
            };
            db.Modules.Add(m);
            await db.SaveChangesAsync();

            
            foreach (var rid in dto.AllowedRoomIds.Distinct())
                db.ModuleRooms.Add(new ModuleRoom { ModuleId = m.Id, RoomId = rid });
            foreach (var bid in dto.AllowedBuildingIds.Distinct())
                db.ModuleBuildings.Add(new ModuleBuilding { ModuleId = m.Id, BuildingId = bid });

            await db.SaveChangesAsync();

            
            await CloneIntoAdditionalCoursesAsync(m, dto);
            return Ok(m.Id);
        }
    }

    
    
    
    
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, [FromQuery] bool force = false)
    {
        var module = await db.Modules.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id);
        if (module is null) return NotFound();

        
        var used = await db.ScheduleItems.AnyAsync(x => x.ModuleId == id);
        if (used && !force)
            return Conflict(new { message = "Модуль використовується у розкладі" });

        if (force)
        {
            
            var q = db.ScheduleItems.Where(x => x.ModuleId == id);

            var affectedLoads = await q.Where(x => x.TeacherId != null)
                .Select(x => new { x.TeacherId, CourseId = x.Group.CourseId })
                .Distinct()
                .ToListAsync();

            
            await q.ExecuteDeleteAsync();

            
            await db.ModulePlans.Where(p => p.ModuleId == id).ExecuteDeleteAsync();
            await db.ModuleRooms.Where(x => x.ModuleId == id).ExecuteDeleteAsync();
            await db.ModuleBuildings.Where(x => x.ModuleId == id).ExecuteDeleteAsync();

            
            if (affectedLoads.Count > 0)
            {
                var tIds = affectedLoads.Select(a => a.TeacherId!.Value).Distinct().ToList();
                var cIds = affectedLoads.Select(a => a.CourseId).Distinct().ToList();

                
                var excludeLoadIds = await db.LessonTypes
                    .Where(lt => !lt.CountInLoad)
                    .Select(lt => lt.Id)
                    .ToListAsync();

                
                var counts = await db.ScheduleItems
                    .Include(si => si.Group)
                    .Where(si => si.TeacherId != null
                                 && !excludeLoadIds.Contains(si.LessonTypeId)
                                 && tIds.Contains(si.TeacherId!.Value)
                                 && cIds.Contains(si.Group.CourseId))
                    .GroupBy(si => new { TeacherId = si.TeacherId!.Value, si.Group.CourseId })
                    .Select(g => new { g.Key.TeacherId, g.Key.CourseId, C = g.Count() })
                    .ToListAsync();

                var loadsToUpdate = await db.TeacherCourseLoads
                    .Where(l => tIds.Contains(l.TeacherId) && cIds.Contains(l.CourseId))
                    .ToListAsync();

                foreach (var l in loadsToUpdate)
                    l.ScheduledHours = counts.FirstOrDefault(c => c.TeacherId == l.TeacherId && c.CourseId == l.CourseId)?.C ?? 0;

                await db.SaveChangesAsync();
            }
        }
        else
        {
            
            await db.ModuleRooms.Where(x => x.ModuleId == id).ExecuteDeleteAsync();
            await db.ModuleBuildings.Where(x => x.ModuleId == id).ExecuteDeleteAsync();
        }

        
        var rows = await db.Modules.Where(x => x.Id == id).ExecuteDeleteAsync();
        if (rows == 0) return NotFound();

        return NoContent();
    }

    
    
    
    
    
    
    [HttpGet("{moduleId:int}/topics")]
    public async Task<ActionResult<List<ModuleTopicViewDto>>> GetTopics(int moduleId)
    {
        var module = await db.Modules.AsNoTracking().FirstOrDefaultAsync(m => m.Id == moduleId);
        if (module is null) return NotFound();

        

        var topics = await db.ModuleTopics
            .Where(t => t.ModuleId == moduleId)
            .Include(t => t.LessonType)
            .OrderBy(t => t.BlockNumber)
            .ThenBy(t => t.Order)
            .ThenBy(t => t.LessonNumber)
            .ThenBy(t => t.QuestionNumber)
            .ToListAsync();

        var topicIds = topics.Select(t => t.Id).ToList();
        var plannedDict = new Dictionary<int, List<string>>();
        var completedDict = new Dictionary<int, List<string>>();

        if (topicIds.Count > 0)
        {
            plannedDict = await db.TeacherDraftItems
                .Where(di => di.ModuleTopicId != null && topicIds.Contains(di.ModuleTopicId!.Value) && di.Status == DraftStatus.Draft)
                .Select(di => new { TopicId = di.ModuleTopicId!.Value, GroupName = di.Group.Name })
                .Distinct()
                .GroupBy(x => x.TopicId)
                .ToDictionaryAsync(g => g.Key, g => g.Select(x => x.GroupName).OrderBy(x => x).ToList());

            completedDict = await db.ScheduleItems
                .Where(si => si.ModuleTopicId != null && topicIds.Contains(si.ModuleTopicId!.Value))
                .Select(si => new { TopicId = si.ModuleTopicId!.Value, GroupName = si.Group.Name })
                .Distinct()
                .GroupBy(x => x.TopicId)
                .ToDictionaryAsync(g => g.Key, g => g.Select(x => x.GroupName).OrderBy(x => x).ToList());
        }

        static string BuildTopicCode(string? moduleCode, ModuleTopic topic)
        {
            var segments = new List<string>();
            if (!string.IsNullOrWhiteSpace(moduleCode))
                segments.Add(moduleCode.Trim());
            if (topic.BlockNumber > 0)
                segments.Add(topic.BlockNumber.ToString(CultureInfo.InvariantCulture));
            var themePart = topic.LessonNumber > 0
                ? topic.LessonNumber.ToString(CultureInfo.InvariantCulture)
                : topic.Order.ToString(CultureInfo.InvariantCulture);
            segments.Add(themePart);
            var questionPart = topic.QuestionNumber > 0
                ? topic.QuestionNumber.ToString(CultureInfo.InvariantCulture)
                : "1";
            segments.Add(questionPart);
            return string.Join(".", segments);
        }

        var result = topics.Select(t =>
        {
            var planned = plannedDict.TryGetValue(t.Id, out var pg) ? new List<string>(pg) : new List<string>();
            var completed = completedDict.TryGetValue(t.Id, out var cg) ? new List<string>(cg) : new List<string>();
            var lessonTypeCode = t.LessonType?.Code ?? string.Empty;
            var lessonTypeName = t.LessonType?.Name ?? string.Empty;
            return new ModuleTopicViewDto(
                t.Id,
                t.ModuleId,
                t.Order,
                t.BlockNumber,
                t.BlockTitle,
                t.LessonNumber,
                t.QuestionNumber,
                BuildTopicCode(module.Code, t),
                t.LessonTypeId,
                lessonTypeCode,
                lessonTypeName,
                t.TotalHours,
                t.AuditoriumHours,
                t.SelfStudyHours,
                t.Title,
                planned,
                completed
            );
        }).ToList();

        return Ok(result);
    }

    [HttpPost("{moduleId:int}/topics/upsert")]
    public async Task<ActionResult<int>> UpsertTopic(int moduleId, [FromBody] ModuleTopicDto dto)
    {
        var module = await db.Modules.FirstOrDefaultAsync(m => m.Id == moduleId);
        if (module is null) return NotFound();

        

        var lessonTypeExists = await db.LessonTypes.AnyAsync(lt => lt.Id == dto.LessonTypeId);
        if (!lessonTypeExists) return BadRequest("Lesson type not found");

        var topicsQuery = db.ModuleTopics.Where(t => t.ModuleId == moduleId);
        var trimmedBlockTitle = dto.BlockTitle?.Trim() ?? string.Empty;
        var trimmedTitle = dto.Title?.Trim() ?? string.Empty;
        var requestedBlockNumber = dto.BlockNumber > 0
            ? dto.BlockNumber
            : (await topicsQuery.MaxAsync(t => (int?)t.BlockNumber) ?? 0) + 1;

        async Task<int> NextLessonNumberAsync(int blockNumber)
        {
            return (await topicsQuery
                .Where(t => t.BlockNumber == blockNumber)
                .MaxAsync(t => (int?)t.LessonNumber) ?? 0) + 1;
        }

        async Task<int> NextQuestionNumberAsync(int blockNumber, int lessonNumber, int? excludeId = null)
        {
            return (await topicsQuery
                    .Where(t => t.BlockNumber == blockNumber
                                && t.LessonNumber == lessonNumber
                                && (!excludeId.HasValue || t.Id != excludeId.Value))
                    .MaxAsync(t => (int?)t.QuestionNumber) ?? 0) + 1;
        }

        ModuleTopic entity;
        if (dto.Id is int topicId && topicId > 0)
        {
            entity = await topicsQuery.FirstOrDefaultAsync(t => t.Id == topicId)
                ?? throw new ArgumentException("Topic not found");

            var blockChanged = entity.BlockNumber != requestedBlockNumber;
            entity.BlockNumber = requestedBlockNumber;

            if (!string.IsNullOrWhiteSpace(trimmedBlockTitle))
            {
                entity.BlockTitle = trimmedBlockTitle;
            }
            else if (blockChanged || string.IsNullOrWhiteSpace(entity.BlockTitle))
            {
                entity.BlockTitle = await topicsQuery
                    .Where(t => t.BlockNumber == entity.BlockNumber
                                && t.Id != entity.Id
                                && t.BlockTitle != null
                                && t.BlockTitle != "")
                    .Select(t => t.BlockTitle)
                    .FirstOrDefaultAsync() ?? string.Empty;
            }

            var previousLessonNumber = entity.LessonNumber;
            var lessonNumber = dto.LessonNumber > 0
                ? dto.LessonNumber
                : (blockChanged || previousLessonNumber <= 0
                    ? await NextLessonNumberAsync(entity.BlockNumber)
                    : previousLessonNumber);
            entity.LessonNumber = lessonNumber;

            var questionNumber = dto.QuestionNumber > 0
                ? dto.QuestionNumber
                : (blockChanged || entity.QuestionNumber <= 0 || lessonNumber != previousLessonNumber
                    ? await NextQuestionNumberAsync(entity.BlockNumber, lessonNumber, entity.Id)
                    : entity.QuestionNumber);
            entity.QuestionNumber = questionNumber;
        }
        else
        {
            var nextOrder = await topicsQuery.MaxAsync(t => (int?)t.Order) ?? 0;
            var blockTitle = !string.IsNullOrWhiteSpace(trimmedBlockTitle)
                ? trimmedBlockTitle
                : await topicsQuery
                    .Where(t => t.BlockNumber == requestedBlockNumber
                                && t.BlockTitle != null
                                && t.BlockTitle != "")
                    .Select(t => t.BlockTitle)
                    .FirstOrDefaultAsync() ?? string.Empty;
            var lessonNumber = dto.LessonNumber > 0
                ? dto.LessonNumber
                : await NextLessonNumberAsync(requestedBlockNumber);
            var questionNumber = dto.QuestionNumber > 0
                ? dto.QuestionNumber
                : await NextQuestionNumberAsync(requestedBlockNumber, lessonNumber);

            entity = new ModuleTopic
            {
                ModuleId = moduleId,
                Order = dto.Order > 0 ? dto.Order : nextOrder + 1,
                BlockNumber = requestedBlockNumber,
                BlockTitle = blockTitle,
                LessonNumber = lessonNumber,
                QuestionNumber = questionNumber
            };
            db.ModuleTopics.Add(entity);
        }

        entity.Title = trimmedTitle;
        entity.LessonTypeId = dto.LessonTypeId;
        var safeAuditorium = Math.Max(0, dto.AuditoriumHours);
        var safeSelfStudy = Math.Max(0, dto.SelfStudyHours);
        var totalHours = Math.Max(0, safeAuditorium + safeSelfStudy);
        entity.TotalHours = totalHours;
        entity.AuditoriumHours = safeAuditorium;
        entity.SelfStudyHours = safeSelfStudy;
        if (dto.Order > 0 && entity.Order != dto.Order)
            entity.Order = dto.Order;
        if (dto.LessonNumber > 0 && entity.LessonNumber != dto.LessonNumber)
            entity.LessonNumber = dto.LessonNumber;
        if (dto.QuestionNumber > 0 && entity.QuestionNumber != dto.QuestionNumber)
            entity.QuestionNumber = dto.QuestionNumber;

        if (entity.AuditoriumHours + entity.SelfStudyHours > entity.TotalHours)
            return BadRequest("Hourly totals exceed overall value");

        await db.SaveChangesAsync();
        
        return Ok(entity.Id);
    }

    [HttpPost("{moduleId:int}/topics/reorder")]
    public async Task<IActionResult> ReorderTopics(int moduleId, [FromBody] List<int> orderedIds)
    {
        if (orderedIds is null || orderedIds.Count == 0) return BadRequest("Empty order");

        var topics = await db.ModuleTopics
            .Where(t => t.ModuleId == moduleId)
            .OrderBy(t => t.Order)
            .ToListAsync();

        if (topics.Count != orderedIds.Count || topics.Any(t => !orderedIds.Contains(t.Id)))
            return BadRequest("Order does not match existing topics");

        for (var i = 0; i < orderedIds.Count; i++)
        {
            var topicId = orderedIds[i];
            var topic = topics.First(t => t.Id == topicId);
            topic.Order = i + 1;
        }

        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{moduleId:int}/topics/{topicId:int}")]
    public async Task<IActionResult> DeleteTopic(int moduleId, int topicId)
    {
        var topic = await db.ModuleTopics.FirstOrDefaultAsync(t => t.Id == topicId && t.ModuleId == moduleId);
        if (topic is null) return NotFound();
        

        var hasDrafts = await db.TeacherDraftItems.AnyAsync(di => di.ModuleTopicId == topicId);
        var hasSchedule = await db.ScheduleItems.AnyAsync(si => si.ModuleTopicId == topicId);
        if (hasDrafts || hasSchedule)
            return Conflict("Topic already used in schedule");

        db.ModuleTopics.Remove(topic);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private async Task CloneIntoAdditionalCoursesAsync(Module sourceModule, ModuleEditDto dto)
    {
        
        if (dto.CloneCourseIds is null || dto.CloneCourseIds.Count == 0) return;

        var requestedIds = dto.CloneCourseIds
            .Where(id => id > 0 && id != sourceModule.CourseId)
            .Distinct()
            .ToList();
        if (requestedIds.Count == 0) return;

        var existingCourseIds = await db.Modules
            .Where(mod => requestedIds.Contains(mod.CourseId)
                          && mod.Code == sourceModule.Code)
            .Select(mod => mod.CourseId)
            .Distinct()
            .ToListAsync();

        var targetCourseIds = await db.Courses
            .Where(c => requestedIds.Contains(c.Id) && !existingCourseIds.Contains(c.Id))
            .Select(c => c.Id)
            .ToListAsync();

        if (targetCourseIds.Count == 0) return;

        var clones = new List<Module>();
        foreach (var courseId in targetCourseIds)
        {
            clones.Add(new Module
            {
                Code = sourceModule.Code,
                Title = sourceModule.Title,
                CourseId = courseId,
                Credits = sourceModule.Credits,
                Competences = sourceModule.Competences,
                LearningOutcomes = sourceModule.LearningOutcomes,
                ReportingForm = sourceModule.ReportingForm
            });
        }

        db.Modules.AddRange(clones);
        await db.SaveChangesAsync();

        var roomIds = dto.AllowedRoomIds?.Distinct().Where(id => id > 0).ToList() ?? new();
        var buildingIds = dto.AllowedBuildingIds?.Distinct().Where(id => id > 0).ToList() ?? new();

        if (roomIds.Count == 0 && buildingIds.Count == 0) return;

        foreach (var clone in clones)
        {
            foreach (var rid in roomIds)
                db.ModuleRooms.Add(new ModuleRoom { ModuleId = clone.Id, RoomId = rid });
            foreach (var bid in buildingIds)
                db.ModuleBuildings.Add(new ModuleBuilding { ModuleId = clone.Id, BuildingId = bid });
        }

        await db.SaveChangesAsync();
    }
}
