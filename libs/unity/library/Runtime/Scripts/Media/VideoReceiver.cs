// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using UnityEngine;

namespace Microsoft.MixedReality.WebRTC.Unity
{
    /// <summary>
    /// This component represents a remote video source added as a video track to an
    /// existing WebRTC peer connection by a remote peer and received locally.
    /// The video track can optionally be displayed locally with a <see cref="VideoRenderer"/>.
    /// </summary>
    [AddComponentMenu("MixedReality-WebRTC/Video Receiver")]
    public class VideoReceiver : VideoRendererSource, IVideoSource, IMediaReceiver, IMediaReceiverInternal
    {
        /// <summary>
        /// Remote video track receiving data from the remote peer.
        ///
        /// This is <c>null</c> until <see cref="IMediaReceiver.Transceiver"/> is set to a non-null value
        /// and a remote track is added to that transceiver.
        /// </summary>
        public RemoteVideoTrack Track { get; private set; }

        /// <summary>
        /// Event raised when the video stream started.
        ///
        /// When this event is raised, the followings are true:
        /// - The <see cref="Track"/> property is a valid remote video track.
        /// - The <see cref="MediaReceiver.IsLive"/> property is <c>true</c>.
        /// - The <see cref="IsStreaming"/> will become <c>true</c> just after the event
        ///   is raised, by design.
        /// </summary>
        /// <remarks>
        /// This event is raised from the main Unity thread to allow Unity object access.
        /// </remarks>
        public VideoStreamStartedEvent VideoStreamStarted = new VideoStreamStartedEvent();

        /// <summary>
        /// Event raised when the video stream stopped.
        ///
        /// When this event is raised, the followings are true:
        /// - The <see cref="Track"/> property is <c>null</c>.
        /// - The <see cref="MediaReceiver.IsLive"/> property is <c>false</c>.
        /// - The <see cref="IsStreaming"/> has just become <c>false</c> right before the event
        ///   was raised, by design.
        /// </summary>
        /// <remarks>
        /// This event is raised from the main Unity thread to allow Unity object access.
        /// </remarks>
        public VideoStreamStoppedEvent VideoStreamStopped = new VideoStreamStoppedEvent();


        #region IVideoSource interface

        /// <inheritdoc/>
        public bool IsStreaming { get; protected set; }

        /// <inheritdoc/>
        public VideoStreamStartedEvent GetVideoStreamStarted() { return VideoStreamStarted; }

        /// <inheritdoc/>
        public VideoStreamStoppedEvent GetVideoStreamStopped() { return VideoStreamStopped; }

        /// <inheritdoc/>
        public VideoEncoding FrameEncoding { get; } = VideoEncoding.I420A;

        /// <summary>
        /// Register a frame callback to listen to incoming video data receiving through this
        /// video receiver from the remote peer.
        ///
        /// The callback can only be registered once the <see cref="Track"/> is valid, that is
        /// once the <see cref="VideoStreamStarted"/> event was raised.
        /// </summary>
        /// <param name="callback">The new frame callback to register.</param>
        /// <remarks>
        /// A typical application uses this callback to display the received video.
        /// </remarks>
        public void RegisterCallback(I420AVideoFrameDelegate callback)
        {
            if (Track != null)
            {
                Track.I420AVideoFrameReady += callback;
            }
        }

        /// <summary>
        /// Register a frame callback to listen to incoming video data receiving through this
        /// video receiver from the remote peer.
        ///
        /// The callback can only be registered once the <see cref="Track"/> is valid, that is
        /// once the <see cref="VideoStreamStarted"/> event was raised.
        /// </summary>
        /// <param name="callback">The new frame callback to register.</param>
        /// <remarks>
        /// A typical application uses this callback to display the received video.
        /// </remarks>
        public void RegisterCallback(Argb32VideoFrameDelegate callback)
        {
            if (Track != null)
            {
                Track.Argb32VideoFrameReady += callback;
            }
        }

        /// <inheritdoc/>
        public void UnregisterCallback(I420AVideoFrameDelegate callback)
        {
            if (Track != null)
            {
                Track.I420AVideoFrameReady -= callback;
            }
        }

        /// <inheritdoc/>
        public void UnregisterCallback(Argb32VideoFrameDelegate callback)
        {
            if (Track != null)
            {
                Track.Argb32VideoFrameReady -= callback;
            }
        }

        #endregion


        #region IMediaReceiver interface

        /// <inheritdoc/>
        MediaKind IMediaReceiver.MediaKind => MediaKind.Video;

        /// <inheritdoc/>
        bool IMediaReceiver.IsLive => _isLive;

        /// <inheritdoc/>
        Transceiver IMediaReceiver.Transceiver => _transceiver;

        #endregion


        private bool _isLive = false;
        private Transceiver _transceiver = null;


        #region IMediaReceiverInternal interface

        /// <inheritdoc/>
        void IMediaReceiverInternal.OnPaired(MediaTrack track)
        {
            var remoteVideoTrack = (RemoteVideoTrack)track;

            // Enqueue invoking from the main Unity app thread, both to avoid locks on public
            // properties and so that listeners of the event can directly access Unity objects
            // from their handler function.
            InvokeOnAppThread(() =>
            {
                Debug.Assert(Track == null);
                Track = remoteVideoTrack;
                _isLive = true;
                VideoStreamStarted.Invoke(this);
                IsStreaming = true;
            });
        }

        /// <inheritdoc/>
        void IMediaReceiverInternal.OnUnpaired(MediaTrack track)
        {
            Debug.Assert(track is RemoteVideoTrack);

            // Enqueue invoking from the main Unity app thread, both to avoid locks on public
            // properties and so that listeners of the event can directly access Unity objects
            // from their handler function.
            InvokeOnAppThread(() =>
            {
                Debug.Assert(Track == track);
                Track = null;
                IsStreaming = false;
                _isLive = false;
                VideoStreamStopped.Invoke(this);
            });
        }

        /// <inheritdoc/>
        void IMediaReceiverInternal.AttachToTransceiver(Transceiver transceiver)
        {
            Debug.Assert((_transceiver == null) || (_transceiver == transceiver));
            _transceiver = transceiver;
        }

        /// <inheritdoc/>
        void IMediaReceiverInternal.DetachFromTransceiver(Transceiver transceiver)
        {
            Debug.Assert((_transceiver == null) || (_transceiver == transceiver));
            _transceiver = null;
        }

        #endregion
    }
}
