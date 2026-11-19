package io.github.lgm42.monalbumpc

import io.github.lgm42.monalbumpc.ui.navigation.TopLevelDestination
import org.junit.Assert.assertEquals
import org.junit.Test

class TopLevelDestinationTest {

    @Test
    fun `bottom bar has four tabs with distinct routes, Sauvegarde first then Photos`() {
        val routes = TopLevelDestination.entries.map { it.route }
        assertEquals(4, routes.toSet().size)
        assertEquals(TopLevelDestination.Backup, TopLevelDestination.entries.first())
        assertEquals(TopLevelDestination.Photos, TopLevelDestination.entries[1])
    }
}
