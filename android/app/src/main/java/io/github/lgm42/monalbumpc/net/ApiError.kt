package io.github.lgm42.monalbumpc.net

/** Failures of a call to the PC, mapped from the `error.code` field of protocol §6. */
sealed class ApiError {
    data object PairingInvalid : ApiError()
    data object PairingExpired : ApiError()
    data object DeviceUnknown : ApiError()
    data object DestinationUnavailable : ApiError()
    data object DiskFull : ApiError()
    data object BusyUsb : ApiError()
    data object ProtocolUnsupported : ApiError()

    /** PATCH sent at the wrong offset; [expected] comes from the `Upload-Offset` header. */
    data class OffsetMismatch(val expected: Long?) : ApiError()

    /** The received bytes do not match the declared SHA-256; the upload was dropped. */
    data object HashMismatch : ApiError()

    /** Any other HTTP error. */
    data class Http(val status: Int, val code: String?) : ApiError()

    /** The PC could not be reached (no route, timeout, connection refused…). */
    data class Network(val message: String?) : ApiError()

    /** The PC presented a certificate other than the pinned one. */
    data object CertificateMismatch : ApiError()

    /** The PC answered something that is not the expected JSON. */
    data object InvalidResponse : ApiError()

    companion object {
        fun fromCode(status: Int, code: String?): ApiError = when (code) {
            "pairing_invalid" -> PairingInvalid
            "pairing_expired" -> PairingExpired
            "device_unknown" -> DeviceUnknown
            "destination_unavailable" -> DestinationUnavailable
            "disk_full" -> DiskFull
            "busy_usb" -> BusyUsb
            "protocol_unsupported" -> ProtocolUnsupported
            "offset_mismatch" -> OffsetMismatch(null)
            "hash_mismatch" -> HashMismatch
            else -> Http(status, code)
        }
    }
}

class ApiException(val error: ApiError) : Exception(error.toString())
