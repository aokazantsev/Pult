package ru.aokazantsev.pult.relay

import android.content.Context
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow

class SharedPreferencesSettingsStore(context: Context) : SettingsStore {

    private val preferences = context.getSharedPreferences(FILE_NAME, Context.MODE_PRIVATE)

    private val mutableSettings = MutableStateFlow(read())
    override val settings: StateFlow<ConnectionSettings?> = mutableSettings

    override fun save(settings: ConnectionSettings) {
        preferences.edit()
            .putString(KEY_SERVER, settings.serverUrl)
            .putString(KEY_PAIR, settings.pairId)
            .putString(KEY_TOKEN, settings.token)
            .apply()
        mutableSettings.value = settings
    }

    override fun clear() {
        preferences.edit().clear().apply()
        mutableSettings.value = null
    }

    private fun read(): ConnectionSettings? {
        val server = preferences.getString(KEY_SERVER, null) ?: return null
        val pair = preferences.getString(KEY_PAIR, null) ?: return null
        val token = preferences.getString(KEY_TOKEN, null) ?: return null
        return ConnectionSettings(serverUrl = server, pairId = pair, token = token)
    }

    private companion object {
        const val FILE_NAME = "pult_connection"
        const val KEY_SERVER = "server_url"
        const val KEY_PAIR = "pair_id"
        const val KEY_TOKEN = "token"
    }
}
