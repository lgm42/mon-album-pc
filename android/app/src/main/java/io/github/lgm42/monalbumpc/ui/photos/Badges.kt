package io.github.lgm42.monalbumpc.ui.photos

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ArrowUpward
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material.icons.filled.PriorityHigh
import androidx.compose.material.icons.filled.Remove
import androidx.compose.material.icons.outlined.Schedule
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import io.github.lgm42.monalbumpc.R

private val Ink = Color(0xFF201E1D)
private val ErrorRed = Color(0xFFC8463D)
private val UntrackedFill = Color(0xFFF3F2F2)
private val UntrackedBorder = Color(0xFF9A9796)
private val PendingInk = Color(0xFF141B33)

/** Round state badge (design README §2 table), 20dp in the grid, 28dp in the viewer. */
@Composable
fun StateBadge(state: BadgeState, modifier: Modifier = Modifier, size: Dp = 20.dp) {
    val style = badgeStyle(state, MaterialTheme.colorScheme.primary)
    var boxModifier = modifier
        .size(size)
        .background(style.fill, CircleShape)
    if (style.border != null) boxModifier = boxModifier.border(1.5.dp, style.border, CircleShape)
    Box(modifier = boxModifier, contentAlignment = Alignment.Center) {
        Icon(
            imageVector = style.icon,
            contentDescription = stringResource(state.labelRes),
            tint = style.tint,
            modifier = Modifier.size(size * 0.62f),
        )
    }
}

private class BadgeStyle(val fill: Color, val border: Color?, val icon: ImageVector, val tint: Color)

private fun badgeStyle(state: BadgeState, primary: Color): BadgeStyle = when (state) {
    BadgeState.SAVED -> BadgeStyle(Ink.copy(alpha = 0.6f), null, Icons.Filled.Check, Color.White)
    BadgeState.PENDING -> BadgeStyle(Color.White, PendingInk, Icons.Outlined.Schedule, PendingInk)
    BadgeState.UPLOADING -> BadgeStyle(Ink, primary, Icons.Filled.ArrowUpward, Color.White)
    BadgeState.ERROR -> BadgeStyle(ErrorRed, null, Icons.Filled.PriorityHigh, Color.White)
    BadgeState.UNTRACKED -> BadgeStyle(UntrackedFill, UntrackedBorder, Icons.Filled.Remove, UntrackedBorder)
}

val BadgeState.labelRes: Int
    get() = when (this) {
        BadgeState.SAVED -> R.string.state_saved
        BadgeState.PENDING -> R.string.state_pending
        BadgeState.UPLOADING -> R.string.state_uploading
        BadgeState.ERROR -> R.string.state_error
        BadgeState.UNTRACKED -> R.string.state_untracked
    }

/** Video duration chip, bottom-left of a thumbnail. */
@Composable
fun DurationChip(durationMs: Long, modifier: Modifier = Modifier) {
    Row(
        modifier = modifier
            .background(Color(0xCC201E1D), RoundedCornerShape(8.dp))
            .padding(horizontal = 5.dp, vertical = 2.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Icon(
            imageVector = Icons.Filled.PlayArrow,
            contentDescription = null,
            tint = Color.White,
            modifier = Modifier.size(12.dp),
        )
        Spacer(Modifier.width(2.dp))
        Text(
            text = GalleryModel.duration(durationMs),
            style = MaterialTheme.typography.labelSmall,
            color = Color.White,
        )
    }
}
