package io.github.lgm42.monalbumpc.ui.navigation

import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Folder
import androidx.compose.material.icons.outlined.Image
import androidx.compose.material.icons.outlined.Settings
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.NavigationBarItemDefaults
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LifecycleEventEffect
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.navigation.NavDestination.Companion.hierarchy
import androidx.navigation.NavGraph.Companion.findStartDestination
import androidx.navigation.NavHostController
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.currentBackStackEntryAsState
import androidx.navigation.compose.rememberNavController
import io.github.lgm42.monalbumpc.data.PairedPc
import io.github.lgm42.monalbumpc.ui.MainViewModel
import io.github.lgm42.monalbumpc.ui.folders.FoldersScreen
import io.github.lgm42.monalbumpc.ui.photos.PhotosScreen
import io.github.lgm42.monalbumpc.ui.settings.SettingsScreen
import io.github.lgm42.monalbumpc.ui.theme.LocalExtendedColors

@Composable
fun MonAlbumPcApp(pc: PairedPc, onUnpair: () -> Unit) {
    val navController = rememberNavController()
    // Activity-scoped: shared by the three tabs.
    val mainViewModel: MainViewModel = viewModel()
    val state by mainViewModel.state.collectAsStateWithLifecycle()

    LifecycleEventEffect(Lifecycle.Event.ON_RESUME) { mainViewModel.refresh() }
    LaunchedEffect(pc.deviceId) { mainViewModel.ensureScheduled() }

    Scaffold(
        bottomBar = { BottomBar(navController) },
    ) { innerPadding ->
        NavHost(
            navController = navController,
            startDestination = TopLevelDestination.Photos.route,
            modifier = Modifier.padding(innerPadding),
        ) {
            composable(TopLevelDestination.Photos.route) {
                PhotosScreen(
                    albumName = pc.albumName,
                    pcName = pc.pcName,
                    state = state,
                    onFilterChange = mainViewModel::setFilter,
                    onBackupNow = mainViewModel::backupNow,
                    onRetry = mainViewModel::retry,
                    onTrackFolder = { key -> mainViewModel.setFolderTracked(key, true) },
                    onOpenFolders = { navController.navigateToTab(TopLevelDestination.Folders) },
                )
            }
            composable(TopLevelDestination.Folders.route) {
                FoldersScreen(
                    folders = state.folders,
                    tracked = state.tracked,
                    onTrackedChange = mainViewModel::setFolderTracked,
                )
            }
            composable(TopLevelDestination.Settings.route) {
                SettingsScreen(
                    pc = pc,
                    onUnpair = onUnpair,
                    chargingOnly = state.backup.chargingOnly,
                    onChargingOnlyChange = mainViewModel::setChargingOnly,
                    lastBackupMs = state.backup.lastBackupMs,
                )
            }
        }
    }
}

@Composable
private fun BottomBar(navController: NavHostController) {
    val backStackEntry by navController.currentBackStackEntryAsState()
    val currentDestination = backStackEntry?.destination
    val primaryText = LocalExtendedColors.current.primaryText

    NavigationBar(
        modifier = Modifier.clip(RoundedCornerShape(topStart = 28.dp, topEnd = 28.dp)),
        containerColor = MaterialTheme.colorScheme.surface,
        tonalElevation = 0.dp,
    ) {
        TopLevelDestination.entries.forEach { destination ->
            val selected = currentDestination?.hierarchy?.any { it.route == destination.route } == true
            NavigationBarItem(
                selected = selected,
                onClick = { navController.navigateToTab(destination) },
                icon = { Icon(destination.icon, contentDescription = null) },
                label = {
                    Text(
                        text = stringResource(destination.labelRes),
                        style = MaterialTheme.typography.labelMedium,
                        fontWeight = if (selected) FontWeight.Bold else FontWeight.Normal,
                    )
                },
                colors = NavigationBarItemDefaults.colors(
                    selectedIconColor = MaterialTheme.colorScheme.onPrimaryContainer,
                    selectedTextColor = primaryText,
                    indicatorColor = MaterialTheme.colorScheme.primaryContainer,
                    unselectedIconColor = MaterialTheme.colorScheme.onSurface,
                    unselectedTextColor = MaterialTheme.colorScheme.onSurface,
                ),
            )
        }
    }
}

private fun NavHostController.navigateToTab(destination: TopLevelDestination) {
    navigate(destination.route) {
        popUpTo(graph.findStartDestination().id) { saveState = true }
        launchSingleTop = true
        restoreState = true
    }
}

private val TopLevelDestination.icon: ImageVector
    get() = when (this) {
        TopLevelDestination.Photos -> Icons.Outlined.Image
        TopLevelDestination.Folders -> Icons.Outlined.Folder
        TopLevelDestination.Settings -> Icons.Outlined.Settings
    }
