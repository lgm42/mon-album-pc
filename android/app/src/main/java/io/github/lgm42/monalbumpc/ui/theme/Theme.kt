package io.github.lgm42.monalbumpc.ui.theme

import android.os.Build
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.dynamicDarkColorScheme
import androidx.compose.material3.dynamicLightColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.Immutable
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext

private val LightColors = lightColorScheme(
    primary = LightPrimary,
    onPrimary = LightBackground,
    primaryContainer = LightPrimaryContainer,
    onPrimaryContainer = LightOnPrimaryContainer,
    inversePrimary = DarkPrimary,
    secondary = LightPrimary,
    onSecondary = LightBackground,
    secondaryContainer = LightPrimaryContainer,
    onSecondaryContainer = LightOnPrimaryContainer,
    tertiary = LightPrimary,
    onTertiary = LightBackground,
    tertiaryContainer = LightPrimaryContainer,
    onTertiaryContainer = LightOnPrimaryContainer,
    background = LightBackground,
    onBackground = LightText,
    surface = LightSurface,
    onSurface = LightText,
    surfaceVariant = LightSurfaceVariant,
    onSurfaceVariant = LightText.copy(alpha = 0.7f),
    surfaceTint = LightPrimary,
    inverseSurface = LightText,
    inverseOnSurface = LightBackground,
    error = LightError,
    onError = Color.White,
    outline = LightText.copy(alpha = 0.4f),
    outlineVariant = LightText.copy(alpha = 0.14f),
    surfaceBright = LightBackground,
    surfaceDim = LightSurfaceVariant,
    surfaceContainerLowest = LightBackground,
    surfaceContainerLow = LightSurface,
    surfaceContainer = LightSurface,
    surfaceContainerHigh = LightSurface,
    surfaceContainerHighest = LightSurfaceVariant,
)

private val DarkColors = darkColorScheme(
    primary = DarkPrimary,
    onPrimary = DarkBackground,
    primaryContainer = DarkPrimaryContainer,
    onPrimaryContainer = DarkOnPrimaryContainer,
    inversePrimary = LightPrimary,
    secondary = DarkPrimary,
    onSecondary = DarkBackground,
    secondaryContainer = DarkPrimaryContainer,
    onSecondaryContainer = DarkOnPrimaryContainer,
    tertiary = DarkPrimary,
    onTertiary = DarkBackground,
    tertiaryContainer = DarkPrimaryContainer,
    onTertiaryContainer = DarkOnPrimaryContainer,
    background = DarkBackground,
    onBackground = DarkText,
    surface = DarkSurface,
    onSurface = DarkText,
    surfaceVariant = DarkSurfaceVariant,
    onSurfaceVariant = DarkText.copy(alpha = 0.7f),
    surfaceTint = DarkPrimary,
    inverseSurface = DarkText,
    inverseOnSurface = DarkBackground,
    error = DarkError,
    onError = DarkOnError,
    outline = DarkText.copy(alpha = 0.4f),
    outlineVariant = DarkText.copy(alpha = 0.16f),
    surfaceBright = DarkSurfaceVariant,
    surfaceDim = DarkBackground,
    surfaceContainerLowest = DarkBackground,
    surfaceContainerLow = DarkSurface,
    surfaceContainer = DarkSurface,
    surfaceContainerHigh = DarkSurface,
    surfaceContainerHighest = DarkSurfaceVariant,
)

/** Brand colors that have no Material 3 role. */
@Immutable
data class ExtendedColors(
    val primaryText: Color,
    val primaryPressed: Color,
    val warningContainer: Color,
    val onWarningContainer: Color,
    val warningIcon: Color,
)

private val LightExtended = ExtendedColors(
    primaryText = LightPrimaryText,
    primaryPressed = LightPrimaryPressed,
    warningContainer = LightWarningContainer,
    onWarningContainer = LightOnWarningContainer,
    warningIcon = LightWarningIcon,
)

private val DarkExtended = ExtendedColors(
    primaryText = DarkPrimaryText,
    primaryPressed = DarkPrimaryPressed,
    warningContainer = DarkWarningContainer,
    onWarningContainer = DarkOnWarningContainer,
    warningIcon = DarkWarningIcon,
)

val LocalExtendedColors = staticCompositionLocalOf { LightExtended }

@Composable
fun MonAlbumPcTheme(
    darkTheme: Boolean = isSystemInDarkTheme(),
    // Brand palette by default; Android 12+ dynamic colors will become a setting.
    dynamicColor: Boolean = false,
    content: @Composable () -> Unit,
) {
    val colorScheme = when {
        dynamicColor && Build.VERSION.SDK_INT >= Build.VERSION_CODES.S -> {
            val context = LocalContext.current
            if (darkTheme) dynamicDarkColorScheme(context) else dynamicLightColorScheme(context)
        }
        darkTheme -> DarkColors
        else -> LightColors
    }

    CompositionLocalProvider(
        LocalExtendedColors provides if (darkTheme) DarkExtended else LightExtended,
    ) {
        MaterialTheme(
            colorScheme = colorScheme,
            typography = Typography,
            content = content,
        )
    }
}
