package io.github.lgm42.monalbumpc.pairing

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class FingerprintsTest {

    @Test
    fun `fingerprint is base64url sha256 without padding`() {
        assertEquals("ungWv48Bz-pBQUDeXa4iI7ADYaOWF3qctBD_YfIAFa0", Fingerprints.of("abc".toByteArray()))
        assertEquals("47DEQpj8HBSa-_TImW-5JCeuQeRkm5NMpJWZG3hSuFU", Fingerprints.of(ByteArray(0)))
    }

    @Test
    fun `matches tolerates padding and rejects null or different values`() {
        val fp = Fingerprints.of("abc".toByteArray())
        assertTrue(Fingerprints.matches(fp, "$fp="))
        assertFalse(Fingerprints.matches(fp, null))
        assertFalse(Fingerprints.matches(fp, Fingerprints.of(ByteArray(0))))
    }
}
