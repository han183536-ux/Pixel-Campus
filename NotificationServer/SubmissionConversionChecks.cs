using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using WebNotifications;

internal static class SubmissionConversionChecks
{
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
    private static void Reject(Action action, string message)
    {
        try { action(); } catch (InvalidDataException) { Console.WriteLine("PASS: " + message); return; }
        throw new Exception("Expected rejection: " + message);
    }
    private static byte[] Archive(params (string name, string text)[] entries)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            foreach (var entry in entries)
            { using var writer = new StreamWriter(zip.CreateEntry(entry.name).Open(), new UTF8Encoding(false)); writer.Write(entry.text); }
        return stream.ToArray();
    }
    public static void Main(string[] args)
    {
        Check(SubmissionDocumentConverter.InputFormat("보고서.docx") == "OOXML", "DOCX uses Hancom's OOXML import filter");
        Check(SubmissionDocumentConverter.InputFormat("제출.HWPX") == "HWPX", "Input extension matching ignores case");
        Reject(() => SubmissionDocumentConverter.InputFormat("scan.pdf"), "PDF is not relabeled as an editable HWP document");
        Reject(() => SubmissionDocumentConverter.InputFormat("file.docx.exe"), "Executable file extensions cannot enter document conversion");
        Reject(() => SubmissionDocumentConverter.ValidateOutput(Encoding.UTF8.GetBytes("fake hwp"), "hwp"), "Renamed text cannot pass HWP verification");
        var ole = new byte[512]; new byte[] {208,207,17,224,161,177,26,225}.CopyTo(ole,0);
        Reject(() => SubmissionDocumentConverter.ValidateOutput(ole, "hwp"), "Generic OLE/Word containers cannot pass HWP verification");
        var valid = Archive(("mimetype","application/hwp+zip"),("Contents/content.hpf","manifest"),("Contents/section0.xml","section"));
        SubmissionDocumentConverter.ValidateOutput(valid, "hwpx");
        Console.WriteLine("PASS: HWPX validation inspects the archive manifest, section and MIME type");
        Reject(() => SubmissionDocumentConverter.ValidateOutput(Archive(("mimetype","application/zip"),("Contents/content.hpf","manifest"),("Contents/section0.xml","section")), "hwpx"), "Ordinary ZIP files cannot pass HWPX verification");
        Reject(() => SubmissionDocumentConverter.ValidateOutput(valid, "hwp"), "HWPX is not accepted as HWP by renaming");
        if (args.Length == 0 || args[0] != "--integration") return;
        var root = Path.GetFullPath(args[1]); Directory.CreateDirectory(root);
        const string marker = "PixelCampus 한글 변환 확인 2026";
        var source = File.ReadAllBytes(args[3]);
        var original = Path.Combine(root,"source.docx"); File.WriteAllBytes(original,source);
        var before = SHA256.HashData(source);
        var worker = File.ReadAllText(args[2]);
        foreach (var target in new[] {"hwp","hwpx"})
        {
            var output = SubmissionDocumentConverter.Convert(source,"source.docx",target,worker,CancellationToken.None);
            File.WriteAllBytes(Path.Combine(root,"converted."+target),output);
            if (target=="hwpx")
            {
                using var zip = new ZipArchive(new MemoryStream(output));
                using var reader = new StreamReader(zip.GetEntry("Contents/section0.xml").Open());
                var text = reader.ReadToEnd();
                Check(text.Contains(marker) && text.Contains("20261234") && text.Contains(":tbl"), "Hancom conversion retains Korean text and the table in HWPX");
            }
            else Check(Encoding.Unicode.GetString(output).Contains(marker), "Hancom conversion retains Korean text in the HWP preview stream");
            Console.WriteLine("PASS: Actual DOCX to " + target.ToUpperInvariant() + " conversion");
        }
        var roundtrip = SubmissionDocumentConverter.Convert(File.ReadAllBytes(Path.Combine(root,"converted.hwpx")),"converted.hwpx","hwp",worker,CancellationToken.None);
        Check(Encoding.Unicode.GetString(roundtrip).Contains(marker), "Actual HWPX to HWP conversion retains Korean text");
        Check(before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(original))), "Original DOCX remains byte-for-byte unchanged");
    }
}
