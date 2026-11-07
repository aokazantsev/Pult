package ru.aokazantsev.pult.render

data class LinkTarget(val url: String) {

    val openable: Boolean
        get() = url.startsWith("http://") || url.startsWith("https://")
}
