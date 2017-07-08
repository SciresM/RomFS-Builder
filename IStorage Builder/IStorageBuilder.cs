using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace IStorage_Builder
{
    public partial class IStorageBuilder : Form
    {
        private const int PADDING_ALIGN = 16;

        private bool isWorkerThreadAlive;
        private string ROOT_DIR;
        private string TempFile;

        public IStorageBuilder()
        {
            InitializeComponent();
            B_Go.Enabled = false;
        }

        private void B_Open_Click(object sender, EventArgs e)
        {
            var fbd = new FolderBrowserDialog();
            if (fbd.ShowDialog() == DialogResult.OK)
            {
                TB_Path.Text = fbd.SelectedPath;
                B_Go.Enabled = true;
            }
        }

        private void B_Go_Click(object sender, EventArgs e)
        {
            if (isWorkerThreadAlive)
            {
                MessageBox.Show("There are files currently being processed.");
            }
            else
            {
                TempFile = "";
                TB_Progress.Text = "";
                var thread = new Thread(BuildIStorage);
                if (TB_Path.Text.Length == 0)
                    return;
                B_Open.Enabled = false;
                B_Go.Enabled = false;
                thread.IsBackground = true;
                thread.Start();
            }
        }

        private void BuildIStorage()
        {
            isWorkerThreadAlive = true;
            ROOT_DIR = TB_Path.Text;
            var FNT = new FileNameTable(ROOT_DIR);
            var RomFiles = new IStorageFile[FNT.NumFiles];
            var In = new LayoutManager.Input[FNT.NumFiles];
            TB_Progress.Invoke((Action) (() => UpdateTB_Progress("Creating Layout...")));
            for (var i = 0; i < FNT.NumFiles; i++)
                In[i] = new LayoutManager.Input
                {
                    FilePath = FNT.NameEntryTable[i].FullName,
                    AlignmentSize = 0x10
                };
            var Out = LayoutManager.Create(In);
            for (var i = 0; i < Out.Length; i++)
                RomFiles[i] = new IStorageFile
                {
                    Offset = Out[i].Offset,
                    PathName = Out[i].FilePath.Replace(Path.GetFullPath(ROOT_DIR), "").Replace("\\", "/"),
                    FullName = Out[i].FilePath,
                    Size = Out[i].Size
                };
            using (var memoryStream = new MemoryStream())
            {
                TB_Progress.Invoke((Action) (() => UpdateTB_Progress("Creating IStorage MetaData...")));
                var mdb = new MetaDataBuilder();
                mdb.BuildIStorageHeader(memoryStream, RomFiles, ROOT_DIR);
                MakeIStorageData(RomFiles, memoryStream);
            }
            isWorkerThreadAlive = false;
            Invoke((Action) (() =>
                {
                    B_Go.Enabled = true;
                    B_Open.Enabled = true;
                }
            ));
        }

        public static ulong Align(ulong input, ulong alignsize)
        {
            var output = input;
            if (output % alignsize != 0)
                output += alignsize - output % alignsize;
            return output;
        }

        private void MakeIStorageData(IStorageFile[] RomFiles, MemoryStream metadata)
        {
            TempFile = Path.GetRandomFileName();
            var OutFileStream = new FileStream(TempFile, FileMode.Create, FileAccess.ReadWrite);
            try
            {
                OutFileStream.Seek(0, SeekOrigin.Begin);
                var metadataArray = metadata.ToArray();
                OutFileStream.Write(metadataArray, 0, metadataArray.Length);
                long baseOfs = 0x200;
                TB_Progress.Invoke((Action) (() => UpdateTB_Progress("Writing File Data...")));
                PB_Show.Invoke((Action) (() =>
                {
                    PB_Show.Minimum = 0;
                    PB_Show.Maximum = RomFiles.Length;
                    PB_Show.Value = 0;
                    PB_Show.Step = 1;
                }));
                for (var i = 0; i < RomFiles.Length; i++)
                {
                    OutFileStream.Seek(baseOfs + (long) RomFiles[i].Offset, SeekOrigin.Begin);
                    using (var inStream = new FileStream(RomFiles[i].FullName, FileMode.Open, FileAccess.Read))
                    {
                        while (inStream.Position < inStream.Length)
                        {
                            var buffer = new byte[inStream.Length - inStream.Position > 0x100000
                                ? 0x100000
                                : inStream.Length - inStream.Position];
                            inStream.Read(buffer, 0, buffer.Length);
                            OutFileStream.Write(buffer, 0, buffer.Length);
                        }
                    }
                    PB_Show.Invoke((Action) (() => PB_Show.PerformStep()));
                }
            }
            finally
            {
                if (OutFileStream != null)
                    OutFileStream.Dispose();
            }
            TB_Progress.Invoke((Action) (() => UpdateTB_Progress("Prompting to Save...")));
            var sfd = new SaveFileDialog();
            Invoke((Action) (() =>
            {
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    TB_Progress.Invoke((Action) (() => UpdateTB_Progress("Writing Binary to " + sfd.FileName + "...")));
                    var thread = new Thread(() => WriteBinary(TempFile, sfd.FileName));
                    thread.IsBackground = true;
                    thread.Start();
                }
            }));
        }

        public void WriteBinary(string tempFile, string outFile)
        {
            using (var fs = new FileStream(outFile, FileMode.Create))
            {
                using (var writer = new BinaryWriter(fs))
                {
                    using (var fileStream = new FileStream(tempFile, FileMode.Open, FileAccess.Read))
                    {
                        uint BUFFER_SIZE = 0x400000; // 4MB Buffer
                        PB_Show.Invoke((Action) (() =>
                        {
                            PB_Show.Minimum = 0;
                            PB_Show.Maximum = (int) (fileStream.Length / BUFFER_SIZE);
                            PB_Show.Value = 0;
                            PB_Show.Step = 1;
                        }));
                        var buffer = new byte[BUFFER_SIZE];
                        while (true)
                        {
                            var count = fileStream.Read(buffer, 0, buffer.Length);
                            if (count != 0)
                            {
                                writer.Write(buffer, 0, count);
                                PB_Show.Invoke((Action) (() => PB_Show.PerformStep()));
                            }
                            else
                            {
                                break;
                            }
                        }
                    }
                    writer.Flush();
                }
            }
            File.Delete(TempFile);
            Invoke((Action) (() => { MessageBox.Show("Wrote IStorage to " + outFile + "."); }));
        }

        public void UpdateTB_Progress(string text)
        {
            TB_Progress.Text += text + Environment.NewLine;
        }
    }

    public class IStorageFile
    {
        public string FullName;
        public ulong Offset;
        public string PathName;
        public ulong Size;

        public static ulong GetDataBlockLength(IStorageFile[] files, ulong PreData)
        {
            return files.Length == 0
                ? PreData
                : PreData + files[files.Length - 1].Offset + files[files.Length - 1].Size;
        }
    }


    public class FileNameTable
    {
        internal FileNameTable(string rootPath)
        {
            NameEntryTable = new List<FileInfo>();
            AddDirectory(new DirectoryInfo(rootPath));
            NameEntryTable.Sort(FilePathCompare);
        }

        public List<FileInfo> NameEntryTable { get; }

        public int NumFiles => NameEntryTable.Count;

        internal static int FilePathCompare(FileInfo a, FileInfo b)
        {
            var aN = Encoding.UTF8.GetBytes(a.FullName.Replace('\\', '/'));
            var bN = Encoding.UTF8.GetBytes(b.FullName.Replace('\\', '/'));
            for (var i = 0; i < aN.Length && i < bN.Length; i++)
            {
                if (aN[i] < bN[i])
                    return -1;
                if (bN[i] < aN[i])
                    return 1;
            }
            if (aN.Length < bN.Length)
                return -1;
            return bN.Length < aN.Length ? 1 : 0;
        }

        internal void AddDirectory(DirectoryInfo dir)
        {
            foreach (var subdir in dir.GetDirectories())
                AddDirectory(subdir);
            foreach (var fileInfo in dir.GetFiles())
                NameEntryTable.Add(fileInfo);
        }
    }

    public class LayoutManager
    {
        public static Output[] Create(Input[] Input)
        {
            var list = new List<Output>();
            ulong Len = 0;
            foreach (var input in Input)
            {
                var output = new Output();
                var fileInfo = new FileInfo(input.FilePath);
                var ofs = IStorageBuilder.Align(Len, input.AlignmentSize);
                output.FilePath = input.FilePath;
                output.Offset = ofs;
                output.Size = (ulong) fileInfo.Length;
                list.Add(output);
                Len = ofs + (ulong) fileInfo.Length;
            }
            return list.ToArray();
        }

        public class Input
        {
            public uint AlignmentSize;
            public string FilePath;
        }

        public class Output
        {
            public string FilePath;
            public ulong Offset;
            public ulong Size;
        }
    }
}