using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlazorWasmDotNet8AspNetCoreHosted.Server.Domain.Entities;




// Сутність часового слоту для бази даних
public class TimeSlot
{
    public int Id { get; set; }

    public int? CourseId { get; set; }
    [ForeignKey(nameof(CourseId))]
    public Course? Course { get; set; }

    public TimeOnly Start { get; set; }     
    public TimeOnly End { get; set; }       

    public int SortOrder { get; set; } = 0; 
    public bool IsActive { get; set; } = true;
}
