using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace WebNotifications
{
    // File bytes stay on this device until the user accepts the converted file
    // and the existing school file picker continues its normal upload process.
    public sealed class SubmissionConversionBridge : IDisposable
    {
        [Serializable] private class Packet
        {
            public string kind, nonce, page, requestId, operation, name, format, data;
            public int index, total, size;
        }
        [Serializable] private class Reply
        {
            public string nonce, requestId, status, data, error;
            public int index, total;
        }
        private readonly ConcurrentQueue<Reply> replies = new ConcurrentQueue<Reply>();
        private CancellationTokenSource cancellation;
        private MemoryStream input;
        private string nonce, page, requestId, name, format, worker;
        private int expectedSize, expectedParts, receivedParts;
        private float inputDeadline;
        private bool converting;

        public void Configure(string documentNonce, string documentPage, string workerScript)
        {
            Dispose();
            nonce = documentNonce; page = documentPage; worker = workerScript;
            cancellation = new CancellationTokenSource();
        }

        public void Receive(string message, string currentPage)
        {
            Packet packet;
            try { packet = JsonUtility.FromJson<Packet>(message); } catch { return; }
            if (packet == null || packet.kind != "submission_convert" || string.IsNullOrEmpty(nonce) ||
                packet.nonce != nonce || packet.page != page || page != currentPage || !Guid.TryParseExact(packet.requestId, "N", out _)) return;
            if (packet.operation == "cancel")
            {
                if (packet.requestId == requestId)
                {
                    cancellation.Cancel();
                    cancellation.Dispose();
                    cancellation = new CancellationTokenSource();
                    ResetInput();
                }
                return;
            }
            try
            {
                if (packet.operation == "begin")
                {
                    if (input != null || converting) throw new InvalidOperationException("이전 문서를 변환하는 중입니다. 완료 후 다시 선택해 주세요.");
                    if (!SubmissionDocumentConverter.IsAvailable) throw new InvalidOperationException("Windows PC에 설치된 한컴오피스 한글이 필요합니다.");
                    SubmissionDocumentConverter.InputFormat(packet.name ?? "");
                    if ((packet.format != "hwp" && packet.format != "hwpx") || packet.size <= 0 || packet.size > SubmissionDocumentConverter.MaxInputBytes ||
                        packet.total != (packet.size + 17999) / 18000) throw new InvalidDataException("문서 형식이나 크기가 올바르지 않습니다. 최대 16MB까지 지원합니다.");
                    requestId = packet.requestId; name = packet.name; format = packet.format;
                    expectedSize = packet.size; expectedParts = packet.total; receivedParts = 0;
                    input = new MemoryStream(packet.size);
                    inputDeadline = Time.realtimeSinceStartup + 60;
                    return;
                }
                if (packet.requestId != requestId || input == null || converting) return;
                if (packet.operation == "chunk")
                {
                    if (packet.index != receivedParts || receivedParts >= expectedParts || packet.data == null || packet.data.Length > 24000)
                        throw new InvalidDataException("문서 전달 순서가 올바르지 않습니다. 다시 선택해 주세요.");
                    var bytes = System.Convert.FromBase64String(packet.data);
                    var expectedChunkSize = Math.Min(18000, expectedSize - receivedParts * 18000);
                    if (bytes.Length != expectedChunkSize) throw new InvalidDataException("문서 일부가 누락되었습니다. 다시 선택해 주세요.");
                    input.Write(bytes, 0, bytes.Length); receivedParts++;
                    inputDeadline = Time.realtimeSinceStartup + 60;
                    return;
                }
                if (packet.operation != "finish") return;
                if (receivedParts != expectedParts || input.Length != expectedSize) throw new InvalidDataException("문서 전달이 완료되지 않았습니다.");
                var source = input.ToArray(); input.Dispose(); input = null;
                converting = true; inputDeadline = 0;
                var jobNonce = nonce; var jobId = requestId; var jobName = name; var jobFormat = format;
                var jobWorker = worker; var token = cancellation.Token;
                Task.Run(() =>
                {
                    try
                    {
                        var output = SubmissionDocumentConverter.Convert(source, jobName, jobFormat, jobWorker, token);
                        int total = (output.Length + 17999) / 18000;
                        for (int index = 0; index < total; index++)
                        {
                            token.ThrowIfCancellationRequested();
                            replies.Enqueue(new Reply { nonce = jobNonce, requestId = jobId, status = "chunk", index = index, total = total,
                                data = System.Convert.ToBase64String(output, index * 18000, Math.Min(18000, output.Length - index * 18000)) });
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception error)
                    {
                        if (!token.IsCancellationRequested) replies.Enqueue(new Reply { nonce = jobNonce, requestId = jobId, status = "error", error = error.Message });
                    }
                });
            }
            catch (Exception error)
            {
                replies.Enqueue(new Reply { nonce = nonce, requestId = packet.requestId, status = "error", error = error.Message });
                if (packet.requestId == requestId && !converting) ResetInput();
            }
        }

        public void Poll(Action<string> evaluate)
        {
            if (input != null && Time.realtimeSinceStartup > inputDeadline)
            {
                replies.Enqueue(new Reply { nonce = nonce, requestId = requestId, status = "error", error = "문서 전달 시간이 초과되었습니다. 파일을 다시 선택해 주세요." });
                ResetInput();
            }
            for (int count = 0; count < 4 && replies.TryDequeue(out var reply); count++)
            {
                if (reply.nonce != nonce) continue;
                if (reply.requestId == requestId && (reply.status == "error" || reply.status == "chunk" && reply.index == reply.total - 1)) ResetInput();
                evaluate("window.PixelCampusSubmission&&window.PixelCampusSubmission.receive(" + JsonUtility.ToJson(reply) + ");");
            }
        }

        private void ResetInput()
        {
            input?.Dispose(); input = null; requestId = name = format = null;
            expectedSize = expectedParts = receivedParts = 0; inputDeadline = 0; converting = false;
        }

        public void Dispose()
        {
            cancellation?.Cancel(); cancellation?.Dispose(); cancellation = null;
            ResetInput(); nonce = page = worker = null;
            while (replies.TryDequeue(out _)) { }
        }
    }
}
