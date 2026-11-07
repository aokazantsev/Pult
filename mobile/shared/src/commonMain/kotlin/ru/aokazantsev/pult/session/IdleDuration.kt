package ru.aokazantsev.pult.session

object IdleDuration {

    private const val MINUTE = 60
    private const val HOUR = 3600

    fun format(seconds: Int): String {
        val hours = seconds / HOUR
        val minutes = (seconds % HOUR) / MINUTE
        return when {
            hours == 0 -> "${maxOf(minutes, 1)} мин"
            minutes == 0 -> "$hours ч"
            else -> "$hours ч $minutes мин"
        }
    }
}
