using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
using BuffInspector.Domain;

namespace BuffInspector.Parsing;

internal static partial class InspectDecoder
{
    public static ItemData Decode(string link)
    {
        var match = InspectPattern().Match(Uri.UnescapeDataString(link));
        if (!match.Success)
        {
            return new ItemData();
        }
        var hex = match.Groups[1].Value;
        if (hex.Length is < 12 or > 65536 || hex.Length % 2 != 0)
        {
            throw new BuffException("检视数据长度无效。");
        }
        var bytes = Convert.FromHexString(hex);
        var mask = bytes[0];
        for (var index = 0; index < bytes.Length; index++)
        {
            bytes[index] ^= mask;
        }
        var reader = new ProtoReader(bytes.AsSpan(1, bytes.Length - 5));
        var result = new ItemData();
        var stickers = new List<Decoration>();
        var keychains = new List<Decoration>();
        while (!reader.AtEnd)
        {
            var (field, wire) = reader.ReadTag();
            if (wire == 0 && field is 3 or 4 or 7 or 8)
            {
                var number = reader.ReadVarint();
                if (number > uint.MaxValue || (field != 7 && number > int.MaxValue))
                {
                    throw new BuffException("检视数据数值溢出。");
                }
                result = field switch
                {
                    3 => result with { DefinitionIndex = (int)number },
                    4 => result with { PaintIndex = (int)number },
                    7 => result with { PaintWear = BitConverter.UInt32BitsToSingle((uint)number) },
                    _ => result with { PaintSeed = (int)number },
                };
            }
            else if (wire == 2 && field is 11 or 12 or 20)
            {
                var payload = reader.ReadBytes();
                if (field == 11)
                {
                    result = result with { NameTag = Encoding.UTF8.GetString(payload) };
                }
                else
                {
                    (field == 12 ? stickers : keychains).Add(ReadDecoration(payload));
                }
            }
            else
            {
                reader.Skip(wire);
            }
        }
        return result with { Stickers = stickers, Keychains = keychains };
    }

    private static Decoration ReadDecoration(ReadOnlySpan<byte> payload)
    {
        var reader = new ProtoReader(payload);
        var result = new Decoration(0, 0, "");
        while (!reader.AtEnd)
        {
            var (field, wire) = reader.ReadTag();
            if (wire == 0 && field is 1 or 2 or 10)
            {
                var value = reader.ReadVarint();
                if (value > int.MaxValue)
                {
                    throw new BuffException("检视饰品数值溢出。");
                }
                result = field switch
                {
                    1 => result with { Slot = (int)value },
                    2 => result with { Id = (int)value },
                    _ => result with { Seed = (int)value },
                };
            }
            else if (wire == 5)
            {
                var value = reader.ReadFloat();
                result = field switch
                {
                    3 => result with { Wear = value },
                    4 => result with { Scale = value },
                    5 => result with { Rotation = value },
                    7 => result with { OffsetX = value },
                    8 => result with { OffsetY = value },
                    9 => result with { OffsetZ = value },
                    _ => result,
                };
            }
            else
            {
                reader.Skip(wire);
            }
        }
        return result;
    }

    [GeneratedRegex(@"(?:csgo_econ_action_preview\s+|^)([0-9a-fA-F]{12,})(?:$|[?\s])")]
    private static partial Regex InspectPattern();

    private ref struct ProtoReader(ReadOnlySpan<byte> data)
    {
        private ReadOnlySpan<byte> remaining = data;
        public readonly bool AtEnd => remaining.IsEmpty;

        public (int Field, int Wire) ReadTag()
        {
            var tag = ReadVarint();
            if (tag is < 8 or > uint.MaxValue)
            {
                throw new BuffException("检视数据字段无效。");
            }
            return ((int)(tag >> 3), (int)(tag & 7));
        }

        public ulong ReadVarint()
        {
            ulong value = 0;
            for (var shift = 0; shift < 64; shift += 7)
            {
                var current = Take(1)[0];
                if (shift == 63 && current > 1)
                {
                    break;
                }
                value |= (ulong)(current & 127) << shift;
                if (current < 128)
                {
                    return value;
                }
            }
            throw new BuffException("检视数据整数编码无效。");
        }

        public ReadOnlySpan<byte> ReadBytes()
        {
            var length = ReadVarint();
            if (length > int.MaxValue)
            {
                throw new BuffException("检视数据字段过长。");
            }
            return Take((int)length);
        }

        public float ReadFloat()
        {
            var value = BinaryPrimitives.ReadSingleLittleEndian(Take(4));
            return float.IsFinite(value) ? value : throw new BuffException("检视数据浮点数无效。");
        }

        public void Skip(int wire)
        {
            switch (wire)
            {
                case 0:
                    ReadVarint();
                    break;
                case 1:
                    Take(8);
                    break;
                case 2:
                    ReadBytes();
                    break;
                case 5:
                    Take(4);
                    break;
                default:
                    throw new BuffException("检视数据编码不受支持。");
            }
        }

        private ReadOnlySpan<byte> Take(int length)
        {
            if (length > remaining.Length)
            {
                throw new BuffException("检视数据不完整。");
            }
            var value = remaining[..length];
            remaining = remaining[length..];
            return value;
        }
    }
}
