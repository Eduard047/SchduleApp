using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BlazorWasmDotNet8AspNetCoreHosted.Server.Infrastructure;
using BlazorWasmDotNet8AspNetCoreHosted.Server.Domain.Entities;
using BlazorWasmDotNet8AspNetCoreHosted.Shared.DTOs;
using System.Globalization;

namespace BlazorWasmDotNet8AspNetCoreHosted.Server.Controllers.Admin;

[ApiController]
[Route("api/admin/config")]
public class AdminConfigController(AppDbContext db) : ControllerBase
{
    [HttpGet("lunch")]
    public async Task<IReadOnlyList<LunchConfigEditDto>> LunchList()
        => await db.LunchConfigs
            .Select(l => new LunchConfigEditDto(l.Id, l.CourseId, l.Start.ToString(@"HH\:mm"), l.End.ToString(@"HH\:mm")))
            .ToListAsync();

    [HttpPost("lunch/upsert")]
    public async Task<ActionResult<int>> LunchUpsert(LunchConfigEditDto dto)
    {
        if (dto.CourseId is int cid && await db.Courses.FindAsync(cid) is null)
            return BadRequest("Course not found");

        if (dto.Id is int id && id > 0)
        {
            var l = await db.LunchConfigs.FindAsync(id) ?? throw new ArgumentException("LunchConfig not found");
            l.CourseId = dto.CourseId;
            l.Start = ParseTime(dto.Start);
            l.End = ParseTime(dto.End);
            await db.SaveChangesAsync();
            return Ok(l.Id);
        }
        else
        {
            var l = new LunchConfig { CourseId = dto.CourseId, Start = ParseTime(dto.Start), End = ParseTime(dto.End) };
            db.LunchConfigs.Add(l); await db.SaveChangesAsync(); return Ok(l.Id);
        }
    }

    [HttpDelete("lunch/{id:int}")]
    public async Task<IActionResult> LunchDelete(int id)
    {
        var rows = await db.LunchConfigs.Where(x => x.Id == id).ExecuteDeleteAsync();
        if (rows == 0) return NotFound();
        return NoContent();
    }

    
    [HttpGet("calendar")]
    public async Task<IReadOnlyList<CalendarExceptionEditDto>> CalendarList()
        => await db.CalendarExceptions
            .OrderBy(x => x.Date)
            .Select(x => new CalendarExceptionEditDto(x.Id, x.Date.ToString("yyyy-MM-dd"), x.IsWorkingDay, x.Name))
            .ToListAsync();

    [HttpPost("calendar/upsert")]
    public async Task<ActionResult<int>> CalendarUpsert(CalendarExceptionEditDto dto)
    {
        var date = DateOnly.Parse(dto.Date);

        if (dto.Id is int id && id > 0)
        {
            var x = await db.CalendarExceptions.FindAsync(id) ?? throw new ArgumentException("CalendarException not found");
            x.Date = date; x.IsWorkingDay = dto.IsWorkingDay; x.Name = dto.Name;
            await db.SaveChangesAsync(); return Ok(x.Id);
        }
        else
        {
            var x = new CalendarException { Date = date, IsWorkingDay = dto.IsWorkingDay, Name = dto.Name };
            db.CalendarExceptions.Add(x); await db.SaveChangesAsync(); return Ok(x.Id);
        }
    }

    [HttpDelete("calendar/{id:int}")]
    public async Task<IActionResult> CalendarDelete(int id)
    {
        var rows = await db.CalendarExceptions.Where(x => x.Id == id).ExecuteDeleteAsync();
        if (rows == 0) return NotFound();
        return NoContent();
    }

    
    
    

    
    public sealed record BulkTimeSlotsSaveDto(int? CourseId, List<TimeSlotDto> Slots);
    public sealed record CloneRequest(int CourseId);

    private static TimeOnly ParseTime(string s)
    {
        if (TimeOnly.TryParseExact(s, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var v)) return v;
        return TimeOnly.Parse(s, CultureInfo.InvariantCulture);
    }

    
    [HttpGet("slots")]
    public async Task<IActionResult> GetEffectiveSlots([FromQuery] int? courseId)
    {
        bool useCourse = (courseId is int cid) && await db.TimeSlots.AnyAsync(s => s.CourseId == cid);

        
        var rows = await db.TimeSlots.AsNoTracking()
            .Where(s => useCourse ? s.CourseId == courseId : s.CourseId == null)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Start)
            .Select(s => new { s.Id, s.CourseId, s.Start, s.End, s.SortOrder, s.IsActive })
            .ToListAsync();

        var slots = rows.Select(s => new TimeSlotDto
        {
            Id = s.Id,
            CourseId = s.CourseId,
            Start = s.Start.ToString("HH:mm"),
            End = s.End.ToString("HH:mm"),
            SortOrder = s.SortOrder,
            IsActive = s.IsActive
        }).ToList();

        return Ok(new { courseId, usingCourseSpecific = useCourse, slots });
    }

    
    
    [HttpGet("slots/raw")]
    public async Task<IActionResult> GetRawSlots([FromQuery] int? courseId)
    {
        
        var sel = db.TimeSlots.AsNoTracking().Select(s => new
        {
            s.Id,
            s.CourseId,
            s.Start,
            s.End,
            s.SortOrder,
            s.IsActive
        });

        List<dynamic> courseRowsDyn = new();
        if (courseId is int cid)
        {
            courseRowsDyn = await sel.Where(s => s.CourseId == cid)
                                     .OrderBy(s => s.SortOrder).ThenBy(s => s.Start)
                                     .ToListAsync<dynamic>();
        }

        var globalRows = await sel.Where(s => s.CourseId == null)
                                  .OrderBy(s => s.SortOrder).ThenBy(s => s.Start)
                                  .ToListAsync();

        
        List<TimeSlotDto> course = courseRowsDyn.Select(s => new TimeSlotDto
        {
            Id = (int)s.Id,
            CourseId = (int?)s.CourseId,
            Start = ((TimeOnly)s.Start).ToString("HH:mm"),
            End = ((TimeOnly)s.End).ToString("HH:mm"),
            SortOrder = (int)s.SortOrder,
            IsActive = (bool)s.IsActive
        }).ToList();

        List<TimeSlotDto> global = globalRows.Select(s => new TimeSlotDto
        {
            Id = s.Id,
            CourseId = s.CourseId,
            Start = s.Start.ToString("HH:mm"),
            End = s.End.ToString("HH:mm"),
            SortOrder = s.SortOrder,
            IsActive = s.IsActive
        }).ToList();

        return Ok(new { course, global });
    }


    
    
    [HttpPost("slots/upsert-bulk")]
    public async Task<IActionResult> UpsertSlots([FromBody] BulkTimeSlotsSaveDto body)
    {
        var courseId = body?.CourseId;
        var rows = body?.Slots ?? new();

        if (courseId is int cid && await db.Courses.FindAsync(cid) is null)
            return BadRequest(new { message = "Course not found" });

        
        var norm = rows.Select((r, i) => new
        {
            Start = ParseTime(r.Start),
            End = ParseTime(r.End),
            IsActive = r.IsActive,
            Sort = r.SortOrder <= 0 ? i + 1 : r.SortOrder
        })
        .OrderBy(x => x.Sort).ThenBy(x => x.Start).ToList();

        
        for (int i = 0; i < norm.Count; i++)
        {
            var s = norm[i].Start;
            var e = norm[i].End;
            if (e <= s) return BadRequest(new { message = $"Slot #{i + 1}: End must be after Start" });
            if (i > 0)
            {
                var prev = norm[i - 1];
                if (s < prev.End) return BadRequest(new { message = $"Overlap between slot #{i} and #{i + 1}" });
            }
        }

        await using var tx = await db.Database.BeginTransactionAsync();
        await db.TimeSlots.Where(s => s.CourseId == courseId).ExecuteDeleteAsync();

        int sort = 1;
        foreach (var x in norm)
        {
            db.TimeSlots.Add(new TimeSlot
            {
                CourseId = courseId,
                Start = x.Start,
                End = x.End,
                SortOrder = sort++,
                IsActive = x.IsActive
            });
        }

        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Ok();
    }

    
    [HttpDelete("slots/clear")]
    public async Task<IActionResult> ClearSlots([FromQuery] int? courseId)
    {
        if (courseId is int cid && await db.Courses.FindAsync(cid) is null)
            return BadRequest(new { message = "Course not found" });

        var rows = await db.TimeSlots.Where(s => s.CourseId == courseId).ExecuteDeleteAsync();
        return rows > 0 ? NoContent() : NotFound();
    }

    
    [HttpPost("slots/clone-from-global")]
    public async Task<IActionResult> CloneFromGlobal([FromBody] CloneRequest r)
    {
        var course = await db.Courses.FindAsync(r.CourseId);
        if (course is null) return BadRequest(new { message = "Course not found" });

        var global = await db.TimeSlots.AsNoTracking()
            .Where(s => s.CourseId == null)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Start)
            .ToListAsync();

        await using var tx = await db.Database.BeginTransactionAsync();
        await db.TimeSlots.Where(s => s.CourseId == r.CourseId).ExecuteDeleteAsync();

        foreach (var s in global)
        {
            db.TimeSlots.Add(new TimeSlot
            {
                CourseId = r.CourseId,
                Start = s.Start,
                End = s.End,
                SortOrder = s.SortOrder,
                IsActive = s.IsActive
            });
        }

        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Ok();
    }
}
