using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BlazorWasmDotNet8AspNetCoreHosted.Server.Infrastructure;
using BlazorWasmDotNet8AspNetCoreHosted.Server.Domain.Entities;
using BlazorWasmDotNet8AspNetCoreHosted.Shared.DTOs;

namespace BlazorWasmDotNet8AspNetCoreHosted.Server.Controllers.Admin;

[ApiController]
[Route("api/admin/courses")]
public class AdminCoursesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<CourseEditDto>> List()
        => await db.Courses.AsNoTracking()
            .OrderBy(c => c.Id)
            .Select(c => new CourseEditDto(c.Id, c.Name, c.DurationWeeks))
            .ToListAsync();

    [HttpPost("upsert")]
    public async Task<ActionResult<int>> Upsert(CourseEditDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { message = "Назва є обовʼязковою" });

        if (dto.Id is int id && id > 0)
        {
            var c = await db.Courses.FindAsync(id) ?? throw new ArgumentException("Курс не знайдено");
            c.Name = dto.Name;
            c.DurationWeeks = dto.DurationWeeks;
            await db.SaveChangesAsync();
            return Ok(c.Id);
        }
        else
        {
            var c = new Course { Name = dto.Name, DurationWeeks = dto.DurationWeeks };
            db.Courses.Add(c);
            await db.SaveChangesAsync();
            return Ok(c.Id);
        }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, [FromQuery] bool force = false)
    {
        
        var used = await db.Groups.AnyAsync(g => g.CourseId == id)
                   || await db.Modules.AnyAsync(m => m.CourseId == id)
                   || await db.ModulePlans.AnyAsync(p => p.CourseId == id)
                   || await db.TeacherCourseLoads.AnyAsync(l => l.CourseId == id)
                   || await db.ScheduleItems.AnyAsync(s => s.Group.CourseId == id);

        if (used && !force)
            return Conflict(new { message = "Курс використовується групами/модулями/розкладом" });

        if (used && force)
        {
            var groupIds = await db.Groups.Where(g => g.CourseId == id).Select(g => g.Id).ToListAsync();
            var moduleIds = await db.Modules.Where(m => m.CourseId == id).Select(m => m.Id).ToListAsync();

            
            await db.ScheduleItems.Where(s => s.Group.CourseId == id).ExecuteDeleteAsync();
            await db.ModulePlans.Where(p => p.CourseId == id).ExecuteDeleteAsync();
            await db.TeacherCourseLoads.Where(l => l.CourseId == id).ExecuteDeleteAsync();

            
            if (moduleIds.Count > 0)
            {
                await db.TeacherModules.Where(tm => moduleIds.Contains(tm.ModuleId)).ExecuteDeleteAsync();
                await db.ModuleRooms.Where(mr => moduleIds.Contains(mr.ModuleId)).ExecuteDeleteAsync();
                await db.ModuleBuildings.Where(mb => moduleIds.Contains(mb.ModuleId)).ExecuteDeleteAsync();
            }

            
            await db.Modules.Where(m => m.CourseId == id).ExecuteDeleteAsync();
            await db.Groups.Where(g => g.CourseId == id).ExecuteDeleteAsync();
        }

        var rows = await db.Courses.Where(c => c.Id == id).ExecuteDeleteAsync();
        if (rows == 0) return NotFound();
        return NoContent();
    }
}
