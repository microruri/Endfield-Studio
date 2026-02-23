using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Endfield.Tool.CLI.Decode.SparkBuffer;

/// <summary>
/// Decoder for Beyond patch bytes (SparkBuffer format).
/// </summary>
public sealed class SparkBytesDecoder
{
    private byte[] data;
    private readonly SparkScheme scheme = new();
    private int typeDefsPtr;
    private int rootDefPtr;
    private int dataPtr;
    private int stringPtr;

    public SparkBytesDecoder(byte[] bytes)
    {
        data = bytes ?? throw new ArgumentNullException(nameof(bytes));
    }

    public JToken Load()
    {
        ReadHeader();
        ExportTypeDefs();
        ExportRootDef();
        ExportStrings();
        return ExportDataFromRoot();
    }

    private void ReadHeader()
    {
        typeDefsPtr = BitConverter.ToInt32(data, 0);
        rootDefPtr = BitConverter.ToInt32(data, 4);
        dataPtr = BitConverter.ToInt32(data, 8);
    }

    private void ExportTypeDefs()
    {
        var pos = Utils.Align(typeDefsPtr, 4);
        var count = BinaryStream.ReadInt32(ref data, ref pos);

        for (var i = 0; i < count; i++)
        {
            var tag = data[pos++];
            pos = Utils.Align(pos, 4);

            if (tag == (byte)SparkType.Bean)
            {
                var bean = new SparkBean
                {
                    BeanType =
                    {
                        TypeHash = BinaryStream.ReadInt32(ref data, ref pos),
                        Name = BinaryStream.ReadCString(ref data, ref pos)
                    }
                };

                pos = Utils.Align(pos, 4);
                var fieldCount = BinaryStream.ReadInt32(ref data, ref pos);
                var currentPos = 0;
                for (var j = 0; j < fieldCount; j++)
                {
                    var field = ParseField(ref pos, ref currentPos);
                    bean.Fields.Add(field);
                }

                scheme.Beans[bean.BeanType.TypeHash] = bean;
            }
            else if (tag == (byte)SparkType.Enum)
            {
                var enu = new SparkEnum
                {
                    TypeHash = BinaryStream.ReadInt32(ref data, ref pos),
                    Name = BinaryStream.ReadCString(ref data, ref pos)
                };

                pos = Utils.Align(pos, 4);
                var itemCount = BinaryStream.ReadInt32(ref data, ref pos);

                for (var j = 0; j < itemCount; j++)
                {
                    var fieldName = BinaryStream.ReadCString(ref data, ref pos);
                    pos = Utils.Align(pos, 4);
                    var fieldValue = BinaryStream.ReadInt32(ref data, ref pos);
                    enu.Items[fieldName] = fieldValue;
                }

                scheme.Enums[enu.TypeHash] = enu;
            }
        }
    }

    private void ExportRootDef()
    {
        var pos = rootDefPtr;
        scheme.Root.RootType = (SparkType)data[pos++];
        scheme.Root.RootName = BinaryStream.ReadCString(ref data, ref pos);
        switch (scheme.Root.RootType)
        {
            case SparkType.Bean:
                pos = Utils.Align(pos, 4);
                scheme.Root.RootTypeHash = BinaryStream.ReadInt32(ref data, ref pos);
                break;
            case SparkType.Array:
                scheme.Root.SubType1 = (SparkType)data[pos++];
                if (scheme.Root.SubType1 == SparkType.Enum || scheme.Root.SubType1 == SparkType.Bean)
                {
                    scheme.Root.SubTypeHash1 = BinaryStream.ReadInt32(ref data, ref pos);
                    scheme.Root.RootTypeHash = scheme.Root.SubTypeHash1;
                }

                break;
            case SparkType.Map:
                scheme.Root.SubType1 = (SparkType)data[pos++];
                if (scheme.Root.SubType1 == SparkType.Enum)
                {
                    pos = Utils.Align(pos, 4);
                    scheme.Root.SubTypeHash1 = BinaryStream.ReadInt32(ref data, ref pos);
                }

                scheme.Root.SubType2 = (SparkType)data[pos++];
                if (scheme.Root.SubType2 == SparkType.Enum || scheme.Root.SubType2 == SparkType.Bean)
                {
                    pos = Utils.Align(pos, 4);
                    scheme.Root.SubTypeHash2 = BinaryStream.ReadInt32(ref data, ref pos);
                }

                break;
        }

        stringPtr = pos;
    }

    private void ExportStrings()
    {
        var pos = Utils.Align(stringPtr, 4);
        var stringCount = BinaryStream.ReadInt32(ref data, ref pos);
        for (var i = 0; i < stringCount; i++)
            scheme.StringPool[pos] = BinaryStream.ReadCString(ref data, ref pos);
    }

    private JToken ExportDataFromRoot()
    {
        return scheme.Root.RootType switch
        {
            SparkType.Bean => ExportBeanData(dataPtr, scheme.Root.RootTypeHash),
            SparkType.Map => ExportMapData(dataPtr, scheme.Root.SubType1, scheme.Root.SubTypeHash1, scheme.Root.SubType2, scheme.Root.SubTypeHash2),
            SparkType.Array => ExportArrayData(dataPtr, scheme.Root.SubType1, scheme.Root.RootTypeHash),
            _ => JValue.CreateString("Unknown Root type")
        };
    }

    private Field ParseField(ref int pos, ref int currentPos)
    {
        var fieldName = BinaryStream.ReadCString(ref data, ref pos);
        var type = (SparkType)data[pos++];
        Field field;

        switch (type)
        {
            case SparkType.Bean:
            case SparkType.Enum:
                pos = Utils.Align(pos, 4);
                field = new BeanField
                {
                    BeanType =
                    {
                        TypeHash = BinaryStream.ReadInt32(ref data, ref pos)
                    }
                };
                break;

            case SparkType.Array:
                var arrayField = new ArrayField
                {
                    ElementType = (SparkType)data[pos++]
                };
                if (arrayField.ElementType == SparkType.Enum || arrayField.ElementType == SparkType.Bean)
                {
                    pos = Utils.Align(pos, 4);
                    arrayField.ElementTypeHash = BinaryStream.ReadInt32(ref data, ref pos);
                }

                field = arrayField;
                break;

            case SparkType.Map:
                var mapField = new MapField
                {
                    KeyType = (SparkType)data[pos++]
                };
                if (mapField.KeyType == SparkType.Enum || mapField.KeyType == SparkType.Bean)
                {
                    pos = Utils.Align(pos, 4);
                    mapField.KeyTypeHash = BinaryStream.ReadInt32(ref data, ref pos);
                }

                mapField.ValueType = (SparkType)data[pos++];
                if (mapField.ValueType == SparkType.Enum || mapField.ValueType == SparkType.Bean)
                {
                    pos = Utils.Align(pos, 4);
                    mapField.ValueTypeHash = BinaryStream.ReadInt32(ref data, ref pos);
                }

                field = mapField;
                break;

            default:
                field = new Field();
                break;
        }

        field.Name = fieldName;
        field.Type = type;
        currentPos = Utils.Align(currentPos, Utils.GetAlignment(type));
        field.Offset = currentPos;
        currentPos += field.GetSize();
        return field;
    }

    private JObject ExportMapData(int addr, SparkType keyType, int keyTypeHash, SparkType valueType, int valueTypeHash)
    {
        var pos = addr;
        var result = new JObject();
        var slots = new List<HashSlot>();
        var slotCount = BinaryStream.ReadInt32(ref data, ref pos);
        for (var i = 0; i < slotCount; i++)
            slots.Add(new HashSlot(BinaryStream.ReadInt32(ref data, ref pos), BinaryStream.ReadInt32(ref data, ref pos)));

        foreach (var slot in slots)
        {
            if (slot.BucketSize <= 0)
                continue;

            var entryPos = slot.Offset;
            for (var j = 0; j < slot.BucketSize; j++)
            {
                var key = ExportElementData(keyType, keyTypeHash, ref entryPos);
                var value = ExportElementData(valueType, valueTypeHash, ref entryPos);
                result[key?.ToString() ?? string.Empty] = value;
            }
        }

        return result;
    }

    private JObject ExportBeanData(int addr, int typeHash)
    {
        if (!scheme.Beans.TryGetValue(typeHash, out var bean))
            return new JObject { ["_error"] = $"Type {typeHash} not found" };

        var result = new JObject();
        foreach (var field in bean.Fields)
        {
            var fieldOffset = field.Offset + addr;
            var refHash = 0;
            if (field is BeanField beanField)
                refHash = beanField.BeanType.TypeHash;
            else if (field is ArrayField arrayField)
                refHash = arrayField.ElementTypeHash;

            result[field.Name] = ExportElementData(field.Type, refHash, ref fieldOffset, field);
        }

        return result;
    }

    private JArray ExportArrayData(int addr, SparkType type, int typeHash)
    {
        var pos = Utils.Align(addr, Utils.GetAlignment(type));
        var result = new JArray();
        var count = BinaryStream.ReadInt32(ref data, ref pos);
        for (var i = 0; i < count; i++)
        {
            var item = ExportElementData(type, typeHash, ref pos);
            result.Add(item ?? JValue.CreateNull());
        }

        return result;
    }

    private JToken? ExportElementData(SparkType type, int typeHash, ref int currentPos, Field? field = null)
    {
        currentPos = Utils.Align(currentPos, Utils.GetAlignment(type));

        switch (type)
        {
            case SparkType.Bool:
                return data[currentPos++] != 0;
            case SparkType.Byte:
                return data[currentPos++];
            case SparkType.Int:
                return (uint)BinaryStream.ReadInt32(ref data, ref currentPos);
            case SparkType.Long:
                return (ulong)BinaryStream.ReadInt64(ref data, ref currentPos);
            case SparkType.Float:
                return BinaryStream.ReadSingle(ref data, ref currentPos);
            case SparkType.Double:
                return BinaryStream.ReadDouble(ref data, ref currentPos);
            case SparkType.Enum:
                var enumValue = BinaryStream.ReadInt32(ref data, ref currentPos);
                if (scheme.Enums.TryGetValue(typeHash, out var enu))
                {
                    var pair = enu.Items.FirstOrDefault(kvp => kvp.Value == enumValue);
                    if (pair.Key != null)
                        return pair.Key;
                }

                return enumValue;
            case SparkType.String:
                var offset = BinaryStream.ReadInt32(ref data, ref currentPos);
                if (offset <= 0)
                    return null;

                return scheme.StringPool.TryGetValue(offset, out var str) ? str : string.Empty;
            case SparkType.Bean:
                var beanPtr = BinaryStream.ReadInt32(ref data, ref currentPos);
                if (beanPtr <= 0)
                    return null;

                return ExportBeanData(beanPtr, typeHash);
            case SparkType.Array:
                var arrayPtr = BinaryStream.ReadInt32(ref data, ref currentPos);
                if (arrayPtr <= 0)
                    return null;

                var elementType = field is ArrayField af ? af.ElementType : SparkType.Bean;
                return ExportArrayData(arrayPtr, elementType, typeHash);
            case SparkType.Map:
                var mapPtr = BinaryStream.ReadInt32(ref data, ref currentPos);
                if (mapPtr <= 0)
                    return null;

                if (field is not MapField mf)
                    return null;

                return ExportMapData(mapPtr, mf.KeyType, mf.KeyTypeHash, mf.ValueType, mf.ValueTypeHash);
            default:
                return "Parse Error";
        }
    }
}
