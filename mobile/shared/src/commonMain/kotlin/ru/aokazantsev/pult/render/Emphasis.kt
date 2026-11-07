package ru.aokazantsev.pult.render

internal object Emphasis {

    fun parse(text: String): List<RichSpan> {
        val spans = ArrayList<RichSpan>()
        val buffer = StringBuilder()
        fun flush() {
            if (buffer.isNotEmpty()) {
                spans.add(RichSpan(buffer.toString()))
                buffer.clear()
            }
        }
        var index = 0
        while (index < text.length) {
            val bold = boldEnd(text, index)
            if (bold != null) {
                flush()
                spans.add(RichSpan(text.substring(index + 2, bold), bold = true))
                index = bold + 2
                continue
            }
            val italic = italicEnd(text, index)
            if (italic != null) {
                flush()
                spans.add(RichSpan(text.substring(index + 1, italic), italic = true))
                index = italic + 1
                continue
            }
            buffer.append(text[index])
            index++
        }
        flush()
        return spans
    }

    private fun boldEnd(text: String, start: Int): Int? {
        if (!text.startsWith("**", start)) return null
        val end = text.indexOf("**", start + 2)
        if (end <= start + 2) return null
        val inner = text.substring(start + 2, end)
        return if (inner.contains('\n') || inner.first().isWhitespace() || inner.last().isWhitespace()) null else end
    }

    private fun italicEnd(text: String, start: Int): Int? {
        val marker = text[start]
        if (marker != '*' && marker != '_') return null
        if (marker == '_' && start > 0 && text[start - 1].isLetterOrDigit()) return null
        val end = text.indexOf(marker, start + 1)
        if (end <= start + 1) return null
        val inner = text.substring(start + 1, end)
        if (inner.contains('\n') || inner.first().isWhitespace() || inner.last().isWhitespace()) return null
        if (marker == '_' && end + 1 < text.length && text[end + 1].isLetterOrDigit()) return null
        return end
    }
}
