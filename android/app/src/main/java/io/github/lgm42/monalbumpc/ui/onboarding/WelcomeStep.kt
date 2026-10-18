package io.github.lgm42.monalbumpc.ui.onboarding

import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Check
import androidx.compose.material.icons.outlined.Computer
import androidx.compose.material.icons.outlined.Photo
import androidx.compose.material.icons.outlined.Wifi
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.withStyle
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import io.github.lgm42.monalbumpc.R
import io.github.lgm42.monalbumpc.ui.theme.MonAlbumPcTheme

private val LogoBackground = Color(0xFF0B1430)

@Composable
fun WelcomeStep(onStart: () -> Unit) {
    OnboardingPage(
        step = 0,
        actions = { PrimaryButton(text = stringResource(R.string.welcome_start), onClick = onStart, arrow = true) },
    ) {
        Spacer(Modifier.height(40.dp))
        Logo()
        Spacer(Modifier.height(24.dp))
        TiltedCards()
        Spacer(Modifier.height(24.dp))
        Text(
            text = stringResource(R.string.welcome_title),
            style = MaterialTheme.typography.displayMedium,
            color = MaterialTheme.colorScheme.onBackground,
        )
        Spacer(Modifier.height(20.dp))
        Text(
            text = stringResource(R.string.welcome_subtitle),
            style = MaterialTheme.typography.bodyLarge,
            color = MaterialTheme.colorScheme.onBackground.copy(alpha = 0.75f),
        )
        Spacer(Modifier.height(28.dp))
        Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Asset(Icons.Outlined.Wifi, stringResource(R.string.welcome_asset_wifi))
            Asset(Icons.Outlined.Computer, stringResource(R.string.welcome_asset_pc))
            Asset(Icons.Outlined.Check, stringResource(R.string.welcome_asset_auto))
        }
    }
}

@Composable
private fun Logo() {
    val line1 = stringResource(R.string.logo_line1)
    val line2 = stringResource(R.string.logo_line2)
    val accent = MaterialTheme.colorScheme.primary
    Row(verticalAlignment = Alignment.CenterVertically) {
        Box(
            modifier = Modifier
                .size(56.dp)
                .clip(RoundedCornerShape(16.dp))
                .background(LogoBackground),
        ) {
            Image(
                painter = painterResource(R.drawable.ic_launcher_foreground),
                contentDescription = null,
                modifier = Modifier.fillMaxSize(),
            )
        }
        Spacer(Modifier.width(12.dp))
        Text(
            text = buildAnnotatedString {
                append(line1)
                append("\n")
                withStyle(SpanStyle(color = accent)) { append(line2) }
            },
            fontSize = 20.sp,
            lineHeight = 21.sp,
            fontWeight = FontWeight.ExtraBold,
            style = MaterialTheme.typography.titleLarge,
            color = MaterialTheme.colorScheme.onBackground,
        )
    }
}

/** Decoration: three tilted "photos", standing in for the design's photo collage. */
@Composable
private fun TiltedCards() {
    val cards = listOf(
        -6f to MaterialTheme.colorScheme.primaryContainer,
        3f to MaterialTheme.colorScheme.surfaceVariant,
        -2f to MaterialTheme.colorScheme.surface,
    )
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = 12.dp, vertical = 16.dp),
    ) {
        cards.forEachIndexed { index, (angle, color) ->
            val shape = RoundedCornerShape(22.dp)
            Box(
                modifier = Modifier
                    .weight(1f)
                    .aspectRatio(1f)
                    .offset(y = if (index == 1) (-8).dp else 8.dp)
                    .graphicsLayer {
                        rotationZ = angle
                        scaleX = 1.15f
                        scaleY = 1.15f
                    }
                    .clip(shape)
                    .background(color)
                    .border(5.dp, Color.White, shape),
                contentAlignment = Alignment.Center,
            ) {
                Icon(
                    imageVector = Icons.Outlined.Photo,
                    contentDescription = null,
                    tint = MaterialTheme.colorScheme.primary.copy(alpha = 0.5f),
                    modifier = Modifier.size(36.dp),
                )
            }
        }
    }
}

@Composable
private fun Asset(icon: ImageVector, label: String) {
    Row(verticalAlignment = Alignment.CenterVertically) {
        Box(
            modifier = Modifier
                .size(40.dp)
                .background(MaterialTheme.colorScheme.primaryContainer, CircleShape),
            contentAlignment = Alignment.Center,
        ) {
            Icon(
                imageVector = icon,
                contentDescription = null,
                tint = MaterialTheme.colorScheme.onPrimaryContainer,
                modifier = Modifier.size(22.dp),
            )
        }
        Spacer(Modifier.width(14.dp))
        Text(
            text = label,
            style = MaterialTheme.typography.titleMedium,
            color = MaterialTheme.colorScheme.onBackground,
        )
    }
}

@Preview(showBackground = true, heightDp = 892)
@Composable
private fun WelcomeStepPreview() {
    MonAlbumPcTheme { WelcomeStep(onStart = {}) }
}
