package io.github.lgm42.monalbumpc.media

/** Sections of the Folders screen (design README §3). */
enum class FolderSection { Recommended, OtherApps, OtherFolders }

/** How a folder is presented: friendly name, section, default selection. */
data class FolderClass(
    val section: FolderSection,
    val friendlyName: String,
    val onSdCard: Boolean,
) {
    /** Recommended folders are tracked by default. */
    val trackedByDefault: Boolean
        get() = section == FolderSection.Recommended
}

/**
 * Classifies a media folder from its MediaStore RELATIVE_PATH ("DCIM/Camera/") and VOLUME_NAME.
 * Pure logic, without Android dependencies, so that it can be unit tested.
 */
object FolderRules {

    const val PRIMARY_VOLUME = "external_primary"

    // Friendly names (shown to the user, French).
    const val CAMERA = "Appareil photo"
    const val SCREENSHOTS = "Captures d'écran"
    const val SCREEN_RECORDINGS = "Enregistrements d'écran"
    const val WHATSAPP_IMAGES = "WhatsApp Images"
    const val WHATSAPP_VIDEO = "WhatsApp Video"
    const val WHATSAPP_SENT_IMAGES = "Photos envoyées WhatsApp"
    const val WHATSAPP_SENT_VIDEO = "Vidéos envoyées WhatsApp"
    const val WHATSAPP_PRIVATE = "WhatsApp privé"
    const val WHATSAPP_STATUSES = "Statuts WhatsApp"
    const val WHATSAPP_OTHER = "WhatsApp"
    const val DOWNLOADS = "Téléchargements"
    const val BLUETOOTH = "Bluetooth"
    const val PICTURES = "Images"
    const val MOVIES = "Vidéos"

    private val dcfFolder = Regex("^[1-9]\\d{2}[A-Za-z0-9_]{5}$")

    private val knownApps = listOf(
        "snapchat" to "Snapchat",
        "telegram" to "Telegram",
        "instagram" to "Instagram",
        "messenger" to "Messenger",
        "facebook" to "Facebook",
        "signal" to "Signal",
        "tiktok" to "TikTok",
        "twitter" to "X (Twitter)",
        "viber" to "Viber",
    )

    private val knownPackages = mapOf(
        "com.snapchat.android" to "Snapchat",
        "org.telegram.messenger" to "Telegram",
        "org.telegram.plus" to "Telegram",
        "com.instagram.android" to "Instagram",
        "com.facebook.orca" to "Messenger",
        "com.facebook.katana" to "Facebook",
        "org.thoughtcrime.securesms" to "Signal",
        "com.zhiliaoapp.musically" to "TikTok",
        "com.twitter.android" to "X (Twitter)",
        "com.viber.voip" to "Viber",
        "com.whatsapp.w4b" to "WhatsApp Business",
    )

    /** Stable key of a folder: volume + relative path. */
    fun key(volumeName: String, relativePath: String): String = "$volumeName|${normalize(relativePath)}"

    /** Relative paths always end with a single '/', without a leading one. */
    fun normalize(relativePath: String): String {
        val trimmed = relativePath.trim().trim('/')
        return if (trimmed.isEmpty()) "" else "$trimmed/"
    }

    fun classify(relativePath: String, volumeName: String): FolderClass {
        val onSd = volumeName.isNotEmpty() && volumeName != PRIMARY_VOLUME && volumeName != "external"
        val segments = normalize(relativePath).split('/').filter { it.isNotEmpty() }
        val (section, name) = classifySegments(segments)
        return FolderClass(section, name, onSd)
    }

    private fun classifySegments(segments: List<String>): Pair<FolderSection, String> {
        if (segments.isEmpty()) return FolderSection.OtherFolders to PICTURES
        val lower = segments.map { it.lowercase() }
        val last = segments.last()

        whatsApp(segments, lower)?.let { return it }

        // Android/media/<package>/…: files of other apps.
        if (lower.size >= 3 && lower[0] == "android" && lower[1] == "media") {
            val pkg = lower[2]
            val appName = knownPackages[pkg] ?: appFromSegments(lower)
            return FolderSection.OtherApps to (appName ?: if (segments.size > 3) last else segments[2])
        }

        // Camera: DCIM/, DCIM/Camera/, DCF folders such as DCIM/100ANDRO/, DCIM/OpenCamera/.
        if (lower[0] == "dcim") {
            if (segments.size == 1) return FolderSection.Recommended to CAMERA
            if (segments.size == 2) {
                val sub = lower[1]
                if (sub == "camera" || sub == "opencamera" || dcfFolder.matches(segments[1])) {
                    return FolderSection.Recommended to CAMERA
                }
                if (sub == "screenshots") return FolderSection.Recommended to SCREENSHOTS
                if (sub == "screen recordings" || sub == "screenrecorder") {
                    return FolderSection.OtherFolders to SCREEN_RECORDINGS
                }
            }
        }

        if (lower.size == 2 && lower[0] == "pictures" && lower[1] == "screenshots") {
            return FolderSection.Recommended to SCREENSHOTS
        }
        if (lower.size == 2 && lower[0] == "movies" && lower[1] == "screenrecords") {
            return FolderSection.OtherFolders to SCREEN_RECORDINGS
        }

        appFromSegments(lower)?.let { return FolderSection.OtherApps to it }

        return FolderSection.OtherFolders to when {
            lower.contains("bluetooth") -> BLUETOOTH
            lower.size == 1 && (lower[0] == "download" || lower[0] == "downloads") -> DOWNLOADS
            lower.size == 1 && lower[0] == "pictures" -> PICTURES
            lower.size == 1 && lower[0] == "movies" -> MOVIES
            else -> last
        }
    }

    private fun appFromSegments(lower: List<String>): String? {
        for (segment in lower) {
            for ((needle, name) in knownApps) {
                if (segment.contains(needle)) return name
            }
        }
        return null
    }

    /**
     * WhatsApp media live under "Android/media/com.whatsapp/WhatsApp/Media/" (Android 11+)
     * or "WhatsApp/Media/" (older installs).
     */
    private fun whatsApp(segments: List<String>, lower: List<String>): Pair<FolderSection, String>? {
        val mediaIndex = when {
            lower.size >= 5 && lower[0] == "android" && lower[1] == "media" && lower[2] == "com.whatsapp" &&
                lower[3] == "whatsapp" && lower[4] == "media" -> 5
            lower.size >= 2 && lower[0] == "whatsapp" && lower[1] == "media" -> 2
            else -> return null
        }
        val rest = lower.drop(mediaIndex)
        if (rest.isEmpty()) return FolderSection.OtherApps to WHATSAPP_OTHER
        val folder = rest[0]
        val sub = rest.getOrNull(1)
        return when {
            folder == ".statuses" -> FolderSection.OtherApps to WHATSAPP_STATUSES
            folder == "whatsapp images" -> when (sub) {
                null -> FolderSection.Recommended to WHATSAPP_IMAGES
                "sent" -> FolderSection.OtherApps to WHATSAPP_SENT_IMAGES
                "private" -> FolderSection.OtherApps to WHATSAPP_PRIVATE
                else -> FolderSection.OtherApps to segments.last()
            }
            folder == "whatsapp video" -> when (sub) {
                null -> FolderSection.Recommended to WHATSAPP_VIDEO
                "sent" -> FolderSection.OtherApps to WHATSAPP_SENT_VIDEO
                "private" -> FolderSection.OtherApps to WHATSAPP_PRIVATE
                else -> FolderSection.OtherApps to segments.last()
            }
            else -> FolderSection.OtherApps to segments[mediaIndex]
        }
    }

    /** Folders selected the first time, before the user changes anything. */
    fun defaultSelection(folders: Collection<Pair<String, String>>): Set<String> =
        folders.filter { (path, volume) -> classify(path, volume).trackedByDefault }
            .map { (path, volume) -> key(volume, path) }
            .toSet()
}
