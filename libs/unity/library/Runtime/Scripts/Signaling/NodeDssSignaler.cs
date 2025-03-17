// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Microsoft.MixedReality.WebRTC.Unity
{
    /// <summary>
    /// Simple signaler for debug and testing.
    /// This is based on https://github.com/bengreenier/node-dss and SHOULD NOT BE USED FOR PRODUCTION.
    /// </summary>
    [AddComponentMenu("MixedReality-WebRTC/NodeDSS Signaler")]
    public class NodeDssSignaler : Signaler
    {
        private bool _readyForPolling = false;

        public event Action<SdpMessage> OnSdpMessageReceived;
        public event Action<IceCandidate> OnIceCandidateReceived;

        /// <summary>
        /// Automatically log all errors to the Unity console.
        /// </summary>
        [Tooltip("Automatically log all errors to the Unity console")]
        public bool AutoLogErrors = true;

        /// <summary>
        /// Unique identifier of the local peer.
        /// </summary>
        [Tooltip("Unique identifier of the local peer")]
        public string LocalPeerId;

        /// <summary>
        /// Unique identifier of the remote peer.
        /// </summary>
        [Tooltip("Unique identifier of the remote peer")]
        public string RemotePeerId;

        /// <summary>
        /// The https://github.com/bengreenier/node-dss HTTP service address to connect to
        /// </summary>
        [Header("Server")]
        [Tooltip("The node-dss server to connect to")]
        public string HttpServerAddress = "http://127.0.0.1:3000/";

        /// <summary>
        /// The interval (in ms) that the server is polled at
        /// </summary>
        [Tooltip("The interval (in ms) that the server is polled at")]
        public float PollTimeMs = 500f;

        /// <summary>
        /// Message exchanged with a <c>node-dss</c> server, serialized as JSON.
        /// </summary>
        /// <remarks>
        /// The names of the fields is critical here for proper JSON serialization.
        /// </remarks>
        [Serializable]
        public class NodeDssMessage
        {
            /// <summary>
            /// Separator for ICE messages.
            /// </summary>
            public const string IceSeparatorChar = "|";

            /// <summary>
            /// Possible message types as-serialized on the wire to <c>node-dss</c>.
            /// </summary>
            public enum Type
            {
                /// <summary>
                /// An unrecognized message.
                /// </summary>
                Unknown = 0,

                /// <summary>
                /// A SDP offer message.
                /// </summary>
                Offer,

                /// <summary>
                /// A SDP answer message.
                /// </summary>
                Answer,

                /// <summary>
                /// A trickle-ice or ice message.
                /// </summary>
                Ice
            }

            /// <summary>
            /// Convert a message type from <see xref="string"/> to <see cref="Type"/>.
            /// </summary>
            /// <param name="stringType">The message type as <see xref="string"/>.</param>
            /// <returns>The message type as a <see cref="Type"/> object.</returns>
            public static Type MessageTypeFromString(string stringType)
            {
                if (string.Equals(stringType, "offer", StringComparison.OrdinalIgnoreCase))
                {
                    return Type.Offer;
                }
                else if (string.Equals(stringType, "answer", StringComparison.OrdinalIgnoreCase))
                {
                    return Type.Answer;
                }
                throw new ArgumentException($"Unkown signaler message type '{stringType}'", "stringType");
            }

            public static Type MessageTypeFromSdpMessageType(SdpMessageType type)
            {
                switch (type)
                {
                case SdpMessageType.Offer: return Type.Offer;
                case SdpMessageType.Answer: return Type.Answer;
                default: return Type.Unknown;
                }
            }

            public IceCandidate ToIceCandidate()
            {
                if (MessageType != Type.Ice)
                {
                    throw new InvalidOperationException("The node-dss message it not an ICE candidate message.");
                }
                var parts = Data.Split(new string[] { IceSeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
                // Note the inverted arguments; candidate is last in IceCandidate, but first in the node-dss wire message
                return new IceCandidate
                {
                    SdpMid = parts[2],
                    SdpMlineIndex = int.Parse(parts[1]),
                    Content = parts[0]
                };
            }

            public NodeDssMessage(SdpMessage message)
            {
                MessageType = MessageTypeFromSdpMessageType(message.Type);
                Data = message.Content;
                IceDataSeparator = string.Empty;
            }

            public NodeDssMessage(IceCandidate candidate)
            {
                MessageType = Type.Ice;
                Data = string.Join(IceSeparatorChar, candidate.Content, candidate.SdpMlineIndex.ToString(), candidate.SdpMid);
                IceDataSeparator = IceSeparatorChar;
            }

            /// <summary>
            /// The message type.
            /// </summary>
            public Type MessageType = Type.Unknown;

            /// <summary>
            /// The primary message contents.
            /// </summary>
            public string Data;

            /// <summary>
            /// The data separator needed for proper ICE serialization.
            /// </summary>
            public string IceDataSeparator;
        }

        /// <summary>
        /// Internal timing helper
        /// </summary>
        private float timeSincePollMs = 0f;

        /// <summary>
        /// Internal last poll response status flag
        /// </summary>
        private bool lastGetComplete = true;


        #region ISignaler interface

        /// <inheritdoc/>
        public override Task SendMessageAsync(SdpMessage message)
        {
            return SendMessageImplAsync(new NodeDssMessage(message));
        }

        /// <inheritdoc/>
        public override Task SendMessageAsync(IceCandidate candidate)
        {
            return SendMessageImplAsync(new NodeDssMessage(candidate));
        }

        #endregion

        private Task SendMessageImplAsync(NodeDssMessage message)
        {
            // This method needs to return a Task object which gets completed once the signaler message
            // has been sent. Because the implementation uses a Unity coroutine, use a reset event to
            // signal the task to complete from the coroutine after the message is sent.
            // Note that the coroutine is a Unity object so needs to be started from the main Unity app thread.
            // Also note that TaskCompletionSource<bool> is used as a no-result variant; there is no meaning
            // to the bool value.
            // https://stackoverflow.com/questions/11969208/non-generic-taskcompletionsource-or-alternative
            var tcs = new TaskCompletionSource<bool>();
            _mainThreadWorkQueue.Enqueue(() => StartCoroutine(PostToServerAndWait(message, tcs)));
            return tcs.Task;
        }

        /// <summary>
        /// Unity Engine Start() hook
        /// </summary>
        /// <remarks>
        /// https://docs.unity3d.com/ScriptReference/MonoBehaviour.Start.html
        /// </remarks>
        private void Start()
        {
            if (string.IsNullOrEmpty(HttpServerAddress))
            {
                throw new ArgumentNullException("HttpServerAddress");
            }
            if (!HttpServerAddress.EndsWith("/"))
            {
                HttpServerAddress += "/";
            }

            // If not explicitly set, default local ID to some unique ID generated by Unity
            if (string.IsNullOrEmpty(LocalPeerId))
            {
                LocalPeerId = SystemInfo.deviceName;
            }
        }

        /// <summary>
        /// Internal helper for sending HTTP data to the node-dss server using POST
        /// </summary>
        /// <param name="msg">the message to send</param>
        private IEnumerator PostToServer(NodeDssMessage msg)
        {
            if (RemotePeerId.Length == 0)
            {
                throw new InvalidOperationException("Cannot send SDP message to remote peer; invalid empty remote peer ID.");
            }

            // ⚠️ Hier eigene JSON-Struktur bauen
            var safeData = msg.Data.Replace("\n", "\\n").Replace("\r", "");
            var json = $"{{\"MessageType\":\"{msg.MessageType}\",\"Data\":\"{safeData}\"}}";

            Debug.Log($"[DEBUG] Gesendetes JSON an Server: {json}");
            var data = System.Text.Encoding.UTF8.GetBytes(json);

            var www = new UnityWebRequest($"{HttpServerAddress}data/{RemotePeerId}", UnityWebRequest.kHttpVerbPOST);
            www.uploadHandler = new UploadHandlerRaw(data);
            www.SetRequestHeader("Content-Type", "application/json"); // 🟢 Jetzt explizit setzen!


            yield return www.SendWebRequest();

            if (AutoLogErrors && (www.isNetworkError || www.isHttpError))
            {
                Debug.Log($"Failed to send message to remote peer {RemotePeerId}: {www.error}");
            }
        }


        /// <summary>
        /// Internal helper to wrap a coroutine into a synchronous call for use inside
        /// a <see cref="Task"/> object.
        /// </summary>
        /// <param name="msg">the message to send</param>
        private IEnumerator PostToServerAndWait(NodeDssMessage message, TaskCompletionSource<bool> tcs)
        {
            yield return StartCoroutine(PostToServer(message));
            const bool dummy = true; // unused
            tcs.SetResult(dummy);
        }

        /// <summary>
        /// Internal coroutine helper for receiving HTTP data from the DSS server using GET
        /// and processing it as needed
        /// </summary>
        /// <returns>the message</returns>
        /// 

        [Serializable]
        public class NodeDssJsonMessage
        {
            public string MessageType;
            public string Data;
        }

        private bool IsNativePeerReady()
        {
            return _nativePeer != null && _nativePeer.Initialized;
        }

        private IEnumerator CO_GetAndProcessFromServer()
        {
            // ...
            if (!IsNativePeerReady())
            {
                Debug.LogWarning("Signal empfangen, aber Native Peer noch nicht ready. Warte auf nächste Poll-Iteration.");
                lastGetComplete = true;
                yield break;
            }
            if (HttpServerAddress.Length == 0)
            {
                throw new InvalidOperationException("Cannot receive SDP messages from remote peer; invalid empty HTTP server address.");
            }
            if (LocalPeerId.Length == 0)
            {
                throw new InvalidOperationException("Cannot receive SDP messages from remote peer; invalid empty local peer ID.");
            }

            var www = UnityWebRequest.Get($"{HttpServerAddress}data/{LocalPeerId}");
            yield return www.SendWebRequest();

            if (!www.isNetworkError && !www.isHttpError)
            {
                var json = www.downloadHandler.text;
                // Verwende unsere neue Hilfsklasse zum Parsen des JSON
                var jsonMsg = JsonUtility.FromJson<NodeDssJsonMessage>(json);
                if (jsonMsg != null)
                {
                    DebugLogLong($"[DEBUG] Empfangene Nachricht: MessageType={jsonMsg.MessageType} | Data Length={jsonMsg.Data?.Length}");

                    // WICHTIG: Überprüfe, ob die native PeerConnection initialisiert ist.
                    if (_nativePeer == null)
                    {
                        Debug.LogWarning("Native PeerConnection ist noch nicht initialisiert – Verarbeitung der Remote-Nachricht wird übersprungen.");
                        lastGetComplete = true;
                        yield break;
                    }

                    Debug.Log($"🟢 JSON empfangen: MessageType={jsonMsg.MessageType}, DataLength={jsonMsg.Data.Length}");

                    switch (jsonMsg.MessageType.ToLowerInvariant())
                    {
                        case "offer":
                        case "answer":
                            var sdpMsg = new WebRTC.SdpMessage
                            {
                                Type = jsonMsg.MessageType.ToLowerInvariant() == "offer" ? SdpMessageType.Offer : SdpMessageType.Answer,
                                Content = jsonMsg.Data.Replace("\\n", "\n")
                            };

                            if (OnSdpMessageReceived != null)
                            {
                                Debug.Log($"🔥 [EVENT] OnSdpMessageReceived feuert jetzt mit {sdpMsg.Type}");
                            }
                            else
                            {
                                Debug.LogWarning("⚠️ Kein Subscriber für OnSdpMessageReceived vorhanden");
                            }

                            OnSdpMessageReceived?.Invoke(sdpMsg);
                            break;

                        case "ice":
                            var iceParts = jsonMsg.Data.Replace("\\n", "\n").Split(NodeDssMessage.IceSeparatorChar);
                            if (iceParts.Length == 3)
                            {
                                var iceCandidate = new IceCandidate
                                {
                                    SdpMid = iceParts[0],
                                    SdpMlineIndex = int.Parse(iceParts[1]),
                                    Content = iceParts[2]
                                };

                                if (OnIceCandidateReceived != null)
                                {
                                    Debug.Log($"🔥 [EVENT] OnIceCandidateReceived feuert jetzt");
                                }
                                else
                                {
                                    Debug.LogWarning("⚠️ Kein Subscriber für OnIceCandidateReceived vorhanden");
                                }

                                OnIceCandidateReceived?.Invoke(iceCandidate);
                            }
                            break;

                        default:
                            Debug.LogWarning($"⚠️ Unbekannter MessageType empfangen: {jsonMsg.MessageType}");
                            break;
                    }




                }
                else if (AutoLogErrors)
                {
                    Debug.LogError($"❌ JSON Parsing-Fehler: {json}");
                }
            }
            else if (AutoLogErrors && www.isNetworkError)
            {
                Debug.LogError($"Network error trying to send data to {HttpServerAddress}: {www.error}");
            }

            lastGetComplete = true;
        }

        // Feld hinzufügen
        private bool _pollingEnabled = false;

        // Event-Callback (aus dem Inspector aufrufbar)
        public void EnablePolling()
        {
            _pollingEnabled = true;
            Debug.Log("Signaler: Polling wurde über OnInitialized aktiviert.");
        }

        public void DisablePolling()
        {
            _pollingEnabled = false;
            Debug.Log("Signaler: Polling wurde deaktiviert.");
        }

        /// Wird vom Setup explizit aufgerufen, wenn alles korrekt subscribed wurde.
        public void EnableSafePolling()
        {
            _readyForPolling = true;
            _pollingEnabled = true;
            Debug.Log("🟢 Signaler Safe-Polling wurde aktiviert nach vollständigem Setup.");
        }






        /// <inheritdoc/>
        protected override void Update()
        {
            base.Update();

            if (!_pollingEnabled || !_readyForPolling)
            {
                return;
            }

            if (timeSincePollMs <= PollTimeMs)
            {
                timeSincePollMs += Time.deltaTime * 1000.0f;
                return;
            }

            if (!lastGetComplete)
            {
                return;
            }

            timeSincePollMs = 0f;
            StartCoroutine(CO_GetAndProcessFromServer());
        }



        private void DebugLogLong(string str)
        {
#if !UNITY_EDITOR && UNITY_ANDROID
            // On Android, logcat truncates to ~1000 characters, so split manually instead.
            const int maxLineSize = 1000;
            int totalLength = str.Length;
            int numLines = (totalLength + maxLineSize - 1) / maxLineSize;
            for (int i = 0; i < numLines; ++i)
            {
                int start = i * maxLineSize;
                int length = Math.Min(start + maxLineSize, totalLength) - start;
                Debug.Log(str.Substring(start, length));
            }
#else
            Debug.Log(str);
#endif
        }
    }
}
