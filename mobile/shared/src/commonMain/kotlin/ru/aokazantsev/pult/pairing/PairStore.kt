package ru.aokazantsev.pult.pairing

import kotlinx.coroutines.flow.StateFlow

interface PairStore {
    val pair: StateFlow<StoredPair?>

    fun save(pair: StoredPair)

    fun clear()
}
