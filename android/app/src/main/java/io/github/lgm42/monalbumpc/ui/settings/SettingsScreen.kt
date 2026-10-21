package io.github.lgm42.monalbumpc.ui.settings

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.selection.toggleable
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Check
import androidx.compose.material.icons.outlined.Computer
import androidx.compose.material.icons.outlined.Delete
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Dialog
import io.github.lgm42.monalbumpc.R
import io.github.lgm42.monalbumpc.ui.components.BackgroundAccessCard
import io.github.lgm42.monalbumpc.ui.components.rememberBackgroundUnrestricted
import io.github.lgm42.monalbumpc.data.PairedPc
import io.github.lgm42.monalbumpc.ui.components.ScreenHeader
import io.github.lgm42.monalbumpc.ui.theme.MonAlbumPcTheme
import io.github.lgm42.monalbumpc.ui.UpdateUiState
import io.github.lgm42.monalbumpc.ui.components.updateDetail
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale

@Composable
fun SettingsScreen(
    pc: PairedPc,
    onUnpair: () -> Unit,
    chargingOnly: Boolean,
    onChargingOnlyChange: (Boolean) -> Unit,
    mobileData: Boolean,
    onMobileDataChange: (Boolean) -> Unit,
    lastBackupMs: Long?,
    update: UpdateUiState,
    onUpdate: () -> Unit,
    modifier: Modifier = Modifier,
) {
    var confirmUnpair by rememberSaveable { mutableStateOf(false) }

    Column(
        modifier = modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState()),
    ) {
        ScreenHeader(title = stringResource(R.string.settings_title))
        Text(
            text = stringResource(R.string.settings_my_pc),
            style = MaterialTheme.typography.titleSmall,
            color = MaterialTheme.colorScheme.onBackground,
            modifier = Modifier.padding(horizontal = 20.dp, vertical = 8.dp),
        )
        MyPcCard(
            pc = pc,
            lastBackupMs = lastBackupMs,
            onUnpairClick = { confirmUnpair = true },
            modifier = Modifier.padding(horizontal = 16.dp),
        )
        Text(
            text = stringResource(R.string.settings_backup),
            style = MaterialTheme.typography.titleSmall,
            color = MaterialTheme.colorScheme.onBackground,
            modifier = Modifier.padding(start = 20.dp, end = 20.dp, top = 24.dp, bottom = 8.dp),
        )
        SwitchRow(
            label = stringResource(R.string.settings_charging_only),
            checked = chargingOnly,
            onCheckedChange = onChargingOnlyChange,
            modifier = Modifier.padding(horizontal = 16.dp),
        )
        Spacer(Modifier.height(8.dp))
        SwitchRow(
            label = stringResource(R.string.settings_mobile_data),
            checked = mobileData,
            onCheckedChange = onMobileDataChange,
            modifier = Modifier.padding(horizontal = 16.dp),
        )
        Text(
            text = stringResource(R.string.settings_mobile_data_hint),
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onBackground.copy(alpha = 0.7f),
            modifier = Modifier.padding(start = 20.dp, end = 20.dp, top = 6.dp),
        )
        Spacer(Modifier.height(16.dp))
        if (rememberBackgroundUnrestricted()) {
            Text(
                text = stringResource(R.string.background_ok),
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onBackground.copy(alpha = 0.7f),
                modifier = Modifier.padding(horizontal = 20.dp),
            )
        } else {
            BackgroundAccessCard(modifier = Modifier.padding(horizontal = 16.dp))
        }
        Text(
            text = stringResource(R.string.settings_about),
            style = MaterialTheme.typography.titleSmall,
            color = MaterialTheme.colorScheme.onBackground,
            modifier = Modifier.padding(start = 20.dp, end = 20.dp, top = 24.dp, bottom = 8.dp),
        )
        VersionRow(update = update, onUpdate = onUpdate, modifier = Modifier.padding(horizontal = 16.dp))
        Spacer(Modifier.height(24.dp))
    }

    if (confirmUnpair) {
        UnpairDialog(
            pcName = pc.pcName,
            onConfirm = {
                confirmUnpair = false
                onUnpair()
            },
            onDismiss = { confirmUnpair = false },
        )
    }
}

@Composable
private fun MyPcCard(pc: PairedPc, lastBackupMs: Long?, onUnpairClick: () -> Unit, modifier: Modifier = Modifier) {
    Surface(
        modifier = modifier.fillMaxWidth(),
        shape = RoundedCornerShape(24.dp),
        color = MaterialTheme.colorScheme.surface,
    ) {
        Row(
            modifier = Modifier.padding(start = 20.dp, top = 16.dp, bottom = 16.dp, end = 8.dp),
            verticalAlignment = Alignment.Top,
        ) {
            Box(
                modifier = Modifier
                    .size(48.dp)
                    .background(MaterialTheme.colorScheme.primaryContainer, CircleShape),
                contentAlignment = Alignment.Center,
            ) {
                Icon(
                    imageVector = Icons.Outlined.Computer,
                    contentDescription = null,
                    tint = MaterialTheme.colorScheme.onPrimaryContainer,
                )
            }
            Spacer(Modifier.width(16.dp))
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = pc.pcName,
                    style = MaterialTheme.typography.titleLarge,
                    color = MaterialTheme.colorScheme.onSurface,
                )
                Spacer(Modifier.height(2.dp))
                Text(
                    text = stringResource(R.string.settings_album_of, pc.albumName),
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                Text(
                    text = lastBackupText(lastBackupMs),
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            IconButton(onClick = onUnpairClick, modifier = Modifier.size(48.dp)) {
                Icon(
                    imageVector = Icons.Outlined.Delete,
                    contentDescription = stringResource(R.string.settings_unpair),
                    tint = MaterialTheme.colorScheme.onSurface,
                )
            }
        }
    }
}

/** "Version 1.4.0 · à jour", or "· la 1.5.0 est disponible" with a Mettre à jour button (design README §4). */
@Composable
private fun VersionRow(update: UpdateUiState, onUpdate: () -> Unit, modifier: Modifier = Modifier) {
    Surface(
        modifier = modifier.fillMaxWidth(),
        shape = RoundedCornerShape(22.dp),
        color = MaterialTheme.colorScheme.surface,
    ) {
        Column(
            modifier = Modifier
                .heightIn(min = 64.dp)
                .padding(horizontal = 18.dp, vertical = 12.dp),
        ) {
            Text(
                text = if (update.available) {
                    stringResource(R.string.settings_version_available, update.installedName, update.availableName)
                } else {
                    stringResource(R.string.settings_version_up_to_date, update.installedName)
                },
                style = MaterialTheme.typography.titleMedium,
                color = MaterialTheme.colorScheme.onSurface,
            )
            if (update.available) {
                updateDetail(update)?.let { detail ->
                    Text(
                        text = detail,
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        modifier = Modifier.padding(top = 4.dp),
                    )
                }
                Button(
                    onClick = onUpdate,
                    enabled = !update.busy,
                    shape = CircleShape,
                    modifier = Modifier.padding(top = 8.dp),
                ) {
                    Text(stringResource(R.string.update_action), style = MaterialTheme.typography.labelLarge)
                }
            }
        }
    }
}

/** Settings row: 22dp card, label, switch; the whole row toggles. */
@Composable
private fun SwitchRow(label: String, checked: Boolean, onCheckedChange: (Boolean) -> Unit, modifier: Modifier = Modifier) {
    Surface(
        modifier = modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(22.dp))
            .toggleable(value = checked, role = Role.Switch, onValueChange = onCheckedChange),
        shape = RoundedCornerShape(22.dp),
        color = MaterialTheme.colorScheme.surface,
    ) {
        Row(
            modifier = Modifier
                .heightIn(min = 64.dp)
                .padding(horizontal = 18.dp, vertical = 12.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text(
                text = label,
                style = MaterialTheme.typography.titleMedium,
                color = MaterialTheme.colorScheme.onSurface,
                modifier = Modifier.weight(1f),
            )
            Spacer(Modifier.width(12.dp))
            Switch(
                checked = checked,
                onCheckedChange = null,
                colors = SwitchDefaults.colors(
                    checkedTrackColor = MaterialTheme.colorScheme.primary,
                    checkedThumbColor = MaterialTheme.colorScheme.onPrimary,
                ),
            )
        }
    }
}

/** "Dernière sauvegarde : aujourd'hui à 12:31". */
@Composable
private fun lastBackupText(lastBackupMs: Long?): String {
    if (lastBackupMs == null) return stringResource(R.string.settings_last_backup_never)
    val zone = ZoneId.systemDefault()
    val time = Instant.ofEpochMilli(lastBackupMs).atZone(zone)
    val clock = time.format(DateTimeFormatter.ofPattern("HH:mm", Locale.FRENCH))
    val text = if (time.toLocalDate() == LocalDate.now(zone)) {
        stringResource(R.string.date_today_at, clock)
    } else {
        stringResource(R.string.date_on_at, time.format(DateTimeFormatter.ofPattern("dd/MM/yyyy", Locale.FRENCH)), clock)
    }
    return stringResource(R.string.settings_last_backup, text)
}

/** App dialog: 28dp card, title, text, note with a check, stacked full-width actions. */
@Composable
private fun UnpairDialog(pcName: String, onConfirm: () -> Unit, onDismiss: () -> Unit) {
    Dialog(onDismissRequest = onDismiss) {
        Surface(
            shape = RoundedCornerShape(28.dp),
            color = MaterialTheme.colorScheme.background,
        ) {
            Column(modifier = Modifier.padding(24.dp)) {
                Text(
                    text = stringResource(R.string.unpair_title),
                    style = MaterialTheme.typography.headlineSmall,
                    color = MaterialTheme.colorScheme.onBackground,
                )
                Spacer(Modifier.height(12.dp))
                Text(
                    text = stringResource(R.string.unpair_body, pcName),
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onBackground,
                )
                Spacer(Modifier.height(12.dp))
                Row(verticalAlignment = Alignment.Top) {
                    Icon(
                        imageVector = Icons.Outlined.Check,
                        contentDescription = null,
                        tint = MaterialTheme.colorScheme.primary,
                        modifier = Modifier.size(20.dp),
                    )
                    Spacer(Modifier.width(8.dp))
                    Text(
                        text = stringResource(R.string.unpair_note),
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onBackground,
                    )
                }
                Spacer(Modifier.height(24.dp))
                Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    Button(
                        onClick = onConfirm,
                        modifier = Modifier
                            .fillMaxWidth()
                            .heightIn(min = 48.dp),
                        shape = CircleShape,
                    ) {
                        Text(stringResource(R.string.unpair_confirm), style = MaterialTheme.typography.labelLarge)
                    }
                    Button(
                        onClick = onDismiss,
                        modifier = Modifier
                            .fillMaxWidth()
                            .heightIn(min = 48.dp),
                        shape = CircleShape,
                        colors = ButtonDefaults.buttonColors(
                            containerColor = MaterialTheme.colorScheme.surface,
                            contentColor = MaterialTheme.colorScheme.onSurface,
                        ),
                    ) {
                        Text(stringResource(R.string.action_cancel), style = MaterialTheme.typography.labelLarge)
                    }
                }
            }
        }
    }
}

private val PreviewPc = PairedPc(
    pcId = "3b2e",
    pcName = "PC-SALON",
    albumName = "Marie",
    host = "192.168.1.20",
    apiPort = 48620,
    certSha256 = "",
    deviceId = "8f1c",
    secret = "",
)

@Preview(showBackground = true)
@Composable
private fun SettingsScreenPreview() {
    MonAlbumPcTheme {
        SettingsScreen(
            pc = PreviewPc,
            onUnpair = {},
            chargingOnly = false,
            onChargingOnlyChange = {},
            mobileData = false,
            onMobileDataChange = {},
            lastBackupMs = null,
            update = UpdateUiState(installedName = "1.4.0"),
            onUpdate = {},
        )
    }
}
