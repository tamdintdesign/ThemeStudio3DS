using System;
using System.IO;

namespace ThemeStudio3DS.Services;

internal static class Lz11Encoder
{
    public static byte[] Encode(ReadOnlySpan<byte> data)
    {
        if (data.Length is < 1 or > 0xFFFFFF)
            throw new ArgumentOutOfRangeException(nameof(data));

        using var output = new MemoryStream();
        output.WriteByte(0x11);
        output.WriteByte((byte)data.Length);
        output.WriteByte((byte)(data.Length >> 8));
        output.WriteByte((byte)(data.Length >> 16));

        // Keep the most recent position in each hash bucket. Verify every byte before emitting a match.
        var previous = new int[1 << 16];
        Array.Fill(previous, -1);
        Span<byte> group = stackalloc byte[1 + 8 * 3];
        int position = 0;
        while (position < data.Length)
        {
            byte flags = 0;
            int used = 1;
            for (int bit = 7; bit >= 0 && position < data.Length; bit--)
            {
                int length = 0;
                int distance = 0;
                if (position + 2 < data.Length)
                {
                    int match = previous[Hash(data, position)];
                    distance = position - match;
                    if (match >= 0 && distance <= 4096)
                    {
                        int limit = Math.Min(272, data.Length - position);
                        while (length < limit && data[match + length] == data[position + length])
                            length++;
                    }
                }

                if (length < 3)
                {
                    group[used++] = data[position];
                    length = 1;
                }
                else
                {
                    flags |= (byte)(1 << bit);
                    int offset = distance - 1;
                    if (length <= 16)
                        group[used++] = (byte)(((length - 1) << 4) | (offset >> 8));
                    else
                    {
                        int extended = length - 17;
                        group[used++] = (byte)(extended >> 4);
                        group[used++] = (byte)((extended << 4) | (offset >> 8));
                    }
                    group[used++] = (byte)offset;
                }

                int end = position + length;
                for (; position < end && position + 2 < data.Length; position++)
                    previous[Hash(data, position)] = position;
                position = end;
            }
            group[0] = flags;
            output.Write(group[..used]);
        }

        while (output.Length % 4 != 0)
            output.WriteByte(0);
        return output.ToArray();
    }

    private static int Hash(ReadOnlySpan<byte> data, int position) =>
        ((data[position] * 251 + data[position + 1]) * 251 + data[position + 2]) & 0xFFFF;
}
