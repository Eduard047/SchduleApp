using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BlazorWasmDotNet8AspNetCoreHosted.Server.Infrastructure;
using BlazorWasmDotNet8AspNetCoreHosted.Shared.DTOs;

[ApiController]
[Route("api/[controller]")]
public class MetaController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<MetaResponseDto> Get([FromQuery] DateOnly? weekStart)
    {
        var courses = await db.Courses.AsNoTracking().Select(x => new LookupDto(x.Id, x.Name)).ToListAsync();
        var groups = await db.Groups.AsNoTracking().Select(x => new LookupDto(x.Id, x.Name)).ToListAsync();
        var teachers = await db.Teachers.AsNoTracking().Select(x => new LookupDto(x.Id, x.FullName)).ToListAsync();
        var rooms = await db.Rooms.AsNoTracking().Select(x => new LookupDto(x.Id, x.Name)).ToListAsync();
        var buildings = await db.Buildings.AsNoTracking().Select(x => new LookupDto(x.Id, x.Name)).ToListAsync();
        var modules = await db.Modules
            .AsNoTracking()
            .Select(x => new ModuleMetaDto(
                x.Id,
                x.Code,
                x.Title,
                x.CourseId,
                x.Course.Name
            ))
            .ToListAsync();

        var lessonTypes = await db.LessonTypes.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new IdCodeNameDto(x.Id, x.Code, x.Name)
            {
                RequiresRoom = x.RequiresRoom
            }).ToListAsync();
        var lunches = await db.LunchConfigs
            .Select(x => new LunchConfigDto(x.CourseId, x.Start.ToString("HH:mm"), x.End.ToString("HH:mm")))
            .ToListAsync();

        var cal = new List<CalendarExceptionDto>();
        if (weekStart is DateOnly s)
        {
            var end = s.AddDays(7);
            var xs = await db.CalendarExceptions.AsNoTracking().Where(x => x.Date >= s && x.Date < end).ToListAsync();
            cal = xs.Select(x => new CalendarExceptionDto(x.Date.ToString("yyyy-MM-dd"), x.IsWorkingDay, x.Name)).ToList();
        }

        return new MetaResponseDto(courses, groups, teachers, rooms, buildings, lessonTypes, lunches)
        {
            Modules = modules,
            Calendar = cal
        };
    }
}


