package io.github.lgm42.monalbumpc.backup

import io.github.lgm42.monalbumpc.R
import java.io.File
import java.util.Locale
import javax.xml.parsers.DocumentBuilderFactory
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import org.w3c.dom.Element

/**
 * Every notification must be readable collapsed, without expanding it: title and text on one line each on a ~360 dp
 * phone, whatever the file name, server name or numbers. Checked against the real French strings.
 */
class NotificationTextsTest {

    /** The app's strings.xml, looked up by the ids of R like the app does. */
    private object XmlTexts : TextSource {
        private val strings = HashMap<String, String>()
        private val plurals = HashMap<String, Map<String, String>>()

        init {
            val file = listOf("src/main/res/values/strings.xml", "app/src/main/res/values/strings.xml")
                .map(::File).first { it.exists() }
            val document = DocumentBuilderFactory.newInstance().newDocumentBuilder().parse(file)
            val stringNodes = document.getElementsByTagName("string")
            for (i in 0 until stringNodes.length) {
                val element = stringNodes.item(i) as Element
                strings[element.getAttribute("name")] = unescape(element.textContent)
            }
            val pluralNodes = document.getElementsByTagName("plurals")
            for (i in 0 until pluralNodes.length) {
                val element = pluralNodes.item(i) as Element
                val items = element.getElementsByTagName("item")
                plurals[element.getAttribute("name")] = (0 until items.length).associate { j ->
                    val item = items.item(j) as Element
                    item.getAttribute("quantity") to unescape(item.textContent)
                }
            }
        }

        private fun unescape(value: String): String {
            val trimmed = value.trim()
            val unquoted = if (trimmed.length >= 2 && trimmed.startsWith("\"") && trimmed.endsWith("\"")) {
                trimmed.substring(1, trimmed.length - 1)
            } else {
                trimmed
            }
            return unquoted.replace("\\'", "'").replace("\\\"", "\"").replace("\\n", "\n")
        }

        private fun nameOf(type: Class<*>, id: Int): String =
            type.fields.first { it.getInt(null) == id }.name

        override fun string(id: Int, vararg args: Any): String {
            val template = strings.getValue(nameOf(R.string::class.java, id))
            return if (args.isEmpty()) template else String.format(Locale.FRENCH, template, *args)
        }

        override fun plural(id: Int, count: Int, vararg args: Any): String {
            val items = plurals.getValue(nameOf(R.plurals::class.java, id))
            // French: 0 and 1 are "one".
            val template = items[if (count <= 1) "one" else "other"] ?: items.getValue("other")
            return String.format(Locale.FRENCH, template, *args)
        }

        /** Like Formatter.formatShortFileSize in French: "45 Mo", "1,2 Go". */
        override fun size(bytes: Long): String {
            val units = listOf("o", "ko", "Mo", "Go", "To")
            var value = bytes.toDouble()
            var unit = 0
            while (value >= 900 && unit < units.lastIndex) {
                value /= 1000
                unit++
            }
            val number = if (value < 10 && unit > 0) String.format(Locale.FRENCH, "%.1f", value) else String.format(Locale.FRENCH, "%.0f", value)
            return "$number ${units[unit]}"
        }
    }

    private val longFile = "IMG-20231224-WA0007-Joyeux-Noel-chez-Mamie-et-Papy-avec-toute-la-famille-reunie-version-finale-2.mp4"
    private val longServer = "NAS-DU-GRENIER-DE-LA-MAISON-DE-CAMPAGNE-SYNOLOGY-DS920PLUS"
    private val servers = listOf("NAS", "PC-SALON", longServer)
    private val files = listOf("IMG_1234.jpg", "VID_20261119_181500.mp4", longFile)
    private val counts = listOf(0 to 1, 3 to 20, 99_998 to 99_999)

    private fun assertFits(words: NotificationText, case: String) {
        assertTrue("title too long ($case): ${words.title}", words.title.length <= NotificationTexts.TITLE_MAX)
        words.text?.let { assertTrue("text too long ($case): $it", it.length <= NotificationTexts.TEXT_MAX) }
        words.subText?.let { assertTrue("header too long ($case): $it", it.length <= NotificationTexts.SUB_MAX) }
        assertTrue("empty title ($case)", words.title.isNotBlank())
    }

    @Test
    fun `progress notifications fit collapsed, whatever the file, the server or the numbers`() {
        val sizes = listOf(0L to 1L, 45_000_000L to 120_000_000L, 999_000_000_000L to 999_999_000_000L)
        for (server in servers) for (file in files) for ((done, total) in counts) for ((bytes, size) in sizes) {
            val run = RunTotals(done, total, bytes, size * 3, 2_100_000)
            for (phase in BackupPhase.entries) {
                val activity = RunActivity(phase, file, bytes, size, 2_100_000)
                assertFits(NotificationTexts.progress(XmlTexts, done, total, server, activity, run), "$phase $server $file $done/$total")
            }
            assertFits(NotificationTexts.progress(XmlTexts, done, total, server, null, null), "no activity $server")
            assertFits(
                NotificationTexts.progress(XmlTexts, done, total, server, RunActivity(BackupPhase.UPLOADING), null),
                "no file $server",
            )
        }
    }

    @Test
    fun `the progress puts the count first, the file name and its bytes next`() {
        val activity = RunActivity(BackupPhase.UPLOADING, "IMG_1234.jpg", 45_000_000, 120_000_000, 2_100_000)
        val words = NotificationTexts.progress(XmlTexts, 3, 20, "NAS", activity, RunTotals(3, 20, 1, 2, 0))
        assertEquals("Sauvegarde · 4 / 20", words.title)
        assertEquals("IMG_1234.jpg · 45 Mo / 120 Mo", words.text)
        assertEquals("NAS", words.subText)
        // The details only when expanded.
        assertTrue(words.expanded!!.contains("2,1 Mo/s"))
        assertTrue(words.expanded!!.contains("Vers NAS"))

        val long = NotificationTexts.progress(XmlTexts, 3, 20, longServer, activity.copy(fileName = longFile), null)
        // Shortened in the middle: the start of the name and its extension stay.
        assertTrue(long.text!!.startsWith("IMG-2023"))
        assertTrue(long.text!!.contains("….mp4") || long.text!!.contains(".mp4 · "))
        assertTrue(long.text!!.endsWith("45 Mo / 120 Mo"))
        assertTrue(long.expanded!!.contains(longFile))
    }

    @Test
    fun `end of backup, errors and update fit collapsed`() {
        val numbers = listOf(0, 1, 2, 999, 99_999)
        for (server in servers) {
            for (photos in numbers) for (videos in numbers) for (left in numbers) {
                if (photos + videos == 0) continue
                assertFits(NotificationTexts.done(XmlTexts, photos, videos, left, 0, server), "done $photos/$videos/$left")
                assertFits(NotificationTexts.done(XmlTexts, photos, videos, 0, left, server), "done failed $left")
            }
            for (issue in BackupIssue.entries) {
                NotificationTexts.issue(XmlTexts, issue, server)?.let { assertFits(it, "$issue $server") }
            }
        }
        for (version in listOf("1.5.0", "0.1.12345", "1.5.0-beta.12+build.20261119.abcdef0")) {
            assertFits(NotificationTexts.update(XmlTexts, version), "update $version")
        }
    }

    @Test
    fun `end of backup says what is left first, then what was sent`() {
        val all = NotificationTexts.done(XmlTexts, 52, 3, 0, 0, "NAS")
        assertEquals("Tout est sauvegardé", all.title)
        assertEquals("52 photos et 3 vidéos envoyées", all.text)
        assertEquals("12 fichiers en attente", NotificationTexts.done(XmlTexts, 1, 0, 12, 2, "NAS").title)
        assertEquals("1 photo envoyée", NotificationTexts.done(XmlTexts, 1, 0, 12, 2, "NAS").text)
        assertEquals("2 fichiers en échec", NotificationTexts.done(XmlTexts, 1, 0, 0, 2, "NAS").title)
        assertEquals("Disque de NAS plein", NotificationTexts.issue(XmlTexts, BackupIssue.DISK_FULL, "NAS")!!.title)
    }

    @Test
    fun `names are shortened in the middle, keeping the extension`() {
        val short = ShortText.middle("IMG-20230812-WA0007.mp4", 20)
        assertEquals(20, short.length)
        assertTrue(short.startsWith("IMG-2023"))
        assertTrue(short.endsWith("0007.mp4"))
        assertTrue(short.contains(ShortText.ELLIPSIS))
        assertEquals("IMG_1.jpg", ShortText.middle("IMG_1.jpg", 20))
        assertEquals(1, ShortText.middle("abcdef", 1).length)
        val fitted = ShortText.fit(25, "NAS-DU-GRENIER-DE-CAMPAGNE") { "Disque de $it plein" }
        assertEquals(25, fitted.length)
        assertTrue(fitted.startsWith("Disque de NAS-") && fitted.endsWith(" plein"))
    }
}
