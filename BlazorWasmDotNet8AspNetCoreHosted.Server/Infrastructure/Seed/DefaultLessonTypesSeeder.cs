using BlazorWasmDotNet8AspNetCoreHosted.Server.Domain.Entities;
using BlazorWasmDotNet8AspNetCoreHosted.Server.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorWasmDotNet8AspNetCoreHosted.Server.Infrastructure.Seed;

public static class DefaultLessonTypesSeeder
{
    private sealed record SeedLessonType(
        string Code,
        string Name,
        string CssKey,
        bool IsActive,
        bool RequiresRoom,
        bool RequiresTeacher,
        bool BlocksRoom,
        bool BlocksTeacher,
        bool CountInPlan,
        bool CountInLoad,
        bool PreferredFirstInWeek);

    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var defaults = new[]
        {
            new SeedLessonType("BREAK", "Перерва", "brk", true, false, false, false, false, false, false, false),
            new SeedLessonType("CANCELED", "Скасовано", "can", true, false, false, false, false, false, false, false),
            new SeedLessonType("RESCHEDULED", "Перенесено", "res", true, false, false, false, false, false, false, false),
        };

        var existing = await db.LessonTypes.ToListAsync();
        var changed = false;

        foreach (var d in defaults)
        {
            var entity = existing.FirstOrDefault(x => string.Equals(x.Code, d.Code, StringComparison.OrdinalIgnoreCase));
            if (entity is null)
            {
                db.LessonTypes.Add(new LessonTypeRef
                {
                    Code = d.Code,
                    Name = d.Name,
                    CssKey = d.CssKey,
                    IsActive = d.IsActive,
                    RequiresRoom = d.RequiresRoom,
                    RequiresTeacher = d.RequiresTeacher,
                    BlocksRoom = d.BlocksRoom,
                    BlocksTeacher = d.BlocksTeacher,
                    CountInPlan = d.CountInPlan,
                    CountInLoad = d.CountInLoad,
                    PreferredFirstInWeek = d.PreferredFirstInWeek
                });
                changed = true;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(entity.Name))
                {
                    entity.Name = d.Name;
                    changed = true;
                }
                if (string.IsNullOrWhiteSpace(entity.CssKey))
                {
                    entity.CssKey = d.CssKey;
                    changed = true;
                }
            }
        }

        if (changed)
        {
            await db.SaveChangesAsync();
        }
    }
}
