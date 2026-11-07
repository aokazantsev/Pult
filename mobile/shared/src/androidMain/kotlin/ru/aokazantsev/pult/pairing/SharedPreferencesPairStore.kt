package ru.aokazantsev.pult.pairing

import android.content.Context
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow

class SharedPreferencesPairStore(context: Context) : PairStore {

    private val preferences = context.getSharedPreferences(FILE_NAME, Context.MODE_PRIVATE)

    private val mutablePair = MutableStateFlow(read())
    override val pair: StateFlow<StoredPair?> = mutablePair

    override fun save(pair: StoredPair) {
        preferences.edit()
            .putString(KEY_SERVER, pair.server)
            .putString(KEY_PAIR, pair.pair)
            .putString(KEY_PC_SIGN, pair.pcSign)
            .putString(KEY_PC_AGREE, pair.pcAgree)
            .putString(KEY_PHONE_SIGN, pair.phoneSign)
            .putString(KEY_PHONE_AGREE, pair.phoneAgree)
            .putInt(KEY_IDLE, pair.idleSeconds)
            .putBoolean(KEY_CONFIRMED, pair.confirmed)
            .putLong(KEY_DEADLINE, pair.helloDeadlineEpochMs)
            .putBoolean(KEY_SOFTWARE, pair.softwareKeys)
            .commit()
        mutablePair.value = pair
    }

    override fun clear() {
        preferences.edit().clear().commit()
        mutablePair.value = null
    }

    private fun read(): StoredPair? {
        val server = preferences.getString(KEY_SERVER, null) ?: return null
        val pair = preferences.getString(KEY_PAIR, null) ?: return null
        return StoredPair(
            server = server,
            pair = pair,
            pcSign = preferences.getString(KEY_PC_SIGN, null) ?: return null,
            pcAgree = preferences.getString(KEY_PC_AGREE, null) ?: return null,
            phoneSign = preferences.getString(KEY_PHONE_SIGN, null) ?: return null,
            phoneAgree = preferences.getString(KEY_PHONE_AGREE, null) ?: return null,
            idleSeconds = preferences.getInt(KEY_IDLE, 0),
            confirmed = preferences.getBoolean(KEY_CONFIRMED, false),
            helloDeadlineEpochMs = preferences.getLong(KEY_DEADLINE, 0L),
            softwareKeys = preferences.getBoolean(KEY_SOFTWARE, false),
        )
    }

    private companion object {
        const val FILE_NAME = "pult_pair_v2"
        const val KEY_SERVER = "server"
        const val KEY_PAIR = "pair"
        const val KEY_PC_SIGN = "pc_sign"
        const val KEY_PC_AGREE = "pc_agree"
        const val KEY_PHONE_SIGN = "phone_sign"
        const val KEY_PHONE_AGREE = "phone_agree"
        const val KEY_IDLE = "idle"
        const val KEY_CONFIRMED = "confirmed"
        const val KEY_DEADLINE = "deadline"
        const val KEY_SOFTWARE = "software"
    }
}
