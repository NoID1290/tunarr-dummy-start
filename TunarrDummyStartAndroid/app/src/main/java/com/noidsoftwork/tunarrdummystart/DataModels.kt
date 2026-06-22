package com.noidsoftwork.tunarrdummystart

import java.io.Serializable

data class AppConfig(
    var baseUrl: String = "",
    var channelCount: Int = 0,
    var startupDelaySeconds: Int = 0,
    var staggerDelayMs: Int = 0,
    var retryCount: Int = 0,
    var retryDelayMs: Int = 1500,
    var ffmpegPath: String = "",
    var autoStartOnLaunch: Boolean = false,
    var startWithWindows: Boolean = false,
    var hwAccel: String = "None",
    var threadsPerProcess: Int = 0,
    var enableWebserver: Boolean = true,
    var webserverPort: Int = 1290,
    var webserverPassword: String = "",
    var tunarrUseService: Boolean = false,
    var tunarrServiceName: String = "Tunarr",
    var tunarrExePath: String = "",
    var waitForTunarr: Boolean = false,
    var channels: List<ChannelConfig> = emptyList()
) : Serializable

data class ChannelConfig(
    var channelId: Int = 0,
    var url: String = "",
    var enabled: Boolean = true,
    var retryCount: Int? = null
) : Serializable

enum class ChannelRunState {
    Idle,
    Connecting,
    Connected,
    Retrying,
    StartFailed,
    UnexpectedExit,
    Canceled,
    Stopped,
    Disabled
}

data class ChannelStatusUpdate(
    val channel: Int,
    val state: String, // String representation of ChannelRunState
    val attempt: Int,
    val maxAttempts: Int,
    val message: String,
    val exitCode: Int?,
    val connectionEstablished: Boolean,
    val runDuration: String?, // C# TimeSpan can be serialized as "00:00:00" or similar
    val updatedAt: String
)

data class ServerStatusResponse(
    val isRunning: Boolean,
    val channels: List<ChannelStatusUpdate>
)

data class ApiResponse(
    val success: Boolean,
    val error: String? = null
)
