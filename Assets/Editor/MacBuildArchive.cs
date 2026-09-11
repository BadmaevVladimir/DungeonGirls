using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;

// bsdtar handles bundle traversal and symlinks. Only ZIP central-directory metadata is
// patched here: compressed bytes, link targets, extra fields and directory structure stay intact.
internal static class MacBuildArchive
{
    internal static void ValidateApp(string app)
    {
        var plist = new XmlDocument { XmlResolver = null };
        plist.Load(Path.Combine(app, "Contents", "Info.plist"));
        string executable = plist.SelectSingleNode("/plist/dict/key[.='CFBundleExecutable']/following-sibling::string[1]")?.InnerText;
        if (string.IsNullOrEmpty(executable) || executable.IndexOfAny(new[] { '/', '\\' }) >= 0)
            throw new IOException("Invalid CFBundleExecutable in Info.plist.");
        string binary = Path.Combine(app, "Contents", "MacOS", executable);
        RequireUniversal(binary);
        string player = Path.Combine(app, "Contents", "Frameworks", "UnityPlayer.dylib");
        if (File.Exists(player)) RequireUniversal(player);
        if (!Directory.Exists(Path.Combine(app, "Contents", "Resources")))
            throw new IOException("Incomplete .app: Contents/Resources is missing.");
    }

    static uint ReadBigEndian(BinaryReader reader)
    {
        byte[] bytes = reader.ReadBytes(4);
        if (bytes.Length != 4) throw new EndOfStreamException();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    internal static void RequireUniversal(string path)
    {
        using (var reader = new BinaryReader(File.OpenRead(path)))
        {
            uint magic = ReadBigEndian(reader);
            if (magic != 0xcafebabe && magic != 0xcafebabf) throw new IOException("Not a Universal Mach-O binary: " + path);
            uint count = ReadBigEndian(reader);
            if (count > 64) throw new IOException("Invalid Mach-O architecture table: " + path);
            var cpus = new HashSet<uint>();
            for (int i = 0; i < count; i++)
            {
                cpus.Add(ReadBigEndian(reader));
                reader.BaseStream.Seek(magic == 0xcafebabf ? 28 : 16, SeekOrigin.Current);
            }
            if (!cpus.Contains(0x01000007) || !cpus.Contains(0x0100000c))
                throw new IOException("Universal build must contain both x64 and ARM64: " + path);
        }
    }

    internal static void SetUnixPermissions(string zip)
    {
        using (var stream = new FileStream(zip, FileMode.Open, FileAccess.ReadWrite))
        using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            long eocd = -1;
            // Locate the real EOCD (including any comment), not a signature inside compressed data.
            for (long p = stream.Length - 22; p >= Math.Max(0, stream.Length - 65557); p--)
            {
                stream.Position = p;
                if (reader.ReadUInt32() != 0x06054b50) continue;
                stream.Position = p + 20;
                if (p + 22 + reader.ReadUInt16() == stream.Length) { eocd = p; break; }
            }
            if (eocd < 0) throw new IOException("Invalid ZIP: end record missing.");
            stream.Position = eocd + 10;
            long count = reader.ReadUInt16();
            reader.ReadUInt32();
            long central = reader.ReadUInt32();
            if (count == ushort.MaxValue || central == uint.MaxValue)
            {
                stream.Position = eocd - 20;
                if (reader.ReadUInt32() != 0x07064b50) throw new IOException("ZIP64 locator missing.");
                reader.ReadUInt32();
                long zip64 = checked((long)reader.ReadUInt64());
                stream.Position = zip64;
                if (reader.ReadUInt32() != 0x06064b50) throw new IOException("ZIP64 end record missing.");
                stream.Position = zip64 + 32;
                count = checked((long)reader.ReadUInt64());
                reader.ReadUInt64();
                central = checked((long)reader.ReadUInt64());
            }
            stream.Position = central;
            for (long i = 0; i < count; i++)
            {
                long start = stream.Position;
                if (reader.ReadUInt32() != 0x02014b50) throw new IOException("Invalid ZIP central directory.");
                stream.Position = start + 28;
                int nameLength = reader.ReadUInt16(), extraLength = reader.ReadUInt16(), commentLength = reader.ReadUInt16();
                stream.Position = start + 38;
                uint attributes = reader.ReadUInt32();
                stream.Position = start + 46;
                string name = Encoding.UTF8.GetString(reader.ReadBytes(nameLength));
                uint type = (attributes >> 16) & 0xf000;
                // Keep symlink type + mode untouched. All regular bundle files receive 0755;
                // this deliberately includes extensionless helpers and framework executables.
                if (type != 0xa000)
                    attributes = ((name.EndsWith("/", StringComparison.Ordinal) ? 0x41edu : 0x81edu) << 16) | (attributes & 0xffff);
                stream.Position = start + 5;
                writer.Write((byte)3); // ZIP "version made by" host = Unix, including on Windows.
                stream.Position = start + 38;
                writer.Write(attributes);
                stream.Position = start + 46 + nameLength + extraLength + commentLength;
            }
        }
    }

    internal static void ValidateZip(string path)
    {
        using (var zip = ZipFile.OpenRead(path))
        {
            const string root = "DungeonGirls_Mac/";
            foreach (string required in new[] { "First Launch.command", "README.txt", "DungeonGirls.app/Contents/Info.plist" })
                if (zip.GetEntry(root + required) == null) throw new IOException("ZIP entry missing: " + required);
            foreach (var entry in zip.Entries)
            {
                if (!entry.FullName.StartsWith(root, StringComparison.Ordinal) || entry.FullName.Contains("\\")
                    || entry.FullName.Split('/').Any(p => p == "..")) throw new IOException("Unsafe ZIP path: " + entry.FullName);
                int mode = (entry.ExternalAttributes >> 16) & 0xffff;
                if ((mode & 0xf000) != 0xa000 && (mode & 0x49) != 0x49)
                    throw new IOException("ZIP executable permissions missing: " + entry.FullName);
                using (var contents = entry.Open())
                {
                    if ((mode & 0xf000) == 0xa000)
                    {
                        using (var reader = new StreamReader(contents))
                        {
                            string target = reader.ReadToEnd();
                            if (string.IsNullOrEmpty(target) || target.StartsWith("/") || target.Contains("\\") || target.Contains(":"))
                                throw new IOException("Non-portable symlink in ZIP: " + entry.FullName + " -> " + target);
                            int depth = entry.FullName.Split('/').Length - 2;
                            foreach (string part in target.Split('/'))
                            {
                                if (part == "..") depth--;
                                else if (part != "." && part != "") depth++;
                                if (depth < 0) throw new IOException("Symlink leaves package: " + entry.FullName);
                            }
                        }
                    }
                    else contents.CopyTo(Stream.Null);
                }
            }
        }
    }

    internal static void ValidateSourceEntries(string package, string path)
    {
        using (var zip = ZipFile.OpenRead(path))
        {
            var pending = new Stack<DirectoryInfo>();
            pending.Push(new DirectoryInfo(package));
            while (pending.Count > 0)
            {
                foreach (var item in pending.Pop().EnumerateFileSystemInfos())
                {
                    string relative = "DungeonGirls_Mac/" + item.FullName.Substring(package.Length + 1).Replace('\\', '/');
                    bool link = (item.Attributes & FileAttributes.ReparsePoint) != 0;
                    bool directory = (item.Attributes & FileAttributes.Directory) != 0;
                    var entry = zip.GetEntry(relative) ?? zip.GetEntry(relative + "/");
                    if (entry == null) throw new IOException("File omitted from ZIP: " + relative);
                    bool archivedLink = ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000;
                    if (link != archivedLink) throw new IOException("bsdtar did not preserve symlink type: " + relative);
                    if (!directory && !link && entry.Length != ((FileInfo)item).Length)
                        throw new IOException("ZIP file size mismatch: " + relative);
                    if (directory && !link) pending.Push((DirectoryInfo)item);
                }
            }
        }
    }
}
