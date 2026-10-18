package io.github.lgm42.monalbumpc.ui.settings

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.tooling.preview.Preview
import io.github.lgm42.monalbumpc.R
import io.github.lgm42.monalbumpc.ui.components.PlaceholderText
import io.github.lgm42.monalbumpc.ui.components.ScreenHeader
import io.github.lgm42.monalbumpc.ui.theme.MonAlbumPcTheme

@Composable
fun SettingsScreen(modifier: Modifier = Modifier) {
    Column(modifier = modifier.fillMaxSize()) {
        ScreenHeader(title = stringResource(R.string.settings_title))
        PlaceholderText(stringResource(R.string.settings_placeholder))
    }
}

@Preview(showBackground = true)
@Composable
private fun SettingsScreenPreview() {
    MonAlbumPcTheme { SettingsScreen() }
}
