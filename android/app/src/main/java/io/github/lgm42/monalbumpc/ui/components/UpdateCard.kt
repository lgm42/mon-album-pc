package io.github.lgm42.monalbumpc.ui.components

import android.content.ActivityNotFoundException
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.provider.Settings
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.SystemUpdate
import androidx.compose.material3.Button
import androidx.compose.material3.Icon
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import io.github.lgm42.monalbumpc.R
import io.github.lgm42.monalbumpc.ui.UpdateUiState
import io.github.lgm42.monalbumpc.update.InstallPhase
import io.github.lgm42.monalbumpc.ui.theme.LocalExtendedColors

/** "Une nouvelle version de Mon Album PC est disponible." · Mettre à jour / Plus tard (design README §2). */
@Composable
fun UpdateCard(
    state: UpdateUiState,
    onUpdate: () -> Unit,
    onAllowInstall: () -> Unit,
    onLater: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val extended = LocalExtendedColors.current
    Surface(
        modifier = modifier.fillMaxWidth(),
        shape = RoundedCornerShape(18.dp),
        color = MaterialTheme.colorScheme.primaryContainer,
    ) {
        Column(Modifier.padding(16.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Icon(
                    imageVector = Icons.Outlined.SystemUpdate,
                    contentDescription = null,
                    tint = MaterialTheme.colorScheme.onPrimaryContainer,
                    modifier = Modifier.size(24.dp),
                )
                Spacer(Modifier.width(10.dp))
                Text(
                    text = stringResource(R.string.update_card_text, state.availableName),
                    style = MaterialTheme.typography.titleSmall,
                    color = MaterialTheme.colorScheme.onPrimaryContainer,
                    modifier = Modifier.weight(1f),
                )
            }
            val detail = updateDetail(state)
            if (detail != null) {
                Text(
                    text = detail,
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onPrimaryContainer,
                    modifier = Modifier.padding(top = 8.dp),
                )
            }
            val progress = state.downloadProgress
            if (progress != null) {
                LinearProgressIndicator(
                    progress = { progress },
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(top = 8.dp)
                        .height(4.dp),
                    color = MaterialTheme.colorScheme.primary,
                    trackColor = MaterialTheme.colorScheme.surface,
                    gapSize = 0.dp,
                    drawStopIndicator = {},
                )
            }
            Spacer(Modifier.height(12.dp))
            Row(verticalAlignment = Alignment.CenterVertically) {
                if (state.canInstallPackages) {
                    Button(onClick = onUpdate, enabled = !state.busy) {
                        Text(stringResource(R.string.update_action))
                    }
                } else {
                    Button(onClick = onAllowInstall) {
                        Text(stringResource(R.string.update_permission_action))
                    }
                }
                Spacer(Modifier.width(8.dp))
                TextButton(onClick = onLater) {
                    Text(stringResource(R.string.update_later), color = extended.primaryText)
                }
            }
        }
    }
}

/** The line under the title: progress, error, or what the user has to allow. */
@Composable
fun updateDetail(state: UpdateUiState): String? {
    val progress = state.downloadProgress
    return when {
        progress != null -> stringResource(R.string.update_downloading, (progress * 100).toInt())
        state.phase == InstallPhase.INSTALLING || state.phase == InstallPhase.WAITING_FOR_USER ->
            stringResource(R.string.update_installing)
        state.phase == InstallPhase.FAILED -> stringResource(R.string.update_install_failed)
        state.downloadFailed -> stringResource(R.string.update_download_failed)
        !state.canInstallPackages -> stringResource(R.string.update_permission_text)
        !state.downloaded -> stringResource(R.string.update_waiting_download)
        else -> null
    }
}

/** "Installer des applis inconnues" for this app, needed on some phones before the first update. */
fun Context.openUnknownSourcesSettings() {
    val intent = Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:$packageName"))
    try {
        startActivity(intent)
    } catch (e: ActivityNotFoundException) {
        try {
            startActivity(Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.parse("package:$packageName")))
        } catch (e2: ActivityNotFoundException) {
            // Nothing to open on this phone.
        }
    }
}
