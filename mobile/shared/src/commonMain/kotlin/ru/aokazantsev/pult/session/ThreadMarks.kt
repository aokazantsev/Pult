package ru.aokazantsev.pult.session

data class ThreadMarks(
    val awaitsAction: Boolean = false,
    val awaitsText: Boolean = false,
    val childAwaitsAction: Boolean = false,
    val childAwaitsText: Boolean = false,
)
