using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

public class BChunk
{
    public const int SECTLEN = 2352;
    public const int WAV_RIFF_HLEN = 12;
    public const int WAV_FORMAT_HLEN = 24;
    public const int WAV_DATA_HLEN = 8;
    public const int WAV_HEADER_LEN = WAV_RIFF_HLEN + WAV_FORMAT_HLEN + WAV_DATA_HLEN;

    public string? BaseFile { get; set; }
    public string? BinFile { get; set; }
    public string? CueFile { get; set; }
    public bool Verbose { get; set; }
    public bool PsxTruncate { get; set; }
    public bool Raw { get; set; }
    public bool SwabAudio { get; set; }
    public bool ToWav { get; set; }

    public class Track
    {
        public int Num { get; set; }
        public int Mode { get; set; }
        public bool Audio { get; set; }
        public string Modes { get; set; } = "";
        public string Extension { get; set; } = "";
        public int Bstart { get; set; }
        public int Bsize { get; set; }
        public long StartSect { get; set; }
        public long StopSect { get; set; }
        public long Start { get; set; }
        public long Stop { get; set; }
    }

    public void ParseArgs(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("-"))
            {
                switch (args[i][1])
                {
                    case 'r':
                        Raw = true;
                        break;
                    case 'v':
                        Verbose = true;
                        break;
                    case 'w':
                        ToWav = true;
                        break;
                    case 'p':
                        PsxTruncate = true;
                        break;
                    case 's':
                        SwabAudio = true;
                        break;
                    case '?':
                    case 'h':
                        Console.WriteLine(GetUsage());
                        Environment.Exit(0);
                        break;
                }
            }
            else
            {
                switch (args.Length - i)
                {
                    case 2:
                        BinFile = args[i];
                        break;
                    case 1:
                        CueFile = args[i];
                        break;
                }
            }
        }

        if (string.IsNullOrEmpty(BinFile) || string.IsNullOrEmpty(CueFile))
        {
            Console.WriteLine(GetUsage());
            Environment.Exit(1);
        }

        BaseFile = Path.GetFileNameWithoutExtension(CueFile);
    }

    public string GetUsage()
    {
        return "用法: bchunk [-v] [-r] [-p (PSX)] [-w (wav)] [-s (交换音频字节序)]\n" +
               "         <image.bin> <image.cue>\n" +
               "示例: bchunk foo.bin foo.cue\n" +
               "  -v  详细模式\n" +
               "  -r  MODE2/2352的原始模式: 从偏移0写入所有2352字节 (VCD/MPEG)\n" +
               "  -p  MODE2/2352的PSX模式: 从偏移24写入2336字节\n" +
               "      (默认MODE2/2352模式从偏移24写入2048字节)\n" +
               "  -w  以WAV格式输出音频文件\n" +
               "  -s  交换音频字节序: 交换音频轨道中的字节顺序\n" +
               "输出文件将自动使用CUE文件的基本名称";
    }

    public string GetVersion()
    {
        return "Windows版binchunker, 版本1.2.1\n" +
               "\t基于Heikki Hannikainen <hessu@hes.iki.fi>的工作\n" +
               "\t根据GNU GPL发布, 版本2或更高(由您选择)。\n\n";
    }

    public long Time2Frames(string s)
    {
        string[] parts = s.Split(':');
        if (parts.Length != 3)
            return -1;

        int mins = int.Parse(parts[0]);
        int secs = int.Parse(parts[1]);
        int frames = int.Parse(parts[2]);

        return 75 * (mins * 60 + secs) + frames;
    }

    public void ProcessTracks()
    {
        Console.WriteLine(GetVersion());
        Console.WriteLine("读取CUE文件:");

        List<Track> tracks = ParseCueFile();

        Console.WriteLine("\n");

        using (FileStream binStream = new FileStream(BinFile!, FileMode.Open, FileAccess.Read))
        {
            foreach (Track track in tracks)
            {
                WriteTrack(binStream, track);
            }
        }

        Console.WriteLine("转换结束\n");
    }

    private List<Track> ParseCueFile()
    {
        List<Track> tracks = new List<Track>();
        Track? currentTrack = null;
        Track? previousTrack = null;

        string[] cueLines = File.ReadAllLines(CueFile!);

        for (int i = 1; i < cueLines.Length; i++)
        {
            string line = cueLines[i].Trim();
            if (string.IsNullOrEmpty(line))
                continue;

            if (line.StartsWith("TRACK"))
            {
                string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3)
                {
                    Console.WriteLine("... 错误, TRACK行格式不正确。");
                    continue;
                }

                previousTrack = currentTrack;
                currentTrack = new Track
                {
                    Num = int.Parse(parts[1]),
                    Modes = parts[2]
                };

                GetTrackMode(currentTrack);
                tracks.Add(currentTrack);

                Console.Write($"\n轨道 {currentTrack.Num}: {currentTrack.Modes,-12} ");
            }
            else if (line.StartsWith("INDEX") && currentTrack != null)
            {
                string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3)
                {
                    Console.WriteLine("... 错误, INDEX行格式不正确。");
                    continue;
                }

                int index = int.Parse(parts[1]);
                string time = parts[2];
                Console.Write($" {parts[1]} {parts[2]}");

                currentTrack.StartSect = Time2Frames(time);
                currentTrack.Start = currentTrack.StartSect * SECTLEN;

                if (Verbose)
                {
                    Console.Write($" (起始扇区 {currentTrack.StartSect} 偏移 {currentTrack.Start})");
                }

                if (previousTrack != null && previousTrack.StopSect < 0)
                {
                    previousTrack.StopSect = currentTrack.StartSect;
                    previousTrack.Stop = currentTrack.Start - 1;
                }
            }
        }

        if (currentTrack != null)
        {
            using (FileStream fs = new FileStream(BinFile!, FileMode.Open, FileAccess.Read))
            {
                currentTrack.Stop = fs.Length;
                currentTrack.StopSect = currentTrack.Stop / SECTLEN;
            }
        }

        return tracks;
    }

    private void GetTrackMode(Track track)
    {
        track.Extension = "iso";
        track.Audio = false;

        switch (track.Modes.ToUpper())
        {
            case "MODE1/2352":
                track.Bstart = 16;
                track.Bsize = 2048;
                break;
            case "MODE2/2352":
                if (Raw)
                {
                    track.Bstart = 0;
                    track.Bsize = 2352;
                }
                else if (PsxTruncate)
                {
                    track.Bstart = 0;
                    track.Bsize = 2336;
                }
                else
                {
                    track.Bstart = 24;
                    track.Bsize = 2048;
                }
                break;
            case "MODE2/2336":
                track.Bstart = 16;
                track.Bsize = 2336;
                break;
            case "AUDIO":
                track.Bstart = 0;
                track.Bsize = 2352;
                track.Audio = true;
                track.Extension = ToWav ? "wav" : "cdr";
                break;
            default:
                Console.Write("(?) ");
                track.Bstart = 0;
                track.Bsize = 2352;
                track.Extension = "ugh";
                break;
        }
    }

    private void WriteTrack(FileStream binStream, Track track)
    {
        // 修改文件名生成逻辑，移除轨道编号
        string fileName = $"{BaseFile}.{track.Extension}";
        Console.Write($"正在处理: {fileName} ");

        using (FileStream outStream = new FileStream(fileName, FileMode.Create, FileAccess.Write))
        {
            binStream.Seek(track.Start, SeekOrigin.Begin);

            long realLen = (track.StopSect - track.StartSect + 1) * track.Bsize;
            if (Verbose)
            {
                Console.WriteLine($"\n MMC扇区 {track.StartSect}->{track.StopSect} ({track.StopSect - track.StartSect + 1})");
                Console.WriteLine($"\n MMC字节 {track.Start}->{track.Stop} ({track.Stop - track.Start + 1})");
                Console.WriteLine($"\n 扇区数据在 {track.Bstart}, 每扇区 {track.Bsize} 字节");
                Console.WriteLine($"\n 实际数据 {realLen} 字节");
                Console.WriteLine();
            }

            Console.Write("                                          ");

            if (track.Audio && ToWav)
            {
                byte[] wavHeader = CreateWavHeader(realLen);
                outStream.Write(wavHeader, 0, wavHeader.Length);
            }

            long realSz = 0;
            long sect = track.StartSect;
            float progress = 0;

            byte[] buffer = new byte[SECTLEN];
            while (sect <= track.StopSect)
            {
                int bytesRead = binStream.Read(buffer, 0, SECTLEN);
                if (bytesRead == 0)
                    break;

                if (track.Audio && SwabAudio)
                {
                    for (int i = track.Bstart; i < track.Bstart + track.Bsize - 1; i += 2)
                    {
                        byte temp = buffer[i];
                        buffer[i] = buffer[i + 1];
                        buffer[i + 1] = temp;
                    }
                }

                outStream.Write(buffer, track.Bstart, track.Bsize);
                sect++;
                realSz += track.Bsize;

                if ((sect % 500) == 0)
                {
                    progress = (float)realSz / (float)realLen;
                    UpdateProgress(realSz, realLen, progress);
                }
            }

            progress = (float)realSz / (float)realLen;
            UpdateProgress(realSz, realLen, progress, true);
        }

        Console.WriteLine();
    }

    private byte[] CreateWavHeader(long dataLength)
    {
        using (MemoryStream ms = new MemoryStream())
        {
            ms.Write(Encoding.ASCII.GetBytes("RIFF"), 0, 4);
            ms.Write(BitConverter.GetBytes((int)(dataLength + WAV_DATA_HLEN + WAV_FORMAT_HLEN + 4)), 0, 4);
            ms.Write(Encoding.ASCII.GetBytes("WAVE"), 0, 4);

            ms.Write(Encoding.ASCII.GetBytes("fmt "), 0, 4);
            ms.Write(BitConverter.GetBytes(0x10), 0, 4);
            ms.Write(BitConverter.GetBytes((short)0x01), 0, 2);
            ms.Write(BitConverter.GetBytes((short)0x02), 0, 2);
            ms.Write(BitConverter.GetBytes(44100), 0, 4);
            ms.Write(BitConverter.GetBytes(44100 * 4), 0, 4);
            ms.Write(BitConverter.GetBytes((short)4), 0, 2);
            ms.Write(BitConverter.GetBytes((short)(2 * 8)), 0, 2);

            ms.Write(Encoding.ASCII.GetBytes("data"), 0, 4);
            ms.Write(BitConverter.GetBytes((int)dataLength), 0, 4);

            return ms.ToArray();
        }
    }

    private void UpdateProgress(long current, long total, float progress, bool final = false)
    {
        string progressBar = GetProgressBar(progress, 20);
        Console.Write($"\r{current / 1024 / 1024,4}/{total / 1024 / 1024,-4} MB  [{progressBar}] {progress * 100,3:0} %");
    }

    private string GetProgressBar(float progress, int length)
    {
        int filled = (int)(length * progress);
        return new string('*', filled) + new string(' ', length - filled);
    }
}

class Program
{
    static void Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("bchunk <输入.bin> <输入.cue>");
            return;
        }

        BChunk bchunk = new BChunk();
        try
        {
            bchunk.ParseArgs(args);
            bchunk.ProcessTracks();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"错误: {ex.Message}");
            Environment.Exit(1);
        }
    }
}