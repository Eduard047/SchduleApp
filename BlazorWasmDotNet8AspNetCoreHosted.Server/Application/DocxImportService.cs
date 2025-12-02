using System.Globalization;
using System.Text.RegularExpressions;
using BlazorWasmDotNet8AspNetCoreHosted.Server.Domain.Entities;
using BlazorWasmDotNet8AspNetCoreHosted.Server.Infrastructure;
using BlazorWasmDotNet8AspNetCoreHosted.Shared.DTOs;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace BlazorWasmDotNet8AspNetCoreHosted.Server.Application;

public sealed class DocxImportService
{
    private static readonly Regex CourseCodeRegex = new(@"[A-Za-zА-Яа-яІіЇїЄєҐґ]{1,6}-\d+", RegexOptions.Compiled);
    private static readonly Regex ModulePrefixRegex = new(@"\b\d+\.\d+\.\d+\b", RegexOptions.Compiled);
    private static readonly Regex TopicCodeRegex = new(@"\d+(?:\.\d+){2,}", RegexOptions.Compiled);
    private static readonly Regex LetterTopicCodeRegex = new(@"[A-Za-zА-Яа-яІіЇїЄєҐґ]+\.?((\d+\.?)+)", RegexOptions.Compiled);

    public async Task<DocxImportResultDto> ImportAsync(IFormFile file, AppDbContext db, bool apply, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return new DocxImportResultDto(string.Empty, null, false, new(), new() { "Файл порожній або не надісланий" }, "Файл порожній або не надісланий");

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        using var doc = WordprocessingDocument.Open(buffer, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null)
            return new DocxImportResultDto(string.Empty, null, false, new(), new() { "Не вдалося прочитати тіло документа" }, "Не вдалося прочитати тіло документа");

        var warnings = new List<string>();
        var allTexts = body.Descendants<Text>().Select(t => t.Text ?? string.Empty).ToList();
        var courseName = ResolveCourseName(file.FileName, allTexts);

        var tables = body.Descendants<Table>().ToList();
        var modulesTable = tables.FirstOrDefault(LooksLikeModuleTable);
        var parsedModules = modulesTable is not null
            ? ParseModules(modulesTable, warnings)
            : new List<DocxImportModuleDto>();

        var topicTables = tables.Where(LooksLikeTopicTable).ToList();
        var moduleOrder = parsedModules.Select(m => m.Code).ToList();
        var knownModuleCodes = new HashSet<string>(moduleOrder, StringComparer.OrdinalIgnoreCase);
        var topicsByModule = ParseTopics(topicTables, moduleOrder, knownModuleCodes, warnings);

        foreach (var (moduleCode, topics) in topicsByModule)
        {
            var target = parsedModules.FirstOrDefault(m => string.Equals(m.Code, moduleCode, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                warnings.Add($"Для модуля з кодом \"{moduleCode}\" знайдено теми, але такого модуля немає у таблиці модулів");
                continue;
            }

            target.Topics.Clear();
            target.Topics.AddRange(topics);
        }

        if (string.IsNullOrWhiteSpace(courseName))
        {
            return new DocxImportResultDto(string.Empty, null, false, parsedModules, warnings, "Не вдалося визначити назву курсу");
        }

        var upperCourse = NormalizeCourseName(courseName);
        var allCourses = await db.Courses.AsNoTracking().ToListAsync(ct);
        var course = allCourses.FirstOrDefault(c => NormalizeCourseName(c.Name) == upperCourse)
                     ?? allCourses.FirstOrDefault(c => NormalizeCourseName(c.Name).Contains(upperCourse));

        if (course is null)
        {
            var codeMatch = CourseCodeRegex.Match(courseName);
            if (codeMatch.Success)
            {
                var codeUpper = NormalizeCourseName(codeMatch.Value);
                course = allCourses.FirstOrDefault(c => NormalizeCourseName(c.Name).Contains(codeUpper));
            }
        }
        if (course is null)
        {
            return new DocxImportResultDto(courseName, null, false, parsedModules, warnings, $"Не знайдено курс \"{courseName}\"");
        }

        var result = new DocxImportResultDto(courseName, course.Id, true, parsedModules, warnings, null);
        if (!apply)
        {
            return result;
        }

        await ApplyAsync(db, course, parsedModules, result, ct);
        return result;
    }

    private static string? ResolveCourseName(string fileName, IEnumerable<string> docTexts)
    {
        var candidates = CourseCodeRegex.Matches(fileName).Select(m => m.Value).ToList();
        if (candidates.Count == 0)
        {
            candidates = docTexts
                .SelectMany(t => CourseCodeRegex.Matches(t).Cast<Match>().Select(m => m.Value))
                .ToList();
        }

        return candidates.FirstOrDefault();
    }

    private static List<DocxImportModuleDto> ParseModules(Table table, List<string> warnings)
    {
        var rows = table.Elements<TableRow>().Skip(1).ToList();
        var modules = new List<DocxImportModuleDto>();

        foreach (var row in rows)
        {
            var cells = GetRowCells(row);
            if (cells.Count < 3) continue;

            var rawCode = cells[0];
            if (string.IsNullOrWhiteSpace(rawCode)) continue;
            if (!Regex.IsMatch(rawCode, @"^\d+$")) continue; // пропускаємо службові рядки типу Усього

            var code = NormalizeCode(rawCode);
            var title = NormalizeText(cells.ElementAtOrDefault(1) ?? string.Empty);
            var credits = ParseDecimal(cells.ElementAtOrDefault(2));

            if (modules.Any(m => string.Equals(m.Code, code, StringComparison.OrdinalIgnoreCase)))
            {
                warnings.Add($"Модуль з кодом \"{code}\" повторюється у таблиці");
                continue;
            }

            modules.Add(new DocxImportModuleDto(code, title, credits, new List<DocxImportTopicDto>()));
        }

        return modules;
    }

    private static Dictionary<string, List<DocxImportTopicDto>> ParseTopics(IEnumerable<Table> tables, List<string> moduleOrder, HashSet<string> knownModuleCodes, List<string> warnings)
    {
        var result = new Dictionary<string, List<DocxImportTopicDto>>(StringComparer.OrdinalIgnoreCase);
        var tableIndex = 0;
        var remainingModules = new Queue<string>(moduleOrder);
        foreach (var table in tables)
        {
            var rows = table.Elements<TableRow>().ToList();
            if (rows.Count < 2) continue;

            var moduleHeader = GetRowCells(rows[1]).FirstOrDefault() ?? string.Empty;
            var modulePrefixMatch = ModulePrefixRegex.Match(moduleHeader);
            string? modulePrefix = null;
            string? moduleCode = null;

            if (modulePrefixMatch.Success)
            {
                modulePrefix = modulePrefixMatch.Value;
                moduleCode = modulePrefix.Split('.')[0];
            }

            if (moduleCode is null)
            {
                foreach (var code in knownModuleCodes)
                {
                    if (moduleHeader.Contains($"{code}.", StringComparison.OrdinalIgnoreCase) ||
                        moduleHeader.Contains($"{code} ", StringComparison.OrdinalIgnoreCase) ||
                        moduleHeader.Contains(code, StringComparison.OrdinalIgnoreCase))
                    {
                        moduleCode = code;
                        modulePrefix = code;
                        break;
                    }
                }
            }

            if (moduleCode is null)
            {
                // Якщо знайдений префікс у вигляді літер+цифр (наприклад, КП1.1.1) — дістаємо першу цифру як код
                var letteredMatch = Regex.Match(moduleHeader, @"[A-Za-zА-Яа-яІіЇїЄєҐґ]{1,4}(\\d+)");
                if (letteredMatch.Success)
                {
                    moduleCode = letteredMatch.Groups[1].Value;
                    modulePrefix = moduleCode;
                }
            }

            if (moduleCode is null && tableIndex < moduleOrder.Count)
            {
                moduleCode = moduleOrder[tableIndex];
                modulePrefix = moduleCode;
            }

            if (moduleCode is null)
            {
                while (remainingModules.Count > 0 && result.ContainsKey(remainingModules.Peek()))
                {
                    remainingModules.Dequeue();
                }
                if (remainingModules.Count > 0)
                {
                    moduleCode = remainingModules.Dequeue();
                    modulePrefix = moduleCode;
                    warnings.Add($"Невідомий заголовок таблиці, прив’язано за порядком до модуля \"{moduleCode}\"");
                }
                else
                {
                    warnings.Add("Не вдалося визначити модуль для однієї з таблиць тем.");
                    tableIndex++;
                    continue;
                }
            }
            if (string.IsNullOrWhiteSpace(moduleCode))
            {
                warnings.Add("Пропущено таблицю тем через відсутній код модуля.");
                tableIndex++;
                continue;
            }

            if (!result.TryGetValue(moduleCode, out var topics))
            {
                topics = new List<DocxImportTopicDto>();
                result[moduleCode] = topics;
            }
            var moduleCodeValue = moduleCode;

            var order = 1;
            foreach (var row in rows.Skip(2))
            {
                var cells = GetRowCells(row);
                if (cells.Count != 6) continue;

                var topicCell = cells[5];
                if (string.IsNullOrWhiteSpace(topicCell)) continue; // пропускаємо «Всього» тощо

                var topicCode = ExtractTopicCode(topicCell, modulePrefix, moduleCode, order);

                var total = ParseInt(cells[2]);
                var auditorium = ParseInt(cells[3]);
                var self = ParseInt(cells[4]);

                var lessonTypeName = NormalizeText(cells[1]);

                topics.Add(new DocxImportTopicDto(
                    moduleCodeValue,
                    topicCode, 
                    lessonTypeName,
                    total,
                    auditorium,
                    self,
                    order++
                ));
            }
            tableIndex++;
        }

        return result;
    }

    private static async Task ApplyAsync(AppDbContext db, Course course, List<DocxImportModuleDto> modules, DocxImportResultDto result, CancellationToken ct)
    {
        var moduleCodes = modules.Select(m => m.Code).ToList();
        var existingModules = await db.Modules
            .Include(m => m.ModuleCourses)
            .Where(m => moduleCodes.Contains(m.Code))
            .ToDictionaryAsync(m => m.Code, StringComparer.OrdinalIgnoreCase, ct);

        foreach (var module in modules)
        {
            if (existingModules.TryGetValue(module.Code, out var entity))
            {
                entity.Title = module.Title;
                entity.Credits = module.Credits;
                entity.CourseId = course.Id;

                if (!entity.ModuleCourses.Any(mc => mc.CourseId == course.Id))
                {
                    db.ModuleCourses.Add(new ModuleCourse { ModuleId = entity.Id, CourseId = course.Id });
                }
            }
            else
            {
                var newModule = new Module
                {
                    Code = module.Code,
                    Title = module.Title,
                    Credits = module.Credits,
                    CourseId = course.Id
                };
                db.Modules.Add(newModule);
                await db.SaveChangesAsync(ct);

                db.ModuleCourses.Add(new ModuleCourse { ModuleId = newModule.Id, CourseId = course.Id });
                existingModules[module.Code] = newModule;
            }
        }

        await db.SaveChangesAsync(ct);

        var allBuildingIds = await db.Buildings.Select(b => b.Id).ToListAsync(ct);
        var allRoomIds = await db.Rooms.Select(r => r.Id).ToListAsync(ct);

        var planDefaults = await db.ModulePlans
            .Where(p => p.CourseId == course.Id)
            .ToDictionaryAsync(p => p.ModuleId, ct);

        foreach (var module in modules)
        {
            if (!existingModules.TryGetValue(module.Code, out var entity)) continue;

            var targetHours = (int)Math.Max(0, Math.Round(module.Credits * 30m));
            if (planDefaults.TryGetValue(entity.Id, out var plan))
            {
                plan.TargetHours = targetHours;
                plan.IsActive = true;
            }
            else
            {
                db.ModulePlans.Add(new ModulePlan
                {
                    CourseId = course.Id,
                    ModuleId = entity.Id,
                    TargetHours = targetHours,
                    ScheduledHours = 0,
                    IsActive = true
                });
            }

            // Призначаємо всі доступні корпуси й аудиторії модулю.
            var existingRoomIds = await db.ModuleRooms.Where(x => x.ModuleId == entity.Id).Select(x => x.RoomId).ToListAsync(ct);
            var existingBuildingIds = await db.ModuleBuildings.Where(x => x.ModuleId == entity.Id).Select(x => x.BuildingId).ToListAsync(ct);

            var toAddRooms = allRoomIds.Except(existingRoomIds).ToList();
            var toRemoveRooms = existingRoomIds.Except(allRoomIds).ToList();
            if (toRemoveRooms.Count > 0)
            {
                await db.ModuleRooms.Where(x => x.ModuleId == entity.Id && toRemoveRooms.Contains(x.RoomId)).ExecuteDeleteAsync(ct);
            }
            foreach (var rid in toAddRooms)
                db.ModuleRooms.Add(new ModuleRoom { ModuleId = entity.Id, RoomId = rid });

            var toAddBuildings = allBuildingIds.Except(existingBuildingIds).ToList();
            var toRemoveBuildings = existingBuildingIds.Except(allBuildingIds).ToList();
            if (toRemoveBuildings.Count > 0)
            {
                await db.ModuleBuildings.Where(x => x.ModuleId == entity.Id && toRemoveBuildings.Contains(x.BuildingId)).ExecuteDeleteAsync(ct);
            }
            foreach (var bid in toAddBuildings)
                db.ModuleBuildings.Add(new ModuleBuilding { ModuleId = entity.Id, BuildingId = bid });
        }

        await db.SaveChangesAsync(ct);

        var lessonTypes = await db.LessonTypes.ToListAsync(ct);
        var lessonTypeLookup = lessonTypes.ToDictionary(
            lt => NormalizeText(lt.Name).ToUpperInvariant(),
            lt => lt,
            StringComparer.OrdinalIgnoreCase);

        foreach (var module in modules)
        {
            if (!existingModules.TryGetValue(module.Code, out var entity))
            {
                result.Warnings.Add($"Не вдалося знайти або створити модуль \"{module.Code}\"");
                continue;
            }

            var existingTopics = await db.ModuleTopics
                .Where(t => t.ModuleId == entity.Id)
                .ToListAsync(ct);

            var existingByCode = existingTopics.ToDictionary(t => t.TopicCode, StringComparer.OrdinalIgnoreCase);
            var parsedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var parsedEntities = new List<(ModuleTopic topic, int desiredOrder)>();

            foreach (var topic in module.Topics.OrderBy(t => t.Order))
            {
                parsedCodes.Add(topic.TopicCode);
                if (!lessonTypeLookup.TryGetValue(NormalizeText(topic.LessonTypeName).ToUpperInvariant(), out var lessonType))
                {
                    // якщо типу немає — створюємо тимчасовий активний тип, що рахується в плані.
                    lessonType = new LessonTypeRef
                    {
                        Code = NormalizeText(topic.LessonTypeName).ToUpperInvariant().Replace(" ", "_"),
                        Name = topic.LessonTypeName,
                        IsActive = true,
                        RequiresRoom = true,
                        RequiresTeacher = true,
                        BlocksRoom = true,
                        BlocksTeacher = true,
                        CountInPlan = true,
                        CountInLoad = true
                    };
                    db.LessonTypes.Add(lessonType);
                    await db.SaveChangesAsync(ct);
                    lessonTypeLookup[NormalizeText(topic.LessonTypeName).ToUpperInvariant()] = lessonType;
                }

                ModuleTopic entityTopic;
                if (existingByCode.TryGetValue(topic.TopicCode, out var existingTopic))
                {
                    entityTopic = existingTopic;
                }
                else
                {
                    entityTopic = new ModuleTopic
                    {
                        ModuleId = entity.Id,
                        TopicCode = topic.TopicCode,
                        // тимчасовий великий порядок щоб уникнути конфлікту унікального індексу (ModuleId, Order)
                        Order = 100000 + parsedEntities.Count
                    };
                    db.ModuleTopics.Add(entityTopic);
                    existingTopics.Add(entityTopic);
                    existingByCode[topic.TopicCode] = entityTopic;
                }

                entityTopic.LessonTypeId = lessonType.Id;
                entityTopic.TotalHours = Math.Max(0, topic.TotalHours);
                entityTopic.AuditoriumHours = Math.Max(0, topic.AuditoriumHours);
                entityTopic.SelfStudyHours = Math.Max(0, topic.SelfStudyHours);
                parsedEntities.Add((entityTopic, topic.Order));
            }

            var remaining = existingTopics
                .Where(t => !parsedCodes.Contains(t.TopicCode))
                .OrderBy(t => t.Order)
                .ToList();

            var ordered = parsedEntities
                .OrderBy(t => t.desiredOrder)
                .ThenBy(t => t.topic.TopicCode, StringComparer.OrdinalIgnoreCase)
                .Select(t => t.topic)
                .Concat(remaining)
                .ToList();

            var order = 1;
            foreach (var t in ordered)
            {
                t.Order = order++;
            }

            await db.SaveChangesAsync(ct);
        }
    }

    private static bool LooksLikeModuleTable(Table table)
    {
        var header = table.Elements<TableRow>().FirstOrDefault();
        if (header is null) return false;

        var cells = GetRowCells(header);
        var headerText = string.Join(" ", cells).ToLowerInvariant();
        return cells.Count >= 3 && headerText.Contains("кредит");
    }

    private static bool LooksLikeTopicTable(Table table)
    {
        var header = table.Elements<TableRow>().FirstOrDefault();
        if (header is null) return false;

        var cells = GetRowCells(header);
        if (cells.Count != 6) return false;

        var expected = new[] { "1", "2", "3", "4", "5", "6" };
        for (var i = 0; i < expected.Length; i++)
        {
            if (!string.Equals(cells[i], expected[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static List<string> GetRowCells(TableRow row)
    {
        return row.Elements<TableCell>()
            .Select(cell => NormalizeText(cell.InnerText))
            .ToList();
    }

    private static string NormalizeText(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var normalized = input.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
        while (normalized.Contains("  "))
        {
            normalized = normalized.Replace("  ", " ");
        }

        return normalized.Trim();
    }

    private static string NormalizeCode(string raw) => NormalizeText(raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? raw);

    private static decimal ParseDecimal(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return 0m;

        var normalized = raw.Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0m;
    }

    private static string NormalizeCourseName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var span = name.Where(c => !char.IsWhiteSpace(c)).ToArray();
        return new string(span).ToUpperInvariant();
    }

    private static int ParseInt(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return 0;

        var normalized = raw.Replace(',', '.');
        if (int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return value;

        if (decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var dec))
            return (int)Math.Round(dec, MidpointRounding.AwayFromZero);

        return 0;
    }

    private static string ExtractTopicCode(string topicCell, string? modulePrefix, string? moduleCode, int order)
    {
        if (!string.IsNullOrWhiteSpace(topicCell))
        {
            var numeric = TopicCodeRegex.Match(topicCell);
            if (numeric.Success)
            {
                return numeric.Value.Trim().Trim('.');
            }

            var lettered = LetterTopicCodeRegex.Match(topicCell);
            if (lettered.Success && !string.IsNullOrWhiteSpace(moduleCode))
            {
                var numericPart = lettered.Groups[1].Value.Trim('.').Trim();
                return string.IsNullOrWhiteSpace(numericPart) ? $"{moduleCode}.{order}" : $"{moduleCode}.{numericPart}";
            }
        }

        var prefix = string.IsNullOrWhiteSpace(modulePrefix) ? moduleCode : modulePrefix;
        return $"{prefix}.{order}";
    }
}
