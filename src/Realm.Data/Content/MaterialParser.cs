using System;
using System.Collections.Generic;
using Realm.Core.Ids;
using Realm.Core.Validation;

namespace Realm.Data.Content;

/// <summary>Parses and validates one <c>material</c> YAML record.</summary>
/// <remarks>
/// Determinism: no randomness; output ordering is sorted. Thread-safety: stateless; a new YAML
/// deserializer is built per call.
/// </remarks>
public static class MaterialParser
{
    private const string SchemaPage = "docs/reference/content/material.md";

    /// <summary>Parses one YAML document into a material, reporting every problem found.</summary>
    /// <param name="yaml">The YAML text.</param>
    /// <param name="location">File location used in issues, for example <c>materials/iron_ore.yaml</c>.</param>
    /// <param name="report">Receives all issues.</param>
    /// <returns>The material, or null when any error prevented building it.</returns>
    public static MaterialDefinition? Parse(string yaml, string location, ValidationReport report)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(report);

        if (!ParserSupport.TryDeserialize(yaml, location, SchemaPage, report, out MaterialDto dto))
        {
            return null;
        }

        int errorsBefore = report.Count(Severity.Error);

        ContentId id = ParserSupport.ParseId(dto.Id, location, report);
        ParserSupport.RequireName(dto.Name, location, report);
        TagSet tags = ParserSupport.ParseTags(dto.Tags, location, report);
        IReadOnlyDictionary<string, double> properties = ParserSupport.ParseProperties(dto.Properties, location, report);

        if (report.Count(Severity.Error) > errorsBefore)
        {
            return null;
        }

        // Safe: a missing or blank name was reported as an error above, which returned null.
        return new MaterialDefinition(id, dto.Name!, tags, properties);
    }

    private sealed class MaterialDto
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public List<string?>? Tags { get; set; }

        public Dictionary<string, double>? Properties { get; set; }
    }
}
