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

// Backup bodies of protocol §5.2 and §5.3.

@Serializable
data class CheckItem(val sha256: String, val size: Long)

@Serializable
data class CheckRequest(val items: List<CheckItem>)

@Serializable
data class CheckResponse(val have: List<String> = emptyList())

@Serializable
data class UploadRequest(
    val sha256: String,
    val size: Long,
    val name: String,
    val sourceFolder: String? = null,
    val mime: String? = null,
    val takenAt: String? = null,
    val modifiedAt: String? = null,
)

/**
 * Answer to POST /uploads: `exists`, `upload` (with [uploadId] and [offset]), or directly
 * `saved` when the PC already had every byte of an interrupted upload.
 */
@Serializable
data class UploadResponse(
    val status: String? = null,
    val uploadId: String? = null,
    val offset: Long? = null,
)

/** Answer to PATCH /uploads/{id}: the new offset, and `saved`/`exists` once complete. */
@Serializable
data class OffsetResponse(
    val offset: Long = 0,
    val status: String? = null,
)
