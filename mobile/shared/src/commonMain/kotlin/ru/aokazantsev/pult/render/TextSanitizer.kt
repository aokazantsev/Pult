package ru.aokazantsev.pult.render

object TextSanitizer {

    private const val REPLACEMENT = '�'

    fun sanitize(value: String): String {
        val normalized = value.replace("\r\n", "\n").replace('\r', '\n')
        val out = StringBuilder(normalized.length)
        for (char in normalized) {
            when {
                isDirectionControl(char) -> Unit
                isForbiddenControl(char) -> out.append(REPLACEMENT)
                else -> out.append(char)
            }
        }
        return out.toString()
    }

    fun oneLine(value: String): String = sanitize(value).replace('\n', ' ')

    private fun isDirectionControl(char: Char): Boolean =
        char in '‪'..'‮' || char in '⁦'..'⁩' || char == '‎' || char == '‏'

    private fun isForbiddenControl(char: Char): Boolean =
        (char < ' ' && char != '\n' && char != '\t') || char in '\u007F'..'\u009F'
}
