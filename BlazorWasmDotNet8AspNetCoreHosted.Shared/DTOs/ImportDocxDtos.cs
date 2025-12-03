using System.Collections.Generic;

namespace BlazorWasmDotNet8AspNetCoreHosted.Shared.DTOs;

public record DocxImportTopicDto(
    string ModuleCode,
    string TopicCode,
    string LessonTypeName,
    int TotalHours,
    int AuditoriumHours,
    int SelfStudyHours,
    int Order
);

public record DocxImportModuleDto(
    string Code,
    string Title,
    decimal Credits,
    List<DocxImportTopicDto> Topics
);

public record DocxImportResultDto(
    string CourseName,
    int? CourseId,
    bool CourseFound,
    List<DocxImportModuleDto> Modules,
    List<string> Warnings,
    string? Error
);
