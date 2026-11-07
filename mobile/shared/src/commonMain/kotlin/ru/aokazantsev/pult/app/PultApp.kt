package ru.aokazantsev.pult.app

import androidx.compose.foundation.background
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.toArgb
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.rememberNavController
import ru.aokazantsev.pult.designsystem.theme.PultTheme
import ru.aokazantsev.pult.di.AppGraph
import ru.aokazantsev.pult.navigation.Route
import ru.aokazantsev.pult.ui.main.MainScreen
import ru.aokazantsev.pult.ui.pair.PairScreen
import ru.aokazantsev.pult.ui.settings.SettingsScreen

@Composable
fun PultApp(
    graph: AppGraph,
    qrScanner: @Composable (modifier: Modifier, onScanned: (String) -> Unit) -> Unit,
    onThemeResolved: (darkTheme: Boolean) -> Unit,
    onBackgroundResolved: (argb: Int) -> Unit,
) {
    val darkTheme = isSystemInDarkTheme()
    LaunchedEffect(darkTheme) { onThemeResolved(darkTheme) }
    PultTheme(darkTheme = darkTheme, wallpaperColors = PultTheme.wallpaperColorsAvailable) {
        val background = PultTheme.colors.surfaceContainer
        LaunchedEffect(background) { onBackgroundResolved(background.toArgb()) }
        Box(modifier = Modifier.fillMaxSize().background(background)) {
            val navController = rememberNavController()
            NavHost(navController = navController, startDestination = Route.Main) {
                composable<Route.Main> {
                    MainScreen(
                        viewModel = viewModel { graph.mainViewModel() },
                        onOpenSettings = { navController.navigate(Route.Settings) },
                        onStartPairing = { navController.navigate(Route.Scan) },
                    )
                }
                composable<Route.Scan> {
                    PairScreen(
                        viewModel = viewModel { graph.pairViewModel() },
                        scanner = qrScanner,
                        onBack = { navController.popBackStack() },
                        onPaired = { navController.popBackStack() },
                    )
                }
                composable<Route.Settings> {
                    SettingsScreen(
                        viewModel = viewModel { graph.settingsViewModel() },
                        onDone = { navController.popBackStack() },
                    )
                }
            }
        }
    }
}
