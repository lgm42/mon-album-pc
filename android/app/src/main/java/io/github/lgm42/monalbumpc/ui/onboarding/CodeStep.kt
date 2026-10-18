package io.github.lgm42.monalbumpc.ui.onboarding

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.ErrorOutline
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.RadioButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import io.github.lgm42.monalbumpc.R
import io.github.lgm42.monalbumpc.net.DiscoveredPc
import io.github.lgm42.monalbumpc.pairing.PairingCode
import io.github.lgm42.monalbumpc.ui.components.boldTemplate
import io.github.lgm42.monalbumpc.ui.theme.LocalExtendedColors

/** Pairing with the 6-digit code shown by the PC, for phones without a working camera. */
@Composable
fun CodeStep(
    state: OnboardingUiState,
    onCodeChange: (String) -> Unit,
    onSelectPc: (String) -> Unit,
    onSubmit: () -> Unit,
    onBack: () -> Unit,
    discover: suspend () -> Unit,
) {
    // Discovery runs while this screen is shown.
    LaunchedEffect(Unit) { discover() }

    OnboardingPage(
        step = null,
        onBack = onBack,
        actions = {
            PrimaryButton(
                text = stringResource(R.string.code_submit),
                onClick = onSubmit,
                enabled = state.canSubmitCode,
            )
        },
    ) {
        Spacer(Modifier.height(16.dp))
        Text(
            text = stringResource(R.string.code_title),
            style = MaterialTheme.typography.headlineLarge,
            fontSize = 28.sp,
            color = MaterialTheme.colorScheme.onBackground,
        )
        Spacer(Modifier.height(12.dp))
        Text(
            text = stringResource(R.string.code_body),
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onBackground,
        )
        Spacer(Modifier.height(20.dp))
        PcChooser(pcs = state.pcs, selected = state.selectedPc, onSelect = onSelectPc)
        Spacer(Modifier.height(24.dp))
        CodeBoxes(
            code = state.code,
            isError = state.codeError,
            onCodeChange = onCodeChange,
            onDone = { if (state.canSubmitCode) onSubmit() },
        )
        if (state.codeError) {
            Spacer(Modifier.height(14.dp))
            Row(verticalAlignment = Alignment.Top) {
                Icon(
                    imageVector = Icons.Outlined.ErrorOutline,
                    contentDescription = null,
                    tint = LocalExtendedColors.current.primaryText,
                    modifier = Modifier.size(20.dp),
                )
                Spacer(Modifier.width(8.dp))
                Text(
                    text = stringResource(R.string.code_error),
                    style = MaterialTheme.typography.bodyMedium,
                    fontSize = 15.sp,
                    color = LocalExtendedColors.current.primaryText,
                )
            }
        }
    }
}

@Composable
private fun PcChooser(pcs: List<DiscoveredPc>, selected: DiscoveredPc?, onSelect: (String) -> Unit) {
    when {
        pcs.isEmpty() -> Row(verticalAlignment = Alignment.CenterVertically) {
            CircularProgressIndicator(modifier = Modifier.size(18.dp), strokeWidth = 2.dp)
            Spacer(Modifier.width(12.dp))
            Text(
                text = stringResource(R.string.code_searching),
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
        pcs.size == 1 -> Text(
            text = boldTemplate(stringResource(R.string.code_found_one), pcs[0].name),
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onBackground,
        )
        else -> Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Text(
                text = stringResource(R.string.code_choose_pc),
                style = MaterialTheme.typography.titleSmall,
                color = MaterialTheme.colorScheme.onBackground,
            )
            pcs.forEach { pc ->
                val isSelected = pc.key == selected?.key
                val shape = RoundedCornerShape(22.dp)
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .heightIn(min = 56.dp)
                        .clip(shape)
                        .background(
                            if (isSelected) MaterialTheme.colorScheme.primaryContainer else MaterialTheme.colorScheme.surface,
                        )
                        .clickable { onSelect(pc.key) }
                        .padding(horizontal = 12.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    RadioButton(selected = isSelected, onClick = null)
                    Spacer(Modifier.width(8.dp))
                    Text(
                        text = pc.name,
                        style = MaterialTheme.typography.titleMedium,
                        color = MaterialTheme.colorScheme.onSurface,
                    )
                }
            }
        }
    }
}

/** Six digit boxes (3 + 3) backed by a single text field: typing advances, backspace goes back. */
@Composable
private fun CodeBoxes(
    code: String,
    isError: Boolean,
    onCodeChange: (String) -> Unit,
    onDone: () -> Unit,
) {
    val focusRequester = remember { FocusRequester() }
    LaunchedEffect(Unit) { focusRequester.requestFocus() }
    val description = stringResource(R.string.code_title)

    BasicTextField(
        value = code,
        onValueChange = { onCodeChange(PairingCode.sanitize(it)) },
        modifier = Modifier
            .fillMaxWidth()
            .focusRequester(focusRequester)
            .semantics { contentDescription = description },
        singleLine = true,
        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.NumberPassword, imeAction = ImeAction.Done),
        keyboardActions = KeyboardActions(onDone = { onDone() }),
        decorationBox = { _ ->
            Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                for (i in 0 until PairingCode.LENGTH) {
                    if (i == PairingCode.LENGTH / 2) Spacer(Modifier.width(12.dp))
                    CodeCell(
                        digit = code.getOrNull(i),
                        active = i == code.length,
                        isError = isError,
                        modifier = Modifier.weight(1f),
                    )
                }
            }
        },
    )
}

@Composable
private fun CodeCell(digit: Char?, active: Boolean, isError: Boolean, modifier: Modifier = Modifier) {
    val lineColor: Color = when {
        isError -> MaterialTheme.colorScheme.error
        active -> MaterialTheme.colorScheme.primary
        digit != null -> MaterialTheme.colorScheme.onBackground
        else -> MaterialTheme.colorScheme.outlineVariant
    }
    Box(
        modifier = modifier
            .height(64.dp)
            .clip(RoundedCornerShape(12.dp))
            .background(MaterialTheme.colorScheme.surface)
            .drawBehind {
                val line = 3.dp.toPx()
                drawRect(
                    color = lineColor,
                    topLeft = Offset(0f, size.height - line),
                    size = Size(size.width, line),
                )
            },
        contentAlignment = Alignment.Center,
    ) {
        Text(
            text = digit?.toString() ?: "",
            fontSize = 28.sp,
            fontWeight = FontWeight.Bold,
            style = MaterialTheme.typography.headlineMedium,
            color = MaterialTheme.colorScheme.onSurface,
        )
    }
}
