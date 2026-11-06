package ru.aokazantsev.pult.di

import dev.zacsweers.metro.AppScope
import dev.zacsweers.metro.DependencyGraph
import dev.zacsweers.metro.Provides
import ru.aokazantsev.pult.ui.main.MainViewModel

@DependencyGraph(AppScope::class)
interface AppGraph {
    val appInfo: AppInfo
    val mainViewModel: () -> MainViewModel

    @DependencyGraph.Factory
    fun interface Factory {
        fun create(@Provides appInfo: AppInfo): AppGraph
    }
}
