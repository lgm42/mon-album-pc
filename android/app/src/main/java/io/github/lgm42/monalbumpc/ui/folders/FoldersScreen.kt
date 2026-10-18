package io.github.lgm42.monalbumpc.ui.folders

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
fun FoldersScreen(modifier: Modifier = Modifier) {
    Column(modifier = modifier.fillMaxSize()) {
        ScreenHeader(title = stringResource(R.string.folders_title))
        PlaceholderText(stringResource(R.string.folders_placeholder))
    }
}

@Preview(showBackground = true)
@Composable
private fun FoldersScreenPreview() {
    MonAlbumPcTheme { FoldersScreen() }
}
