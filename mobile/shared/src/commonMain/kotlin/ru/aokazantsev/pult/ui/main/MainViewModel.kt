package ru.aokazantsev.pult.ui.main

import androidx.lifecycle.ViewModel
import dev.zacsweers.metro.Inject
import kotlinx.coroutines.flow.StateFlow
import ru.aokazantsev.pult.di.AppInfo
import ru.aokazantsev.pult.session.RelaySession
import ru.aokazantsev.pult.session.SessionState

@Inject
class MainViewModel(
    appInfo: AppInfo,
    private val session: RelaySession,
) : ViewModel() {

    val versionText: String = "Версия ${appInfo.versionName}"

    val state: StateFlow<SessionState> = session.state

    fun cancelPairing() = session.unpair()
}
