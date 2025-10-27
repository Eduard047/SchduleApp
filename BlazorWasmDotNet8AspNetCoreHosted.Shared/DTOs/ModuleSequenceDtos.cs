using System.Collections.Generic;
namespace BlazorWasmDotNet8AspNetCoreHosted.Shared.DTOs;

public record ModuleSequenceItemDto(int Id, int ModuleId, string ModuleCode, string ModuleTitle, int Order);

public record ModuleSequenceConfigDto(int CourseId, List<ModuleSequenceItemDto> MainSequence, List<int> FillerModuleIds);

public record ModuleSequenceSaveRequestDto(int CourseId, List<int> MainModuleIds, List<int> FillerModuleIds);
