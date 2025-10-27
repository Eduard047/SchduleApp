namespace BlazorWasmDotNet8AspNetCoreHosted.Shared.DTOs;

public record class TimeSlotDto
{
    public int Id { get; set; }
    public int? CourseId { get; set; }
    public int SortOrder { get; set; } = 0;
    public string Start { get; set; } = "08:30";
    public string End { get; set; } = "10:00";
    public bool IsActive { get; set; } = true;
}
