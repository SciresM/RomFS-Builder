using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace IStorage_Builder
{
    internal class MetaDataBuilder
    {
        private const int PADDING_ALIGN = 16;
        private const uint IStorage_UNUSED_ENTRY = 0xFFFFFFFF;
        public string ROOT_DIR;

        public void BuildIStorageHeader(MemoryStream istorage_stream, IStorageFile[] Entries, string DIR)
        {
            ROOT_DIR = DIR;

            var MetaData = new IStorageMetaData();

            InitializeMetaData(MetaData);

            CalcIStorageSize(MetaData, Entries[Entries.Length - 1]);

            PopulateIStorage(MetaData, Entries);

            WriteMetaDataToStream(MetaData, istorage_stream);
        }

        private void InitializeMetaData(IStorageMetaData MetaData)
        {
            MetaData.InfoHeader = new IStorageInfoHeader();
            MetaData.DirTable = new IStorageDirTable();
            MetaData.DirTableLen = 0;
            MetaData.M_DirTableLen = 0;
            MetaData.FileTable = new IStorage_FileTable();
            MetaData.FileTableLen = 0;
            MetaData.DirTable.DirectoryTable = new List<IStorage_DirEntry>();
            MetaData.FileTable.FileTable = new List<IStorage_FileEntry>();
            MetaData.InfoHeader.HeaderLength = 0x50;
            MetaData.InfoHeader.Sections = new IStorage_SectionHeader[4];
            MetaData.DirHashTable = new List<uint>();
            MetaData.FileHashTable = new List<uint>();
        }

        private void CalcIStorageSize(IStorageMetaData MetaData, IStorageFile lastFile)
        {
            MetaData.DirNum = 1;
            var Root_DI = new DirectoryInfo(ROOT_DIR);
            CalcDirSize(MetaData, Root_DI);


            MetaData.M_DirHashTableEntry = GetHashTableEntryCount(MetaData.DirNum);

            MetaData.M_FileHashTableEntry = GetHashTableEntryCount(MetaData.FileNum);

            MetaData.InfoHeader.DataOffset = 0x200;

            for (var i = 0; i < MetaData.M_DirHashTableEntry; i++)
                MetaData.DirHashTable.Add(IStorage_UNUSED_ENTRY);
            for (var i = 0; i < MetaData.M_FileHashTableEntry; i++)
                MetaData.FileHashTable.Add(IStorage_UNUSED_ENTRY);
            var Pos = Align(MetaData.InfoHeader.DataOffset + lastFile.Offset + lastFile.Size, PADDING_ALIGN);
            for (var i = 0; i < 4; i++)
            {
                MetaData.InfoHeader.Sections[i].Offset = Pos;
                uint size = 0;
                switch (i)
                {
                    case 0:
                        size = MetaData.M_DirHashTableEntry * 4;
                        break;
                    case 1:
                        size = MetaData.M_DirTableLen;
                        break;
                    case 2:
                        size = MetaData.M_FileHashTableEntry * 4;
                        break;
                    case 3:
                        size = MetaData.M_FileTableLen;
                        break;
                }
                MetaData.InfoHeader.Sections[i].Size = size;
                Pos += size;
            }
        }

        private uint GetHashTableEntryCount(uint Entries)
        {
            var count = Entries;
            if (Entries < 3)
                count = 3;
            else if (count < 19)
                count |= 1;
            else
                while (count % 2 == 0 || count % 3 == 0 || count % 5 == 0 || count % 7 == 0 || count % 11 == 0 ||
                       count % 13 == 0 || count % 17 == 0)
                    count++;
            return count;
        }

        private void CalcDirSize(IStorageMetaData MetaData, DirectoryInfo dir)
        {
            if (MetaData.M_DirTableLen == 0)
                MetaData.M_DirTableLen = 0x18;
            else
                MetaData.M_DirTableLen += 0x18 + (uint) Align((uint) Encoding.UTF8.GetBytes(dir.Name).Length, 4);

            var files = dir.GetFiles();
            for (var i = 0; i < files.Length; i++)
                MetaData.M_FileTableLen += 0x20 + (uint) Align((uint) Encoding.UTF8.GetBytes(files[i].Name).Length, 4);

            var SubDirectories = dir.GetDirectories();
            for (var i = 0; i < SubDirectories.Length; i++)
                CalcDirSize(MetaData, SubDirectories[i]);

            MetaData.FileNum += (uint) files.Length;
            MetaData.DirNum += (uint) SubDirectories.Length;
        }

        private void PopulateIStorage(IStorageMetaData MetaData, IStorageFile[] Entries)
        {
            //Recursively Add All Directories to DirectoryTable
            AddDir(MetaData, new DirectoryInfo(ROOT_DIR), 0, IStorage_UNUSED_ENTRY);

            //Iteratively Add All Files to FileTable
            AddFiles(MetaData, Entries);

            //Set HashKeyPointers, Build HashTables
            PopulateHashTables(MetaData);

            //Thats it.
        }

        private void PopulateHashTables(IStorageMetaData MetaData)
        {
            for (var i = 0; i < MetaData.DirTable.DirectoryTable.Count; i++)
                AddDirHashKey(MetaData, i);
            for (var i = 0; i < MetaData.FileTable.FileTable.Count; i++)
                AddFileHashKey(MetaData, i);
        }

        private void AddDirHashKey(IStorageMetaData MetaData, int index)
        {
            var parent = MetaData.DirTable.DirectoryTable[index].ParentOffset;
            var Name = MetaData.DirTable.DirectoryTable[index].Name;
            // Encoding is "probably" UTF8.
            var NArr = index == 0 ? Encoding.UTF8.GetBytes("") : Encoding.UTF8.GetBytes(Name);
            var hash = CalcPathHash(parent, NArr, 0, NArr.Length);
            var ind2 = (int) (hash % MetaData.M_DirHashTableEntry);
            if (MetaData.DirHashTable[ind2] == IStorage_UNUSED_ENTRY)
            {
                MetaData.DirHashTable[ind2] = MetaData.DirTable.DirectoryTable[index].Offset;
            }
            else
            {
                var i = GetIStorageDirEntry(MetaData, MetaData.DirHashTable[ind2]);
                var tempindex = index;
                MetaData.DirHashTable[ind2] = MetaData.DirTable.DirectoryTable[index].Offset;
                while (true)
                    if (MetaData.DirTable.DirectoryTable[tempindex].HashKeyPointer == IStorage_UNUSED_ENTRY)
                    {
                        MetaData.DirTable.DirectoryTable[tempindex].HashKeyPointer =
                            MetaData.DirTable.DirectoryTable[i].Offset;
                        break;
                    }
                    else
                    {
                        i = tempindex;
                        tempindex = GetIStorageDirEntry(MetaData, MetaData.DirTable.DirectoryTable[i].HashKeyPointer);
                    }
            }
        }

        private void AddFileHashKey(IStorageMetaData MetaData, int index)
        {
            var parent = MetaData.FileTable.FileTable[index].ParentDirOffset;
            var Name = MetaData.FileTable.FileTable[index].Name;
            var NArr = Encoding.UTF8.GetBytes(Name);
            var hash = CalcPathHash(parent, NArr, 0, NArr.Length);
            var ind2 = (int) (hash % MetaData.M_FileHashTableEntry);
            if (MetaData.FileHashTable[ind2] == IStorage_UNUSED_ENTRY)
            {
                MetaData.FileHashTable[ind2] = MetaData.FileTable.FileTable[index].Offset;
            }
            else
            {
                var i = GetIStorageFileEntry(MetaData, MetaData.FileHashTable[ind2]);
                var tempindex = index;
                MetaData.FileHashTable[ind2] = MetaData.FileTable.FileTable[index].Offset;
                while (true)
                    if (MetaData.FileTable.FileTable[tempindex].HashKeyPointer == IStorage_UNUSED_ENTRY)
                    {
                        MetaData.FileTable.FileTable[tempindex].HashKeyPointer = MetaData.FileTable.FileTable[i].Offset;
                        break;
                    }
                    else
                    {
                        i = tempindex;
                        tempindex = GetIStorageFileEntry(MetaData, MetaData.FileTable.FileTable[i].HashKeyPointer);
                    }
            }
        }

        private uint CalcPathHash(uint ParentOffset, byte[] NameArray, int start, int len)
        {
            var hash = ParentOffset ^ 123456789;
            for (var i = 0; i < len; i++)
            {
                hash = (hash >> 5) | (hash << 27);
                hash ^= NameArray[start + i];
            }
            return hash;
        }

        private void AddDir(IStorageMetaData MetaData, DirectoryInfo Dir, uint parent, uint sibling)
        {
            var SubDirectories = Dir.GetDirectories();
            var CurrentDir = MetaData.DirTableLen;
            var Entry = new IStorage_DirEntry();
            Entry.ParentOffset = parent;
            Entry.ChildOffset = Entry.HashKeyPointer = Entry.FileOffset = IStorage_UNUSED_ENTRY;
            Entry.SiblingOffset = sibling;
            Entry.FullName = Dir.FullName;
            Entry.Name = Entry.FullName == ROOT_DIR ? "" : Dir.Name;
            Entry.Offset = CurrentDir;
            MetaData.DirTable.DirectoryTable.Add(Entry);
            MetaData.DirTableLen += CurrentDir == 0
                ? 0x18
                : 0x18 + (uint) Align((ulong) Encoding.UTF8.GetBytes(Entry.Name).Length, 4);

            for (var i = 0; i < SubDirectories.Length; i++)
                AddDir(MetaData, SubDirectories[i], CurrentDir, sibling);
            for (var i = 1; i < SubDirectories.Length; i++)
            {
                var PrevFullName = SubDirectories[i - 1].FullName;
                var ThisName = SubDirectories[i].FullName;
                var PrevIndex = GetIStorageDirEntry(MetaData, PrevFullName);
                var ThisIndex = GetIStorageDirEntry(MetaData, ThisName);
                MetaData.DirTable.DirectoryTable[PrevIndex].SiblingOffset =
                    MetaData.DirTable.DirectoryTable[ThisIndex].Offset;
            }
            if (SubDirectories.Length > 0)
            {
                var curindex = GetIStorageDirEntry(MetaData, Dir.FullName);
                var childindex = GetIStorageDirEntry(MetaData, SubDirectories[0].FullName);
                if (curindex > -1 && childindex > -1)
                    MetaData.DirTable.DirectoryTable[curindex].ChildOffset =
                        MetaData.DirTable.DirectoryTable[childindex].Offset;
            }
        }

        private void AddFiles(IStorageMetaData MetaData, IStorageFile[] Entries)
        {
            var PreviousSiblings = new Dictionary<string, uint>();
            for (var i = 0; i < Entries.Length; i++)
            {
                var file = new FileInfo(Entries[i].FullName);
                var Entry = new IStorage_FileEntry();
                var DirPath = Path.GetDirectoryName(Entries[i].FullName);
                var ParentIndex = GetIStorageDirEntry(MetaData, DirPath);
                Entry.FullName = Entries[i].FullName;
                Entry.Offset = MetaData.FileTableLen;
                Entry.ParentDirOffset = MetaData.DirTable.DirectoryTable[ParentIndex].Offset;
                Entry.SiblingOffset = IStorage_UNUSED_ENTRY;
                if (PreviousSiblings.ContainsKey(DirPath))
                {
                    MetaData.FileTable.FileTable[GetIStorageFileEntry(MetaData, PreviousSiblings[DirPath])]
                        .SiblingOffset = Entry.Offset;
                    PreviousSiblings[DirPath] = Entry.Offset;
                }
                else
                {
                    PreviousSiblings[DirPath] = Entry.Offset;
                }
                if (MetaData.DirTable.DirectoryTable[ParentIndex].FileOffset == IStorage_UNUSED_ENTRY)
                    MetaData.DirTable.DirectoryTable[ParentIndex].FileOffset = Entry.Offset;
                Entry.HashKeyPointer = IStorage_UNUSED_ENTRY;
                Entry.NameSize = (uint) Encoding.UTF8.GetBytes(file.Name).Length;
                Entry.Name = file.Name;
                Entry.DataOffset = Entries[i].Offset;
                Entry.DataSize = Entries[i].Size;
                MetaData.FileTable.FileTable.Add(Entry);
                MetaData.FileTableLen += 0x20 + (uint) Align(Entry.NameSize, 4);
            }
        }

        private void WriteMetaDataToStream(IStorageMetaData MetaData, MemoryStream stream)
        {
            //First, InfoHeader.
            stream.Write(BitConverter.GetBytes(MetaData.InfoHeader.HeaderLength), 0, 8);
            foreach (var SH in MetaData.InfoHeader.Sections)
            {
                stream.Write(BitConverter.GetBytes(SH.Offset), 0, 8);
                stream.Write(BitConverter.GetBytes(SH.Size), 0, 8);
            }
            stream.Write(BitConverter.GetBytes(MetaData.InfoHeader.DataOffset), 0, 8);

            //DirHashTable
            stream.Seek((long) MetaData.InfoHeader.Sections[0].Offset, SeekOrigin.Begin);
            foreach (var u in MetaData.DirHashTable)
                stream.Write(BitConverter.GetBytes(u), 0, 4);

            //DirTable
            stream.Seek((long) MetaData.InfoHeader.Sections[1].Offset, SeekOrigin.Begin);
            foreach (var dir in MetaData.DirTable.DirectoryTable)
            {
                stream.Write(BitConverter.GetBytes(dir.ParentOffset), 0, 4);
                stream.Write(BitConverter.GetBytes(dir.SiblingOffset), 0, 4);
                stream.Write(BitConverter.GetBytes(dir.ChildOffset), 0, 4);
                stream.Write(BitConverter.GetBytes(dir.FileOffset), 0, 4);
                stream.Write(BitConverter.GetBytes(dir.HashKeyPointer), 0, 4);
                var name = Encoding.UTF8.GetBytes(dir.Name);
                stream.Write(BitConverter.GetBytes(name.Length), 0, 4);
                var NameArray = new byte[(int) Align((uint) name.Length, 4)];
                name.CopyTo(NameArray, 0);
                stream.Write(NameArray, 0, NameArray.Length);
            }

            //FileHashTable
            stream.Seek((long) MetaData.InfoHeader.Sections[2].Offset, SeekOrigin.Begin);
            foreach (var u in MetaData.FileHashTable)
                stream.Write(BitConverter.GetBytes(u), 0, 4);

            //FileTable
            stream.Seek((long) MetaData.InfoHeader.Sections[3].Offset, SeekOrigin.Begin);
            foreach (var file in MetaData.FileTable.FileTable)
            {
                stream.Write(BitConverter.GetBytes(file.ParentDirOffset), 0, 4);
                stream.Write(BitConverter.GetBytes(file.SiblingOffset), 0, 4);
                stream.Write(BitConverter.GetBytes(file.DataOffset), 0, 8);
                stream.Write(BitConverter.GetBytes(file.DataSize), 0, 8);
                stream.Write(BitConverter.GetBytes(file.HashKeyPointer), 0, 4);
                var name = Encoding.UTF8.GetBytes(file.Name);
                stream.Write(BitConverter.GetBytes(name.Length), 0, 4);
                var NameArray = new byte[(int) Align((uint) name.Length, 4)];
                name.CopyTo(NameArray, 0);
                stream.Write(NameArray, 0, NameArray.Length);
            }

            //All Done.
            stream.Seek((long) MetaData.InfoHeader.DataOffset, SeekOrigin.Begin);
        }

        //GetIStorage[...]Entry Functions are all O(n)

        private int GetIStorageDirEntry(IStorageMetaData MetaData, string FullName)
        {
            for (var i = 0; i < MetaData.DirTable.DirectoryTable.Count; i++)
                if (MetaData.DirTable.DirectoryTable[i].FullName == FullName)
                    return i;
            return -1;
        }

        private int GetIStorageDirEntry(IStorageMetaData MetaData, uint Offset)
        {
            for (var i = 0; i < MetaData.DirTable.DirectoryTable.Count; i++)
                if (MetaData.DirTable.DirectoryTable[i].Offset == Offset)
                    return i;
            return -1;
        }

        private int GetIStorageFileEntry(IStorageMetaData MetaData, uint Offset)
        {
            for (var i = 0; i < MetaData.FileTable.FileTable.Count; i++)
                if (MetaData.FileTable.FileTable[i].Offset == Offset)
                    return i;
            return -1;
        }

        private ulong Align(ulong input, ulong alignsize)
        {
            var output = input;
            if (output % alignsize != 0)
                output += alignsize - output % alignsize;
            return output;
        }

        public class IStorageMetaData
        {
            public List<uint> DirHashTable;
            public uint DirNum;
            public IStorageDirTable DirTable;
            public uint DirTableLen;
            public List<uint> FileHashTable;
            public uint FileNum;
            public IStorage_FileTable FileTable;
            public uint FileTableLen;
            public IStorageInfoHeader InfoHeader;
            public uint M_DirHashTableEntry;
            public uint M_DirTableLen;
            public uint M_FileHashTableEntry;
            public uint M_FileTableLen;
        }

        public struct IStorage_SectionHeader
        {
            public ulong Offset;
            public ulong Size;
        }

        public struct IStorageInfoHeader
        {
            public ulong HeaderLength;
            public IStorage_SectionHeader[] Sections;
            public ulong DataOffset;
        }

        public class IStorageDirTable
        {
            public List<IStorage_DirEntry> DirectoryTable;
        }

        public class IStorage_FileTable
        {
            public List<IStorage_FileEntry> FileTable;
        }

        public class IStorage_DirEntry
        {
            public uint ChildOffset;
            public uint FileOffset;
            public string FullName;
            public uint HashKeyPointer;
            public string Name;
            public uint Offset;
            public uint ParentOffset;
            public uint SiblingOffset;
        }

        public class IStorage_FileEntry
        {
            public ulong DataOffset;
            public ulong DataSize;
            public string FullName;
            public uint HashKeyPointer;
            public string Name;
            public uint NameSize;
            public uint Offset;
            public uint ParentDirOffset;
            public uint SiblingOffset;
        }
    }
}