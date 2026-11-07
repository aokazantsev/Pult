package ru.aokazantsev.pult.session

class HelloRequestPolicy(
    private val minIntervalMs: Long = MIN_INTERVAL_MS,
    private val maxPerStream: Int = MAX_PER_STREAM,
) {

    private var issued = 0
    private var lastAtMs: Long? = null

    val exhausted: Boolean
        get() = issued >= maxPerStream

    fun waitMs(nowMs: Long): Long = lastAtMs?.let { maxOf(0L, it + minIntervalMs - nowMs) } ?: 0L

    fun reset() {
        issued = 0
        lastAtMs = null
    }

    fun tryRequest(nowMs: Long): Boolean {
        if (issued >= maxPerStream) return false
        val last = lastAtMs
        if (last != null && nowMs - last < minIntervalMs) return false
        issued++
        lastAtMs = nowMs
        return true
    }

    companion object {
        const val MIN_INTERVAL_MS = 10_000L
        const val MAX_PER_STREAM = 3
    }
}
