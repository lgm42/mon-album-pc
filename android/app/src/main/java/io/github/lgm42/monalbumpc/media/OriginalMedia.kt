package io.github.lgm42.monalbumpc.media

import android.Manifest
import android.content.Context
import android.content.pm.PackageManager
import android.net.Uri
import android.provider.MediaStore
import androidx.core.content.ContextCompat
import java.io.FileNotFoundException
import java.io.IOException
import java.io.InputStream

/**
 * Opens the bytes of a media exactly as they are on the phone. Without ACCESS_MEDIA_LOCATION, Android
 * (10+) hands out a copy whose GPS metadata is blanked: same size, other bytes, hence another SHA-256.
 * Every read of a media's bytes (hash, upload) goes through here.
 */
object OriginalMedia {

    fun isLocationGranted(context: Context): Boolean =
        ContextCompat.checkSelfPermission(context, Manifest.permission.ACCESS_MEDIA_LOCATION) ==
            PackageManager.PERMISSION_GRANTED

    /** The original bytes when allowed, else what MediaStore gives (location removed). */
    @Throws(IOException::class)
    fun open(context: Context, uri: Uri): InputStream {
        val resolver = context.contentResolver
        if (isLocationGranted(context)) {
            try {
                resolver.openInputStream(MediaStore.setRequireOriginal(uri))?.let { return it }
            } catch (e: UnsupportedOperationException) {
                // Not a MediaStore item, or the permission was just withdrawn: plain read below.
            } catch (e: SecurityException) {
                // Same.
            } catch (e: FileNotFoundException) {
                // Some providers reject the "requireOriginal" parameter: plain read below.
            }
        }
        return resolver.openInputStream(uri) ?: throw IOException("Cannot open $uri")
    }
}
