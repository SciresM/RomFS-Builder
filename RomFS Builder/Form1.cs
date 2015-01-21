using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace RomFS_Builder
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
            B_Go.Enabled = false;
        }

        private bool isWorkerThreadAlive = false;

        private void B_Open_Click(object sender, EventArgs e)
        {
            FolderBrowserDialog fbd = new FolderBrowserDialog();
            if (fbd.ShowDialog() == DialogResult.OK)
            {
                TB_Path.Text = fbd.SelectedPath;
                B_Go.Enabled = true;
            }
        }

        private void B_Go_Click(object sender, EventArgs e)
        {      
            if (this.isWorkerThreadAlive)
            {
                MessageBox.Show("There are files currently being processed.");
            }
            else
            {
                TempFile = "";
                TB_Progress.Text = "";
                Thread thread = new Thread(new ThreadStart(this.BuildRomFS));
                if (TB_Path.Text.Length == 0)
                    return;
                B_Open.Enabled = false;
                B_Go.Enabled = false;
                thread.IsBackground = true;
                thread.Start();
            }
        }

        private const int PADDING_ALIGN = 16;
        private string ROOT_DIR;
        private string TempFile;
        
        private void BuildRomFS()
        {
            this.isWorkerThreadAlive = true;
            ROOT_DIR = TB_Path.Text;
            FileNameTable FNT = new FileNameTable(ROOT_DIR);
            RomfsFile[] RomFiles = new RomfsFile[FNT.NumFiles];
            LayoutManager.Input[] In = new LayoutManager.Input[FNT.NumFiles];
            TB_Progress.Invoke((Action)(() => UpdateTB_Progress("Creating Layout...")));
            for (int i = 0; i < FNT.NumFiles; i++)
            {
                In[i] = new LayoutManager.Input();
                In[i].FilePath = FNT.NameEntryTable[i].FullName;
                In[i].AlignmentSize = 0x10;
            }
            LayoutManager.Output[] Out = LayoutManager.Create(In);
            for (int i = 0; i < Out.Length; i++)
            {
                RomFiles[i] = new RomfsFile();
                RomFiles[i].Offset = Out[i].Offset;
                RomFiles[i].PathName = Out[i].FilePath.Replace(Path.GetFullPath(ROOT_DIR), "").Replace("\\", "/");
                RomFiles[i].FullName = Out[i].FilePath;
                RomFiles[i].Size = Out[i].Size;
            }        
            using (MemoryStream memoryStream = new MemoryStream())
            {
                TB_Progress.Invoke((Action)(() => UpdateTB_Progress("Creating RomFS MetaData...")));
                MetaDataBuilder mdb = new MetaDataBuilder();
                mdb.BuildRomFSHeader(memoryStream, RomFiles, ROOT_DIR);
                MakeRomFSData(RomFiles, memoryStream);
            }
            this.isWorkerThreadAlive = false;
            Invoke((Action)(() =>
            {
                B_Go.Enabled = true;
                B_Open.Enabled = true;
            }
            ));
        }

        public static ulong Align(ulong input, ulong alignsize)
        {
            ulong output = input;
            if (output % alignsize != 0)
            {
                output += (alignsize - (output % alignsize));
            }
           return output;
        }

        private void MakeRomFSData(RomfsFile[] RomFiles, MemoryStream metadata)
        {
            TempFile = Path.GetRandomFileName();
            TB_Progress.Invoke((Action)(() => UpdateTB_Progress("Computing IVFC Header Data...")));
            IVFCInfo ivfc = new IVFCInfo();
            ivfc.Levels = new IVFCLevel[3];
            for (int i=0;i<ivfc.Levels.Length;i++)
            {
                ivfc.Levels[i] = new IVFCLevel();
                ivfc.Levels[i].BlockSize = 0x1000;
            }
            ivfc.Levels[2].DataLength = RomfsFile.GetDataBlockLength(RomFiles, (ulong)metadata.Length);
            ivfc.Levels[1].DataLength = (Align(ivfc.Levels[2].DataLength , ivfc.Levels[2].BlockSize) / ivfc.Levels[2].BlockSize) * 0x20; //0x20 per SHA256 hash
            ivfc.Levels[0].DataLength = (Align(ivfc.Levels[1].DataLength, ivfc.Levels[1].BlockSize) / ivfc.Levels[1].BlockSize) * 0x20; //0x20 per SHA256 hash
            ulong MasterHashLen = (Align(ivfc.Levels[0].DataLength, ivfc.Levels[0].BlockSize) / ivfc.Levels[0].BlockSize) * 0x20;
            ulong lofs = 0;
            for (int i = 0; i < ivfc.Levels.Length; i++)
            {
                ivfc.Levels[i].HashOffset = lofs;
                lofs += Align(ivfc.Levels[i].DataLength,ivfc.Levels[i].BlockSize);
            }
            uint IVFC_MAGIC = 0x43465649; //IVFC
            uint RESERVED = 0x0;
            uint HeaderLen = 0x5C;
            uint MEDIA_UNIT_SIZE = 0x200;
            byte[] SuperBlockHash = new byte[0x20];
            FileStream OutFileStream = new FileStream(TempFile, FileMode.Create, FileAccess.ReadWrite);
            try
            {
                    OutFileStream.Seek(0,SeekOrigin.Begin);
                    OutFileStream.Write(BitConverter.GetBytes(IVFC_MAGIC), 0, 0x4);
                    OutFileStream.Write(BitConverter.GetBytes(0x10000), 0, 0x4);
                    OutFileStream.Write(BitConverter.GetBytes(MasterHashLen), 0, 0x4);
                    for (int i = 0; i < ivfc.Levels.Length; i++)
                    {
                        OutFileStream.Write(BitConverter.GetBytes(ivfc.Levels[i].HashOffset), 0, 0x8);
                        OutFileStream.Write(BitConverter.GetBytes(ivfc.Levels[i].DataLength), 0, 0x8);
                        OutFileStream.Write(BitConverter.GetBytes((int)(Math.Log(ivfc.Levels[i].BlockSize,2))), 0, 0x4);
                        OutFileStream.Write(BitConverter.GetBytes(RESERVED), 0, 0x4);
                    }
                    OutFileStream.Write(BitConverter.GetBytes(HeaderLen), 0, 0x4);
                    //IVFC Header is Written.
                    OutFileStream.Seek((long)Align(MasterHashLen+0x60, ivfc.Levels[0].BlockSize), SeekOrigin.Begin);
                    byte[] metadataArray = metadata.ToArray();
                    OutFileStream.Write(metadataArray, 0, metadataArray.Length);
                    long baseOfs = OutFileStream.Position;
                    TB_Progress.Invoke((Action)(() => UpdateTB_Progress("Writing Level 2 Data...")));
                    PB_Show.Invoke((Action)(() => {PB_Show.Minimum = 0;
                PB_Show.Maximum = RomFiles.Length;
                PB_Show.Value = 0;
                PB_Show.Step = 1;
                    }));
                    for (int i = 0; i < RomFiles.Length; i++)
                    {
                        OutFileStream.Seek((long)(baseOfs + (long)RomFiles[i].Offset), SeekOrigin.Begin);
                        using (FileStream inStream = new FileStream(RomFiles[i].FullName, FileMode.Open, FileAccess.Read))
                        {
                            while (inStream.Position < inStream.Length)
                            {
                                byte[] buffer = new byte[inStream.Length - inStream.Position > 0x100000 ? 0x100000 : inStream.Length - inStream.Position];
                                inStream.Read(buffer, 0, buffer.Length);
                                OutFileStream.Write(buffer, 0, buffer.Length);
                            }
                        }
                        PB_Show.Invoke((Action)(() => PB_Show.PerformStep()));
                    }
                    long hashBaseOfs = (long)Align((ulong)OutFileStream.Position,ivfc.Levels[2].BlockSize);
                    long hOfs = (long)Align(MasterHashLen, ivfc.Levels[0].BlockSize);
                    long cOfs = hashBaseOfs + (long)ivfc.Levels[1].HashOffset;
                    SHA256Managed sha = new SHA256Managed();
                    for (int i = ivfc.Levels.Length - 1; i >= 0; i--)
                    {
                        TB_Progress.Invoke((Action)(() => UpdateTB_Progress("Computing Level "+i+" Hashes...")));
                        byte[] buffer = new byte[(int)ivfc.Levels[i].BlockSize];
                        PB_Show.Invoke((Action)(() =>
                        {
                            PB_Show.Minimum = 0;
                            PB_Show.Maximum = (int)(ivfc.Levels[i].DataLength / ivfc.Levels[i].BlockSize);
                            PB_Show.Value = 0;
                            PB_Show.Step = 1;
                        }));
                        for (long ofs = 0;ofs<(long)ivfc.Levels[i].DataLength;ofs+=ivfc.Levels[i].BlockSize)
                        {
                            OutFileStream.Seek(hOfs, SeekOrigin.Begin);
                            OutFileStream.Read(buffer, 0, (int)ivfc.Levels[i].BlockSize);
                            hOfs = OutFileStream.Position;
                            byte[] hash = sha.ComputeHash(buffer);
                            OutFileStream.Seek(cOfs, SeekOrigin.Begin);
                            OutFileStream.Write(hash, 0, hash.Length);
                            cOfs = OutFileStream.Position;
                            PB_Show.Invoke((Action)(() => PB_Show.PerformStep()));
                        }
                        if (i == 2)
                        {
                            long len = OutFileStream.Position;
                            if (len % 0x1000 != 0)
                            {
                                len = (long)Align((ulong)len, 0x1000);
                                byte[] buf = new byte[len - OutFileStream.Position];
                                OutFileStream.Write(buf, 0, buf.Length);
                            }
                        }
                        if (i>0)
                        {
                            hOfs = hashBaseOfs + (long)ivfc.Levels[i-1].HashOffset;
                            if (i > 1)
                            {
                                cOfs = hashBaseOfs + (long)ivfc.Levels[i - 2].HashOffset;
                            }
                            else
                            {
                                cOfs = (long)Align(HeaderLen, PADDING_ALIGN);
                            }
                        }
                    }
                    OutFileStream.Seek(0, SeekOrigin.Begin);
                    uint SuperBlockLen = (uint)Align(MasterHashLen+0x60,MEDIA_UNIT_SIZE);
                    byte[] MasterHashes = new byte[SuperBlockLen];
                    OutFileStream.Read(MasterHashes, 0, (int)SuperBlockLen);
                    SuperBlockHash = sha.ComputeHash(MasterHashes);
            }
            finally
            {
                    if (OutFileStream != null)
                        OutFileStream.Dispose();
            }
            TB_Progress.Invoke((Action)(() => UpdateTB_Progress("RomFS Super Block Hash: " + ByteArrayToString(SuperBlockHash)))); 
            TB_Progress.Invoke((Action)(() => UpdateTB_Progress("Prompting to Save...")));
            SaveFileDialog sfd = new SaveFileDialog();
            Invoke((Action)(() =>
            {
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    
                            TB_Progress.Invoke((Action)(() => UpdateTB_Progress("Writing Binary to " + sfd.FileName + "...")));
                            Thread thread = new Thread(() => WriteBinary(TempFile, sfd.FileName));
                            thread.IsBackground = true;
                            thread.Start();
                        
                }
            }));
        }

        public void WriteBinary(string tempFile, string outFile)
        {
            using (FileStream fs = new FileStream(outFile, FileMode.Create))
            {
                using (BinaryWriter writer = new BinaryWriter(fs))
                {
                    using (FileStream fileStream = new FileStream(tempFile, FileMode.Open, FileAccess.Read))
                    {
                        uint BUFFER_SIZE = 0x100000;
                        PB_Show.Invoke((Action)(() =>
                        {
                            PB_Show.Minimum = 0;
                            PB_Show.Maximum = (int)(fileStream.Length / BUFFER_SIZE);
                            PB_Show.Value = 0;
                            PB_Show.Step = 1;
                        }));
                        byte[] buffer = new byte[BUFFER_SIZE];
                        while (true)
                        {
                            int count = fileStream.Read(buffer, 0, buffer.Length);
                            if (count != 0)
                            {
                                writer.Write(buffer, 0, count);
                                PB_Show.Invoke((Action)(() => PB_Show.PerformStep()));
                            }
                            else
                                break;
                        }
                    }
                    writer.Flush();
                }
            }
            File.Delete(TempFile);
            Invoke((Action)(() =>
            {
                MessageBox.Show("Wrote RomFS to " + outFile + ".");
            }));
        }

        public static string ByteArrayToString(byte[] input)
        {
            StringBuilder sb = new StringBuilder();
            foreach (byte b in input)
            {
                sb.Append(b.ToString("X2") + " ");
            }
            return sb.ToString();
        }

        public void UpdateTB_Progress(string text)
        {
            TB_Progress.Text += text + Environment.NewLine;
        }

    }

    public class RomfsFile
    {
            public string PathName;
            public ulong Offset;
            public ulong Size;
            public string FullName;

            public static ulong GetDataBlockLength(RomfsFile[] files, ulong PreData)
            {
                return (files.Length == 0) ? PreData : PreData + files[files.Length - 1].Offset + files[files.Length - 1].Size;
            }
    }

    public class IVFCInfo
    {
        public IVFCLevel[] Levels;
    }

    public class IVFCLevel
    {
        public ulong HashOffset;
        public ulong DataLength;
        public uint BlockSize;
    }

    public class FileNameTable
    {
        public List<FileInfo> NameEntryTable { get; private set; }

        public int NumFiles
        {
            get
            {
                return this.NameEntryTable.Count;
            }
        }

        internal FileNameTable(string rootPath)
        {
            this.NameEntryTable = new List<FileInfo>();
            this.AddDirectory(new DirectoryInfo(rootPath));
        }

        internal void AddDirectory(DirectoryInfo dir)
        {
            foreach (FileInfo fileInfo in dir.GetFiles())
            {
                this.NameEntryTable.Add(fileInfo);
            }
            foreach (DirectoryInfo subdir in dir.GetDirectories())
            {
                this.AddDirectory(subdir);
            }
        }
    }

    public class LayoutManager
    {
        public static LayoutManager.Output[] Create(LayoutManager.Input[] Input)
        {
            List<LayoutManager.Output> list = new List<LayoutManager.Output>();
            ulong Len = 0;
            foreach (LayoutManager.Input input in Input)
            {
                LayoutManager.Output output = new LayoutManager.Output();
                FileInfo fileInfo = new FileInfo(input.FilePath);
                ulong ofs = Form1.Align(Len, input.AlignmentSize);
                output.FilePath = input.FilePath;
                output.Offset = ofs;
                output.Size = (ulong)fileInfo.Length;
                list.Add(output);
                Len = ofs + (ulong)fileInfo.Length;
            }
            return list.ToArray();
        }

        public class Input
        {
            public string FilePath;
            public uint AlignmentSize;
        }

        public class Output
        {
            public string FilePath;
            public ulong Offset;
            public ulong Size;
        }
    }

    
}
