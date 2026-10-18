package io.github.lgm42.monalbumpc.pairing

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class PairingLinkTest {

    @Test
    fun `parses the QR code url`() {
        val offer = PairingLink.parse(
            "http://192.168.1.20:48621/p?t=K7Q2ABCD&f=ungWv48Bz-pBQUDeXa4iI7ADYaOWF3qctBD_YfIAFa0&h=192.168.1.20&n=PC-SALON",
        )
        assertEquals(
            PairingOffer(
                host = "192.168.1.20",
                apiPort = 48620,
                token = "K7Q2ABCD",
                fingerprint = "ungWv48Bz-pBQUDeXa4iI7ADYaOWF3qctBD_YfIAFa0",
                pcName = "PC-SALON",
            ),
            offer,
        )
    }

    @Test
    fun `parses the deep link with a custom api port`() {
        val offer = PairingLink.parse("monalbumpc://pair?t=TOKEN&f=FP&h=10.0.0.5&n=Bureau&p=50000")
        assertEquals(PairingOffer("10.0.0.5", 50000, "TOKEN", "FP", "Bureau"), offer)
    }

    @Test
    fun `decodes the url-encoded pc name`() {
        val offer = PairingLink.parse("monalbumpc://pair?t=T&f=F&h=10.0.0.5&n=PC%20de%20Ren%C3%A9+%26+Co")
        assertEquals("PC de René & Co", offer?.pcName)
    }

    @Test
    fun `falls back to the url host when h is missing`() {
        val offer = PairingLink.parse("http://192.168.1.30:48621/p?t=T&f=F")
        assertEquals("192.168.1.30", offer?.host)
        assertNull(offer?.pcName)
    }

    @Test
    fun `deep link without h is rejected`() {
        assertNull(PairingLink.parse("monalbumpc://pair?t=T&f=F"))
    }

    @Test
    fun `rejects links missing the token or the fingerprint`() {
        assertNull(PairingLink.parse("http://192.168.1.30:48621/p?f=F&h=192.168.1.30"))
        assertNull(PairingLink.parse("http://192.168.1.30:48621/p?t=T&h=192.168.1.30"))
        assertNull(PairingLink.parse("http://192.168.1.30:48621/p"))
    }

    @Test
    fun `rejects invalid ports`() {
        assertNull(PairingLink.parse("monalbumpc://pair?t=T&f=F&h=10.0.0.5&p=abc"))
        assertNull(PairingLink.parse("monalbumpc://pair?t=T&f=F&h=10.0.0.5&p=70000"))
    }

    @Test
    fun `rejects unrelated content`() {
        assertNull(PairingLink.parse("https://example.com/?t=T&f=F&h=1.2.3.4"))
        assertNull(PairingLink.parse("monalbumpc://other?t=T&f=F&h=1.2.3.4"))
        assertNull(PairingLink.parse("WIFI:S:maison;T:WPA;P:secret;;"))
        assertNull(PairingLink.parse("just some text"))
        assertNull(PairingLink.parse(""))
    }
}
