package ru.aokazantsev.pult.di

import dev.zacsweers.metro.createGraphFactory

fun createAndroidAppGraph(appInfo: AppInfo): AppGraph =
    createGraphFactory<AppGraph.Factory>().create(appInfo = appInfo)
