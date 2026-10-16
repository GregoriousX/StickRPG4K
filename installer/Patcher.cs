using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

public sealed class SR4KTag {
    public int Code;
    public byte[] Data;
    public SR4KTag(int code, byte[] data) { Code = code; Data = data; }
}

public static class StickRpg4kPatch {
    private static byte[] Digest(byte[] data) {
        using (SHA256 hash = SHA256.Create()) return hash.ComputeHash(data);
    }
    private static bool Equal(byte[] a, byte[] b) {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }
    private static byte[] ReadExact(BinaryReader reader, int count) {
        if (count < 0) throw new InvalidDataException("Negative record size.");
        byte[] data = reader.ReadBytes(count);
        if (data.Length != count) throw new EndOfStreamException();
        return data;
    }
    public static List<SR4KTag> ReadTags(string path) {
        byte[] raw = File.ReadAllBytes(path);
        byte[] expanded;
        if (raw.Length < 14) throw new InvalidDataException("Invalid SWF.");
        if (raw[0] == (byte)'C' && raw[1] == (byte)'W' && raw[2] == (byte)'S') {
            using (MemoryStream output = new MemoryStream()) {
                output.Write(raw, 0, 8);
                using (MemoryStream input = new MemoryStream(raw, 10, raw.Length - 14))
                using (DeflateStream deflate = new DeflateStream(input, CompressionMode.Decompress))
                    deflate.CopyTo(output);
                expanded = output.ToArray();
                expanded[0] = (byte)'F';
            }
        } else if (raw[0] == (byte)'F' && raw[1] == (byte)'W' && raw[2] == (byte)'S') {
            expanded = raw;
        } else throw new InvalidDataException("Unsupported SWF compression.");
        if (BitConverter.ToUInt32(expanded, 4) != expanded.Length)
            throw new InvalidDataException("SWF length mismatch.");
        int rectBits = expanded[8] >> 3;
        int position = 8 + (5 + 4 * rectBits + 7) / 8 + 4;
        List<SR4KTag> tags = new List<SR4KTag>();
        using (MemoryStream stream = new MemoryStream(expanded))
        using (BinaryReader reader = new BinaryReader(stream)) {
            stream.Position = position;
            while (stream.Position < stream.Length) {
                int word = reader.ReadUInt16();
                int code = word >> 6, length = word & 63;
                if (length == 63) length = checked((int)reader.ReadUInt32());
                tags.Add(new SR4KTag(code, ReadExact(reader, length)));
                if (code == 0) break;
            }
            if (stream.Position != stream.Length) throw new InvalidDataException("Unexpected SWF trailer.");
        }
        return tags;
    }
    public static byte[] Asset(string path, long offset, int length) {
        using (FileStream stream = File.OpenRead(path))
        using (BinaryReader reader = new BinaryReader(stream)) {
            if (offset < 0 || length < 0 || offset + length > stream.Length)
                throw new InvalidDataException("Invalid asset range.");
            stream.Position = offset;
            return ReadExact(reader, length);
        }
    }
    public static SR4KTag Addon(byte[] prefix, byte[] abc) {
        byte[] data = new byte[prefix.Length + abc.Length];
        Buffer.BlockCopy(prefix, 0, data, 0, prefix.Length);
        Buffer.BlockCopy(abc, 0, data, prefix.Length, abc.Length);
        return new SR4KTag(82, data);
    }
    public static SR4KTag PatchAbc(SR4KTag source, byte[] prefix, string patchPath) {
        if (source.Code != 82) throw new InvalidDataException("Expected original code tag.");
        int start = Array.IndexOf<byte>(source.Data, 0, 4) + 1;
        if (start < 5) throw new InvalidDataException("Invalid code tag.");
        byte[] old = new byte[source.Data.Length - start];
        Buffer.BlockCopy(source.Data, start, old, 0, old.Length);
        using (FileStream file = File.OpenRead(patchPath))
        using (GZipStream gzip = new GZipStream(file, CompressionMode.Decompress))
        using (BinaryReader reader = new BinaryReader(gzip))
        using (MemoryStream output = new MemoryStream()) {
            if (Encoding.ASCII.GetString(ReadExact(reader, 8)) != "SR4KDEL1")
                throw new InvalidDataException("Unknown patch format.");
            int sourceSize = reader.ReadInt32(), targetSize = reader.ReadInt32();
            byte[] sourceHash = ReadExact(reader, 32), targetHash = ReadExact(reader, 32);
            if (sourceSize != old.Length || !Equal(sourceHash, Digest(old)))
                throw new InvalidDataException("Original game code does not match this patch.");
            int commands = reader.ReadInt32();
            if (commands < 0 || commands > 100000) throw new InvalidDataException("Invalid patch commands.");
            for (int i = 0; i < commands; i++) {
                int operation = reader.ReadByte();
                if (operation == 0) {
                    int offset = reader.ReadInt32(), length = reader.ReadInt32();
                    if (offset < 0 || length < 0 || (long)offset + length > old.Length)
                        throw new InvalidDataException("Invalid original code range.");
                    output.Write(old, offset, length);
                } else if (operation == 1) {
                    byte[] literal = ReadExact(reader, reader.ReadInt32());
                    output.Write(literal, 0, literal.Length);
                } else throw new InvalidDataException("Unknown patch operation.");
                if (output.Length > targetSize) throw new InvalidDataException("Patch output exceeds target.");
            }
            byte[] result = output.ToArray();
            if (result.Length != targetSize || !Equal(targetHash, Digest(result)))
                throw new InvalidDataException("Patched code checksum mismatch.");
            if (reader.Read() != -1) throw new InvalidDataException("Unexpected patch trailer.");
            return Addon(prefix, result);
        }
    }
    public static void WriteSwf(string path, byte[] header, List<SR4KTag> tags) {
        using (FileStream stream = new FileStream(path, FileMode.CreateNew))
        using (BinaryWriter writer = new BinaryWriter(stream)) {
            writer.Write(header);
            foreach (SR4KTag tag in tags) {
                int length = tag.Data.Length;
                writer.Write((ushort)((tag.Code << 6) | Math.Min(length, 63)));
                if (length >= 63) writer.Write((uint)length);
                writer.Write(tag.Data);
            }
            writer.Flush();
            if (stream.Length != BitConverter.ToUInt32(header, 4))
                throw new InvalidDataException("Rebuilt SWF length mismatch.");
        }
    }

    // Copy original MP3 frames from the user's album. No decoding, resampling or
    // re-encoding: retain the game's sample count and trim to the matching bar.
    public static SR4KTag SoundtrackSound(SR4KTag original, string path, int startSample) {
        if (original.Code != 14 || original.Data.Length < 9 || original.Data[2] != 47)
            throw new InvalidDataException("Expected a 44.1 kHz stereo MP3 sound.");
        if (startSample < 0) throw new InvalidDataException("Invalid soundtrack trim.");
        byte[] mp3 = File.ReadAllBytes(path);
        int position = 0;
        if (mp3.Length >= 10 && Encoding.ASCII.GetString(mp3, 0, 3) == "ID3") {
            int size = 0;
            for (int i = 6; i < 10; i++) {
                if (mp3[i] > 127) throw new InvalidDataException("Invalid ID3 size.");
                size = checked((size << 7) | mp3[i]);
            }
            position = checked(size + 10 + ((mp3[5] & 16) != 0 ? 10 : 0));
        }
        List<int> offsets = new List<int>();
        int[] bitrates = {0,32,40,48,56,64,80,96,112,128,160,192,224,256,320,0};
        while (position + 4 <= mp3.Length) {
            if (position + 128 == mp3.Length && Encoding.ASCII.GetString(mp3, position, 3) == "TAG") break;
            uint h = ((uint)mp3[position] << 24) | ((uint)mp3[position + 1] << 16) |
                     ((uint)mp3[position + 2] << 8) | mp3[position + 3];
            int bitrate = bitrates[(h >> 12) & 15];
            if ((h & 0xfffe0c00u) != 0xfffa0000u || bitrate == 0 || ((h >> 6) & 3) == 3)
                throw new InvalidDataException("Expected MPEG-1 Layer III, 44.1 kHz stereo soundtrack.");
            int length = 144000 * bitrate / 44100 + (int)((h >> 9) & 1);
            if (position + length > mp3.Length) throw new InvalidDataException("Truncated MP3 frame.");
            offsets.Add(position);
            position += length;
        }
        offsets.Add(position);
        int count = checked((int)BitConverter.ToUInt32(original.Data, 3));
        // Ten frames warm up the MP3 bit reservoir and synthesis filters before
        // the audible sample. Two trailing frames cover decoder latency.
        int first = Math.Max(0, startSample / 1152 - 10);
        int last = checked((startSample + count + 1151) / 1152 + 2);
        if (last >= offsets.Count) throw new InvalidDataException("Soundtrack excerpt exceeds the source.");
        int seek = startSample - first * 1152;
        if (seek > Int16.MaxValue) throw new InvalidDataException("Soundtrack trim exceeds SWF range.");
        byte[] data = new byte[9 + offsets[last] - offsets[first]];
        Buffer.BlockCopy(original.Data, 0, data, 0, 7);
        Buffer.BlockCopy(BitConverter.GetBytes((short)seek), 0, data, 7, 2);
        Buffer.BlockCopy(mp3, offsets[first], data, 9, data.Length - 9);
        if (first > 0) {
            if ((data[10] & 1) == 0) throw new InvalidDataException("CRC-protected MP3 primer is unsupported.");
            // An excerpt starts mid-reservoir. Silence only its first, inaudible
            // pre-roll frame so every decoder emits it instead of dropping it.
            // Keep the main-data bytes: following frames need that reservoir.
            Array.Clear(data, 13, 32);
        }
        return new SR4KTag(14, data);
    }

    public static void WriteResizedSwf(string path, byte[] header, List<SR4KTag> tags) {
        header = (byte[])header.Clone();
        long length = header.Length;
        foreach (SR4KTag tag in tags) length += (tag.Data.Length >= 63 ? 6 : 2) + tag.Data.Length;
        Buffer.BlockCopy(BitConverter.GetBytes(checked((uint)length)), 0, header, 4, 4);
        WriteSwf(path, header, tags);
    }
}
