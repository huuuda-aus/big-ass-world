namespace Realm.Data.Content;

/// <summary>
/// Stable issue codes produced by content loading. Meanings and fixes: docs/reference/validators.md.
/// Codes are never reused for a different problem.
/// </summary>
public static class ValidatorCodes
{
    /// <summary>YAML syntax error, unknown field or wrong value type.</summary>
    public const string YamlInvalid = "DATA001";

    /// <summary>A required field is missing or empty.</summary>
    public const string MissingField = "DATA002";

    /// <summary>The <c>id</c> violates the content ID grammar.</summary>
    public const string InvalidId = "DATA003";

    /// <summary>The ID's type does not match the directory the file lives in.</summary>
    public const string IdTypeMismatch = "DATA004";

    /// <summary>Two records share one ID.</summary>
    public const string DuplicateId = "DATA005";

    /// <summary>A tag violates the tag grammar.</summary>
    public const string InvalidTag = "DATA006";

    /// <summary>A property name violates the grammar or a value is not a finite number.</summary>
    public const string InvalidProperty = "DATA007";

    /// <summary>The file name does not match the ID's name segment.</summary>
    public const string FileNameMismatch = "DATA008";

    /// <summary>A directory under the content root is not a known content type.</summary>
    public const string UnknownDirectory = "DATA009";

    /// <summary>The content root directory does not exist.</summary>
    public const string RootMissing = "DATA010";

    /// <summary>A file could not be read.</summary>
    public const string FileUnreadable = "DATA011";

    /// <summary>A file contains no record.</summary>
    public const string EmptyFile = "DATA012";

    /// <summary>A record references an ID that no loaded record defines.</summary>
    public const string DanglingReference = "DATA013";

    /// <summary>A reference field points at a record of the wrong content type.</summary>
    public const string ReferenceTypeMismatch = "DATA014";

    /// <summary><c>world_size_m</c> is not a positive number.</summary>
    public const string InvalidWorldSize = "DATA015";

    /// <summary>A region polygon is not a valid simple polygon, or a vertex is not an [x, z] pair.</summary>
    public const string InvalidPolygon = "DATA016";

    /// <summary>A region polygon extends outside the world square.</summary>
    public const string PolygonOutsideWorld = "DATA017";

    /// <summary>A <c>service</c> record has an unknown tier, ward, placement, foundation or room word, or an out-of-range number.</summary>
    public const string InvalidServiceField = "DATA018";

    /// <summary>A <c>foliage</c> record has an unknown kind, a density, range or limit out of bounds, or a malformed pair.</summary>
    public const string InvalidFoliageField = "DATA019";
}
