package io.github.lgm42.monalbumpc.ui.navigation

import androidx.annotation.StringRes
import io.github.lgm42.monalbumpc.R

/** The tabs of the bottom navigation bar, in display order; the first one opens with the app. */
enum class TopLevelDestination(val route: String, @StringRes val labelRes: Int) {
    Backup("backup", R.string.nav_backup),
    Photos("photos", R.string.nav_photos),
    Folders("folders", R.string.nav_folders),
    Settings("settings", R.string.nav_settings),
}
