package io.github.lgm42.monalbumpc.media

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class FolderRulesTest {

    private val primary = FolderRules.PRIMARY_VOLUME

    private fun check(path: String, section: FolderSection, name: String, volume: String = primary) {
        val result = FolderRules.classify(path, volume)
        assertEquals("section of $path", section, result.section)
        assertEquals("name of $path", name, result.friendlyName)
    }

    @Test
    fun `camera folders are recommended`() {
        check("DCIM/Camera/", FolderSection.Recommended, "Appareil photo")
        check("DCIM/", FolderSection.Recommended, "Appareil photo")
        check("DCIM/100ANDRO/", FolderSection.Recommended, "Appareil photo")
        check("DCIM/OpenCamera/", FolderSection.Recommended, "Appareil photo")
        check("dcim/camera", FolderSection.Recommended, "Appareil photo")
    }

    @Test
    fun `screenshots are recommended`() {
        check("Pictures/Screenshots/", FolderSection.Recommended, "Captures d'écran")
        check("DCIM/Screenshots/", FolderSection.Recommended, "Captures d'écran")
    }

    @Test
    fun `whatsapp received media are recommended, sent and private are not`() {
        val base = "Android/media/com.whatsapp/WhatsApp/Media/"
        check(base + "WhatsApp Images/", FolderSection.Recommended, "WhatsApp Images")
        check(base + "WhatsApp Video/", FolderSection.Recommended, "WhatsApp Video")
        check(base + "WhatsApp Images/Sent/", FolderSection.OtherApps, "Photos envoyées WhatsApp")
        check(base + "WhatsApp Video/Sent/", FolderSection.OtherApps, "Vidéos envoyées WhatsApp")
        check(base + "WhatsApp Images/Private/", FolderSection.OtherApps, "WhatsApp privé")
        check(base + ".Statuses/", FolderSection.OtherApps, "Statuts WhatsApp")
        check(base + "WhatsApp Animated Gifs/", FolderSection.OtherApps, "WhatsApp Animated Gifs")
        // Older installs.
        check("WhatsApp/Media/WhatsApp Images/", FolderSection.Recommended, "WhatsApp Images")
        check("WhatsApp/Media/WhatsApp Images/Sent/", FolderSection.OtherApps, "Photos envoyées WhatsApp")
    }

    @Test
    fun `other apps`() {
        check("Pictures/Snapchat/", FolderSection.OtherApps, "Snapchat")
        check("Snapchat/", FolderSection.OtherApps, "Snapchat")
        check("Pictures/Telegram/", FolderSection.OtherApps, "Telegram")
        check("Android/media/org.telegram.messenger/Telegram/Telegram Images/", FolderSection.OtherApps, "Telegram")
        check("Pictures/Instagram/", FolderSection.OtherApps, "Instagram")
        check("DCIM/Instagram/", FolderSection.OtherApps, "Instagram")
        check("Android/media/com.example.app/Pictures/", FolderSection.OtherApps, "Pictures")
        check("Android/media/com.example.app/", FolderSection.OtherApps, "com.example.app")
        check("Android/media/com.whatsapp.w4b/WhatsApp Business/Media/", FolderSection.OtherApps, "WhatsApp Business")
    }

    @Test
    fun `other folders`() {
        check("Download/", FolderSection.OtherFolders, "Téléchargements")
        check("Bluetooth/", FolderSection.OtherFolders, "Bluetooth")
        check("Download/Bluetooth/", FolderSection.OtherFolders, "Bluetooth")
        check("Pictures/", FolderSection.OtherFolders, "Images")
        check("Movies/", FolderSection.OtherFolders, "Vidéos")
        check("Pictures/Vacances 2026/", FolderSection.OtherFolders, "Vacances 2026")
        check("DCIM/Restored/", FolderSection.OtherFolders, "Restored")
    }

    @Test
    fun `sd card volume is flagged`() {
        val sd = FolderRules.classify("DCIM/Camera/", "1a2b-3c4d")
        assertTrue(sd.onSdCard)
        assertEquals(FolderSection.Recommended, sd.section)
        assertFalse(FolderRules.classify("DCIM/Camera/", primary).onSdCard)
    }

    @Test
    fun `keys normalize the path`() {
        assertEquals("external_primary|DCIM/Camera/", FolderRules.key(primary, "DCIM/Camera"))
        assertEquals("external_primary|DCIM/Camera/", FolderRules.key(primary, "/DCIM/Camera/"))
    }

    @Test
    fun `default selection keeps only recommended folders`() {
        val selection = FolderRules.defaultSelection(
            listOf(
                "DCIM/Camera/" to primary,
                "Pictures/Screenshots/" to primary,
                "Android/media/com.whatsapp/WhatsApp/Media/WhatsApp Images/Sent/" to primary,
                "Download/" to primary,
                "DCIM/Camera/" to "1a2b-3c4d",
            ),
        )
        assertEquals(
            setOf(
                "external_primary|DCIM/Camera/",
                "external_primary|Pictures/Screenshots/",
                "1a2b-3c4d|DCIM/Camera/",
            ),
            selection,
        )
    }

    @Test
    fun `folders are grouped from media`() {
        fun media(id: Long, path: String, date: Long) = DeviceMedia(
            id = id, uri = "content://media/$id", volume = primary, relativePath = path, displayName = "f$id.jpg",
            size = 10, dateModifiedMs = date, dateTakenMs = null, mime = "image/jpeg", isVideo = false, durationMs = 0,
        )
        val folders = DeviceFolders.fromMedia(
            listOf(media(1, "Download/", 5), media(2, "DCIM/Camera/", 1), media(3, "DCIM/Camera/", 9)),
        )
        assertEquals(listOf("Appareil photo", "Téléchargements"), folders.map { it.friendlyName })
        assertEquals(2, folders[0].count)
        assertEquals("content://media/3", folders[0].newestUri)
    }
}
