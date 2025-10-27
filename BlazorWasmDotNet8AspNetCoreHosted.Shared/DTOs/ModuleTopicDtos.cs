using System.Collections.Generic;

namespace BlazorWasmDotNet8AspNetCoreHosted.Shared.DTOs;

public record ModuleTopicDto(
    int? Id,
    int ModuleId,
    int Order,
    int BlockNumber,
    string BlockTitle,
    int LessonNumber,
    int QuestionNumber,
    int LessonTypeId,
    int TotalHours,
    int AuditoriumHours,
    int SelfStudyHours,
    string Title
);

public record ModuleTopicViewDto(
    int Id,
    int ModuleId,
    int Order,
    int BlockNumber,
    string BlockTitle,
    int LessonNumber,
    int QuestionNumber,
    string TopicCode,
    int LessonTypeId,
    string LessonTypeCode,
    string LessonTypeName,
    int TotalHours,
    int AuditoriumHours,
    int SelfStudyHours,
    string Title,
    List<string> PlannedGroups,
    List<string> CompletedGroups
);

