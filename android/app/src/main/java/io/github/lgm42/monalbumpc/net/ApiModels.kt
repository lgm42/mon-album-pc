package io.github.lgm42.monalbumpc.net

import kotlinx.serialization.Serializable

// JSON bodies of protocol §3.3, §5.1 and §6.

@Serializable
data class DeviceInfo(
    val name: String,
    val model: String,
    val androidVersion: String,
    val appVersion: String,
    val appVersionCode: Int,
)

@Serializable
data class PairRequest(
    val token: String? = null,
    val code: String? = null,
    val device: DeviceInfo,
)

@Serializable
data class PairResponse(
    val deviceId: String,
    val secret: String,
    val pcId: String,
    val pcName: String,
    val albumName: String,
    val certSha256: String,
)

@Serializable
data class ApkInfo(
    val versionCode: Int = 0,
    val versionName: String = "",
    val sha256: String = "",
    val size: Long = 0,
)

@Serializable
data class StatusResponse(
    val protocol: Int = 1,
    val pcName: String = "",
    val albumName: String = "",
    val destinationAvailable: Boolean = true,
    val busy: String? = null,
    val apk: ApkInfo? = null,
    val serverTime: String? = null,
)

@Serializable
data class ErrorEnvelope(val error: ErrorBody? = null)

@Serializable
data class ErrorBody(val code: String = "", val message: String? = null)
