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

    /// <summary>The schema name of the class, e.g. "authentication".</summary>
    public string Name { get; }
}

/// <summary>Declares the OCSF object name of a generated object class.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class OcsfObjectAttribute : Attribute
{
    public OcsfObjectAttribute(string name) => Name = name;

    /// <summary>The schema name of the object, e.g. "user".</summary>
    public string Name { get; }
}
