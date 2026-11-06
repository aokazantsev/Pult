package ru.aokazantsev.pult.di

import dev.zacsweers.metro.AppScope
import dev.zacsweers.metro.DependencyGraph
import dev.zacsweers.metro.Provides
import io.ktor.client.HttpClient
import ru.aokazantsev.pult.relay.SettingsStore
import ru.aokazantsev.pult.ui.main.MainViewModel
import ru.aokazantsev.pult.ui.settings.SettingsViewModel

@DependencyGraph(AppScope::class)
interface AppGraph {
    val appInfo: AppInfo
    val mainViewModel: () -> MainViewModel
    val settingsViewModel: () -> SettingsViewModel

    @DependencyGraph.Factory
    fun interface Factory {
        fun create(
            @Provides appInfo: AppInfo,
            @Provides httpClient: HttpClient,
            @Provides settingsStore: SettingsStore,
        ): AppGraph
    }
}
