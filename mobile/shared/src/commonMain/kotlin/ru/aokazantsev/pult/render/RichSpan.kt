package ru.aokazantsev.pult.render

data class RichSpan(
    val text: String,
    val bold: Boolean = false,
    val italic: Boolean = false,
    val code: Boolean = false,
    val link: LinkTarget? = null,
)
