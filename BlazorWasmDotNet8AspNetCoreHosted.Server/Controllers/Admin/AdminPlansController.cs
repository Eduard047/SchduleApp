using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BlazorWasmDotNet8AspNetCoreHosted.Server.Infrastructure;
using BlazorWasmDotNet8AspNetCoreHosted.Shared.DTOs;
using BlazorWasmDotNet8AspNetCoreHosted.Server.Domain.Entities;

namespace BlazorWasmDotNet8AspNetCoreHosted.Server.Controllers.Admin;

[ApiController]
[Route("api/admin/plans")]
public sealed class AdminPlansController : ControllerBase
{
    private readonly AppDbContext _db;
    public AdminPlansController(AppDbContext db) => _db = db;

    
    
    [HttpGet("module/{moduleId:int}")]
    public async Task<ActionResult<List<CourseModulePlanDto>>> GetByModule(int moduleId)
    {
        
        var courseId = await _db.Modules
            .Where(m => m.Id == moduleId)
            .Select(m => (int?)m.CourseId)
            .SingleOrDefaultAsync();

        if (courseId is null)
            return NotFound(new { message = "Модуль не знайдено" });

        
        var lessonTypes = await _db.LessonTypes
            .Select(t => new { t.Id, t.Code, t.CountInPlan })
            .ToListAsync();

        var excludePlanIds = lessonTypes
            .Where(t => !t.CountInPlan && !string.Equals(t.Code, "CANCELED", System.StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Id)
            .ToHashSet();

        

        
        var scheduled = await _db.ScheduleItems
            .Where(si => si.ModuleId == moduleId
                         && si.Group.CourseId == courseId.Value
                         && !excludePlanIds.Contains(si.LessonTypeId))
            .CountAsync();

        
        var plan = await _db.ModulePlans.AsNoTracking()
            .FirstOrDefaultAsync(p => p.CourseId == courseId.Value && p.ModuleId == moduleId);

        var row = new CourseModulePlanDto(
            CourseId: courseId.Value,
            ModuleId: moduleId,
            TargetHours: plan?.TargetHours ?? 0,
            ScheduledHours: scheduled,
            IsActive: plan?.IsActive ?? false
        );

        return Ok(new List<CourseModulePlanDto> { row });
    }

    
    [HttpPost("module/{moduleId:int}/upsert")]
    public async Task<IActionResult> Upsert(int moduleId, [FromBody] List<SaveCourseModulePlanDto> items)
    {
        var courseId = await _db.Modules
            .Where(m => m.Id == moduleId)
            .Select(m => (int?)m.CourseId)
            .SingleOrDefaultAsync();

        if (courseId is null)
            return NotFound(new { message = "Модуль не знайдено" });

        var dto = items?.FirstOrDefault();
        if (dto is null)
            return BadRequest(new { message = "Порожній запит" });

        var plan = await _db.ModulePlans
            .FirstOrDefaultAsync(p => p.CourseId == courseId.Value && p.ModuleId == moduleId);

        if (plan is null)
        {
            _db.ModulePlans.Add(new ModulePlan
            {
                CourseId = courseId.Value,
                ModuleId = moduleId,
                TargetHours = dto.TargetHours,
                ScheduledHours = 0,
                IsActive = dto.IsActive
            });
        }
        else
        {
            plan.TargetHours = dto.TargetHours;
            plan.IsActive = dto.IsActive;
        }

        
        var moduleEntity = await _db.Modules.FirstOrDefaultAsync(m => m.Id == moduleId);
        if (moduleEntity is not null)
        {
            moduleEntity.Credits = Math.Round(dto.TargetHours / 30m, 2);
        }

        await _db.SaveChangesAsync();
        return NoContent();
    }
}
