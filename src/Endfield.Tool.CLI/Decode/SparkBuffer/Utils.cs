namespace Endfield.Tool.CLI.Decode.SparkBuffer;

/// <summary>
/// Alignment helpers used by Spark binary parser.
/// </summary>
public static class Utils
{
    public static int Align(int currPos, int align)
    {
        if (align <= 1)
            return currPos;

        var mod = currPos % align;
        return mod == 0 ? currPos : currPos + (align - mod);
    }

    public static int GetAlignment(SparkType type)
    {
        return type switch
        {
            SparkType.Long => 8,
            SparkType.Double => 8,
            SparkType.Int => 4,
            SparkType.Float => 4,
            SparkType.String => 4,
            SparkType.Bean => 4,
            SparkType.Array => 4,
            SparkType.Map => 4,
            _ => 1
        };
    }
}
