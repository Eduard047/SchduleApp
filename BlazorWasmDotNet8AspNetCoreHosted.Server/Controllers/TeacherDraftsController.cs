using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using BlazorWasmDotNet8AspNetCoreHosted.Server.Application;
using BlazorWasmDotNet8AspNetCoreHosted.Server.Domain.Entities;
using BlazorWasmDotNet8AspNetCoreHosted.Server.Infrastructure;
using BlazorWasmDotNet8AspNetCoreHosted.Shared.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlazorWasmDotNet8AspNetCoreHosted.Server.Controllers;

[ApiController]
[Route("api/teacher-drafts")]
// Контролер для керування чернетками викладачів
public sealed class TeacherDraftsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly RulesService _rules;
    private static readonly JsonSerializerOptions ValidationJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public TeacherDraftsController(AppDbContext db, RulesService rules)
    {
        _db = db;
        _rules = rules;
    }

    public sealed record DraftAutoGenRequest(
        DateOnly WeekStart,
        bool ClearExisting = true,
        int? CourseId = null,
        int? GroupId = null,
        int? TeacherId = null,
        bool AllowOnDaysOff = false,
        WeekPreset Days = WeekPreset.MonFri
    );

    public sealed record ApproveWeekRequest(DateOnly WeekStart, int TeacherId);
    public sealed record PublishWeekRequest(DateOnly WeekStart, int? TeacherId);
    public sealed record PublishWeekResults(int Created, int Skipped, List<string> Warnings);

    private sealed record BusySlot(
        int GroupId,
        int? TeacherId,
        int? RoomId,
        DateOnly Date,
        TimeOnly StartTime,
        TimeOnly EndTime,
        int? BuildingId,
        int ModuleId,
        int LessonTypeId
    );


    [HttpGet]
    /// <summary>
    /// Повертає перелік чернеток викладачів за тиждень із додатковою інформацією.
    /// </summary>
    public async Task<IReadOnlyList<TeacherDraftItemDto>> Get(
        [FromQuery] DateOnly weekStart,
        [FromQuery] int? teacherId,
        [FromQuery] int? groupId,
        [FromQuery] int? roomId)
    {
        var weekEnd = weekStart.AddDays(7);

        var q = _db.TeacherDraftItems
            .Include(x => x.Group).ThenInclude(g => g.Course)
            .Include(x => x.Module)
            .Include(x => x.ModuleTopic)
            .Include(x => x.Teacher)
            .Include(x => x.Room).ThenInclude(r => r!.Building)
            .Include(x => x.LessonType)
            .Where(x => x.Date >= weekStart && x.Date < weekEnd)
            .AsQueryable();

        if (teacherId is int tid) q = q.Where(x => x.TeacherId == tid);
        if (groupId is int gid) q = q.Where(x => x.GroupId == gid);
        if (roomId is int rid) q = q.Where(x => x.RoomId == rid);

        var items = await q.OrderBy(x => x.Date).ThenBy(x => x.StartTime).ToListAsync();

        static string ResolveTeacherGroupKey(TeacherDraftItem item)
        {
            if (!string.IsNullOrWhiteSpace(item.BatchKey))
            {
                return $"batch:{item.BatchKey}";
            }

            var roomPart = item.RoomId.HasValue ? item.RoomId.Value.ToString() : "none";
            return $"slot:{item.Date:yyyyMMdd}|{item.StartTime:HHmm}|{item.EndTime:HHmm}|m{item.ModuleId}|lt{item.LessonTypeId}|r{roomPart}";
        }

        var teacherGroups = items
            .GroupBy(ResolveTeacherGroupKey)
            .ToDictionary(
                g => g.Key,
                g => g.Select(t => t.Teacher?.FullName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => name!)
                    .Distinct(StringComparer.CurrentCultureIgnoreCase)
                    .ToList()
            );

        return items.Select(i =>
        {
        var rescheduleInfo = ParseRescheduleBatchKey(i.BatchKey);
        var groupKey = ResolveTeacherGroupKey(i);
        var teacherNames = teacherGroups.TryGetValue(groupKey, out var groupedNames)
            ? groupedNames
            : new List<string>();

            if (teacherNames.Count == 0 && !string.IsNullOrWhiteSpace(i.Teacher?.FullName))
            {
                teacherNames = new List<string> { i.Teacher!.FullName };
            }

        var teacherLabel = teacherNames.Count > 0 ? string.Join(", ", teacherNames) : (i.Teacher?.FullName ?? "");
        var topicCode = BuildModuleTopicCode(i.ModuleTopic);

            var requiresRoom = i.LessonType.RequiresRoom;

            return new TeacherDraftItemDto(
            Id: i.Id,
            Date: i.Date,
            TimeStart: i.StartTime.ToString("HH:mm"),
            TimeEnd: i.EndTime.ToString("HH:mm"),
            DayNumber: (int)i.DayOfWeek,
            Group: i.Group.Name,
            GroupId: i.GroupId,
            Module: i.Module.Title,
            ModuleId: i.ModuleId,
            TopicCode: topicCode,
        ModuleTopicId: i.ModuleTopicId,
            Teacher: teacherLabel,
            TeacherId: i.TeacherId,
            Room: requiresRoom && i.Room is not null ? i.Room.Name : "",
            RoomId: requiresRoom ? i.RoomId : null,
            RequiresRoom: requiresRoom,
            LessonTypeId: i.LessonTypeId,
            LessonTypeCode: i.LessonType.Code,
            LessonTypeName: i.LessonType.Name,
            Status: (DraftStatusDto)(int)i.Status,
            PublishedItemId: i.PublishedItemId,
            Warnings: i.ValidationWarnings,
            IsLocked: i.IsLocked,
            IsRescheduled: rescheduleInfo.isRescheduled,
            RescheduledFromLessonTypeId: rescheduleInfo.originalLessonTypeId,
            BatchKey: i.BatchKey,
            TeacherNames: teacherNames,
            LessonTypeCss: i.LessonType.CssKey
        );
        }).ToList();
    }

    [HttpGet("week")]
    /// <summary>
    /// Додає коротку кінцеву точку, що делегує основному методу отримання даних.
    /// </summary>
    public Task<IReadOnlyList<TeacherDraftItemDto>> GetWeekAlias(
        [FromQuery] DateOnly weekStart,
        [FromQuery] int? teacherId,
        [FromQuery] int? groupId,
        [FromQuery] int? roomId)
        => Get(weekStart, teacherId, groupId, roomId);

    [HttpDelete("{id:int}")]
    /// <summary>
    /// Видаляє чернетку, якщо запис існує та не заблокований.
    /// </summary>
    public async Task<IActionResult> Delete(int id)
    {
        var item = await _db.TeacherDraftItems.FirstOrDefaultAsync(x => x.Id == id);
        if (item is null) return NotFound(new { message = $"TeacherDraftItem {id} not found" });
        if (item.IsLocked) return Conflict(new { message = "Чернетка заблокована. Розблокуйте її, щоб видалити." });

        _db.TeacherDraftItems.Remove(item);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static (bool isRescheduled, int? originalLessonTypeId) ParseRescheduleBatchKey(string? batchKey)
    {
        if (string.IsNullOrWhiteSpace(batchKey)) return (false, null);
        if (!batchKey.StartsWith("rescheduled", StringComparison.OrdinalIgnoreCase)) return (false, null);

        var parts = batchKey.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 3 && int.TryParse(parts[2], out var ltId))
        {
            return (true, ltId);
        }

        return (true, null);
    }
    /// <summary>
    /// Нормалізує код теми модуля перед збереженням.
    /// </summary>
    private static string? BuildModuleTopicCode(ModuleTopic? topic)
    {
        if (topic is null) return null;
        return string.IsNullOrWhiteSpace(topic.TopicCode) ? null : topic.TopicCode.Trim();
    }


    [HttpPost("upsert")]
    /// <summary>
    /// Валідує й створює або оновлює чернетку викладача, повертає її ідентифікатор.
    /// </summary>
    public async Task<ActionResult<int>> Upsert([FromBody] DraftUpsertRequest r)
    {
        var validation = await _rules.ValidateDraftAsync(r);
        if (validation.Errors.Count > 0 && !r.IgnoreValidationErrors)
            return Conflict(new
            {
                message = "Validation failed",
                errors = validation.Errors,
                warnings = validation.Warnings,
                details = validation.Report
            });

        var reportJson = validation.Report.Issues.Count > 0
            ? JsonSerializer.Serialize(validation.Report, ValidationJsonOptions)
            : null;

        var lessonTypeRequiresRoom = await _db.LessonTypes
            .Where(x => x.Id == r.LessonTypeId)
            .Select(x => (bool?)x.RequiresRoom)
            .FirstOrDefaultAsync();
        var normalizedRoomId = lessonTypeRequiresRoom is false ? null : r.RoomId;

        var start = TimeOnly.Parse(r.TimeStart);
        var end = TimeOnly.Parse(r.TimeEnd);

        if (r.Id is int id && id > 0)
        {
            var item = await _db.TeacherDraftItems.FirstOrDefaultAsync(x => x.Id == id);
            if (item is null) return NotFound(new { message = $"TeacherDraftItem {id} not found" });

            item.Date = r.Date;
            item.DayOfWeek = r.Date.ToDateTime(TimeOnly.MinValue).DayOfWeek;
            item.StartTime = start;
            item.EndTime = end;
            item.GroupId = r.GroupId;
            item.ModuleId = r.ModuleId;
            item.ModuleTopicId = r.ModuleTopicId;
            item.TeacherId = r.TeacherId;
            item.RoomId = normalizedRoomId;
            item.LessonTypeId = r.LessonTypeId;
            item.IsLocked = r.IsLocked;
            item.ValidationWarnings = reportJson;
            item.Status = DraftStatus.Draft;

            await _db.SaveChangesAsync();
            return Ok(item.Id);
        }
        else
        {
            var item = new TeacherDraftItem
            {
                Date = r.Date,
                DayOfWeek = r.Date.ToDateTime(TimeOnly.MinValue).DayOfWeek,
                StartTime = start,
                EndTime = end,
                GroupId = r.GroupId,
                ModuleId = r.ModuleId,
                ModuleTopicId = r.ModuleTopicId,
                TeacherId = r.TeacherId,
                RoomId = normalizedRoomId,
                LessonTypeId = r.LessonTypeId,
                Status = DraftStatus.Draft,
                IsLocked = r.IsLocked,
                ValidationWarnings = reportJson
            };
            _db.TeacherDraftItems.Add(item);
            await _db.SaveChangesAsync();
            return Ok(item.Id);
        }
    }

    [HttpPost("clear-week")]
    /// <summary>
    /// Очищає незаблоковані чернетки за вказаний тиждень із можливими додатковими фільтрами.
    /// </summary>
    public async Task<ActionResult<ClearWeekResult>> ClearWeek([FromBody] ClearWeekRequest r)
    {
        var start = r.WeekStart;
        var end = start.AddDays(7);

        var q = _db.TeacherDraftItems.Where(x => x.Date >= start && x.Date < end && !x.IsLocked);
        if (r.CourseId is int cid) q = q.Where(x => x.Group.CourseId == cid);
        if (r.GroupId is int gid) q = q.Where(x => x.GroupId == gid);

        var deleted = await q.ExecuteDeleteAsync();
        return Ok(new ClearWeekResult(deleted));
    }


    [HttpPost("autogen/week")]
    /// <summary>
    /// Викликає автогенерацію чернеток для одного тижня.
    /// </summary>
    public Task<ActionResult<AutoGenResult>> DraftAutoGenWeek([FromBody] DraftAutoGenRequest r)
        => DraftAutoGen(r);

    [HttpPost("autogen/month")]
    /// <summary>
    /// Автоматично генерує чернетки для кожного тижня в межах місяця.
    /// </summary>
    public async Task<ActionResult<AutoGenResult>> AutogenMonth([FromBody] AutogenMonthRequest r)
    {
        var monthStart = r.MonthStart;
        var nextMonth = new DateOnly(monthStart.Year, monthStart.Month, 1).AddMonths(1);
        var monday = ToMonday(monthStart);
        int created = 0, skipped = 0;
        var warnings = new List<string>();
        var gapDetails = new List<AutoGenGapDetail>();

        while (monday < nextMonth)
        {
            var res = await DraftAutoGen(new DraftAutoGenRequest(
                WeekStart: monday,
                ClearExisting: true,
                CourseId: r.CourseId,
                GroupId: r.GroupId,
                TeacherId: r.TeacherId,
                AllowOnDaysOff: r.AllowOnDaysOff,
                Days: r.Days
            ));
            var ok = (res.Result as OkObjectResult)?.Value as AutoGenResult;
            if (ok is not null)
            {
                created += ok.Created;
                skipped += ok.Skipped;
                warnings.AddRange(ok.Warnings);
                if (ok.GapDetails is not null)
                {
                    gapDetails.AddRange(ok.GapDetails);
                }
            }

            monday = monday.AddDays(7);
        }

        return Ok(new AutoGenResult(created, skipped, warnings, gapDetails));
    }

    [HttpPost("autogen/course")]
    /// <summary>
    /// Генерує чернетки для курсу в заданому діапазоні тижнів.
    /// </summary>
    public async Task<ActionResult<AutoGenResult>> AutogenCourse([FromBody] AutogenCourseRequest r)
    {
        var monday = ToMonday(r.From);
        var to = r.To;

        int created = 0, skipped = 0;
        var warnings = new List<string>();
        var gapDetails = new List<AutoGenGapDetail>();

        while (monday <= to)
        {
            var res = await DraftAutoGen(new DraftAutoGenRequest(
                WeekStart: monday,
                ClearExisting: true,
                CourseId: r.CourseId,
                GroupId: r.GroupId,
                TeacherId: r.TeacherId,
                AllowOnDaysOff: r.AllowOnDaysOff,
                Days: r.Days
            ));
            var ok = (res.Result as OkObjectResult)?.Value as AutoGenResult;
            if (ok is not null)
            {
                created += ok.Created;
                skipped += ok.Skipped;
                warnings.AddRange(ok.Warnings);
                if (ok.GapDetails is not null)
                {
                    gapDetails.AddRange(ok.GapDetails);
                }
            }

            monday = monday.AddDays(7);
        }

        return Ok(new AutoGenResult(created, skipped, warnings, gapDetails));
    }
    /// <summary>
    /// Повертає понеділок тижня, до якого належить передана дата.
    /// </summary>
    private static DateOnly ToMonday(DateOnly d)
        => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    [HttpPost("autogen")]
    /// <summary>
    /// Створює чернетки на основі правил і доступних даних для заданого тижня.
    /// </summary>
    public async Task<ActionResult<AutoGenResult>> DraftAutoGen([FromBody] DraftAutoGenRequest r)
    {
        var types = await _db.LessonTypes.AsNoTracking().ToListAsync();
        if (types.Count == 0)
            return BadRequest(new AutoGenResult(0, 0, new() { "Типи занять відсутні або вимкнені." }));
        var typeBreakId = types.FirstOrDefault(t => t.Code.ToUpper() == "BREAK" && t.IsActive)?.Id;
        var typeCanceledId = types.FirstOrDefault(t => t.Code.ToUpper() == "CANCELED")?.Id;
        var excludedTypeIds = new HashSet<int>(new[] { typeBreakId, typeCanceledId }.Where(x => x != null)!.Select(x => x!.Value));
        var typeById = types.ToDictionary(t => t.Id);

        var activeStudyTypes = types.Where(t => t.IsActive && t.CountInPlan).OrderBy(t => t.Id).ToList();
        var preferredFirstTypeId = types.FirstOrDefault(t => t.PreferredFirstInWeek)?.Id ?? 0;

        int ltIndex = 0;
        int NextCyclicLessonTypeId() =>
            activeStudyTypes.Count > 0 ? activeStudyTypes[ltIndex++ % activeStudyTypes.Count].Id : preferredFirstTypeId;

        var weekStart = r.WeekStart;
        var weekEnd = weekStart.AddDays(7);

        bool DayAllowed(DayOfWeek dow)
        {
            var day = dow == DayOfWeek.Sunday ? 7 : (int)dow;
            return r.Days switch
            {
                WeekPreset.MonSun => day is >= 1 and <= 7,
                WeekPreset.MonSat => day is >= 1 and <= 6,
                _ => day is >= 1 and <= 5
            };
        }

        var weekExceptions = await _db.CalendarExceptions
            .Where(c => c.Date >= weekStart && c.Date < weekEnd)
            .ToDictionaryAsync(c => c.Date, c => c.IsWorkingDay);

        bool IsWorking(DateOnly d)
        {
            if (!DayAllowed(d.ToDateTime(TimeOnly.MinValue).DayOfWeek)) return false;
            if (r.AllowOnDaysOff) return true;
            if (weekExceptions.TryGetValue(d, out var isWork)) return isWork;
            var dow = d.ToDateTime(TimeOnly.MinValue).DayOfWeek;
            return dow != DayOfWeek.Saturday && dow != DayOfWeek.Sunday;
        }

        int? courseId = (r.CourseId > 0) ? r.CourseId : null;
        int? groupId = (r.GroupId > 0) ? r.GroupId : null;

        var groups = await _db.Groups
            .Include(x => x.Course)
            .Where(x => courseId == null || x.CourseId == courseId)
            .Where(x => groupId == null || x.Id == groupId)
            .ToListAsync();

        if (groups.Count == 0)
            return Ok(new AutoGenResult(0, 0, new() { "Групи не знайдено." }));

        if (r.ClearExisting)
        {
            var gids = groups.Select(g => g.Id).ToList();
            await _db.TeacherDraftItems
                .Where(x => x.Date >= weekStart && x.Date < weekEnd && gids.Contains(x.GroupId) && !x.IsLocked)
                .ExecuteDeleteAsync();
        }

        var lunchesAll = await _db.LunchConfigs.AsNoTracking().ToListAsync();
        var roomsAll = await _db.Rooms.AsNoTracking().ToListAsync();

        var moduleIdsAll = await _db.Modules.Select(m => m.Id).ToListAsync();

        var allowedRoomsByModule = await _db.ModuleRooms
            .Where(mr => moduleIdsAll.Contains(mr.ModuleId))
            .GroupBy(mr => mr.ModuleId)
            .ToDictionaryAsync(g => g.Key, g => g.Select(x => x.RoomId).ToHashSet());

        var allowedBuildingsByModule = await _db.ModuleBuildings
            .Where(mb => moduleIdsAll.Contains(mb.ModuleId))
            .GroupBy(mb => mb.ModuleId)
            .ToDictionaryAsync(g => g.Key, g => g.Select(x => x.BuildingId).ToHashSet());

        var busy = await _db.TeacherDraftItems
            .Include(x => x.Room)
            .Where(x => x.Date >= weekStart && x.Date < weekEnd)
            .Select(x => new BusySlot(
                x.GroupId,
                x.TeacherId,
                x.RoomId,
                x.Date,
                x.StartTime,
                x.EndTime,
                x.Room != null ? (int?)x.Room.BuildingId : null,
                x.ModuleId,
                x.LessonTypeId))
            .ToListAsync();

        var hasPreferred = new HashSet<(int groupId, int moduleId)>(
            busy.Where(b => preferredFirstTypeId != 0 && b.LessonTypeId == preferredFirstTypeId)
                .Select(b => (b.GroupId, b.ModuleId)));

        var perDayCount = new Dictionary<(int groupId, DateOnly date), int>();
        foreach (var b in busy.Where(b => !excludedTypeIds.Contains(b.LessonTypeId)))
        {
            var key = (b.GroupId, b.Date);
            perDayCount[key] = perDayCount.TryGetValue(key, out var existing) ? existing + 1 : 1;
        }
        int CountFor(int gid, DateOnly date) => perDayCount.TryGetValue((gid, date), out var c) ? c : 0;
        int CountModuleForDay(int gid, DateOnly date, int moduleId) =>
            busy.Count(x => x.GroupId == gid
                            && x.Date == date
                            && x.ModuleId == moduleId
                            && !excludedTypeIds.Contains(x.LessonTypeId));
        void Inc(int gid, DateOnly date)
        {
            var key = (gid, date);
            perDayCount[key] = CountFor(gid, date) + 1;
        }

        bool HadSameModulePreviousDay(int gid, int mid, DateOnly date)
        {
            var prev = date.AddDays(-1);
            return busy.Any(x => x.GroupId == gid
                                 && x.ModuleId == mid
                                 && x.Date == prev
                                 && !excludedTypeIds.Contains(x.LessonTypeId));
        }

        var teachersForModule = await _db.TeacherModules.AsNoTracking().ToListAsync();

        var teacherNames = await _db.Teachers
            .AsNoTracking()
            .ToDictionaryAsync(
                t => t.Id,
                t => string.IsNullOrWhiteSpace(t.FullName) ? $"#{t.Id}" : t.FullName!
            );

        var teacherWorkingHours = await _db.TeacherWorkingHours.AsNoTracking()
            .Select(w => new { w.TeacherId, w.DayOfWeek, w.Start, w.End })
            .ToListAsync();

        var workingHoursByTeacher = teacherWorkingHours
            .GroupBy(w => w.TeacherId)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(x => x.DayOfWeek)
                    .ToDictionary(
                        d => d.Key,
                        d => d.Select(x => (x.Start, x.End)).ToList()
                    )
            );

        bool TeacherFitsWorkingHours(int teacherId, DateOnly date, TimeOnly start, TimeOnly end)
        {
            if (!workingHoursByTeacher.TryGetValue(teacherId, out var dayMap) || dayMap.Count == 0)
                return true;

            var dow = date.ToDateTime(TimeOnly.MinValue).DayOfWeek;
            if (!dayMap.TryGetValue(dow, out var windows) || windows.Count == 0)
                return false;

            return windows.Any(w => w.Start <= start && end <= w.End);
        }

        var courseIds = groups.Select(g => g.CourseId).Distinct().ToList();
        var activePlans = await _db.ModulePlans.Where(p => courseIds.Contains(p.CourseId) && p.IsActive).ToListAsync();

        var moduleIdsForPlans = activePlans.Select(p => p.ModuleId).Distinct().ToList();
        var topicsRaw = await _db.ModuleTopics
            .Where(t => moduleIdsForPlans.Contains(t.ModuleId))
            .OrderBy(t => t.Order)
            .ThenBy(t => t.TopicCode)
            .ToListAsync();
        var topicsByModule = topicsRaw
            .GroupBy(t => t.ModuleId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var topicUsageLimitById = topicsRaw
            .ToDictionary(t => t.Id, t => Math.Max(0, t.AuditoriumHours));
        var moduleAuditoriumHours = topicsByModule
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Sum(t => Math.Max(0, t.AuditoriumHours)));
        var topicAssignmentsDraft = await _db.TeacherDraftItems
            .Where(di => di.ModuleTopicId != null && courseIds.Contains(di.Group.CourseId))
            .Select(di => new { di.GroupId, di.ModuleId, TopicId = di.ModuleTopicId!.Value })
            .ToListAsync();
        var topicAssignmentsSchedule = await _db.ScheduleItems
            .Where(si => si.ModuleTopicId != null && courseIds.Contains(si.Group.CourseId))
            .Select(si => new { si.GroupId, si.ModuleId, TopicId = si.ModuleTopicId!.Value })
            .ToListAsync();
        var topicAssignments = new Dictionary<(int GroupId, int ModuleId), Dictionary<int, int>>();

        void SeedTopicAssignment(int groupId, int moduleId, int topicId)
        {
            var key = (groupId, moduleId);
            if (!topicAssignments.TryGetValue(key, out var assigned))
            {
                assigned = new Dictionary<int, int>();
                topicAssignments[key] = assigned;
            }

            assigned.TryGetValue(topicId, out var usedCount);
            assigned[topicId] = usedCount + 1;
        }

        foreach (var entry in topicAssignmentsDraft.Concat(topicAssignmentsSchedule))
        {
            SeedTopicAssignment(entry.GroupId, entry.ModuleId, entry.TopicId);
        }
        var topicCursor = new Dictionary<(int GroupId, int ModuleId), int>();
        var topicsExhaustedNotified = new HashSet<(int GroupId, int ModuleId)>();
        var missingModulesNotified = new HashSet<int>();

        int GetTopicUsageLimit(ModuleTopic topic)
            => topicUsageLimitById.TryGetValue(topic.Id, out var limit) ? limit : Math.Max(0, topic.AuditoriumHours);

        void NormalizeTopicCursor(int groupId, int moduleId)
        {
            var key = (groupId, moduleId);
            if (!topicsByModule.TryGetValue(moduleId, out var list) || list.Count == 0)
            {
                topicCursor[key] = 0;
                return;
            }

            var cursor = topicCursor.TryGetValue(key, out var index) ? index : 0;
            if (cursor < 0) cursor = 0;

            topicAssignments.TryGetValue(key, out var assigned);

            while (cursor < list.Count)
            {
                var topic = list[cursor];
                var limit = GetTopicUsageLimit(topic);
                if (limit <= 0)
                {
                    cursor++;
                    continue;
                }

                var usedCount = assigned != null && assigned.TryGetValue(topic.Id, out var count) ? count : 0;
                if (usedCount < limit)
                {
                    break;
                }

                cursor++;
            }

            topicCursor[key] = cursor;
        }
        int created = 0, skipped = 0;
        var warnings = new List<string>();
        var gapDetails = new List<AutoGenGapDetail>();
        var gapWarnings = new HashSet<(int GroupId, DateOnly Date, TimeOnly Start, TimeOnly End)>();
        var slotFailureReasons = new Dictionary<(int GroupId, DateOnly Date, TimeOnly Start, TimeOnly End), HashSet<string>>();


        bool TypeAllowed(int lessonTypeId)
        {
            return typeById.TryGetValue(lessonTypeId, out var lt)
                   && lt.IsActive
                   && lt.CountInPlan
                   && !excludedTypeIds.Contains(lessonTypeId);
        }

        ModuleTopic? PeekNextTopic(int groupId, int moduleId)
        {
            if (!topicsByModule.TryGetValue(moduleId, out var list) || list.Count == 0)
                return null;

            var key = (groupId, moduleId);
            NormalizeTopicCursor(groupId, moduleId);
            var cursor = topicCursor.TryGetValue(key, out var index) ? index : 0;
            if (cursor >= list.Count) return null;
            return list[cursor];
        }

        void MarkTopicUsed(int groupId, int moduleId, ModuleTopic topic)
        {
            var key = (groupId, moduleId);
            if (!topicAssignments.TryGetValue(key, out var assigned))
            {
                assigned = new Dictionary<int, int>();
                topicAssignments[key] = assigned;
            }

            assigned.TryGetValue(topic.Id, out var used);
            assigned[topic.Id] = used + 1;

            NormalizeTopicCursor(groupId, moduleId);
        }

        bool TopicsDepleted(int groupIdCheck, int moduleIdCheck)
        {
            if (!topicsByModule.TryGetValue(moduleIdCheck, out var list) || list.Count == 0)
                return false;

            var key = (groupIdCheck, moduleIdCheck);
            NormalizeTopicCursor(groupIdCheck, moduleIdCheck);
            var cursor = topicCursor.TryGetValue(key, out var index) ? index : 0;
            if (cursor < list.Count)
                return false;

            topicAssignments.TryGetValue(key, out var assigned);
            foreach (var topic in list)
            {
                var limit = GetTopicUsageLimit(topic);
                if (limit <= 0)
                {
                    continue;
                }

                var usedCount = assigned != null && assigned.TryGetValue(topic.Id, out var count) ? count : 0;
                if (usedCount < limit)
                {
                    return false;
                }
            }

            return true;
        }

        bool HadSameLessonTypePreviousDay(int gid, int mid, int lessonTypeId, DateOnly date)
        {
            var prev = date.AddDays(-1);
            return busy.Any(x => x.GroupId == gid
                                 && x.ModuleId == mid
                                 && x.Date == prev
                                 && x.LessonTypeId == lessonTypeId
                                 && !excludedTypeIds.Contains(x.LessonTypeId));
        }

        (int LessonTypeId, ModuleTopic? Topic) PickLessonType(int groupIdPick, int courseIdPick, int moduleIdPick, DateOnly date)
        {
            var topicCandidate = PeekNextTopic(groupIdPick, moduleIdPick);
            if (topicCandidate is not null && TypeAllowed(topicCandidate.LessonTypeId))
            {
                return (topicCandidate.LessonTypeId, topicCandidate);
            }

            if (!hasPreferred.Contains((groupIdPick, moduleIdPick))
                && preferredFirstTypeId != 0
                && TypeAllowed(preferredFirstTypeId)
                && !HadSameLessonTypePreviousDay(groupIdPick, moduleIdPick, preferredFirstTypeId, date))
            {
                return (preferredFirstTypeId, null);
            }

            var cycleCount = activeStudyTypes.Count == 0 ? 1 : activeStudyTypes.Count;
            for (int attempt = 0; attempt < cycleCount; attempt++)
            {
                var candidate = NextCyclicLessonTypeId();
                if (!TypeAllowed(candidate)) continue;
                if (HadSameLessonTypePreviousDay(groupIdPick, moduleIdPick, candidate, date) && cycleCount > 1)
                    continue;

                return (candidate, null);
            }

            var fallbackType = activeStudyTypes.FirstOrDefault()?.Id ?? preferredFirstTypeId;
            if (fallbackType != 0 && TypeAllowed(fallbackType))
            {
                return (fallbackType, null);
            }

            return (types.First().Id, null);
        }
        var sequenceItems = await _db.ModuleSequenceItems
            .Where(x => courseIds.Contains(x.CourseId))
            .OrderBy(x => x.Order)
            .ToListAsync();
        var mainSequenceByCourse = sequenceItems
            .GroupBy(x => x.CourseId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ModuleId).ToList());

        var fillerByCourse = await _db.ModuleFillers
            .Where(x => courseIds.Contains(x.CourseId))
            .GroupBy(x => x.CourseId)
            .ToDictionaryAsync(g => g.Key, g => g.Select(x => x.ModuleId).ToHashSet());

        List<int> BuildCourseModuleOrder(int courseId, List<int> planModules)
        {
            var ordered = new List<int>();
            var seen = new HashSet<int>();
            var planSet = new HashSet<int>(planModules);

            if (mainSequenceByCourse.TryGetValue(courseId, out var mainSequence))
            {
                foreach (var mid in mainSequence)
                {
                    if (planSet.Contains(mid) && seen.Add(mid))
                    {
                        ordered.Add(mid);
                    }
                }
            }

            if (fillerByCourse.TryGetValue(courseId, out var fillerSet) && fillerSet.Count > 0)
            {
                foreach (var mid in planModules)
                {
                    if (fillerSet.Contains(mid) && seen.Add(mid))
                    {
                        ordered.Add(mid);
                    }
                }
            }

            foreach (var mid in planModules)
            {
                if (seen.Add(mid))
                {
                    ordered.Add(mid);
                }
            }

            return ordered;
        }

        var remainingByGroupModule = new Dictionary<(int GroupId, int ModuleId), int>();
        var allGroupsByCourse = await _db.Groups
            .Where(g => courseIds.Contains(g.CourseId))
            .GroupBy(g => g.CourseId)
            .ToDictionaryAsync(g => g.Key, g => g.OrderBy(x => x.Id).ToList());

        var factByGroupModule = await _db.TeacherDraftItems
            .Where(si => !excludedTypeIds.Contains(si.LessonTypeId) && courseIds.Contains(si.Group.CourseId))
            .GroupBy(si => new { si.GroupId, si.ModuleId })
            .Select(g => new { g.Key.GroupId, g.Key.ModuleId, C = g.Count() })
            .ToListAsync();
        var factMap = factByGroupModule.ToDictionary(k => (k.GroupId, k.ModuleId), v => v.C);

        var scheduleByGroupModule = await _db.ScheduleItems
            .Where(si => !excludedTypeIds.Contains(si.LessonTypeId) && courseIds.Contains(si.Group.CourseId))
            .GroupBy(si => new { si.GroupId, si.ModuleId })
            .Select(g => new { g.Key.GroupId, g.Key.ModuleId, C = g.Count() })
            .ToListAsync();
        foreach (var item in scheduleByGroupModule)
        {
            var key = (item.GroupId, item.ModuleId);
            if (factMap.TryGetValue(key, out var existing))
            {
                factMap[key] = existing + item.C;
            }
            else
            {
                factMap[key] = item.C;
            }
        }

        foreach (var plan in activePlans)
        {
            if (!allGroupsByCourse.TryGetValue(plan.CourseId, out var courseGroups) || courseGroups.Count == 0)
                continue;

            int n = courseGroups.Count;
            int baseShare = plan.TargetHours / n;
            int extra = plan.TargetHours % n;
            var moduleMinHours = moduleAuditoriumHours.TryGetValue(plan.ModuleId, out var minHours) ? minHours : 0;

            for (int i = 0; i < n; i++)
            {
                int gid = courseGroups[i].Id;
                int planShare = baseShare + (i < extra ? 1 : 0);
                int target = Math.Max(planShare, moduleMinHours);
                int fact = factMap.TryGetValue((gid, plan.ModuleId), out var c) ? c : 0;
                remainingByGroupModule[(gid, plan.ModuleId)] = Math.Max(0, target - fact);
            }
        }
        int RemainingFor(int gid, int mid) =>
            remainingByGroupModule.TryGetValue((gid, mid), out var left) ? left : 0;



        foreach (var grp in groups)
        {
            var hasCourseSlots = await _db.TimeSlots.AsNoTracking().AnyAsync(s => s.CourseId == grp.CourseId && s.IsActive);
            var slots = await _db.TimeSlots.AsNoTracking()
                .Where(s => s.IsActive && (hasCourseSlots ? s.CourseId == grp.CourseId : s.CourseId == null))
                .OrderBy(s => s.SortOrder).ThenBy(s => s.Start)
                .ToListAsync();

            if (slots.Count == 0)
            {
                warnings.Add($"Не знайдено жодного слоту розкладу (глобального або для курсу {grp.Course.Name}). Група {grp.Name} пропущена.");
                continue;
            }

            bool SlotFilled(DateOnly date, TimeSlot slot) =>
                busy.Any(b => b.GroupId == grp.Id && b.Date == date && b.StartTime == slot.Start && b.EndTime == slot.End);

            bool DayHasGaps(DateOnly date, out TimeSlot? firstGap)
            {
                foreach (var slot in slots)
                {
                    if (!SlotFilled(date, slot))
                    {
                        firstGap = slot;
                        return true;
                    }

                }

                firstGap = null;
                return false;
            }

            string TeacherLabel(int teacherId) =>
                teacherNames.TryGetValue(teacherId, out var name) && !string.IsNullOrWhiteSpace(name)
                    ? name
                    : $"#{teacherId}";

            void RecordSlotFailureReason(DateOnly date, TimeSlot slot, string reason)
            {
                var key = (grp.Id, date, slot.Start, slot.End);
                if (!slotFailureReasons.TryGetValue(key, out var reasons))
                {
                    reasons = new HashSet<string>();
                    slotFailureReasons[key] = reasons;
                }
                reasons.Add(reason);
            }

            void RecordSlotFailureReasonForAllSlots(DateOnly date, string reason)
            {
                foreach (var slot in slots)
                {
                    RecordSlotFailureReason(date, slot, reason);
                }
            }

            string? ComposeGapReason(DateOnly date, TimeSlot slot)
            {
                var key = (grp.Id, date, slot.Start, slot.End);
                if (slotFailureReasons.TryGetValue(key, out var reasons) && reasons.Count > 0)
                {
                    return string.Join("; ", reasons);
                }

                if (!remainingByGroupModule.Any(entry => entry.Key.GroupId == grp.Id && entry.Value > 0))
                {
                    return $"Для групи {grp.Name} більше не лишилось модулів із невикористаними годинами.";
                }

                return null;
            }

            void WarnGap(DateOnly date, TimeSlot gap)
            {
                var label = $"{gap.Start:HH\\:mm}-{gap.End:HH\\:mm}";
                var key = (grp.Id, date, gap.Start, gap.End);
                if (gapWarnings.Add(key))
                {
                    var reason = ComposeGapReason(date, gap);
                    var reasonSuffix = string.IsNullOrWhiteSpace(reason) ? string.Empty : $" Причина: {reason}";
                    warnings.Add($"Автогенерація не заповнила слот {label} для групи {grp.Name} на {date:yyyy-MM-dd}.{reasonSuffix}");
                    gapDetails.Add(new AutoGenGapDetail(
                        GroupId: grp.Id,
                        GroupName: grp.Name,
                        Date: date,
                        Start: gap.Start,
                        End: gap.End,
                        SlotLabel: label,
                        Reason: reason));
                }
            }

            if (typeBreakId.HasValue)
            {
                var glunch = lunchesAll.FirstOrDefault(l => l.CourseId == grp.CourseId)
                          ?? lunchesAll.FirstOrDefault(l => l.CourseId == null);

                if (glunch is not null)
                {
                    var anyModuleId = await _db.ModuleCourses
                        .Where(mc => mc.CourseId == grp.CourseId)
                        .Select(mc => mc.ModuleId)
                        .FirstOrDefaultAsync();
                    if (anyModuleId != 0)
                    {
                        var candidateSlots = slots
                            .Where(sl => sl.Start <= glunch.Start && glunch.End <= sl.End)
                            .ToList();

                        if (candidateSlots.Count == 0)
                        {
                            candidateSlots = slots
                                .Where(sl => sl.Start < glunch.End && glunch.Start < sl.End)
                                .ToList();
                        }

                        for (int d = 0; d < 7; d++)
                        {
                            var date = weekStart.AddDays(d);
                            if (!IsWorking(date)) continue;

                            foreach (var slot in candidateSlots)
                            {
                                var s = slot.Start;
                                var e = slot.End;

                                var hasBreak = busy.Any(b =>
                                    b.GroupId == grp.Id &&
                                    b.Date == date &&
                                    b.StartTime == s &&
                                    b.EndTime == e &&
                                    b.LessonTypeId == typeBreakId.Value);
                                if (hasBreak) continue;

                                var breakItem = new TeacherDraftItem
                                {
                                    Date = date,
                                    DayOfWeek = date.ToDateTime(TimeOnly.MinValue).DayOfWeek,
                                    StartTime = s,
                                    EndTime = e,
                                    GroupId = grp.Id,
                                    ModuleId = anyModuleId,
                                    LessonTypeId = typeBreakId.Value,
                                    Status = DraftStatus.Draft,
                                    IsLocked = false
                                };

                                _db.TeacherDraftItems.Add(breakItem);

                                busy.Add(new BusySlot(grp.Id, null, null, date, s, e, null, anyModuleId, typeBreakId.Value));
                            }
                        }
                    }
                }
            }

            var modules = activePlans.Where(p => p.CourseId == grp.CourseId).Select(p => p.ModuleId).Distinct().ToList();
            var orderedModules = BuildCourseModuleOrder(grp.CourseId, modules);
            fillerByCourse.TryGetValue(grp.CourseId, out var fillerSetRaw);
            var fillerLookup = fillerSetRaw is not null
                ? new HashSet<int>(fillerSetRaw)
                : new HashSet<int>();
            var fillerModulesOrdered = fillerLookup.OrderBy(x => x).ToList();
            var mainModulesOrdered = orderedModules.Where(mid => !fillerLookup.Contains(mid)).ToList();

            int CountCompletedPrimary()
            {
                int total = 0;
                foreach (var mid in mainModulesOrdered)
                {
                    if (factMap.TryGetValue((grp.Id, mid), out var c))
                    {
                        total += c;
                    }
                }
                return total;
            }

            int nextModuleIndex = mainModulesOrdered.Count > 0
                ? CountCompletedPrimary() % mainModulesOrdered.Count
                : 0;

            List<Room> CandidateRoomsFor(int mid)
            {
                allowedRoomsByModule.TryGetValue(mid, out var allowedRooms);
                allowedBuildingsByModule.TryGetValue(mid, out var allowedBuildings);
                return roomsAll
                    .Where(rm => (allowedBuildings == null || allowedBuildings.Count == 0 || allowedBuildings.Contains(rm.BuildingId))
                                 && (allowedRooms == null || allowedRooms.Count == 0 || allowedRooms.Contains(rm.Id))
                                 && rm.Capacity >= grp.StudentsCount)
                    .OrderBy(rm => rm.Capacity)
                    .ThenBy(rm => rm.Id)
                    .ToList();
            }

            int? ResolvePrimaryModule()
            {
                if (mainModulesOrdered.Count == 0) return null;

                for (int offset = 0; offset < mainModulesOrdered.Count; offset++)
                {
                    var idx = (nextModuleIndex + offset) % mainModulesOrdered.Count;
                    var moduleId = mainModulesOrdered[idx];
                    if (RemainingFor(grp.Id, moduleId) <= 0)
                    {
                        continue;
                    }

                    return moduleId;
                }

                return null;
            }

            async Task<bool> TryPlaceModuleAsync(int moduleId, DateOnly date, bool isPrimary, bool allowRepeatPreviousDay = false, bool allowExtraSameDay = false)
            {
                if (CountFor(grp.Id, date) >= slots.Count)
                    return false;

                var remainingKey = (grp.Id, moduleId);
                bool isFiller = fillerLookup.Contains(moduleId);
                string? moduleTitle = null;

                async Task<bool> EnsureModuleTitleAsync()
                {
                    if (moduleTitle is not null)
                    {
                        return true;
                    }

                    moduleTitle = await _db.Modules
                        .Where(m => m.Id == moduleId)
                        .Select(m => m.Title)
                        .FirstOrDefaultAsync();

                    if (moduleTitle is null)
                    {
                        if (missingModulesNotified.Add(moduleId))
                        {
                            warnings.Add($"В базі даних відсутній модуль із ідентифікатором {moduleId}. Автогенерацію для нього пропущено.");
                        }

                        skipped++;
                        return false;
                    }

                    return true;
                }

                string ModuleLabel() => string.IsNullOrWhiteSpace(moduleTitle) ? $"#{moduleId}" : moduleTitle!;

                if (!isFiller && RemainingFor(grp.Id, moduleId) <= 0)
                {
                    return false;
                }

                if (!await EnsureModuleTitleAsync())
                {
                    return false;
                }

                if (TopicsDepleted(grp.Id, moduleId))
                {
                    if (remainingByGroupModule.ContainsKey(remainingKey))
                    {
                        remainingByGroupModule[remainingKey] = 0;
                    }

                    if (topicsExhaustedNotified.Add(remainingKey))
                    {
                        warnings.Add($"Для модуля <{ModuleLabel()}> у групи {grp.Name} вичерпано теми. Пропустили розкладення.");
                    }

                    var topicReason = $"Для модуля <{ModuleLabel()}> у групи {grp.Name} вичерпано теми для цього тижня.";
                    RecordSlotFailureReasonForAllSlots(date, topicReason);
                    return false;
                }

                bool TooManySameThisDay() => CountModuleForDay(grp.Id, date, moduleId) >= 2;
                if (!allowExtraSameDay && TooManySameThisDay())
                {
                    RecordSlotFailureReasonForAllSlots(date, $"Для модуля <{ModuleLabel()}> у групи {grp.Name} уже є дві пари на {date:dd.MM.yyyy}.");
                    return false;
                }

                if (!isFiller && !allowRepeatPreviousDay && HadSameModulePreviousDay(grp.Id, moduleId, date))
                {
                    RecordSlotFailureReasonForAllSlots(date, $"Для модуля <{ModuleLabel()}> у групи {grp.Name} вже розкладено пару напередодні, тому цей слот пропущено.");
                    return false;
                }

                var tids = teachersForModule
                    .Where(x => x.ModuleId == moduleId)
                    .Select(x => x.TeacherId)
                    .Distinct()
                    .OrderBy(id => id)
                    .ToList();
                if (tids.Count == 0)
                {
                    var teacherReason = $"Не знайдено викладачів для модуля <{ModuleLabel()}> (група {grp.Name}).";
                    RecordSlotFailureReasonForAllSlots(date, teacherReason);
                    warnings.Add(teacherReason);
                    skipped++;
                    return false;
                }

                var candidateRooms = CandidateRoomsFor(moduleId);

                foreach (var sl in slots)
                {
                    if (CountFor(grp.Id, date) >= slots.Count) break;

                    var s = sl.Start;
                    var e = sl.End;
                    var slotLabel = $"{s:HH\\:mm}-{e:HH\\:mm}";
                    var slotIssues = new HashSet<string>();

                    bool slotBreak = busy.Any(b => b.GroupId == grp.Id && b.Date == date
                                                   && b.LessonTypeId == typeBreakId && b.StartTime == s && b.EndTime == e);
                    if (slotBreak) continue;

                    foreach (var tidCandidate in tids)
                    {
                        if (!TeacherFitsWorkingHours(tidCandidate, date, s, e))
                        {
                            slotIssues.Add($"Викладач {TeacherLabel(tidCandidate)} не працює у слоті {slotLabel}.");
                            continue;
                        }

                        bool peopleBusy = busy.Any(x => x.Date == date
                                                        && (x.GroupId == grp.Id || x.TeacherId == tidCandidate)
                                                        && x.StartTime < e && s < x.EndTime);
                        if (peopleBusy)
                        {
                            slotIssues.Add($"Група {grp.Name} або викладач {TeacherLabel(tidCandidate)} зайняті у слоті {slotLabel}.");
                            continue;
                        }

                        var pickResult = PickLessonType(grp.Id, grp.CourseId, moduleId, date);
                        var ltypeId = pickResult.LessonTypeId;
                        var topicSelection = pickResult.Topic;
                        if (!TypeAllowed(ltypeId))
                        {
                            continue;
                        }

                        var requiresRoom = (typeById.TryGetValue(ltypeId, out var ltMeta) ? ltMeta.RequiresRoom : (bool?)null) ?? true;

                        if (requiresRoom)
                        {
                            if (candidateRooms.Count == 0)
                            {
                                var roomReason = $"Не знайдено аудиторій для модуля <{ModuleLabel()}> (група {grp.Name}) у слоті {slotLabel}.";
                                RecordSlotFailureReason(date, sl, roomReason);
                                warnings.Add($"Не знайдено аудиторій для модуля <{ModuleLabel()}> (група {grp.Name}).");
                                skipped++;
                                return false;
                            }

                            foreach (var rm in candidateRooms)
                            {
                                bool roomBusy = busy.Any(x => x.Date == date
                                                              && x.RoomId == rm.Id
                                                              && x.StartTime < e && s < x.EndTime);
                                if (roomBusy)
                                {
                                    slotIssues.Add($"Усі аудиторії для модуля <{ModuleLabel()}> зайняті у слоті {slotLabel}.");
                                    continue;
                                }

                                var item = new TeacherDraftItem
                                {
                                    Date = date,
                                    DayOfWeek = date.ToDateTime(TimeOnly.MinValue).DayOfWeek,
                                    StartTime = s,
                                    EndTime = e,
                                    GroupId = grp.Id,
                                    ModuleId = moduleId,
                                    RoomId = rm.Id,
                                    TeacherId = tidCandidate,
                                    ModuleTopicId = topicSelection?.Id,
                                    LessonTypeId = ltypeId,
                                    Status = DraftStatus.Draft,
                                    IsLocked = false
                                };
                                _db.TeacherDraftItems.Add(item);

                                if (topicSelection is not null)
                                {
                                    MarkTopicUsed(grp.Id, moduleId, topicSelection);
                                }

                                busy.Add(new BusySlot(
                                    grp.Id,
                                    tidCandidate,
                                    rm.Id,
                                    date,
                                    s,
                                    e,
                                    rm.BuildingId,
                                    moduleId,
                                    ltypeId));

                                created++;
                                Inc(grp.Id, date);
                                hasPreferred.Add((grp.Id, moduleId));

                                if (remainingByGroupModule.TryGetValue(remainingKey, out var left) && left > 0)
                                {
                                    left--;
                                    remainingByGroupModule[remainingKey] = Math.Max(0, left);
                                }

                                if (isPrimary && mainModulesOrdered.Count > 0)
                                {
                                    var currentIdx = mainModulesOrdered.FindIndex(mid => mid == moduleId);
                                    if (currentIdx >= 0)
                                    {
                                        nextModuleIndex = (currentIdx + 1) % mainModulesOrdered.Count;
                                    }
                                }

                                return true;
                            }
                        }
                        else
                        {
                            var item = new TeacherDraftItem
                            {
                                Date = date,
                                DayOfWeek = date.ToDateTime(TimeOnly.MinValue).DayOfWeek,
                                StartTime = s,
                                EndTime = e,
                                GroupId = grp.Id,
                                ModuleId = moduleId,
                                RoomId = null,
                                TeacherId = tidCandidate,
                                ModuleTopicId = topicSelection?.Id,
                                LessonTypeId = ltypeId,
                                Status = DraftStatus.Draft,
                                IsLocked = false
                            };
                            _db.TeacherDraftItems.Add(item);

                            if (topicSelection is not null)
                            {
                                MarkTopicUsed(grp.Id, moduleId, topicSelection);
                            }

                            busy.Add(new BusySlot(
                                grp.Id,
                                tidCandidate,
                                null,
                                date,
                                s,
                                e,
                                null,
                                moduleId,
                                ltypeId));

                            created++;
                            Inc(grp.Id, date);
                            hasPreferred.Add((grp.Id, moduleId));

                            if (remainingByGroupModule.TryGetValue(remainingKey, out var left) && left > 0)
                            {
                                left--;
                                remainingByGroupModule[remainingKey] = Math.Max(0, left);
                            }

                            if (isPrimary && mainModulesOrdered.Count > 0)
                            {
                                var currentIdx = mainModulesOrdered.FindIndex(mid => mid == moduleId);
                                if (currentIdx >= 0)
                                {
                                    nextModuleIndex = (currentIdx + 1) % mainModulesOrdered.Count;
                                }
                            }

                            return true;
                        }

                    }
                    if (slotIssues.Count > 0)
                    {
                        foreach (var reason in slotIssues)
                        {
                            RecordSlotFailureReason(date, sl, reason);
                        }
                    }
                }

                return false;
            }

            for (int d = 0; d < 7; d++)
            {
                var date = weekStart.AddDays(d);
                if (!IsWorking(date)) continue;

                int maxPerDay = slots.Count;
                if (maxPerDay == 0) continue;

                var modulesAttemptedToday = new HashSet<int>();

                async Task FillWithRemainingModulesAsync(bool allowRepeatPreviousDay = false, bool allowExtraSameDay = false)
                {
                    foreach (var moduleId in orderedModules)
                    {
                        if (CountFor(grp.Id, date) >= maxPerDay)
                        {
                            break;
                        }

                        if (!allowExtraSameDay && modulesAttemptedToday.Contains(moduleId))
                        {
                            continue;
                        }

                        if (RemainingFor(grp.Id, moduleId) <= 0)
                        {
                            continue;
                        }

                        modulesAttemptedToday.Add(moduleId);
                        await TryPlaceModuleAsync(moduleId, date, isPrimary: false, allowRepeatPreviousDay: allowRepeatPreviousDay, allowExtraSameDay: allowExtraSameDay);
                    }
                }

                var primaryModuleId = ResolvePrimaryModule();
                bool placedPrimary = false;
                if (primaryModuleId.HasValue)
                {
                    modulesAttemptedToday.Add(primaryModuleId.Value);
                    placedPrimary = await TryPlaceModuleAsync(primaryModuleId.Value, date, isPrimary: true);
                    if (placedPrimary
                        && RemainingFor(grp.Id, primaryModuleId.Value) > 0
                        && CountModuleForDay(grp.Id, date, primaryModuleId.Value) < 2
                        && CountFor(grp.Id, date) < maxPerDay)
                    {
                        await TryPlaceModuleAsync(primaryModuleId.Value, date, isPrimary: true);
                    }
                }

                Queue<int> BuildFillerQueueForDay()
                {
                    if (fillerModulesOrdered.Count == 0)
                        return new Queue<int>();

                    var dayRandom = new Random(HashCode.Combine(weekStart.DayNumber, grp.Id, date.DayNumber));
                    var shuffled = fillerModulesOrdered
                        .OrderBy(_ => dayRandom.Next())
                        .ToList();
                    return new Queue<int>(shuffled);
                }

                if (fillerModulesOrdered.Count > 0)
                {
                    var fillerQueue = BuildFillerQueueForDay();
                    int fillerAttempts = 0;

                    while (CountFor(grp.Id, date) < maxPerDay)
                    {
                        if (fillerQueue.Count == 0) break;

                        var fillerModuleId = fillerQueue.Dequeue();
                        if (RemainingFor(grp.Id, fillerModuleId) <= 0)
                        {
                            fillerAttempts++;
                            if (fillerAttempts >= fillerModulesOrdered.Count)
                            {
                                break;
                            }

                            if (fillerQueue.Count == 0)
                            {
                                fillerQueue = BuildFillerQueueForDay();
                            }
                            continue;
                        }

                        modulesAttemptedToday.Add(fillerModuleId);
                        var placedFiller = await TryPlaceModuleAsync(fillerModuleId, date, isPrimary: false);
                        if (!placedFiller)
                        {
                            fillerAttempts++;
                            if (fillerAttempts >= fillerModulesOrdered.Count)
                            {
                                break;
                            }

                            if (fillerQueue.Count == 0)
                            {
                                fillerQueue = BuildFillerQueueForDay();
                            }
                            continue;
                        }

                        fillerAttempts = 0;

                        if (fillerQueue.Count == 0 && CountFor(grp.Id, date) < maxPerDay)
                        {
                            fillerQueue = BuildFillerQueueForDay();
                        }
                    }
                }

                if (CountFor(grp.Id, date) < maxPerDay)
                {
                    await FillWithRemainingModulesAsync();
                }

                if (CountFor(grp.Id, date) < maxPerDay)
                {
                    await FillWithRemainingModulesAsync(allowRepeatPreviousDay: true, allowExtraSameDay: true);
                }

                if (DayHasGaps(date, out var remainingGap) && remainingGap is not null)
                {
                    WarnGap(date, remainingGap);
                }
            }
        }

        await _db.SaveChangesAsync();
        return Ok(new AutoGenResult(created, skipped, warnings, gapDetails));
    }


    [HttpPost("approve-week")]
    /// <summary>
    /// Позначає чернетки викладача за тиждень як затверджені.
    /// </summary>
    public async Task<IActionResult> ApproveWeek([FromBody] ApproveWeekRequest r)
    {
        var start = r.WeekStart;
        var end = start.AddDays(7);

        var rows = await _db.TeacherDraftItems
            .Where(x => x.TeacherId == r.TeacherId && x.Date >= start && x.Date < end)
            .ToListAsync();

        foreach (var x in rows) x.Status = DraftStatus.Published;

        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("publish-week")]
    /// <summary>
    /// Публікує затверджені чернетки у розкладі та повертає статистику операції.
    /// </summary>
    public async Task<ActionResult<PublishWeekResults>> PublishWeek([FromBody] PublishWeekRequest r)
    {
        var start = r.WeekStart;
        var end = start.AddDays(7);

        var q = _db.TeacherDraftItems.Where(x => x.Date >= start && x.Date < end);
        if (r.TeacherId is int tid) q = q.Where(x => x.TeacherId == tid);

        var drafts = await q
            .Include(x => x.Group)
            .ToListAsync();

        var lessonTypeIds = drafts.Select(d => d.LessonTypeId).Distinct().ToList();
        var lessonTypeRoomMap = await _db.LessonTypes.AsNoTracking()
            .Where(lt => lessonTypeIds.Contains(lt.Id))
            .ToDictionaryAsync(lt => lt.Id, lt => lt.RequiresRoom);

        var calendar = await _db.CalendarExceptions.AsNoTracking()
            .Where(x => x.Date >= start && x.Date < end)
            .ToDictionaryAsync(x => x.Date, x => x.IsWorkingDay);

        int created = 0, skipped = 0;
        var warnings = new List<string>();
        var publishedIds = new List<int>();

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        foreach (var d in drafts)
        {
            var isWeekend = d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            var isWorking = !isWeekend;
            if (calendar.TryGetValue(d.Date, out var isWorkingDay))
            {
                isWorking = isWorkingDay;
            }
            var overrideNonWorking = !isWorking;

            var requiresRoom = lessonTypeRoomMap.TryGetValue(d.LessonTypeId, out var reqRoom) ? reqRoom : true;
            var normalizedRoomId = requiresRoom ? d.RoomId : null;

            var req = new UpsertScheduleItemRequest(
                Id: null,
                Date: d.Date,
                TimeStart: d.StartTime.ToString("HH:mm"),
                TimeEnd: d.EndTime.ToString("HH:mm"),
                GroupId: d.GroupId,
                ModuleId: d.ModuleId,
                TeacherId: d.TeacherId,
                RoomId: normalizedRoomId,
                LessonTypeId: d.LessonTypeId,
                IsLocked: false,
                OverrideNonWorkingDay: overrideNonWorking
            );

            var (errors, warn) = await _rules.ValidateUpsertAsync(req);
            if (errors.Count > 0)
            {
                skipped++;
                warnings.Add($"[{d.Date:yyyy-MM-dd} {d.StartTime:HH\\:mm}-{d.EndTime:HH\\:mm}] {string.Join("; ", errors)}");
                continue;
            }

            var item = new ScheduleItem
            {
                Date = d.Date,
                DayOfWeek = d.DayOfWeek,
                StartTime = d.StartTime,
                EndTime = d.EndTime,
                GroupId = d.GroupId,
                ModuleId = d.ModuleId,
                RoomId = normalizedRoomId,
                TeacherId = d.TeacherId,
                ModuleTopicId = d.ModuleTopicId,
                LessonTypeId = d.LessonTypeId,
                IsLocked = false
            };
            _db.ScheduleItems.Add(item);
            created++;
            publishedIds.Add(d.Id);
        }

        await _db.SaveChangesAsync();

        if (publishedIds.Count > 0)
            await _db.TeacherDraftItems
                .Where(x => publishedIds.Contains(x.Id))
                .ExecuteDeleteAsync();

        var publishedDrafts = drafts.Where(d => publishedIds.Contains(d.Id)).ToList();
        var affectedPlans = publishedDrafts
            .Select(x => new { x.ModuleId, CourseId = x.Group.CourseId })
            .Distinct()
            .Select(x => (x.CourseId, x.ModuleId));
        var affectedLoads = publishedDrafts
            .Where(x => x.TeacherId != null)
            .Select(x => new { TeacherId = x.TeacherId!.Value, CourseId = x.Group.CourseId })
            .Distinct()
            .Select(x => (x.TeacherId, x.CourseId));

        await RecalcAggregatesAsync(affectedPlans, affectedLoads);

        await tx.CommitAsync();
        return Ok(new PublishWeekResults(created, skipped, warnings));
    }
    /// <summary>
    /// Перераховує агреговані плани та навантаження після змін у чернетках.
    /// </summary>


    private async Task RecalcAggregatesAsync(
        IEnumerable<(int CourseId, int ModuleId)> plans,
        IEnumerable<(int TeacherId, int CourseId)> loads)
    {
        var lessonTypes = await _db.LessonTypes
            .Select(lt => new { lt.Id, lt.Code, lt.CountInPlan, lt.CountInLoad })
            .ToListAsync();

        var excludePlanIds = lessonTypes
            .Where(lt => !lt.CountInPlan && !string.Equals(lt.Code, "CANCELED", System.StringComparison.OrdinalIgnoreCase))
            .Select(lt => lt.Id)
            .ToHashSet();

        var excludeLoadIds = lessonTypes
            .Where(lt => !lt.CountInLoad)
            .Select(lt => lt.Id)
            .ToHashSet();

        var planKeys = plans.Distinct().ToList();
        if (planKeys.Count > 0)
        {
            var cIds = planKeys.Select(k => k.CourseId).Distinct().ToList();
            var mIds = planKeys.Select(k => k.ModuleId).Distinct().ToList();

            var counts = await _db.ScheduleItems
                .Include(si => si.Group)
                .Where(si => !excludePlanIds.Contains(si.LessonTypeId)
                             && cIds.Contains(si.Group.CourseId)
                             && mIds.Contains(si.ModuleId))
                .GroupBy(si => new { CourseId = si.Group.CourseId, si.ModuleId })
                .Select(g => new { g.Key.CourseId, g.Key.ModuleId, GCount = g.Count() })
                .ToListAsync();

            var plansToUpdate = await _db.ModulePlans
                .Where(mp => cIds.Contains(mp.CourseId) && mIds.Contains(mp.ModuleId))
                .ToListAsync();

            foreach (var p in plansToUpdate)
                p.ScheduledHours = counts.FirstOrDefault(c => c.CourseId == p.CourseId && c.ModuleId == p.ModuleId)?.GCount ?? 0;
        }

        var loadKeys = loads.Distinct().ToList();
        if (loadKeys.Count > 0)
        {
            var tIds = loadKeys.Select(k => k.TeacherId).Distinct().ToList();
            var cIds = loadKeys.Select(k => k.CourseId).Distinct().ToList();

            var counts = await _db.ScheduleItems
                .Include(si => si.Group)
                .Where(si => si.TeacherId != null
                             && !excludeLoadIds.Contains(si.LessonTypeId)
                             && tIds.Contains(si.TeacherId!.Value)
                             && cIds.Contains(si.Group.CourseId))
                .GroupBy(si => new { TeacherId = si.TeacherId!.Value, si.Group.CourseId })
                .Select(g => new { g.Key.TeacherId, g.Key.CourseId, GCount = g.Count() })
                .ToListAsync();

            var loadsToUpdate = await _db.TeacherCourseLoads
                .Where(l => l.IsActive && tIds.Contains(l.TeacherId) && cIds.Contains(l.CourseId))
                .ToListAsync();

            foreach (var l in loadsToUpdate)
                l.ScheduledHours = counts.FirstOrDefault(c => c.TeacherId == l.TeacherId && c.CourseId == l.CourseId)?.GCount ?? 0;
        }

        await _db.SaveChangesAsync();
    }
}
