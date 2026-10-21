package io.github.lgm42.monalbumpc.ui.components

import android.Manifest
import android.app.Activity
import android.content.ActivityNotFoundException
import android.content.Context
import android.content.ContextWrapper
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.LocationOn
import androidx.compose.material3.Button
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import androidx.core.app.ActivityCompat
import androidx.core.content.edit
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LifecycleEventEffect
import io.github.lgm42.monalbumpc.R
import io.github.lgm42.monalbumpc.backup.BackgroundAccess
import io.github.lgm42.monalbumpc.media.OriginalMedia
import io.github.lgm42.monalbumpc.ui.onboarding.MediaPermissions
import io.github.lgm42.monalbumpc.ui.onboarding.PhotoAccess
import io.github.lgm42.monalbumpc.ui.theme.LocalExtendedColors

/**
 * Whether the app should still ask for ACCESS_MEDIA_LOCATION: photo access is granted but not this one
 * (app installed before it was requested, or refused). Refreshed each time the screen comes back.
 */
@Composable
fun rememberMediaLocationMissing(): Boolean {
    val context = LocalContext.current
    var missing by remember { mutableStateOf(isMediaLocationMissing(context)) }
    LifecycleEventEffect(Lifecycle.Event.ON_RESUME) { missing = isMediaLocationMissing(context) }
    return missing
}

private fun isMediaLocationMissing(context: Context): Boolean =
    MediaPermissions.photoAccess(context) != PhotoAccess.Denied && !OriginalMedia.isLocationGranted(context)

/**
 * Card asking for the position of the photos: without it Android removes the GPS data from every photo
 * the app reads, and the PC receives them without their location. Asks once by itself, then on the button.
 */
@Composable
fun MediaLocationCard(modifier: Modifier = Modifier) {
    val context = LocalContext.current
    val warning = LocalExtendedColors.current
    // The next reads use the originals by themselves; the card hides itself on resume.
    val launcher = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { }
    val request = {
        val activity = context.findActivity()
        val permanentlyDenied = MediaLocationAsk.asked(context) && activity != null &&
            !ActivityCompat.shouldShowRequestPermissionRationale(activity, Manifest.permission.ACCESS_MEDIA_LOCATION)
        if (permanentlyDenied) {
            // Android no longer shows the question: the switch is in the app's permissions.
            try {
                context.startActivity(BackgroundAccess.appDetails(context))
            } catch (e: ActivityNotFoundException) {
                // Nothing to open.
            }
        } else {
            MediaLocationAsk.markAsked(context)
            launcher.launch(Manifest.permission.ACCESS_MEDIA_LOCATION)
        }
    }
    // Apps updated from a version that did not ask: put the question once, without waiting for the button.
    LaunchedEffect(Unit) {
        if (!MediaLocationAsk.asked(context)) request()
    }
    Surface(
        modifier = modifier.fillMaxWidth(),
        shape = RoundedCornerShape(18.dp),
        color = warning.warningContainer,
    ) {
        Column(Modifier.padding(16.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Icon(Icons.Outlined.LocationOn, contentDescription = null, tint = warning.warningIcon, modifier = Modifier.size(24.dp))
                Spacer(Modifier.width(10.dp))
                Text(
                    text = stringResource(R.string.media_location_title),
                    style = MaterialTheme.typography.titleMedium,
                    color = warning.onWarningContainer,
                )
            }
            Text(
                text = stringResource(R.string.media_location_text),
                style = MaterialTheme.typography.bodyMedium,
                color = warning.onWarningContainer,
                modifier = Modifier.padding(top = 8.dp, bottom = 12.dp),
            )
            Button(onClick = request) {
                Text(stringResource(R.string.media_location_allow))
            }
        }
    }
}

/** Remembers that the question was put once, to tell "refused for good" from "never asked". */
private object MediaLocationAsk {
    private const val FILE = "media_location"
    private const val KEY_ASKED = "asked"

    fun asked(context: Context): Boolean =
        context.getSharedPreferences(FILE, Context.MODE_PRIVATE).getBoolean(KEY_ASKED, false)

    fun markAsked(context: Context) {
        context.getSharedPreferences(FILE, Context.MODE_PRIVATE).edit { putBoolean(KEY_ASKED, true) }
    }
}

private tailrec fun Context.findActivity(): Activity? = when (this) {
    is Activity -> this
    is ContextWrapper -> baseContext.findActivity()
    else -> null
}
