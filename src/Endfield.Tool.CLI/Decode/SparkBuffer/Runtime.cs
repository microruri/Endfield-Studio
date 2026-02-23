using System.Collections.Generic;

namespace Endfield.Tool.CLI.Decode.SparkBuffer;

/// <summary>
/// Runtime schema structures for Spark bytes decoding.
/// </summary>
public class Field
{
    public string Name { get; set; } = string.Empty;
    public SparkType Type { get; set; }
    public int Offset { get; set; }

    public virtual int GetSize() => Type switch
    {
        SparkType.Bool => 1,
        SparkType.Byte => 1,
        SparkType.Int => 4,
        SparkType.Float => 4,
        SparkType.Enum => 4,
        SparkType.Long => 8,
        SparkType.Double => 8,
        _ => 4
    };
}

public sealed class ArrayField : Field
{
    public SparkType ElementType { get; set; }
    public int ElementTypeHash { get; set; }
}

public sealed class MapField : Field
{
    public SparkType KeyType { get; set; }
    public int KeyTypeHash { get; set; }
    public SparkType ValueType { get; set; }
    public int ValueTypeHash { get; set; }
}

public sealed class BeanField : Field
{
    public BeanType BeanType { get; set; } = new();
}

public sealed class BeanType
{
    public int TypeHash { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class SparkBean
{
    public BeanType BeanType { get; set; } = new();
    public List<Field> Fields { get; } = new();
}

public sealed class SparkEnum
{
    public int TypeHash { get; set; }
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, int> Items { get; } = new();
}

public sealed class SparkRoot
{
    public string RootName { get; set; } = string.Empty;
    public SparkType RootType { get; set; }
    public int RootTypeHash { get; set; }
    public SparkType SubType1 { get; set; }
    public int SubTypeHash1 { get; set; }
    public SparkType SubType2 { get; set; }
    public int SubTypeHash2 { get; set; }
}

public readonly record struct HashSlot(int Offset, int BucketSize);

public sealed class SparkScheme
{
    public Dictionary<int, SparkBean> Beans { get; } = new();
    public Dictionary<int, SparkEnum> Enums { get; } = new();
    public SparkRoot Root { get; } = new();
    public Dictionary<int, string> StringPool { get; } = new();
}
