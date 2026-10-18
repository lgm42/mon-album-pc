package io.github.lgm42.monalbumpc.ui.onboarding

import android.Manifest
import android.os.Build
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Info
import androidx.compose.material.icons.outlined.NotificationsNone
import androidx.compose.material.icons.outlined.PhotoLibrary
import androidx.compose.material.icons.outlined.WarningAmber
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import io.github.lgm42.monalbumpc.R
import io.github.lgm42.monalbumpc.ui.components.boldTemplate
import io.github.lgm42.monalbumpc.ui.theme.LocalExtendedColors

@Composable
fun PhotosPermissionStep(onResult: (PhotoAccess) -> Unit) {
    val context = LocalContext.current
    val launcher = rememberLauncherForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) {
        onResult(MediaPermissions.photoAccess(context))
    }
    OnboardingPage(
        step = 1,
        actions = {
            PrimaryButton(
                text = stringResource(R.string.action_continue),
                onClick = { launcher.launch(MediaPermissions.photoPermissions()) },
                arrow = true,
            )
        },
    ) {
        Spacer(Modifier.height(48.dp))
        StepBadge(Icons.Outlined.PhotoLibrary)
        Spacer(Modifier.height(28.dp))
        StepOverline(stringResource(R.string.perm_photos_overline))
        Spacer(Modifier.height(8.dp))
        StepTitle(stringResource(R.string.perm_photos_title))
        Spacer(Modifier.height(16.dp))
        StepBody(
            boldTemplate(
                stringResource(R.string.perm_photos_body),
                stringResource(R.string.perm_photos_body_bold),
            ),
        )
        Spacer(Modifier.height(12.dp))
        StepBody(stringResource(R.string.perm_photos_hint))
    }
}

@Composable
fun PartialAccessStep(denied: Boolean, onResult: (PhotoAccess) -> Unit, onContinue: () -> Unit) {
    val context = LocalContext.current
    val launcher = rememberLauncherForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) {
        onResult(MediaPermissions.photoAccess(context))
    }
    val extended = LocalExtendedColors.current
    OnboardingPage(
        step = 1,
        actions = {
            PrimaryButton(
                text = stringResource(R.string.partial_allow_all),
                onClick = { launcher.launch(MediaPermissions.photoPermissions()) },
            )
            SecondaryButton(text = stringResource(R.string.partial_continue), onClick = onContinue)
        },
    ) {
        Spacer(Modifier.height(48.dp))
        StepBadge(Icons.Outlined.WarningAmber, warning = true)
        Spacer(Modifier.height(28.dp))
        StepTitle(stringResource(R.string.partial_title))
        Spacer(Modifier.height(16.dp))
        StepBody(
            stringResource(if (denied) R.string.partial_body_denied else R.string.partial_body_selected),
        )
        Spacer(Modifier.height(20.dp))
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .background(extended.warningContainer, RoundedCornerShape(18.dp))
                .padding(16.dp),
            verticalAlignment = Alignment.Top,
        ) {
            Icon(
                imageVector = Icons.Outlined.Info,
                contentDescription = null,
                tint = extended.warningIcon,
                modifier = Modifier.size(22.dp),
            )
            Spacer(Modifier.width(12.dp))
            Text(
                text = boldTemplate(
                    stringResource(R.string.partial_hint),
                    stringResource(R.string.partial_hint_bold),
                ),
                style = MaterialTheme.typography.bodyMedium,
                color = extended.onWarningContainer,
            )
        }
    }
}

@Composable
fun NotificationsStep(onDone: () -> Unit) {
    val launcher = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) {
        onDone()
    }
    OnboardingPage(
        step = 2,
        actions = {
            PrimaryButton(
                text = stringResource(R.string.action_continue),
                onClick = {
                    if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                        launcher.launch(Manifest.permission.POST_NOTIFICATIONS)
                    } else {
                        onDone()
                    }
                },
                arrow = true,
            )
        },
    ) {
        Spacer(Modifier.height(48.dp))
        StepBadge(Icons.Outlined.NotificationsNone)
        Spacer(Modifier.height(28.dp))
        StepOverline(stringResource(R.string.perm_notif_overline))
        Spacer(Modifier.height(8.dp))
        StepTitle(stringResource(R.string.perm_notif_title))
        Spacer(Modifier.height(16.dp))
        StepBody(
            boldTemplate(
                stringResource(R.string.perm_notif_body),
                stringResource(R.string.perm_notif_body_bold),
            ),
        )
    }
}
