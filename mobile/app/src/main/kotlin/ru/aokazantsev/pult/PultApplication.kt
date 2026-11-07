package ru.aokazantsev.pult

import android.app.Application
import ru.aokazantsev.pult.di.AppGraph
import ru.aokazantsev.pult.di.AppGraphHolder
import ru.aokazantsev.pult.di.AppInfo
import ru.aokazantsev.pult.di.createAndroidAppGraph

class PultApplication : Application(), AppGraphHolder {

    override val graph: AppGraph by lazy {
        createAndroidAppGraph(this, AppInfo(versionName = BuildConfig.VERSION_NAME, isDebug = BuildConfig.DEBUG))
    }

    override fun onCreate() {
        super.onCreate()
        graph.deviceKeyStore.deleteAllExcept(graph.pairStore.pair.value?.pair)
    }
}
