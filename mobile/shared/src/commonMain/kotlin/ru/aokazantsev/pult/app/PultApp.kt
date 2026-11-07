package ru.aokazantsev.pult.app

import androidx.compose.foundation.background
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveableStateHolder
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.toArgb
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LifecycleEventEffect
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.navigation.NavHostController
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.rememberNavController
import androidx.navigation.toRoute
import ru.aokazantsev.pult.access.AccessState
import ru.aokazantsev.pult.designsystem.theme.PultTheme
import ru.aokazantsev.pult.di.AppGraph
import ru.aokazantsev.pult.navigation.Route
import ru.aokazantsev.pult.render.LinkTarget
import ru.aokazantsev.pult.ui.access.LockScreen
import ru.aokazantsev.pult.ui.access.NoSecureLockScreen
import ru.aokazantsev.pult.ui.main.MainScreen
import ru.aokazantsev.pult.ui.pair.PairScreen
import ru.aokazantsev.pult.ui.render.LinkActions
import ru.aokazantsev.pult.ui.render.LinkDialog
import ru.aokazantsev.pult.ui.render.ProvideLinkHandler
import ru.aokazantsev.pult.ui.settings.SettingsScreen
import ru.aokazantsev.pult.ui.thread.ThreadScreen

@Composable
fun PultApp(
    graph: AppGraph,
    qrScanner: @Composable (modifier: Modifier, onScanned: (String) -> Unit) -> Unit,
    linkActions: LinkActions,
    onThemeResolved: (darkTheme: Boolean) -> Unit,
    onBackgroundResolved: (argb: Int) -> Unit,
) {
    val darkTheme = isSystemInDarkTheme()
    LaunchedEffect(darkTheme) { onThemeResolved(darkTheme) }
    val access = graph.appAccess
    LifecycleEventEffect(Lifecycle.Event.ON_START) { access.onForeground() }
    LifecycleEventEffect(Lifecycle.Event.ON_RESUME) { access.onResumed() }
    LifecycleEventEffect(Lifecycle.Event.ON_STOP) { access.onBackground() }
    PultTheme(darkTheme = darkTheme, wallpaperColors = PultTheme.wallpaperColorsAvailable) {
        val background = PultTheme.colors.surfaceContainer
        LaunchedEffect(background) { onBackgroundResolved(background.toArgb()) }
        Box(modifier = Modifier.fillMaxSize().background(background)) {
            val accessState by access.state.collectAsState()
            val navController = rememberNavController()
            val unlockedState = rememberSaveableStateHolder()
            when (accessState) {
                AccessState.Locked -> LockScreen(onUnlock = access::requestAuthentication)
                AccessState.NoSecureLock -> NoSecureLockScreen()
                AccessState.Unlocked -> unlockedState.SaveableStateProvider("unlocked") {
                    UnlockedContent(graph, navController, qrScanner, linkActions)
                }
            }
        }
    }
}

@Composable
private fun UnlockedContent(
    graph: AppGraph,
    navController: NavHostController,
    qrScanner: @Composable (modifier: Modifier, onScanned: (String) -> Unit) -> Unit,
    linkActions: LinkActions,
) {
    var pendingLink by remember { mutableStateOf<LinkTarget?>(null) }
    ProvideLinkHandler(onLink = { pendingLink = it }) {
        AppNavHost(graph, navController, qrScanner)
    }
    pendingLink?.let { link ->
        LinkDialog(link = link, actions = linkActions, onDismiss = { pendingLink = null })
    }
}

@Composable
private fun AppNavHost(
    graph: AppGraph,
    navController: NavHostController,
    qrScanner: @Composable (modifier: Modifier, onScanned: (String) -> Unit) -> Unit,
) {
    NavHost(navController = navController, startDestination = Route.Main) {
        composable<Route.Main> {
            MainScreen(
                viewModel = viewModel { graph.mainViewModel() },
                onOpenSettings = { navController.navigate(Route.Settings) },
                onStartPairing = { navController.navigate(Route.Scan) },
                onOpenThread = { navController.navigate(Route.Thread(it)) },
            )
        }
        composable<Route.Thread> { entry ->
            ThreadScreen(
                threadId = entry.toRoute<Route.Thread>().id,
                viewModel = viewModel { graph.threadViewModel() },
                onBack = { navController.popBackStack() },
            )
        }
        composable<Route.Scan> {
            PairScreen(
                viewModel = viewModel { graph.pairViewModel() },
                scanner = qrScanner,
                onBack = { navController.popBackStack() },
                onPaired = { navController.popBackStack(Route.Main, inclusive = false) },
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
