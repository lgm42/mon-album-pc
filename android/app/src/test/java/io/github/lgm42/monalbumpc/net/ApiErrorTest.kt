package io.github.lgm42.monalbumpc.net

import org.junit.Assert.assertEquals
import org.junit.Test

class ApiErrorTest {

    @Test
    fun `maps protocol error codes`() {
        assertEquals(ApiError.PairingInvalid, ApiError.fromCode(400, "pairing_invalid"))
        assertEquals(ApiError.PairingExpired, ApiError.fromCode(410, "pairing_expired"))
        assertEquals(ApiError.DeviceUnknown, ApiError.fromCode(401, "device_unknown"))
        assertEquals(ApiError.DestinationUnavailable, ApiError.fromCode(503, "destination_unavailable"))
        assertEquals(ApiError.DiskFull, ApiError.fromCode(507, "disk_full"))
    }

    @Test
    fun `unknown codes keep the http status`() {
        assertEquals(ApiError.Http(500, null), ApiError.fromCode(500, null))
        assertEquals(ApiError.Http(418, "teapot"), ApiError.fromCode(418, "teapot"))
    }

    @Test
    fun `ipv6 hosts are bracketed in urls`() {
        assertEquals("192.168.1.20", PcApi.urlHost("192.168.1.20"))
        assertEquals("[fe80::1]", PcApi.urlHost("fe80::1"))
        assertEquals("[fe80::1]", PcApi.urlHost("[fe80::1]"))
    }
}
