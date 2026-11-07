package ru.aokazantsev.pult.pairing

import ru.aokazantsev.pult.protocol.Wire

data class StoredPair(
    val server: String,
    val pair: String,
    val pcSign: String,
    val pcAgree: String,
    val phoneSign: String,
    val phoneAgree: String,
    val idleSeconds: Int,
    val confirmed: Boolean,
    val helloDeadlineEpochMs: Long,
    val softwareKeys: Boolean,
) {
    val transcript: String
        get() = Wire.transcript(pair, pcSign, pcAgree, phoneSign, phoneAgree)
}
