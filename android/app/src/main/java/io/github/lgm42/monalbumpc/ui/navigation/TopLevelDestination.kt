package io.github.lgm42.monalbumpc.ui.navigation

import androidx.annotation.StringRes
import io.github.lgm42.monalbumpc.R

/** The three tabs of the bottom navigation bar, in display order. */
enum class TopLevelDestination(val route: String, @StringRes val labelRes: Int) {
    Photos("photos", R.string.nav_photos),
    Folders("folders", R.string.nav_folders),
    Settings("settings", R.string.nav_settings),
}
