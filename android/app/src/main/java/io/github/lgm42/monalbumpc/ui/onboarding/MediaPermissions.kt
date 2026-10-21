package io.github.lgm42.monalbumpc.ui.onboarding

import android.Manifest
import android.content.Context
import android.content.pm.PackageManager
import android.os.Build
import androidx.core.content.ContextCompat

enum class PhotoAccess { Full, Partial, Denied }

/** Runtime permissions of the onboarding, depending on the Android version. */
object MediaPermissions {

    /**
     * Permissions to request for the photos step. ACCESS_MEDIA_LOCATION comes with them (same dialog):
     * without it Android removes the GPS position from the photos the app reads.
     */
    fun photoPermissions(): Array<String> = mediaPermissions() + Manifest.permission.ACCESS_MEDIA_LOCATION

    private fun mediaPermissions(): Array<String> = when {
        Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE -> arrayOf(
            Manifest.permission.READ_MEDIA_IMAGES,
            Manifest.permission.READ_MEDIA_VIDEO,
            Manifest.permission.READ_MEDIA_VISUAL_USER_SELECTED,
        )
        Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU -> arrayOf(
            Manifest.permission.READ_MEDIA_IMAGES,
            Manifest.permission.READ_MEDIA_VIDEO,
        )
        else -> arrayOf(Manifest.permission.READ_EXTERNAL_STORAGE)
    }

    fun photoAccess(context: Context): PhotoAccess {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            val images = isGranted(context, Manifest.permission.READ_MEDIA_IMAGES)
            val videos = isGranted(context, Manifest.permission.READ_MEDIA_VIDEO)
            if (images && videos) return PhotoAccess.Full
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE &&
                isGranted(context, Manifest.permission.READ_MEDIA_VISUAL_USER_SELECTED)
            ) {
                return PhotoAccess.Partial
            }
            return if (images || videos) PhotoAccess.Partial else PhotoAccess.Denied
        }
        return if (isGranted(context, Manifest.permission.READ_EXTERNAL_STORAGE)) PhotoAccess.Full else PhotoAccess.Denied
    }

    /** POST_NOTIFICATIONS only exists from Android 13. */
    fun needsNotificationPermission(context: Context): Boolean =
        Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU &&
            !isGranted(context, Manifest.permission.POST_NOTIFICATIONS)

    fun isGranted(context: Context, permission: String): Boolean =
        ContextCompat.checkSelfPermission(context, permission) == PackageManager.PERMISSION_GRANTED
}
