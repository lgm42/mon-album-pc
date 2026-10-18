package io.github.lgm42.monalbumpc.pairing

/** Where and how to pair: read from the PC's QR code / deep link, or built from mDNS discovery. */
data class PairingOffer(
    val host: String,
    val apiPort: Int,
    val token: String?,
    val fingerprint: String?,
    val pcName: String?,
)
