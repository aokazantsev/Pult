package ru.aokazantsev.pult.ui.settings

import androidx.lifecycle.ViewModel
import dev.zacsweers.metro.Inject
import ru.aokazantsev.pult.relay.ConnectionSettings
import ru.aokazantsev.pult.relay.SettingsStore

@Inject
class SettingsViewModel(private val store: SettingsStore) : ViewModel() {

    val current: ConnectionSettings = store.settings.value ?: ConnectionSettings("", "", "")

    fun save(serverUrl: String, pairId: String, token: String) {
        store.save(ConnectionSettings(serverUrl = serverUrl.trim(), pairId = pairId.trim(), token = token.trim()))
    }
}
