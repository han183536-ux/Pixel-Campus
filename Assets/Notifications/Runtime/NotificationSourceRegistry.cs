using System;
using UnityEngine;

namespace WebNotifications
{
    public class NotificationSourceRegistry : MonoBehaviour
    {
        [Serializable]
        public class Source
        {
            public string sourceId, displayName, targetObjectId;
            public Transform target;
        }
        public Source[] sources;
    }
}
