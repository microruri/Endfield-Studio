using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Endfield.Tool.CLI.Decode.IFix;

/// <summary>
/// IFix patch binary decoder with full packet-style dump.
/// Parsing order follows InjectFix PatchManager.Load/readMethod.
/// </summary>
public static class ILFixPatchReadableDecoder
{
    private const ulong DefaultInstructionMagic = 317431043901UL;

    public static bool LooksLikeIlFixPatch(byte[] input)
    {
        if (input == null || input.Length < 64)
            return false;

        return IndexOfAscii(input, "IFix.ILFixInterfaceBridge", 0) >= 0 ||
               IndexOfAscii(input, "IFix.WrappersManagerImpl", 0) >= 0;
    }

    public static byte[] DecodeToReadableTextBytes(byte[] input)
    {
        if (input == null)
            throw new ArgumentNullException(nameof(input));

        var parse = Parse(input);
        var text = BuildReport(input, parse);
        return Encoding.UTF8.GetBytes(text);
    }

    private static ParseResult Parse(byte[] input)
    {
        var result = new ParseResult();
        var payloadOffset = DetectPayloadOffset(input);
        if (payloadOffset < 0)
        {
            result.Success = false;
            result.Error = "Cannot detect IFix payload offset.";
            return result;
        }

        result.PayloadOffset = payloadOffset;

        try
        {
            var reader = new PatchReader(input, payloadOffset);

            var magicStart = reader.Position;
            var magic = reader.ReadUInt64();
            result.InstructionMagic = magic;
            AddField(result, magicStart, reader.Position, "instructionMagic", $"0x{magic:X16}", "IFix VM instruction format magic");

            var bridgeStart = reader.Position;
            var bridge = reader.ReadString();
            result.InterfaceBridgeTypeName = bridge;
            AddField(result, bridgeStart, reader.Position, "interfaceBridgeTypeName", bridge, "Type.GetType(...) for bridge interface");

            var externTypeCountStart = reader.Position;
            var externTypeCount = reader.ReadInt32();
            EnsureNonNegative(externTypeCount, nameof(externTypeCount));
            result.ExternTypeCount = externTypeCount;
            AddField(result, externTypeCountStart, reader.Position, "externTypeCount", externTypeCount.ToString(CultureInfo.InvariantCulture), "extern type table size");

            for (var i = 0; i < externTypeCount; i++)
            {
                var itemStart = reader.Position;
                var typeName = reader.ReadString();
                result.ExternTypes.Add(typeName);
                AddField(result, itemStart, reader.Position, $"externTypes[{i}]", typeName, "assembly-qualified type string");
            }

            var methodCountStart = reader.Position;
            var methodCount = reader.ReadInt32();
            EnsureNonNegative(methodCount, nameof(methodCount));
            result.MethodCount = methodCount;
            AddField(result, methodCountStart, reader.Position, "methodCount", methodCount.ToString(CultureInfo.InvariantCulture), "VM method body count");

            for (var i = 0; i < methodCount; i++)
            {
                var codeSizeStart = reader.Position;
                var codeSize = reader.ReadInt32();
                EnsureNonNegative(codeSize, "codeSize");
                AddField(result, codeSizeStart, reader.Position, $"methods[{i}].codeSize", codeSize.ToString(CultureInfo.InvariantCulture), "instruction count (each instruction = 8 bytes)");

                var codeBytesStart = reader.Position;
                reader.Skip(codeSize * 8L);
                AddField(result, codeBytesStart, reader.Position, $"methods[{i}].codeBytes", $"{codeSize * 8} bytes", "raw instruction bytes [int code + int operand]");

                var ehCountStart = reader.Position;
                var ehCount = reader.ReadInt32();
                EnsureNonNegative(ehCount, "exceptionHandlerCount");
                AddField(result, ehCountStart, reader.Position, $"methods[{i}].exceptionHandlerCount", ehCount.ToString(CultureInfo.InvariantCulture), "exception handler entry count");

                var ehBytesStart = reader.Position;
                reader.Skip(ehCount * 24L);
                AddField(result, ehBytesStart, reader.Position, $"methods[{i}].exceptionHandlerBytes", $"{ehCount * 24} bytes", "raw exception handler table (6 x int)");
            }

            var externMethodCountStart = reader.Position;
            var externMethodCount = reader.ReadInt32();
            EnsureNonNegative(externMethodCount, nameof(externMethodCount));
            result.ExternMethodCount = externMethodCount;
            AddField(result, externMethodCountStart, reader.Position, "externMethodCount", externMethodCount.ToString(CultureInfo.InvariantCulture), "readMethod(...) entry count");

            for (var i = 0; i < externMethodCount; i++)
            {
                var methodStart = reader.Position;
                var signature = ReadMethodRef(reader, result.ExternTypes);
                AddField(result, methodStart, reader.Position, $"externMethods[{i}]", signature, "resolved extern method signature");
            }

            var internCountStart = reader.Position;
            var internCount = reader.ReadInt32();
            EnsureNonNegative(internCount, nameof(internCount));
            result.InternStringCount = internCount;
            AddField(result, internCountStart, reader.Position, "internStringsCount", internCount.ToString(CultureInfo.InvariantCulture), "intern string pool count");

            for (var i = 0; i < internCount; i++)
            {
                var sStart = reader.Position;
                var value = reader.ReadString();
                AddField(result, sStart, reader.Position, $"internStrings[{i}]", value, "intern string value");
            }

            var fieldCountStart = reader.Position;
            var fieldCount = reader.ReadInt32();
            EnsureNonNegative(fieldCount, nameof(fieldCount));
            result.FieldCount = fieldCount;
            AddField(result, fieldCountStart, reader.Position, "fieldCount", fieldCount.ToString(CultureInfo.InvariantCulture), "field metadata entry count");

            var newFieldCount = 0;
            for (var i = 0; i < fieldCount; i++)
            {
                var fStart = reader.Position;
                var isNewField = reader.ReadBoolean();
                if (isNewField)
                    newFieldCount++;

                var declaringTypeIndex = reader.ReadInt32();
                var fieldName = reader.ReadString();

                var value = $"isNewField={isNewField}, declaringType={ResolveTypeName(result.ExternTypes, declaringTypeIndex)}, fieldName={fieldName}";
                if (isNewField)
                {
                    var fieldTypeIndex = reader.ReadInt32();
                    var initMethodId = reader.ReadInt32();
                    value += $", newFieldType={ResolveTypeName(result.ExternTypes, fieldTypeIndex)}, initMethodId={initMethodId}";
                }

                AddField(result, fStart, reader.Position, $"fields[{i}]", value, "field/new-field metadata");
            }

            result.NewFieldCount = newFieldCount;

            var staticFieldTypeCountStart = reader.Position;
            var staticFieldTypeCount = reader.ReadInt32();
            EnsureNonNegative(staticFieldTypeCount, nameof(staticFieldTypeCount));
            result.StaticFieldTypeCount = staticFieldTypeCount;
            AddField(result, staticFieldTypeCountStart, reader.Position, "staticFieldTypeCount", staticFieldTypeCount.ToString(CultureInfo.InvariantCulture), "count of (typeIndex, cctorMethodId)");

            for (var i = 0; i < staticFieldTypeCount; i++)
            {
                var eStart = reader.Position;
                var typeIndex = reader.ReadInt32();
                var cctorMethodId = reader.ReadInt32();
                AddField(result, eStart, reader.Position, $"staticFieldEntries[{i}]", $"type={ResolveTypeName(result.ExternTypes, typeIndex)}, cctorMethodId={cctorMethodId}", "static field type mapping");
            }

            var anonymousStoreyCountStart = reader.Position;
            var anonymousStoreyCount = reader.ReadInt32();
            EnsureNonNegative(anonymousStoreyCount, nameof(anonymousStoreyCount));
            result.AnonymousStoreyCount = anonymousStoreyCount;
            AddField(result, anonymousStoreyCountStart, reader.Position, "anonymousStoreyCount", anonymousStoreyCount.ToString(CultureInfo.InvariantCulture), "anonymous storey metadata count");

            for (var i = 0; i < anonymousStoreyCount; i++)
            {
                var storeyStart = reader.Position;
                var fieldNum = reader.ReadInt32();
                EnsureNonNegative(fieldNum, nameof(fieldNum));
                reader.Skip(fieldNum * 4L);

                var ctorId = reader.ReadInt32();
                var ctorParamNum = reader.ReadInt32();
                var interfaceCount = reader.ReadInt32();
                EnsureNonNegative(interfaceCount, nameof(interfaceCount));

                if (interfaceCount > 0)
                {
                    if (!JumpToWrappersManager(reader, input))
                        throw new InvalidDataException("Cannot recover parse position for anonymousStorey interface slots.");

                    AddField(result, storeyStart, reader.Position, $"anonymousStoreys[{i}]", $"fieldNum={fieldNum}, ctorId={ctorId}, ctorParamNum={ctorParamNum}, interfaceCount={interfaceCount}", "partial parse (interface slot lengths depend on runtime reflection)");
                    result.Notes.Add("anonymousStorey with interface slots parsed partially; parser jumped to wrappersManagerImplName marker.");
                    break;
                }

                var virtualMethodNum = reader.ReadInt32();
                EnsureNonNegative(virtualMethodNum, nameof(virtualMethodNum));
                reader.Skip(virtualMethodNum * 4L);

                AddField(result, storeyStart, reader.Position, $"anonymousStoreys[{i}]", $"fieldNum={fieldNum}, ctorId={ctorId}, ctorParamNum={ctorParamNum}, interfaceCount={interfaceCount}, virtualMethodNum={virtualMethodNum}", "anonymous storey metadata");
            }

            var wrappersStart = reader.Position;
            var wrappersName = reader.ReadString();
            result.WrappersManagerImplName = wrappersName;
            AddField(result, wrappersStart, reader.Position, "wrappersManagerImplName", wrappersName, "wrappers manager type name");

            var assemblyStart = reader.Position;
            var assemblyStr = reader.ReadString();
            result.AssemblyStr = assemblyStr;
            AddField(result, assemblyStart, reader.Position, "assemblyStr", assemblyStr, "suffix used for IFix.IDMAP* lookup");

            var fixCountStart = reader.Position;
            var fixCount = reader.ReadInt32();
            EnsureNonNegative(fixCount, nameof(fixCount));
            result.FixCount = fixCount;
            AddField(result, fixCountStart, reader.Position, "fixCount", fixCount.ToString(CultureInfo.InvariantCulture), "patched mapping entry count");

            for (var i = 0; i < fixCount; i++)
            {
                var fixStart = reader.Position;
                var methodRef = ReadMethodRef(reader, result.ExternTypes);
                var vmMethodId = reader.ReadInt32();
                AddField(result, fixStart, reader.Position, $"fixes[{i}]", $"vmMethodId={vmMethodId}, target={methodRef}", "method redirection mapping");
            }

            var newClassCountStart = reader.Position;
            var newClassCount = reader.ReadInt32();
            EnsureNonNegative(newClassCount, nameof(newClassCount));
            result.NewClassCount = newClassCount;
            AddField(result, newClassCountStart, reader.Position, "newClassCount", newClassCount.ToString(CultureInfo.InvariantCulture), "declared new class count");

            for (var i = 0; i < newClassCount; i++)
            {
                var cStart = reader.Position;
                var className = reader.ReadString();
                AddField(result, cStart, reader.Position, $"newClasses[{i}]", className, "new class full name");
            }

            result.EndOffset = reader.Position;
            result.Success = true;
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            return result;
        }
    }

    private static string BuildReport(byte[] input, ParseResult result)
    {
        var sb = new StringBuilder(256 * 1024);

        sb.AppendLine("ILFix Patch Packet View");
        sb.AppendLine("======================");
        sb.AppendLine($"fileSize: {input.Length} bytes");
        sb.AppendLine($"looksLikeIFixPatch: {LooksLikeIlFixPatch(input)}");
        sb.AppendLine($"payloadOffset: {(result.PayloadOffset >= 0 ? $"0x{result.PayloadOffset:X8}" : "N/A")}");
        sb.AppendLine($"parseStatus: {(result.Success ? "success" : "failed")}");
        if (!result.Success)
            sb.AppendLine($"parseError: {result.Error}");

        if (result.InstructionMagic != 0)
        {
            sb.AppendLine($"instructionMagic: 0x{result.InstructionMagic:X16}");
            sb.AppendLine($"instructionMagicMatchesDefault: {result.InstructionMagic == DefaultInstructionMagic}");
        }

        if (!string.IsNullOrWhiteSpace(result.InterfaceBridgeTypeName))
            sb.AppendLine($"interfaceBridgeTypeName: {result.InterfaceBridgeTypeName}");

        sb.AppendLine($"externTypeCount={result.ExternTypeCount}, methodCount={result.MethodCount}, externMethodCount={result.ExternMethodCount}");
        sb.AppendLine($"internStringsCount={result.InternStringCount}, fieldCount={result.FieldCount}, newFieldCount={result.NewFieldCount}");
        sb.AppendLine($"staticFieldTypeCount={result.StaticFieldTypeCount}, anonymousStoreyCount={result.AnonymousStoreyCount}");
        sb.AppendLine($"fixCount={result.FixCount}, newClassCount={result.NewClassCount}");
        if (result.EndOffset > 0)
            sb.AppendLine($"parsedBytes: 0x00000000 ~ 0x{Math.Max(0, result.EndOffset - 1):X8}");

        if (result.Notes.Count > 0)
        {
            sb.AppendLine("notes:");
            foreach (var note in result.Notes)
                sb.AppendLine($"- {note}");
        }

        sb.AppendLine();
        sb.AppendLine("Prefix 0x00000000 ~ payloadOffset");
        sb.AppendLine("--------------------------------");
        var prefixEnd = result.PayloadOffset >= 0 ? result.PayloadOffset : Math.Min(input.Length, 128);
        AppendRangeLines(sb, input, 0, prefixEnd, "prefixBytes", "payload prefix/custom wrapper region");

        sb.AppendLine();
        sb.AppendLine("Structured Fields");
        sb.AppendLine("-----------------");
        foreach (var field in result.Fields)
            AppendFieldLines(sb, input, field);

        if (!result.Success)
        {
            sb.AppendLine();
            sb.AppendLine("Fallback Full Hex");
            sb.AppendLine("-----------------");
            AppendRangeLines(sb, input, 0, input.Length, "rawBytes", "full file hex dump");
        }

        return sb.ToString();
    }

    private static void AppendFieldLines(StringBuilder sb, byte[] input, FieldView field)
    {
        AppendRangeLines(sb, input, field.StartOffset, field.EndOffset, field.Name, field.Value);
    }

    private static void AppendRangeLines(StringBuilder sb, byte[] input, int startOffset, int endOffsetExclusive, string name, string annotation)
    {
        if (startOffset < 0)
            startOffset = 0;

        if (endOffsetExclusive < startOffset)
            endOffsetExclusive = startOffset;

        if (endOffsetExclusive > input.Length)
            endOffsetExclusive = input.Length;

        var rowStart = AlignDown16(startOffset);
        var rowEndExclusive = AlignUp16(endOffsetExclusive);

        if (rowStart == rowEndExclusive)
            rowEndExclusive = rowStart + 16;

        var pos = rowStart;
        var firstLine = true;
        while (pos < rowEndExclusive)
        {
            sb.Append(FormatLine(input, pos, startOffset, endOffsetExclusive));
            if (firstLine)
            {
                sb.Append("  ");
                sb.Append(name);
                sb.Append(": ");
                sb.Append(annotation);
            }

            sb.AppendLine();
            pos += 16;
            firstLine = false;
        }

        sb.AppendLine();
    }

    private static string FormatLine(byte[] input, int rowOffset, int rangeStart, int rangeEndExclusive)
    {
        var tokens = new string[16];
        for (var i = 0; i < 16; i++)
        {
            var absolute = rowOffset + i;
            var inRange = absolute >= rangeStart && absolute < rangeEndExclusive;
            tokens[i] = inRange ? input[absolute].ToString("X2", CultureInfo.InvariantCulture) : "  ";
        }

        return $"{rowOffset:X8} | {string.Join(' ', tokens, 0, 8)}   {string.Join(' ', tokens, 8, 8)}";
    }

    private static int AlignDown16(int value)
    {
        return value & ~0x0F;
    }

    private static int AlignUp16(int value)
    {
        return (value + 15) & ~0x0F;
    }

    private static int DetectPayloadOffset(byte[] input)
    {
        var magicBytes = BitConverter.GetBytes(DefaultInstructionMagic);
        var direct = IndexOfBytes(input, magicBytes, 0);
        if (direct >= 0)
            return direct;

        var marker = IndexOfAscii(input, "IFix.ILFixInterfaceBridge", 0);
        if (marker >= 9)
            return marker - 9;

        return -1;
    }

    private static string ReadMethodRef(PatchReader reader, List<string> externTypes)
    {
        var isGenericInstance = reader.ReadBoolean();
        var declaringTypeIndex = reader.ReadInt32();
        var methodName = reader.ReadString();

        var sb = new StringBuilder();
        sb.Append(ResolveTypeName(externTypes, declaringTypeIndex));
        sb.Append("::");
        sb.Append(methodName);

        if (isGenericInstance)
        {
            var genericArgCount = reader.ReadInt32();
            EnsureNonNegative(genericArgCount, nameof(genericArgCount));

            var genericArgs = new List<string>(genericArgCount);
            for (var i = 0; i < genericArgCount; i++)
                genericArgs.Add(ResolveTypeName(externTypes, reader.ReadInt32()));

            sb.Append('<');
            sb.Append(string.Join(", ", genericArgs));
            sb.Append('>');

            var parameterCount = reader.ReadInt32();
            EnsureNonNegative(parameterCount, nameof(parameterCount));
            var parameters = new List<string>(parameterCount);
            for (var i = 0; i < parameterCount; i++)
            {
                var isGenericParam = reader.ReadBoolean();
                parameters.Add(isGenericParam ? reader.ReadString() : ResolveTypeName(externTypes, reader.ReadInt32()));
            }

            sb.Append('(');
            sb.Append(string.Join(", ", parameters));
            sb.Append(')');
            return sb.ToString();
        }

        var paramCount = reader.ReadInt32();
        EnsureNonNegative(paramCount, nameof(paramCount));
        var paramTypes = new List<string>(paramCount);
        for (var i = 0; i < paramCount; i++)
            paramTypes.Add(ResolveTypeName(externTypes, reader.ReadInt32()));

        sb.Append('(');
        sb.Append(string.Join(", ", paramTypes));
        sb.Append(')');
        return sb.ToString();
    }

    private static bool JumpToWrappersManager(PatchReader reader, byte[] input)
    {
        var marker = IndexOfAscii(input, "IFix.WrappersManagerImpl", reader.Position);
        if (marker < 1)
            return false;

        reader.Position = marker - 1;
        return true;
    }

    private static string ResolveTypeName(List<string> externTypes, int index)
    {
        if (index >= 0 && index < externTypes.Count)
            return externTypes[index];

        return $"<type#{index}>";
    }

    private static void AddField(ParseResult result, int startOffset, int endOffset, string name, string value, string meaning)
    {
        result.Fields.Add(new FieldView(startOffset, endOffset, name, value, meaning));
    }

    private static void EnsureNonNegative(int value, string name)
    {
        if (value < 0)
            throw new InvalidDataException($"{name} is negative: {value}");
    }

    private static int IndexOfAscii(byte[] input, string text, int startIndex)
    {
        return IndexOfBytes(input, Encoding.ASCII.GetBytes(text), startIndex);
    }

    private static int IndexOfBytes(byte[] haystack, byte[] needle, int startIndex)
    {
        if (needle.Length == 0 || needle.Length > haystack.Length)
            return -1;

        if (startIndex < 0)
            startIndex = 0;

        for (var i = startIndex; i <= haystack.Length - needle.Length; i++)
        {
            var matched = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] == needle[j])
                    continue;

                matched = false;
                break;
            }

            if (matched)
                return i;
        }

        return -1;
    }

    private sealed class ParseResult
    {
        public bool Success { get; set; }
        public string Error { get; set; } = string.Empty;
        public int PayloadOffset { get; set; } = -1;
        public int EndOffset { get; set; }
        public ulong InstructionMagic { get; set; }
        public string InterfaceBridgeTypeName { get; set; } = string.Empty;
        public int ExternTypeCount { get; set; }
        public int MethodCount { get; set; }
        public int ExternMethodCount { get; set; }
        public int InternStringCount { get; set; }
        public int FieldCount { get; set; }
        public int NewFieldCount { get; set; }
        public int StaticFieldTypeCount { get; set; }
        public int AnonymousStoreyCount { get; set; }
        public string WrappersManagerImplName { get; set; } = string.Empty;
        public string AssemblyStr { get; set; } = string.Empty;
        public int FixCount { get; set; }
        public int NewClassCount { get; set; }
        public List<string> ExternTypes { get; } = new();
        public List<string> Notes { get; } = new();
        public List<FieldView> Fields { get; } = new();
    }

    private readonly record struct FieldView(int StartOffset, int EndOffset, string Name, string Value, string Meaning);

    private sealed class PatchReader
    {
        private readonly byte[] data;

        public PatchReader(byte[] data, int startOffset)
        {
            this.data = data;
            Position = startOffset;
        }

        public int Position { get; set; }

        public ulong ReadUInt64()
        {
            EnsureReadable(8);
            var value = BitConverter.ToUInt64(data, Position);
            Position += 8;
            return value;
        }

        public int ReadInt32()
        {
            EnsureReadable(4);
            var value = BitConverter.ToInt32(data, Position);
            Position += 4;
            return value;
        }

        public bool ReadBoolean()
        {
            EnsureReadable(1);
            return data[Position++] != 0;
        }

        public string ReadString()
        {
            var length = Read7BitEncodedInt();
            if (length < 0)
                throw new InvalidDataException("Negative string length.");

            EnsureReadable(length);
            var value = Encoding.UTF8.GetString(data, Position, length);
            Position += length;
            return value;
        }

        public void Skip(long count)
        {
            if (count < 0)
                throw new InvalidDataException($"Invalid skip count: {count}");

            if (Position + count > data.Length)
                throw new EndOfStreamException("Unexpected EOF while skipping bytes.");

            Position += (int)count;
        }

        private int Read7BitEncodedInt()
        {
            var count = 0;
            var shift = 0;
            while (shift != 35)
            {
                EnsureReadable(1);
                var b = data[Position++];
                count |= (b & 0x7F) << shift;
                shift += 7;
                if ((b & 0x80) == 0)
                    return count;
            }

            throw new FormatException("Invalid 7-bit encoded integer.");
        }

        private void EnsureReadable(int count)
        {
            if (Position + count > data.Length)
                throw new EndOfStreamException("Unexpected EOF while reading patch bytes.");
        }
    }
}
