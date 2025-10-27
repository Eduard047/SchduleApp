using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BlazorWasmDotNet8AspNetCoreHosted.Server.Infrastructure;
using BlazorWasmDotNet8AspNetCoreHosted.Shared.DTOs;
using BlazorWasmDotNet8AspNetCoreHosted.Server.Domain.Entities;

namespace BlazorWasmDotNet8AspNetCoreHosted.Server.Controllers.Admin;

[ApiController]
[Route("api/admin/rooms")]
public class AdminRoomsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<RoomEditDto>> List()
        => await db.Rooms.AsNoTracking()
            .Select(r => new RoomEditDto(r.Id, r.Name, r.Capacity, r.BuildingId))
            .ToListAsync();

    [HttpPost("upsert")]
    public async Task<ActionResult<int>> Upsert(RoomEditDto dto)
    {
        _ = await db.Buildings.FindAsync(dto.BuildingId) ?? throw new ArgumentException("Корпус не знайдено");

        if (dto.Id is int id && id > 0)
        {
            var r = await db.Rooms.FindAsync(id) ?? throw new ArgumentException("Аудиторію не знайдено");
            r.Name = dto.Name; r.Capacity = dto.Capacity; r.BuildingId = dto.BuildingId;
            await db.SaveChangesAsync(); return Ok(r.Id);
        }
        else
        {
            var r = new Room { Name = dto.Name, Capacity = dto.Capacity, BuildingId = dto.BuildingId };
            db.Rooms.Add(r); await db.SaveChangesAsync(); return Ok(r.Id);
        }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, [FromQuery] bool force = false)
    {
        var exists = await db.Rooms.AnyAsync(r => r.Id == id);
        if (!exists) return NotFound();

        var used = await db.ScheduleItems.AnyAsync(x => x.RoomId == id);
        if (used && !force)
            return Conflict(new { message = "Аудиторія використовується у розкладі" });

        if (force)
        {
            var q = db.ScheduleItems.Where(x => x.RoomId == id);
            var affectedLoads = await q.Where(x => x.TeacherId != null)
                .Select(x => new { x.TeacherId, CourseId = x.Group.CourseId })
                .Distinct()
                .ToListAsync();

            await q.ExecuteDeleteAsync();
            await db.ModuleRooms.Where(x => x.RoomId == id).ExecuteDeleteAsync();

            
            if (affectedLoads.Count > 0)
            {
                var tIds = affectedLoads.Select(a => a.TeacherId!.Value).Distinct().ToList();
                var cIds = affectedLoads.Select(a => a.CourseId).Distinct().ToList();

                var counts = await db.ScheduleItems
                    .Include(si => si.Group)
                    .Where(si => si.TeacherId != null && tIds.Contains(si.TeacherId!.Value) && cIds.Contains(si.Group.CourseId))
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

        var rows = await db.Rooms.Where(x => x.Id == id).ExecuteDeleteAsync();
        if (rows == 0) return NotFound();
        return NoContent();
    }
}



