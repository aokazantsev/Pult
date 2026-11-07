package ru.aokazantsev.pult.pairing

data class PairInfo(
    val server: String,
    val fingerprint: String,
    val confirmed: Boolean,
    val helloDeadlineEpochMs: Long,
    val softwareKeys: Boolean,
    val idleSeconds: Int,
)
