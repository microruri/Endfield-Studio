using System.Text;

namespace Endfield.Tool.GUI.UI;

public static class HexDumpFormatter
{
    public static string Format(byte[] data, int bytesPerLine = 16)
    {
        if (data == null || data.Length == 0)
            return "<empty>";

        var sb = new StringBuilder(data.Length * 4);

        for (var offset = 0; offset < data.Length; offset += bytesPerLine)
        {
            var lineLength = Math.Min(bytesPerLine, data.Length - offset);
            sb.Append(offset.ToString("X8"));
            sb.Append("  ");

            for (var i = 0; i < bytesPerLine; i++)
            {
                if (i < lineLength)
                {
                    sb.Append(data[offset + i].ToString("X2"));
                    sb.Append(' ');
                }
                else
                {
                    sb.Append("   ");
                }
            }

            sb.Append(' ');
            for (var i = 0; i < lineLength; i++)
            {
                var b = data[offset + i];
                sb.Append(b >= 32 && b <= 126 ? (char)b : '.');
            }

            if (offset + lineLength < data.Length)
                sb.AppendLine();
        }

        return sb.ToString();
    }
}
