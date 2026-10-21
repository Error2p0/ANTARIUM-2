using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Unity.Mathematics;

namespace Antarium.Sim
{
    /// <summary>Uncompressed summary at the start of a save, readable without loading the rest (menus, offline time).</summary>
    public sealed class SaveHeader
    {
        public int Version;
        /// <summary>When the save was written (UTC). Offline progress is measured from here.</summary>
        public DateTime SavedUtc;
        public uint Seed;
        public double PlayTime;
        public int Ants;
        public int MaxDepth;
    }

    /// <summary>
    /// Container format: magic, header, then a deflate-compressed body of tagged sections (tag, length, bytes).
    /// Readers skip tags they don't know and use defaults for missing ones, so sections can be added without
    /// breaking old saves; <see cref="SaveGame.Version"/> and per-section versions drive real migrations.
    /// </summary>
    public static class SaveFormat
    {
        static readonly byte[] Magic = Encoding.ASCII.GetBytes("ANT2");

        public static void Write(Stream stream, SaveHeader header, IReadOnlyList<KeyValuePair<string, byte[]>> sections)
        {
            using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            w.Write(Magic);
            WriteHeader(w, header);
            w.Flush();
            using (var deflate = new DeflateStream(stream, CompressionLevel.Fastest, leaveOpen: true))
            using (var body = new BinaryWriter(deflate, Encoding.UTF8, leaveOpen: true))
            {
                body.Write(sections.Count);
                foreach (var s in sections)
                {
                    body.Write(s.Key);
                    body.Write(s.Value.Length);
                    body.Write(s.Value);
                }
            }
            stream.Flush();
        }

        public static SaveHeader ReadHeader(Stream stream)
        {
            using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            var magic = r.ReadBytes(Magic.Length);
            for (int i = 0; i < Magic.Length; i++)
                if (magic.Length != Magic.Length || magic[i] != Magic[i]) throw new InvalidDataException("Not an Antarium save file.");
            return ReadHeaderFields(r);
        }

        /// <summary>Reads the header and all sections.</summary>
        public static Dictionary<string, byte[]> Read(Stream stream, out SaveHeader header)
        {
            header = ReadHeader(stream);
            var sections = new Dictionary<string, byte[]>();
            using var deflate = new DeflateStream(stream, CompressionMode.Decompress, leaveOpen: true);
            using var r = new BinaryReader(deflate, Encoding.UTF8, leaveOpen: true);
            int count = r.ReadInt32();
            if (count < 0 || count > 4096) throw new InvalidDataException("Corrupt save: bad section count.");
            for (int i = 0; i < count; i++)
            {
                string tag = r.ReadString();
                int length = r.ReadInt32();
                if (length < 0) throw new InvalidDataException($"Corrupt save: bad length for section '{tag}'.");
                var data = r.ReadBytes(length);
                if (data.Length != length) throw new EndOfStreamException($"Save ends inside section '{tag}'.");
                sections[tag] = data;
            }
            return sections;
        }

        static void WriteHeader(BinaryWriter w, SaveHeader h)
        {
            w.Write(h.Version);
            w.Write(h.SavedUtc.Ticks);
            w.Write(h.Seed);
            w.Write(h.PlayTime);
            w.Write(h.Ants);
            w.Write(h.MaxDepth);
        }

        static SaveHeader ReadHeaderFields(BinaryReader r) => new()
        {
            Version = r.ReadInt32(),
            SavedUtc = new DateTime(r.ReadInt64(), DateTimeKind.Utc),
            Seed = r.ReadUInt32(),
            PlayTime = r.ReadDouble(),
            Ants = r.ReadInt32(),
            MaxDepth = r.ReadInt32(),
        };
    }

    /// <summary>Small helpers for the section payloads.</summary>
    static class SaveIO
    {
        public static void Write(this BinaryWriter w, int2 v)
        {
            w.Write(v.x);
            w.Write(v.y);
        }

        public static void Write(this BinaryWriter w, float2 v)
        {
            w.Write(v.x);
            w.Write(v.y);
        }

        public static int2 ReadInt2(this BinaryReader r) => new(r.ReadInt32(), r.ReadInt32());
        public static float2 ReadFloat2(this BinaryReader r) => new(r.ReadSingle(), r.ReadSingle());

        public static void WriteIds(this BinaryWriter w, IReadOnlyList<string> ids)
        {
            w.Write(ids.Count);
            foreach (var id in ids) w.Write(id ?? string.Empty);
        }

        public static string[] ReadIds(this BinaryReader r)
        {
            int n = r.ReadInt32();
            if (n < 0 || n > 1 << 20) throw new InvalidDataException("Corrupt save: bad id table.");
            var ids = new string[n];
            for (int i = 0; i < n; i++) ids[i] = r.ReadString();
            return ids;
        }
    }
}
