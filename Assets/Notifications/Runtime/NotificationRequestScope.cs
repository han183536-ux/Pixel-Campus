using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Networking;

namespace WebNotifications
{
    // Own requests independently of coroutine disposal, which is not guaranteed on StopAllCoroutines.
    internal sealed class NotificationRequestScope
    {
        private readonly HashSet<Lease> active = new HashSet<Lease>();
        public Lease Track(UnityWebRequest request)
        {
            var lease = new Lease(this, request);
            active.Add(lease);
            return lease;
        }
        public void CancelAll()
        {
            foreach (var lease in new List<Lease>(active)) lease.Cancel();
        }

        internal sealed class Lease : IDisposable
        {
            private readonly NotificationRequestScope owner;
            public UnityWebRequest Request { get; }
            public bool Cancelled { get; private set; }
            private bool disposed;
            internal Lease(NotificationRequestScope owner, UnityWebRequest request)
            { this.owner = owner; Request = request ?? throw new ArgumentNullException(nameof(request)); }

            public IEnumerator Send()
            {
                if (Cancelled || disposed) yield break;
                Request.SendWebRequest();
                // Do not yield the native AsyncOperation: it can retain a coroutine GC handle
                // after its managed domain has been unloaded. Poll completion on Unity's main thread.
                while (!Cancelled && !disposed && !Request.isDone) yield return null;
            }
            internal void Cancel()
            {
                if (disposed) return;
                Cancelled = true;
                try { Request.Abort(); }
                finally { Dispose(); }
            }
            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                owner.active.Remove(this);
                Request.Dispose();
            }
        }
    }
}
