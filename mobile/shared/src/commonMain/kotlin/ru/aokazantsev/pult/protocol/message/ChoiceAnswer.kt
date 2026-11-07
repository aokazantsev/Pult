package ru.aokazantsev.pult.protocol.message

data class ChoiceAnswer(val question: String, val selected: List<String>, val other: String? = null)
