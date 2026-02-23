using System;
using System.Text;

namespace Endfield.Tool.CLI.Decode.SparkBuffer;

/// <summary>
/// Primitive readers over Spark binary byte arrays.
/// </summary>
public static class BinaryStream
{
    public static string ReadCString(ref byte[] data, ref int pos)
    {
        var start = pos;
        while (data[pos] != 0)
            pos++;

        var value = Encoding.UTF8.GetString(data, start, pos - start);
        pos++;
        return value;
    }

    public static int ReadInt32(ref byte[] data, ref int pos)
    {
        var value = BitConverter.ToInt32(data, pos);
        pos += 4;
        return value;
    }

    public static long ReadInt64(ref byte[] data, ref int pos)
    {
        var value = BitConverter.ToInt64(data, pos);
        pos += 8;
        return value;
    }

    public static float ReadSingle(ref byte[] data, ref int pos)
    {
        var value = BitConverter.ToSingle(data, pos);
        pos += 4;
        return value;
    }

    public static double ReadDouble(ref byte[] data, ref int pos)
    {
        var value = BitConverter.ToDouble(data, pos);
        pos += 8;
        return value;
    }
}
