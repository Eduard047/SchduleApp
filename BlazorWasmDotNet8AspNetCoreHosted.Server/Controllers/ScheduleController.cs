using System.Collections.Generic;
using System.Data;
using System.Linq;
using BlazorWasmDotNet8AspNetCoreHosted.Server.Application;
using BlazorWasmDotNet8AspNetCoreHosted.Server.Domain.Entities;
using BlazorWasmDotNet8AspNetCoreHosted.Server.Infrastructure;
using BlazorWasmDotNet8AspNetCoreHosted.Shared.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlazorWasmDotNet8AspNetCoreHosted.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ScheduleController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly RulesService _rules;

    public ScheduleController(AppDbContext db, RulesService rules)
    {
        _db = db;
        _rules = rules;
    }

    
    
    
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
    public async Task<IReadOnlyList<ScheduleItemDto>> Get(
        [FromQuery] DateOnly weekStart,
        [FromQuery] int? courseId,
        [FromQuery] int? groupId,
        [FromQuery] int? teacherId,
        [FromQuery] int? roomId)
    {
        var weekEnd = weekStart.AddDays(7);

        var q = _db.ScheduleItems
            .Include(x => x.Group).ThenInclude(g => g.Course)
            .Include(x => x.Module)
            .Include(x => x.Teacher)
            .Include(x => x.Room).ThenInclude(r => r!.Building)
            .Include(x => x.LessonType)
            .Where(x => x.Date >= weekStart && x.Date < weekEnd)
            .AsQueryable();

        if (courseId is int cid) q = q.Where(x => x.Group.CourseId == cid);
        if (groupId is int gidFilter) q = q.Where(x => x.GroupId == gidFilter);
        if (teacherId is int tid) q = q.Where(x => x.TeacherId == tid);
        if (roomId is int rid) q = q.Where(x => x.RoomId == rid);

        var items = await q.OrderBy(x => x.Date).ThenBy(x => x.StartTime).ToListAsync();
        return items.Select(i => i.ToDto()).ToList();
    }

    
    [HttpPost("upsert")]
    public async Task<ActionResult<int>> Upsert([FromBody] UpsertScheduleItemRequest r)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        var (errors, warnings) = await _rules.ValidateUpsertAsync(r);
        if (errors.Count > 0) return Conflict(new { message = "Validation failed", errors, warnings });

        var lt = await _db.LessonTypes.FindAsync(r.LessonTypeId);
        if (lt is null) return BadRequest(new { message = "LessonType not found" });

        var normalizedRoomId = lt.RequiresRoom ? r.RoomId : null;

        var start = TimeOnly.Parse(r.TimeStart);
        var end = TimeOnly.Parse(r.TimeEnd);

        if (r.Id is int id && id > 0)
        {
            
            var item = await _db.ScheduleItems.FirstOrDefaultAsync(x => x.Id == id);
            if (item is null) return NotFound(new { message = $"ScheduleItem {id} not found" });

            var previousLessonTypeId = item.LessonTypeId;
            var oldGroupId = item.GroupId;
            var oldModuleId = item.ModuleId;
            var oldTeacherId = item.TeacherId;
            var oldCourseId = await _db.Groups.Where(g => g.Id == oldGroupId).Select(g => g.CourseId).FirstAsync();

            item.Date = r.Date;
            item.DayOfWeek = r.Date.ToDateTime(TimeOnly.MinValue).DayOfWeek;
            item.StartTime = start;
            item.EndTime = end;
            item.GroupId = r.GroupId;
            item.ModuleId = r.ModuleId;
            item.RoomId = normalizedRoomId;
            item.TeacherId = r.TeacherId;
            item.LessonTypeId = r.LessonTypeId;
            item.IsLocked = r.IsLocked;

            var recheck = await _rules.ValidateUpsertAsync(new UpsertScheduleItemRequest(
                item.Id, item.Date, item.StartTime.ToString("HH:mm"), item.EndTime.ToString("HH:mm"),
                item.GroupId, item.ModuleId, item.TeacherId, item.RoomId, item.LessonTypeId, item.IsLocked, r.OverrideNonWorkingDay));
            if (recheck.errors.Count > 0)
                return Conflict(new { message = "Validation failed (recheck)", errors = recheck.errors, warnings = recheck.warnings });

            await _db.SaveChangesAsync();

            
            var isRescheduled = string.Equals(lt.Code, "RESCHEDULED", StringComparison.OrdinalIgnoreCase);
            if (isRescheduled && previousLessonTypeId != r.LessonTypeId)
            {
                await TryCreateRescheduledCopyAsync(item, previousLessonTypeId, start, end);
            }

            var newCourseId = await _db.Groups.Where(g => g.Id == r.GroupId).Select(g => g.CourseId).FirstAsync();
            await RecalcAggregatesAsync(
                plans: new[] { (newCourseId, r.ModuleId) },
                loads: new[]
                {
                    (oldTeacherId is int t1) ? (t1, oldCourseId) : default,
                    (r.TeacherId  is int t2) ? (t2, newCourseId) : default
                }.Where(x => x != default)!);

            await tx.CommitAsync();
            return Ok(item.Id);
        }
        else
        {
            
            var item = new ScheduleItem
            {
                Date = r.Date,
                DayOfWeek = r.Date.ToDateTime(TimeOnly.MinValue).DayOfWeek,
                StartTime = start,
                EndTime = end,
                GroupId = r.GroupId,
                ModuleId = r.ModuleId,
                RoomId = normalizedRoomId,
                TeacherId = r.TeacherId,
                LessonTypeId = r.LessonTypeId,
                IsLocked = r.IsLocked
            };
            _db.ScheduleItems.Add(item);

            var recheck = await _rules.ValidateUpsertAsync(new UpsertScheduleItemRequest(
                r.Id,
                r.Date,
                r.TimeStart,
                r.TimeEnd,
                r.GroupId,
                r.ModuleId,
                r.TeacherId,
                normalizedRoomId,
                r.LessonTypeId,
                r.IsLocked,
                r.OverrideNonWorkingDay));
            if (recheck.errors.Count > 0)
                return Conflict(new { message = "Validation failed (recheck)", errors = recheck.errors, warnings = recheck.warnings });

            await _db.SaveChangesAsync();

            var courseId = await _db.Groups.Where(g => g.Id == r.GroupId).Select(g => g.CourseId).FirstAsync();
            await RecalcAggregatesAsync(
                plans: new[] { (courseId, r.ModuleId) },
                loads: (r.TeacherId is int t) ? new[] { (t, courseId) } : null
            );

            await tx.CommitAsync();
            return Ok(item.Id);
        }
    }

    
    private static DateOnly WeekStartMonday(DateOnly date)
    {
        var diff = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-diff);
    }

    private async Task TryCreateRescheduledCopyAsync(ScheduleItem source, int previousLessonTypeId, TimeOnly originalStart, TimeOnly originalEnd)
    {
        var prevType = await _db.LessonTypes.FindAsync(previousLessonTypeId);
        if (prevType is null) return;

        var requiresRoom = prevType.RequiresRoom;
        var normalizedRoomId = requiresRoom ? source.RoomId : null;

        var groupInfo = await _db.Groups
            .Where(g => g.Id == source.GroupId)
            .Select(g => new { g.CourseId })
            .FirstOrDefaultAsync();
        if (groupInfo is null) return;

        var nextWeekStart = WeekStartMonday(source.Date).AddDays(7);
        var earliestAllowed = nextWeekStart.ToDateTime(originalStart);

        
        var sequenceItems = await _db.ModuleSequenceItems
            .Where(x => x.CourseId == groupInfo.CourseId)
            .OrderBy(x => x.Order)
            .Select(x => new { x.ModuleId, x.Order })
            .ToListAsync();

        var currentSequence = sequenceItems.FirstOrDefault(x => x.ModuleId == source.ModuleId);
        if (currentSequence is not null)
        {
            var predecessors = sequenceItems
                .Where(x => x.Order < currentSequence.Order)
                .Select(x => x.ModuleId)
                .ToList();

            if (predecessors.Count > 0)
            {
                var predecessorItems = await _db.ScheduleItems
                    .Where(x => x.GroupId == source.GroupId && predecessors.Contains(x.ModuleId))
                    .Select(x => new { x.Date, x.EndTime })
                    .ToListAsync();

                if (predecessorItems.Count > 0)
                {
                    var predecessorMax = predecessorItems
                        .Select(x => x.Date.ToDateTime(x.EndTime))
                        .Max()
                        .AddMinutes(1);

                    if (predecessorMax > earliestAllowed)
                    {
                        earliestAllowed = predecessorMax;
                    }
                }
            }
        }

        var hasCourseSlots = await _db.TimeSlots.AsNoTracking().AnyAsync(s => s.IsActive && s.CourseId == groupInfo.CourseId);
        var effectiveSlots = await _db.TimeSlots.AsNoTracking()
            .Where(s => s.IsActive && (hasCourseSlots ? s.CourseId == groupInfo.CourseId : s.CourseId == null))
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Start)
            .Select(s => new { s.Start, s.End })
            .ToListAsync();

        var candidateSlots = new List<(TimeOnly Start, TimeOnly End)> { (originalStart, originalEnd) };
        foreach (var slot in effectiveSlots)
        {
            if (slot.Start == originalStart && slot.End == originalEnd) continue;
            if (candidateSlots.Contains((slot.Start, slot.End))) continue;
            candidateSlots.Add((slot.Start, slot.End));
        }

        if (candidateSlots.Count == 0)
        {
            candidateSlots.Add((originalStart, originalEnd));
        }

        const int daySearchHorizon = 7; 
        for (int offset = 0; offset < daySearchHorizon; offset++)
        {
            var candidate = nextWeekStart.AddDays(offset);
            foreach (var (slotStart, slotEnd) in candidateSlots)
            {
                var candidateMoment = candidate.ToDateTime(slotStart);
                if (candidateMoment < earliestAllowed) continue;

                var batchKey = $"rescheduled:{source.Id}:{previousLessonTypeId}";

                var draftRequest = new DraftUpsertRequest(
                    Id: null,
                    Date: candidate,
                    TimeStart: slotStart.ToString("HH:mm"),
                    TimeEnd: slotEnd.ToString("HH:mm"),
                    GroupId: source.GroupId,
                    ModuleId: source.ModuleId,
                    ModuleTopicId: source.ModuleTopicId,
                    TeacherId: source.TeacherId,
                    RoomId: normalizedRoomId,
                    RequiresRoom: requiresRoom,
                    LessonTypeId: previousLessonTypeId,
                    OverrideNonWorkingDay: false,
                    BatchKey: batchKey,
                    IsLocked: false,
                    IgnoreValidationErrors: false
                );

                var validation = await _rules.ValidateDraftAsync(draftRequest);
                if (validation.Errors.Count > 0) continue;

                var exists = await _db.ScheduleItems.AnyAsync(x =>
                    x.Date == candidate
                    && x.StartTime == slotStart
                    && x.EndTime == slotEnd
                    && x.GroupId == source.GroupId
                    && x.ModuleId == source.ModuleId
                    && x.RoomId == normalizedRoomId
                    && x.TeacherId == source.TeacherId
                    && x.LessonTypeId == previousLessonTypeId);
                if (exists) continue;

                var existsDraft = await _db.TeacherDraftItems.AnyAsync(x =>
                    x.Date == candidate
                    && x.StartTime == slotStart
                    && x.EndTime == slotEnd
                    && x.GroupId == source.GroupId
                    && x.ModuleId == source.ModuleId
                    && x.RoomId == normalizedRoomId
                    && x.TeacherId == source.TeacherId);
                if (existsDraft) continue;

                var newDraft = new TeacherDraftItem
                {
                    Date = candidate,
                    DayOfWeek = candidate.ToDateTime(TimeOnly.MinValue).DayOfWeek,
                    StartTime = slotStart,
                    EndTime = slotEnd,
                    GroupId = source.GroupId,
                    ModuleId = source.ModuleId,
                    TeacherId = source.TeacherId,
                    RoomId = normalizedRoomId,
                    LessonTypeId = previousLessonTypeId,
                    Status = DraftStatus.Draft,
                    PublishedItemId = null,
                    BatchKey = batchKey,
                    ValidationWarnings = null,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    IsLocked = true 
                };

                
                _db.TeacherDraftItems.Add(newDraft);
                await _db.SaveChangesAsync();
                return;
            }
        }
    }

    
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var info = await _db.ScheduleItems
            .Where(x => x.Id == id)
            .Select(x => new { x.GroupId, x.ModuleId, x.TeacherId, CourseId = x.Group.CourseId })
            .FirstOrDefaultAsync();

        if (info is null)
            return NotFound(new { message = $"ScheduleItem {id} not found" });

        await _db.ScheduleItems.Where(x => x.Id == id).ExecuteDeleteAsync();

        await RecalcAggregatesAsync(
            plans: new[] { (info.CourseId, info.ModuleId) },
            loads: (info.TeacherId is int t) ? new[] { (t, info.CourseId) } : null
        );

        return NoContent();
    }


    
    [HttpPost("clear")]
    public async Task<ActionResult<ClearWeekResult>> ClearWeek([FromBody] ClearWeekRequest r)
    {
        var start = r.WeekStart;
        var end = start.AddDays(7);

        var q = _db.ScheduleItems.Where(x => x.Date >= start && x.Date < end && !x.IsLocked);
        if (r.CourseId is int cid) q = q.Where(x => x.Group.CourseId == cid);
        if (r.GroupId is int gidFilter) q = q.Where(x => x.GroupId == gidFilter);

        var affectedPlans = await q
            .Select(x => new { x.ModuleId, CourseId = x.Group.CourseId })
            .Distinct()
            .ToListAsync();

        var affectedLoads = await q.Where(x => x.TeacherId != null)
            .Select(x => new { x.TeacherId, CourseId = x.Group.CourseId })
            .Distinct()
            .ToListAsync();

        var deleted = await q.ExecuteDeleteAsync();

        await RecalcAggregatesAsync(
            plans: affectedPlans.Select(a => (a.CourseId, a.ModuleId)),
            loads: affectedLoads.Select(a => (a.TeacherId!.Value, a.CourseId))
        );

        return Ok(new ClearWeekResult(deleted));
    }

    
    
    
    private async Task RecalcAggregatesAsync(
        IEnumerable<(int CourseId, int ModuleId)>? plans = null,
        IEnumerable<(int TeacherId, int CourseId)>? loads = null)
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

        
        if (plans is null)
        {
            var allPlans = await _db.ModulePlans.ToListAsync();
            var cIds = allPlans.Select(p => p.CourseId).Distinct().ToList();
            var mIds = allPlans.Select(p => p.ModuleId).Distinct().ToList();

            var counts = await _db.ScheduleItems
                .Include(si => si.Group)
                .Where(si => !excludePlanIds.Contains(si.LessonTypeId)
                             && cIds.Contains(si.Group.CourseId)
                             && mIds.Contains(si.ModuleId))
                .GroupBy(si => new { CourseId = si.Group.CourseId, si.ModuleId })
                .Select(g => new { g.Key.CourseId, g.Key.ModuleId, C = g.Count() })
                .ToListAsync();

            foreach (var p in allPlans)
                p.ScheduledHours = counts.FirstOrDefault(c => c.CourseId == p.CourseId && c.ModuleId == p.ModuleId)?.C ?? 0;
        }
        else
        {
            var keys = plans.Distinct().ToList();
            if (keys.Count > 0)
            {
                var cIds = keys.Select(k => k.CourseId).Distinct().ToList();
                var mIds = keys.Select(k => k.ModuleId).Distinct().ToList();

                var counts = await _db.ScheduleItems
                    .Include(si => si.Group)
                    .Where(si => !excludePlanIds.Contains(si.LessonTypeId)
                                 && cIds.Contains(si.Group.CourseId)
                                 && mIds.Contains(si.ModuleId))
                    .GroupBy(si => new { CourseId = si.Group.CourseId, si.ModuleId })
                    .Select(g => new { g.Key.CourseId, g.Key.ModuleId, C = g.Count() })
                    .ToListAsync();

                var plansToUpdate = await _db.ModulePlans
                    .Where(mp => cIds.Contains(mp.CourseId) && mIds.Contains(mp.ModuleId))
                    .ToListAsync();

                foreach (var p in plansToUpdate)
                    p.ScheduledHours = counts.FirstOrDefault(c => c.CourseId == p.CourseId && c.ModuleId == p.ModuleId)?.C ?? 0;
            }
        }

        
        if (loads is null)
        {
            var activeLoads = await _db.TeacherCourseLoads.Where(l => l.IsActive).ToListAsync();
            var tIds = activeLoads.Select(l => l.TeacherId).Distinct().ToList();
            var cIds = activeLoads.Select(l => l.CourseId).Distinct().ToList();

            var counts = await _db.ScheduleItems
                .Include(si => si.Group)
                .Where(si => si.TeacherId != null
                             && !excludeLoadIds.Contains(si.LessonTypeId)
                             && tIds.Contains(si.TeacherId!.Value)
                             && cIds.Contains(si.Group.CourseId))
                .GroupBy(si => new { TeacherId = si.TeacherId!.Value, si.Group.CourseId })
                .Select(g => new { g.Key.TeacherId, g.Key.CourseId, C = g.Count() })
                .ToListAsync();

            foreach (var l in activeLoads)
                l.ScheduledHours = counts.FirstOrDefault(c => c.TeacherId == l.TeacherId && c.CourseId == l.CourseId)?.C ?? 0;

            var inactive = await _db.TeacherCourseLoads.Where(l => !l.IsActive).ToListAsync();
            foreach (var l in inactive) l.ScheduledHours = 0;
        }
        else
        {
            var keys = loads.Distinct().ToList();
            if (keys.Count > 0)
            {
                var tIds = keys.Select(k => k.TeacherId).Distinct().ToList();
                var cIds = keys.Select(k => k.CourseId).Distinct().ToList();

                var counts = await _db.ScheduleItems
                    .Include(si => si.Group)
                    .Where(si => si.TeacherId != null
                                 && !excludeLoadIds.Contains(si.LessonTypeId)
                                 && tIds.Contains(si.TeacherId!.Value)
                                 && cIds.Contains(si.Group.CourseId))
                    .GroupBy(si => new { TeacherId = si.TeacherId!.Value, si.Group.CourseId })
                    .Select(g => new { g.Key.TeacherId, g.Key.CourseId, C = g.Count() })
                    .ToListAsync();

                var loadsToUpdate = await _db.TeacherCourseLoads
                    .Where(l => l.IsActive && tIds.Contains(l.TeacherId) && cIds.Contains(l.CourseId))
                    .ToListAsync();

                foreach (var l in loadsToUpdate)
                    l.ScheduledHours = counts.FirstOrDefault(c => c.TeacherId == l.TeacherId && c.CourseId == l.CourseId)?.C ?? 0;

                var inactive = await _db.TeacherCourseLoads
                    .Where(l => !l.IsActive && tIds.Contains(l.TeacherId) && cIds.Contains(l.CourseId))
                    .ToListAsync();
                foreach (var l in inactive) l.ScheduledHours = 0;
            }
        }

        await _db.SaveChangesAsync();
    }
}



