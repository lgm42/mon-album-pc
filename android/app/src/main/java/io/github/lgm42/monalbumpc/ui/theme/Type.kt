package io.github.lgm42.monalbumpc.ui.theme

import androidx.compose.material3.Typography
import androidx.compose.ui.text.ExperimentalTextApi
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.Font
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontVariation
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.em
import androidx.compose.ui.unit.sp
import io.github.lgm42.monalbumpc.R

// One variable font file, instanced at each weight the design uses.
@OptIn(ExperimentalTextApi::class)
private fun nunito(weight: Int) = Font(
    resId = R.font.nunito_variable,
    weight = FontWeight(weight),
    variationSettings = FontVariation.Settings(FontVariation.weight(weight)),
)

val Nunito = FontFamily(nunito(400), nunito(600), nunito(700), nunito(800))

private fun style(size: Int, weight: FontWeight, lineHeight: Float = 1.3f) = TextStyle(
    fontFamily = Nunito,
    fontWeight = weight,
    fontSize = size.sp,
    lineHeight = lineHeight.em,
)

// Type scale from docs/design/android/README.md, mapped to Material roles.
val Typography = Typography(
    displayLarge = style(48, FontWeight.Bold, 1.1f),          // big counter
    displayMedium = style(36, FontWeight.ExtraBold, 1.08f),   // welcome title
    headlineLarge = style(30, FontWeight.ExtraBold, 1.15f),   // onboarding step title
    headlineMedium = style(26, FontWeight.ExtraBold, 1.2f),   // tab title
    headlineSmall = style(22, FontWeight.ExtraBold, 1.25f),   // dialog title
    titleLarge = style(20, FontWeight.Bold),                  // PC name
    titleMedium = style(17, FontWeight.SemiBold),             // row label
    titleSmall = style(16, FontWeight.Bold),                  // section title
    bodyLarge = style(17, FontWeight.Normal, 1.5f),
    bodyMedium = style(16, FontWeight.Normal, 1.5f),
    bodySmall = style(14, FontWeight.Normal, 1.4f),           // secondary text
    labelLarge = style(16, FontWeight.Bold),                  // buttons
    labelMedium = style(14, FontWeight.Normal),               // nav labels
    labelSmall = style(11, FontWeight.SemiBold),              // video duration
)
