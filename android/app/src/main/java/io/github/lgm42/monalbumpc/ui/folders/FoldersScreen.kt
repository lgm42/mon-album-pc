package io.github.lgm42.monalbumpc.ui.folders

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.selection.toggleable
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.CreateNewFolder
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
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
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.res.pluralStringResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import io.github.lgm42.monalbumpc.R
import io.github.lgm42.monalbumpc.media.DeviceFolder
import io.github.lgm42.monalbumpc.media.FolderSection
import io.github.lgm42.monalbumpc.ui.components.AppDialog
import io.github.lgm42.monalbumpc.ui.components.MediaThumbnail
import io.github.lgm42.monalbumpc.ui.components.ScreenHeader
import io.github.lgm42.monalbumpc.ui.theme.LocalExtendedColors
import java.text.NumberFormat
import java.util.Locale

/** Folders tab (design README §3): tracked folders by section, with a switch per folder. */
@Composable
fun FoldersScreen(
    folders: List<DeviceFolder>,
    tracked: Set<String>,
    onTrackedChange: (key: String, tracked: Boolean) -> Unit,
    modifier: Modifier = Modifier,
) {
    var confirmUntrack by rememberSaveable { mutableStateOf<String?>(null) }
    val trackedFolders = folders.filter { it.key in tracked }
    val bySection = folders.groupBy { it.classification.section }

    LazyColumn(
        modifier = modifier.fillMaxSize(),
        contentPadding = PaddingValues(bottom = 24.dp),
    ) {
        item(key = "header") {
            ScreenHeader(
                title = stringResource(R.string.folders_title),
                overline = null,
            )
            Text(
                text = stringResource(
                    R.string.folders_summary,
                    pluralStringResource(R.plurals.folders_count, trackedFolders.size, trackedFolders.size),
                    pluralStringResource(
                        R.plurals.items_count,
                        trackedFolders.sumOf { it.count },
                        formatCount(trackedFolders.sumOf { it.count }),
                    ),
                ),
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(start = 20.dp, end = 20.dp, bottom = 8.dp),
            )
        }
        if (folders.isEmpty()) {
            item(key = "empty") {
                Text(
                    text = stringResource(R.string.folders_empty),
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.padding(horizontal = 20.dp, vertical = 24.dp),
                )
            }
        }
        for (section in FolderSection.entries) {
            val sectionFolders = bySection[section].orEmpty()
            if (sectionFolders.isEmpty()) continue
            item(key = "section-${section.name}") {
                SectionHeader(
                    title = stringResource(section.titleRes),
                    savedCount = sectionFolders.count { it.key in tracked },
                )
            }
            items(sectionFolders, key = { it.key }) { folder ->
                FolderRow(
                    folder = folder,
                    checked = folder.key in tracked,
                    onCheckedChange = { checked ->
                        if (checked) onTrackedChange(folder.key, true) else confirmUntrack = folder.key
                    },
                    modifier = Modifier.padding(horizontal = 16.dp, vertical = 4.dp),
                )
            }
        }
        item(key = "add") {
            // TODO: pick any folder with ACTION_OPEN_DOCUMENT_TREE (Storage Access Framework).
            OutlinedButton(
                onClick = {},
                enabled = false,
                shape = CircleShape,
                modifier = Modifier
                    .padding(horizontal = 16.dp, vertical = 16.dp)
                    .fillMaxWidth()
                    .heightIn(min = 48.dp),
            ) {
                Icon(Icons.Outlined.CreateNewFolder, contentDescription = null)
                Spacer(Modifier.width(8.dp))
                Text(stringResource(R.string.folders_add), style = MaterialTheme.typography.labelLarge)
            }
        }
    }

    val untrackKey = confirmUntrack
    val untrackFolder = folders.firstOrNull { it.key == untrackKey }
    if (untrackKey != null && untrackFolder != null) {
        AppDialog(
            title = stringResource(R.string.untrack_title, untrackFolder.friendlyName),
            note = stringResource(R.string.untrack_note),
            confirmLabel = stringResource(R.string.untrack_confirm),
            dismissLabel = stringResource(R.string.action_cancel),
            onConfirm = {
                confirmUntrack = null
                onTrackedChange(untrackKey, false)
            },
            onDismiss = { confirmUntrack = null },
        )
    }
}

private val FolderSection.titleRes: Int
    get() = when (this) {
        FolderSection.Recommended -> R.string.folders_section_recommended
        FolderSection.OtherApps -> R.string.folders_section_apps
        FolderSection.OtherFolders -> R.string.folders_section_other
    }

@Composable
private fun SectionHeader(title: String, savedCount: Int) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(start = 20.dp, end = 20.dp, top = 16.dp, bottom = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(
            text = title,
            style = MaterialTheme.typography.titleSmall,
            color = MaterialTheme.colorScheme.onBackground,
            modifier = Modifier.weight(1f),
        )
        Text(
            text = pluralStringResource(R.plurals.folders_section_saved, savedCount, savedCount),
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
    }
}

@Composable
private fun FolderRow(
    folder: DeviceFolder,
    checked: Boolean,
    onCheckedChange: (Boolean) -> Unit,
    modifier: Modifier = Modifier,
) {
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
                .heightIn(min = 72.dp)
                .padding(horizontal = 12.dp, vertical = 10.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Box(
                modifier = Modifier
                    .size(52.dp)
                    .clip(RoundedCornerShape(14.dp))
                    .background(MaterialTheme.colorScheme.surfaceVariant)
                    .alpha(if (checked) 1f else 0.5f),
            ) {
                val uri = folder.newestUri
                if (uri != null) MediaThumbnail(uri = uri, modifier = Modifier.fillMaxSize(), sizePx = 160)
            }
            Spacer(Modifier.width(14.dp))
            Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.Center) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text(
                        text = folder.friendlyName,
                        style = MaterialTheme.typography.titleMedium,
                        color = MaterialTheme.colorScheme.onSurface,
                        maxLines = 2,
                        overflow = TextOverflow.Ellipsis,
                        modifier = Modifier.weight(1f, fill = false),
                    )
                    if (folder.classification.onSdCard) {
                        Spacer(Modifier.width(8.dp))
                        SdCardLabel()
                    }
                }
                Text(
                    text = pluralStringResource(R.plurals.items_count, folder.count, formatCount(folder.count)),
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            Spacer(Modifier.width(8.dp))
            // The whole row toggles; the switch only shows the state.
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

@Composable
private fun SdCardLabel() {
    Text(
        text = stringResource(R.string.folder_sd_card),
        style = MaterialTheme.typography.labelSmall,
        color = LocalExtendedColors.current.primaryText,
        modifier = Modifier
            .background(MaterialTheme.colorScheme.primaryContainer, RoundedCornerShape(8.dp))
            .padding(horizontal = 8.dp, vertical = 2.dp),
    )
}

/** "10 312" with the French thousands separator. */
fun formatCount(count: Int): String = NumberFormat.getIntegerInstance(Locale.FRENCH).format(count)
