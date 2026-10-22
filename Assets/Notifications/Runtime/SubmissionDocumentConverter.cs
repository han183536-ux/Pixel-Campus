using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;

namespace WebNotifications
{
    // Runs only against a private copy of the selected file. Never rename a file
    // and describe it as converted: inspect the resulting document container.
    public static class SubmissionDocumentConverter
    {
        public const int MaxInputBytes = 16 * 1024 * 1024;
        public const int MaxOutputBytes = 32 * 1024 * 1024;

        public static bool IsAvailable
        {
            get
            {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
                try { return Type.GetTypeFromProgID("HWPFrame.HwpObject", false) != null; }
                catch { return false; }
#else
                return false;
#endif
            }
        }

        public static string InputFormat(string filename)
        {
            switch (Path.GetExtension(filename).ToLowerInvariant())
            {
                case ".docx": return "OOXML";
                case ".doc": return "MSWORD";
                case ".rtf": return "RTF";
                case ".hwp": return "HWP";
                case ".hwpx": return "HWPX";
                default: throw new InvalidDataException("한글 변환은 DOCX·DOC·RTF·HWP·HWPX 문서를 지원합니다. PDF나 이미지는 편집 가능한 한글 문서로 자동 변환하지 않습니다.");
            }
        }

        public static void ValidateOutput(byte[] data, string format)
        {
            if (data == null || data.Length == 0 || data.Length > MaxOutputBytes)
                throw new InvalidDataException("변환 결과의 크기가 올바르지 않습니다.");
            if (format == "hwp")
            {
                byte[] signature = { 0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1 };
                if (data.Length < 512) throw new InvalidDataException("정상적인 HWP 문서가 생성되지 않았습니다.");
                for (int index = 0; index < signature.Length; index++)
                    if (data[index] != signature[index]) throw new InvalidDataException("HWP 파일 형식 검증에 실패했습니다.");
                // A Word .doc is also an OLE container. Check the HWP header stream.
                if (Encoding.ASCII.GetString(data).IndexOf("HWP Document File", StringComparison.Ordinal) < 0)
                    throw new InvalidDataException("HWP 문서 헤더를 확인하지 못했습니다.");
            }
            else if (format == "hwpx")
            {
                using (var stream = new MemoryStream(data, false))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    var mime = archive.GetEntry("mimetype");
                    if (mime == null || mime.Length > 128 || archive.GetEntry("Contents/content.hpf") == null || archive.GetEntry("Contents/section0.xml") == null)
                        throw new InvalidDataException("HWPX 문서 구조를 확인하지 못했습니다.");
                    using (var reader = new StreamReader(mime.Open()))
                        if (reader.ReadToEnd().Trim() != "application/hwp+zip")
                            throw new InvalidDataException("HWPX 파일 형식 검증에 실패했습니다.");
                }
            }
            else throw new InvalidDataException("제출 형식은 HWP 또는 HWPX를 선택해 주세요.");
        }

        public static byte[] Convert(byte[] source, string filename, string target, string workerScript, CancellationToken cancellation)
        {
            var inputFormat = InputFormat(filename);
            if (target != "hwp" && target != "hwpx") throw new InvalidDataException("지원하지 않는 변환 형식입니다.");
            if (source == null || source.Length == 0 || source.Length > MaxInputBytes) throw new InvalidDataException("변환할 문서는 0바이트보다 크고 16MB 이하여야 합니다.");
            if (!IsAvailable) throw new InvalidOperationException("한글 문서 변환은 Windows PC에 설치된 한컴오피스 한글이 필요합니다. 뷰어만 설치한 경우에는 지원되지 않습니다.");
            var root = Path.Combine(Path.GetTempPath(), "PixelCampus-Conversion-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var input = Path.Combine(root, "source" + Path.GetExtension(filename).ToLowerInvariant());
                var output = Path.Combine(root, "submission." + target);
                var script = Path.Combine(root, "convert.ps1");
                var result = Path.Combine(root, "result.txt");
                File.WriteAllBytes(input, source);
                File.WriteAllText(script, workerScript, new UTF8Encoding(true));
                cancellation.ThrowIfCancellationRequested();
                var start = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
                    Arguments = "-NoProfile -NonInteractive -STA -ExecutionPolicy Bypass -File " + Quote(script) +
                        " -InputFile " + Quote(input) + " -OutputFile " + Quote(output) + " -InputFormat " + inputFormat +
                        " -OutputFormat " + target.ToUpperInvariant() + " -ResultFile " + Quote(result),
                    UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
                };
                using (var process = Process.Start(start))
                {
                    if (process == null) throw new InvalidOperationException("한글 변환 작업을 시작하지 못했습니다.");
                    var timer = Stopwatch.StartNew();
                    while (!process.WaitForExit(100))
                    {
                        if (cancellation.IsCancellationRequested || timer.Elapsed.TotalSeconds > 120)
                        {
                            // Stop only the worker and the Hancom instance it created.
                            // Never close an already-running personal document window.
                            TryStopOwnedHancom(root);
                            try { process.Kill(); } catch { }
                            process.WaitForExit(5000);
                            cancellation.ThrowIfCancellationRequested();
                            throw new TimeoutException("한글 변환이 지연되고 있습니다. 한컴오피스의 파일 접근 승인·문서 암호·실행 상태를 확인한 뒤 다시 시도해 주세요.");
                        }
                    }
                    if (process.ExitCode != 0 || !File.Exists(output))
                        throw new InvalidOperationException(File.Exists(result) ? File.ReadAllText(result).Trim() : "한글 변환에 실패했습니다. 한컴오피스에서 원본 파일을 열 수 있는지 확인해 주세요.");
                }
                cancellation.ThrowIfCancellationRequested();
                if (new FileInfo(output).Length > MaxOutputBytes) throw new InvalidDataException("변환 파일이 32MB를 초과했습니다.");
                var converted = File.ReadAllBytes(output);
                ValidateOutput(converted, target);
                return converted;
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { /* A still-closing converter may briefly hold a temporary file. */ }
            }
        }

        private static string Quote(string path)
        {
            if (path.IndexOf('"') >= 0) throw new InvalidDataException("변환 경로가 올바르지 않습니다.");
            return "\"" + path + "\"";
        }

        private static void TryStopOwnedHancom(string root)
        {
            try
            {
                // Worker records a PID only when the instance was absent before
                // activation. Verify its name and start time before stopping it.
                var proof = File.ReadAllLines(Path.Combine(root, "owned-process.txt"));
                if (proof.Length != 2 || !int.TryParse(proof[0], out var id) || !long.TryParse(proof[1], out var ticks)) return;
                using (var process = Process.GetProcessById(id))
                    if (process.ProcessName.Equals("Hwp", StringComparison.OrdinalIgnoreCase) && process.StartTime.ToUniversalTime().Ticks == ticks)
                        process.Kill();
            }
            catch { }
        }
    }
}
