using System;
using System.Collections.Generic;
using System.Linq;
using BlazorWasmDotNet8AspNetCoreHosted.Server.Domain.Entities;
using BlazorWasmDotNet8AspNetCoreHosted.Server.Infrastructure;
using BlazorWasmDotNet8AspNetCoreHosted.Shared.DTOs;
using Microsoft.EntityFrameworkCore;

namespace BlazorWasmDotNet8AspNetCoreHosted.Server.Application;

public sealed class RulesService(AppDbContext db)
{
    public async Task<(List<string> errors, List<string> warnings)> ValidateUpsertAsync(UpsertScheduleItemRequest r)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        var group = await db.Groups.Include(g => g.Course).FirstOrDefaultAsync(x => x.Id == r.GroupId);
        if (group is null) errors.Add("Групу не знайдено.");

        var module = await db.Modules
            .Include(m => m.AllowedRooms)
            .Include(m => m.AllowedBuildings)
            .FirstOrDefaultAsync(x => x.Id == r.ModuleId);
        if (module is null) errors.Add("Модуль не знайдено.");

        var ltype = await db.LessonTypes.FirstOrDefaultAsync(x => x.Id == r.LessonTypeId);
        if (ltype is null) errors.Add("Тип заняття не знайдено.");

        var requiresRoom = ltype?.RequiresRoom ?? true;
        var requiresTeacher = ltype?.RequiresTeacher ?? true;
        var blocksRoom = ltype?.BlocksRoom ?? true;
        var blocksTeacher = ltype?.BlocksTeacher ?? true;

        Room? room = null;
        if (requiresRoom)
        {
            if (r.RoomId is int rid)
            {
                room = await db.Rooms.Include(x => x.Building)
                    .FirstOrDefaultAsync(x => x.Id == rid);
                if (room is null) errors.Add("Аудиторію не знайдено.");
            }
            else errors.Add("Для цього заняття потрібно обрати аудиторію.");
        }

        if (errors.Count > 0) return (errors, warnings);

        var start = TimeOnly.Parse(r.TimeStart);
        var end = TimeOnly.Parse(r.TimeEnd);
        if (end <= start) errors.Add("Час завершення має бути більшим за час початку.");

        if (group is not null)
        {
            var hasCourseSlots = await db.TimeSlots.AnyAsync(s => s.CourseId == group.CourseId && s.IsActive);
            var effectiveSlots = await db.TimeSlots.AsNoTracking()
                .Where(s => s.IsActive && (hasCourseSlots ? s.CourseId == group.CourseId : s.CourseId == null))
                .Select(s => new { s.Start, s.End })
                .ToListAsync();

            if (effectiveSlots.Count > 0 && !effectiveSlots.Any(s => s.Start == start && s.End == end))
                errors.Add("Обраний часовий проміжок не входить до дозволених слотів.");
        }

        var dow = r.Date.ToDateTime(TimeOnly.MinValue).DayOfWeek;
        bool isWeekend = dow is DayOfWeek.Saturday or DayOfWeek.Sunday;
        var cal = await db.CalendarExceptions.AsNoTracking().FirstOrDefaultAsync(x => x.Date == r.Date);
        bool isWorking = !isWeekend || (cal?.IsWorkingDay == true);
        if (!isWorking && !r.OverrideNonWorkingDay)
            warnings.Add("Увага: заняття потрапляє на вихідний день.");

        if (requiresRoom)
        {
            if (room!.Capacity < group!.StudentsCount)
                errors.Add($"Аудиторія {room.Name} замала для групи {group.Name} ({room.Capacity} < {group.StudentsCount}).");

            var allowedBuildingIds = module!.AllowedBuildings.Select(b => b.BuildingId).ToList();
            if (allowedBuildingIds.Count > 0 && !allowedBuildingIds.Contains(room.BuildingId))
                errors.Add($"Корпус {room.Building.Name} не дозволений для цього модуля.");

            var allowedRoomIds = module.AllowedRooms.Select(ar => ar.RoomId).ToList();
            if (allowedRoomIds.Count > 0 && !allowedRoomIds.Contains(room.Id))
                errors.Add($"Аудиторія {room.Name} не входить до дозволених для цього модуля.");
        }

        var conflicts = await db.ScheduleItems
            .Where(x => x.Id != (r.Id ?? 0)
                        && x.Date == r.Date
                        && (
                            x.GroupId == r.GroupId
                            || (blocksRoom && r.RoomId != null && x.RoomId == r.RoomId)
                            || (blocksTeacher && r.TeacherId != null && x.TeacherId == r.TeacherId)
                        )
                        && x.StartTime < end && start < x.EndTime)
            .AnyAsync();
        if (conflicts)
            errors.Add("Знайдено конфлікт уже опублікованого розкладу.");

        if (requiresRoom && blocksRoom && r.RoomId is int)
        {
            var travel = await db.BuildingTravels.AsNoTracking()
                .ToDictionaryAsync(k => (k.FromBuildingId, k.ToBuildingId), v => v.Minutes);

            int TravelMinutes(int fromId, int toId)
            {
                if (fromId == toId) return 0;
                if (travel.TryGetValue((fromId, toId), out var m)) return m;
                if (travel.TryGetValue((toId, fromId), out m)) return m;
                return 10;
            }

            var adj = await db.ScheduleItems
                .Include(x => x.Room).ThenInclude(rm => rm!.Building)
                .Where(x => x.Date == r.Date
                            && (x.GroupId == r.GroupId || (r.TeacherId != null && x.TeacherId == r.TeacherId)))
                .ToListAsync();

            foreach (var a in adj)
            {
                if (a.Room is null) continue;
                var need = TravelMinutes(a.Room.BuildingId, room!.BuildingId);
                var gapBefore = (start.ToTimeSpan() - a.EndTime.ToTimeSpan()).TotalMinutes;
                var gapAfter = (a.StartTime.ToTimeSpan() - end.ToTimeSpan()).TotalMinutes;
                if (a.EndTime <= start && gapBefore < need)
                    errors.Add("Замало часу на перехід (попереднє заняття).");
                if (end <= a.StartTime && gapAfter < need)
                    errors.Add("Замало часу на перехід (наступне заняття).");
            }
        }

        if (requiresTeacher && r.TeacherId is int tWin)
        {
            var dayEnum = r.Date.ToDateTime(TimeOnly.MinValue).DayOfWeek;
            var windows = await db.TeacherWorkingHours
                .Where(w => w.TeacherId == tWin && w.DayOfWeek == dayEnum)
                .Select(w => new { w.Start, w.End })
                .ToListAsync();

            if (windows.Count > 0)
            {
                bool fits = windows.Any(w => w.Start <= start && end <= w.End);
                if (!fits) warnings.Add("Заняття виходить за межі робочих годин викладача.");
            }
        }

        if (ltype?.CountInLoad == true && r.TeacherId is int tId && group is not null)
        {
            var load = await db.TeacherCourseLoads
                .FirstOrDefaultAsync(l => l.TeacherId == tId && l.CourseId == group.CourseId && l.IsActive);

            if (load is not null && load.ScheduledHours >= load.TargetHours)
                warnings.Add("Навантаження викладача вже виконано.");
        }

        return (errors, warnings);
    }

    public sealed record DraftValidationResult(
        List<string> Errors,
        List<string> Warnings,
        DraftValidationReportDto Report
    );

    public async Task<DraftValidationResult> ValidateDraftAsync(DraftUpsertRequest r)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var issues = new List<DraftValidationIssueDto>();

        void AddError(string code, string title, string description)
        {
            errors.Add(description);
            issues.Add(new DraftValidationIssueDto("error", code, title, description));
        }

        void AddWarning(string code, string title, string description)
        {
            warnings.Add(description);
            issues.Add(new DraftValidationIssueDto("warning", code, title, description));
        }

        var group = await db.Groups.Include(g => g.Course).FirstOrDefaultAsync(x => x.Id == r.GroupId);
        if (group is null)
            AddError("group-not-found", "Групу не знайдено", $"Група з ідентифікатором {r.GroupId} відсутня у базі даних.");

        var module = await db.Modules
            .Include(m => m.AllowedRooms)
            .Include(m => m.AllowedBuildings)
            .FirstOrDefaultAsync(x => x.Id == r.ModuleId);
        if (module is null)
            AddError("module-not-found", "Модуль не знайдено", $"Модуль з ідентифікатором {r.ModuleId} відсутній у базі.");

        var ltype = await db.LessonTypes.FirstOrDefaultAsync(x => x.Id == r.LessonTypeId);
        if (ltype is null)
            AddError("lesson-type-not-found", "Тип заняття не знайдено", $"Тип заняття {r.LessonTypeId} не існує.");

        var requiresRoom = ltype?.RequiresRoom ?? true;
        var requiresTeacher = ltype?.RequiresTeacher ?? true;
        var blocksRoom = ltype?.BlocksRoom ?? true;
        var blocksTeacher = ltype?.BlocksTeacher ?? true;

        Room? room = null;
        if (requiresRoom)
        {
            if (r.RoomId is int rid)
            {
                room = await db.Rooms.Include(x => x.Building)
                    .FirstOrDefaultAsync(x => x.Id == rid);
                if (room is null)
                    AddError("room-not-found", "Аудиторію не знайдено", $"Аудиторія з ідентифікатором {rid} відсутня.");
            }
            else
            {
                AddError("room-required", "Потрібна аудиторія", "Цей тип заняття потребує вибраної аудиторії.");
            }
        }

        if (errors.Count > 0)
            return new DraftValidationResult(errors, warnings, new DraftValidationReportDto(DateTimeOffset.UtcNow, issues));

        var start = TimeOnly.Parse(r.TimeStart);
        var end = TimeOnly.Parse(r.TimeEnd);
        if (end <= start)
            AddError("time-window-invalid", "Некоректний час", $"Час завершення {r.TimeEnd} не може бути меншим або рівним часу початку {r.TimeStart}.");

        if (group is not null)
        {
            var hasCourseSlots = await db.TimeSlots.AnyAsync(s => s.CourseId == group.CourseId && s.IsActive);
            var effectiveSlots = await db.TimeSlots.AsNoTracking()
                .Where(s => s.IsActive && (hasCourseSlots ? s.CourseId == group.CourseId : s.CourseId == null))
                .Select(s => new { s.Start, s.End })
                .ToListAsync();

            if (effectiveSlots.Count > 0 && !effectiveSlots.Any(s => s.Start == start && s.End == end))
                AddError("slot-not-allowed", "Недозволений слот", $"Проміжок {r.TimeStart}-{r.TimeEnd} відсутній серед дозволених для курсу {group.Course.Name}.");
        }

        var dow = r.Date.ToDateTime(TimeOnly.MinValue).DayOfWeek;
        bool isWeekend = dow is DayOfWeek.Saturday or DayOfWeek.Sunday;
        var cal = await db.CalendarExceptions.AsNoTracking().FirstOrDefaultAsync(x => x.Date == r.Date);
        bool isWorking = !isWeekend || (cal?.IsWorkingDay == true);
        if (!isWorking && !r.OverrideNonWorkingDay)
        {
            var reason = cal is not null ? cal.Name : (isWeekend ? "вихідний день" : "неробочий день");
            AddWarning("non-working-day", "Заняття у вихідний", $"Дата {r.Date:yyyy-MM-dd} позначена як {reason}. Для публікації потрібно примусове збереження.");
        }

        if (requiresRoom)
        {
            if (room!.Capacity < group!.StudentsCount)
                AddError("room-capacity", "Недостатня місткість", $"Аудиторія {room.Name} вміщує {room.Capacity} осіб, у групі {group.Name} {group.StudentsCount} студентів.");

            var allowedBuildingIds = module!.AllowedBuildings.Select(b => b.BuildingId).ToList();
            if (allowedBuildingIds.Count > 0 && !allowedBuildingIds.Contains(room.BuildingId))
                AddError("building-not-allowed", "Корпус заборонено", $"Модуль {module.Title} заборонено проводити у корпусі {room.Building.Name}.");

            var allowedRoomIds = module.AllowedRooms.Select(ar => ar.RoomId).ToList();
            if (allowedRoomIds.Count > 0 && !allowedRoomIds.Contains(room.Id))
                AddError("room-not-allowed", "Аудиторія заборонена", $"Аудиторія {room.Name} не входить до списку дозволених для модуля {module.Title}.");
        }

        var officialConflicts = await db.ScheduleItems
            .Include(x => x.Group)
            .Include(x => x.Module)
            .Include(x => x.Teacher)
            .Include(x => x.Room).ThenInclude(rm => rm!.Building)
            .Where(x => x.Id != (r.Id ?? 0)
                        && x.Date == r.Date
                        && x.StartTime < end && start < x.EndTime
                        && (
                            x.GroupId == r.GroupId
                            || (blocksTeacher && r.TeacherId != null && x.TeacherId == r.TeacherId)
                            || (blocksRoom && r.RoomId != null && x.RoomId == r.RoomId)
                        ))
            .ToListAsync();

        foreach (var c in officialConflicts)
        {
            var slot = $"{c.StartTime:HH\\:mm}-{c.EndTime:HH\\:mm}";
            if (c.GroupId == r.GroupId)
                AddError("conflict-official-group", "Група зайнята", $"Група {c.Group.Name} має опубліковане заняття {c.Module.Title} у слоті {slot}.");
            if (blocksTeacher && r.TeacherId != null && c.TeacherId == r.TeacherId)
            {
                var teacherName = c.Teacher?.FullName ?? $"ID {r.TeacherId}";
                AddError("conflict-official-teacher", "Викладач зайнятий", $"Викладач {teacherName} проводить заняття {c.Module.Title} для групи {c.Group.Name} у слоті {slot}.");
            }
            if (blocksRoom && r.RoomId != null && c.RoomId == r.RoomId && c.Room is not null)
            {
                var buildingName = c.Room.Building?.Name is { Length: > 0 } b ? $" ({b})" : string.Empty;
                AddError("conflict-official-room", "Аудиторія зайнята", $"Аудиторія {c.Room.Name}{buildingName} використовується для заняття {c.Module.Title} у слоті {slot}.");
            }
        }

        var draftConflicts = await db.TeacherDraftItems
            .Include(x => x.Group)
            .Include(x => x.Module)
            .Include(x => x.Teacher)
            .Include(x => x.Room).ThenInclude(rm => rm!.Building)
            .Where(x => x.Id != (r.Id ?? 0)
                        && x.Date == r.Date
                        && x.Status == DraftStatus.Draft
                        && x.StartTime < end && start < x.EndTime
                        && (
                            x.GroupId == r.GroupId
                            || (blocksTeacher && r.TeacherId != null && x.TeacherId == r.TeacherId)
                            || (blocksRoom && r.RoomId != null && x.RoomId == r.RoomId)
                        ))
            .ToListAsync();

        foreach (var c in draftConflicts)
        {
            var slot = $"{c.StartTime:HH\\:mm}-{c.EndTime:HH\\:mm}";
            if (c.GroupId == r.GroupId)
                AddError("conflict-draft-group", "Група вже має чернетку", $"Група {c.Group.Name} вже має чернетку {c.Module.Title} у слоті {slot}.");
            if (blocksTeacher && r.TeacherId != null && c.TeacherId == r.TeacherId)
            {
                var teacherName = c.Teacher?.FullName ?? $"ID {r.TeacherId}";
                AddError("conflict-draft-teacher", "Викладач зайнятий у чернетці", $"Викладач {teacherName} уже запланований на чернетку {c.Module.Title} у слоті {slot}.");
            }
            if (blocksRoom && r.RoomId != null && c.RoomId == r.RoomId && c.Room is not null)
            {
                var buildingName = c.Room.Building?.Name is { Length: > 0 } b ? $" ({b})" : string.Empty;
                AddError("conflict-draft-room", "Аудиторія зайнята у чернетці", $"Аудиторія {c.Room.Name}{buildingName} вже використовується для чернетки {c.Module.Title} у слоті {slot}.");
            }
        }

        if (requiresRoom && blocksRoom && room is not null)
        {
            var travelMap = await db.BuildingTravels.AsNoTracking()
                .ToDictionaryAsync(k => (k.FromBuildingId, k.ToBuildingId), v => v.Minutes);

            int TravelMinutes(int fromId, int toId)
            {
                if (fromId == toId) return 0;
                if (travelMap.TryGetValue((fromId, toId), out var m)) return m;
                if (travelMap.TryGetValue((toId, fromId), out m)) return m;
                return 10;
            }

            void CheckTravel(TimeOnly otherStart, TimeOnly otherEnd, Room? otherRoom, string scope, string label)
            {
                if (otherRoom is null) return;
                var need = TravelMinutes(otherRoom.BuildingId, room.BuildingId);
                var gapBefore = (start.ToTimeSpan() - otherEnd.ToTimeSpan()).TotalMinutes;
                var gapAfter = (otherStart.ToTimeSpan() - end.ToTimeSpan()).TotalMinutes;
                if (otherEnd <= start && gapBefore < need)
                    AddError($"travel-{scope}-before", "Недостатньо часу на перехід", $"{label} завершується о {otherEnd:HH\\:mm} в аудиторії {otherRoom.Name}. Для переходу потрібно {need} хвилин, доступно лише {gapBefore:N0} хв.");
                if (end <= otherStart && gapAfter < need)
                    AddError($"travel-{scope}-after", "Недостатньо часу на перехід", $"{label} починається о {otherStart:HH\\:mm} в аудиторії {otherRoom.Name}. Для переходу потрібно {need} хвилин, доступно лише {gapAfter:N0} хв.");
            }

            var relatedOfficial = await db.ScheduleItems
                .Include(x => x.Room)
                .Where(x => x.Date == r.Date && (x.GroupId == r.GroupId || (r.TeacherId != null && x.TeacherId == r.TeacherId)))
                .ToListAsync();
            foreach (var a in relatedOfficial)
                CheckTravel(a.StartTime, a.EndTime, a.Room, "official", "Опубліковане заняття");

            var relatedDrafts = await db.TeacherDraftItems
                .Include(x => x.Room)
                .Where(x => x.Id != (r.Id ?? 0)
                            && x.Status == DraftStatus.Draft
                            && x.Date == r.Date
                            && (x.GroupId == r.GroupId || (r.TeacherId != null && x.TeacherId == r.TeacherId)))
                .ToListAsync();
            foreach (var a in relatedDrafts)
                CheckTravel(a.StartTime, a.EndTime, a.Room, "draft", "Чернетка");
        }

        if (requiresTeacher && r.TeacherId is int tWin)
        {
            var dayEnum = r.Date.ToDateTime(TimeOnly.MinValue).DayOfWeek;
            var windows = await db.TeacherWorkingHours
                .Where(w => w.TeacherId == tWin && w.DayOfWeek == dayEnum)
                .Select(w => new { w.Start, w.End })
                .ToListAsync();

            if (windows.Count > 0)
            {
                bool fits = windows.Any(w => w.Start <= start && end <= w.End);
                if (!fits)
                    AddWarning("teacher-working-hours", "Поза робочими годинами", $"Інтервал {r.TimeStart}-{r.TimeEnd} виходить за межі робочих годин викладача для {dayEnum}.");
            }
        }

        if (ltype?.CountInLoad == true && r.TeacherId is int tId && group is not null)
        {
            var load = await db.TeacherCourseLoads
                .FirstOrDefaultAsync(l => l.TeacherId == tId && l.CourseId == group.CourseId && l.IsActive);

            if (load is not null && load.ScheduledHours >= load.TargetHours)
                AddWarning("teacher-load-exceeded", "Перевищено навантаження", $"Викладач виконав {load.ScheduledHours} із {load.TargetHours} запланованих занять.");
        }

        var report = new DraftValidationReportDto(DateTimeOffset.UtcNow, issues);
        return new DraftValidationResult(errors, warnings, report);
    }
}





