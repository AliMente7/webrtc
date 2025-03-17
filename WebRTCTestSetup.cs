using UnityEngine;
using Microsoft.MixedReality.WebRTC;
using Microsoft.MixedReality.WebRTC.Unity;

using UnityPeerConnection = Microsoft.MixedReality.WebRTC.Unity.PeerConnection;
using NativePeerConnection = Microsoft.MixedReality.WebRTC.PeerConnection;

[RequireComponent(typeof(UnityPeerConnection))]
[RequireComponent(typeof(NodeDssSignaler))]
public class WebRtcTestSetup : MonoBehaviour
{
    private UnityPeerConnection peerConnectionUnity;
    private NativePeerConnection peerConnectionNative;
    private NodeDssSignaler signaler;

    [Header("Signaling Settings")]
    public bool isCaller = true;
    public string localPeerId = "Tablet";
    public string remotePeerId = "HoloLens";

    [Header("Debug Overlay")]
    public string statusText = "Idle";

    private bool _hasSentAnswer = false;

    public static WebRtcTestSetup Instance { get; private set; }


    void Awake()
    {
        Instance = this;
        peerConnectionUnity = GetComponent<UnityPeerConnection>();
        signaler = GetComponent<NodeDssSignaler>();

        // Peer-Zuweisung aus Inspector übernehmen
        signaler.LocalPeerId = localPeerId;
        signaler.RemotePeerId = remotePeerId;

        Debug.Log($"[Setup] LocalPeerId = {localPeerId}, RemotePeerId = {remotePeerId}");

        // Optional: Debug, wer Caller ist
        if (isCaller)
        {
            Debug.Log("Setup im Caller-Modus aktiv.");
        }
        else
        {
            Debug.Log("Callee-Modus aktiv -> wartet auf Offer...");
        }
    }

    private async void Start()
    {
        // Manuelle Initialisierung
        peerConnectionUnity.OnInitialized.AddListener(OnPeerInitialized);
        await peerConnectionUnity.InitializeAsync();
    }

    private void OnPeerInitialized()
    {
        Debug.Log("✅ OnPeerInitialized CALLEE erreicht");
        peerConnectionNative = peerConnectionUnity.Peer;

        signaler.OnSdpMessageReceived += HandleSdpMessage;
        signaler.OnIceCandidateReceived += HandleIceCandidate;

        // Sicherstellen, dass Polling erst jetzt losläuft
        signaler.EnableSafePolling();


        // Event-Handler registrieren
        peerConnectionNative.LocalSdpReadytoSend += (sdp) =>
        {
            Debug.Log($"[DEBUG] Local SDP Type: {sdp.Type} | Content Length: {sdp.Content?.Length}");
            signaler.SendMessageAsync(sdp);
            statusText = "Offer/Answer gesendet...";
        };

        peerConnectionNative.IceCandidateReadytoSend += (ice) =>
        {
            signaler.SendMessageAsync(ice);
            Debug.Log($"[ICE] Candidate gesendet an {signaler.RemotePeerId}");
        };

        peerConnectionNative.Connected += () =>
        {
            statusText = "✅ Connected!";
            Debug.Log("✅ WebRTC Verbindung erfolgreich aufgebaut!");
        };

        peerConnectionNative.IceStateChanged += (state) =>
        {
            Debug.Log($"ICE-State: {state}");
        };

        // Rolle anwenden
        if (isCaller)
        {
            statusText = "Caller: Initialisiere ICE...";
            StartCoroutine(WaitAndCreateOffer());
        }
        else
        {
            statusText = "Callee: Warte auf Offer...";
            Debug.Log("Callee-Modus aktiv → wartet auf Offer...");
        }
    }

    private void HandleSdpMessage(SdpMessage sdp)
    {
        Debug.Log($"🔥 HandleSdpMessage {(isCaller ? "CALLER" : "CALLEE")} aufgerufen mit {sdp.Type}");

        if (!isCaller && sdp.Type == SdpMessageType.Offer)
        {
            if (_hasSentAnswer)
            {
                Debug.LogWarning("⚠️ Answer wurde bereits gesendet, wird ignoriert.");
                return;
            }

            Debug.Log("🟢 Callee akzeptiert Offer und erzeugt Answer...");
            peerConnectionNative.SetRemoteDescriptionAsync(sdp).ContinueWith(_ =>
            {
                peerConnectionNative.CreateAnswer();
                _hasSentAnswer = true;
            });
        }

        if (isCaller && sdp.Type == SdpMessageType.Answer)
        {
            peerConnectionNative.SetRemoteDescriptionAsync(sdp);
        }
    }


    public void ResetAnswerFlag()
    {
        Debug.Log("🔴 Peer wurde getrennt (Inspector Event), Antwort-Flag zurückgesetzt.");
        _hasSentAnswer = false;
    }



    private void HandleIceCandidate(IceCandidate ice)
    {
        Debug.Log($"[ICE] Empfangener Candidate wird hinzugefügt.");
        peerConnectionNative.AddIceCandidate(ice);
    }


    private System.Collections.IEnumerator WaitAndCreateOffer()
    {
        yield return new WaitForSeconds(1.0f);

        if (!peerConnectionNative.IsConnected)
        {
            statusText = "Caller: Sende Offer...";
            Debug.Log("Caller erzeugt jetzt Offer");
            peerConnectionNative.CreateOffer();
        }
        else
        {
            statusText = "Caller: Peer bereits verbunden";
        }
    }

    private void OnGUI()
    {
        GUI.Label(new Rect(10, 10, 400, 20), $"Status: {statusText}");
    }
}
