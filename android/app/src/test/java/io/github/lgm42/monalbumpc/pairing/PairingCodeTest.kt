package io.github.lgm42.monalbumpc.pairing

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class PairingCodeTest {

    @Test
    fun `sanitize keeps at most six digits`() {
        assertEquals("123456", PairingCode.sanitize("123 456"))
        assertEquals("123456", PairingCode.sanitize("1234567"))
        assertEquals("12", PairingCode.sanitize("1a-2"))
        assertEquals("", PairingCode.sanitize(""))
    }

    @Test
    fun `complete only with six digits`() {
        assertTrue(PairingCode.isComplete("012345"))
        assertFalse(PairingCode.isComplete("01234"))
        assertFalse(PairingCode.isComplete("01234a"))
    }

    @Test
    fun `format splits the code in two groups`() {
        assertEquals("123 456", PairingCode.format("123456"))
        assertEquals("123", PairingCode.format("123"))
        assertEquals("1234", PairingCode.format("1234").replace(" ", ""))
    }
}
