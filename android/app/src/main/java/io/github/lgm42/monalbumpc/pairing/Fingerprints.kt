package io.github.lgm42.monalbumpc.pairing

import java.security.MessageDigest
import java.util.Base64

/** Certificate fingerprint as used by the protocol: base64url (no padding) of SHA-256 of the DER bytes. */
object Fingerprints {
    fun of(derBytes: ByteArray): String {
        val digest = MessageDigest.getInstance("SHA-256").digest(derBytes)
        return Base64.getUrlEncoder().withoutPadding().encodeToString(digest)
    }

    /** Compares two fingerprints, tolerating base64 padding, in constant time. */
    fun matches(a: String?, b: String?): Boolean {
        if (a == null || b == null) return false
        val left = a.trim().trimEnd('=').toByteArray(Charsets.US_ASCII)
        val right = b.trim().trimEnd('=').toByteArray(Charsets.US_ASCII)
        return MessageDigest.isEqual(left, right)
    }
}
