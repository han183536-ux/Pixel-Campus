using System;
using WebNotifications;

// Exercise request ownership without network access or a running Unity editor.
namespace UnityEngine.Networking
{
    public sealed class UnityWebRequest : IDisposable
    {
        public int Sends, Aborts, Disposals;
        public bool Done;
        public bool isDone => Disposals > 0 ? throw new ObjectDisposedException("request") : Done;
        public object SendWebRequest() { Sends++; return new object(); }
        public void Abort() { Aborts++; }
        public void Dispose() { Disposals++; }
    }
}

internal static class RequestScopeChecks
{
    private static int checks;
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
    public static void Main()
    {
        var scope = new NotificationRequestScope();
        var finished = new UnityEngine.Networking.UnityWebRequest();
        using (var lease = scope.Track(finished))
        {
            var wait = lease.Send();
            Check(wait.MoveNext() && wait.Current == null, "Wait must yield a frame, never the native async operation.");
            Check(finished.Sends == 1, "Send exactly once.");
            finished.Done = true;
            Check(!wait.MoveNext() && !lease.Cancelled, "Complete a successful request normally.");
        }
        Check(finished.Disposals == 1 && finished.Aborts == 0, "Dispose successful requests exactly once.");

        var pending = new UnityEngine.Networking.UnityWebRequest();
        var cancelled = scope.Track(pending);
        var waiting = cancelled.Send(); waiting.MoveNext();
        scope.CancelAll();
        Check(cancelled.Cancelled && pending.Aborts == 1 && pending.Disposals == 1, "Abort and release active requests on shutdown.");
        Check(!waiting.MoveNext(), "Cancelled wait exits without reading a disposed request.");
        cancelled.Dispose(); scope.CancelAll();
        Check(pending.Disposals == 1 && pending.Aborts == 1, "Repeated shutdown and iterator disposal must be harmless.");

        var queued = new UnityEngine.Networking.UnityWebRequest();
        var queuedLease = scope.Track(queued);
        scope.CancelAll();
        Check(!queuedLease.Send().MoveNext() && queued.Sends == 0, "Never start an already-cancelled request.");
        var workers = new UnityEngine.Networking.UnityWebRequest[4];
        for (int i = 0; i < workers.Length; i++)
        {
            workers[i] = new UnityEngine.Networking.UnityWebRequest();
            scope.Track(workers[i]).Send().MoveNext();
        }
        scope.CancelAll();
        foreach (var worker in workers) Check(worker.Aborts == 1 && worker.Disposals == 1, "Cancel every concurrent board worker.");

        var resumed = new UnityEngine.Networking.UnityWebRequest { Done = true };
        using (var lease = scope.Track(resumed))
            Check(!lease.Send().MoveNext() && !lease.Cancelled, "A component can start new requests after re-enable.");
        Console.WriteLine("PASS: " + checks + " request lifecycle checks.");
    }
}
