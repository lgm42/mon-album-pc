package io.github.lgm42.monalbumpc

import io.github.lgm42.monalbumpc.ui.navigation.TopLevelDestination
import org.junit.Assert.assertEquals
import org.junit.Test

class TopLevelDestinationTest {

    @Test
    fun `bottom bar has three tabs with distinct routes and Photos first`() {
        val routes = TopLevelDestination.entries.map { it.route }
        assertEquals(3, routes.toSet().size)
        assertEquals(TopLevelDestination.Photos, TopLevelDestination.entries.first())
    }
}
