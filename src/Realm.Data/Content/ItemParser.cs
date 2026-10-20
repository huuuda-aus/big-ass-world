using System;
using System.Collections.Generic;
using Realm.Core.Ids;
using Realm.Core.Validation;

namespace Realm.Data.Content;

/// <summary>Parses and validates one <c>item</c> YAML record.</summary>
/// <remarks>Determinism: no randomness. Thread-safety: stateless.</remarks>
public static class ItemParser
{
    private const string SchemaPage = "docs/reference/content/item.md";
    private const string MaterialType = "material";

    /// <summary>Parses one YAML document into an item, reporting every problem found.</summary>
    /// <param name="yaml">The YAML text.</param>
    /// <param name="location">File location used in issues, for example <c>items/torch.yaml</c>.</param>
    /// <param name="report">Receives all issues.</param>
    /// <returns>The item, or null when any error prevented building it.</returns>
    /// <remarks>Whether a referenced material exists is checked later by <see cref="ContentValidator"/>.</remarks>
    public static ItemDefinition? Parse(string yaml, string location, ValidationReport report)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(report);

        if (!ParserSupport.TryDeserialize(yaml, location, SchemaPage, report, out ItemDto dto))
        {
            return null;
        }

        int errorsBefore = report.Count(Severity.Error);

        ContentId id = ParserSupport.ParseId(dto.Id, location, report);
        ParserSupport.RequireName(dto.Name, location, report);
        TagSet tags = ParserSupport.ParseTags(dto.Tags, location, report);
        ContentId? material = ParserSupport.ParseReference(dto.Material, "material", MaterialType, location, report);

        if (report.Count(Severity.Error) > errorsBefore)
        {
            return null;
        }

        // Safe: a missing or blank name was reported as an error above, which returned null.
        return new ItemDefinition(id, dto.Name!, tags, material);
    }

    private sealed class ItemDto
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public List<string?>? Tags { get; set; }

        public string? Material { get; set; }
    }
}
