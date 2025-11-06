using System.Collections.Generic;

namespace BlazorWasmDotNet8AspNetCoreHosted.Shared.DTOs;

/// <summary>
/// DTO для опису тем модуля під час редагування.
/// </summary>
public record ModuleTopicDto(
    int? Id,
    int ModuleId,
    int Order,
    string TopicCode,
    int LessonTypeId,
    int TotalHours,
    int AuditoriumHours,
    int SelfStudyHours
);

/// <summary>
/// DTO для перегляду тем модуля разом з інформацією про групи.
/// </summary>
public record ModuleTopicViewDto(
    int Id,
    int ModuleId,
    int Order,
    string TopicCode,
    int LessonTypeId,
    string LessonTypeCode,
    string LessonTypeName,
    int TotalHours,
    int AuditoriumHours,
    int SelfStudyHours,
    List<string> PlannedGroups,
    List<string> CompletedGroups
);
