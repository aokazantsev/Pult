package ru.aokazantsev.pult.session

class StreamContext(val id: String) {
    private var counter = 0L

    fun nextCounter(): Long = ++counter
}
