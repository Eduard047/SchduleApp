using System;
using System.Collections.Generic;

// DTO для автогенерації розкладу
namespace BlazorWasmDotNet8AspNetCoreHosted.Shared.DTOs;

public enum WeekPreset
{
    MonFri,
    MonSat,
    MonSun
}

public record AutoGenRequest(
    DateOnly WeekStart,
    bool ClearExisting = true,
    int? CourseId = null,
    int? GroupId = null,
    bool AllowOnDaysOff = false,
    WeekPreset Days = WeekPreset.MonFri
);

public record AutoGenResult(
    int Created,
    int Skipped,
    List<string> Warnings
);
