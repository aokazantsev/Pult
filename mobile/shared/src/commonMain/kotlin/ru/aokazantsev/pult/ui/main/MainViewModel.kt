package ru.aokazantsev.pult.ui.main

import androidx.lifecycle.ViewModel
import dev.zacsweers.metro.Inject
import ru.aokazantsev.pult.di.AppInfo

@Inject
class MainViewModel(appInfo: AppInfo) : ViewModel() {

    val versionText: String = "Версия ${appInfo.versionName}"
}
