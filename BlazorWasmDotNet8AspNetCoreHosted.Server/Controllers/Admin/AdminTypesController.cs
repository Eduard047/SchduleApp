using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BlazorWasmDotNet8AspNetCoreHosted.Server.Controllers.Infrastructure;
using BlazorWasmDotNet8AspNetCoreHosted.Server.Infrastructure;
using BlazorWasmDotNet8AspNetCoreHosted.Server.Domain.Entities;
using BlazorWasmDotNet8AspNetCoreHosted.Shared.DTOs;

namespace BlazorWasmDotNet8AspNetCoreHosted.Server.Controllers.Admin;

[ApiController]
[Route("api/admin/types")]
// Контролер адміністратора для типів занять
public class AdminTypesController(AppDbContext db) : ControllerBase
{
    
    private sealed record PaletteItem(string Name, string Hex);

    
    private static readonly IReadOnlyDictionary<string, PaletteItem> Palette =
        new Dictionary<string, PaletteItem>
        {
            ["lec"] = new("Лекція (блакитний)", "#E9F3FF"),
            ["sem"] = new("Семінар (зелений)", "#EAFAF0"),
            ["lab"] = new("Лабораторне (жовтий)", "#FFF7DF"),
            ["prc"] = new("Практика (фіолет.)", "#EFE5FF"),
            ["brk"] = new("Перерва (сірий)", "#F1F5F9"),
            ["can"] = new("Скасовано (рожев.)", "#FFE9E9"),
            ["c7"] = new("Небесний", "#E7F4FF"),
            ["c8"] = new("Мʼята", "#E6FFF5"),
            ["c9"] = new("Лимон", "#FFFBE6"),
            ["c10"] = new("Лаванда", "#F3E8FF"),
        };

    
    [HttpGet("lesson/palette")]
    public async Task<IReadOnlyList<LessonColorDto>> LessonPalette()
    {
        var used = await db.LessonTypes
            .Where(x => x.CssKey != null && x.CssKey != "")
            .Select(x => new { x.Id, CssKey = x.CssKey! })
            .ToListAsync();

        var usedMap = used.GroupBy(x => x.CssKey).ToDictionary(g => g.Key, g => g.First().Id);

        return Palette.Select(p =>
        {
            usedMap.TryGetValue(p.Key, out var usedById);
            return new LessonColorDto(
                Key: p.Key,
                Name: p.Value.Name,
                Hex: p.Value.Hex,
                IsUsed: usedById != 0,
                UsedByTypeId: usedById == 0 ? null : usedById
            );
        }).ToList();
    }

    
    [HttpGet("lesson")]
    public async Task<IReadOnlyList<LessonTypeEditDto>> LessonList() =>
        await db.LessonTypes.AsNoTracking()
            .OrderBy(x => x.Id)
            .Select(x => new LessonTypeEditDto
            {
                Id = x.Id,
                Code = x.Code,
                Name = x.Name,
                IsActive = x.IsActive,
                CssKey = x.CssKey,
                RequiresRoom = x.RequiresRoom,
                RequiresTeacher = x.RequiresTeacher,
                BlocksRoom = x.BlocksRoom,
                BlocksTeacher = x.BlocksTeacher,
                CountInPlan = x.CountInPlan,
                CountInLoad = x.CountInLoad,
                PreferredFirstInWeek = x.PreferredFirstInWeek
            })
            .ToListAsync();

    [HttpPost("lesson/upsert")]
    public async Task<ActionResult<int>> LessonUpsert([FromBody] LessonTypeEditDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Code) || string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { message = "Код та Назва є обовʼязковими" });

        LessonTypeRef e;
        if (dto.Id is int id && id > 0)
        {
            e = await db.LessonTypes.FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new ArgumentException("Тип заняття не знайдено");
        }
        else
        {
            e = new LessonTypeRef();
            db.LessonTypes.Add(e);
        }

        e.Code = dto.Code.Trim();
        e.Name = dto.Name.Trim();
        e.IsActive = dto.IsActive;

        
        var newKey = string.IsNullOrWhiteSpace(dto.CssKey) ? null : dto.CssKey.Trim();

        if (newKey != null)
        {
            if (!Palette.ContainsKey(newKey))
                return BadRequest(new { message = $"Недопустимий CSS-ключ '{newKey}'. Оберіть один із фіксованої палітри." });

            var takenBy = await db.LessonTypes
                .Where(x => x.CssKey == newKey)
                .Select(x => x.Id)
                .FirstOrDefaultAsync();

            if (takenBy != 0 && takenBy != e.Id)
                return Conflict(new { message = $"Колір '{newKey}' уже використовується типом #{takenBy}." });
        }

        e.CssKey = newKey;

        e.RequiresRoom = dto.RequiresRoom;
        e.RequiresTeacher = dto.RequiresTeacher;
        e.BlocksRoom = dto.BlocksRoom;
        e.BlocksTeacher = dto.BlocksTeacher;
        e.CountInPlan = dto.CountInPlan;
        e.CountInLoad = dto.CountInLoad;
        e.PreferredFirstInWeek = dto.PreferredFirstInWeek;

        await db.SaveChangesAsync();
        return Ok(e.Id);
    }

    [HttpDelete("lesson/{id:int}")]
    [RequireDeletionConfirmation("тип заняття")]
    public async Task<IActionResult> LessonDelete(int id)
    {
        var used = await db.ScheduleItems.AnyAsync(s => s.LessonTypeId == id);
        if (used) return Conflict(new { message = "Тип заняття використовується у розкладі" });
        var rows = await db.LessonTypes.Where(x => x.Id == id).ExecuteDeleteAsync();
        if (rows == 0) return NotFound();
        return NoContent();
    }



}
