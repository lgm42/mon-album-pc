package io.github.lgm42.monalbumpc.ui.onboarding

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Check
import androidx.compose.material.icons.outlined.ErrorOutline
import androidx.compose.material.icons.outlined.QrCode
import androidx.compose.material.icons.outlined.WifiOff
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import io.github.lgm42.monalbumpc.R
import io.github.lgm42.monalbumpc.ui.components.boldTemplate
import io.github.lgm42.monalbumpc.ui.theme.MonAlbumPcTheme

@Composable
fun PairingInProgress() {
    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(MaterialTheme.colorScheme.background)
            .safeDrawingPadding()
            .padding(24.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.Center,
    ) {
        CircularProgressIndicator()
        Spacer(Modifier.height(20.dp))
        Text(
            text = stringResource(R.string.pairing_in_progress),
            style = MaterialTheme.typography.bodyLarge,
            color = MaterialTheme.colorScheme.onBackground,
        )
    }
}

@Composable
fun SuccessStep(pcName: String, albumName: String, onContinue: () -> Unit) {
    OnboardingPage(
        step = 4,
        actions = {
            PrimaryButton(text = stringResource(R.string.action_continue), onClick = onContinue, arrow = true)
        },
    ) {
        Spacer(Modifier.height(48.dp))
        StepBadge(Icons.Outlined.Check)
        Spacer(Modifier.height(28.dp))
        StepTitle(stringResource(R.string.success_title))
        Spacer(Modifier.height(12.dp))
        Text(
            text = boldTemplate(stringResource(R.string.success_body), pcName, albumName),
            style = MaterialTheme.typography.bodyLarge,
            fontSize = 19.sp,
            color = MaterialTheme.colorScheme.onBackground,
        )
        Spacer(Modifier.height(28.dp))
        Row(modifier = Modifier.fillMaxWidth()) {
            InfoCell(
                label = stringResource(R.string.success_cell_pc),
                value = pcName,
                modifier = Modifier
                    .weight(1f)
                    .padding(end = 12.dp),
            )
            InfoCell(
                label = stringResource(R.string.success_cell_album),
                value = albumName,
                modifier = Modifier
                    .weight(1f)
                    .padding(start = 12.dp),
            )
        }
    }
}

@Composable
private fun InfoCell(label: String, value: String, modifier: Modifier = Modifier) {
    Column(modifier = modifier.padding(vertical = 16.dp)) {
        Text(
            text = label,
            style = MaterialTheme.typography.bodySmall,
            fontSize = 12.sp,
            color = MaterialTheme.colorScheme.onBackground.copy(alpha = 0.65f),
        )
        Spacer(Modifier.height(6.dp))
        Text(
            text = value,
            style = MaterialTheme.typography.titleLarge,
            color = MaterialTheme.colorScheme.onBackground,
        )
    }
}

/** Error screen: amber badge, title, text, optional numbered checks and two actions. */
@Composable
private fun PairingErrorPage(
    icon: ImageVector,
    title: String,
    body: String,
    primaryLabel: String,
    onPrimary: () -> Unit,
    secondaryLabel: String,
    onSecondary: () -> Unit,
    onBack: () -> Unit,
    checks: List<String> = emptyList(),
) {
    OnboardingPage(
        step = null,
        onBack = onBack,
        actions = {
            PrimaryButton(text = primaryLabel, onClick = onPrimary)
            SecondaryButton(text = secondaryLabel, onClick = onSecondary)
        },
    ) {
        Spacer(Modifier.height(24.dp))
        StepBadge(icon, warning = true)
        Spacer(Modifier.height(28.dp))
        StepTitle(title)
        Spacer(Modifier.height(12.dp))
        StepBody(body)
        if (checks.isNotEmpty()) {
            Spacer(Modifier.height(20.dp))
            NumberedList(checks)
        }
    }
}

@Composable
fun QrInvalidStep(onRescan: () -> Unit, onUseCode: () -> Unit) {
    PairingErrorPage(
        icon = Icons.Outlined.QrCode,
        title = stringResource(R.string.error_qr_title),
        body = stringResource(R.string.error_qr_body),
        primaryLabel = stringResource(R.string.error_qr_rescan),
        onPrimary = onRescan,
        secondaryLabel = stringResource(R.string.error_qr_use_code),
        onSecondary = onUseCode,
        onBack = onRescan,
    )
}

@Composable
fun UnreachableStep(onRetry: () -> Unit, onOpenWifiSettings: () -> Unit, onBack: () -> Unit) {
    PairingErrorPage(
        icon = Icons.Outlined.WifiOff,
        title = stringResource(R.string.error_wifi_title),
        body = stringResource(R.string.error_wifi_body),
        checks = listOf(
            stringResource(R.string.error_wifi_check1),
            stringResource(R.string.error_wifi_check2),
            stringResource(R.string.error_wifi_check3),
        ),
        primaryLabel = stringResource(R.string.error_wifi_retry),
        onPrimary = onRetry,
        secondaryLabel = stringResource(R.string.error_wifi_settings),
        onSecondary = onOpenWifiSettings,
        onBack = onBack,
    )
}

@Composable
fun FailedStep(onRescan: () -> Unit, onUseCode: () -> Unit) {
    PairingErrorPage(
        icon = Icons.Outlined.ErrorOutline,
        title = stringResource(R.string.error_other_title),
        body = stringResource(R.string.error_other_body),
        primaryLabel = stringResource(R.string.error_qr_rescan),
        onPrimary = onRescan,
        secondaryLabel = stringResource(R.string.error_qr_use_code),
        onSecondary = onUseCode,
        onBack = onRescan,
    )
}

@Preview(showBackground = true, heightDp = 892)
@Composable
private fun SuccessStepPreview() {
    MonAlbumPcTheme { SuccessStep(pcName = "PC-SALON", albumName = "Marie", onContinue = {}) }
}
