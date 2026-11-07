package ru.aokazantsev.pult.protocol.message

data class ChoiceQuestion(
    val id: String,
    val header: String?,
    val question: String,
    val multi: Boolean,
    val other: Boolean,
    val options: List<ChoiceOption>,
)
