package io.github.lgm42.monalbumpc.ui.components

import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.withStyle

// Matches %1$s, %2$s…
private val placeholder = Regex("%(\\d+)\\\$s")

/**
 * Fills the positional placeholders of a string resource (read unformatted) with [args]
 * shown in bold, e.g. "Ce téléphone est relié à **PC-SALON**".
 */
fun boldTemplate(template: String, vararg args: String): AnnotatedString = buildAnnotatedString {
    var last = 0
    for (match in placeholder.findAll(template)) {
        append(template.substring(last, match.range.first))
        val index = match.groupValues[1].toInt() - 1
        withStyle(SpanStyle(fontWeight = FontWeight.Bold)) {
            append(args.getOrElse(index) { "" })
        }
        last = match.range.last + 1
    }
    append(template.substring(last))
}
