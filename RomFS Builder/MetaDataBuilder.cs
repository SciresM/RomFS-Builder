using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;

namespace RomFS_Builder
{
    class MetaDataBuilder
    {
        public string ROOT_DIR;
        private const int PADDING_ALIGN = 16;
        private const uint ROMFS_UNUSED_ENTRY = 0xFFFFFFFF;

        public void BuildRomFSHeader(MemoryStream romfs_stream, RomfsFile[] Entries, string DIR)
        {
            ROOT_DIR = DIR;

            Romfs_MetaData MetaData = new Romfs_MetaData();

            InitializeMetaData(MetaData);

            CalcRomfsSize(MetaData);

            PopulateRomfs(MetaData, Entries);

            WriteMetaDataToStream(MetaData, romfs_stream);
        }

        private void InitializeMetaData(Romfs_MetaData MetaData)
        {
            MetaData.InfoHeader = new Romfs_InfoHeader();
            MetaData.DirTable = new Romfs_DirTable();
            MetaData.DirTableLen = 0;
            MetaData.M_DirTableLen = 0;
            MetaData.FileTable = new Romfs_FileTable();
            MetaData.FileTableLen = 0;
            MetaData.DirTable.DirectoryTable = new List<Romfs_DirEntry>();
            MetaData.FileTable.FileTable = new List<Romfs_FileEntry>();
            MetaData.InfoHeader.HeaderLength = 0x28;
            MetaData.InfoHeader.Sections = new Romfs_SectionHeader[4];
            MetaData.DirUTable = new List<uint>();
            MetaData.FileUTable = new List<uint>();
        }

        private void CalcRomfsSize(Romfs_MetaData MetaData)
        {
            MetaData.DirNum = 1;
            DirectoryInfo Root_DI = new DirectoryInfo(ROOT_DIR);
            CalcDirSize(MetaData, Root_DI);

            MetaData.M_DirUTableEntry = 3;
            if (MetaData.DirNum > 3)
            {
                MetaData.M_DirUTableEntry += (uint)Align((ulong)MetaData.DirNum - 3, 2);
            }

            MetaData.M_FileUTableEntry = 3;
            if (MetaData.FileNum > 3)
            {
                MetaData.M_FileUTableEntry += (uint)Align((ulong)MetaData.FileNum - 3, 2);
            }


            uint MetaDataSize = (uint)Align((ulong)(0x28 + MetaData.M_DirUTableEntry * 4 + MetaData.M_DirTableLen + MetaData.M_FileUTableEntry * 4 + MetaData.M_FileTableLen), PADDING_ALIGN);
            for (int i = 0; i < MetaData.M_DirUTableEntry; i++)
            {
                MetaData.DirUTable.Add(ROMFS_UNUSED_ENTRY);
            }
            for (int i = 0; i < MetaData.M_FileUTableEntry; i++)
            {
                MetaData.FileUTable.Add(ROMFS_UNUSED_ENTRY);
            }
            uint Pos = MetaData.InfoHeader.HeaderLength;
            for (int i = 0; i < 4; i++)
            {
                MetaData.InfoHeader.Sections[i].Offset = Pos;
                uint size = 0;
                switch (i)
                {
                    case 0:
                        size = MetaData.M_DirUTableEntry * 4;
                        break;
                    case 1:
                        size = MetaData.M_DirTableLen;
                        break;
                    case 2:
                        size = MetaData.M_FileUTableEntry * 4;
                        break;
                    case 3:
                        size = MetaData.M_FileTableLen;
                        break;
                }
                MetaData.InfoHeader.Sections[i].Size = size;
                Pos += size;
            }
            MetaData.InfoHeader.DataOffset = MetaDataSize;
        }

        private void CalcDirSize(Romfs_MetaData MetaData, DirectoryInfo dir)
        {
            if (MetaData.M_DirTableLen == 0)
            {
                MetaData.M_DirTableLen = 0x18;
            }
            else
            {
                MetaData.M_DirTableLen += 0x18 + (uint)Align((ulong)dir.Name.Length * 2, 4);
            }

            FileInfo[] files = dir.GetFiles();
            for (int i = 0; i < files.Length; i++)
            {
                MetaData.M_FileTableLen += 0x20 + (uint)Align((ulong)files[i].Name.Length * 2, 4);
            }

            DirectoryInfo[] SubDirectories = dir.GetDirectories();
            for (int i = 0; i < SubDirectories.Length; i++)
            {
                CalcDirSize(MetaData, SubDirectories[i]);
            }

            MetaData.FileNum += (uint)files.Length;
            MetaData.DirNum += (uint)SubDirectories.Length;
        }

        private void PopulateRomfs(Romfs_MetaData MetaData, RomfsFile[] Entries)
        {
            //Recursively Add All Directories to DirectoryTable
            AddDir(MetaData, new DirectoryInfo(ROOT_DIR), 0, ROMFS_UNUSED_ENTRY);

            //Iteratively Add All Files to FileTable
            AddFiles(MetaData, Entries);

            //Set Weird Offsets, Buld DirUTable+FileUTable
            CalculateWeirdOffsets(MetaData);

            //Thats it.
        }

        private void CalculateWeirdOffsets(Romfs_MetaData MetaData)
        {
            for (int i = 0; i < MetaData.DirTable.DirectoryTable.Count; i++)
            {
                AddDirHashKey(MetaData, i);
            }
            for (int i = 0; i < MetaData.FileTable.FileTable.Count; i++)
            {
                AddFileHashKey(MetaData, i);
            }
        }

        private void AddDirHashKey(Romfs_MetaData MetaData, int index)
        {
            uint parent = MetaData.DirTable.DirectoryTable[index].ParentOffset;
            string Name = MetaData.DirTable.DirectoryTable[index].Name;
            byte[] NArr = (index == 0) ? Encoding.Unicode.GetBytes("") : Encoding.Unicode.GetBytes(Name);
            uint hash = CalcPathHash(parent, NArr, 0, NArr.Length);
            int ind2 = (int)(hash % MetaData.M_DirUTableEntry);
            if (MetaData.DirUTable[ind2] == ROMFS_UNUSED_ENTRY)
            {
                MetaData.DirUTable[ind2] = MetaData.DirTable.DirectoryTable[index].Offset;
            }
            else
            {
                int i = GetRomfsDirEntry(MetaData, MetaData.DirUTable[ind2]);
                while (true)
                {
                    if (MetaData.DirTable.DirectoryTable[i].WeirdOffset == ROMFS_UNUSED_ENTRY)
                    {
                        MetaData.DirTable.DirectoryTable[i].WeirdOffset = MetaData.DirTable.DirectoryTable[index].Offset;
                        break;
                    }
                    else
                    {
                        i = GetRomfsDirEntry(MetaData, MetaData.DirTable.DirectoryTable[i].WeirdOffset);
                    }
                }
            }
        }

        private void AddFileHashKey(Romfs_MetaData MetaData, int index)
        {
            uint parent = MetaData.FileTable.FileTable[index].ParentDirOffset;
            string Name = MetaData.FileTable.FileTable[index].Name;
            byte[] NArr = Encoding.Unicode.GetBytes(Name);
            uint hash = CalcPathHash(parent, NArr, 0, NArr.Length);
            int ind2 = (int)(hash % MetaData.M_FileUTableEntry);
            if (MetaData.FileUTable[ind2] == ROMFS_UNUSED_ENTRY)
            {
                MetaData.FileUTable[ind2] = MetaData.FileTable.FileTable[index].Offset;
            }
            else
            { 
                int i = GetRomfsFileEntry(MetaData, MetaData.FileUTable[ind2]);
                while (true)
                {
                    if (MetaData.FileTable.FileTable[i].WeirdOffset == ROMFS_UNUSED_ENTRY)
                    {
                        MetaData.FileTable.FileTable[i].WeirdOffset = MetaData.FileTable.FileTable[index].Offset;
                        break;
                    }
                    else
                    {
                        i = GetRomfsFileEntry(MetaData, MetaData.FileTable.FileTable[i].WeirdOffset);
                    }
                }
            }
        }

        private uint CalcPathHash(uint ParentOffset, byte[] NameArray, int start, int len)
        {
            uint hash = ParentOffset ^ 123456789;
            for (int i = 0; i < NameArray.Length; i+=2)
            {
                hash = (uint)((hash >> 5) | (hash << 27));
                hash ^= (ushort)((NameArray[start + i]) | (NameArray[start + i + 1] << 8));
            }
            return hash;
        }

        private void AddDir(Romfs_MetaData MetaData, DirectoryInfo Dir, uint parent, uint sibling)
        {
            uint CurrentDir = MetaData.DirTableLen;
            Romfs_DirEntry Entry = new Romfs_DirEntry();
            Entry.ParentOffset = parent;
            Entry.ChildOffset = Entry.WeirdOffset = Entry.FileOffset = ROMFS_UNUSED_ENTRY;
            Entry.SiblingOffset = sibling;
            Entry.FullName = Dir.FullName;
            Entry.Name = (Entry.FullName == ROOT_DIR) ? "" : Dir.Name;
            Entry.Offset = CurrentDir;
            MetaData.DirTable.DirectoryTable.Add(Entry);
            MetaData.DirTableLen += (CurrentDir == 0) ? 0x18 : 0x18 + (uint)Align((ulong)Dir.Name.Length * 2, 4);
            DirectoryInfo[] SubDirectories = Dir.GetDirectories();
            int ParentIndex = GetRomfsDirEntry(MetaData, Dir.FullName);
            uint poff = MetaData.DirTable.DirectoryTable[ParentIndex].Offset;
            if (SubDirectories.Length > 0)
            {
                MetaData.DirTable.DirectoryTable[ParentIndex].ChildOffset = MetaData.DirTableLen;
            }
            for (int i = 0; i < SubDirectories.Length; i++)
            {
                AddDir(MetaData, SubDirectories[i], Entry.Offset, sibling);
                if (i > 0)
                {
                    string PrevFullName = SubDirectories[i - 1].FullName;
                    string ThisName = SubDirectories[i].FullName;
                    int PrevIndex = GetRomfsDirEntry(MetaData, PrevFullName);
                    int ThisIndex = GetRomfsDirEntry(MetaData, ThisName);
                    MetaData.DirTable.DirectoryTable[PrevIndex].SiblingOffset = MetaData.DirTable.DirectoryTable[ThisIndex].Offset;
                }
            }
        }

        private void AddFiles(Romfs_MetaData MetaData, RomfsFile[] Entries)
        {
            string PrevDirPath = "";
            for (int i = 0; i < Entries.Length; i++)
            {
                FileInfo file = new FileInfo(Entries[i].FullName);
                Romfs_FileEntry Entry = new Romfs_FileEntry();
                string DirPath = Path.GetDirectoryName(Entries[i].FullName);
                int ParentIndex = GetRomfsDirEntry(MetaData, DirPath);
                Entry.FullName = Entries[i].FullName;
                Entry.Offset = MetaData.FileTableLen;
                Entry.ParentDirOffset = MetaData.DirTable.DirectoryTable[ParentIndex].Offset;
                Entry.SiblingOffset = ROMFS_UNUSED_ENTRY;
                if (DirPath == PrevDirPath)
                {
                    MetaData.FileTable.FileTable[i - 1].SiblingOffset = Entry.Offset;
                }
                if (MetaData.DirTable.DirectoryTable[ParentIndex].FileOffset == ROMFS_UNUSED_ENTRY)
                {
                    MetaData.DirTable.DirectoryTable[ParentIndex].FileOffset = Entry.Offset;
                }
                Entry.WeirdOffset = ROMFS_UNUSED_ENTRY;
                Entry.NameSize = (uint)file.Name.Length * 2;
                Entry.Name = file.Name;
                Entry.DataOffset = Entries[i].Offset;
                Entry.DataSize = Entries[i].Size;
                MetaData.FileTable.FileTable.Add(Entry);
                MetaData.FileTableLen += 0x20 + (uint)Align((ulong)file.Name.Length * 2, 4);
                PrevDirPath = DirPath;
            }
        }

        private void WriteMetaDataToStream(Romfs_MetaData MetaData, MemoryStream stream)
        {
            //First, InfoHeader.
            stream.Write(BitConverter.GetBytes(MetaData.InfoHeader.HeaderLength), 0, 4);
            foreach (Romfs_SectionHeader SH in MetaData.InfoHeader.Sections)
            {
                stream.Write(BitConverter.GetBytes(SH.Offset), 0, 4);
                stream.Write(BitConverter.GetBytes(SH.Size), 0, 4);
            }
            stream.Write(BitConverter.GetBytes(MetaData.InfoHeader.DataOffset), 0, 4);
            
            //DirUTable
            foreach (uint u in MetaData.DirUTable)
            {
                stream.Write(BitConverter.GetBytes(u), 0, 4);
            }
            
            //DirTable
            foreach(Romfs_DirEntry dir in MetaData.DirTable.DirectoryTable)
            {
                stream.Write(BitConverter.GetBytes(dir.ParentOffset), 0, 4);
                stream.Write(BitConverter.GetBytes(dir.SiblingOffset), 0, 4);
                stream.Write(BitConverter.GetBytes(dir.ChildOffset), 0, 4);
                stream.Write(BitConverter.GetBytes(dir.FileOffset), 0, 4);
                stream.Write(BitConverter.GetBytes(dir.WeirdOffset), 0, 4);
                uint nlen = (uint)dir.Name.Length * 2;
                stream.Write(BitConverter.GetBytes(nlen), 0, 4);
                byte[] NameArray = new byte[(int)Align((ulong)nlen, 4)];
                Array.Copy(Encoding.Unicode.GetBytes(dir.Name), 0, NameArray, 0, nlen);
                stream.Write(NameArray, 0, NameArray.Length);
            }

            //FileUTable
            foreach (uint u in MetaData.FileUTable)
            {
                stream.Write(BitConverter.GetBytes(u), 0, 4);
            }

            //FileTable
            foreach (Romfs_FileEntry file in MetaData.FileTable.FileTable)
            {
                stream.Write(BitConverter.GetBytes(file.ParentDirOffset), 0, 4);
                stream.Write(BitConverter.GetBytes(file.SiblingOffset), 0, 4);
                stream.Write(BitConverter.GetBytes(file.DataOffset), 0, 8);
                stream.Write(BitConverter.GetBytes(file.DataSize), 0, 8);
                stream.Write(BitConverter.GetBytes(file.WeirdOffset), 0, 4);
                uint nlen = (uint)file.Name.Length * 2;
                stream.Write(BitConverter.GetBytes(nlen), 0, 4);
                byte[] NameArray = new byte[(int)Align((ulong)nlen, 4)];
                Array.Copy(Encoding.Unicode.GetBytes(file.Name), 0, NameArray, 0, nlen);
                stream.Write(NameArray, 0, NameArray.Length);
            }
            //All Done.
        }

        //GetRomfs[...]Entry Functions are all O(n)

        private int GetRomfsDirEntry(Romfs_MetaData MetaData, string FullName)
        {
            for (int i = 0; i < MetaData.DirTable.DirectoryTable.Count; i++)
            {
                if (MetaData.DirTable.DirectoryTable[i].FullName == FullName)
                {
                    return i;
                }
            }
            return -1;
        }

        private int GetRomfsDirEntry(Romfs_MetaData MetaData, uint Offset)
        {
            for (int i = 0; i < MetaData.DirTable.DirectoryTable.Count; i++)
            {
                if (MetaData.DirTable.DirectoryTable[i].Offset == Offset)
                {
                    return i;
                }
            }
            return -1;
        }

        private int GetRomfsFileEntry(Romfs_MetaData MetaData, uint Offset)
        {
            for (int i = 0; i < MetaData.FileTable.FileTable.Count; i++)
            {
                if (MetaData.FileTable.FileTable[i].Offset == Offset)
                {
                    return i;
                }
            }
            return -1;
        }

        private ulong Align(ulong input, ulong alignsize)
        {
            ulong output = input;
            if (output % alignsize != 0)
            {
                output += (alignsize - (output % alignsize));
            }
            return output;
        }

        public class Romfs_MetaData
        {
            public Romfs_InfoHeader InfoHeader;
            public uint DirNum;
            public uint FileNum;
            public List<uint> DirUTable;
            public uint M_DirUTableEntry;
            public Romfs_DirTable DirTable;
            public uint DirTableLen;
            public uint M_DirTableLen;
            public List<uint> FileUTable;
            public uint M_FileUTableEntry;
            public Romfs_FileTable FileTable;
            public uint FileTableLen;
            public uint M_FileTableLen;
        }

        public struct Romfs_SectionHeader
        {
            public uint Offset;
            public uint Size;
        }

        public struct Romfs_InfoHeader
        {
            public uint HeaderLength;
            public Romfs_SectionHeader[] Sections;
            public uint DataOffset;
        }

        public class Romfs_DirTable
        {
            public List<Romfs_DirEntry> DirectoryTable;
        }

        public class Romfs_FileTable
        {
            public List<Romfs_FileEntry> FileTable;
        }

        public class Romfs_DirEntry
        {
            public uint ParentOffset;
            public uint SiblingOffset;
            public uint ChildOffset;
            public uint FileOffset;
            public uint WeirdOffset;
            public string Name;
            public string FullName;
            public uint Offset;
        }

        public class Romfs_FileEntry
        {
            public uint ParentDirOffset;
            public uint SiblingOffset;
            public ulong DataOffset;
            public ulong DataSize;
            public uint WeirdOffset;
            public uint NameSize;
            public string Name;
            public string FullName;
            public uint Offset;
        }
    }
}
