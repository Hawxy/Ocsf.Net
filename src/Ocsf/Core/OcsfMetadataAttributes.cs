namespace Ocsf;

/// <summary>OCSF attribute requirement levels.</summary>
public enum OcsfRequirement
{
    Optional = 0,
    Recommended = 1,
    Required = 2,
}

/// <summary>Declares the OCSF requirement level of a generated property.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class OcsfRequirementAttribute : Attribute
{
    public OcsfRequirementAttribute(OcsfRequirement requirement) => Requirement = requirement;

    public OcsfRequirement Requirement { get; }

    /// <summary>True when the event class constructor populates this attribute
    /// (classification attributes such as class_uid), so producers need not set it.</summary>
    public bool InitializedByConstructor { get; set; }

    /// <summary>The profiles this attribute belongs to, when profile-sourced, comma-separated
    /// when it belongs to more than one. Its requirement applies only to events that declare
    /// one of the profiles in metadata.profiles.</summary>
    public string? Profile { get; set; }
}

/// <summary>Names the sibling label property of an enum-coded attribute
/// (e.g. StatusId's sibling is Status).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class OcsfSiblingAttribute : Attribute
{
    public OcsfSiblingAttribute(string propertyName) => PropertyName = propertyName;

    /// <summary>The C# name of the sibling label property.</summary>
    public string PropertyName { get; }
}

/// <summary>Kinds of OCSF class and object constraints.</summary>
public enum OcsfConstraintKind
{
    AtLeastOne = 0,
    JustOne = 1,
}

/// <summary>Declares an OCSF constraint over a set of properties of the class.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class OcsfConstraintAttribute : Attribute
{
    public OcsfConstraintAttribute(OcsfConstraintKind kind, params string[] propertyNames)
    {
        Kind = kind;
        PropertyNames = propertyNames;
    }

    public OcsfConstraintKind Kind { get; }

    /// <summary>The C# names of the constrained properties.</summary>
    public string[] PropertyNames { get; }
}

/// <summary>Declares the OCSF classification of a generated event class.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class OcsfEventClassAttribute : Attribute
{
    public OcsfEventClassAttribute(int classUid, int categoryUid, string name)
    {
        ClassUid = classUid;
        CategoryUid = categoryUid;
        Name = name;
    }

    /// <summary>The class_uid, e.g. 3002 for authentication.</summary>
    public int ClassUid { get; }

    /// <summary>The category_uid, e.g. 3 for Identity &amp; Access Management.</summary>
    public int CategoryUid { get; }

    /// <summary>The schema name of the class, e.g. "authentication". Extension classes use the
    /// unprefixed name; the schema key is <c>$"{Extension}/{Name}"</c>.</summary>
    public string Name { get; }

    /// <summary>The owning extension name for extension classes, e.g. "win".</summary>
    public string? Extension { get; set; }

    /// <summary>The owning extension uid, e.g. 2 for win. Zero for core classes.</summary>
    public int ExtensionUid { get; set; }
}

/// <summary>Declares the OCSF object name of a generated object class.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class OcsfObjectAttribute : Attribute
{
    public OcsfObjectAttribute(string name) => Name = name;

    /// <summary>The schema name of the object, e.g. "user". Extension objects use the
    /// unprefixed name; the schema key is <c>$"{Extension}/{Name}"</c>.</summary>
    public string Name { get; }

    /// <summary>The owning extension name for extension objects, e.g. "win".</summary>
    public string? Extension { get; set; }

    /// <summary>The owning extension uid, e.g. 2 for win. Zero for core objects.</summary>
    public int ExtensionUid { get; set; }
}
