using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Realm.Core.Ids;
using Realm.Core.Validation;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Realm.Data.Content;

/// <summary>Plumbing shared by every content parser: strict YAML, IDs, tags.</summary>
internal static class ParserSupport
{
    private static readonly Regex UnknownFieldPattern = new("Property '([^']+)' not found", RegexOptions.Compiled);

    /// <summary>Deserializes strictly. Reports and returns false on bad YAML or an empty file.</summary>
    public static bool TryDeserialize<T>(
        string yaml, string location, string schemaPage, ValidationReport report, out T dto)
        where T : class
    {
        T? parsed;
        try
        {
            parsed = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build()
                .Deserialize<T?>(yaml);
        }
        catch (YamlException e)
        {
            report.Error(
                ValidatorCodes.YamlInvalid,
                $"{location}:{e.Start.Line}",
                FriendlyYamlMessage(e.InnerException?.Message ?? e.Message),
                $"Check the field names and value types against {schemaPage}.");
            dto = null!; // Never read: callers stop when this returns false.
            return false;
        }

        if (parsed is null)
        {
            report.Error(ValidatorCodes.EmptyFile, location, "The file contains no record.");
            dto = null!; // Never read: callers stop when this returns false.
            return false;
        }

        dto = parsed;
        return true;
    }

    /// <summary>Checks the required <c>id</c> field.</summary>
    public static ContentId ParseId(string? raw, string location, ValidationReport report)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            report.Error(ValidatorCodes.MissingField, location, "Missing required field 'id'.");
            return default;
        }

        if (!ContentId.TryParse(raw, out ContentId id, out string? error))
        {
            // Safe: TryParse sets the error text whenever it returns false.
            report.Error(ValidatorCodes.InvalidId, location, error!);
            return default;
        }

        return id;
    }

    /// <summary>Checks the required <c>name</c> field.</summary>
    public static void RequireName(string? name, string location, ValidationReport report)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            report.Error(ValidatorCodes.MissingField, location, "Missing required field 'name'.");
        }
    }

    /// <summary>Validates the optional <c>tags</c> list, reporting each bad tag.</summary>
    public static TagSet ParseTags(List<string?>? raw, string location, ValidationReport report)
    {
        if (raw is null)
        {
            return TagSet.Empty;
        }

        var valid = new List<string>();
        foreach (string? tag in raw)
        {
            if (ContentId.IsValidSegment(tag))
            {
                valid.Add(tag!); // Safe: IsValidSegment returns false for null, so tag is not null here.
            }
            else
            {
                report.Error(
                    ValidatorCodes.InvalidTag,
                    location,
                    $"Invalid tag '{tag}'.",
                    "Use lowercase letters, digits and underscores, starting with a letter.");
            }
        }

        return TagSet.From(valid);
    }

    /// <summary>Validates an optional reference to another record, requiring a given content type.</summary>
    /// <returns>The ID, or null when absent or invalid (invalid is reported).</returns>
    public static ContentId? ParseReference(
        string? raw, string field, string requiredType, string location, ValidationReport report)
    {
        if (raw is null)
        {
            return null;
        }

        if (!ContentId.TryParse(raw, out ContentId id, out string? error))
        {
            report.Error(ValidatorCodes.InvalidId, location, $"Field '{field}': {error}");
            return null;
        }

        if (id.Type != requiredType)
        {
            report.Error(
                ValidatorCodes.ReferenceTypeMismatch,
                location,
                $"Field '{field}' must reference a '{requiredType}' but '{id}' is a '{id.Type}'.",
                $"Use an ID starting with '{requiredType}.'.");
            return null;
        }

        return id;
    }

    /// <summary>Validates the optional <c>properties</c> map: valid names, finite numbers, sorted by name.</summary>
    public static IReadOnlyDictionary<string, double> ParseProperties(
        Dictionary<string, double>? raw, string location, ValidationReport report)
    {
        var sorted = new SortedDictionary<string, double>(StringComparer.Ordinal);
        if (raw is not null)
        {
            foreach ((string name, double value) in raw)
            {
                if (!ContentId.IsValidSegment(name))
                {
                    report.Error(
                        ValidatorCodes.InvalidProperty,
                        location,
                        $"Invalid property name '{name}'.",
                        "Use lowercase letters, digits and underscores, starting with a letter.");
                }
                else if (!double.IsFinite(value))
                {
                    report.Error(
                        ValidatorCodes.InvalidProperty,
                        location,
                        $"Property '{name}' must be a finite number.");
                }
                else
                {
                    sorted[name] = value;
                }
            }
        }

        return new ReadOnlyDictionary<string, double>(sorted);
    }

    // YamlDotNet names the internal DTO type in unknown-field errors, which means nothing to a modder.
    private static string FriendlyYamlMessage(string message)
    {
        Match unknown = UnknownFieldPattern.Match(message);
        return unknown.Success ? $"Unknown field '{unknown.Groups[1].Value}'." : message;
    }
}
