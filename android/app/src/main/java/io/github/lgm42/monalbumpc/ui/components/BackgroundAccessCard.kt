package io.github.lgm42.monalbumpc.ui.components

import android.content.ActivityNotFoundException
import android.content.Context
import android.content.Intent
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.BatteryAlert
import androidx.compose.material3.Button
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LifecycleEventEffect
import io.github.lgm42.monalbumpc.R
import io.github.lgm42.monalbumpc.backup.BackgroundAccess
import io.github.lgm42.monalbumpc.ui.theme.LocalExtendedColors

/** Whether the app may back up in the background, refreshed each time the screen comes back. */
@Composable
fun rememberBackgroundUnrestricted(): Boolean {
    val context = LocalContext.current
    var unrestricted by remember { mutableStateOf(BackgroundAccess.isUnrestricted(context)) }
    LifecycleEventEffect(Lifecycle.Event.ON_RESUME) { unrestricted = BackgroundAccess.isUnrestricted(context) }
    return unrestricted
}

/** Warning card asking to lift the battery restriction (and Xiaomi's "Démarrage automatique"). */
@Composable
fun BackgroundAccessCard(modifier: Modifier = Modifier) {
    val context = LocalContext.current
    val warning = LocalExtendedColors.current
    Surface(
        modifier = modifier.fillMaxWidth(),
        shape = RoundedCornerShape(18.dp),
        color = warning.warningContainer,
    ) {
        Column(Modifier.padding(16.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Icon(Icons.Outlined.BatteryAlert, contentDescription = null, tint = warning.warningIcon, modifier = Modifier.size(24.dp))
                Spacer(Modifier.width(10.dp))
                Text(
                    text = stringResource(R.string.background_title),
                    style = MaterialTheme.typography.titleMedium,
                    color = warning.onWarningContainer,
                )
            }
            Text(
                text = stringResource(if (BackgroundAccess.isXiaomi) R.string.background_text_xiaomi else R.string.background_text),
                style = MaterialTheme.typography.bodyMedium,
                color = warning.onWarningContainer,
                modifier = Modifier.padding(top = 8.dp, bottom = 12.dp),
            )
            Row(verticalAlignment = Alignment.CenterVertically) {
                Button(onClick = { context.open(BackgroundAccess.requestUnrestricted(context)) }) {
                    Text(stringResource(R.string.background_allow))
                }
                if (BackgroundAccess.isXiaomi) {
                    Spacer(Modifier.width(8.dp))
                    TextButton(onClick = { context.open(BackgroundAccess.appDetails(context)) }) {
                        Text(stringResource(R.string.background_autostart), color = warning.onWarningContainer)
                    }
                }
            }
        }
    }
}

private fun Context.open(intent: Intent) {
    try {
        startActivity(intent)
    } catch (e: ActivityNotFoundException) {
        startActivity(BackgroundAccess.appDetails(this))
    }
}
