// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#pragma once

#include "interop_api.h"

extern "C" {

/// Assign some opaque user data to the data channel. The implementation will
/// store the pointer in the data channel object and not touch it. It can be
/// retrieved with |mrsDataChannelGetUserData()| at any point during the data
/// channel lifetime. This is not multithread-safe.
MRS_API void MRS_CALL mrsDataChannelSetUserData(mrsDataChannelHandle handle,
                                                void* user_data) noexcept;

/// Get the opaque user data pointer previously assigned to the data channel
/// with |mrsDataChannelSetUserData()|. If no value was previously assigned,
/// return |nullptr|. This is not multithread-safe.
MRS_API void* MRS_CALL
mrsDataChannelGetUserData(mrsDataChannelHandle handle) noexcept;

/// Callback fired when a message is received on a data channel.
using mrsDataChannelMessageCallback = void(MRS_CALL*)(void* user_data,
                                                      const void* data,
                                                      const uint64_t size);

/// Callback fired when a data channel buffering changes.
/// The |previous| and |current| values are the old and new sizes in byte of the
/// buffering buffer. The |limit| is the capacity of the buffer.
/// Note that when the buffer is full, any attempt to send data will result is
/// an abrupt closing of the data channel. So monitoring this state is critical.
using mrsDataChannelBufferingCallback = void(MRS_CALL*)(void* user_data,
                                                        const uint64_t previous,
                                                        const uint64_t current,
                                                        const uint64_t limit);

/// Callback fired when the state of a data channel changed.
using mrsDataChannelStateCallback = void(MRS_CALL*)(void* user_data,
                                                    int32_t state,
                                                    int32_t id);

/// Helper to register a group of data channel callbacks.
struct mrsDataChannelCallbacks {
  mrsDataChannelMessageCallback message_callback{};
  void* message_user_data{};
  mrsDataChannelBufferingCallback buffering_callback{};
  void* buffering_user_data{};
  mrsDataChannelStateCallback state_callback{};
  void* state_user_data{};
};

/// Register callbacks for managing a data channel
MRS_API void MRS_CALL mrsDataChannelRegisterCallbacks(
    mrsDataChannelHandle handle,
    const mrsDataChannelCallbacks* callbacks) noexcept;

MRS_API mrsResult MRS_CALL
mrsDataChannelSendMessage(mrsDataChannelHandle data_channel_handle,
                          const void* data,
                          uint64_t size) noexcept;

}  // extern "C"
