package ru.aokazantsev.pult.relay

import kotlinx.coroutines.flow.StateFlow

interface SettingsStore {
    val settings: StateFlow<ConnectionSettings?>

    fun save(settings: ConnectionSettings)

    fun clear()
}
